using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>Seil/Schnur als gestreckter Zylinder (Weltkoordinaten).</summary>
public class RopeVis
{
    readonly MeshInstance3D mi;
    readonly float r;
    static Mesh unit;
    public RopeVis(Node3D parent, float r = 0.022f, string mat = "rope")
    {
        if (unit == null) { var k = new MeshKit(); k.Cyl("rope", new Vector3(0, 0.5f, 0), 1, 1, 1, 6, Quaternion.Identity, false); unit = k.BuildMesh(); }
        mi = new MeshInstance3D { Mesh = unit, MaterialOverride = Mats.Get(mat), TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        parent.AddChild(mi);
        this.r = r;
    }
    public void Set(Vector3 a, Vector3 b)
    {
        var d = b - a; float L = d.Length();
        if (L < 1e-4f) return;
        var y = d / L;
        var x = MathF.Abs(y.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up.Cross(y).Normalized();
        var z = x.Cross(y);
        mi.GlobalTransform = new Transform3D(new Basis(x * r, y * L, z * r), a);
    }
    public void Show(bool v) => mi.Visible = v;
}

/// <summary>Fertigteil als Physikkörper; im Lager fixiert, am Kran frei pendelnd.</summary>
public partial class Element : RigidBody3D
{
    public ElementDef def;
    public ConstructionSite site;
    public int index;
    public string state = "stored";
    public Vector3 liftLocal;
    public TowerCrane crane;
    public float MassT => def.massT;

    public override void _IntegrateForces(PhysicsDirectBodyState3D st)
    {
        if (crane != null && state == "hooked" && IsInstanceValid(crane)) Rope.Apply(st, liftLocal, crane.TrolleyWorld(), crane.L);
    }
    public Vector3 LiftPointWorld => GlobalTransform * liftLocal;

    public static Element Create(Node parent, ElementDef def, ConstructionSite site, int index, Vector3 pos, float yaw)
    {
        var e = new Element { def = def, site = site, index = index, Name = def.name };
        e.CollisionLayer = Layers.Element;
        e.CollisionMask = Layers.All & ~Layers.Hook;
        e.Mass = def.massT * 1000f;
        e.ContinuousCd = true;
        e.ContactMonitor = true;
        e.MaxContactsReported = 4;
        e.Freeze = true;
        e.FreezeMode = FreezeModeEnum.Kinematic;
        e.LinearDamp = 0.05f; e.AngularDamp = 0.6f;
        e.CanSleep = false;
        e.PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.8f };
        var k = new MeshKit();
        foreach (var b in def.boxes)
        {
            var T = PieceType.All[b.type];
            k.Box(T.mat, b.c, b.size, b.Rot, T.glass ? 0 : 0.02f);
            e.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = b.size }, Transform = new Transform3D(new Basis(b.Rot), b.c) });
        }
        k.Build(e, "Fertigteil");
        e.liftLocal = new Vector3(0, def.size.Y + 0.1f, 0);
        parent.AddChild(e);
        e.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
        e.BodyEntered += e.OnHit;
        Buildings.I?.elements.Add(e);
        return e;
    }

    public float HeightAboveGround()
    {
        var p = GlobalPosition + Vector3.Up * 0.2f;
        var q = PhysicsRayQueryParameters3D.Create(p, p + Vector3.Down * 60f, Layers.Ground & ~Layers.Element);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        return hit.Count > 0 ? MathF.Max(0, p.DistanceTo((Vector3)hit["position"]) - 0.2f) : 0;
    }

    public Vector3[] SlingPoints()
    {
        float hx = def.size.X * 0.35f, hz = def.size.Z * 0.35f, y = def.size.Y;
        var t = GlobalTransform;
        return new[] { t * new Vector3(hx, y, hz), t * new Vector3(-hx, y, hz), t * new Vector3(hx, y, -hz), t * new Vector3(-hx, y, -hz) };
    }

    void OnHit(Node body)
    {
        if (state == "placed" || Freeze) return;
        float v = LinearVelocity.Length();
        if (v > 3.6f) CallDeferred(nameof(Shatter), v);
        else if (v > 1.0f) SoundFX.I?.Play("thud", GlobalPosition, Math.Clamp(v / 3f, 0.2f, 0.9f), 0.15f);
    }

    public void Shatter(float impact)
    {
        if (state == "broken" || state == "placed") return;
        state = "broken";
        var t = GlobalTransform;
        foreach (var b in def.boxes)
            Buildings.I.SpawnDebris(t * b.c, t.Basis.GetRotationQuaternion() * b.Rot, b.size, b.type, LinearVelocity * 0.4f + U.RandSphere() * impact * 0.2f, b.size.X * b.size.Y * b.size.Z);
        SoundFX.I?.Play("crash", GlobalPosition, 1f);
        SoilFX.I?.Dust(GlobalPosition + Vector3.Up, 10, new Color(0.75f, 0.72f, 0.68f), 2f);
        Hud.Toast($"{def.name} ist zerbrochen!", ToastKind.Warn);
        var crane = GameServices.Crane;
        if (crane != null && crane.load == this) crane.DropLoad();
        Buildings.I?.elements.Remove(this);
        site?.OnElementLost(this);
        QueueFree();
    }

    public override void _ExitTree() { Buildings.I?.elements.Remove(this); }
}

