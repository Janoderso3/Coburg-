using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

public struct ControlInput
{
    public float lx, ly, rx, ry;
    public static ControlInput None => new();
}

public class ControlsInfo
{
    public string lUp = "", lDown = "", lLeft = "", lRight = "", rUp = "", rDown = "", rLeft = "", rRight = "";
    public List<(string id, string label, string hint)> buttons = new();
}

/// <summary>
/// Basis aller Maschinen (Starrkörper). Konvention: lokale +Z = vorne, +X = links, +Y = oben.
/// Motor (Drehzahl/Last für Klang und Abgas), Rundumleuchte, Standsicherheit, Bergung.
/// </summary>
public abstract partial class VehicleBase : RigidBody3D
{
    public string displayName = "Fahrzeug", shortName = "Fzg";
    public EngineSynth engine;
    public bool isActive;
    public ControlInput input;
    public float rpm = 750f, idleRpm = 750f, maxRpm = 2100f, engineLoad, hydFlow, relief;
    public Node3D exhaust, cabEye;
    public float tiltDeg, stability = 1f, hitWarning;
    public bool tipped;
    public Transform3D spawn;
    public float camDistance = 13f, camHeight = 2.2f, cabinFov = 70f;
    float smokeT;
    protected PhysicsRayQueryParameters3D rayQ;

    public abstract ControlsInfo Controls();
    public virtual bool OnButton(string id) => false;
    public abstract List<(string label, string value, string sub)> HudLines();
    public virtual Vector3 CameraTarget() => GlobalPosition + Vector3.Up * camHeight;
    /// <summary>Blickrichtung (Gierwinkel, Radiant), in die die Kamera hinter dem Fahrer schaut.</summary>
    public virtual float CameraYaw() { var f = GlobalBasis.Z; return MathF.Atan2(f.X, f.Z); }
    public Vector3 Fwd => GlobalBasis.Z;
    public virtual float SpeedKmh => LinearVelocity.Dot(Fwd) * 3.6f;

    protected void SetupBody(float massKg, Vector3 com, Vector3 inertia)
    {
        Mass = massKg;
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = com;
        Inertia = inertia;
        CollisionLayer = Layers.Vehicle;
        CollisionMask = Layers.All;
        ContinuousCd = true;
        LinearDamp = 0.02f;
        AngularDamp = 0.3f;
        CanSleep = false;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f, Bounce = 0.02f };
        rayQ = new PhysicsRayQueryParameters3D { CollisionMask = Layers.Ground, CollideWithAreas = false };
    }

    protected static Vector3 BoxInertia(float m, float w, float h, float l) => new(m * (h * h + l * l) / 12f, m * (w * w + l * l) / 12f, m * (w * w + h * h) / 12f);

    protected void SetupEngine(int cyl, float idle, float max, float res1, float res2, float vol = 1f)
    {
        idleRpm = idle; maxRpm = max; rpm = idle;
        engine = new EngineSynth { Name = "Motor", cylinders = cyl, idleRpm = idle, maxRpm = max, exhaustRes1 = res1, exhaustRes2 = res2, volume = vol, Position = new Vector3(0, 1.5f, -1f) };
        AddChild(engine);
    }

    protected Node3D Marker(Node3D parent, string name, Vector3 pos)
    {
        var n = new Node3D { Name = name, Position = pos };
        parent.AddChild(n);
        return n;
    }

    protected CollisionShape3D AddBox(Vector3 c, Vector3 size, Basis? rot = null)
    {
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = c };
        if (rot.HasValue) cs.Basis = rot.Value;
        AddChild(cs);
        return cs;
    }

    /// <summary>Kinematischer Werkzeugkörper (Löffel, Schild): schiebt Brocken und Trümmer, ignoriert Boden.</summary>
    public static AnimatableBody3D ToolBody(Node3D parent, Vector3 c, Vector3 size, Basis? rot = null)
    {
        var b = new AnimatableBody3D { SyncToPhysics = true, CollisionLayer = Layers.Tool, CollisionMask = Layers.Debris | Layers.Clod | Layers.Element };
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = c };
        if (rot.HasValue) cs.Basis = rot.Value;
        b.AddChild(cs);
        parent.AddChild(b);
        return b;
    }

    protected void UpdateEngine(float demand, float load, float dt)
    {
        float target = idleRpm + (maxRpm - idleRpm) * U.Clamp01(demand) - load * 180f;
        rpm = U.Damp(rpm, target, demand > 0.05f ? 3f : 2f, dt);
        engineLoad = U.Damp(engineLoad, U.Clamp01(load), 5f, dt);
        if (engine != null)
        {
            engine.rpm = rpm; engine.load = engineLoad; engine.hydFlow = hydFlow; engine.relief = relief;
            engine.volume = Settings.Volume * (isActive ? 1f : 0.5f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (exhaust != null && SoilFX.I != null)
        {
            smokeT -= dt;
            if (smokeT <= 0)
            {
                float rn = (rpm - idleRpm) / MathF.Max(1f, maxRpm - idleRpm);
                smokeT = Mathf.Lerp(0.16f, 0.05f, rn);
                SoilFX.I.Smoke(exhaust.GlobalPosition, LinearVelocity * 0.8f, rn * 0.6f + engineLoad * 0.4f, engineLoad);
            }
        }
        tiltDeg = Mathf.RadToDeg(GlobalBasis.Y.AngleTo(Vector3.Up));
        if (tiltDeg > 55f && !tipped)
        {
            tipped = true;
            Hud.Toast($"{displayName} ist umgekippt!", ToastKind.Warn);
            SoundFX.I?.Play("crash", GlobalPosition, 1f);
        }
        hitWarning = MathF.Max(0, hitWarning - dt);
    }

    protected void ComputeStability(float halfLen, float halfWidth)
    {
        // Schwerpunkt entlang der Schwerkraft auf die Aufstandsebene projizieren (Neigung wirkt automatisch mit)
        var com = CenterOfMass;
        var g = GlobalBasis.Inverse() * Vector3.Down;
        if (g.Y < -0.2f) com -= g * (com.Y / g.Y);
        float mx = halfWidth - MathF.Abs(com.X), mz = halfLen - MathF.Abs(com.Z);
        float margin = MathF.Min(mx / halfWidth, mz / halfLen);
        stability = U.Clamp01(margin * 1.25f);
    }

    public void Recover()
    {
        var p = GlobalPosition;
        float y = SoilTerrain.I != null ? SoilTerrain.I.Height(p.X, p.Z) : p.Y;
        float yaw = CameraYaw();
        GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(p.X, y + 0.8f, p.Z));
        LinearVelocity = Vector3.Zero; AngularVelocity = Vector3.Zero;
        tipped = false;
        Hud.Toast($"{displayName} wurde geborgen und aufgerichtet.", ToastKind.Info);
    }

    public virtual void Cabin(out Vector3 pos, out Vector3 fwd)
    {
        var t = cabEye ?? (Node3D)this;
        pos = t.GlobalPosition;
        fwd = t.GlobalBasis.Z;
    }

    /// <summary>Strahl nach unten (Bodenkontakt), eigene Kollision ausgenommen.</summary>
    protected bool Ray(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal, out GodotObject collider)
    {
        rayQ.From = from; rayQ.To = to;
        rayQ.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var r = GetWorld3D().DirectSpaceState.IntersectRay(rayQ);
        if (r.Count == 0) { point = normal = Vector3.Zero; collider = null; return false; }
        point = (Vector3)r["position"]; normal = (Vector3)r["normal"]; collider = r["collider"].AsGodotObject();
        return true;
    }
}

