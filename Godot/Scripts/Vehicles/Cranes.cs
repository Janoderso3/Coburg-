using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>Undehnbares Seil als Impuls-Bedingung an einem Punkt eines Starrkörpers (im Integrationsschritt).</summary>
public static class Rope
{
    /// <returns>Seilkraft (N) entlang des Seils, für die Rückwirkung auf die Aufhängung.</returns>
    public static float Apply(PhysicsDirectBodyState3D st, Vector3 localPoint, Vector3 anchor, float L)
    {
        var tr = st.Transform;
        var p = tr * localPoint;
        var com = tr.Origin + st.CenterOfMass;
        var d = p - anchor;
        float dist = d.Length();
        if (dist <= L || dist < 1e-4f) return 0;
        var n = d / dist;
        var r = p - com;
        var vp = st.LinearVelocity + st.AngularVelocity.Cross(r);
        float vn = vp.Dot(n);
        float dt = st.Step;
        float bias = MathF.Min((dist - L) * 0.25f / dt, 0.6f);
        if (vn + bias <= 0) return 0;
        var rn = r.Cross(n);
        float k = st.InverseMass + (st.InverseInertiaTensor * rn).Cross(r).Dot(n);
        if (k <= 1e-9f) return 0;
        float lambda = -(vn + bias) / k;
        st.ApplyImpulse(n * lambda, p - tr.Origin);
        return -lambda / dt;
    }
}

public static class Lattice
{
    public static void Bar(MeshKit k, string mat, Vector3 a, Vector3 b, float t)
    {
        var d = b - a;
        if (d.LengthSquared() < 1e-6f) return;
        var up = MathF.Abs(d.Normalized().Dot(Vector3.Up)) > 0.95f ? Vector3.Back : Vector3.Up;
        k.Box(mat, (a + b) * 0.5f, new Vector3(t, t, d.Length()), MeshKit.LookRot(d.Normalized(), up));
    }
}

/// <summary>Kranhaken (Unterflasche) am Seil.</summary>
public partial class CraneHook : RigidBody3D
{
    public TowerCrane crane;
    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (crane == null) return;
        if (crane.load == null) Rope.Apply(state, new Vector3(0, 0.7f, 0), crane.TrolleyWorld(), crane.L);
    }
}

/// <summary>
/// Turmdrehkran 40 m: Haken pendelt am Seil, Last hängt an der Unterflasche (Pendel); Traglast 10 t bis 14 m, danach 140 t·m;
/// Lastmomentbegrenzung sperrt Katze aus und Heben.
/// </summary>
public partial class TowerCrane : VehicleBase
{
    public float H = 30f, jib = 40f, slew = -0.8f, slewV, r = 15f, rV, L = 12f, LV;
    Node3D slewT, trolleyVis;
    public CraneHook hook;
    public Element load;
    float releaseArm;
    RopeVis rope1, rope2;
    readonly RopeVis[] slings = new RopeVis[4];
    public int dropped;
    Vector3 basePos;

    public static TowerCrane Create(Node parent, Vector3 pos)
    {
        var c = new TowerCrane { Name = "Turmdrehkran" };
        c.basePos = pos;
        c.Build();
        parent.AddChild(c);
        c.GlobalPosition = pos;
        c.spawn = c.GlobalTransform;
        c.hook.GlobalPosition = c.TrolleyWorld() + Vector3.Down * c.L;
        c.ApplyTransforms();
        return c;
    }

