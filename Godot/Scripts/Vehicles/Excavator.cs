using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>
/// 21-t-Kettenbagger: Oberwagen, Monoblock-Ausleger, Stiel, Tieflöffel.
/// Reißkräfte wirken zurück auf die Maschine; mit dem Ausleger kann sie sich anheben; Schwerpunkt wandert mit Arm und Ladung.
/// </summary>
public partial class Excavator : VehicleBase
{
    public bool workMode = true;
    Node3D under, upper, boomT, stickT, bucketT;
    HydJoint swing, boom, stick, buck;
    public SoilTool tool;
    TrackDrive tracks;
    StandardMaterial3D trackMatL, trackMatR;
    HydCylinder cBoomL, cBoomR, cStick, cBucket;
    CollisionShape3D upperShape, cabShape;
    float liftForce;
    static readonly Vector3 aBoomBaseL = new(0.12f, 0.55f, 1.35f), aBoomBaseR = new(-0.52f, 0.55f, 1.35f);
    static readonly Vector3 aBoomMidL = new(0.33f, -0.3f, 2.3f), aBoomMidR = new(-0.33f, -0.3f, 2.3f);
    static readonly Vector3 aStickBase = new(0, 1.55f, 2.6f), aStickEnd = new(0, 0.5f, -0.55f);
    static readonly Vector3 aBuckBase = new(0, 0.45f, 0.5f), aBuckEnd = new(0, 0.42f, -0.1f);

    public static Excavator Create(Node parent, Vector3 pos, float yaw)
    {
        var e = new Excavator { Name = "Kettenbagger" };
        e.Build();
        parent.AddChild(e);
        e.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
        e.spawn = e.GlobalTransform;
        e.tracks.Init(e, 2.4f, 3.9f, 0f, 1.55f, 7);
        e.tracks.mu = 0.8f; e.tracks.muLat = 0.7f;
        return e;
    }