/// <summary>Hydraulisch angetriebenes Gelenk mit Ventilrampe und Endanschlägen.</summary>
public class HydJoint
{
    public float pos, vel, min, max, maxSpeed, ramp;
    public HydJoint(float start, float min, float max, float maxSpeed, float ramp) { pos = start; this.min = min; this.max = max; this.maxSpeed = maxSpeed; this.ramp = ramp; }
    public void Step(float cmd, float factor, float dt)
    {
        vel = U.Approach(vel, cmd * maxSpeed * factor, ramp * dt);
        pos += vel * dt;
        if (pos < min) { pos = min; vel = 0; }
        if (pos > max) { pos = max; vel = 0; }
    }
}

/// <summary>Sichtbarer Hydraulikzylinder (Rohr + Chromstange) zwischen zwei Weltpunkten.</summary>
public class HydCylinder
{
    readonly Node3D root;
    readonly MeshInstance3D barrel, rod;
    readonly float barrelLen;
    static Mesh unit;
    public HydCylinder(Node3D parent, float rBarrel, float rRod, float barrelLen, string mat = "paint_yellow")
    {
        if (unit == null) { var k = new MeshKit(); k.Cyl("chrome", new Vector3(0, 0.5f, 0), 1f, 1f, 1f, 14, Quaternion.Identity); unit = k.BuildMesh(); }
        this.barrelLen = barrelLen;
        root = new Node3D { Name = "Zylinder", TopLevel = true };
        parent.AddChild(root);
        barrel = new MeshInstance3D { Mesh = unit, MaterialOverride = Mats.Get(mat), Scale = new Vector3(rBarrel, 1, rBarrel) };
        rod = new MeshInstance3D { Mesh = unit, MaterialOverride = Mats.Get("chrome"), Scale = new Vector3(rRod, 1, rRod) };
        root.AddChild(barrel); root.AddChild(rod);
        rr = rRod; rb = rBarrel;
    }
    readonly float rr, rb;
    public void Update(Vector3 a, Vector3 b)
    {
        var d = b - a; float L = d.Length();
        if (L < 1e-4f) return;
        var y = d / L;
        var x = MathF.Abs(y.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up.Cross(y).Normalized();
        var z = x.Cross(y);
        root.GlobalTransform = new Transform3D(new Basis(x, y, z), a);
        float bl = MathF.Min(barrelLen, L * 0.95f);
        barrel.Scale = new Vector3(rb, bl, rb); barrel.Position = Vector3.Zero;
        float rl = MathF.Max(0.05f, L - bl * 0.85f);
        rod.Scale = new Vector3(rr, rl, rr); rod.Position = new Vector3(0, L - rl, 0);
    }
    public void SetVisible(bool v) => root.Visible = v;
}