public static class GameServices
{
    public static TowerCrane Crane;
    public static Rect2 Yard = new(-31, 3, 17, 13);
}

/// <summary>Baustelle: Planum → Schalung/Beton → Aushärten → Montage der Fertigteile per Kran.</summary>
public class ConstructionSite : ISoilCatcher
{
    public Design design;
    public Vector3 pos;
    public float yaw;
    public string stage = "planum";
    public float dev = 9f, baseY, poured, need, cure, cureTime = 20f;
    public List<ElementDef> defs;
    public HashSet<int> placed = new();
    public int tier, lostElements;
    public Building building;
    readonly Buildings sys;
    readonly Node3D root;
    MeshInstance3D formwork, pour, footGhost;
    readonly List<MeshInstance3D> ghosts = new();
    float checkT;
    bool overpourMsg;

    public ConstructionSite(Buildings sys, string key, Vector3 pos, float yaw)
    {
        this.sys = sys;
        design = Design.All[key];
        this.pos = pos; this.yaw = yaw;
        need = design.hw * 2 * design.hd * 2 * design.slabT;
        defs = design.build();
        root = new Node3D { Name = "Baustelle " + design.name };
        sys.AddChild(root);
        MakeStakes();
        MakeGhosts();
        SoilFX.Catchers.Add(this);
        sys.sites.Add(this);
    }

    Quaternion Rot => Quaternion.FromEuler(new Vector3(0, yaw, 0));
    Vector3 W(float lx, float lz) => pos + Rot * new Vector3(lx, 0, lz);
    Vector2 Local(Vector3 w) { var l = Rot.Inverse() * (w - pos); return new Vector2(l.X, l.Z); }

    void MakeStakes()
    {
        var T = SoilTerrain.I;
        float m = 0.6f, hw = design.hw, hd = design.hd;
        var corners = new[] { W(-hw - m, -hd - m), W(hw + m, -hd - m), W(hw + m, hd + m), W(-hw - m, hd + m) };
        var k = new MeshKit();
        var tops = new List<Vector3>();
        foreach (var c in corners)
        {
            float y = T.Height(c.X, c.Z);
            k.Box("formwork", new Vector3(c.X, y + 0.5f, c.Z), new Vector3(0.06f, 1.0f, 0.06f));
            k.Box("marker_red", new Vector3(c.X, y + 0.93f, c.Z), new Vector3(0.065f, 0.15f, 0.065f));
            tops.Add(new Vector3(c.X, y + 0.8f, c.Z));
        }
        k.Build(root, "Pflöcke");
        for (int i = 0; i < 4; i++) new RopeVis(root, 0.008f, "marker_red").Set(tops[i], tops[(i + 1) % 4]);
    }