    void Build()
    {
        displayName = "Kettenbagger 21 t"; shortName = "Bagger"; camDistance = 13f;
        SetupBody(21000f, new Vector3(0, 0.9f, -0.3f), BoxInertia(21000f, 3f, 2.6f, 4.5f) * 1.4f);
        SetupEngine(6, 800, 2000, 92f, 340f);
        swing = new HydJoint(0, -1e6f, 1e6f, 0.75f, 0.9f);
        boom = new HydJoint(0.55f, -0.62f, 1.0f, 0.42f, 1.8f);
        stick = new HydJoint(1.4f, 0.5f, 2.6f, 0.62f, 2.2f);
        buck = new HydJoint(1.2f, -0.85f, 2.25f, 0.95f, 3.5f);
        var k = new MeshKit();
        // ---------- Unterwagen ----------
        under = new Node3D { Name = "Unterwagen" }; AddChild(under);
        k.Box("dark_metal", new Vector3(0, 0.66f, 0), new Vector3(1.9f, 0.55f, 2.3f), 0.06f);
        foreach (float sx in new[] { -1.2f, 1.2f })
        {
            k.Box("dark_metal", new Vector3(sx, 0.47f, 0), new Vector3(0.36f, 0.42f, 3.7f), 0.05f);
            k.CylX("steel", new Vector3(sx, 0.42f, 1.85f), 0.36f, 0.5f, 18);
            k.CylX("dark_metal", new Vector3(sx, 0.42f, -1.85f), 0.38f, 0.52f, 18);
            for (int i = 0; i < 7; i++) k.CylX("steel", new Vector3(sx, 0.16f, -1.5f + i * 0.5f), 0.12f, 0.46f, 10);
            k.CylX("steel", new Vector3(sx, 0.78f, 0.7f), 0.09f, 0.4f, 10);
            k.CylX("steel", new Vector3(sx, 0.78f, -0.7f), 0.09f, 0.4f, 10);
        }
        k.Cyl("dark_metal", new Vector3(0, 0.99f, 0), 1.0f, 1.0f, 0.18f, 28, Quaternion.Identity);
        k.Build(under, "Fahrwerk");
        trackMatL = Mats.Copy("track"); trackMatR = Mats.Copy("track");
        foreach (float sx in new[] { 1.2f, -1.2f })
        {
            k.TrackBelt("track", sx, 0.42f, 4.5f, 0.42f, 0.6f);
            k.Build(under, "Kette").MaterialOverride = sx > 0 ? trackMatL : trackMatR;
        }
        AddBox(new Vector3(0, 0.85f, 0), new Vector3(2.0f, 0.55f, 2.4f));
        AddBox(new Vector3(-1.2f, 0.62f, 0), new Vector3(0.6f, 0.5f, 4.3f));
        AddBox(new Vector3(1.2f, 0.62f, 0), new Vector3(0.6f, 0.5f, 4.3f));
        // ---------- Oberwagen (Kabine links = +X) ----------
        upper = new Node3D { Name = "Oberwagen", Position = new Vector3(0, 1.08f, 0) }; AddChild(upper);
        k.Box("dark_metal", new Vector3(0, 0.11f, -0.45f), new Vector3(2.7f, 0.22f, 4.2f), 0.04f);
        k.Box("paint_yellow", new Vector3(0, 0.7f, -1.55f), new Vector3(2.75f, 0.95f, 1.9f), 0.1f);
        k.ProfileX("paint_yellow", new[] { new Vector2(-2.25f, 0.25f), new Vector2(-2.6f, 0.45f), new Vector2(-2.62f, 1.15f), new Vector2(-2.3f, 1.3f), new Vector2(-1.9f, 1.3f), new Vector2(-1.9f, 0.25f) }, 2.85f);
        k.Box("paint_yellow", new Vector3(-0.82f, 0.7f, 0.55f), new Vector3(1.1f, 0.95f, 1.6f), 0.08f);
        k.Box("paint_yellow", new Vector3(-0.15f, 0.45f, 1.0f), new Vector3(0.4f, 0.5f, 0.9f), 0.04f);
        k.Box("black_plastic", new Vector3(0, 1.2f, -1.55f), new Vector3(2.6f, 0.06f, 1.8f));
        for (int i = 0; i < 6; i++) k.Box("dark_metal", new Vector3(-0.9f + i * 0.36f, 1.18f, -1.55f), new Vector3(0.05f, 0.05f, 1.6f));
        k.Cyl("steel", new Vector3(-0.7f, 1.45f, -1.3f), 0.07f, 0.07f, 0.6f, 10, Quaternion.Identity);
        k.Box("paint_yellow", new Vector3(0.82f, 0.35f, 0.55f), new Vector3(1.0f, 0.25f, 1.75f), 0.04f);
        k.Box("paint_yellow", new Vector3(0.82f, 2.05f, 0.5f), new Vector3(1.04f, 0.09f, 1.85f), 0.04f);
        foreach (var (x, z) in new[] { (0.34f, 1.4f), (1.3f, 1.4f), (0.34f, -0.3f), (1.3f, -0.3f) })
            k.Box("paint_yellow", new Vector3(x, 1.22f, z), new Vector3(0.07f, 1.65f, 0.07f));
        k.Box("seat", new Vector3(0.82f, 0.75f, 0.25f), new Vector3(0.55f, 0.55f, 0.5f), 0.08f);
        k.Box("seat", new Vector3(0.82f, 1.1f, 0.0f), new Vector3(0.55f, 0.6f, 0.12f), 0.05f);
        k.Box("black_plastic", new Vector3(1.15f, 0.85f, 0.55f), new Vector3(0.18f, 0.25f, 0.5f), 0.03f);
        k.Box("black_plastic", new Vector3(0.49f, 0.85f, 0.55f), new Vector3(0.18f, 0.25f, 0.5f), 0.03f);
        k.Box("steel", new Vector3(1.36f, 1.0f, -0.8f), new Vector3(0.05f, 0.05f, 1.0f));
        k.Box("steel", new Vector3(-1.38f, 1.2f, -0.6f), new Vector3(0.05f, 0.05f, 2.6f));
        k.Build(upper, "Oberwagen");
        var g = new MeshKit();
        g.Box("glass", new Vector3(0.82f, 1.2f, 0.55f), new Vector3(0.94f, 1.55f, 1.68f));
        g.Build(upper, "Scheiben", false);
        Beacon(upper, new Vector3(0.82f, 2.15f, 0.0f), this);
        var lamp = new MeshKit();
        lamp.Box("headlight", new Vector3(0.82f, 2.06f, 1.47f), new Vector3(0.2f, 0.1f, 0.02f));
        lamp.Box("dark_metal", new Vector3(0.82f, 2.06f, 1.41f), new Vector3(0.24f, 0.14f, 0.1f), 0.02f);
        lamp.Box("headlight", new Vector3(-0.82f, 1.25f, 1.36f), new Vector3(0.22f, 0.12f, 0.08f), 0.02f);
        lamp.Build(upper, "Scheinwerfer", false);
        cabEye = Marker(upper, "Fahrerauge", new Vector3(0.82f, 1.75f, 0.7f));
        exhaust = Marker(upper, "Auspuff", new Vector3(-0.7f, 1.78f, -1.3f));
        upperShape = AddBox(Vector3.Zero, new Vector3(2.7f, 1.1f, 4.1f));
        cabShape = AddBox(Vector3.Zero, new Vector3(1.0f, 1.7f, 1.75f));
        // ---------- Ausleger, Stiel, Löffel ----------
        boomT = new Node3D { Name = "Ausleger", Position = new Vector3(-0.2f, 1.05f, 1.05f) }; upper.AddChild(boomT);
        k.Beam("paint_yellow", Vector3.Zero, new Vector3(0, 1.25f, 2.9f), 0.55f, 0.62f, Vector3.Up, 0.06f);
        k.Beam("paint_yellow", new Vector3(0, 1.25f, 2.9f), new Vector3(0, 0, 5.7f), 0.5f, 0.55f, Vector3.Up, 0.06f);
        k.Box("paint_yellow", new Vector3(0, 1.2f, 2.9f), new Vector3(0.6f, 0.36f, 0.5f), 0.05f);
        k.CylX("steel", Vector3.Zero, 0.14f, 0.75f, 14);
        k.CylX("steel", new Vector3(0, 0, 5.7f), 0.12f, 0.62f, 14);
        k.Beam("dark_metal", new Vector3(-0.2f, 0.95f, 1.0f), new Vector3(-0.2f, 1.45f, 4.8f), 0.05f, 0.05f, Vector3.Up, 0);
        k.Build(boomT, "Ausleger");
        BeamTool(boomT, Vector3.Zero, new Vector3(0, 1.25f, 2.9f), 0.55f, 0.62f);
        BeamTool(boomT, new Vector3(0, 1.25f, 2.9f), new Vector3(0, 0, 5.7f), 0.5f, 0.55f);
        stickT = new Node3D { Name = "Stiel", Position = new Vector3(0, 0, 5.7f) }; boomT.AddChild(stickT);
        k.Beam("paint_yellow", new Vector3(0, 0.25f, -0.6f), new Vector3(0, 0, 2.95f), 0.42f, 0.48f, Vector3.Up, 0.05f);
        k.Box("paint_yellow", new Vector3(0, 0.35f, -0.5f), new Vector3(0.44f, 0.3f, 0.4f), 0.04f);
        k.CylX("steel", new Vector3(0, 0, 2.95f), 0.1f, 0.5f, 12);
        k.Build(stickT, "Stiel");
        BeamTool(stickT, new Vector3(0, 0.25f, -0.6f), new Vector3(0, 0, 2.95f), 0.42f, 0.48f);
        bucketT = new Node3D { Name = "Löffel", Position = new Vector3(0, 0, 2.95f) }; stickT.AddChild(bucketT);
        float W = 1.1f;
        k.ProfileX("paint_yellow_worn", new[] { new Vector2(-0.12f, -0.08f), new Vector2(-0.08f, 0.32f), new Vector2(0.25f, 0.68f), new Vector2(0.8f, 0.78f), new Vector2(1.22f, 0.48f), new Vector2(1.42f, 0.02f), new Vector2(1.48f, -0.16f), new Vector2(1.36f, -0.16f), new Vector2(1.2f, 0.36f), new Vector2(0.78f, 0.62f), new Vector2(0.3f, 0.55f), new Vector2(0.05f, 0.25f), new Vector2(0.0f, -0.08f) }, W);
        foreach (float sx in new[] { -W / 2, W / 2 })
            k.ProfileX("paint_yellow_worn", new[] { new Vector2(-0.15f, -0.1f), new Vector2(-0.1f, 0.35f), new Vector2(0.25f, 0.7f), new Vector2(0.8f, 0.8f), new Vector2(1.25f, 0.5f), new Vector2(1.48f, -0.18f), new Vector2(0.6f, -0.12f) }, 0.04f, sx);
        for (int i = 0; i < 5; i++) k.Box("bucket_wear", new Vector3(-0.44f + i * 0.22f, -0.2f, 1.58f), new Vector3(0.09f, 0.08f, 0.26f), Quaternion.FromEuler(new Vector3(0.49f, 0, 0)), 0.015f);
        k.Box("paint_yellow", new Vector3(0, 0.32f, -0.12f), new Vector3(0.3f, 0.3f, 0.35f), 0.04f);
        k.Build(bucketT, "Löffel");
        ToolBody(bucketT, new Vector3(0, 0.3f, 0.75f), new Vector3(W, 0.9f, 1.4f));
        var kc = new MeshKit();
        kc.Sphere("soil_chunk", Vector3.Zero, 1f, 14);
        var cont = kc.Build(bucketT, "Inhalt");
        cont.Position = new Vector3(0, 0.25f, 0.75f);
        cont.Visible = false;
        tool = new SoilTool
        {
            t = bucketT, owner = this, cap = 1.0f, width = W, power = 140f,
            tipL = new Vector3(0, -0.24f, 1.66f), heelL = new Vector3(0, 0.72f, 0.8f),
            openL = new Vector3(0, -1, 0.15f).Normalized(), tipDirL = new Vector3(0, -0.45f, 1).Normalized(),
            content = cont, contentScale = new Vector3(0.5f, 0.42f, 0.6f),
        };
        cBoomL = new HydCylinder(this, 0.11f, 0.065f, 1.4f);
        cBoomR = new HydCylinder(this, 0.11f, 0.065f, 1.4f);
        cStick = new HydCylinder(this, 0.12f, 0.07f, 1.6f);
        cBucket = new HydCylinder(this, 0.1f, 0.06f, 1.2f);
        tracks = new TrackDrive();
        ApplyJoints();
    }

