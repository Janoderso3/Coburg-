using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>
/// Kettenlaufwerk: Strahl-Federung je Laufrolle, Längs- und Querreibung je Kontakt (begrenzt durch μ·N).
/// Lenken über unterschiedliche Kettengeschwindigkeit wie beim hydrostatischen Fahrantrieb.
/// </summary>
public class TrackDrive
{
    VehicleBase b;
    public float gauge, length, contactY, maxSpeed, accel = 1.0f, mu = 0.75f, muLat = 0.65f, travel = 0.16f;
    public int rollers = 6;
    public float beltL, beltR, distL, distR, slip, tractionUsed;
    public int grounded;
    float k, c, compactT;
    Vector3[] pts;
    PhysicsRayQueryParameters3D q;

    public void Init(VehicleBase body, float gauge, float length, float contactY, float maxSpeed, int rollers = 6)
    {
        b = body; this.gauge = gauge; this.length = length; this.contactY = contactY; this.maxSpeed = maxSpeed; this.rollers = rollers;
        var list = new List<Vector3>();
        foreach (float sx in new[] { gauge / 2, -gauge / 2 })
        {
            for (int i = 0; i < rollers; i++) list.Add(new Vector3(sx, contactY, Mathf.Lerp(-length / 2, length / 2, (i + 0.5f) / rollers)));
            list.Add(new Vector3(sx, contactY + 0.22f, length / 2 + 0.15f));
            list.Add(new Vector3(sx, contactY + 0.22f, -length / 2 - 0.15f));
        }
        pts = list.ToArray();
        float perContact = b.Mass * 9.81f / (rollers * 2);
        k = perContact / 0.04f;
        c = 2f * 0.6f * MathF.Sqrt(k * b.Mass / (rollers * 2));
        q = new PhysicsRayQueryParameters3D { CollisionMask = Layers.Ground, Exclude = new Godot.Collections.Array<Rid> { b.GetRid() } };
    }

    public void Step(float throttle, float steer, float dt, float powerFactor = 1f)
    {
        // links = +X (Godot), Lenken nach rechts: linke Kette schneller
        float tl = throttle + steer * 0.8f, tr = throttle - steer * 0.8f;
        float mx = MathF.Max(1f, MathF.Max(MathF.Abs(tl), MathF.Abs(tr)));
        tl /= mx; tr /= mx;
        float vmax = maxSpeed * Mathf.Lerp(1f, 0.45f, tractionUsed) * powerFactor;
        beltL = U.Approach(beltL, tl * vmax, accel * dt);
        beltR = U.Approach(beltR, tr * vmax, accel * dt);
        grounded = 0;
        var gt = b.GlobalTransform;
        var up = gt.Basis.Y;
        float mShare = b.Mass / (rollers * 2);
        float usedSum = 0, slipSum = 0;
        var soil = SoilTerrain.I;
        compactT += dt;
        bool doCompact = compactT > 0.1f;
        if (doCompact) compactT = 0;
        var space = b.GetWorld3D().DirectSpaceState;
        var com = gt * b.CenterOfMass;
        for (int i = 0; i < pts.Length; i++)
        {
            var p = gt * pts[i];
            var origin = p + up * travel;
            q.From = origin; q.To = origin - up * (travel * 2f + 0.25f);
            var hit = space.IntersectRay(q);
            if (hit.Count == 0) continue;
            var hp = (Vector3)hit["position"]; var n = (Vector3)hit["normal"];
            float dist = origin.DistanceTo(hp);
            float x = travel - dist;
            if (x <= 0) continue;
            grounded++;
            bool idler = (i % (rollers + 2)) >= rollers;
            var pv = b.LinearVelocity + b.AngularVelocity.Cross(hp - com);
            float vn = pv.Dot(up);
            float N = MathF.Max(0f, k * x - c * vn);
            if (idler) N *= 0.6f;
            U.ForceAt(b, up * N, hp);
            var f = (gt.Basis.Z - n * gt.Basis.Z.Dot(n)).Normalized();
            var l = n.Cross(f);
            bool left = pts[i].X > 0;
            float belt = left ? beltL : beltR;
            float vLong = pv.Dot(f) - belt;
            float vLat = pv.Dot(l);
            float loose = soil != null ? U.Clamp01(soil.LooseAt(hp.X, hp.Z) * 4f) : 0f;
            float muL = mu * (1f - 0.25f * loose);
            float fLong = -vLong * mShare / dt * 0.35f;
            float fLat = -vLat * mShare / dt * 0.35f;
            float maxL = muL * N, maxLat = muLat * N * (1f - 0.2f * loose);
            float aL = MathF.Abs(fLong);
            if (aL > maxL) { fLong *= maxL / aL; slipSum += 1; }
            if (MathF.Abs(fLat) > maxLat) fLat = MathF.Sign(fLat) * maxLat;
            float vf = pv.Dot(f);
            float rr = -MathF.Sign(vf) * N * (0.03f + 0.15f * loose) * U.Clamp01(MathF.Abs(vf) * 4f);
            U.ForceAt(b, f * (fLong + rr) + l * fLat, hp);
            usedSum += maxL > 0 ? MathF.Abs(fLong) / maxL : 0;
            if (doCompact && soil != null && hit["collider"].AsGodotObject() is Node cn && cn.GetParent() is TerrainChunks && (MathF.Abs(belt) > 0.05f || MathF.Abs(vLong) > 0.05f))
                soil.Compact(hp, 0.28f, b.Mass / 20000f * (0.4f + MathF.Abs(belt)), 0.1f);
        }
        tractionUsed = U.Damp(tractionUsed, grounded > 0 ? usedSum / grounded : 0, 4f, dt);
        slip = U.Damp(slip, grounded > 0 ? slipSum / grounded : 0, 4f, dt);
        distL += beltL * dt; distR += beltR * dt;
        if (soil != null && grounded > 0) soil.Surcharge(com, MathF.Max(gauge, 2f), b.Mass / 20000f * dt * 0.6f);
    }