    void MakeGhosts()
    {
        var fk = new MeshKit();
        fk.Box("ghost", Vector3.Zero, new Vector3(design.hw * 2, 0.2f, design.hd * 2));
        footGhost = fk.Build(root, "Umriss", false);
        foreach (var d in defs)
        {
            var k = new MeshKit();
            foreach (var b in d.boxes) k.Box("ghost", b.c, b.size, b.Rot);
            var g = k.Build(root, "Ziel " + d.name, false);
            g.Visible = false;
            ghosts.Add(g);
        }
        UpdateGhostPos();
    }

    void UpdateGhostPos()
    {
        float y = stage == "planum" ? SoilTerrain.I.StatsRect(pos, design.hw, design.hd, yaw).mean : baseY;
        footGhost.GlobalTransform = new Transform3D(new Basis(Rot), new Vector3(pos.X, y + 0.1f, pos.Z));
        float top = y + design.slabT;
        for (int i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            var wp = W(d.local.X, d.local.Z);
            d.target = new Vector3(wp.X, top + d.local.Y, wp.Z);
            d.targetYaw = yaw;
            d.hasTarget = true;
            ghosts[i].GlobalTransform = new Transform3D(new Basis(Rot), d.target);
        }
    }

    public bool TryCatch(Vector3 p, Vector3 v, float vol, SoilMat mat)
    {
        if (mat != SoilMat.Concrete || stage == "planum") return false;
        if (p.Y > baseY + 0.8f || p.Y < baseY - 0.4f) return false;
        var l = Local(p);
        if (MathF.Abs(l.X) > design.hw || MathF.Abs(l.Y) > design.hd) return false;
        if (stage != "beton")
        {
            if (!overpourMsg) { overpourMsg = true; Hud.Toast("Bodenplatte ist schon voll – Beton ablassen stoppen!", ToastKind.Warn); }
            return true;
        }
        poured += vol;
        return true;
    }

    public void Tick(float dt)
    {
        var T = SoilTerrain.I;
        if (stage == "planum")
        {
            checkT -= dt;
            if (checkT > 0) return;
            checkT = 0.5f;
            var st = T.StatsRect(pos, design.hw + 0.3f, design.hd + 0.3f, yaw);
            dev = MathF.Max(st.max - st.mean, st.mean - st.min);
            UpdateGhostPos();
            if (dev <= 0.15f)
            {
                baseY = st.mean;
                T.LevelRect(pos, design.hw + 0.3f, design.hd + 0.3f, yaw, baseY, 3);
                stage = "beton";
                MakeFormwork();
                UpdateGhostPos();
                footGhost.Visible = false;
                Hud.Toast("Planum fertig! Schalung steht – jetzt mit dem Fahrmischer betonieren.", ToastKind.Ok);
                SoundFX.I?.PlayUI("success");
            }
        }
        else if (stage == "beton")
        {
            float f = MathF.Min(1f, poured / need);
            pour.Scale = new Vector3(1, MathF.Max(0.01f, f), 1);
            pour.GlobalPosition = new Vector3(pos.X, baseY + design.slabT * f / 2, pos.Z);
            if (f >= 1f) { stage = "haerten"; cure = cureTime; Hud.Toast("Bodenplatte betoniert. Beton härtet aus (Zeitraffer) …", ToastKind.Ok); }
        }
        else if (stage == "haerten")
        {
            cure -= dt;
            if (cure <= 0)
            {
                pour.QueueFree(); formwork.QueueFree();
                var bp = new Blueprint();
                bp.Slab(-design.hw, -design.hd, design.hw, design.hd, 0, design.slabT, "concrete", 1.05f);
                building = sys.CreateFromBlueprint(bp, new Vector3(pos.X, baseY, pos.Z), yaw, design.name, "new");
                stage = "montage";
                Deliver(0);
            }
        }
        else if (stage == "montage")
        {
            var hooked = GameServices.Crane?.load;
            for (int i = 0; i < defs.Count; i++)
            {
                bool show = defs[i].tier == tier && !placed.Contains(i);
                ghosts[i].Visible = show;
                if (show) ghosts[i].MaterialOverride = Mats.Get(hooked != null && hooked.def == defs[i] && CanPlace(hooked) ? "ghost_ok" : "ghost");
            }
            int remaining = 0;
            for (int i = 0; i < defs.Count; i++) if (defs[i].tier == tier && !placed.Contains(i)) remaining++;
            if (remaining == 0)
            {
                int next = int.MaxValue;
                for (int i = 0; i < defs.Count; i++) if (!placed.Contains(i) && defs[i].tier > tier) next = Math.Min(next, defs[i].tier);
                if (next != int.MaxValue) { tier = next; Deliver(tier); }
                else
                {
                    stage = "fertig";
                    building.done = true;
                    sys.NotifyBuilt(building);
                    Hud.Toast($"{design.name} fertiggestellt!", ToastKind.Ok);
                    SoundFX.I?.PlayUI("success");
                    Remove();
                }
            }
        }
    }

