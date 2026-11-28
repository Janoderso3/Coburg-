using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>Planierraupe: Schild schält Boden ab und schiebt ihn als Wall vor sich her; der Schubwiderstand wirkt als Kraft.</summary>
public partial class Dozer : VehicleBase
{
    Node3D frameT, bladeT;
    HydJoint blade, tilt;
    TrackDrive tracks;
    StandardMaterial3D trackMatL, trackMatR;
    HydCylinder cL, cR;
    public float pushVol;
    Vector3 edge;
    const float BW = 3.4f;

    public static Dozer Create(Node parent, Vector3 pos, float yaw)
    {
        var d = new Dozer { Name = "Planierraupe" };
        d.Build();
        parent.AddChild(d);
        d.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
        d.spawn = d.GlobalTransform;
        d.tracks.Init(d, 1.9f, 2.9f, 0.05f, 2.7f, 6);
        d.tracks.mu = 0.85f; d.tracks.muLat = 0.75f; d.tracks.accel = 1.2f;
        return d;
    }

    void Build()
    {
        displayName = "Planierraupe 20 t"; shortName = "Raupe"; camDistance = 12f;
        SetupBody(20000f, new Vector3(0, 0.95f, 0.1f), BoxInertia(20000f, 3.2f, 3.2f, 5f));
        SetupEngine(6, 750, 1950, 85f, 310f, 1.1f);
        blade = new HydJoint(0.3f, -0.45f, 1.1f, 0.35f, 2.5f);
        tilt = new HydJoint(0, -0.1f, 0.1f, 0.12f, 1f);
        var k = new MeshKit();
        var body = new Node3D { Name = "Rumpf" }; AddChild(body);
        foreach (float sx in new[] { -0.95f, 0.95f })
        {
            k.Box("dark_metal", new Vector3(sx, 0.45f, 0), new Vector3(0.35f, 0.45f, 2.9f), 0.04f);
            k.CylX("dark_metal", new Vector3(sx, 0.95f, -1.3f), 0.42f, 0.5f, 18);
            k.CylX("steel", new Vector3(sx, 0.38f, 1.45f), 0.32f, 0.5f, 18);
            for (int i = 0; i < 5; i++) k.CylX("steel", new Vector3(sx, 0.15f, -1.1f + i * 0.55f), 0.12f, 0.45f, 10);
        }
        k.Box("dark_metal", new Vector3(0, 0.9f, 0), new Vector3(1.6f, 0.6f, 3.2f), 0.05f);
        k.Box("paint_yellow", new Vector3(0, 1.6f, 0.6f), new Vector3(1.45f, 1.05f, 2.0f), 0.1f);
        k.Box("black_plastic", new Vector3(0, 1.55f, 1.62f), new Vector3(1.2f, 0.6f, 0.1f), 0.02f);
        for (int i = 0; i < 8; i++) k.Box("dark_metal", new Vector3(-0.5f + i * 0.143f, 1.55f, 1.665f), new Vector3(0.04f, 0.55f, 0.04f));
        k.Box("paint_yellow", new Vector3(0, 1.25f, -0.6f), new Vector3(2.2f, 0.15f, 2.3f), 0.04f);
        k.Box("paint_yellow", new Vector3(0, 1.4f, -0.8f), new Vector3(1.5f, 0.2f, 1.4f), 0.04f);
        k.Box("paint_yellow", new Vector3(0, 3.2f, -0.85f), new Vector3(1.55f, 0.1f, 1.45f), 0.04f);
        foreach (var (x, z) in new[] { (0.72f, -0.15f), (-0.72f, -0.15f), (0.72f, -1.55f), (-0.72f, -1.55f) }) k.Box("paint_yellow", new Vector3(x, 2.3f, z), new Vector3(0.07f, 1.8f, 0.07f));
        k.Box("seat", new Vector3(0, 1.8f, -1.0f), new Vector3(0.5f, 0.6f, 0.5f), 0.06f);
        k.Box("dark_metal", new Vector3(0, 1.0f, -1.75f), new Vector3(1.5f, 0.5f, 0.4f), 0.04f);
        k.Box("dark_metal", new Vector3(0, 0.9f, -2.1f), new Vector3(1.4f, 0.2f, 0.4f), 0.03f);
        k.Box("bucket_wear", new Vector3(0, 0.45f, -2.25f), new Vector3(0.12f, 0.9f, 0.15f), 0.02f);
        k.Cyl("steel", new Vector3(-0.45f, 2.55f, 0.6f), 0.06f, 0.06f, 0.8f, 10, Quaternion.Identity);
        k.Build(body, "Rumpf");
        var g = new MeshKit();
        g.Box("glass", new Vector3(0, 2.3f, -0.85f), new Vector3(1.38f, 1.7f, 1.33f));
        g.Build(body, "Scheiben", false);
        Excavator.Beacon(body, new Vector3(-0.5f, 3.27f, -1.2f), this);
        trackMatL = Mats.Copy("track"); trackMatR = Mats.Copy("track");
        foreach (float sx in new[] { 0.95f, -0.95f })
        {
            k.TrackBelt("track", sx, 0.5f, 3.4f, 0.45f, 0.5f);
            k.Build(body, "Kette").MaterialOverride = sx > 0 ? trackMatL : trackMatR;
        }
        cabEye = Marker(body, "Fahrerauge", new Vector3(0, 2.5f, -0.8f));
        exhaust = Marker(body, "Auspuff", new Vector3(-0.45f, 2.98f, 0.6f));
        AddBox(new Vector3(0, 1.2f, 0.1f), new Vector3(1.7f, 1.3f, 3.6f));
        AddBox(new Vector3(-0.95f, 0.65f, 0), new Vector3(0.5f, 0.6f, 3.3f));
        AddBox(new Vector3(0.95f, 0.65f, 0), new Vector3(0.5f, 0.6f, 3.3f));
        AddBox(new Vector3(0, 2.3f, -0.85f), new Vector3(1.5f, 1.8f, 1.45f));
        frameT = new Node3D { Name = "Schubrahmen", Position = new Vector3(0, 0.45f, -0.2f) }; AddChild(frameT);
        foreach (float sx in new[] { -1.18f, 1.18f }) k.Beam("paint_yellow", new Vector3(sx, 0, 0), new Vector3(sx, 0.1f, 2.25f), 0.18f, 0.25f, Vector3.Up, 0.03f);
        k.Build(frameT, "Schubrahmen");
        bladeT = new Node3D { Name = "Schild", Position = new Vector3(0, 0.1f, 2.35f) }; frameT.AddChild(bladeT);
        k.ProfileX("paint_yellow", new[] { new Vector2(0f, -0.55f), new Vector2(0.18f, -0.55f), new Vector2(0.12f, -0.2f), new Vector2(0.18f, 0.25f), new Vector2(0.32f, 0.65f), new Vector2(0.38f, 0.75f), new Vector2(0.22f, 0.78f), new Vector2(-0.02f, 0.4f), new Vector2(-0.1f, 0f), new Vector2(-0.08f, -0.5f) }, BW);
        foreach (float sx in new[] { -BW / 2, BW / 2 }) k.ProfileX("paint_yellow", new[] { new Vector2(-0.1f, -0.5f), new Vector2(0.2f, -0.55f), new Vector2(0.38f, 0.75f), new Vector2(-0.05f, 0.6f) }, 0.05f, sx);
        k.Box("bucket_wear", new Vector3(0, -0.52f, 0.18f), new Vector3(BW, 0.1f, 0.06f));
        k.Build(bladeT, "Schild");
        ToolBody(bladeT, new Vector3(0, 0.1f, 0.1f), new Vector3(BW, 1.3f, 0.4f));
        cL = new HydCylinder(this, 0.08f, 0.05f, 1.0f);
        cR = new HydCylinder(this, 0.08f, 0.05f, 1.0f);
        tracks = new TrackDrive();
        ApplyJoints();
    }

