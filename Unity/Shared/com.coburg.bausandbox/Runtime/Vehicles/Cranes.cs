using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Seil als gestreckter Zylinder.</summary>
    public class RopeVis
    {
        readonly Transform t;
        static Mesh unit;
        public RopeVis(Transform parent, float r = 0.022f)
        {
            if (!unit) { var k = new MeshKit(); k.Cyl("x", new Vector3(0, 0.5f, 0), 1, 1, 1, 6, Quaternion.identity, false); unit = k.BuildMesh(out _); }
            var go = new GameObject("Seil");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = unit;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Get("dark_metal");
            t = go.transform;
            t.localScale = new Vector3(r, 1, r);
            this.r = r;
        }
        readonly float r;
        public void Set(Vector3 a, Vector3 b)
        {
            var d = b - a; float L = d.magnitude;
            if (L < 1e-4f) return;
            t.position = a;
            t.rotation = Quaternion.FromToRotation(Vector3.up, d / L);
            t.localScale = new Vector3(r, L, r);
        }
        public void Show(bool v) { t.gameObject.SetActive(v); }
    }

    public static class Lattice
    {
        /// <summary>Fachwerkstab zwischen zwei Punkten.</summary>
        public static void Bar(MeshKit k, string mat, Vector3 a, Vector3 b, float t)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            var up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            k.Box(mat, (a + b) * 0.5f, new Vector3(t, t, d.magnitude), Quaternion.LookRotation(d.normalized, up));
        }
    }

    /// <summary>
    /// Turmdrehkran (40 m Ausleger). Haken hängt an einem Seil-Gelenk (Pendel), Last an Anschlagketten (Doppelpendel).
    /// Traglastkurve: 10 t bis 14 m, danach 140 t·m. Lastmomentbegrenzung sperrt Katze aus / Heben.
    /// </summary>
    public class TowerCrane : VehicleBase
    {
        public float H = 30f, jib = 40f;
        Transform slewT, trolleyVis;
        public Rigidbody trolley, hook;
        ConfigurableJoint rope;
        public float slew = -0.8f, slewV, r = 15f, rV, L = 12f, LV;
        public Element load;
        ConfigurableJoint sling;
        float releaseArm;
        RopeVis rope1, rope2;
        readonly RopeVis[] slings = new RopeVis[4];
        public float baseY;
        public int dropped;

        public static TowerCrane Create(Vector3 pos)
        {
            var go = new GameObject("Turmdrehkran");
            go.transform.position = pos;
            var c = go.AddComponent<TowerCrane>();
            c.Build();
            return c;
        }

        void Build()
        {
            displayName = "Turmdrehkran 40 m"; shortName = "Kran"; camDistance = 34f;
            baseY = transform.position.y;
            var k = new MeshKit();
            k.Box("concrete", new Vector3(0, 0.4f, 0), new Vector3(5, 0.8f, 5), 0.05f);
            foreach (var (bx, bz) in new[] { (-1.6f, -1.6f), (1.6f, -1.6f), (-1.6f, 1.6f), (1.6f, 1.6f) }) k.Box("concrete", new Vector3(bx, 1.25f, bz), new Vector3(1.4f, 0.9f, 1.4f), 0.04f);
            float w = 0.85f, seg = 2.5f, top = H + 1;
            var c4 = new[] { new Vector2(-w, -w), new Vector2(w, -w), new Vector2(w, w), new Vector2(-w, w) };
            for (float y = 0.8f; y < top - 0.01f; y += seg)
            {
                float y1 = Mathf.Min(top, y + seg);
                for (int i = 0; i < 4; i++)
                {
                    var a = c4[i]; var b = c4[(i + 1) % 4];
                    Lattice.Bar(k, "paint_yellow", new Vector3(a.x, y, a.y), new Vector3(a.x, y1, a.y), 0.16f);
                    Lattice.Bar(k, "paint_yellow", new Vector3(a.x, y1, a.y), new Vector3(b.x, y1, b.y), 0.08f);
                    Lattice.Bar(k, "paint_yellow", new Vector3(a.x, y, a.y), new Vector3(b.x, y1, b.y), 0.07f);
                }
            }
            // Leiter
            for (float y = 1f; y < top; y += 0.3f) Lattice.Bar(k, "steel", new Vector3(-0.3f, y, -w - 0.05f), new Vector3(0.3f, y, -w - 0.05f), 0.03f);
            k.Build(transform, "Turm");
            Excavator.AddBox(transform, new Vector3(0, 1.0f, 0), new Vector3(5, 2, 5), Layers.Building);
            var mast = new GameObject("Mastkollision"); mast.layer = Layers.Building; mast.transform.SetParent(transform, false);
            var mb = mast.AddComponent<BoxCollider>(); mb.center = new Vector3(0, top / 2, 0); mb.size = new Vector3(1.8f, top, 1.8f);

            slewT = new GameObject("Drehteil").transform;
            slewT.SetParent(transform, false);
            slewT.localPosition = new Vector3(0, top, 0);
            var apex = new Vector3(0, 7, 0);
            foreach (var c in c4) Lattice.Bar(k, "paint_yellow", new Vector3(c.x, 0.4f, c.y), apex, 0.14f);
            float Lj = jib + 1.5f;
            for (float z = 1; z < Lj - 0.01f; z += 2)
            {
                float z1 = Mathf.Min(Lj, z + 2);
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
            foreach (var c in c4) Lattice.Bar(k, "paint_yellow", new Vector3(c.x, 0, c.y), new Vector3(Mathf.Sign(c.x) * 0.65f, 0, c.y > 0 ? 1.2f : -1.2f), 0.14f);
            k.Box("concrete", new Vector3(0, -0.9f, -11.6f), new Vector3(2.3f, 2.2f, 2.6f), 0.05f);
            k.Box("dark_metal", new Vector3(0, 0.9f, -7.5f), new Vector3(1.4f, 1.0f, 1.6f), 0.05f);
            k.Box("steel", new Vector3(0, 0.35f, -6.5f), new Vector3(1.9f, 0.08f, 11.5f));
            k.Box("paint_yellow", new Vector3(0, 0.2f, 0), new Vector3(1.8f, 0.4f, 1.8f), 0.05f);
            k.Box("paint_white", new Vector3(-1.55f, 0.2f, 1.2f), new Vector3(1.3f, 1.9f, 1.6f), 0.1f);
            k.Build(slewT, "Ausleger");
            var g = new MeshKit();
            g.Box("glass", new Vector3(-1.55f, 0.45f, 1.25f), new Vector3(1.32f, 1.2f, 1.62f));
            g.Build(slewT, "Kabinenglas", false);
            beacons.Add(Excavator.Beacon(slewT, new Vector3(0, 7.05f, 0)));
            cabEye = new GameObject("Kranführer").transform; cabEye.SetParent(slewT, false); cabEye.localPosition = new Vector3(-1.55f, 0.6f, 1.9f);
            trolleyVis = new GameObject("Laufkatze").transform;
            trolleyVis.SetParent(slewT, false);
            k.Box("dark_metal", new Vector3(0, -0.25f, 0), new Vector3(1.5f, 0.35f, 1.3f), 0.04f);
            k.CylX("steel", new Vector3(0, -0.55f, 0.3f), 0.22f, 1.2f, 14);
            k.CylX("steel", new Vector3(0, -0.55f, -0.3f), 0.22f, 1.2f, 14);
            k.Build(trolleyVis, "Laufkatze");
            // Kinematischer Katzkörper als Seilaufhängung
            var tgo = new GameObject("Katzaufhängung");
            trolley = tgo.AddComponent<Rigidbody>();
            trolley.isKinematic = true;
            trolley.interpolation = RigidbodyInterpolation.Interpolate;
            tgo.transform.position = TrolleyWorld();
            // Haken (Unterflasche)
            var hgo = new GameObject("Kranhaken");
            hgo.layer = Layers.Hook;
            hgo.transform.position = TrolleyWorld() + Vector3.down * L;
            k.Box("paint_red", new Vector3(0, 0.35f, 0), new Vector3(0.7f, 0.8f, 0.35f), 0.06f);
            k.CylX("steel", new Vector3(0, 0.5f, 0), 0.25f, 0.4f, 14);
            k.Cyl("dark_metal", new Vector3(0, -0.15f, 0), 0.05f, 0.05f, 0.35f, 8, Quaternion.identity);
            k.Cyl("dark_metal", new Vector3(0, -0.45f, 0), 0.06f, 0.15f, 0.3f, 10, Quaternion.identity);
            k.Build(hgo.transform, "Unterflasche");
            var hc = hgo.AddComponent<BoxCollider>(); hc.center = new Vector3(0, 0.25f, 0); hc.size = new Vector3(0.7f, 1.0f, 0.4f); hc.sharedMaterial = U.SteelMat;
            hook = hgo.AddComponent<Rigidbody>();
            hook.mass = 800f;
            hook.interpolation = RigidbodyInterpolation.Interpolate;
            hook.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            hook.SetDamping(0.01f, 0.5f);
            hook.solverIterations = 25;
            rope = hgo.AddComponent<ConfigurableJoint>();
            rope.connectedBody = trolley;
            rope.autoConfigureConnectedAnchor = false;
            rope.anchor = new Vector3(0, 0.7f, 0);
            rope.connectedAnchor = Vector3.zero;
            rope.xMotion = rope.yMotion = rope.zMotion = ConfigurableJointMotion.Limited;
            rope.angularXMotion = rope.angularYMotion = rope.angularZMotion = ConfigurableJointMotion.Free;
            rope.linearLimit = new SoftJointLimit { limit = L };
            rope.enableCollision = false;
            rope1 = new RopeVis(transform); rope2 = new RopeVis(transform);
            for (int i = 0; i < 4; i++) { slings[i] = new RopeVis(transform, 0.015f); slings[i].Show(false); }
            var mgo = new GameObject("Kranmotor");
            mgo.transform.SetParent(slewT, false);
            mgo.AddComponent<AudioSource>();
            engine = mgo.AddComponent<EngineSynth>();
            engine.electric = 1f;
            ApplyTransforms();
        }

        Vector3 TrolleyWorld()
        {
            return transform.position + new Vector3(Mathf.Sin(slew) * r, H + 1 - 0.75f, Mathf.Cos(slew) * r);
        }

        public float MaxLoad(float rad) { return Mathf.Min(10f, 140f / Mathf.Max(rad, 1f)); }
        public float MaxLoad() { return MaxLoad(r); }
        public float LoadT => load ? load.MassT : 0f;
        public Vector3 HookPos => hook.position;

        void ApplyTransforms()
        {
            slewT.localRotation = Quaternion.Euler(0, slew * Mathf.Rad2Deg, 0);
            trolleyVis.localPosition = new Vector3(0, -0.05f, r);
        }

        public override ControlsInfo Controls()
        {
            var c = new ControlsInfo { lUp = "Katze aus", lDown = "Katze ein", lLeft = "Drehen L", lRight = "Drehen R", rUp = "Heben", rDown = "Senken" };
            c.buttons.Add(("hook", load ? "Ablassen" : "Einhängen", "Last"));
            return c;
        }

        public override bool OnButton(string id)
        {
            if (id != "hook") return false;
            if (!load)
            {
                Element best = null; float bd = 2.5f;
                if (Buildings.I)
                    foreach (var el in Buildings.I.elements)
                    {
                        if (!el || el.state == "hooked" || el.state == "placed") continue;
                        float d = Vector3.Distance(el.LiftPointWorld, hook.position - Vector3.up * 0.6f);
                        if (d < bd) { bd = d; best = el; }
                    }
                if (!best) { Hud.Toast("Kein Fertigteil am Haken. Haken über den Anschlagpunkt bringen und absenken.", ToastKind.Warn); return true; }
                if (best.MassT > MaxLoad()) Hud.Toast($"Achtung: {U.Fmt(best.MassT)} t – zulässig bei {U.Fmt(r, 0)} m nur {U.Fmt(MaxLoad())} t!", ToastKind.Warn);
                Attach(best);
                Hud.Toast($"{best.def.name} angeschlagen ({U.Fmt(best.MassT)} t).", ToastKind.Ok);
                SoundFX.I?.Play("clank", hook.position, 0.8f);
                return true;
            }
            var site = load.site;
            if (site != null && site.CanPlace(load))
            {
                var el = load;
                Detach();
                site.Place(el);
                return true;
            }
            float above = load.HeightAboveGround();
            if (above > 0.3f && releaseArm <= 0)
            {
                releaseArm = 2.5f;
                Hud.Toast($"Last schwebt {U.Fmt(above)} m über Grund! Erneut tippen zum Ausklinken.", ToastKind.Warn);
                return true;
            }
            releaseArm = 0;
            var e = load;
            Detach();
            if (above > 0.3f) dropped++;
            Hud.Toast($"{e.def.name} ausgeklinkt.", ToastKind.Info);
            return true;
        }

        void Attach(Element el)
        {
            load = el;
            el.state = "hooked";
            sling = el.gameObject.AddComponent<ConfigurableJoint>();
            sling.connectedBody = hook;
            sling.autoConfigureConnectedAnchor = false;
            sling.anchor = el.liftLocal;
            sling.connectedAnchor = new Vector3(0, -0.55f, 0);
            sling.xMotion = sling.yMotion = sling.zMotion = ConfigurableJointMotion.Limited;
            sling.linearLimit = new SoftJointLimit { limit = 0.35f };
            sling.angularXMotion = sling.angularYMotion = sling.angularZMotion = ConfigurableJointMotion.Free;
            el.rb.isKinematic = false;
            el.rb.WakeUp();
        }

        void Detach()
        {
            if (sling) Destroy(sling);
            sling = null;
            if (load) load.state = "loose";
            load = null;
            foreach (var s in slings) s.Show(false);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var inp = isActive ? input : ControlInput.None;
            float sl = inp.lx, tr = inp.ly, ho = inp.ry;
            releaseArm = Mathf.Max(0, releaseArm - dt);
            float mass = LoadT;
            bool overload = mass > MaxLoad();
            if (overload && tr > 0) tr = 0;
            // Frequenzumrichter: sanfte Rampen
            slewV = U.Approach(slewV, sl * 0.11f, 0.045f * dt);
            rV = U.Approach(rV, tr * 1.0f, 0.6f * dt);
            float hoistMax = mass > 0 ? 1.0f : 1.6f;
            LV = U.Approach(LV, -ho * hoistMax, 1.5f * dt);
            if (overload && LV < 0) { LV = 0; if (ho > 0.2f && Random.value < 0.02f) Hud.Toast("Überlast! Last zu schwer für diese Ausladung – Katze einfahren.", ToastKind.Warn); }
            slew += slewV * dt;
            r = Mathf.Clamp(r + rV * dt, 3.5f, jib - 1f);
            if (r <= 3.5f || r >= jib - 1f) rV = 0;
            float maxL = H + 1f - (SoilTerrain.I ? SoilTerrain.I.Height(hook.position.x, hook.position.z) - baseY : 0f) + 2f;
            L = Mathf.Clamp(L + LV * dt, 1.5f, maxL);
            rope.linearLimit = new SoftJointLimit { limit = L };
            ApplyTransforms();
            trolley.MovePosition(TrolleyWorld());
            // Monteure führen das Teil mit Leitseilen
            if (load) Guide(dt);
            hydFlow = Mathf.Clamp01(Mathf.Abs(slewV) * 8f + Mathf.Abs(rV) * 0.6f + Mathf.Abs(LV) * 0.6f);
            if (engine) { engine.hydFlow = hydFlow; engine.volume = Settings.Volume * (isActive ? 1f : 0.4f); engine.on = true; }
        }

        void Guide(float dt)
        {
            var el = load;
            if (el.site == null || !el.def.hasTarget) return;
            var tgt = el.def.target;
            var p = el.rb.position;
            float dx = tgt.x - p.x, dz = tgt.z - p.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < 8f)
            {
                float yawErr = Mathf.DeltaAngle(el.transform.eulerAngles.y, el.def.targetYaw);
                el.rb.AddTorque(Vector3.up * (yawErr * Mathf.Deg2Rad * 4f - el.rb.angularVelocity.y * 3f) * el.rb.mass * 1.5f, ForceMode.Force);
            }
            if (d < 1.2f && p.y - tgt.y < 1.5f)
            {
                var v = el.rb.Vel();
                var f = new Vector3(dx * 3f - v.x * 2.5f, 0, dz * 3f - v.z * 2.5f) * el.rb.mass;
                el.rb.AddForce(f, ForceMode.Force);
            }
        }

        void LateUpdate()
        {
            var t = trolleyVis.position + Vector3.down * 0.4f;
            var hp = hook.transform.TransformPoint(new Vector3(0, 0.7f, 0));
            var side = slewT.right * 0.25f;
            rope1.Set(t + side, hp + side);
            rope2.Set(t - side, hp - side);
            if (load)
            {
                var hb = hook.transform.TransformPoint(new Vector3(0, -0.55f, 0));
                var c = load.SlingPoints();
                for (int i = 0; i < 4; i++) { slings[i].Show(true); slings[i].Set(hb, c[i]); }
            }
        }

        public override Vector3 CameraTarget()
        {
            var hp = hook.position;
            return new Vector3((hp.x * 2 + transform.position.x) / 3, hp.y + 2, (hp.z * 2 + transform.position.z) / 3);
        }
        public override float CameraYaw() { return slew * Mathf.Rad2Deg; }
        public override float SpeedKmh => 0;

        public override void Cabin(out Vector3 pos, out Vector3 fwd)
        {
            pos = cabEye.position;
            fwd = (hook.position - pos).normalized;
        }

        public override List<(string, string, string)> HudLines()
        {
            float g = SoilTerrain.I ? SoilTerrain.I.Height(hook.position.x, hook.position.z) : 0;
            float mass = LoadT, ml = MaxLoad();
            int deg = Mathf.RoundToInt(Mathf.Repeat(slew * Mathf.Rad2Deg, 360));
            float sway = Vector3.ProjectOnPlane(hook.Vel(), Vector3.up).magnitude;
            return new List<(string, string, string)>
            {
                ("Ausladung", U.Fmt(r) + " m", $"Drehwinkel {deg}°"),
                ("Haken", U.Fmt(hook.position.y - g) + " m", sway > 0.6f ? $"pendelt {U.Fmt(sway)} m/s" : "über Grund"),
                ("Last", $"{U.Fmt(mass)} t / {U.Fmt(ml)} t", mass > ml ? "ÜBERLAST!" : load ? load.def.name : "ohne Last"),
            };
        }

        public override void ResetVehicle()
        {
            if (load) Detach();
            slew = -0.8f; r = 15; L = 12; slewV = rV = LV = 0;
            ApplyTransforms();
            trolley.position = TrolleyWorld();
            hook.position = TrolleyWorld() + Vector3.down * L;
            hook.SetVel(Vector3.zero);
        }
    }

    /// <summary>Seilbagger mit 3-t-Abrissbirne. Schwung entsteht durch Drehen des Oberwagens.</summary>
    public class WreckingCrane : VehicleBase
    {
        Transform upper, boomT;
        HydJoint swing, luff;
        public float ropeL = 9f, ropeV;
        public Rigidbody ball;
        ConfigurableJoint rope;
        TrackDrive tracks;
        RopeVis hoist, pendant;
        Material trackMatL, trackMatR;
        public bool workMode = true;
        const float BoomLen = 19f, BallR = 0.55f;

        public static WreckingCrane Create(Vector3 pos, float yaw)
        {
            var go = new GameObject("Seilbagger");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var w = go.AddComponent<WreckingCrane>();
            w.Build();
            return w;
        }

        void Build()
        {
            displayName = "Seilbagger mit Abrissbirne"; shortName = "Birne"; camDistance = 26f;
            spawnPos = transform.position; spawnRot = transform.rotation;
            SetupBody(55000f, new Vector3(0, 1.2f, -0.8f));
            rb.inertiaTensor = new Vector3(55000 * (4f * 4f + 5.5f * 5.5f) / 12f * 1.5f, 55000 * (4f * 4f + 5.5f * 5.5f) / 12f * 1.5f, 55000 * (4f * 4f + 4f * 4f) / 12f * 1.5f);
            rb.inertiaTensorRotation = Quaternion.identity;
            SetupEngine(6, 700, 1900, 80f, 300f, 1.1f);
            swing = new HydJoint(0, -1e6f, 1e6f, 0.32f, 0.35f);
            luff = new HydJoint(1.12f, 0.55f, 1.3f, 0.16f, 0.45f);
            var k = new MeshKit();
            k.Box("dark_metal", new Vector3(0, 0.7f, 0), new Vector3(2.2f, 0.6f, 2.8f), 0.05f);
            foreach (float sx in new[] { -1.6f, 1.6f })
            {
                k.Box("dark_metal", new Vector3(sx, 0.5f, 0), new Vector3(0.45f, 0.5f, 4.4f), 0.05f);
                k.CylX("steel", new Vector3(sx, 0.45f, 2.2f), 0.38f, 0.6f, 16);
                k.CylX("dark_metal", new Vector3(sx, 0.47f, -2.2f), 0.42f, 0.62f, 16);
            }
            k.Build(transform, "Unterwagen");
            trackMatL = new Material(Mats.Get("track")); trackMatR = new Material(Mats.Get("track"));
            foreach (float sx in new[] { -1.6f, 1.6f })
            {
                k.TrackBelt("track", sx, 0.47f, 5.3f, 0.47f, 0.75f);
                k.Build(transform, "Kette").GetComponent<MeshRenderer>().sharedMaterial = sx < 0 ? trackMatL : trackMatR;
            }
            Excavator.AddBox(transform, new Vector3(0, 0.85f, 0), new Vector3(3.6f, 0.8f, 4.6f), Layers.Vehicle);
            upper = new GameObject("Oberwagen").transform;
            upper.SetParent(transform, false);
            upper.localPosition = new Vector3(0, 1.15f, 0);
            k.Box("dark_metal", new Vector3(0, 0.15f, -0.6f), new Vector3(3.0f, 0.3f, 4.8f), 0.04f);
            k.Box("paint_red", new Vector3(0, 1.05f, -1.4f), new Vector3(2.9f, 1.5f, 3.0f), 0.12f);
            k.Box("dark_metal", new Vector3(0, 0.9f, -3.3f), new Vector3(3.0f, 1.2f, 1.0f), 0.1f);
            k.Box("paint_red", new Vector3(-1.0f, 1.2f, 1.0f), new Vector3(1.0f, 1.8f, 1.6f), 0.08f);
            k.CylX("steel", new Vector3(0, 1.2f, -1.0f), 0.45f, 1.2f, 18);
            k.Cyl("steel", new Vector3(1.0f, 2.0f, -2.2f), 0.07f, 0.07f, 0.5f, 10, Quaternion.identity);
            var apex = new Vector3(0, 3.6f, -1.6f);
            foreach (var p in new[] { new Vector3(-0.9f, 0.3f, -2.6f), new Vector3(0.9f, 0.3f, -2.6f), new Vector3(-0.9f, 0.3f, -0.2f), new Vector3(0.9f, 0.3f, -0.2f) })
                Lattice.Bar(k, "dark_metal", p, apex, 0.15f);
            k.Build(upper, "Oberwagen");
            var g = new MeshKit();
            g.Box("glass", new Vector3(-1.0f, 1.55f, 1.05f), new Vector3(1.02f, 1.0f, 1.5f));
            g.Build(upper, "Scheiben", false);
            beacons.Add(Excavator.Beacon(upper, new Vector3(-1.0f, 2.15f, 0.6f)));
            cabEye = new GameObject("Fahrerauge").transform; cabEye.SetParent(upper, false); cabEye.localPosition = new Vector3(-1.0f, 1.7f, 1.3f);
            exhaust = new GameObject("Auspuff").transform; exhaust.SetParent(upper, false); exhaust.localPosition = new Vector3(1.0f, 2.25f, -2.2f);
            Excavator.AddBox(upper, new Vector3(0, 1.0f, -1.2f), new Vector3(3.0f, 1.8f, 4.6f), Layers.Vehicle);
            boomT = new GameObject("Gittermast").transform;
            boomT.SetParent(upper, false);
            boomT.localPosition = new Vector3(0, 0.6f, 1.2f);
            for (float z = 0; z < BoomLen - 0.01f; z += 1.5f)
            {
                float z1 = Mathf.Min(BoomLen, z + 1.5f);
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
            Excavator.AddBeamCollider(boomT, Vector3.zero, new Vector3(0, 0, BoomLen), 0.9f, 0.9f);
            hoist = new RopeVis(transform, 0.03f);
            pendant = new RopeVis(transform, 0.03f);
            tracks = new TrackDrive();
            tracks.Init(rb, transform, 3.2f, 4.6f, 0f, 0.9f, 7);
            ApplyJoints();
            // Birne
            var bgo = new GameObject("Abrissbirne");
            bgo.layer = Layers.Hook;
            var tip = BoomTip();
            bgo.transform.position = tip + Vector3.down * ropeL;
            k.Sphere("dark_metal", Vector3.zero, BallR, 20);
            k.Cyl("steel", new Vector3(0, BallR + 0.15f, 0), 0.08f, 0.12f, 0.3f, 10, Quaternion.identity);
            k.Build(bgo.transform, "Birne");
            var sc = bgo.AddComponent<SphereCollider>(); sc.radius = BallR; sc.sharedMaterial = U.SteelMat;
            ball = bgo.AddComponent<Rigidbody>();
            ball.mass = 3000f;
            ball.interpolation = RigidbodyInterpolation.Interpolate;
            ball.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            ball.SetDamping(0.005f, 0.2f);
            ball.solverIterations = 25;
            bgo.AddComponent<BallImpact>();
            rope = bgo.AddComponent<ConfigurableJoint>();
            rope.connectedBody = rb;
            rope.autoConfigureConnectedAnchor = false;
            rope.anchor = new Vector3(0, BallR + 0.3f, 0);
            rope.connectedAnchor = transform.InverseTransformPoint(tip);
            rope.xMotion = rope.yMotion = rope.zMotion = ConfigurableJointMotion.Limited;
            rope.angularXMotion = rope.angularYMotion = rope.angularZMotion = ConfigurableJointMotion.Free;
            rope.linearLimit = new SoftJointLimit { limit = ropeL };
        }

        Vector3 BoomTip() { return boomT.TransformPoint(new Vector3(0, 0, BoomLen)); }

        void ApplyJoints()
        {
            upper.localRotation = Quaternion.Euler(0, swing.pos * Mathf.Rad2Deg, 0);
            boomT.localRotation = Quaternion.Euler(-luff.pos * Mathf.Rad2Deg, 0, 0);
        }

        public override ControlsInfo Controls()
        {
            var c = new ControlsInfo { rUp = "Heben", rDown = "Senken" };
            if (workMode) { c.lUp = "Ausleger ab"; c.lDown = "Ausleger auf"; c.lLeft = "Drehen L"; c.lRight = "Drehen R"; }
            else { c.lUp = "Vor"; c.lDown = "Zurück"; c.lLeft = "Links"; c.lRight = "Rechts"; }
            c.buttons.Add(("mode", workMode ? "Fahren" : "Arbeiten", "Modus"));
            return c;
        }

        public override bool OnButton(string id)
        {
            if (id != "mode") return false;
            workMode = !workMode;
            return true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var inp = isActive ? input : ControlInput.None;
            float thr = 0, st = 0, sw = 0, lf = 0;
            if (workMode) { sw = inp.lx; lf = -inp.ly; } else { thr = inp.ly; st = inp.lx; }
            swing.Step(sw, 1f, dt);
            luff.Step(lf, 1f, dt);
            ropeV = U.Approach(ropeV, -inp.ry * 1.4f, 2f * dt);
            ropeL = Mathf.Clamp(ropeL + ropeV * dt, 1.5f, 22f);
            ApplyJoints();
            rope.connectedAnchor = transform.InverseTransformPoint(BoomTip());
            rope.linearLimit = new SoftJointLimit { limit = ropeL };
            tracks.Step(thr, st, dt);
            hydFlow = Mathf.Clamp01(Mathf.Abs(sw) + Mathf.Abs(lf) + Mathf.Abs(inp.ry) * 0.6f);
            UpdateEngine(0.12f + hydFlow * 0.6f + Mathf.Abs(thr) * 0.5f, hydFlow * 0.5f + tracks.tractionUsed * 0.4f, dt);
            if (engine) engine.trackSpeed = Mathf.Max(Mathf.Abs(tracks.beltL), Mathf.Abs(tracks.beltR));
            ComputeStability(2.6f, 1.9f);
        }

        void LateUpdate()
        {
            var tip = BoomTip();
            hoist.Set(tip, ball.transform.TransformPoint(new Vector3(0, BallR + 0.3f, 0)));
            pendant.Set(upper.TransformPoint(new Vector3(0, 3.6f, -1.6f)), boomT.TransformPoint(new Vector3(0, 0, BoomLen * 0.98f)));
            trackMatL.mainTextureOffset = new Vector2(tracks.distL / 0.22f, 0);
            trackMatR.mainTextureOffset = new Vector2(tracks.distR / 0.22f, 0);
        }

        public override Vector3 CameraTarget()
        {
            var b = ball.position;
            return new Vector3((transform.position.x + b.x) / 2, transform.position.y + 5, (transform.position.z + b.z) / 2);
        }
        public override float CameraYaw() { return workMode ? upper.eulerAngles.y : transform.eulerAngles.y; }

        public override void Cabin(out Vector3 pos, out Vector3 fwd)
        {
            pos = cabEye.position;
            fwd = (ball.position - pos).normalized;
        }

        public override List<(string, string, string)> HudLines()
        {
            var tip = BoomTip();
            float reach = new Vector2(tip.x - transform.position.x, tip.z - transform.position.z).magnitude;
            return new List<(string, string, string)>
            {
                ("Birne", "3,0 t", U.Fmt(ball.Vel().magnitude * 3.6f, 0) + " km/h"),
                ("Ausladung", U.Fmt(reach) + " m", "Seil " + U.Fmt(ropeL) + " m"),
                ("Modus", workMode ? "Arbeiten" : "Fahren", stability < 0.25f ? "Kippgefahr!" : ""),
            };
        }

        public override void ResetVehicle()
        {
            base.ResetVehicle();
            ball.position = BoomTip() + Vector3.down * ropeL;
            ball.SetVel(Vector3.zero);
        }
    }

    /// <summary>Aufprall der Abrissbirne: kinetische Energie zerstört Bauteile im Umkreis.</summary>
    public class BallImpact : MonoBehaviour
    {
        Rigidbody rb;
        float cool;
        void Awake() { rb = GetComponent<Rigidbody>(); }
        void Update() { cool -= Time.deltaTime; }
        void OnCollisionEnter(Collision c)
        {
            if (cool > 0) return;
            float v = c.relativeVelocity.magnitude;
            if (v < 1.0f) return;
            float e = 0.5f * rb.mass / 1000f * v * v; // kJ
            var p = c.GetContact(0).point;
            if (c.collider.gameObject.layer == Layers.Building && Buildings.I)
            {
                cool = 0.05f;
                var res = Buildings.I.Impact(p, 0.9f, e * 7f, rb.Vel() * 0.7f);
                SoundFX.I?.Play(res.broken > 0 ? "crash" : "thud", p, Mathf.Clamp(e / 20f, 0.4f, 1f), 0.05f);
            }
            else if (v > 2f) SoundFX.I?.Play("metal", p, Mathf.Clamp(v / 8f, 0.2f, 0.9f), 0.2f);
        }
    }
}