    void MakeFormwork()
    {
        var k = new MeshKit();
        float t = 0.04f, h = design.slabT + 0.08f, hw = design.hw, hd = design.hd;
        var r = Rot;
        var o = new Vector3(pos.X, baseY, pos.Z);
        void Add(Vector3 lc, Vector3 size) => k.Box("formwork", o + r * lc, size, r, 0.005f);
        Add(new Vector3(0, h / 2, hd + t / 2), new Vector3(hw * 2 + 2 * t, h, t));
        Add(new Vector3(0, h / 2, -hd - t / 2), new Vector3(hw * 2 + 2 * t, h, t));
        Add(new Vector3(hw + t / 2, h / 2, 0), new Vector3(t, h, hd * 2));
        Add(new Vector3(-hw - t / 2, h / 2, 0), new Vector3(t, h, hd * 2));
        for (float x = -hw + 0.15f; x < hw; x += 0.3f) k.Box("dark_metal", o + r * new Vector3(x, 0.08f, 0), new Vector3(0.012f, 0.012f, hd * 2 - 0.1f), r);
        for (float z = -hd + 0.15f; z < hd; z += 0.3f) k.Box("dark_metal", o + r * new Vector3(0, 0.09f, z), new Vector3(hw * 2 - 0.1f, 0.012f, 0.012f), r);
        formwork = k.Build(root, "Schalung");
        var pk = new MeshKit();
        pk.Box("fresh_concrete", Vector3.Zero, new Vector3(hw * 2, design.slabT, hd * 2));
        pour = pk.Build(root, "Frischbeton");
        pour.GlobalTransform = new Transform3D(new Basis(r), o);
        pour.Scale = new Vector3(1, 0.01f, 1);
    }

    void Deliver(int t)
    {
        var yard = GameServices.Yard;
        float cx = yard.Position.X + 0.4f, cz = yard.Position.Y + 0.6f, rowH = 0;
        int n = 0;
        for (int i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            if (d.tier != t || placed.Contains(i)) continue;
            var ext = Rot * d.size;
            float sx = MathF.Abs(ext.X), sz = MathF.Abs(ext.Z);
            if (cx + sx > yard.End.X) { cx = yard.Position.X + 0.4f; cz += rowH + 1.0f; rowH = 0; }
            var p = new Vector3(cx + sx / 2, 0, cz + sz / 2);
            p.Y = SoilTerrain.I.Height(p.X, p.Z) + 0.02f;
            bool roof = d.boxes.Exists(b => MathF.Abs(b.pitch) > 0.01f);
            if (roof)
            {
                var tk = new MeshKit();
                tk.Box("timber", p + new Vector3(0, 0.15f, 0), new Vector3(sx * 0.9f, 0.3f, 0.3f), 0.02f);
                tk.Build(root, "Transportbock");
                p.Y += 0.3f;
            }
            Element.Create(sys, d, this, i, p, yaw);
            cx += sx + 1.0f;
            rowH = MathF.Max(rowH, sz);
            n++;
        }
        if (n > 0) Hud.Toast($"Lkw hat {n} Fertigteile auf den Lagerplatz geliefert. Mit dem Kran montieren!", ToastKind.Ok);
    }