    public static void BeamTool(Node3D parent, Vector3 a, Vector3 b, float w, float h)
    {
        var rot = new Basis(MeshKit.LookRot((b - a).Normalized(), Vector3.Up));
        ToolBody(parent, (a + b) * 0.5f, new Vector3(w, h, (b - a).Length()), rot);
    }

    /// <summary>Rundumkennleuchte mit blinkendem Licht (nur am aktiven Fahrzeug).</summary>
    public static void Beacon(Node3D parent, Vector3 pos, VehicleBase owner)
    {
        var k = new MeshKit();
        k.Cyl("black_plastic", pos, 0.1f, 0.1f, 0.05f, 12, Quaternion.Identity);
        k.Build(parent, "Leuchtensockel");
        var bm = Mats.Copy("beacon");
        k.Cyl("beacon", pos + new Vector3(0, 0.1f, 0), 0.07f, 0.085f, 0.15f, 12, Quaternion.Identity);
        var mi = k.Build(parent, "Rundumleuchte", false);
        mi.MaterialOverride = bm;
        var light = new OmniLight3D { Position = pos + new Vector3(0, 0.15f, 0), LightColor = new Color(1f, 0.55f, 0.1f), OmniRange = 7f, LightEnergy = 0f, ShadowEnabled = false };
        parent.AddChild(light);
        var blink = new BeaconBlink { light = light, mat = bm, owner = owner };
        parent.AddChild(blink);
    }