    public float Speed => (beltL + beltR) * 0.5f;
}

/// <summary>
/// Radfahrwerk mit eigener Strahl-Federung, Reifenreibung (Schlupf begrenzt durch μ·N),
/// Dieselkennlinie und Automatikgetriebe.
/// </summary>
public class WheelSim
{
    public class W
    {
        public Vector3 local; public float r, travel, k, c; public bool steer, drive;
        public Node3D visual; public float spin, comp, lastComp; public bool grounded; public float steerSign = 1;
    }

    VehicleBase b;
    public readonly List<W> wheels = new();
    public float[] gears = { 6.0f, 3.6f, 2.3f, 1.5f, 1.0f, 0.78f };
    public float reverseGear = 5.5f, finalDrive = 5.2f, maxTorque = 1900f, idle = 650f, maxRpm = 2100f, shiftUp = 1850f, shiftDown = 1050f;
    public int gear = 1;
    public float rpm = 650f, brakeForce = 30000f, maxSteerDeg = 36f, speedLimit = 13f, steerAngle, throttleOut, mu = 0.9f;
    public bool reversing, braking;
    float shiftTimer;
    PhysicsRayQueryParameters3D q;

    public void Init(VehicleBase body)
    {
        b = body;
        q = new PhysicsRayQueryParameters3D { CollisionMask = Layers.Ground, Exclude = new Godot.Collections.Array<Rid> { b.GetRid() } };
    }

    public W Add(Vector3 local, float r, float travel, float staticLoadKg, bool steer, bool drive, Node3D visual)
    {
        float k = staticLoadKg * 9.81f / (travel * 0.45f);
        var w = new W { local = local, r = r, travel = travel, k = k, c = 2f * 0.45f * MathF.Sqrt(k * staticLoadKg), steer = steer, drive = drive, visual = visual };
        wheels.Add(w);
        return w;
    }

