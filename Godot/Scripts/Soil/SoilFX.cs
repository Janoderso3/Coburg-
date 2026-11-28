using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

public interface ISoilCatcher { bool TryCatch(Vector3 p, Vector3 v, float vol, SoilMat mat); }
public interface ISoilReceiver { void Receive(float vol, SoilMat mat); }
public interface IScoopable { float Volume { get; } SoilMat Mat { get; } void Absorb(); }

/// <summary>
/// Schüttgut-Effekte: fallende Erdkrümel (tragen echtes Volumen), Erdbrocken als Physikkörper (zerfallen später),
/// Staub- und Abgaswolken als Billboards.
/// </summary>
public partial class SoilFX : Node3D
{
    public static SoilFX I;
    public static readonly List<ISoilCatcher> Catchers = new();

    struct Grain { public Vector3 p, v, spin; public float vol, size, age; public SoilMat mat; public Basis rot; }
    readonly List<Grain> grains = new(2048);
    readonly Dictionary<SoilMat, MultiMeshInstance3D> grainMM = new();
    int maxGrains = 2200;

    readonly List<Clod> clods = new();
    readonly Stack<Clod> clodPool = new();
    int maxClods = 160;
    public static Mesh RockMeshA, RockMeshB;

    struct Puff { public Vector3 p, v; public float size, grow, life, t, a; public Color c; }
    readonly List<Puff> puffs = new(2048);
    MultiMeshInstance3D puffMM;
    int maxPuffs = 1800;
    public float soundAccum;