    void ApplyJoints()
    {
        upper.Rotation = new Vector3(0, swing.pos, 0);
        boomT.Rotation = new Vector3(-boom.pos, 0, 0);
        stickT.Rotation = new Vector3(stick.pos, 0, 0);
        bucketT.Rotation = new Vector3(buck.pos, 0, 0);
        upperShape.Transform = upper.Transform * new Transform3D(Basis.Identity, new Vector3(0, 0.65f, -0.5f));
        cabShape.Transform = upper.Transform * new Transform3D(Basis.Identity, new Vector3(0.82f, 1.25f, 0.55f));
    }

    public override ControlsInfo Controls()
    {
        bool iso = Settings.Iso;
        var c = new ControlsInfo();
        if (workMode) { c.lUp = "Stiel aus"; c.lDown = "Stiel ein"; c.lLeft = "Drehen L"; c.lRight = "Drehen R"; }
        else { c.lUp = "Vor"; c.lDown = "Zurück"; c.lLeft = "Links"; c.lRight = "Rechts"; }
        c.rUp = iso ? "Ausleger ab" : "Ausleger auf"; c.rDown = iso ? "Ausleger auf" : "Ausleger ab";
        c.rLeft = "Löffel ein"; c.rRight = "Löffel aus";
        c.buttons.Add(("mode", workMode ? "Fahren" : "Arbeiten", "Modus"));
        return c;
    }