    public bool CanPlace(Element el)
    {
        var d = el.def;
        if (!d.hasTarget || d.tier != tier || placed.Contains(el.index) || stage != "montage") return false;
        var p = el.GlobalPosition;
        float dx = p.X - d.target.X, dz = p.Z - d.target.Z;
        if (dx * dx + dz * dz > 0.7f * 0.7f) return false;
        float dy = p.Y - d.target.Y;
        if (dy < -0.4f || dy > 0.5f) return false;
        float elYaw = el.GlobalBasis.GetEuler().Y;
        if (MathF.Abs(Mathf.AngleDifference(elYaw, d.targetYaw)) > Mathf.DegToRad(12f)) return false;
        return true;
    }

    public void Place(Element el)
    {
        var d = el.def;
        var r = Quaternion.FromEuler(new Vector3(0, d.targetYaw, 0));
        var list = new List<Piece>();
        foreach (var b in d.boxes) list.Add(sys.AddPiece(building, b.type, d.target + r * b.c, b.size, r * b.Rot));
        sys.Finalize(building, list, baseY);
        placed.Add(el.index);
        el.state = "placed";
        sys.elements.Remove(el);
        el.QueueFree();
        SoundFX.I?.Play("thud", d.target, 0.7f);
        Hud.Toast($"{d.name} montiert ({placed.Count}/{defs.Count})", ToastKind.Ok);
    }

    public void OnElementLost(Element el)
    {
        lostElements++;
        if (stage != "montage") return;
        var yard = GameServices.Yard;
        var p = new Vector3(yard.GetCenter().X, 0, yard.End.Y - el.def.size.Z - 0.5f);
        p.Y = SoilTerrain.I.Height(p.X, p.Z) + 0.02f;
        Element.Create(sys, el.def, this, el.index, p, yaw);
        Hud.Toast($"Ersatz für „{el.def.name}“ wurde auf den Lagerplatz geliefert.", ToastKind.Warn);
    }

    public void Remove()
    {
        SoilFX.Catchers.Remove(this);
        sys.sites.Remove(this);
        root.QueueFree();
    }

    public (string title, string detail, float prog) Status() => stage switch
    {
        "planum" => ($"{design.name}: Baugrund planieren", $"Höhenabweichung ±{U.Fmt(dev, 2)} m (Ziel ≤ 0,15 m)", U.Clamp01(1f - (dev - 0.15f) / 1.2f)),
        "beton" => ($"{design.name}: Bodenplatte betonieren", $"{U.Fmt(poured)} / {U.Fmt(need)} m³ Beton", poured / need),
        "haerten" => ($"{design.name}: Beton härtet aus", $"noch {(int)MathF.Ceiling(cure)} s (Zeitraffer)", 1f - cure / MathF.Max(1f, cureTime)),
        "montage" => ($"{design.name}: Montage {placed.Count}/{defs.Count}", NextHint(), placed.Count / (float)defs.Count),
        _ => ($"{design.name}: fertig", "", 1f),
    };

    string NextHint()
    {
        for (int i = 0; i < defs.Count; i++) if (defs[i].tier == tier && !placed.Contains(i)) return $"z. B. {defs[i].name} ({U.Fmt(defs[i].massT)} t)";
        return "";
    }
}