    public override void _Ready()
    {
        I = this;
        RockMeshA = RockMesh(1, 0.25f);
        RockMeshB = RockMesh(2, 0.3f);
        foreach (SoilMat m in Enum.GetValues(typeof(SoilMat)))
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = RockMeshA, InstanceCount = maxGrains, VisibleInstanceCount = 0 };
            var mi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = Mats.Get(MatKey(m)), CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
            AddChild(mi);
            grainMM[m] = mi;
        }
        // Staub: Billboard-Quads mit weichem Rand
        var quad = new QuadMesh { Size = new Vector2(1, 1) };
        var pm = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = SoftDot(),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            ProximityFadeEnabled = true,
            ProximityFadeDistance = 0.8f,
        };
        quad.Material = pm;
        var pmm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = quad, InstanceCount = maxPuffs, VisibleInstanceCount = 0 };
        puffMM = new MultiMeshInstance3D { Multimesh = pmm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(puffMM);
    }

    public static Mesh RockMesh(int seed, float rough)
    {
        var r = new Rng(seed * 31 + 7);
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        Vector3[] b = { new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1) };
        int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        var disp = new float[12];
        for (int i = 0; i < 12; i++) disp[i] = 1f + (r.Next() - 0.5f) * rough * 2f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < f.Length; i += 3)
        {
            var p0 = b[f[i]].Normalized() * disp[f[i]] * 0.5f;
            var p1 = b[f[i + 1]].Normalized() * disp[f[i + 1]] * 0.5f;
            var p2 = b[f[i + 2]].Normalized() * disp[f[i + 2]] * 0.5f;
            var n = (p1 - p0).Cross(p2 - p0).Normalized();
            if (n.Dot(p0 + p1 + p2) < 0) { (p1, p2) = (p2, p1); n = -n; }
            // Godot: Vorderseite im Uhrzeigersinn -> Reihenfolge umdrehen
            foreach (var p in new[] { p0, p2, p1 }) { st.SetNormal(n); st.SetUV(new Vector2(p.X + p.Z, p.Y)); st.AddVertex(p); }
        }
        st.GenerateTangents();
        return st.Commit();
    }

    public static string MatKey(SoilMat m) => m switch
    {
        SoilMat.Rubble => "rubble_chunk",
        SoilMat.Gravel => "gravel_chunk",
        SoilMat.Concrete => "concrete_wet",
        _ => "soil_chunk",
    };

    public static Color DustColor(SoilMat m) => m switch
    {
        SoilMat.Rubble => new Color(0.68f, 0.65f, 0.6f),
        SoilMat.Concrete => new Color(0.7f, 0.7f, 0.68f),
        SoilMat.Gravel => new Color(0.66f, 0.64f, 0.6f),
        _ => new Color(0.58f, 0.5f, 0.4f),
    };

    // ---------- Krümel ----------
    public void SpawnFalling(Vector3 p, Vector3 v, float vol, SoilMat mat, float size = 0f)
    {
        if (grains.Count >= maxGrains) { var o = grains[0]; grains.RemoveAt(0); Land(o); }
        if (size <= 0) size = Math.Clamp(MathF.Pow(MathF.Max(vol, 0.0002f), 1f / 3f) * 0.75f, 0.05f, 0.26f);
        grains.Add(new Grain { p = p, v = v, vol = vol, mat = mat, size = size, rot = new Basis(new Quaternion(U.RandSphere().Normalized(), U.Rand(0, 6.28f))), spin = U.RandSphere() * 8f });
    }

    static void Land(Grain g)
    {
        if (g.vol > 0 && SoilTerrain.I != null) SoilTerrain.I.Deposit(g.p.X, g.p.Z, g.vol, g.mat, 0.32f);
    }

    // ---------- Brocken ----------
    public void SpawnClod(Vector3 p, float vol, SoilMat mat, Vector3 vel)
    {
        int n = Math.Clamp((int)MathF.Ceiling(vol / 0.12f), 1, 10);
        float each = vol / n;
        for (int i = 0; i < n; i++)
        {
            if (clods.Count >= maxClods) clods[0].Crumble(true);
            var c = clodPool.Count > 0 ? clodPool.Pop() : CreateClod();
            var pos = p + U.RandSphere() * MathF.Pow(vol, 1f / 3f) * 0.4f;
            c.Activate(pos, each, mat, vel + U.RandSphere() * 0.6f);
            clods.Add(c);
        }
    }

    Clod CreateClod()
    {
        var c = new Clod { fx = this };
        AddChild(c);
        return c;
    }

    internal void Release(Clod c)
    {
        clods.Remove(c);
        c.Deactivate();
        clodPool.Push(c);
    }
    public int ClodCount => clods.Count;

    // ---------- Staub / Rauch ----------
    public void Dust(Vector3 p, int count, Color c, float size = 1f)
    {
        for (int i = 0; i < count; i++)
        {
            if (puffs.Count >= maxPuffs) puffs.RemoveAt(0);
            var cc = c * U.Rand(0.9f, 1.1f);
            puffs.Add(new Puff
            {
                p = p + new Vector3(U.Rand(-0.5f, 0.5f), U.Rand(0, 0.3f), U.Rand(-0.5f, 0.5f)),
                v = new Vector3(U.Rand(-0.7f, 0.7f), U.Rand(0.3f, 1.1f), U.Rand(-0.7f, 0.7f)),
                size = size * U.Rand(0.8f, 1.6f), grow = 1.1f * size, life = U.Rand(2.5f, 5f), a = U.Rand(0.3f, 0.5f), c = cc,
            });
        }
    }

    public void Smoke(Vector3 p, Vector3 v, float density, float darkness)
    {
        if (puffs.Count >= maxPuffs) puffs.RemoveAt(0);
        float g = Mathf.Lerp(0.55f, 0.15f, darkness);
        puffs.Add(new Puff { p = p, v = v + new Vector3(U.Rand(-0.15f, 0.15f), U.Rand(1.0f, 1.6f), U.Rand(-0.15f, 0.15f)), size = U.Rand(0.25f, 0.4f), grow = 0.9f, life = 1.2f + density * 1.8f, a = 0.15f + density * 0.35f, c = new Color(g, g, g) });
    }

    public static Texture2D SoftDot()
    {
        int s = 64;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2 - 1, dy = (y + 0.5f) / s * 2 - 1;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float n = 0.75f + 0.25f * U.Noise(x * 0.25f, y * 0.25f);
                float a = U.Clamp01(1 - d) * n;
                px[y * s + x] = new Color(1, 1, 1, a * a);
            }
        return ProcTex.ToTexture(px, s);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var T = SoilTerrain.I;
        int n = 0;
        for (int i = 0; i < grains.Count; i++)
        {
            var g = grains[i];
            g.v.Y -= 9.81f * dt;
            g.v *= 1f - 0.4f * dt;
            g.p += g.v * dt;
            g.age += dt;
            bool gone = false;
            if (g.v.Y < 0)
                for (int c = 0; c < Catchers.Count; c++)
                    if (Catchers[c].TryCatch(g.p, g.v, g.vol, g.mat)) { gone = true; break; }
            if (!gone && T != null)
            {
                float gy = T.Height(g.p.X, g.p.Z);
                if (g.p.Y <= gy || g.age > 8f || !T.Inside(g.p.X, g.p.Z, 1f))
                {
                    if (T.Inside(g.p.X, g.p.Z, 1f)) { g.p.Y = gy; Land(g); }
                    if (g.vol > 0.01f && U.Rand() < 0.2f) Dust(new Vector3(g.p.X, gy + 0.1f, g.p.Z), 1, DustColor(g.mat), 0.8f);
                    soundAccum += g.vol;
                    gone = true;
                }
            }
            if (!gone) grains[n++] = g;
        }
        grains.RemoveRange(n, grains.Count - n);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        // Krümel zeichnen
        var counts = new Dictionary<SoilMat, int>();
        foreach (var kv in grainMM) counts[kv.Key] = 0;
        for (int i = 0; i < grains.Count; i++)
        {
            var g = grains[i];
            var mm = grainMM[g.mat].Multimesh;
            int c = counts[g.mat];
            if (c >= mm.InstanceCount) continue;
            g.rot = g.rot.Rotated(g.spin.Normalized(), g.spin.Length() * dt).Orthonormalized();
            grains[i] = g;
            mm.SetInstanceTransform(c, new Transform3D(g.rot.Scaled(Vector3.One * g.size), g.p));
            counts[g.mat] = c + 1;
        }
        foreach (var kv in grainMM) kv.Value.Multimesh.VisibleInstanceCount = counts[kv.Key];
        // Staub
        var pmm = puffMM.Multimesh;
        int pn = 0;
        for (int i = 0; i < puffs.Count; i++)
        {
            var o = puffs[i];
            o.t += dt;
            if (o.t >= o.life) continue;
            o.p += o.v * dt;
            o.v.X *= 0.97f; o.v.Z *= 0.97f; o.v.Y = o.v.Y * 0.97f + 0.05f * dt;
            o.p.X += 0.35f * dt; o.p.Z += 0.12f * dt; // leichter Wind
            o.size += o.grow * dt;
            puffs[pn] = o;
            float f = o.t / o.life;
            float a = o.a * MathF.Min(1, o.t * 6) * (1 - f) * (1 - f);
            if (pn < pmm.InstanceCount)
            {
                pmm.SetInstanceTransform(pn, new Transform3D(Basis.Identity.Scaled(Vector3.One * o.size), o.p));
                pmm.SetInstanceColor(pn, new Color(o.c.R, o.c.G, o.c.B, a));
            }
            pn++;
        }
        puffs.RemoveRange(pn, puffs.Count - pn);
        pmm.VisibleInstanceCount = Math.Min(pn, pmm.InstanceCount);
    }
}