    public override bool OnButton(string id)
    {
        if (id != "mode") return false;
        workMode = !workMode;
        Hud.Toast(workMode ? "Arbeitsmodus: Oberwagen, Ausleger, Stiel, Löffel" : "Fahrmodus: Kettenfahrwerk", ToastKind.Info);
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var inp = isActive ? input : ControlInput.None;
        float thr = 0, st = 0, sw = 0, sk = 0;
        if (workMode) { sw = -inp.lx; sk = -inp.ly; } else { thr = inp.ly; st = inp.lx; }
        float bm = Settings.Iso ? -inp.ry : inp.ry;
        float bk = -inp.rx;
        float demand = MathF.Abs(sw) + MathF.Abs(sk) + MathF.Abs(bm) + MathF.Abs(bk) + (MathF.Abs(thr) + MathF.Abs(st)) * 0.8f;
        float share = demand > 1.6f ? 1.6f / demand : 1f;
        float slow = 1f - tool.resist * 0.6f;
        float pb = boom.pos, ps = stick.pos, pk = buck.pos, pw = swing.pos;
        swing.Step(sw, share, dt);
        boom.Step(bm, share * slow, dt);
        stick.Step(sk, share * slow, dt);
        buck.Step(bk, share * slow, dt);
        ApplyJoints();
        tool.ComputePoints(dt);
        bool tooDeep = tool.tip.Y < GlobalPosition.Y - 6.6f;
        tool.Step(dt);
        bool blocked = tooDeep || tool.blocked || tool.WouldBlock();
        relief = U.Damp(relief, blocked ? 1f : 0f, 8f, dt);
        if (blocked)
        {
            bool boomDown = boom.pos < pb;
            swing.pos = pw; boom.pos = pb; stick.pos = ps; buck.pos = pk;
            swing.vel *= -0.1f; boom.vel = 0; stick.vel = 0; buck.vel = 0;
            ApplyJoints();
            tool.ComputePoints(0);
            if (boomDown && !tool.blocked) liftForce = U.Approach(liftForce, 165000f * MathF.Abs(bm), 400000f * dt);
        }
        else liftForce = U.Approach(liftForce, 0, 600000f * dt);
        if (liftForce > 1f) U.ForceAt(this, Vector3.Up * liftForce, tool.tip);
        tool.CommitDepth();
        if (tool.resist > 0.05f && tool.vel.LengthSquared() > 0.001f) U.ForceAt(this, -tool.vel.Normalized() * tool.resist * 70000f, tool.tip);
        tracks.Step(thr, st, dt);
        UpdateCom();
        hydFlow = U.Clamp01(demand / 1.6f);
        float drv = (MathF.Abs(tracks.beltL) + MathF.Abs(tracks.beltR)) / 3f;
        UpdateEngine(0.12f + hydFlow * 0.8f + drv * 0.6f + relief * 0.3f, tool.resist + relief * 0.8f + hydFlow * 0.3f + tracks.tractionUsed * 0.5f, dt);
        if (engine != null) engine.trackSpeed = MathF.Max(MathF.Abs(tracks.beltL), MathF.Abs(tracks.beltR));
        ComputeStability(2.2f, 1.5f);
    }