    void ApplyJoints()
    {
        float a = MathF.Asin(Math.Clamp(blade.pos / 2.35f, -0.5f, 0.6f));
        frameT.Rotation = new Vector3(-a, 0, 0);
        bladeT.Rotation = new Vector3(a, 0, tilt.pos);
    }

    public override ControlsInfo Controls()
    {
        bool iso = Settings.Iso;
        return new ControlsInfo { lUp = "Vor", lDown = "Zurück", lLeft = "Links", lRight = "Rechts", rUp = iso ? "Schild ab" : "Schild auf", rDown = iso ? "Schild auf" : "Schild ab", rLeft = "Neigen L", rRight = "Neigen R" };
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var inp = isActive ? input : ControlInput.None;
        float bl = Settings.Iso ? -inp.ry : inp.ry;
        blade.Step(bl, 1f, dt);
        tilt.Step(-inp.rx, 1f, dt);
        ApplyJoints();
        float resist = U.Clamp01(pushVol / 10f);
        tracks.Step(inp.ly, inp.lx, dt, Mathf.Lerp(1f, 0.55f, resist));
        DoBlade(dt);
        hydFlow = MathF.Abs(bl) * 0.7f;
        float drv = MathF.Max(MathF.Abs(tracks.beltL), MathF.Abs(tracks.beltR)) / tracks.maxSpeed;
        UpdateEngine(0.12f + drv * 0.75f + hydFlow * 0.4f, tracks.tractionUsed * 0.9f + resist * 0.5f, dt);
        if (engine != null) { engine.trackSpeed = MathF.Max(MathF.Abs(tracks.beltL), MathF.Abs(tracks.beltR)); engine.beeper = tracks.Speed < -0.1f; }
        ComputeStability(1.7f, 1.2f);
    }