/// <summary>Erdbrocken: rollt physikalisch und zerfällt nach dem Liegenbleiben zu losem Boden.</summary>
public partial class Clod : RigidBody3D, IScoopable
{
    public SoilFX fx;
    public float vol;
    public SoilMat mat;
    float rest, age;
    bool active;
    MeshInstance3D mesh;
    CollisionShape3D shape;
    public float Volume => vol;
    public SoilMat Mat => mat;

    public override void _Ready()
    {
        CollisionLayer = Layers.Clod;
        CollisionMask = Layers.All;
        ContinuousCd = true;
        ContactMonitor = true;
        MaxContactsReported = 2;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.85f, Bounce = 0.02f };
        mesh = new MeshInstance3D { Mesh = SoilFX.RockMeshB };
        AddChild(mesh);
        shape = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.45f } };
        AddChild(shape);
        BodyEntered += OnHit;
    }

    public void Activate(Vector3 p, float v, SoilMat m, Vector3 vel)
    {
        vol = v; mat = m; rest = 0; age = 0; active = true;
        float size = Math.Clamp(MathF.Pow(v, 1f / 3f) * 1.05f, 0.12f, 0.9f);
        var sc = new Vector3(size * U.Rand(0.85f, 1.25f), size * U.Rand(0.6f, 0.9f), size * U.Rand(0.85f, 1.2f));
        mesh.Scale = sc;
        mesh.MaterialOverride = Mats.Get(SoilFX.MatKey(m));
        ((SphereShape3D)shape.Shape).Radius = MathF.Max(0.06f, size * 0.42f);
        Visible = true;
        ProcessMode = ProcessModeEnum.Inherit;
        Freeze = false;
        GlobalPosition = p;
        Mass = MathF.Max(5f, v * 1700f);
        LinearVelocity = vel;
        AngularVelocity = U.RandSphere() * 3f;
        CollisionLayer = Layers.Clod;
        Sleeping = false;
    }

    public void Deactivate()
    {
        active = false;
        Visible = false;
        Freeze = true;
        CollisionLayer = 0;
        GlobalPosition = new Vector3(0, -100, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!active) return;
        float dt = (float)delta;
        age += dt;
        if (LinearVelocity.LengthSquared() < 0.04f && AngularVelocity.LengthSquared() < 0.2f) rest += dt; else rest = 0;
        if (rest > 1.2f || age > 14f || GlobalPosition.Y < SoilTerrain.BaseY) Crumble(true);
    }

    void OnHit(Node body)
    {
        if (!active) return;
        float imp = LinearVelocity.Length();
        if (imp > 3f && U.Rand() < 0.3f) SoundFX.I?.Play("soil", GlobalPosition, Math.Clamp(vol * 3f, 0.1f, 1f));
        if (imp > 6.5f && vol > 0.02f) CallDeferred(nameof(Burst));
    }

    void Burst() { Crumble(true, true); }

    public void Absorb() { if (active) fx.Release(this); }

    public void Crumble(bool deposit, bool burst = false)
    {
        if (!active) return;
        var p = GlobalPosition;
        if (deposit)
        {
            if (burst || vol < 0.02f)
            {
                int n = Math.Clamp((int)MathF.Ceiling(vol / 0.03f), 1, 12);
                for (int i = 0; i < n; i++) fx.SpawnFalling(p + U.RandSphere() * 0.2f, LinearVelocity * 0.4f + U.RandSphere() * 1.2f, vol / n, mat);
            }
            else
            {
                bool given = false;
                var space = GetWorld3D().DirectSpaceState;
                var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 0.2f, p + Vector3.Down * 1.5f, Layers.All & ~(Layers.Clod | Layers.Debris));
                var hit = space.IntersectRay(q);
                if (hit.Count > 0)
                {
                    var col = hit["collider"].AsGodotObject() as Node;
                    var rcv = FindReceiver(col);
                    if (rcv != null) { rcv.Receive(vol, mat); given = true; }
                }
                if (!given && SoilTerrain.I != null) SoilTerrain.I.Deposit(p.X, p.Z, vol, mat, MathF.Max(0.3f, mesh.Scale.X * 0.6f));
                fx.Dust(p, 2, SoilFX.DustColor(mat), 0.9f);
            }
        }
        fx.Release(this);
    }

    public static ISoilReceiver FindReceiver(Node n)
    {
        while (n != null)
        {
            if (n is ISoilReceiver r) return r;
            n = n.GetParent();
        }
        return null;
    }
}