    void Build()
    {
        displayName = "Turmdrehkran 40 m"; shortName = "Kran"; camDistance = 26f;
        Freeze = true; FreezeMode = FreezeModeEnum.Static;
        CollisionLayer = Layers.Building; CollisionMask = 0;
        var k = new MeshKit();
        k.Box("concrete", new Vector3(0, 0.4f, 0), new Vector3(5, 0.8f, 5), 0.05f);
        foreach (var (bx, bz) in new[] { (-1.6f, -1.6f), (1.6f, -1.6f), (-1.6f, 1.6f), (1.6f, 1.6f) }) k.Box("concrete", new Vector3(bx, 1.25f, bz), new Vector3(1.4f, 0.9f, 1.4f), 0.04f);
        float w = 0.85f, seg = 2.5f, top = H + 1;
        var c4 = new[] { new Vector2(-w, -w), new Vector2(w, -w), new Vector2(w, w), new Vector2(-w, w) };
        for (float y = 0.8f; y < top - 0.01f; y += seg)
        {
            float y1 = MathF.Min(top, y + seg);
            for (int i = 0; i < 4; i++)
            {
                var a = c4[i]; var b = c4[(i + 1) % 4];
                Lattice.Bar(k, "paint_yellow", new Vector3(a.X, y, a.Y), new Vector3(a.X, y1, a.Y), 0.16f);
                Lattice.Bar(k, "paint_yellow", new Vector3(a.X, y1, a.Y), new Vector3(b.X, y1, b.Y), 0.08f);
                Lattice.Bar(k, "paint_yellow", new Vector3(a.X, y, a.Y), new Vector3(b.X, y1, b.Y), 0.07f);
            }
        }
        for (float y = 1f; y < top; y += 0.3f) Lattice.Bar(k, "steel", new Vector3(-0.3f, y, -w - 0.05f), new Vector3(0.3f, y, -w - 0.05f), 0.03f);
        k.Build(this, "Turm");
        AddBox(new Vector3(0, 1.0f, 0), new Vector3(5, 2, 5));
        AddBox(new Vector3(0, top / 2, 0), new Vector3(1.8f, top, 1.8f));
        slewT = new Node3D { Name = "Drehteil", Position = new Vector3(0, top, 0) }; AddChild(slewT);
        var apex = new Vector3(0, 7, 0);
        foreach (var c in c4) Lattice.Bar(k, "paint_yellow", new Vector3(c.X, 0.4f, c.Y), apex, 0.14f);
        float Lj = jib + 1.5f;
        for (float z = 1; z < Lj - 0.01f; z += 2)
        {
            float z1 = MathF.Min(Lj, z + 2);
            Lattice.Bar(k, "paint_yellow", new Vector3(-0.65f, 0, z), new Vector3(-0.65f, 0, z1), 0.12f);
            Lattice.Bar(k, "paint_yellow", new Vector3(0.65f, 0, z), new Vector3(0.65f, 0, z1), 0.12f);
            Lattice.Bar(k, "paint_yellow", new Vector3(0, 1.4f, z), new Vector3(0, 1.4f, z1), 0.12f);
            Lattice.Bar(k, "paint_yellow", new Vector3(-0.65f, 0, z1), new Vector3(0.65f, 0, z1), 0.07f);
            Lattice.Bar(k, "paint_yellow", new Vector3(-0.65f, 0, z), new Vector3(0, 1.4f, z1), 0.06f);
            Lattice.Bar(k, "paint_yellow", new Vector3(0.65f, 0, z), new Vector3(0, 1.4f, z1), 0.06f);
            Lattice.Bar(k, "paint_yellow", new Vector3(-0.65f, 0, z), new Vector3(0.65f, 0, z1), 0.05f);
        }
        for (float z = -1; z > -13; z -= 2)
        {
            float z1 = z - 2;
            Lattice.Bar(k, "paint_yellow", new Vector3(-0.9f, 0.3f, z), new Vector3(-0.9f, 0.3f, z1), 0.14f);
            Lattice.Bar(k, "paint_yellow", new Vector3(0.9f, 0.3f, z), new Vector3(0.9f, 0.3f, z1), 0.14f);
            Lattice.Bar(k, "paint_yellow", new Vector3(-0.9f, 0.3f, z1), new Vector3(0.9f, 0.3f, z1), 0.07f);
            Lattice.Bar(k, "paint_yellow", new Vector3(-0.9f, 0.3f, z), new Vector3(0.9f, 0.3f, z1), 0.06f);
        }
        Lattice.Bar(k, "steel", apex, new Vector3(0, 1.4f, 24), 0.06f);
        Lattice.Bar(k, "steel", apex, new Vector3(0, 1.4f, 12), 0.06f);
        Lattice.Bar(k, "steel", apex, new Vector3(-0.9f, 0.3f, -12), 0.06f);
        Lattice.Bar(k, "steel", apex, new Vector3(0.9f, 0.3f, -12), 0.06f);
        foreach (var c in c4) Lattice.Bar(k, "paint_yellow", new Vector3(c.X, 0, c.Y), new Vector3(MathF.Sign(c.X) * 0.65f, 0, c.Y > 0 ? 1.2f : -1.2f), 0.14f);
        k.Box("concrete", new Vector3(0, -0.9f, -11.6f), new Vector3(2.3f, 2.2f, 2.6f), 0.05f);
        k.Box("dark_metal", new Vector3(0, 0.9f, -7.5f), new Vector3(1.4f, 1.0f, 1.6f), 0.05f);
        k.Box("steel", new Vector3(0, 0.35f, -6.5f), new Vector3(1.9f, 0.08f, 11.5f));
        k.Box("paint_yellow", new Vector3(0, 0.2f, 0), new Vector3(1.8f, 0.4f, 1.8f), 0.05f);
        k.Box("paint_white", new Vector3(1.55f, 0.2f, 1.2f), new Vector3(1.3f, 1.9f, 1.6f), 0.1f);
        k.Build(slewT, "Ausleger");
        var g = new MeshKit();
        g.Box("glass", new Vector3(1.55f, 0.45f, 1.25f), new Vector3(1.32f, 1.2f, 1.62f));
        g.Build(slewT, "Kabinenglas", false);
        Excavator.Beacon(slewT, new Vector3(0, 7.05f, 0), this);
        cabEye = Marker(slewT, "Kranführer", new Vector3(1.55f, 0.6f, 1.9f));
        trolleyVis = new Node3D { Name = "Laufkatze" }; slewT.AddChild(trolleyVis);
        k.Box("dark_metal", new Vector3(0, -0.25f, 0), new Vector3(1.5f, 0.35f, 1.3f), 0.04f);
        k.CylX("steel", new Vector3(0, -0.55f, 0.3f), 0.22f, 1.2f, 14);
        k.CylX("steel", new Vector3(0, -0.55f, -0.3f), 0.22f, 1.2f, 14);
        k.Build(trolleyVis, "Laufkatze");
        hook = new CraneHook { Name = "Kranhaken", crane = this, Mass = 800f, ContinuousCd = true, CanSleep = false, CustomIntegrator = false, TopLevel = true, CollisionLayer = Layers.Hook, CollisionMask = Layers.All & ~Layers.Element };
        hook.LinearDamp = 0.01f; hook.AngularDamp = 0.5f;
        k.Box("paint_red", new Vector3(0, 0.35f, 0), new Vector3(0.7f, 0.8f, 0.35f), 0.06f);
        k.CylX("steel", new Vector3(0, 0.5f, 0), 0.25f, 0.4f, 14);
        k.Cyl("dark_metal", new Vector3(0, -0.15f, 0), 0.05f, 0.05f, 0.35f, 8, Quaternion.Identity);
        k.Cyl("dark_metal", new Vector3(0, -0.45f, 0), 0.06f, 0.15f, 0.3f, 10, Quaternion.Identity);
        k.Build(hook, "Unterflasche");
        hook.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.7f, 1.0f, 0.4f) }, Position = new Vector3(0, 0.25f, 0) });
        AddChild(hook);
        rope1 = new RopeVis(this); rope2 = new RopeVis(this);
        for (int i = 0; i < 4; i++) { slings[i] = new RopeVis(this, 0.015f); slings[i].Show(false); }
        engine = new EngineSynth { Name = "Kranmotor", electric = 1f };
        slewT.AddChild(engine);
    }

    public Vector3 TrolleyWorld() => basePos + new Vector3(MathF.Sin(slew) * r, H + 1 - 0.75f, MathF.Cos(slew) * r);
    public float MaxLoad(float rad) => MathF.Min(10f, 140f / MathF.Max(rad, 1f));
    public float MaxLoad() => MaxLoad(r);
    public float LoadT => load != null ? load.MassT : 0f;

    void ApplyTransforms()
    {
        slewT.Rotation = new Vector3(0, slew, 0);
        trolleyVis.Position = new Vector3(0, -0.05f, r);
    }

    public override ControlsInfo Controls()
    {
        var c = new ControlsInfo { lUp = "Katze aus", lDown = "Katze ein", lLeft = "Drehen L", lRight = "Drehen R", rUp = "Heben", rDown = "Senken" };
        c.buttons.Add(("hook", load != null ? "Ablassen" : "Einhängen", "Last"));
        return c;
    }

    public override bool OnButton(string id)
    {
        if (id != "hook") return false;
        if (load == null)
        {
            Element best = null; float bd = 2.6f;
            if (Buildings.I != null)
                foreach (var el in Buildings.I.elements)
                {
                    if (!IsInstanceValid(el) || el.state == "hooked" || el.state == "placed" || el.state == "broken") continue;
                    float d = el.LiftPointWorld.DistanceTo(hook.GlobalPosition - Vector3.Up * 0.6f);
                    if (d < bd) { bd = d; best = el; }
                }
            if (best == null) { Hud.Toast("Kein Fertigteil am Haken. Haken über den Anschlagpunkt bringen und absenken.", ToastKind.Warn); return true; }
            if (best.MassT > MaxLoad()) Hud.Toast($"Achtung: {U.Fmt(best.MassT)} t – zulässig bei {U.Fmt(r, 0)} m nur {U.Fmt(MaxLoad())} t!", ToastKind.Warn);
            load = best;
            // Seil straffen, ohne die Last zu reißen
            L = MathF.Max(L, TrolleyWorld().DistanceTo(best.LiftPointWorld) - 0.05f);
            best.state = "hooked";
            best.Freeze = false;
            best.crane = this;
            hook.Freeze = true;
            Hud.Toast($"{best.def.name} angeschlagen ({U.Fmt(best.MassT)} t).", ToastKind.Ok);
            SoundFX.I?.Play("clank", hook.GlobalPosition, 0.8f);
            return true;
        }
        var site = load.site;
        if (site != null && site.CanPlace(load)) { var el = load; DropLoad(); site.Place(el); return true; }
        float above = load.HeightAboveGround();
        if (above > 0.3f && releaseArm <= 0)
        {
            releaseArm = 2.5f;
            Hud.Toast($"Last schwebt {U.Fmt(above)} m über Grund! Erneut tippen zum Ausklinken.", ToastKind.Warn);
            return true;
        }
        releaseArm = 0;
        if (above > 0.3f) dropped++;
        Hud.Toast($"{load.def.name} ausgeklinkt.", ToastKind.Info);
        DropLoad();
        return true;
    }

    public void DropLoad()
    {
        if (load != null && IsInstanceValid(load)) { load.state = "loose"; load.crane = null; }
        if (load != null && IsInstanceValid(load)) hook.GlobalPosition = load.LiftPointWorld + Vector3.Up * 0.7f;
        load = null;
        hook.Freeze = false;
        hook.LinearVelocity = Vector3.Zero;
        foreach (var s in slings) s.Show(false);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var inp = isActive ? input : ControlInput.None;
        float sl = -inp.lx, tr = inp.ly, ho = inp.ry;
        releaseArm = MathF.Max(0, releaseArm - dt);
        if (load != null && !IsInstanceValid(load)) DropLoad();
        float mass = LoadT;
        bool overload = mass > MaxLoad();
        if (overload && tr > 0) tr = 0;
        slewV = U.Approach(slewV, sl * 0.11f, 0.045f * dt);
        rV = U.Approach(rV, tr * 1.0f, 0.6f * dt);
        LV = U.Approach(LV, -ho * (mass > 0 ? 1.0f : 1.6f), 1.5f * dt);
        if (overload && LV < 0) LV = 0;
        slew += slewV * dt;
        r = Math.Clamp(r + rV * dt, 3.5f, jib - 1f);
        if (r <= 3.5f || r >= jib - 1f) rV = 0;
        var hp = hook.GlobalPosition;
        float ground = SoilTerrain.I != null ? SoilTerrain.I.Height(hp.X, hp.Z) : 0;
        L = Math.Clamp(L + LV * dt, 1.5f, basePos.Y + H + 1f - ground + 1f);
        ApplyTransforms();
        if (load != null)
        {
            // Haken folgt dem Anschlagpunkt der Last
            hook.GlobalTransform = new Transform3D(new Basis(Vector3.Up, slew), load.LiftPointWorld + Vector3.Up * 0.75f);
            Guide(dt);
        }
        hydFlow = U.Clamp01(MathF.Abs(slewV) * 8f + MathF.Abs(rV) * 0.6f + MathF.Abs(LV) * 0.6f);
        if (engine != null) { engine.hydFlow = hydFlow; engine.volume = Settings.Volume * (isActive ? 1f : 0.4f); }
    }

    /// <summary>Monteure führen das Teil mit Leitseilen in Position.</summary>
    void Guide(float dt)
    {
        var el = load;
        if (el.site == null || !el.def.hasTarget) return;
        var tgt = el.def.target;
        var p = el.GlobalPosition;
        float dx = tgt.X - p.X, dz = tgt.Z - p.Z;
        float d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < 8f)
        {
            float yawErr = Mathf.AngleDifference(el.GlobalBasis.GetEuler().Y, el.def.targetYaw);
            el.ApplyTorque(Vector3.Up * (yawErr * 4f - el.AngularVelocity.Y * 3f) * el.Mass * 1.5f);
        }
        if (d < 1.2f && p.Y - tgt.Y < 1.5f)
        {
            var v = el.LinearVelocity;
            el.ApplyCentralForce(new Vector3(dx * 3f - v.X * 2.5f, 0, dz * 3f - v.Z * 2.5f) * el.Mass);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        var t = trolleyVis.GlobalPosition + Vector3.Down * 0.4f;
        var hpTop = hook.GlobalTransform * new Vector3(0, 0.7f, 0);
        var side = slewT.GlobalBasis.X * 0.25f;
        rope1.Set(t + side, hpTop + side);
        rope2.Set(t - side, hpTop - side);
        if (load != null && IsInstanceValid(load))
        {
            var hb = hook.GlobalTransform * new Vector3(0, -0.55f, 0);
            var c = load.SlingPoints();
            for (int i = 0; i < 4; i++) { slings[i].Show(true); slings[i].Set(hb, c[i]); }
        }
    }

    public override Vector3 CameraTarget()
    {
        var hp = load != null && IsInstanceValid(load) ? load.GlobalPosition : hook.GlobalPosition;
        return hp + Vector3.Up * 1.5f;
    }
    // schräg von der Seite auf Haken und Last, damit der Turm nicht im Bild steht
    public override float CameraYaw() => slew + 1.1f;
    public override float SpeedKmh => 0;
    public override void Cabin(out Vector3 pos, out Vector3 fwd) { pos = cabEye.GlobalPosition; fwd = (hook.GlobalPosition - pos).Normalized(); }

    public override List<(string, string, string)> HudLines()
    {
        var hp = hook.GlobalPosition;
        float g = SoilTerrain.I != null ? SoilTerrain.I.Height(hp.X, hp.Z) : 0;
        float mass = LoadT, ml = MaxLoad();
        int deg = (int)Mathf.PosMod(Mathf.RadToDeg(slew), 360);
        float sway = U.Flat(load != null ? load.LinearVelocity : hook.LinearVelocity).Length();
        return new()
        {
            ("Ausladung", U.Fmt(r) + " m", $"Drehwinkel {deg}°"),
            ("Haken", U.Fmt(hp.Y - g) + " m", sway > 0.6f ? $"pendelt {U.Fmt(sway)} m/s" : "über Grund"),
            ("Last", $"{U.Fmt(mass)} t / {U.Fmt(ml)} t", mass > ml ? "ÜBERLAST!" : load != null ? load.def.name : "ohne Last"),
        };
    }
}

