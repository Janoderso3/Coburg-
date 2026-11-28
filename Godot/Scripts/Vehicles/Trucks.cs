using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>Dreiachs-Lkw (6x4) mit Fahrerhaus, Automatikgetriebe, Druckluftbremse.</summary>
public abstract partial class TruckBase : VehicleBase
{
    protected WheelSim ws;
    bool wasMoving;
    public float baseMass;

    protected void BuildChassis(string cabMat, float massKg)
    {
        baseMass = massKg;
        SetupBody(massKg, new Vector3(0, 1.0f, 0.2f), BoxInertia(massKg, 2.5f, 2.5f, 8.5f));
        SetupEngine(6, 650, 1900, 72f, 260f, 0.95f);
        var k = new MeshKit();
        k.Box("dark_metal", new Vector3(0.45f, 0.95f, -0.3f), new Vector3(0.22f, 0.3f, 8.0f), 0.02f);
        k.Box("dark_metal", new Vector3(-0.45f, 0.95f, -0.3f), new Vector3(0.22f, 0.3f, 8.0f), 0.02f);
        for (int i = 0; i < 6; i++) k.Box("dark_metal", new Vector3(0, 0.95f, -3.8f + i * 1.4f), new Vector3(0.9f, 0.12f, 0.12f));
        foreach (float z in new[] { 3.3f, -1.5f, -2.85f }) k.CylX("dark_metal", new Vector3(0, 0.55f, z), 0.08f, 2.0f, 8);
        k.Box(cabMat, new Vector3(0, 2.15f, 3.15f), new Vector3(2.45f, 1.75f, 1.9f), 0.12f);
        k.Box(cabMat, new Vector3(0, 3.2f, 3.05f), new Vector3(2.45f, 0.45f, 2.0f), 0.14f);
        k.Box("black_plastic", new Vector3(0, 1.35f, 4.12f), new Vector3(2.4f, 0.5f, 0.14f), 0.04f);
        k.Box("dark_metal", new Vector3(0, 1.85f, 4.13f), new Vector3(2.3f, 0.45f, 0.08f), 0.02f);
        for (int i = 0; i < 6; i++) k.Box("black_plastic", new Vector3(0, 1.7f + i * 0.06f, 4.18f), new Vector3(2.0f, 0.02f, 0.02f));
        k.Box(cabMat, new Vector3(0, 1.2f, 3.35f), new Vector3(2.5f, 0.35f, 1.4f), 0.06f);
        k.Box("black_plastic", new Vector3(1.35f, 2.6f, 3.95f), new Vector3(0.08f, 0.45f, 0.25f), 0.02f);
        k.Box("black_plastic", new Vector3(-1.35f, 2.6f, 3.95f), new Vector3(0.08f, 0.45f, 0.25f), 0.02f);
        k.Cyl("steel", new Vector3(0.8f, 1.15f, 1.5f), 0.32f, 0.32f, 1.0f, 16, Quaternion.FromEuler(new Vector3(MathF.PI / 2, 0, 0)));
        k.Box("black_plastic", new Vector3(-0.8f, 1.15f, 1.6f), new Vector3(0.5f, 0.45f, 0.8f), 0.03f);
        k.Cyl("steel", new Vector3(-0.9f, 2.8f, 2.25f), 0.07f, 0.07f, 1.3f, 10, Quaternion.Identity);
        foreach (float sx in new[] { -1.05f, 1.05f })
        {
            k.Box("black_plastic", new Vector3(sx, 1.15f, 3.3f), new Vector3(0.5f, 0.06f, 1.2f), 0.02f);
            k.Box("black_plastic", new Vector3(sx, 1.15f, -2.17f), new Vector3(0.5f, 0.06f, 2.6f), 0.02f);
        }
        k.Build(this, "Fahrgestell");
        var g = new MeshKit();
        g.Box("glass", new Vector3(0, 2.55f, 4.09f), new Vector3(2.3f, 0.85f, 0.06f), Quaternion.FromEuler(new Vector3(-0.09f, 0, 0)));
        g.Box("glass", new Vector3(0, 2.55f, 3.35f), new Vector3(2.48f, 0.7f, 1.1f));
        g.Build(this, "Scheiben", false);
        var l = new MeshKit();
        foreach (float sx in new[] { -0.85f, 0.85f })
        {
            l.Box("headlight", new Vector3(sx, 1.65f, 4.18f), new Vector3(0.35f, 0.18f, 0.05f), 0.02f);
            l.Box("taillight", new Vector3(sx * 1.2f, 1.0f, -4.3f), new Vector3(0.25f, 0.15f, 0.05f), 0.02f);
        }
        l.Build(this, "Leuchten", false);
        Excavator.Beacon(this, new Vector3(0.6f, 3.45f, 3.4f), this);
        cabEye = Marker(this, "Fahrerauge", new Vector3(0.55f, 2.75f, 3.2f));
        exhaust = Marker(this, "Auspuff", new Vector3(-0.9f, 3.45f, 2.25f));
        AddBox(new Vector3(0, 2.2f, 3.15f), new Vector3(2.5f, 2.1f, 2.0f));
        AddBox(new Vector3(0, 1.0f, -0.3f), new Vector3(1.4f, 0.5f, 8.0f));
        ws = new WheelSim { maxTorque = 2100f, finalDrive = 5.3f, speedLimit = 12.5f, idle = 650, maxRpm = 1900, shiftUp = 1750, shiftDown = 1050, maxSteerDeg = 34f };
        float r = 0.53f;
        foreach (float z in new[] { 3.3f, -1.5f, -2.85f })
            foreach (float sx in new[] { -1f, 1f })
            {
                var vis = WheelLoader.Wheel(this, r, 0.42f, "steel");
                ws.Add(new Vector3(sx * 1.02f, r + 0.2f, z), r, 0.25f, (massKg + 12000f) / 6f, z > 0, z < 0, vis);
            }
    }