    public void Step(float throttle, float steerIn, float dt)
    {
        var gt = b.GlobalTransform;
        var up = gt.Basis.Y;
        float v = b.LinearVelocity.Dot(gt.Basis.Z);
        float absV = MathF.Abs(v);
        float steerLimit = maxSteerDeg * Mathf.Lerp(1f, 0.35f, U.Clamp01(absV / 12f));
        // rechts lenken = negative Gierrichtung (Godot: +X ist links)
        steerAngle = U.Approach(steerAngle, -steerIn * steerLimit, 45f * dt);
        bool wantRev = throttle < -0.05f, wantFwd = throttle > 0.05f;
        braking = false;
        float drive = 0f, brake = 0f;
        if (wantFwd) { if (v < -0.4f) { brake = throttle; braking = true; } else { if (gear < 1) gear = 1; drive = throttle; } }
        else if (wantRev) { if (v > 0.4f) { brake = -throttle; braking = true; } else { gear = -1; drive = -throttle; } }
        else brake = absV < 0.6f ? 0.8f : 0.05f;
        reversing = gear == -1 && v < -0.05f;
        float ratio = gear == -1 ? reverseGear : gear >= 1 ? gears[gear - 1] : 0f;
        float wheelR = 0.5f;
        foreach (var w in wheels) if (w.drive) { wheelR = w.r; break; }
        float engineRpmFromWheels = absV / wheelR / (2 * MathF.PI) * 60f * ratio * finalDrive;
        float targetRpm = MathF.Max(idle + (maxRpm - idle) * 0.35f * drive, engineRpmFromWheels);
        if (drive < 0.05f) targetRpm = MathF.Max(idle, engineRpmFromWheels * 0.9f);
        rpm = U.Damp(rpm, Math.Clamp(targetRpm, idle, maxRpm + 100f), 8f, dt);
        shiftTimer -= dt;
        if (gear >= 1 && shiftTimer <= 0)
        {
            if (rpm > shiftUp && gear < gears.Length) { gear++; shiftTimer = 0.9f; }
            else if (rpm < shiftDown && gear > 1) { gear--; shiftTimer = 0.9f; }
        }
        float rn = Mathf.InverseLerp(idle, maxRpm, rpm);
        float tq = maxTorque * U.Clamp01(0.7f + 0.6f * rn - 0.55f * rn * rn) * (rpm > maxRpm ? 0f : 1f);
        if (absV > speedLimit) drive = 0;
        int dn = 0; foreach (var w in wheels) if (w.drive) dn++;
        float wheelForce = tq * ratio * finalDrive * drive * 0.88f / MathF.Max(1, dn) / wheelR;
        if (shiftTimer > 0.6f) wheelForce *= 0.2f;
        if (gear == -1) wheelForce = -wheelForce;
        throttleOut = drive;
        int nGround = 0; foreach (var w in wheels) if (w.grounded) nGround++;
        float mShare = b.Mass / MathF.Max(1, wheels.Count);
        var space = b.GetWorld3D().DirectSpaceState;
        var com = gt * b.CenterOfMass;
        var soil = SoilTerrain.I;
        foreach (var w in wheels)
        {
            var top = gt * w.local;
            q.From = top; q.To = top - up * (w.travel + w.r);
            var hit = space.IntersectRay(q);
            w.grounded = false;
            float hitDist = w.travel + w.r;
            if (hit.Count > 0)
            {
                var hp = (Vector3)hit["position"]; var n = (Vector3)hit["normal"];
                hitDist = top.DistanceTo(hp);
                float x = w.travel + w.r - hitDist;
                w.grounded = true;
                w.lastComp = w.comp; w.comp = x;
                float cv = (w.comp - w.lastComp) / dt;
                float N = MathF.Max(0, w.k * x + w.c * cv);
                U.ForceAt(b, up * N, hp);
                float ang = w.steer ? Mathf.DegToRad(steerAngle) * w.steerSign : 0f;
                var fwd = gt.Basis.Z.Rotated(up, ang);
                var f = (fwd - n * fwd.Dot(n)).Normalized();
                var l = n.Cross(f);
                var pv = b.LinearVelocity + b.AngularVelocity.Cross(hp - com);
                float vLong = pv.Dot(f), vLat = pv.Dot(l);
                float loose = soil != null ? U.Clamp01(soil.LooseAt(hp.X, hp.Z) * 4f) : 0f;
                float maxF = mu * N * (1f - 0.3f * loose);
                float fLong = w.drive ? wheelForce : 0f;
                if (brake > 0) fLong += -vLong * mShare / dt * 0.5f * U.Clamp01(brake * brakeForce / (mShare * 9.81f));
                fLong -= MathF.Sign(vLong) * N * (0.015f + 0.08f * loose) * U.Clamp01(MathF.Abs(vLong) * 3f);
                float fLat = -vLat * mShare / dt * 0.45f;
                // Reibkreis
                float tot = MathF.Sqrt(fLong * fLong + fLat * fLat);
                if (tot > maxF) { fLong *= maxF / tot; fLat *= maxF / tot; }
                U.ForceAt(b, f * fLong + l * fLat, hp);
                w.spin += vLong / w.r * dt;
            }
            else { w.comp = 0; w.lastComp = 0; if (w.drive && drive > 0) w.spin += 3f * dt; }
            if (w.visual != null)
            {
                float steerRad = w.steer ? Mathf.DegToRad(steerAngle) * w.steerSign : 0f;
                w.visual.Position = w.local - Vector3.Up * (hitDist - w.r);
                w.visual.Rotation = new Vector3(w.spin, steerRad, 0);
            }
        }
    }

    public void Wear(float dt)
    {
        var soil = SoilTerrain.I;
        if (soil == null) return;
        var gt = b.GlobalTransform;
        foreach (var w in wheels)
            if (w.grounded) soil.Compact(gt * (w.local - Vector3.Up * (w.travel + w.r - w.comp)), 0.25f, b.Mass / 20000f * 0.7f, dt);
    }
}