/// <summary>Abrissbirne am Seilbagger.</summary>
public partial class WreckingBall : RigidBody3D
{
    public WreckingCrane crane;
    float cool;
    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (crane == null) return;
        float F = Rope.Apply(state, new Vector3(0, 0.85f, 0), crane.BoomTip(), crane.ropeL);
        crane.ropeForce = F;
    }
    public override void _PhysicsProcess(double delta) { cool -= (float)delta; }
    public void OnHit(Rid rid, Node body, long bodyShape, long localShape)
    {
        if (cool > 0) return;
        float v = LinearVelocity.Length();
        if (v < 1.0f) return;
        float e = 0.5f * Mass / 1000f * v * v;
        var p = GlobalPosition + LinearVelocity.Normalized() * 0.55f;
        if (body is StaticBody3D && body.GetParent() is Buildings)
        {
            cool = 0.05f;
            var res = Buildings.I.Impact(p, 1.0f, e * 9f, LinearVelocity * 0.7f);
            SoundFX.I?.Play(res.broken > 0 ? "crash" : "thud", p, Math.Clamp(e / 20f, 0.4f, 1f), 0.05f);
        }
        else if (v > 2f) SoundFX.I?.Play("metal", p, Math.Clamp(v / 8f, 0.2f, 0.9f), 0.2f);
    }
}