    protected void InitDrive() => ws.Init(this);

    protected void DriveStep(float dt, ControlInput inp, float extraLoad)
    {
        ws.Step(inp.ly, inp.lx, dt);
        ws.Wear(dt);
        bool moving = MathF.Abs(SpeedKmh) > 1f;
        if (wasMoving && !moving && engine != null) engine.brakeHiss = 1f;
        wasMoving = moving;
        UpdateEngine(MathF.Max(ws.throttleOut, 0.05f), ws.throttleOut * 0.7f + extraLoad, dt);
        if (engine != null) { engine.rpm = ws.rpm; engine.beeper = ws.reversing; }
    }

    protected string GearText => ws.gear == -1 ? "Rückwärts" : ws.gear == 0 ? "Neutral" : $"Gang {ws.gear}";
    public override ControlsInfo Controls() => new() { lUp = "Gas", lDown = "Bremse/Rück", lLeft = "Lenken L", lRight = "Lenken R" };
}

/// <summary>Kipper: Mulde mit Kipppunkt am Heck, Ladung als Volumen + Gewicht, Brocken rutschen physikalisch heraus.</summary>
public partial class DumpTruck : TruckBase, ISoilCatcher, ISoilReceiver
{
    Node3D bed, gate;
    HydJoint bedJ;
    public float cargo, cap = 12f;
    public SoilMat cargoMat = SoilMat.Soil;
    MeshInstance3D heap;
    HydCylinder ram;
    float spillAcc, clodAcc;
    const float L = 5.4f, Wd = 2.4f, H = 1.15f;
    readonly List<CollisionShape3D> bedShapes = new();
    readonly List<Transform3D> bedShapeLocal = new();

    public static DumpTruck Create(Node parent, Vector3 pos, float yaw)
    {
        var t = new DumpTruck { Name = "Kipper" };
        t.Build();
        parent.AddChild(t);
        t.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
        t.spawn = t.GlobalTransform;
        t.InitDrive();
        SoilFX.Catchers.Add(t);
        return t;
    }