    void DoBlade(float dt)
    {
        var T = SoilTerrain.I;
        var m = bladeT.GlobalTransform;
        var fwd = U.Flat(GlobalBasis.Z).Normalized();
        float sp = LinearVelocity.Dot(fwd);
        bool moving = MathF.Abs(sp) > 0.03f;
        const int n = 9;
        float vol = 0, front = 0;
        var sideW = m.Basis.X.Normalized();
        for (int i = 0; i < n; i++)
        {
            float lx = -BW / 2 + BW * (i + 0.5f) / n;
            var p = m * new Vector3(lx, -0.55f, 0.15f);
            if (i == n / 2) edge = p;
            float gnd = T.Height(p.X, p.Z);
            if (gnd > p.Y + 0.01f && moving)
            {
                float seg = BW / n / 2;
                var r = T.CutSegment(p - sideW * seg, p + sideW * seg, p.Y, 0.3f, 4f);
                if (r.vol > 0)
                {
                    float dir = sp >= 0 ? 1 : -1;
                    T.Deposit(p.X + fwd.X * 0.75f * dir, p.Z + fwd.Z * 0.75f * dir, r.vol, r.mat, 0.4f);
                    vol += r.vol;
                }
            }
            var fp = p + fwd * 0.6f;
            front += MathF.Max(0, T.Height(fp.X, fp.Z) - p.Y) * 0.6f * (BW / n);
        }
        pushVol = U.Damp(pushVol, front * 1.5f, 4f, dt);
        if (pushVol > 0.05f && moving) U.ForceAt(this, -fwd * MathF.Sign(sp) * pushVol * 1700f * 9.81f * 0.55f, edge + Vector3.Up * 0.4f);
        if (vol > 0.002f && U.Rand() < 0.15f) SoilFX.I.Dust(edge + fwd + Vector3.Up * 0.5f, 1, SoilFX.DustColor(SoilMat.Soil), 1f);
        if (Buildings.I != null)
        {
            var vel = fwd * sp;
            foreach (float lx in new[] { -1.2f, 0f, 1.2f })
            {
                var res = Buildings.I.ToolHit(m * new Vector3(lx, 0.1f, 0.1f), 0.6f, vel, 55f, dt);
                if (res.hit > 0) hitWarning = 0.3f;
            }
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        cL.Update(GlobalTransform * new Vector3(0.75f, 1.75f, 1.35f), bladeT.GlobalTransform * new Vector3(0.75f, 0.3f, 0.05f));
        cR.Update(GlobalTransform * new Vector3(-0.75f, 1.75f, 1.35f), bladeT.GlobalTransform * new Vector3(-0.75f, 0.3f, 0.05f));
        trackMatL.Uv1Offset = new Vector3(tracks.distL / 0.22f, 0, 0);
        trackMatR.Uv1Offset = new Vector3(tracks.distR / 0.22f, 0, 0);
    }

    public override List<(string, string, string)> HudLines()
    {
        float rel = edge.Y - GlobalPosition.Y;
        return new()
        {
            ("Schild", (rel >= 0 ? "+" : "") + U.Fmt(rel, 2) + " m", "zur Kettenauflage"),
            ("Schubvolumen", U.Fmt(pushVol) + " m³", tracks.slip > 0.4f ? "Ketten rutschen!" : pushVol > 5 ? "schwer" : ""),
            ("Tempo", U.Fmt(MathF.Abs(tracks.Speed) * 3.6f) + " km/h", tracks.Speed < -0.1f ? "rückwärts" : ""),
        };
    }
}
