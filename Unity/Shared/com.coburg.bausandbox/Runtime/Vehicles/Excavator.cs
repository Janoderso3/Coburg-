using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>
    /// 21-t-Kettenbagger: Oberwagen, Monoblock-Ausleger, Stiel, Tieflöffel.
    /// Grabkräfte wirken auf die Maschine zurück (sie kann sich mit dem Ausleger anheben und kippen).
    /// </summary>
    public class Excavator : VehicleBase
    {
        public bool workMode = true;
        Transform under, upper, boomT, stickT, bucketT;
        HydJoint swing, boom, stick, buck;
        public SoilTool tool;
        TrackDrive tracks;
        Material trackMatL, trackMatR;
        HydCylinder cBoomL, cBoomR, cStick, cBucket;
        Vector3 aBoomBaseL = new Vector3(-0.15f, 0.55f, 1.35f), aBoomBaseR = new Vector3(0.55f, 0.55f, 1.35f);
        Vector3 aBoomMidL = new Vector3(-0.33f, -0.3f, 2.3f), aBoomMidR = new Vector3(0.33f, -0.3f, 2.3f);
        Vector3 aStickBase = new Vector3(0, 1.55f, 2.6f), aStickEnd = new Vector3(0, 0.5f, -0.55f);
        Vector3 aBuckBase = new Vector3(0, 0.45f, 0.5f), aBuckEnd = new Vector3(0, 0.42f, -0.1f);
        float liftForce;

        public static Excavator Create(Vector3 pos, float yaw)
        {
            var go = new GameObject("Kettenbagger");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var e = go.AddComponent<Excavator>();
            e.Build();
            return e;
        }

        void Build()
        {
            displayName = "Kettenbagger 21 t"; shortName = "Bagger"; camDistance = 13f;
            spawnPos = transform.position; spawnRot = transform.rotation;
            SetupBody(21000f, new Vector3(0, 0.9f, -0.3f));
            rb.inertiaTensor = new Vector3(21000 * (3f * 3f + 2f * 2f) / 12f * 1.4f, 21000 * (3f * 3f + 4.5f * 4.5f) / 12f * 1.3f, 21000 * (2f * 2f + 4.5f * 4.5f) / 12f * 1.4f);
            rb.inertiaTensorRotation = Quaternion.identity;
            SetupEngine(6, 800, 2000, 92f, 340f);
            swing = new HydJoint(0, -1e6f, 1e6f, 0.75f, 0.9f);
            boom = new HydJoint(0.55f, -0.62f, 1.0f, 0.42f, 1.8f);
            stick = new HydJoint(1.4f, 0.5f, 2.6f, 0.62f, 2.2f);
            buck = new HydJoint(1.2f, -0.85f, 2.25f, 0.95f, 3.5f);

            var k = new MeshKit();
            // ---------- Unterwagen ----------
            under = new GameObject("Unterwagen").transform;
            under.SetParent(transform, false);
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
            k.Cyl("dark_metal", new Vector3(0, 0.99f, 0), 1.0f, 1.0f, 0.18f, 28, Quaternion.identity);
            k.Build(under, "Fahrwerk");
            trackMatL = new Material(Mats.Get("track"));
            trackMatR = new Material(Mats.Get("track"));
            foreach (float sx in new[] { -1.2f, 1.2f })
            {
                k.TrackBelt("track", sx, 0.42f, 4.5f, 0.42f, 0.6f);
                var tg = k.Build(under, sx < 0 ? "Kette links" : "Kette rechts");
                tg.GetComponent<MeshRenderer>().sharedMaterial = sx < 0 ? trackMatL : trackMatR;
            }
            AddBox(under, new Vector3(0, 0.85f, 0), new Vector3(2.0f, 0.55f, 2.4f), Layers.Vehicle);
            AddBox(under, new Vector3(-1.2f, 0.62f, 0), new Vector3(0.6f, 0.5f, 4.3f), Layers.Vehicle);
            AddBox(under, new Vector3(1.2f, 0.62f, 0), new Vector3(0.6f, 0.5f, 4.3f), Layers.Vehicle);

            // ---------- Oberwagen ----------
            upper = new GameObject("Oberwagen").transform;
            upper.SetParent(transform, false);
            upper.localPosition = new Vector3(0, 1.08f, 0);
            k.Box("dark_metal", new Vector3(0, 0.11f, -0.45f), new Vector3(2.7f, 0.22f, 4.2f), 0.04f);
            k.Box("paint_yellow", new Vector3(0, 0.7f, -1.55f), new Vector3(2.75f, 0.95f, 1.9f), 0.1f);
            k.ProfileX("dark_metal", new[] { new Vector2(-2.25f, 0.25f), new Vector2(-2.6f, 0.45f), new Vector2(-2.62f, 1.15f), new Vector2(-2.3f, 1.3f), new Vector2(-1.9f, 1.3f), new Vector2(-1.9f, 0.25f) }, 2.85f);
            k.Box("paint_yellow", new Vector3(0.82f, 0.7f, 0.55f), new Vector3(1.1f, 0.95f, 1.6f), 0.08f);
            k.Box("paint_yellow", new Vector3(0.15f, 0.45f, 1.0f), new Vector3(0.4f, 0.5f, 0.9f), 0.04f);
            k.Box("black_plastic", new Vector3(0, 1.2f, -1.55f), new Vector3(2.6f, 0.06f, 1.8f));
            for (int i = 0; i < 6; i++) k.Box("dark_metal", new Vector3(-0.9f + i * 0.36f, 1.18f, -1.55f), new Vector3(0.05f, 0.05f, 1.6f));
            k.Cyl("steel", new Vector3(0.7f, 1.45f, -1.3f), 0.07f, 0.07f, 0.6f, 10, Quaternion.identity);
            // Kabine links (-X)
            k.Box("paint_yellow", new Vector3(-0.82f, 0.35f, 0.55f), new Vector3(1.0f, 0.25f, 1.75f), 0.04f);
            k.Box("paint_yellow", new Vector3(-0.82f, 2.05f, 0.5f), new Vector3(1.04f, 0.09f, 1.85f), 0.04f);
            foreach (var (x, z) in new[] { (-0.34f, 1.4f), (-1.3f, 1.4f), (-0.34f, -0.3f), (-1.3f, -0.3f) })
                k.Box("paint_yellow", new Vector3(x, 1.22f, z), new Vector3(0.07f, 1.65f, 0.07f));
            k.Box("seat", new Vector3(-0.82f, 0.75f, 0.25f), new Vector3(0.55f, 0.55f, 0.5f), 0.08f);
            k.Box("seat", new Vector3(-0.82f, 1.1f, 0.0f), new Vector3(0.55f, 0.6f, 0.12f), 0.05f);
            k.Box("black_plastic", new Vector3(-1.15f, 0.85f, 0.55f), new Vector3(0.18f, 0.25f, 0.5f), 0.03f);
            k.Box("black_plastic", new Vector3(-0.49f, 0.85f, 0.55f), new Vector3(0.18f, 0.25f, 0.5f), 0.03f);
            k.Box("steel", new Vector3(-1.36f, 1.0f, -0.8f), new Vector3(0.05f, 0.05f, 1.0f));
            k.Box("steel", new Vector3(1.38f, 1.2f, -0.6f), new Vector3(0.05f, 0.05f, 2.6f));
            k.Build(upper, "Oberwagen");
            var glass = new MeshKit();
            glass.Box("glass", new Vector3(-0.82f, 1.2f, 0.55f), new Vector3(0.94f, 1.55f, 1.68f));
            glass.Build(upper, "Scheiben", false);
            beacons.Add(Beacon(upper, new Vector3(-0.82f, 2.15f, 0.0f)));
            var lamp = new MeshKit();
            lamp.Box("headlight", new Vector3(-0.82f, 2.0f, 1.45f), new Vector3(0.22f, 0.12f, 0.08f), 0.02f);
            lamp.Box("headlight", new Vector3(0.82f, 1.25f, 1.36f), new Vector3(0.22f, 0.12f, 0.08f), 0.02f);
            lamp.Build(upper, "Scheinwerfer", false);
            cabEye = new GameObject("Fahrerauge").transform;
            cabEye.SetParent(upper, false);
            cabEye.localPosition = new Vector3(-0.82f, 1.75f, 0.7f);
            exhaust = new GameObject("Auspuff").transform;
            exhaust.SetParent(upper, false);
            exhaust.localPosition = new Vector3(0.7f, 1.78f, -1.3f);
            AddBox(upper, new Vector3(0, 0.65f, -0.5f), new Vector3(2.7f, 1.1f, 4.1f), Layers.Vehicle);
            AddBox(upper, new Vector3(-0.82f, 1.25f, 0.55f), new Vector3(1.0f, 1.7f, 1.75f), Layers.Vehicle);

            // ---------- Ausleger ----------
            boomT = new GameObject("Ausleger").transform;
            boomT.SetParent(upper, false);
            boomT.localPosition = new Vector3(0.2f, 1.05f, 1.05f);
            k.Beam("paint_yellow", new Vector3(0, 0, 0), new Vector3(0, 1.25f, 2.9f), 0.55f, 0.62f, Vector3.up, 0.06f);
            k.Beam("paint_yellow", new Vector3(0, 1.25f, 2.9f), new Vector3(0, 0, 5.7f), 0.5f, 0.55f, Vector3.up, 0.06f);
            k.Box("paint_yellow", new Vector3(0, 1.2f, 2.9f), new Vector3(0.6f, 0.36f, 0.5f), 0.05f);
            k.CylX("steel", Vector3.zero, 0.14f, 0.75f, 14);
            k.CylX("steel", new Vector3(0, 0, 5.7f), 0.12f, 0.62f, 14);
            k.Beam("dark_metal", new Vector3(0.2f, 0.95f, 1.0f), new Vector3(0.2f, 1.45f, 4.8f), 0.05f, 0.05f, Vector3.up, 0);
            k.Build(boomT, "Ausleger");
            AddBeamCollider(boomT, Vector3.zero, new Vector3(0, 1.25f, 2.9f), 0.55f, 0.62f);
            AddBeamCollider(boomT, new Vector3(0, 1.25f, 2.9f), new Vector3(0, 0, 5.7f), 0.5f, 0.55f);

            stickT = new GameObject("Stiel").transform;
            stickT.SetParent(boomT, false);
            stickT.localPosition = new Vector3(0, 0, 5.7f);
            k.Beam("paint_yellow", new Vector3(0, 0.25f, -0.6f), new Vector3(0, 0, 2.95f), 0.42f, 0.48f, Vector3.up, 0.05f);
            k.Box("paint_yellow", new Vector3(0, 0.35f, -0.5f), new Vector3(0.44f, 0.3f, 0.4f), 0.04f);
            k.CylX("steel", new Vector3(0, 0, 2.95f), 0.1f, 0.5f, 12);
            k.Build(stickT, "Stiel");
            AddBeamCollider(stickT, new Vector3(0, 0.25f, -0.6f), new Vector3(0, 0, 2.95f), 0.42f, 0.48f);

            bucketT = new GameObject("Löffel").transform;
            bucketT.SetParent(stickT, false);
            bucketT.localPosition = new Vector3(0, 0, 2.95f);
            float W = 1.1f;
            k.ProfileX("paint_yellow_worn", new[] { new Vector2(-0.12f, -0.08f), new Vector2(-0.08f, 0.32f), new Vector2(0.25f, 0.68f), new Vector2(0.8f, 0.78f), new Vector2(1.22f, 0.48f), new Vector2(1.42f, 0.02f), new Vector2(1.48f, -0.16f), new Vector2(1.36f, -0.16f), new Vector2(1.2f, 0.36f), new Vector2(0.78f, 0.62f), new Vector2(0.3f, 0.55f), new Vector2(0.05f, 0.25f), new Vector2(0.0f, -0.08f) }, W);
            foreach (float sx in new[] { -W / 2, W / 2 })
                k.ProfileX("paint_yellow_worn", new[] { new Vector2(-0.15f, -0.1f), new Vector2(-0.1f, 0.35f), new Vector2(0.25f, 0.7f), new Vector2(0.8f, 0.8f), new Vector2(1.25f, 0.5f), new Vector2(1.48f, -0.18f), new Vector2(0.6f, -0.12f) }, 0.04f, sx);
            for (int i = 0; i < 5; i++) k.Box("bucket_wear", new Vector3(-0.44f + i * 0.22f, -0.2f, 1.58f), new Vector3(0.09f, 0.08f, 0.26f), Quaternion.Euler(28f, 0, 0), 0.015f);
            k.Box("paint_yellow", new Vector3(0, 0.32f, -0.12f), new Vector3(0.3f, 0.3f, 0.35f), 0.04f);
            k.Build(bucketT, "Löffel");
            AddBox(bucketT, new Vector3(0, 0.3f, 0.75f), new Vector3(W, 0.9f, 1.4f), Layers.Tool);
            // Inhalt
            var kc = new MeshKit();
            kc.Sphere("soil_chunk", Vector3.zero, 1f, 14);
            var cont = kc.Build(bucketT, "Inhalt");
            cont.transform.localPosition = new Vector3(0, 0.25f, 0.75f);
            cont.SetActive(false);
            tool = new SoilTool
            {
                t = bucketT, owner = this, cap = 1.0f, width = W, power = 140f,
                tipL = new Vector3(0, -0.24f, 1.66f), heelL = new Vector3(0, 0.72f, 0.8f),
                openL = new Vector3(0, -1, 0.15f).normalized, tipDirL = new Vector3(0, -0.45f, 1).normalized,
                content = cont.transform, contentScale = new Vector3(0.5f, 0.42f, 0.6f),
            };
            // Zylinder
            cBoomL = new HydCylinder(transform, 0.11f, 0.065f, 1.4f);
            cBoomR = new HydCylinder(transform, 0.11f, 0.065f, 1.4f);
            cStick = new HydCylinder(transform, 0.12f, 0.07f, 1.6f);
            cBucket = new HydCylinder(transform, 0.1f, 0.06f, 1.2f);
            tracks = new TrackDrive();
            tracks.Init(rb, transform, 2.4f, 3.9f, 0f, 1.55f, 7);
            tracks.mu = 0.8f; tracks.muLat = 0.7f;
            ApplyJoints();
        }

        public static void AddBox(Transform parent, Vector3 c, Vector3 size, int layer)
        {
            var go = new GameObject("Kollision");
            go.layer = layer;
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<BoxCollider>();
            b.center = c; b.size = size;
            b.sharedMaterial = U.SteelMat;
        }

        public static void AddBeamCollider(Transform parent, Vector3 a, Vector3 b, float w, float h)
        {
            var go = new GameObject("Kollision");
            go.layer = Layers.Tool;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (a + b) * 0.5f;
            go.transform.localRotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(w, h, (b - a).magnitude);
            bc.sharedMaterial = U.SteelMat;
        }

        public static Renderer Beacon(Transform parent, Vector3 pos)
        {
            var k = new MeshKit();
            k.Cyl("black_plastic", pos, 0.1f, 0.1f, 0.05f, 12, Quaternion.identity);
            k.Build(parent, "Leuchtensockel");
            k.Cyl("beacon", pos + new Vector3(0, 0.1f, 0), 0.07f, 0.085f, 0.15f, 12, Quaternion.identity);
            var go = k.Build(parent, "Rundumleuchte", false);
            var light = new GameObject("Licht").AddComponent<Light>();
            light.transform.SetParent(go.transform, false);
            light.transform.localPosition = pos + new Vector3(0, 0.12f, 0);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.1f);
            light.range = 6f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            go.AddComponent<BeaconBlink>().light = light;
            return go.GetComponent<Renderer>();
        }

        void ApplyJoints()
        {
            upper.localRotation = Quaternion.Euler(0, swing.pos * Mathf.Rad2Deg, 0);
            boomT.localRotation = Quaternion.Euler(-boom.pos * Mathf.Rad2Deg, 0, 0);
            stickT.localRotation = Quaternion.Euler(stick.pos * Mathf.Rad2Deg, 0, 0);
            bucketT.localRotation = Quaternion.Euler(buck.pos * Mathf.Rad2Deg, 0, 0);
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

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var inp = isActive ? input : ControlInput.None;
            float thr = 0, st = 0, sw = 0, sk = 0, bm, bk;
            if (workMode) { sw = inp.lx; sk = -inp.ly; } else { thr = inp.ly; st = inp.lx; }
            bm = Settings.Iso ? -inp.ry : inp.ry;
            bk = -inp.rx;
            // Pumpenförderstrom aufteilen
            float demand = Mathf.Abs(sw) + Mathf.Abs(sk) + Mathf.Abs(bm) + Mathf.Abs(bk) + (Mathf.Abs(thr) + Mathf.Abs(st)) * 0.8f;
            float share = demand > 1.6f ? 1.6f / demand : 1f;
            float slow = 1f - tool.resist * 0.6f;
            // Schwenkwerk: positive Drehung = nach rechts
            swing.Step(sw, share, dt);
            float pb = boom.pos, ps = stick.pos, pk = buck.pos, pw = swing.pos;
            boom.Step(bm, share * slow, dt);
            stick.Step(sk, share * slow, dt);
            buck.Step(bk, share * slow, dt);
            ApplyJoints();
            tool.ComputePoints(dt);
            bool tooDeep = tool.tip.y < transform.position.y - 6.6f;
            tool.Step(dt);
            bool blocked = tooDeep || tool.blocked || tool.WouldBlock();
            relief = U.Damp(relief, blocked ? 1f : 0f, 8f, dt);
            if (blocked)
            {
                // Hydraulik steht – Kraft wirkt auf die Maschine zurück
                bool boomDown = boom.pos < pb;
                swing.pos = pw; boom.pos = pb; stick.pos = ps; buck.pos = pk;
                swing.vel *= -0.1f; boom.vel = 0; stick.vel = 0; buck.vel = 0;
                ApplyJoints();
                tool.ComputePoints(0);
                if (boomDown && !tool.blocked)
                {
                    // Bagger stemmt sich mit dem Ausleger hoch
                    liftForce = U.Approach(liftForce, 165000f * Mathf.Abs(bm), 400000f * dt);
                }
            }
            else liftForce = U.Approach(liftForce, 0, 600000f * dt);
            if (liftForce > 1f) rb.AddForceAtPosition(Vector3.up * liftForce, tool.tip, ForceMode.Force);
            tool.CommitDepth();
            // Reißkraft: Maschine wird zum Löffel gezogen
            if (tool.resist > 0.05f && tool.vel.sqrMagnitude > 0.001f)
                rb.AddForceAtPosition(-tool.vel.normalized * tool.resist * 70000f, tool.tip, ForceMode.Force);
            // Fahren
            tracks.Step(thr, st, dt);
            // Schwerpunkt mit Arm und Ladung
            UpdateCom();
            hydFlow = Mathf.Clamp01(demand / 1.6f);
            float drive = (Mathf.Abs(tracks.beltL) + Mathf.Abs(tracks.beltR)) / 3f;
            UpdateEngine(0.12f + hydFlow * 0.8f + drive * 0.6f + relief * 0.3f, tool.resist + relief * 0.8f + hydFlow * 0.3f + tracks.tractionUsed * 0.5f, dt);
            if (engine) engine.trackSpeed = Mathf.Max(Mathf.Abs(tracks.beltL), Mathf.Abs(tracks.beltR));
            ComputeStability(2.2f, 1.5f);
        }

        void UpdateCom()
        {
            var inv = transform.worldToLocalMatrix;
            Vector3 sum = new Vector3(0, 0.7f, 0) * 9200f;
            float m = 9200f;
            System.Action<Vector3, float> add = (w, mass) => { sum += inv.MultiplyPoint3x4(w) * mass; m += mass; };
            add(upper.TransformPoint(new Vector3(0, 0.7f, -1.1f)), 8300f);
            add(boomT.TransformPoint(new Vector3(0, 0.8f, 2.9f)), 1900f);
            add(stickT.TransformPoint(new Vector3(0, 0.1f, 1.2f)), 950f);
            add(bucketT.TransformPoint(new Vector3(0, 0.3f, 0.8f)), 850f + tool.LoadMassKg);
            rb.centerOfMass = sum / m;
            rb.mass = m;
        }

        void LateUpdate()
        {
            Vector3 W(Transform t, Vector3 p) => t.TransformPoint(p);
            cBoomL.Update(W(upper, aBoomBaseL), W(boomT, aBoomMidL));
            cBoomR.Update(W(upper, aBoomBaseR), W(boomT, aBoomMidR));
            cStick.Update(W(boomT, aStickBase), W(stickT, aStickEnd));
            cBucket.Update(W(stickT, aBuckBase), W(bucketT, aBuckEnd));
            trackMatL.mainTextureOffset = new Vector2(tracks.distL / 0.22f, 0);
            trackMatR.mainTextureOffset = new Vector2(tracks.distR / 0.22f, 0);
        }

        public override Vector3 CameraTarget()
        {
            var f = upper.forward;
            return transform.position + Flat(f).normalized * 2.5f + Vector3.up * 2.2f;
        }

        public override float CameraYaw() { return workMode ? upper.eulerAngles.y : transform.eulerAngles.y; }

        public override List<(string, string, string)> HudLines()
        {
            float depth = tool.tip.y - transform.position.y;
            string sub = tool.spillT > 0 && tool.load > 0.02f ? "verliert Material!" : tool.load > 0.02f ? MatName(tool.mat) : "leer";
            return new List<(string, string, string)>
            {
                ("Löffel", $"{U.Fmt(tool.load, 2)} / {U.Fmt(tool.cap)} m³", sub),
                ("Schneidkante", (depth >= 0 ? "+" : "") + U.Fmt(depth, 2) + " m", "zur Standfläche"),
                ("Hydraulik", relief > 0.5f ? "Druckbegrenzung" : $"{Mathf.RoundToInt(hydFlow * 100)} %", workMode ? "Arbeiten" : "Fahren"),
            };
        }

        public static string MatName(SoilMat m)
        {
            switch (m) { case SoilMat.Rubble: return "Bauschutt"; case SoilMat.Gravel: return "Schotter"; case SoilMat.Concrete: return "Beton"; default: return "Erde"; }
        }
    }

    /// <summary>Lässt Rundumleuchte und Punktlicht blinken (sichtbar nur am aktiven Fahrzeug).</summary>
    public class BeaconBlink : MonoBehaviour
    {
        public Light light;
        float t;
        Renderer r;
        void Awake() { r = GetComponent<Renderer>(); t = Random.value * 6; }
        void Update()
        {
            var v = GetComponentInParent<VehicleBase>();
            bool active = v && v.isActive;
            t += Time.deltaTime * 7f;
            bool on = active && Mathf.Sin(t) > 0.2f;
            if (light) light.enabled = on;
            if (r) r.enabled = true;
        }
    }
}