/// <summary>Seilbagger mit 3-t-Abrissbirne. Schwung entsteht durch Drehen des Oberwagens; Seilzug wirkt auf die Maschine.</summary>
public partial class WreckingCrane : VehicleBase
{
    Node3D upper, boomT;
    HydJoint swing, luff;
    public float ropeL = 9f, ropeV, ropeForce;
    public WreckingBall ball;
    TrackDrive tracks;
    RopeVis hoist, pendant;
    StandardMaterial3D trackMatL, trackMatR;
    CollisionShape3D upperShape;
    public bool workMode = true;
    const float BoomLen = 19f, BallR = 0.55f;

    public static WreckingCrane Create(Node parent, Vector3 pos, float yaw)
    {
        var w = new WreckingCrane { Name = "Seilbagger" };
        w.Build();
        parent.AddChild(w);
        w.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
        w.spawn = w.GlobalTransform;
        w.tracks.Init(w, 3.2f, 4.6f, 0f, 0.9f, 7);
        w.ball.GlobalPosition = w.BoomTip() + Vector3.Down * w.ropeL;
        return w;
    }

    void Build()
    {
        displayName = "Seilbagger mit Abrissbirne"; shortName = "Birne"; camDistance = 26f;
        SetupBody(55000f, new Vector3(0, 1.2f, -0.8f), BoxInertia(55000f, 4f, 4f, 5.5f) * 1.5f);
        SetupEngine(6, 700, 1900, 80f, 300f, 1.1f);
        swing = new HydJoint(0, -1e6f, 1e6f, 0.34f, 0.5f);
        luff = new HydJoint(1.12f, 0.55f, 1.3f, 0.16f, 0.45f);
        var k = new MeshKit();
        k.Box("dark_metal", new Vector3(0, 0.7f, 0), new Vector3(2.2f, 0.6f, 2.8f), 0.05f);
        foreach (float sx in new[] { -1.6f, 1.6f })
        {
            k.Box("dark_metal", new Vector3(sx, 0.5f, 0), new Vector3(0.45f, 0.5f, 4.4f), 0.05f);
            k.CylX("steel", new Vector3(sx, 0.45f, 2.2f), 0.38f, 0.6f, 16);
            k.CylX("dark_metal", new Vector3(sx, 0.47f, -2.2f), 0.42f, 0.62f, 16);
        }
        k.Build(this, "Unterwagen");
        trackMatL = Mats.Copy("track"); trackMatR = Mats.Copy("track");
        foreach (float sx in new[] { 1.6f, -1.6f })
        {
            k.TrackBelt("track", sx, 0.47f, 5.3f, 0.47f, 0.75f);
            k.Build(this, "Kette").MaterialOverride = sx > 0 ? trackMatL : trackMatR;
        }
        AddBox(new Vector3(0, 0.85f, 0), new Vector3(3.6f, 0.8f, 4.6f));
        upper = new Node3D { Name = "Oberwagen", Position = new Vector3(0, 1.15f, 0) }; AddChild(upper);
        k.Box("dark_metal", new Vector3(0, 0.15f, -0.6f), new Vector3(3.0f, 0.3f, 4.8f), 0.04f);
        k.Box("paint_red", new Vector3(0, 1.05f, -1.4f), new Vector3(2.9f, 1.5f, 3.0f), 0.12f);
        k.Box("dark_metal", new Vector3(0, 0.9f, -3.3f), new Vector3(3.0f, 1.2f, 1.0f), 0.1f);
        k.Box("paint_red", new Vector3(1.0f, 1.2f, 1.0f), new Vector3(1.0f, 1.8f, 1.6f), 0.08f);
        k.CylX("steel", new Vector3(0, 1.2f, -1.0f), 0.45f, 1.2f, 18);
        k.Cyl("steel", new Vector3(-1.0f, 2.0f, -2.2f), 0.07f, 0.07f, 0.5f, 10, Quaternion.Identity);
        var apex = new Vector3(0, 3.6f, -1.6f);
        foreach (var p in new[] { new Vector3(-0.9f, 0.3f, -2.6f), new Vector3(0.9f, 0.3f, -2.6f), new Vector3(-0.9f, 0.3f, -0.2f), new Vector3(0.9f, 0.3f, -0.2f) }) Lattice.Bar(k, "dark_metal", p, apex, 0.15f);
        k.Build(upper, "Oberwagen");
        var g = new MeshKit();
        g.Box("glass", new Vector3(1.0f, 1.55f, 1.05f), new Vector3(1.02f, 1.0f, 1.5f));
        g.Build(upper, "Scheiben", false);
        Excavator.Beacon(upper, new Vector3(1.0f, 2.15f, 0.6f), this);
        cabEye = Marker(upper, "Fahrerauge", new Vector3(1.0f, 1.7f, 1.3f));
        exhaust = Marker(upper, "Auspuff", new Vector3(-1.0f, 2.25f, -2.2f));
        upperShape = AddBox(Vector3.Zero, new Vector3(3.0f, 1.8f, 4.6f));
        boomT = new Node3D { Name = "Gittermast", Position = new Vector3(0, 0.6f, 1.2f) }; upper.AddChild(boomT);
        for (float z = 0; z < BoomLen - 0.01f; z += 1.5f)
        {
            float z1 = MathF.Min(BoomLen, z + 1.5f);
            float w0 = 0.55f - 0.25f * z / BoomLen, w1 = 0.55f - 0.25f * z1 / BoomLen;
            var c0 = new[] { new Vector3(-w0, -w0, z), new Vector3(w0, -w0, z), new Vector3(w0, w0, z), new Vector3(-w0, w0, z) };
            var c1 = new[] { new Vector3(-w1, -w1, z1), new Vector3(w1, -w1, z1), new Vector3(w1, w1, z1), new Vector3(-w1, w1, z1) };
            for (int i = 0; i < 4; i++)
            {
                Lattice.Bar(k, "paint_red", c0[i], c1[i], 0.1f);
                Lattice.Bar(k, "paint_red", c0[i], c1[(i + 1) % 4], 0.05f);
                Lattice.Bar(k, "paint_red", c1[i], c1[(i + 1) % 4], 0.05f);
            }
        }
        k.CylX("steel", new Vector3(0, 0, BoomLen), 0.3f, 0.6f, 14);
        k.Build(boomT, "Gittermast");
        hoist = new RopeVis(this, 0.03f);
        pendant = new RopeVis(this, 0.03f);
        tracks = new TrackDrive();
        ball = new WreckingBall { Name = "Abrissbirne", crane = this, Mass = 3000f, ContinuousCd = true, CanSleep = false, TopLevel = true, ContactMonitor = true, MaxContactsReported = 4, CollisionLayer = Layers.Hook, CollisionMask = Layers.All & ~Layers.Vehicle };
        ball.LinearDamp = 0.005f; ball.AngularDamp = 0.2f;
        ball.PhysicsMaterialOverride = new PhysicsMaterial { Bounce = 0.15f, Friction = 0.5f };
        k.Sphere("dark_metal", Vector3.Zero, BallR, 20);
        k.Cyl("steel", new Vector3(0, BallR + 0.15f, 0), 0.08f, 0.12f, 0.3f, 10, Quaternion.Identity);
        k.Build(ball, "Birne");
        ball.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = BallR } });
        ball.BodyShapeEntered += ball.OnHit;
        AddChild(ball);
        ApplyJoints();
    }

    public Vector3 BoomTip() => boomT.GlobalTransform * new Vector3(0, 0, BoomLen);

    void ApplyJoints()
    {
        upper.Rotation = new Vector3(0, swing.pos, 0);
        boomT.Rotation = new Vector3(-luff.pos, 0, 0);
        upperShape.Transform = upper.Transform * new Transform3D(Basis.Identity, new Vector3(0, 1.0f, -1.2f));
    }

    public override ControlsInfo Controls()
    {
        var c = new ControlsInfo { rUp = "Heben", rDown = "Senken" };
        if (workMode) { c.lUp = "Ausleger ab"; c.lDown = "Ausleger auf"; c.lLeft = "Drehen L"; c.lRight = "Drehen R"; }
        else { c.lUp = "Vor"; c.lDown = "Zurück"; c.lLeft = "Links"; c.lRight = "Rechts"; }
        c.buttons.Add(("mode", workMode ? "Fahren" : "Arbeiten", "Modus"));
        return c;
    }

    public override bool OnButton(string id) { if (id != "mode") return false; workMode = !workMode; return true; }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var inp = isActive ? input : ControlInput.None;
        float thr = 0, st = 0, sw = 0, lf = 0;
        if (workMode) { sw = -inp.lx; lf = -inp.ly; } else { thr = inp.ly; st = inp.lx; }
        swing.Step(sw, 1f, dt);
        luff.Step(lf, 1f, dt);
        ropeV = U.Approach(ropeV, -inp.ry * 1.4f, 2f * dt);
        ropeL = Math.Clamp(ropeL + ropeV * dt, 1.5f, 22f);
        ApplyJoints();
        tracks.Step(thr, st, dt);
        // Seilzug der Birne wirkt auf die Auslegerspitze (Kippgefahr beim Pendeln)
        if (ropeForce > 1f)
        {
            var tip = BoomTip();
            var dir = (ball.GlobalPosition - tip).Normalized();
            U.ForceAt(this, dir * MathF.Min(ropeForce, 400000f), tip);
        }
        hydFlow = U.Clamp01(MathF.Abs(sw) + MathF.Abs(lf) + MathF.Abs(inp.ry) * 0.6f);
        UpdateEngine(0.12f + hydFlow * 0.6f + MathF.Abs(thr) * 0.5f, hydFlow * 0.5f + tracks.tractionUsed * 0.4f, dt);
        if (engine != null) engine.trackSpeed = MathF.Max(MathF.Abs(tracks.beltL), MathF.Abs(tracks.beltR));
        ComputeStability(2.6f, 1.9f);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        var tip = BoomTip();
        hoist.Set(tip, ball.GlobalTransform * new Vector3(0, BallR + 0.3f, 0));
        pendant.Set(upper.GlobalTransform * new Vector3(0, 3.6f, -1.6f), boomT.GlobalTransform * new Vector3(0, 0, BoomLen * 0.98f));
        trackMatL.Uv1Offset = new Vector3(tracks.distL / 0.22f, 0, 0);
        trackMatR.Uv1Offset = new Vector3(tracks.distR / 0.22f, 0, 0);
    }

    public override Vector3 CameraTarget()
    {
        var b = ball.GlobalPosition;
        return new Vector3((GlobalPosition.X + b.X) / 2, GlobalPosition.Y + 5, (GlobalPosition.Z + b.Z) / 2);
    }
    public override float CameraYaw() { var f = workMode ? upper.GlobalBasis.Z : GlobalBasis.Z; return MathF.Atan2(f.X, f.Z); }
    public override void Cabin(out Vector3 pos, out Vector3 fwd) { pos = cabEye.GlobalPosition; fwd = (ball.GlobalPosition - pos).Normalized(); }

    public override List<(string, string, string)> HudLines()
    {
        var tip = BoomTip();
        float reach = new Vector2(tip.X - GlobalPosition.X, tip.Z - GlobalPosition.Z).Length();
        return new()
        {
            ("Birne", "3,0 t", U.Fmt(ball.LinearVelocity.Length() * 3.6f, 0) + " km/h"),
            ("Ausladung", U.Fmt(reach) + " m", "Seil " + U.Fmt(ropeL) + " m"),
            ("Modus", workMode ? "Arbeiten" : "Fahren", stability < 0.25f ? "Kippgefahr!" : ""),
        };
    }
}