    void Build()
    {
        displayName = "Kipper-Lkw 3-Achser"; shortName = "Kipper"; camDistance = 15f;
        BuildChassis("paint_white", 13000f);
        bedJ = new HydJoint(0, 0, 0.9f, 0.16f, 0.6f);
        bed = new Node3D { Name = "Mulde", Position = new Vector3(0, 1.22f, -3.85f) }; AddChild(bed);
        var k = new MeshKit();
        k.Box("paint_orange", new Vector3(0, 0.06f, L / 2 + 0.15f), new Vector3(Wd, 0.12f, L), 0.02f);
        k.Box("paint_orange", new Vector3(Wd / 2 - 0.04f, H / 2 + 0.1f, L / 2 + 0.15f), new Vector3(0.08f, H, L), 0.02f);
        k.Box("paint_orange", new Vector3(-Wd / 2 + 0.04f, H / 2 + 0.1f, L / 2 + 0.15f), new Vector3(0.08f, H, L), 0.02f);
        k.Box("paint_orange", new Vector3(0, (H + 0.35f) / 2 + 0.1f, L + 0.15f), new Vector3(Wd, H + 0.35f, 0.1f), 0.02f);
        k.Box("paint_orange", new Vector3(0, H + 0.4f, L + 0.45f), new Vector3(Wd, 0.1f, 0.6f), 0.02f);
        for (int i = 1; i < 5; i++)
        {
            k.Box("dark_metal", new Vector3(Wd / 2 + 0.03f, H / 2 + 0.1f, 0.15f + L * i / 5), new Vector3(0.1f, H * 0.9f, 0.1f), 0.02f);
            k.Box("dark_metal", new Vector3(-Wd / 2 - 0.03f, H / 2 + 0.1f, 0.15f + L * i / 5), new Vector3(0.1f, H * 0.9f, 0.1f), 0.02f);
        }
        k.Box("dark_metal", new Vector3(0, -0.05f, L / 2), new Vector3(0.9f, 0.12f, L - 0.2f));
        k.Build(bed, "Mulde");
        // Muldenkollision (Brocken bleiben liegen und rutschen beim Kippen heraus)
        void BedBox(Vector3 c, Vector3 s) { var cs = AddBox(Vector3.Zero, s); bedShapes.Add(cs); bedShapeLocal.Add(new Transform3D(Basis.Identity, c)); }
        BedBox(new Vector3(0, 0.06f, L / 2 + 0.15f), new Vector3(Wd, 0.12f, L));
        BedBox(new Vector3(Wd / 2 - 0.04f, H / 2 + 0.1f, L / 2 + 0.15f), new Vector3(0.08f, H, L));
        BedBox(new Vector3(-Wd / 2 + 0.04f, H / 2 + 0.1f, L / 2 + 0.15f), new Vector3(0.08f, H, L));
        BedBox(new Vector3(0, (H + 0.35f) / 2 + 0.1f, L + 0.15f), new Vector3(Wd, H + 0.35f, 0.1f));
        gate = new Node3D { Name = "Heckklappe", Position = new Vector3(0, H + 0.1f, 0.1f) }; bed.AddChild(gate);
        k.Box("paint_orange", new Vector3(0, -H / 2, 0), new Vector3(Wd, H, 0.08f), 0.02f);
        k.Build(gate, "Heckklappe");
        heap = BuildHeap(bed, Wd - 0.2f, L - 0.25f);
        heap.Position = new Vector3(0, 0.12f, L / 2 + 0.15f);
        heap.Visible = false;
        ram = new HydCylinder(this, 0.13f, 0.09f, 1.0f, "steel");
        UpdateBed();
    }

