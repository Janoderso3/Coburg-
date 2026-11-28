using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Dreiachs-Lkw-Fahrgestell (6x4) mit Fahrerhaus.</summary>
    public abstract class TruckBase : VehicleBase
    {
        protected WheelDrive wd;
        protected Transform chassis;
        bool wasMoving;

        protected void BuildChassis(string cabMat, float massKg)
        {
            SetupBody(massKg, new Vector3(0, 1.0f, 0.2f));
            rb.inertiaTensor = new Vector3(massKg * (2.5f * 2.5f + 8.5f * 8.5f) / 12f, massKg * (2.5f * 2.5f + 8.5f * 8.5f) / 12f, massKg * (2.5f * 2.5f + 2.5f * 2.5f) / 12f);
            rb.inertiaTensorRotation = Quaternion.identity;
            SetupEngine(6, 650, 1900, 72f, 260f, 0.95f);
            chassis = transform;
            var k = new MeshKit();
            k.Box("dark_metal", new Vector3(0.45f, 0.95f, -0.3f), new Vector3(0.22f, 0.3f, 8.0f), 0.02f);
            k.Box("dark_metal", new Vector3(-0.45f, 0.95f, -0.3f), new Vector3(0.22f, 0.3f, 8.0f), 0.02f);
            for (int i = 0; i < 6; i++) k.Box("dark_metal", new Vector3(0, 0.95f, -3.8f + i * 1.4f), new Vector3(0.9f, 0.12f, 0.12f));
            foreach (float z in new[] { 3.3f, -1.5f, -2.85f }) k.CylX("dark_metal", new Vector3(0, 0.55f, z), 0.08f, 2.0f, 8);
            // Fahrerhaus
            k.Box(cabMat, new Vector3(0, 2.15f, 3.15f), new Vector3(2.45f, 1.75f, 1.9f), 0.12f);
            k.Box(cabMat, new Vector3(0, 3.2f, 3.05f), new Vector3(2.45f, 0.45f, 2.0f), 0.14f);
            k.Box("black_plastic", new Vector3(0, 1.35f, 4.12f), new Vector3(2.4f, 0.5f, 0.14f), 0.04f);
            k.Box("dark_metal", new Vector3(0, 1.85f, 4.13f), new Vector3(2.3f, 0.45f, 0.08f), 0.02f);
            for (int i = 0; i < 6; i++) k.Box("black_plastic", new Vector3(0, 1.7f + i * 0.06f, 4.18f), new Vector3(2.0f, 0.02f, 0.02f));
            k.Box(cabMat, new Vector3(0, 1.2f, 3.35f), new Vector3(2.5f, 0.35f, 1.4f), 0.06f);
            k.Box("black_plastic", new Vector3(1.35f, 2.6f, 3.95f), new Vector3(0.08f, 0.45f, 0.25f), 0.02f);
            k.Box("black_plastic", new Vector3(-1.35f, 2.6f, 3.95f), new Vector3(0.08f, 0.45f, 0.25f), 0.02f);
            k.Cyl("steel", new Vector3(-0.8f, 1.15f, 1.5f), 0.32f, 0.32f, 1.0f, 16, Quaternion.Euler(90, 0, 0));
            k.Box("black_plastic", new Vector3(0.8f, 1.15f, 1.6f), new Vector3(0.5f, 0.45f, 0.8f), 0.03f);
            k.Cyl("steel", new Vector3(0.9f, 2.8f, 2.25f), 0.07f, 0.07f, 1.3f, 10, Quaternion.identity);
            // Kotflügel
            foreach (float sx in new[] { -1.05f, 1.05f })
            {
                k.Box("black_plastic", new Vector3(sx, 1.15f, 3.3f), new Vector3(0.5f, 0.06f, 1.2f), 0.02f);
                k.Box("black_plastic", new Vector3(sx, 1.15f, -2.17f), new Vector3(0.5f, 0.06f, 2.6f), 0.02f);
            }
            k.Build(chassis, "Fahrgestell");
            var g = new MeshKit();
            g.Box("glass", new Vector3(0, 2.55f, 4.09f), new Vector3(2.3f, 0.85f, 0.06f), Quaternion.Euler(-5, 0, 0));
            g.Box("glass", new Vector3(0, 2.55f, 3.35f), new Vector3(2.48f, 0.7f, 1.1f));
            g.Build(chassis, "Scheiben", false);
            var l = new MeshKit();
            foreach (float sx in new[] { -0.85f, 0.85f })
            {
                l.Box("headlight", new Vector3(sx, 1.65f, 4.18f), new Vector3(0.35f, 0.18f, 0.05f), 0.02f);
                l.Box("taillight", new Vector3(sx * 1.2f, 1.0f, -4.3f), new Vector3(0.25f, 0.15f, 0.05f), 0.02f);
            }
            l.Build(chassis, "Leuchten", false);
            beacons.Add(Excavator.Beacon(chassis, new Vector3(-0.6f, 3.45f, 3.4f)));
            cabEye = new GameObject("Fahrerauge").transform; cabEye.SetParent(chassis, false); cabEye.localPosition = new Vector3(-0.55f, 2.75f, 3.2f);
            exhaust = new GameObject("Auspuff").transform; exhaust.SetParent(chassis, false); exhaust.localPosition = new Vector3(0.9f, 3.45f, 2.25f);
            Excavator.AddBox(chassis, new Vector3(0, 2.2f, 3.15f), new Vector3(2.5f, 2.1f, 2.0f), Layers.Vehicle);
            Excavator.AddBox(chassis, new Vector3(0, 1.0f, -0.3f), new Vector3(1.4f, 0.5f, 8.0f), Layers.Vehicle);
            wd = new WheelDrive { rb = rb, maxTorque = 2100f, finalDrive = 5.3f, speedLimit = 12.5f, idle = 650, maxRpm = 1900, shiftUp = 1750, shiftDown = 1050, brakeMax = 30000f, maxSteerDeg = 34f };
            float r = 0.53f;
            float perWheel = (massKg + 16000f) / 6f;
            float kSpring = perWheel * 9.81f / 0.1f;
            float damper = 0.8f * Mathf.Sqrt(kSpring * perWheel);
            foreach (float z in new[] { 3.3f, -1.5f, -2.85f })
                foreach (float sx in new[] { -1f, 1f })
                {
                    var vis = WheelLoader.Wheel(chassis, r, 0.42f, "steel");
                    wd.Add(chassis, new Vector3(sx * 1.02f, r + 0.15f, z), r, 300f, 0.25f, kSpring, damper, z > 0, z < 0, vis);
                }
        }

        protected void DriveStep(float dt, ControlInput inp, float extraLoad)
        {
            wd.Step(inp.ly, inp.lx, dt);
            wd.Wear(dt);
            bool moving = Mathf.Abs(SpeedKmh) > 1f;
            if (wasMoving && !moving && engine) engine.brakeHiss = 1f;
            wasMoving = moving;
            UpdateEngine(Mathf.Max(wd.throttleOut, 0.05f), wd.throttleOut * 0.7f + extraLoad, dt);
            if (engine) { engine.rpm = wd.rpm; engine.beeper = wd.reversing; }
        }

        protected string GearText => wd.gear == -1 ? "Rückwärts" : wd.gear == 0 ? "Neutral" : $"Gang {wd.gear}";

        public override ControlsInfo Controls()
        {
            return new ControlsInfo { lUp = "Gas", lDown = "Bremse/Rück", lLeft = "Lenken L", lRight = "Lenken R" };
        }
    }

    /// <summary>Kipper: Mulde mit Kipppunkt am Heck, Ladung als Volumen + Gewicht, Brocken rutschen physikalisch heraus.</summary>
    public class DumpTruck : TruckBase, ISoilCatcher, ISoilReceiver
    {
        Transform bed, gate;
        HydJoint bedJ;
        public float cargo;
        public SoilMat cargoMat = SoilMat.Soil;
        public float cap = 12f;
        Transform heap;
        HydCylinder ram;
        float spillAcc, clodAcc;
        const float L = 5.4f, Wd = 2.4f, H = 1.15f;
        public float delivered; // in Deponie abgekippt (für Missionen)

        public static DumpTruck Create(Vector3 pos, float yaw)
        {
            var go = new GameObject("Kipper");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var t = go.AddComponent<DumpTruck>();
            t.Build();
            return t;
        }

        void Build()
        {
            displayName = "Kipper-Lkw 3-Achser"; shortName = "Kipper"; camDistance = 15f;
            spawnPos = transform.position; spawnRot = transform.rotation;
            BuildChassis("paint_white", 13000f);
            bedJ = new HydJoint(0, 0, 0.9f, 0.16f, 0.6f);
            bed = new GameObject("Mulde").transform;
            bed.SetParent(chassis, false);
            bed.localPosition = new Vector3(0, 1.22f, -3.85f);
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
            // Kollision der Mulde (Brocken bleiben darin liegen)
            Excavator.AddBox(bed, new Vector3(0, 0.06f, L / 2 + 0.15f), new Vector3(Wd, 0.12f, L), Layers.Vehicle);
            Excavator.AddBox(bed, new Vector3(Wd / 2 - 0.04f, H / 2 + 0.1f, L / 2 + 0.15f), new Vector3(0.08f, H, L), Layers.Vehicle);
            Excavator.AddBox(bed, new Vector3(-Wd / 2 + 0.04f, H / 2 + 0.1f, L / 2 + 0.15f), new Vector3(0.08f, H, L), Layers.Vehicle);
            Excavator.AddBox(bed, new Vector3(0, (H + 0.35f) / 2 + 0.1f, L + 0.15f), new Vector3(Wd, H + 0.35f, 0.1f), Layers.Vehicle);
            gate = new GameObject("Heckklappe").transform;
            gate.SetParent(bed, false);
            gate.localPosition = new Vector3(0, H + 0.1f, 0.1f);
            k.Box("paint_orange", new Vector3(0, -H / 2, 0), new Vector3(Wd, H, 0.08f), 0.02f);
            k.Build(gate, "Heckklappe");
            Excavator.AddBox(gate, new Vector3(0, -H / 2, 0), new Vector3(Wd, H, 0.08f), Layers.Vehicle);
            // Ladung (Haufen)
            heap = BuildHeap(bed, Wd - 0.2f, L - 0.25f).transform;
            heap.localPosition = new Vector3(0, 0.12f, L / 2 + 0.15f);
            heap.gameObject.SetActive(false);
            ram = new HydCylinder(chassis, 0.13f, 0.09f, 1.0f, "steel");
            SoilFX.Catchers.Add(this);
        }

        static GameObject BuildHeap(Transform parent, float w, float l)
        {
            var go = new GameObject("Ladung");
            go.transform.SetParent(parent, false);
            int nx = 8, nz = 12;
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = (i / (float)nx - 0.5f) * w, z = (j / (float)nz - 0.5f) * l;
                    float u = x / (w / 2), q = z / (l / 2);
                    float y = 1f + 0.35f * (1 - u * u) * (1 - q * q * 0.6f) + U.Noise(x * 3, z * 3) * 0.05f;
                    v.Add(new Vector3(x, y, z)); uv.Add(new Vector2(x, z));
                }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
            // Seiten als einfacher Kasten unter der Oberfläche
            int b0 = v.Count;
            foreach (var p in new[] { new Vector3(-w / 2, 0, -l / 2), new Vector3(w / 2, 0, -l / 2), new Vector3(w / 2, 0, l / 2), new Vector3(-w / 2, 0, l / 2) }) { v.Add(p); uv.Add(new Vector2(p.x, p.z)); }
            var m = new Mesh();
            m.SetVertices(v); m.SetTriangles(t, 0); m.SetUVs(0, uv);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Get("soil_chunk");
            return go;
        }

        public float PayloadT => cargo * SoilTool.Density(cargoMat);

        public bool TryCatch(Vector3 p, Vector3 v, float vol, SoilMat mat)
        {
            var lp = bed.InverseTransformPoint(p);
            if (Mathf.Abs(lp.x) > Wd / 2 - 0.05f || lp.z < 0.2f || lp.z > L + 0.1f) return false;
            float fill = cargo / cap;
            float top = 0.15f + fill * 1.35f;
            if (lp.y > top + 0.25f || lp.y < -0.4f) return false;
            if (cargo >= cap * 1.05f) return false;
            Receive(vol, mat);
            return true;
        }

        public void Receive(float vol, SoilMat mat)
        {
            if (cargo < 0.05f || vol > cargo * 0.1f) cargoMat = mat;
            cargo += vol;
        }

        public override ControlsInfo Controls()
        {
            var c = base.Controls();
            c.rUp = "Mulde heben"; c.rDown = "Mulde senken";
            return c;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var inp = isActive ? input : ControlInput.None;
            float cmd = inp.ry > 0.2f ? 1 : inp.ry < -0.2f ? -1.4f : 0;
            bedJ.Step(cmd, 1f, dt);
            bed.localRotation = Quaternion.Euler(-bedJ.pos * Mathf.Rad2Deg, 0, 0);
            gate.localRotation = Quaternion.Euler(bedJ.pos * 0.92f * Mathf.Rad2Deg, 0, 0);
            rb.mass = 13000f + PayloadT * 1000f;
            rb.centerOfMass = new Vector3(0, 1.1f + cargo * 0.04f, 0.2f - cargo / cap * 1.4f);
            hydFlow = Mathf.Abs(bedJ.vel) * 5f;
            DriveStep(dt, inp, hydFlow * 0.4f);
            if (engine) engine.hydFlow = hydFlow;
            // Abkippen ab ca. 22°
            if (cargo > 0 && bedJ.pos > 0.38f)
            {
                float flow = Mathf.Min(cargo, cap * 0.32f * (bedJ.pos - 0.36f) * dt * 3f);
                cargo -= flow;
                spillAcc += flow; clodAcc += flow;
                const float chunk = 0.06f;
                var dir = transform.forward;
                while (spillAcc > chunk)
                {
                    spillAcc -= chunk;
                    var p = bed.TransformPoint(new Vector3(Random.Range(-1f, 1f), Random.Range(0.25f, 0.55f), -0.1f));
                    SoilFX.I.SpawnFalling(p, -dir * 1.2f + Random.insideUnitSphere * 0.3f + Vector3.down * 0.3f, chunk, cargoMat);
                }
                if (clodAcc > 0.5f && cargoMat != SoilMat.Concrete)
                {
                    float cv = clodAcc * 0.2f; clodAcc = 0; spillAcc -= cv;
                    SoilFX.I.SpawnClod(bed.TransformPoint(new Vector3(0, 0.5f, 0.3f)), cv, cargoMat, -dir * 1.5f);
                }
                if (Random.value < 0.15f) SoundFX.I?.Play("soil", transform.position - dir * 5f, 0.9f, 0.15f);
            }
            ComputeStability(3.2f, 1.1f);
        }

        void LateUpdate()
        {
            float f = cargo / cap;
            bool vis = f > 0.01f;
            if (heap.gameObject.activeSelf != vis) heap.gameObject.SetActive(vis);
            if (vis)
            {
                heap.localScale = new Vector3(1, Mathf.Max(0.05f, f * 1.15f), 1);
                var key = cargoMat == SoilMat.Rubble ? "rubble_chunk" : cargoMat == SoilMat.Gravel ? "gravel_chunk" : cargoMat == SoilMat.Concrete ? "concrete_wet" : "soil_chunk";
                var r = heap.GetComponent<MeshRenderer>();
                if (r.sharedMaterial != Mats.Get(key)) r.sharedMaterial = Mats.Get(key);
            }
            ram.SetVisible(bedJ.pos > 0.02f);
            ram.Update(chassis.TransformPoint(new Vector3(0, 1.05f, 1.45f)), bed.TransformPoint(new Vector3(0, 0.05f, 5.2f)));
        }

        public override List<(string, string, string)> HudLines()
        {
            float t = PayloadT;
            return new List<(string, string, string)>
            {
                ("Ladung", $"{U.Fmt(cargo)} / {U.Fmt(cap, 0)} m³", cargo > 0.05f ? $"{Excavator.MatName(cargoMat)}, {U.Fmt(t)} t" : "leer"),
                ("Tempo", U.Fmt(Mathf.Abs(SpeedKmh), 0) + " km/h", GearText),
                ("Mulde", Mathf.RoundToInt(bedJ.pos * Mathf.Rad2Deg) + "°", t > 16f ? "überladen!" : ""),
            };
        }

        void OnDestroy() { SoilFX.Catchers.Remove(this); }
    }

    /// <summary>Fahrmischer: 9 m³ Trommel, Schurre schwenkbar, Nachladen am Betonwerk.</summary>
    public class MixerTruck : TruckBase
    {
        Transform drum, chute, chuteArm;
        public float concrete = 9f, cap = 9f;
        public bool pouring;
        float chuteYaw, chuteP = 0.5f, pourAcc;
        public static Vector3 PlantPos = new Vector3(-50, 0, 46);
        bool fullMsg;

        public static MixerTruck Create(Vector3 pos, float yaw)
        {
            var go = new GameObject("Fahrmischer");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var t = go.AddComponent<MixerTruck>();
            t.Build();
            return t;
        }

        void Build()
        {
            displayName = "Fahrmischer 9 m³"; shortName = "Mischer"; camDistance = 15f;
            spawnPos = transform.position; spawnRot = transform.rotation;
            BuildChassis("paint_blue", 14000f);
            var dg = new GameObject("Trommelachse").transform;
            dg.SetParent(chassis, false);
            dg.localPosition = new Vector3(0, 2.45f, -0.9f);
            dg.localRotation = Quaternion.Euler(11, 0, 0);
            drum = new GameObject("Trommel").transform;
            drum.SetParent(dg, false);
            var prof = new List<Vector2>();
            for (int i = 0; i <= 14; i++)
            {
                float t = i / 14f;
                float r = t < 0.12f ? 0.55f + t * 3.5f : t < 0.6f ? 1.0f : 1.0f - (t - 0.6f) * 1.4f;
                prof.Add(new Vector2(Mathf.Max(0.35f, r), (t - 0.5f) * 5.0f));
            }
            var k = new MeshKit();
            k.Lathe("drum", prof.ToArray(), 28, Vector3.zero, Quaternion.Euler(-90, 0, 0));
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * 360f;
                var q = Quaternion.Euler(0, 0, a);
                k.Box("paint_red", q * new Vector3(1.0f, 0, 0) + new Vector3(0, 0, 0.2f), new Vector3(0.05f, 0.12f, 3.2f), q, 0.01f);
            }
            k.Build(drum, "Trommel");
            k.Box("dark_metal", new Vector3(0, 1.25f, -0.9f), new Vector3(1.4f, 0.3f, 4.6f), 0.03f);
            k.Box("paint_blue", new Vector3(0, 1.8f, 1.4f), new Vector3(1.3f, 1.0f, 0.25f), 0.05f);
            k.Box("steel", new Vector3(0, 3.3f, -3.6f), new Vector3(0.9f, 0.7f, 0.9f), 0.04f);
            k.Build(chassis, "Mischeraufbau");
            Excavator.AddBox(chassis, new Vector3(0, 2.45f, -0.9f), new Vector3(2.0f, 2.0f, 5.0f), Layers.Vehicle);
            chute = new GameObject("Schurre").transform;
            chute.SetParent(chassis, false);
            chute.localPosition = new Vector3(0, 2.2f, -3.95f);
            chuteArm = new GameObject("Schurrenarm").transform;
            chuteArm.SetParent(chute, false);
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

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var inp = isActive ? input : ControlInput.None;
            chuteYaw = Mathf.Clamp(chuteYaw + inp.rx * 0.8f * dt, -1.2f, 1.2f);
            chuteP = Mathf.Clamp(chuteP - inp.ry * 0.5f * dt, 0.15f, 0.9f);
            chute.localRotation = Quaternion.Euler(0, chuteYaw * Mathf.Rad2Deg, 0);
            chuteArm.localRotation = Quaternion.Euler(-chuteP * Mathf.Rad2Deg, 0, 0);
            rb.mass = 14000f + concrete * 2350f;
            DriveStep(dt, inp, pouring ? 0.35f : 0.1f);
            if (pouring)
            {
                if (concrete <= 0) { pouring = false; Hud.Toast("Fahrmischer leer.", ToastKind.Info); }
                else
                {
                    float v = Mathf.Min(0.45f * dt, concrete);
                    concrete -= v; pourAcc += v;
                    var end = chuteArm.TransformPoint(new Vector3(0, 0, -1.8f));
                    var dir = chuteArm.TransformDirection(new Vector3(0, 0, -1));
                    while (pourAcc > 0.03f)
                    {
                        pourAcc -= 0.03f;
                        SoilFX.I.SpawnFalling(end + Random.insideUnitSphere * 0.1f, dir * 1.2f + rb.Vel(), 0.03f, SoilMat.Concrete, 0.12f);
                    }
                    if (Random.value < 0.1f) SoundFX.I?.Play("pour", end, 0.5f, 0.6f);
                }
            }
            // Betonwerk: Einfülltrichter unter dem Silo
            var hopper = transform.TransformPoint(new Vector3(0, 0, -3.6f));
            var d = new Vector2(hopper.x - PlantPos.x, hopper.z - PlantPos.z).magnitude;
            if (d < 2.2f && Mathf.Abs(SpeedKmh) < 1f && concrete < cap)
            {
                concrete = Mathf.Min(cap, concrete + dt * 1.0f);
                fullMsg = false;
                if (Random.value < 0.05f) SoilFX.I.Dust(new Vector3(PlantPos.x, transform.position.y + 3.8f, PlantPos.z), 1, new Color(0.8f, 0.8f, 0.78f), 0.8f);
                if (concrete >= cap && !fullMsg) { fullMsg = true; Hud.Toast("Fahrmischer beladen: 9 m³ Beton C25/30.", ToastKind.Ok); }
            }
            hydFlow = pouring ? 0.5f : 0.15f;
            ComputeStability(3.2f, 1.1f);
        }

        void LateUpdate()
        {
            drum.Rotate(0, 0, (pouring ? -150f : 55f) * Time.deltaTime, Space.Self);
        }

        public float HopperDistance()
        {
            var hopper = transform.TransformPoint(new Vector3(0, 0, -3.6f));
            return new Vector2(hopper.x - PlantPos.x, hopper.z - PlantPos.z).magnitude;
        }

        public override List<(string, string, string)> HudLines()
        {
            float hd = HopperDistance();
            return new List<(string, string, string)>
            {
                ("Beton", $"{U.Fmt(concrete)} / {U.Fmt(cap, 0)} m³", pouring ? "Entleeren…" : "Mischen"),
                ("Tempo", U.Fmt(Mathf.Abs(SpeedKmh), 0) + " km/h", GearText),
                hd < 12f ? ("Trichter", U.Fmt(hd) + " m", "zum Silo") : ("Schurre", Mathf.RoundToInt(chuteYaw * Mathf.Rad2Deg) + "°", ""),
            };
        }
    }
}
