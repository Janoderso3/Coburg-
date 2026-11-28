using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>
    /// Radlader mit echter Knicklenkung: Vorder- und Hinterwagen sind zwei Körper, verbunden durch ein Gelenk,
    /// das von Lenkzylindern (Feder-Antrieb) auf den Sollwinkel gedrückt wird.
    /// </summary>
    public class WheelLoader : VehicleBase
    {
        public Rigidbody front;
        HingeJoint hinge;
        Transform armT, bucketT;
        HydJoint lift, tiltJ;
        float steer;
        public SoilTool tool;
        WheelDrive wd;
        HydCylinder cLiftL, cLiftR, cTilt, cSteerL, cSteerR;

        public static WheelLoader Create(Vector3 pos, float yaw)
        {
            var go = new GameObject("Radlader");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var l = go.AddComponent<WheelLoader>();
            l.Build();
            return l;
        }

        void Build()
        {
            displayName = "Radlader 18 t"; shortName = "Radlader"; camDistance = 13f;
            spawnPos = transform.position; spawnRot = transform.rotation;
            SetupBody(10500f, new Vector3(0, 1.0f, -1.6f));
            SetupEngine(6, 750, 2200, 105f, 390f);
            lift = new HydJoint(-0.05f, -0.27f, 1.05f, 0.38f, 1.8f);
            tiltJ = new HydJoint(0.1f, -0.85f, 0.75f, 0.9f, 4f);
            var k = new MeshKit();
            // ---------- Hinterwagen (Ursprung = Knickgelenk) ----------
            k.Box("dark_metal", new Vector3(0, 0.95f, -1.5f), new Vector3(1.0f, 0.5f, 3.0f), 0.04f);
            k.Box("paint_yellow", new Vector3(0, 1.6f, -2.35f), new Vector3(1.9f, 1.15f, 2.1f), 0.12f);
            k.Box("paint_yellow", new Vector3(0, 1.1f, -3.45f), new Vector3(2.2f, 0.55f, 0.6f), 0.12f);
            for (int i = 0; i < 7; i++) k.Box("black_plastic", new Vector3(-0.6f + i * 0.2f, 1.6f, -3.42f), new Vector3(0.08f, 0.45f, 0.02f));
            k.Box("dark_metal", new Vector3(0, 1.12f, -1.6f), new Vector3(2.35f, 0.12f, 2.6f), 0.03f);
            k.Box("paint_yellow", new Vector3(0, 1.38f, -0.75f), new Vector3(1.35f, 0.25f, 1.4f), 0.04f);
            k.Box("paint_yellow", new Vector3(0, 3.15f, -0.8f), new Vector3(1.4f, 0.1f, 1.5f), 0.04f);
            foreach (var (x, z) in new[] { (0.64f, -0.08f), (-0.64f, -0.08f), (0.64f, -1.5f), (-0.64f, -1.5f) })
                k.Box("paint_yellow", new Vector3(x, 2.32f, z), new Vector3(0.07f, 1.65f, 0.07f));
            k.Box("seat", new Vector3(0, 1.85f, -0.9f), new Vector3(0.5f, 0.55f, 0.5f), 0.05f);
            k.Box("black_plastic", new Vector3(0, 2.18f, -2.9f), new Vector3(1.7f, 0.06f, 1.0f));
            k.Cyl("steel", new Vector3(-0.45f, 2.55f, -2.6f), 0.07f, 0.07f, 0.7f, 10, Quaternion.identity);
            k.Build(transform, "Hinterwagen");
            var g = new MeshKit();
            g.Box("glass", new Vector3(0, 2.32f, -0.8f), new Vector3(1.25f, 1.55f, 1.35f));
            g.Build(transform, "Scheiben", false);
            var lights = new MeshKit();
            lights.Box("headlight", new Vector3(0.5f, 3.05f, -0.06f), new Vector3(0.22f, 0.1f, 0.06f), 0.02f);
            lights.Box("headlight", new Vector3(-0.5f, 3.05f, -0.06f), new Vector3(0.22f, 0.1f, 0.06f), 0.02f);
            lights.Box("taillight", new Vector3(0.85f, 1.3f, -3.76f), new Vector3(0.15f, 0.12f, 0.04f), 0.01f);
            lights.Box("taillight", new Vector3(-0.85f, 1.3f, -3.76f), new Vector3(0.15f, 0.12f, 0.04f), 0.01f);
            lights.Build(transform, "Leuchten", false);
            beacons.Add(Excavator.Beacon(transform, new Vector3(0, 3.22f, -1.1f)));
            cabEye = new GameObject("Fahrerauge").transform; cabEye.SetParent(transform, false); cabEye.localPosition = new Vector3(0, 2.45f, -0.75f);
            exhaust = new GameObject("Auspuff").transform; exhaust.SetParent(transform, false); exhaust.localPosition = new Vector3(-0.45f, 2.92f, -2.6f);
            Excavator.AddBox(transform, new Vector3(0, 1.55f, -2.2f), new Vector3(2.2f, 1.3f, 3.2f), Layers.Vehicle);
            Excavator.AddBox(transform, new Vector3(0, 2.3f, -0.8f), new Vector3(1.4f, 1.7f, 1.5f), Layers.Vehicle);
            wd = new WheelDrive { rb = rb, maxTorque = 1100f, finalDrive = 22f, gears = new[] { 3.2f, 1.7f, 0.95f, 0.6f }, reverseGear = 3.0f, speedLimit = 9.5f, maxRpm = 2200, idle = 750, shiftUp = 2000, shiftDown = 1100, brakeMax = 26000f };
            float r = 0.8f;
            foreach (float sx in new[] { -1f, 1f })
            {
                var vis = Wheel(transform, r, 0.6f);
                wd.Add(transform, new Vector3(sx * 1.0f, r + 0.1f, -1.65f), r, 400f, 0.2f, 330000f, 26000f, false, true, vis);
            }

            // ---------- Vorderwagen ----------
            var fgo = new GameObject("Radlader Vorderwagen");
            fgo.layer = Layers.Vehicle;
            fgo.transform.SetPositionAndRotation(transform.position, transform.rotation);
            front = fgo.AddComponent<Rigidbody>();
            front.mass = 7000f;
            front.interpolation = RigidbodyInterpolation.Interpolate;
            front.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            front.solverIterations = 14;
            front.centerOfMass = new Vector3(0, 1.1f, 1.6f);
            fgo.AddComponent<VehiclePart>().owner = this;
            var ft = fgo.transform;
            k.Box("dark_metal", new Vector3(0, 0.95f, 1.1f), new Vector3(1.1f, 0.55f, 2.2f), 0.05f);
            k.Box("paint_yellow", new Vector3(0, 1.3f, 0.55f), new Vector3(1.3f, 0.5f, 0.9f), 0.06f);
            k.Box("dark_metal", new Vector3(0, 1.12f, 1.65f), new Vector3(2.35f, 0.12f, 1.1f), 0.03f);
            k.Build(ft, "Vorderwagen");
            Excavator.AddBox(ft, new Vector3(0, 1.1f, 1.2f), new Vector3(1.3f, 0.9f, 2.0f), Layers.Vehicle);
            foreach (float sx in new[] { -1f, 1f })
            {
                var vis = Wheel(ft, r, 0.6f);
                wd.Add(ft, new Vector3(sx * 1.0f, r + 0.1f, 1.65f), r, 400f, 0.2f, 330000f, 26000f, false, true, vis);
            }
            // Hubarm
            armT = new GameObject("Hubarm").transform;
            armT.SetParent(ft, false);
            armT.localPosition = new Vector3(0, 2.0f, 0.65f);
            foreach (float sx in new[] { -0.62f, 0.62f }) k.Beam("paint_yellow", new Vector3(sx, 0, 0), new Vector3(sx, -0.35f, 2.75f), 0.2f, 0.42f, Vector3.up, 0.04f);
            k.Box("paint_yellow", new Vector3(0, -0.2f, 1.4f), new Vector3(1.4f, 0.3f, 0.3f), 0.04f);
            k.CylX("steel", Vector3.zero, 0.1f, 1.5f, 12);
            k.Build(armT, "Hubarm");
            Excavator.AddBeamCollider(armT, new Vector3(0, 0, 0), new Vector3(0, -0.35f, 2.75f), 1.45f, 0.42f);
            bucketT = new GameObject("Schaufel").transform;
            bucketT.SetParent(armT, false);
            bucketT.localPosition = new Vector3(0, -0.35f, 2.75f);
            float W = 2.7f;
            k.ProfileX("paint_yellow_worn", new[] { new Vector2(0.05f, 0.25f), new Vector2(-0.1f, 0f), new Vector2(-0.1f, -0.7f), new Vector2(0.1f, -1.0f), new Vector2(0.75f, -1.08f), new Vector2(1.15f, -1.1f), new Vector2(1.15f, -1.0f), new Vector2(0.75f, -0.98f), new Vector2(0.15f, -0.9f), new Vector2(0.02f, -0.65f), new Vector2(0.02f, 0.05f), new Vector2(0.12f, 0.3f) }, W);
            foreach (float sx in new[] { -W / 2, W / 2 }) k.ProfileX("paint_yellow_worn", new[] { new Vector2(-0.1f, 0.3f), new Vector2(-0.12f, -0.72f), new Vector2(0.1f, -1.05f), new Vector2(1.15f, -1.1f), new Vector2(0.6f, 0f) }, 0.05f, sx);
            k.Box("bucket_wear", new Vector3(0, -1.08f, 1.12f), new Vector3(W, 0.06f, 0.2f), 0.01f);
            k.Build(bucketT, "Schaufel");
            Excavator.AddBox(bucketT, new Vector3(0, -0.6f, 0.5f), new Vector3(W, 1.0f, 1.2f), Layers.Tool);
            var kc = new MeshKit();
            kc.Sphere("soil_chunk", Vector3.zero, 1f, 14);
            var cont = kc.Build(bucketT, "Inhalt");
            cont.transform.localPosition = new Vector3(0, -0.6f, 0.6f);
            cont.SetActive(false);
            tool = new SoilTool
            {
                t = bucketT, owner = this, cap = 2.8f, width = W, power = 45f,
                tipL = new Vector3(0, -1.08f, 1.2f), heelL = new Vector3(0, -0.95f, 0.1f),
                openL = new Vector3(0, 0.55f, 1).normalized, tipDirL = new Vector3(0, -0.05f, 1).normalized,
                content = cont.transform, contentScale = new Vector3(1.25f, 0.5f, 0.6f),
            };
            // Knickgelenk
            hinge = fgo.AddComponent<HingeJoint>();
            hinge.connectedBody = rb;
            hinge.anchor = new Vector3(0, 1.0f, 0);
            hinge.axis = Vector3.up;
            hinge.useLimits = true;
            hinge.limits = new JointLimits { min = -40f, max = 40f };
            hinge.useSpring = true;
            hinge.spring = new JointSpring { spring = 4.0e6f, damper = 4.0e5f, targetPosition = 0 };
            hinge.enableCollision = false;
            cLiftL = new HydCylinder(ft, 0.1f, 0.06f, 1.0f);
            cLiftR = new HydCylinder(ft, 0.1f, 0.06f, 1.0f);
            cTilt = new HydCylinder(ft, 0.12f, 0.07f, 1.1f);
            cSteerL = new HydCylinder(transform, 0.06f, 0.035f, 0.45f, "dark_metal");
            cSteerR = new HydCylinder(transform, 0.06f, 0.035f, 0.45f, "dark_metal");
            ApplyJoints();
        }

        public static Transform Wheel(Transform parent, float r, float w, string rim = "paint_yellow")
        {
            var root = new GameObject("Rad").transform;
            root.SetParent(parent, false);
            var k = new MeshKit();
            var rot = Quaternion.Euler(0, 0, 90);
            k.Cyl("tread", Vector3.zero, r, r, w, 28, rot, false);
            k.Cyl("rubber", Vector3.zero, r * 0.97f, r * 0.97f, w * 0.96f, 28, rot, true, "rubber");
            k.Cyl(rim, Vector3.zero, r * 0.58f, r * 0.58f, w * 1.0f, 18, rot);
            k.Cyl("dark_metal", Vector3.zero, r * 0.2f, r * 0.24f, w * 1.12f, 10, rot);
            // Profilstollen
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                var c = new Vector3(0, Mathf.Sin(a) * r, Mathf.Cos(a) * r);
                k.Box("rubber", c, new Vector3(w * 0.9f, 0.06f, 0.12f), Quaternion.Euler(-a * Mathf.Rad2Deg, 0, 0) * Quaternion.Euler(0, 30f * (i % 2 == 0 ? 1 : -1), 0), 0.01f);
            }
            k.Build(root, "Reifen");
            return root;
        }

        void ApplyJoints()
        {
            armT.localRotation = Quaternion.Euler(-lift.pos * Mathf.Rad2Deg, 0, 0);
            bucketT.localRotation = Quaternion.Euler((lift.pos - tiltJ.pos) * Mathf.Rad2Deg, 0, 0);
        }

        public override ControlsInfo Controls()
        {
            bool iso = Settings.Iso;
            return new ControlsInfo
            {
                lUp = "Gas", lDown = "Zurück", lLeft = "Lenken L", lRight = "Lenken R",
                rUp = iso ? "Senken" : "Heben", rDown = iso ? "Heben" : "Senken", rLeft = "Ankippen", rRight = "Auskippen",
            };
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var inp = isActive ? input : ControlInput.None;
            float lf = Settings.Iso ? -inp.ry : inp.ry;
            float tl = -inp.rx;
            float slow = 1f - tool.resist * 0.5f;
            float pl = lift.pos, pt = tiltJ.pos;
            lift.Step(lf, slow, dt);
            tiltJ.Step(tl, 1f, dt);
            // Lenkung über Knickgelenk
            steer = U.Approach(steer, -inp.lx * 38f, 32f * dt);
            var sp = hinge.spring; sp.targetPosition = steer; hinge.spring = sp;
            wd.Step(inp.ly, 0f, dt);
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
            // Eindringwiderstand im Haufen bremst das Fahrzeug
            var v = front.Vel();
            float fwdV = Vector3.Dot(v, front.transform.forward);
            if ((tool.resist > 0.05f || blocked) && fwdV > 0.05f)
            {
                float F = blocked ? 260000f : tool.resist * 140000f;
                F = Mathf.Min(F, front.mass * fwdV / dt * 0.5f + 60000f);
                front.AddForceAtPosition(-front.transform.forward * F, tool.tip, ForceMode.Force);
            }
            // Schwerpunkt mit Schaufelinhalt
            front.centerOfMass = new Vector3(0, 1.1f + lift.pos * 0.4f, 1.6f + tool.load * 0.12f);
            front.mass = 7000f + tool.LoadMassKg;
            hydFlow = Mathf.Clamp01(Mathf.Abs(lf) + Mathf.Abs(tl) * 0.6f + Mathf.Abs(inp.lx) * 0.3f);
            UpdateEngine(Mathf.Max(wd.throttleOut, hydFlow * 0.8f) + 0.08f, tool.resist + relief * 0.8f + wd.throttleOut * 0.4f, dt);
            if (engine) { engine.rpm = Mathf.Max(rpm, wd.rpm); engine.beeper = wd.reversing; }
            ComputeStability(1.9f, 1.1f);
        }

        void LateUpdate()
        {
            var ft = front.transform;
            cLiftL.Update(ft.TransformPoint(new Vector3(-0.62f, 1.0f, 0.2f)), armT.TransformPoint(new Vector3(-0.62f, -0.3f, 1.4f)));
            cLiftR.Update(ft.TransformPoint(new Vector3(0.62f, 1.0f, 0.2f)), armT.TransformPoint(new Vector3(0.62f, -0.3f, 1.4f)));
            cTilt.Update(ft.TransformPoint(new Vector3(0, 1.55f, 0.95f)), bucketT.TransformPoint(new Vector3(0, 0.15f, -0.05f)));
            cSteerL.Update(transform.TransformPoint(new Vector3(-0.55f, 1.0f, -0.6f)), ft.TransformPoint(new Vector3(-0.5f, 1.0f, 0.35f)));
            cSteerR.Update(transform.TransformPoint(new Vector3(0.55f, 1.0f, -0.6f)), ft.TransformPoint(new Vector3(0.5f, 1.0f, 0.35f)));
        }

        public override Vector3 CameraTarget() { return transform.position + transform.forward * 1.5f + Vector3.up * 2.0f; }

        public override void ResetVehicle()
        {
            base.ResetVehicle();
            front.transform.SetPositionAndRotation(spawnPos, spawnRot);
            front.SetVel(Vector3.zero); front.angularVelocity = Vector3.zero;
        }

        public override List<(string, string, string)> HudLines()
        {
            string sub = tool.spillT > 0 && tool.load > 0.02f ? "verliert Material!" : tool.load > 0.02f ? Excavator.MatName(tool.mat) : "leer";
            return new List<(string, string, string)>
            {
                ("Schaufel", $"{U.Fmt(tool.load, 2)} / {U.Fmt(tool.cap)} m³", sub),
                ("Tempo", U.Fmt(Mathf.Abs(SpeedKmh)) + " km/h", wd.gear == -1 ? "Rückwärts" : $"Gang {Mathf.Max(1, wd.gear)}"),
                ("Hubhöhe", U.Fmt(tool.tip.y - transform.position.y) + " m", "Schneidkante"),
            };
        }
    }

    /// <summary>Verweist von einem Teilkörper (z. B. Vorderwagen) auf das Fahrzeug.</summary>
    public class VehiclePart : MonoBehaviour
    {
        public VehicleBase owner;
    }
}