    static MeshInstance3D BuildHeap(Node3D parent, float w, float l)
    {
        int nx = 8, nz = 12;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 P(int i, int j)
        {
            float x = (i / (float)nx - 0.5f) * w, z = (j / (float)nz - 0.5f) * l;
            float u = x / (w / 2), q = z / (l / 2);
            return new Vector3(x, 1f + 0.35f * (1 - u * u) * (1 - q * q * 0.6f) + U.Noise(x * 3, z * 3) * 0.05f, z);
        }
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                var a = P(i, j); var b = P(i + 1, j); var c = P(i, j + 1); var d = P(i + 1, j + 1);
                foreach (var p in new[] { a, b, c, b, d, c }) { st.SetUV(new Vector2(p.X, p.Z)); st.AddVertex(p); }
            }
        st.GenerateNormals();
        st.GenerateTangents();
        var mi = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Mats.Get("soil_chunk") };
        parent.AddChild(mi);
        return mi;
    }

    public float PayloadT => cargo * SoilTool.Density(cargoMat);

    public bool TryCatch(Vector3 p, Vector3 v, float vol, SoilMat mat)
    {
        var lp = bed.GlobalTransform.AffineInverse() * p;
        if (MathF.Abs(lp.X) > Wd / 2 - 0.05f || lp.Z < 0.2f || lp.Z > L + 0.1f) return false;
        float top = 0.15f + cargo / cap * 1.35f;
        if (lp.Y > top + 0.25f || lp.Y < -0.4f) return false;
        if (cargo >= cap * 1.05f) return false;
        Receive(vol, mat);
        return true;
    }

    public void Receive(float vol, SoilMat mat)
    {
        if (cargo < 0.05f || vol > cargo * 0.1f) cargoMat = mat;
        cargo += vol;
    }

    public override ControlsInfo Controls() { var c = base.Controls(); c.rUp = "Mulde heben"; c.rDown = "Mulde senken"; return c; }

    void UpdateBed()
    {
        bed.Rotation = new Vector3(-bedJ.pos, 0, 0);
        gate.Rotation = new Vector3(bedJ.pos * 0.92f, 0, 0);
        for (int i = 0; i < bedShapes.Count; i++) bedShapes[i].Transform = bed.Transform * bedShapeLocal[i];
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var inp = isActive ? input : ControlInput.None;
        float cmd = inp.ry > 0.2f ? 1 : inp.ry < -0.2f ? -1.4f : 0;
        bedJ.Step(cmd, 1f, dt);
        UpdateBed();
        Mass = baseMass + PayloadT * 1000f;
        CenterOfMass = new Vector3(0, 1.1f + cargo * 0.04f, 0.2f - cargo / cap * 1.4f);
        hydFlow = MathF.Abs(bedJ.vel) * 5f;
        DriveStep(dt, inp, hydFlow * 0.4f);
        if (engine != null) engine.hydFlow = hydFlow;
        if (cargo > 0 && bedJ.pos > 0.38f)
        {
            float flow = MathF.Min(cargo, cap * 0.32f * (bedJ.pos - 0.36f) * dt * 3f);
            cargo -= flow; spillAcc += flow; clodAcc += flow;
            const float chunk = 0.06f;
            var dir = GlobalBasis.Z;
            var bt = bed.GlobalTransform;
            while (spillAcc > chunk)
            {
                spillAcc -= chunk;
                var p = bt * new Vector3(U.Rand(-1f, 1f), U.Rand(0.25f, 0.55f), -0.1f);
                SoilFX.I.SpawnFalling(p, -dir * 1.2f + U.RandSphere() * 0.3f + Vector3.Down * 0.3f, chunk, cargoMat);
            }
            if (clodAcc > 0.5f && cargoMat != SoilMat.Concrete)
            {
                float cv = clodAcc * 0.2f; clodAcc = 0; spillAcc -= cv;
                SoilFX.I.SpawnClod(bt * new Vector3(0, 0.5f, 0.3f), cv, cargoMat, -dir * 1.5f);
            }
            if (U.Rand() < 0.15f) SoundFX.I?.Play("soil", GlobalPosition - dir * 5f, 0.9f, 0.15f);
        }
        ComputeStability(3.2f, 1.1f);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        float f = cargo / cap;
        heap.Visible = f > 0.01f;
        if (heap.Visible)
        {
            heap.Scale = new Vector3(1, MathF.Max(0.05f, f * 1.15f), 1);
            heap.MaterialOverride = Mats.Get(SoilFX.MatKey(cargoMat));
        }
        ram.SetVisible(bedJ.pos > 0.02f);
        ram.Update(GlobalTransform * new Vector3(0, 1.05f, 1.45f), bed.GlobalTransform * new Vector3(0, 0.05f, 5.2f));
    }

    public override List<(string, string, string)> HudLines()
    {
        float t = PayloadT;
        return new()
        {
            ("Ladung", $"{U.Fmt(cargo)} / {U.Fmt(cap, 0)} m³", cargo > 0.05f ? $"{Excavator.MatName(cargoMat)}, {U.Fmt(t)} t" : "leer"),
            ("Tempo", U.Fmt(MathF.Abs(SpeedKmh), 0) + " km/h", GearText),
            ("Mulde", (int)Mathf.RadToDeg(bedJ.pos) + "°", t > 16f ? "überladen!" : ""),
        };
    }

    public override void _ExitTree() => SoilFX.Catchers.Remove(this);
}

