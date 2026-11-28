using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>
/// Radlader mit Knicklenkung: Vorder- und Hinterachse lenken gegensinnig um das Gelenk (wie das Knicken des Rahmens),
/// Hubarm mit Parallelführung, Schaufel mit Ankippen/Auskippen.
/// </summary>
public partial class WheelLoader : VehicleBase
{
    Node3D frontT, armT, bucketT;
    HydJoint lift, tiltJ;
    public SoilTool tool;
    WheelSim ws;
    HydCylinder cLiftL, cLiftR, cTilt, cSteerL, cSteerR;

    public static WheelLoader Create(Node parent, Vector3 pos, float yaw)
    {
        var l = new WheelLoader { Name = "Radlader" };
        l.Build();
        parent.AddChild(l);
        l.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
        l.spawn = l.GlobalTransform;
        l.ws.Init(l);
        return l;
    }

    public static Node3D Wheel(Node3D parent, float r, float w, string rim = "paint_yellow")
    {
        var root = new Node3D { Name = "Rad" };
        parent.AddChild(root);
        var k = new MeshKit();
        var rot = Quaternion.FromEuler(new Vector3(0, 0, MathF.PI / 2));
        k.Cyl("tread", Vector3.Zero, r, r, w, 28, rot, false);
        k.Cyl("rubber", Vector3.Zero, r * 0.97f, r * 0.97f, w * 0.96f, 28, rot, true, "rubber");
        k.Cyl(rim, Vector3.Zero, r * 0.58f, r * 0.58f, w * 1.0f, 18, rot);
        k.Cyl("dark_metal", Vector3.Zero, r * 0.2f, r * 0.24f, w * 1.12f, 10, rot);
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * MathF.PI * 2f;
            var c = new Vector3(0, MathF.Sin(a) * r, MathF.Cos(a) * r);
            k.Box("rubber", c, new Vector3(w * 0.9f, 0.06f, 0.12f), Quaternion.FromEuler(new Vector3(-a, 0, 0)) * Quaternion.FromEuler(new Vector3(0, (i % 2 == 0 ? 1 : -1) * 0.5f, 0)), 0.01f);
        }
        k.Build(root, "Reifen");
        return root;
    }

    void Build()
    {
        displayName = "Radlader 18 t"; shortName = "Radlader"; camDistance = 13f;
        SetupBody(17500f, new Vector3(0, 1.0f, -0.4f), BoxInertia(17500f, 2.6f, 3f, 7f));
        SetupEngine(6, 750, 2200, 105f, 390f);
        lift = new HydJoint(-0.05f, -0.27f, 1.05f, 0.38f, 1.8f);
        tiltJ = new HydJoint(0.1f, -0.85f, 0.75f, 0.9f, 4f);
        var k = new MeshKit();
        // Hinterwagen (Ursprung = Knickgelenk)
        k.Box("dark_metal", new Vector3(0, 0.95f, -1.5f), new Vector3(1.0f, 0.5f, 3.0f), 0.04f);
        k.Box("paint_yellow", new Vector3(0, 1.6f, -2.35f), new Vector3(1.9f, 1.15f, 2.1f), 0.12f);
        k.Box("paint_yellow", new Vector3(0, 1.1f, -3.45f), new Vector3(2.2f, 0.55f, 0.6f), 0.12f);
        for (int i = 0; i < 7; i++) k.Box("black_plastic", new Vector3(-0.6f + i * 0.2f, 1.6f, -3.42f), new Vector3(0.08f, 0.45f, 0.02f));
        k.Box("dark_metal", new Vector3(0, 1.12f, -1.6f), new Vector3(2.35f, 0.12f, 2.6f), 0.03f);
        k.Box("paint_yellow", new Vector3(0, 1.38f, -0.75f), new Vector3(1.35f, 0.25f, 1.4f), 0.04f);
        k.Box("paint_yellow", new Vector3(0, 3.15f, -0.8f), new Vector3(1.4f, 0.1f, 1.5f), 0.04f);
        foreach (var (x, z) in new[] { (0.64f, -0.08f), (-0.64f, -0.08f), (0.64f, -1.5f), (-0.64f, -1.5f) }) k.Box("paint_yellow", new Vector3(x, 2.32f, z), new Vector3(0.07f, 1.65f, 0.07f));
        k.Box("seat", new Vector3(0, 1.85f, -0.9f), new Vector3(0.5f, 0.55f, 0.5f), 0.05f);
        k.Box("black_plastic", new Vector3(0, 2.18f, -2.9f), new Vector3(1.7f, 0.06f, 1.0f));
        k.Cyl("steel", new Vector3(0.45f, 2.55f, -2.6f), 0.07f, 0.07f, 0.7f, 10, Quaternion.Identity);
        k.Build(this, "Hinterwagen");
        var g = new MeshKit();
        g.Box("glass", new Vector3(0, 2.32f, -0.8f), new Vector3(1.25f, 1.55f, 1.35f));
        g.Build(this, "Scheiben", false);
        var lights = new MeshKit();
        lights.Box("headlight", new Vector3(0.5f, 3.05f, -0.06f), new Vector3(0.22f, 0.1f, 0.06f), 0.02f);
        lights.Box("headlight", new Vector3(-0.5f, 3.05f, -0.06f), new Vector3(0.22f, 0.1f, 0.06f), 0.02f);
        lights.Box("taillight", new Vector3(0.85f, 1.3f, -3.76f), new Vector3(0.15f, 0.12f, 0.04f), 0.01f);
        lights.Box("taillight", new Vector3(-0.85f, 1.3f, -3.76f), new Vector3(0.15f, 0.12f, 0.04f), 0.01f);
        lights.Build(this, "Leuchten", false);
        Excavator.Beacon(this, new Vector3(0, 3.22f, -1.1f), this);
        cabEye = Marker(this, "Fahrerauge", new Vector3(0, 2.45f, -0.75f));
        exhaust = Marker(this, "Auspuff", new Vector3(0.45f, 2.92f, -2.6f));
        AddBox(new Vector3(0, 1.55f, -2.2f), new Vector3(2.2f, 1.3f, 3.2f));
        AddBox(new Vector3(0, 2.3f, -0.8f), new Vector3(1.4f, 1.7f, 1.5f));
        AddBox(new Vector3(0, 1.1f, 1.2f), new Vector3(1.3f, 0.9f, 2.0f));
        // Vorderwagen (dreht sich sichtbar um das Gelenk)
        frontT = new Node3D { Name = "Vorderwagen" }; AddChild(frontT);
        k.Box("dark_metal", new Vector3(0, 0.95f, 1.1f), new Vector3(1.1f, 0.55f, 2.2f), 0.05f);
        k.Box("paint_yellow", new Vector3(0, 1.3f, 0.55f), new Vector3(1.3f, 0.5f, 0.9f), 0.06f);
        k.Box("dark_metal", new Vector3(0, 1.12f, 1.65f), new Vector3(2.35f, 0.12f, 1.1f), 0.03f);
        k.Build(frontT, "Vorderwagen");
        ws = new WheelSim { maxTorque = 1100f, finalDrive = 22f, gears = new[] { 3.2f, 1.7f, 0.95f, 0.6f }, reverseGear = 3.0f, speedLimit = 9.5f, maxRpm = 2200, idle = 750, shiftUp = 2000, shiftDown = 1100, maxSteerDeg = 20f, mu = 0.85f };
        float r = 0.8f;
        foreach (float z in new[] { 1.65f, -1.65f })
            foreach (float sx in new[] { -1f, 1f })
            {
                var vis = Wheel(this, r, 0.6f);
                var w = ws.Add(new Vector3(sx * 1.0f, r + 0.15f, z), r, 0.18f, 17500f / 4f, true, true, vis);
                w.steerSign = z > 0 ? 1f : -1f;
            }
        armT = new Node3D { Name = "Hubarm", Position = new Vector3(0, 2.0f, 0.65f) }; frontT.AddChild(armT);
        foreach (float sx in new[] { -0.62f, 0.62f }) k.Beam("paint_yellow", new Vector3(sx, 0, 0), new Vector3(sx, -0.35f, 2.75f), 0.2f, 0.42f, Vector3.Up, 0.04f);
        k.Box("paint_yellow", new Vector3(0, -0.2f, 1.4f), new Vector3(1.4f, 0.3f, 0.3f), 0.04f);
        k.CylX("steel", Vector3.Zero, 0.1f, 1.5f, 12);
        k.Build(armT, "Hubarm");
        Excavator.BeamTool(armT, Vector3.Zero, new Vector3(0, -0.35f, 2.75f), 1.45f, 0.42f);
        bucketT = new Node3D { Name = "Schaufel", Position = new Vector3(0, -0.35f, 2.75f) }; armT.AddChild(bucketT);
        float W = 2.7f;
        k.ProfileX("paint_yellow_worn", new[] { new Vector2(0.05f, 0.25f), new Vector2(-0.1f, 0f), new Vector2(-0.1f, -0.7f), new Vector2(0.1f, -1.0f), new Vector2(0.75f, -1.08f), new Vector2(1.15f, -1.1f), new Vector2(1.15f, -1.0f), new Vector2(0.75f, -0.98f), new Vector2(0.15f, -0.9f), new Vector2(0.02f, -0.65f), new Vector2(0.02f, 0.05f), new Vector2(0.12f, 0.3f) }, W);
        foreach (float sx in new[] { -W / 2, W / 2 }) k.ProfileX("paint_yellow_worn", new[] { new Vector2(-0.1f, 0.3f), new Vector2(-0.12f, -0.72f), new Vector2(0.1f, -1.05f), new Vector2(1.15f, -1.1f), new Vector2(0.6f, 0f) }, 0.05f, sx);
        k.Box("bucket_wear", new Vector3(0, -1.08f, 1.12f), new Vector3(W, 0.06f, 0.2f), 0.01f);
        k.Build(bucketT, "Schaufel");
        ToolBody(bucketT, new Vector3(0, -0.6f, 0.5f), new Vector3(W, 1.0f, 1.2f));
        var kc = new MeshKit();
        kc.Sphere("soil_chunk", Vector3.Zero, 1f, 14);
        var cont = kc.Build(bucketT, "Inhalt");
        cont.Position = new Vector3(0, -0.6f, 0.6f);
        cont.Visible = false;
        tool = new SoilTool
        {
            t = bucketT, owner = this, cap = 2.8f, width = W, power = 45f,
            tipL = new Vector3(0, -1.08f, 1.2f), heelL = new Vector3(0, -0.95f, 0.1f),
            openL = new Vector3(0, 0.55f, 1).Normalized(), tipDirL = new Vector3(0, -0.05f, 1).Normalized(),
            content = cont, contentScale = new Vector3(1.25f, 0.5f, 0.6f),
        };
        cLiftL = new HydCylinder(this, 0.1f, 0.06f, 1.0f);
        cLiftR = new HydCylinder(this, 0.1f, 0.06f, 1.0f);
        cTilt = new HydCylinder(this, 0.12f, 0.07f, 1.1f);
        cSteerL = new HydCylinder(this, 0.06f, 0.035f, 0.45f, "dark_metal");
        cSteerR = new HydCylinder(this, 0.06f, 0.035f, 0.45f, "dark_metal");
        ApplyJoints();
    }

    void ApplyJoints()
    {
        frontT.Rotation = new Vector3(0, Mathf.DegToRad(ws?.steerAngle ?? 0) * 2f, 0);
        armT.Rotation = new Vector3(-lift.pos, 0, 0);
        bucketT.Rotation = new Vector3(lift.pos - tiltJ.pos, 0, 0);
    }

    public override ControlsInfo Controls()
    {
        bool iso = Settings.Iso;
        return new ControlsInfo { lUp = "Gas", lDown = "Zurück", lLeft = "Lenken L", lRight = "Lenken R", rUp = iso ? "Senken" : "Heben", rDown = iso ? "Heben" : "Senken", rLeft = "Ankippen", rRight = "Auskippen" };
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var inp = isActive ? input : ControlInput.None;
        float lf = Settings.Iso ? -inp.ry : inp.ry;
        float tl = -inp.rx;
        float pl = lift.pos, pt = tiltJ.pos;
        lift.Step(lf, 1f - tool.resist * 0.5f, dt);
        tiltJ.Step(tl, 1f, dt);
        ws.Step(inp.ly, inp.lx, dt);
        ws.Wear(dt);
        ApplyJoints();
        tool.ComputePoints(dt);
        tool.Step(dt);
        bool blocked = tool.blocked || tool.WouldBlock();
        relief = U.Damp(relief, blocked ? 1 : 0, 8f, dt);
        if (blocked)
        {
            lift.pos = pl; tiltJ.pos = pt; lift.vel = tiltJ.vel = 0;
            ApplyJoints();
            tool.ComputePoints(0);
        }
        tool.CommitDepth();
        float fwdV = LinearVelocity.Dot(GlobalBasis.Z);
        if ((tool.resist > 0.05f || blocked) && fwdV > 0.05f)
        {
            float F = blocked ? 260000f : tool.resist * 140000f;
            F = MathF.Min(F, Mass * fwdV / dt * 0.5f + 60000f);
            U.ForceAt(this, -GlobalBasis.Z * F, tool.tip);
        }
        CenterOfMass = new Vector3(0, 1.0f + lift.pos * 0.3f, -0.4f + tool.load * 0.12f);
        Mass = 17500f + tool.LoadMassKg;
        hydFlow = U.Clamp01(MathF.Abs(lf) + MathF.Abs(tl) * 0.6f + MathF.Abs(inp.lx) * 0.3f);
        UpdateEngine(MathF.Max(ws.throttleOut, hydFlow * 0.8f) + 0.08f, tool.resist + relief * 0.8f + ws.throttleOut * 0.4f, dt);
        if (engine != null) { engine.rpm = MathF.Max(rpm, ws.rpm); engine.beeper = ws.reversing; }
        ComputeStability(1.9f, 1.1f);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        var gt = GlobalTransform;
        var ft = frontT.GlobalTransform;
        cLiftL.Update(ft * new Vector3(0.62f, 1.0f, 0.2f), armT.GlobalTransform * new Vector3(0.62f, -0.3f, 1.4f));
        cLiftR.Update(ft * new Vector3(-0.62f, 1.0f, 0.2f), armT.GlobalTransform * new Vector3(-0.62f, -0.3f, 1.4f));
        cTilt.Update(ft * new Vector3(0, 1.55f, 0.95f), bucketT.GlobalTransform * new Vector3(0, 0.15f, -0.05f));
        cSteerL.Update(gt * new Vector3(0.55f, 1.0f, -0.6f), ft * new Vector3(0.5f, 1.0f, 0.35f));
        cSteerR.Update(gt * new Vector3(-0.55f, 1.0f, -0.6f), ft * new Vector3(-0.5f, 1.0f, 0.35f));
    }

    public override Vector3 CameraTarget() => GlobalPosition + GlobalBasis.Z * 1.5f + Vector3.Up * 2.0f;

    public override List<(string, string, string)> HudLines()
    {
        string sub = tool.spillT > 0 && tool.load > 0.02f ? "verliert Material!" : tool.load > 0.02f ? Excavator.MatName(tool.mat) : "leer";
        return new()
        {
            ("Schaufel", $"{U.Fmt(tool.load, 2)} / {U.Fmt(tool.cap)} m³", sub),
            ("Tempo", U.Fmt(MathF.Abs(SpeedKmh)) + " km/h", ws.gear == -1 ? "Rückwärts" : $"Gang {Math.Max(1, ws.gear)}"),
            ("Hubhöhe", U.Fmt(tool.tip.Y - GlobalPosition.Y) + " m", "Schneidkante"),
        };
    }
}