    void UpdateCom()
    {
        var inv = GlobalTransform.AffineInverse();
        Vector3 sum = new Vector3(0, 0.7f, 0) * 9200f;
        float m = 9200f;
        void Add(Vector3 w, float mass) { sum += (inv * w) * mass; m += mass; }
        Add(upper.GlobalTransform * new Vector3(0, 0.7f, -1.1f), 8300f);
        Add(boomT.GlobalTransform * new Vector3(0, 0.8f, 2.9f), 1900f);
        Add(stickT.GlobalTransform * new Vector3(0, 0.1f, 1.2f), 950f);
        Add(bucketT.GlobalTransform * new Vector3(0, 0.3f, 0.8f), 850f + tool.LoadMassKg);
        CenterOfMass = sum / m;
        Mass = m;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        Vector3 Wp(Node3D t, Vector3 p) => t.GlobalTransform * p;
        cBoomL.Update(Wp(upper, aBoomBaseL), Wp(boomT, aBoomMidL));
        cBoomR.Update(Wp(upper, aBoomBaseR), Wp(boomT, aBoomMidR));
        cStick.Update(Wp(boomT, aStickBase), Wp(stickT, aStickEnd));
        cBucket.Update(Wp(stickT, aBuckBase), Wp(bucketT, aBuckEnd));
        trackMatL.Uv1Offset = new Vector3(tracks.distL / 0.22f, 0, 0);
        trackMatR.Uv1Offset = new Vector3(tracks.distR / 0.22f, 0, 0);
    }

    public override Vector3 CameraTarget()
    {
        var b = GlobalPosition + Vector3.Up * 2.0f;
        var t = tool.tip;
        return b.Lerp(new Vector3(t.X, MathF.Max(t.Y, GlobalPosition.Y) + 1f, t.Z), 0.45f);
    }
    public override float CameraYaw() { var f = workMode ? upper.GlobalBasis.Z : GlobalBasis.Z; return MathF.Atan2(f.X, f.Z); }

    public override List<(string, string, string)> HudLines()
    {
        float depth = tool.tip.Y - GlobalPosition.Y;
        string sub = tool.spillT > 0 && tool.load > 0.02f ? "verliert Material!" : tool.load > 0.02f ? MatName(tool.mat) : "leer";
        return new()
        {
            ("Löffel", $"{U.Fmt(tool.load, 2)} / {U.Fmt(tool.cap)} m³", sub),
            ("Schneidkante", (depth >= 0 ? "+" : "") + U.Fmt(depth, 2) + " m", "zur Standfläche"),
            ("Hydraulik", relief > 0.5f ? "Druckbegrenzung" : $"{(int)(hydFlow * 100)} %", workMode ? "Arbeiten" : "Fahren"),
        };
    }

    public static string MatName(SoilMat m) => m switch { SoilMat.Rubble => "Bauschutt", SoilMat.Gravel => "Schotter", SoilMat.Concrete => "Beton", _ => "Erde" };
}

/// <summary>Blinkende Rundumleuchte.</summary>
public partial class BeaconBlink : Node
{
    public OmniLight3D light;
    public StandardMaterial3D mat;
    public VehicleBase owner;
    float t = U.Rand(0, 6);
    public override void _Process(double delta)
    {
        t += (float)delta * 7f;
        bool on = owner != null && owner.isActive && MathF.Sin(t) > 0.2f;
        if (light != null) light.LightEnergy = on ? 2.5f : 0f;
        if (mat != null) mat.EmissionEnergyMultiplier = on ? 6f : 0.3f;
    }
}