/// <summary>Fahrmischer: 9-m³-Trommel, schwenkbare Schurre, Nachladen am Betonwerk.</summary>
public partial class MixerTruck : TruckBase
{
    Node3D drum, chute, chuteArm;
    public float concrete = 9f, cap = 9f;
    public bool pouring;
    float chuteYaw, chuteP = 0.5f, pourAcc;
    public static Vector3 PlantPos = new(-50, 0, 46);
    bool fullMsg;

    public static MixerTruck Create(Node parent, Vector3 pos, float yaw)
    {
        var t = new MixerTruck { Name = "Fahrmischer" };
        t.Build();
        parent.AddChild(t);
        t.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
        t.spawn = t.GlobalTransform;
        t.InitDrive();
        return t;
    }

    void Build()
    {
        displayName = "Fahrmischer 9 m³"; shortName = "Mischer"; camDistance = 15f;
        BuildChassis("paint_blue", 14000f);
        var dg = new Node3D { Name = "Trommelachse", Position = new Vector3(0, 2.45f, -0.9f), Rotation = new Vector3(0.19f, 0, 0) }; AddChild(dg);
        drum = new Node3D { Name = "Trommel" }; dg.AddChild(drum);
        var prof = new List<Vector2>();
        for (int i = 0; i <= 14; i++)
        {
            float t = i / 14f;
            float r = t < 0.12f ? 0.55f + t * 3.5f : t < 0.6f ? 1.0f : 1.0f - (t - 0.6f) * 1.4f;
            prof.Add(new Vector2(MathF.Max(0.35f, r), (t - 0.5f) * 5.0f));
        }
        var k = new MeshKit();
        // Drehkörper entlang -Z (Öffnung hinten)
        k.Lathe("drum", prof.ToArray(), 28, Vector3.Zero, Quaternion.FromEuler(new Vector3(-MathF.PI / 2, 0, 0)));
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathF.PI * 2;
            var q = Quaternion.FromEuler(new Vector3(0, 0, a));
            k.Box("paint_red", q * new Vector3(1.0f, 0, 0) + new Vector3(0, 0, 0.2f), new Vector3(0.05f, 0.12f, 3.2f), q, 0.01f);
        }
        k.Build(drum, "Trommel");
        k.Box("dark_metal", new Vector3(0, 1.25f, -0.9f), new Vector3(1.4f, 0.3f, 4.6f), 0.03f);
        k.Box("paint_blue", new Vector3(0, 1.8f, 1.4f), new Vector3(1.3f, 1.0f, 0.25f), 0.05f);
        k.Box("steel", new Vector3(0, 3.3f, -3.6f), new Vector3(0.9f, 0.7f, 0.9f), 0.04f);
        k.Build(this, "Mischeraufbau");
        AddBox(new Vector3(0, 2.45f, -0.9f), new Vector3(2.0f, 2.0f, 5.0f));
        chute = new Node3D { Name = "Schurre", Position = new Vector3(0, 2.2f, -3.95f) }; AddChild(chute);
        chuteArm = new Node3D { Name = "Schurrenarm" }; chute.AddChild(chuteArm);
        k.Box("steel", new Vector3(0, 0, -0.9f), new Vector3(0.42f, 0.06f, 1.8f), 0.01f);
        k.Box("steel", new Vector3(0.21f, 0.08f, -0.9f), new Vector3(0.04f, 0.18f, 1.8f), 0.01f);
        k.Box("steel", new Vector3(-0.21f, 0.08f, -0.9f), new Vector3(0.04f, 0.18f, 1.8f), 0.01f);
        k.Build(chuteArm, "Schurre");
    }

    public override ControlsInfo Controls()
    {
        var c = base.Controls();
        c.rUp = "Schurre auf"; c.rDown = "Schurre ab"; c.rLeft = "Schurre L"; c.rRight = "Schurre R";
        c.buttons.Add(("pour", pouring ? "Stopp" : "Beton ablassen", "Trommel"));
        return c;
    }

    public override bool OnButton(string id)
    {
        if (id != "pour") return false;
        if (!pouring && concrete <= 0.01f) { Hud.Toast("Trommel leer – am Betonwerk unter das Silo fahren.", ToastKind.Warn); return true; }
        pouring = !pouring;
        return true;
    }

    public float HopperDistance()
    {
        var hp = GlobalTransform * new Vector3(0, 0, -3.6f);
        return new Vector2(hp.X - PlantPos.X, hp.Z - PlantPos.Z).Length();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var inp = isActive ? input : ControlInput.None;
        chuteYaw = Math.Clamp(chuteYaw - inp.rx * 0.8f * dt, -1.2f, 1.2f);
        chuteP = Math.Clamp(chuteP - inp.ry * 0.5f * dt, 0.15f, 0.9f);
        chute.Rotation = new Vector3(0, chuteYaw, 0);
        chuteArm.Rotation = new Vector3(-chuteP, 0, 0);
        Mass = baseMass + concrete * 2350f;
        DriveStep(dt, inp, pouring ? 0.35f : 0.1f);
        if (pouring)
        {
            if (concrete <= 0) { pouring = false; Hud.Toast("Fahrmischer leer.", ToastKind.Info); }
            else
            {
                float v = MathF.Min(0.45f * dt, concrete);
                concrete -= v; pourAcc += v;
                var at = chuteArm.GlobalTransform;
                var end = at * new Vector3(0, 0, -1.8f);
                var dir = (at.Basis * new Vector3(0, 0, -1)).Normalized();
                while (pourAcc > 0.03f)
                {
                    pourAcc -= 0.03f;
                    SoilFX.I.SpawnFalling(end + U.RandSphere() * 0.1f, dir * 1.2f + LinearVelocity, 0.03f, SoilMat.Concrete, 0.12f);
                }
                if (U.Rand() < 0.1f) SoundFX.I?.Play("pour", end, 0.5f, 0.6f);
            }
        }
        if (HopperDistance() < 2.2f && MathF.Abs(SpeedKmh) < 1f && concrete < cap)
        {
            concrete = MathF.Min(cap, concrete + dt * 1.0f);
            fullMsg = false;
            if (U.Rand() < 0.05f) SoilFX.I.Dust(new Vector3(PlantPos.X, GlobalPosition.Y + 3.8f, PlantPos.Z), 1, new Color(0.8f, 0.8f, 0.78f), 0.8f);
            if (concrete >= cap && !fullMsg) { fullMsg = true; Hud.Toast("Fahrmischer beladen: 9 m³ Beton C25/30.", ToastKind.Ok); }
        }
        hydFlow = pouring ? 0.5f : 0.15f;
        ComputeStability(3.2f, 1.1f);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        drum.RotateObjectLocal(Vector3.Back, (pouring ? -2.6f : 0.95f) * (float)delta);
    }

    public override List<(string, string, string)> HudLines()
    {
        float hd = HopperDistance();
        return new()
        {
            ("Beton", $"{U.Fmt(concrete)} / {U.Fmt(cap, 0)} m³", pouring ? "Entleeren…" : "Mischen"),
            ("Tempo", U.Fmt(MathF.Abs(SpeedKmh), 0) + " km/h", GearText),
            hd < 12f ? ("Trichter", U.Fmt(hd) + " m", "zum Silo") : ("Schurre", (int)Mathf.RadToDeg(chuteYaw) + "°", ""),
        };
    }
}
