using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Planierraupe: Schild schält Boden ab und schiebt ihn als Wall vor sich her. Der Schubwiderstand wirkt als Kraft.</summary>
    public class Dozer : VehicleBase
    {
        Transform frameT, bladeT;
        HydJoint blade, tilt;
        TrackDrive tracks;
        Material trackMatL, trackMatR;
        HydCylinder cL, cR;
        public float pushVol;
        Vector3 edge;
        const float BW = 3.4f;

        public static Dozer Create(Vector3 pos, float yaw)
        {
            var go = new GameObject("Planierraupe");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var d = go.AddComponent<Dozer>();
            d.Build();
            return d;
        }

        void Build()
        {
            displayName = "Planierraupe 20 t"; shortName = "Raupe"; camDistance = 12f;
            spawnPos = transform.position; spawnRot = transform.rotation;
            SetupBody(20000f, new Vector3(0, 0.95f, 0.1f));
            rb.inertiaTensor = new Vector3(20000 * (3.2f * 3.2f + 5f * 5f) / 12f, 20000 * (3.4f * 3.4f + 5f * 5f) / 12f, 20000 * (3.2f * 3.2f + 3.4f * 3.4f) / 12f);
            rb.inertiaTensorRotation = Quaternion.identity;
            SetupEngine(6, 750, 1950, 85f, 310f, 1.1f);
            blade = new HydJoint(0.3f, -0.45f, 1.1f, 0.35f, 2.5f);
            tilt = new HydJoint(0, -0.1f, 0.1f, 0.12f, 1f);
            var k = new MeshKit();
            var body = new GameObject("Rumpf").transform;
            body.SetParent(transform, false);
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
            foreach (var (x, z) in new[] { (0.72f, -0.15f), (-0.72f, -0.15f), (0.72f, -1.55f), (-0.72f, -1.55f) })
                k.Box("paint_yellow", new Vector3(x, 2.3f, z), new Vector3(0.07f, 1.8f, 0.07f));
            k.Box("seat", new Vector3(0, 1.8f, -1.0f), new Vector3(0.5f, 0.6f, 0.5f), 0.06f);
            k.Box("dark_metal", new Vector3(0, 1.0f, -1.75f), new Vector3(1.5f, 0.5f, 0.4f), 0.04f);
            // Heckaufreißer
            k.Box("dark_metal", new Vector3(0, 0.9f, -2.1f), new Vector3(1.4f, 0.2f, 0.4f), 0.03f);
            k.Box("bucket_wear", new Vector3(0, 0.45f, -2.25f), new Vector3(0.12f, 0.9f, 0.15f), 0.02f);
            k.Cyl("steel", new Vector3(0.45f, 2.55f, 0.6f), 0.06f, 0.06f, 0.8f, 10, Quaternion.identity);
            k.Build(body, "Rumpf");
            var g = new MeshKit();
            g.Box("glass", new Vector3(0, 2.3f, -0.85f), new Vector3(1.38f, 1.7f, 1.33f));
            g.Build(body, "Scheiben", false);
            beacons.Add(Excavator.Beacon(body, new Vector3(0.5f, 3.27f, -1.2f)));
            trackMatL = new Material(Mats.Get("track")); trackMatR = new Material(Mats.Get("track"));
            foreach (float sx in new[] { -0.95f, 0.95f })
            {
                k.TrackBelt("track", sx, 0.5f, 3.4f, 0.45f, 0.5f);
                k.Build(body, "Kette").GetComponent<MeshRenderer>().sharedMaterial = sx < 0 ? trackMatL : trackMatR;
            }
            cabEye = new GameObject("Fahrerauge").transform; cabEye.SetParent(body, false); cabEye.localPosition = new Vector3(0, 2.5f, -0.8f);
            exhaust = new GameObject("Auspuff").transform; exhaust.SetParent(body, false); exhaust.localPosition = new Vector3(0.45f, 2.98f, 0.6f);
            Excavator.AddBox(body, new Vector3(0, 1.2f, 0.1f), new Vector3(1.7f, 1.3f, 3.6f), Layers.Vehicle);
            Excavator.AddBox(body, new Vector3(-0.95f, 0.65f, 0), new Vector3(0.5f, 0.6f, 3.3f), Layers.Vehicle);
            Excavator.AddBox(body, new Vector3(0.95f, 0.65f, 0), new Vector3(0.5f, 0.6f, 3.3f), Layers.Vehicle);
            Excavator.AddBox(body, new Vector3(0, 2.3f, -0.85f), new Vector3(1.5f, 1.8f, 1.45f), Layers.Vehicle);
            // Schubrahmen + Schild
            frameT = new GameObject("Schubrahmen").transform;
            frameT.SetParent(transform, false);
            frameT.localPosition = new Vector3(0, 0.45f, -0.2f);
            foreach (float sx in new[] { -1.18f, 1.18f }) k.Beam("paint_yellow", new Vector3(sx, 0, 0), new Vector3(sx, 0.1f, 2.25f), 0.18f, 0.25f, Vector3.up, 0.03f);
            k.Build(frameT, "Schubrahmen");
            bladeT = new GameObject("Schild").transform;
            bladeT.SetParent(frameT, false);
            bladeT.localPosition = new Vector3(0, 0.1f, 2.35f);
            k.ProfileX("paint_yellow", new[] { new Vector2(0f, -0.55f), new Vector2(0.18f, -0.55f), new Vector2(0.12f, -0.2f), new Vector2(0.18f, 0.25f), new Vector2(0.32f, 0.65f), new Vector2(0.38f, 0.75f), new Vector2(0.22f, 0.78f), new Vector2(-0.02f, 0.4f), new Vector2(-0.1f, 0f), new Vector2(-0.08f, -0.5f) }, BW);
            foreach (float sx in new[] { -BW / 2, BW / 2 }) k.ProfileX("paint_yellow", new[] { new Vector2(-0.1f, -0.5f), new Vector2(0.2f, -0.55f), new Vector2(0.38f, 0.75f), new Vector2(-0.05f, 0.6f) }, 0.05f, sx);
            k.Box("bucket_wear", new Vector3(0, -0.52f, 0.18f), new Vector3(BW, 0.1f, 0.06f));
            k.Build(bladeT, "Schild");
            Excavator.AddBox(bladeT, new Vector3(0, 0.1f, 0.1f), new Vector3(BW, 1.3f, 0.4f), Layers.Tool);
            cL = new HydCylinder(transform, 0.08f, 0.05f, 1.0f);
            cR = new HydCylinder(transform, 0.08f, 0.05f, 1.0f);
            tracks = new TrackDrive();
            tracks.Init(rb, transform, 1.9f, 2.9f, 0.05f, 2.7f, 6);
            tracks.mu = 0.85f; tracks.muLat = 0.75f; tracks.accel = 1.2f;
            ApplyJoints();
        }

        void ApplyJoints()
        {
            float a = Mathf.Asin(Mathf.Clamp(blade.pos / 2.35f, -0.5f, 0.6f)) * Mathf.Rad2Deg;
            frameT.localRotation = Quaternion.Euler(-a, 0, 0);
            bladeT.localRotation = Quaternion.Euler(a, 0, tilt.pos * Mathf.Rad2Deg);
        }

        public override ControlsInfo Controls()
        {
            bool iso = Settings.Iso;
            return new ControlsInfo
            {
                lUp = "Vor", lDown = "Zurück", lLeft = "Links", lRight = "Rechts",
                rUp = iso ? "Schild ab" : "Schild auf", rDown = iso ? "Schild auf" : "Schild ab", rLeft = "Neigen L", rRight = "Neigen R",
            };
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var inp = isActive ? input : ControlInput.None;
            float bl = Settings.Iso ? -inp.ry : inp.ry;
            blade.Step(bl, 1f, dt);
            tilt.Step(-inp.rx, 1f, dt);
            ApplyJoints();
            float resist = Mathf.Clamp01(pushVol / 10f);
            tracks.Step(inp.ly, inp.lx, dt, Mathf.Lerp(1f, 0.55f, resist));
            DoBlade(dt);
            hydFlow = Mathf.Abs(bl) * 0.7f;
            float drive = Mathf.Max(Mathf.Abs(tracks.beltL), Mathf.Abs(tracks.beltR)) / tracks.maxSpeed;
            UpdateEngine(0.12f + drive * 0.75f + hydFlow * 0.4f, tracks.tractionUsed * 0.9f + resist * 0.5f, dt);
            if (engine) engine.trackSpeed = Mathf.Max(Mathf.Abs(tracks.beltL), Mathf.Abs(tracks.beltR));
            if (engine) engine.beeper = tracks.Speed < -0.1f;
            ComputeStability(1.7f, 1.2f);
        }

        void DoBlade(float dt)
        {
            var T = SoilTerrain.I;
            var m = bladeT.localToWorldMatrix;
            var fwd = Flat(transform.forward).normalized;
            float sp = Vector3.Dot(rb.Vel(), fwd);
            bool moving = Mathf.Abs(sp) > 0.03f;
            const int n = 9;
            float vol = 0, front = 0;
            var sideW = m.MultiplyVector(Vector3.right).normalized;
            for (int i = 0; i < n; i++)
            {
                float lx = -BW / 2 + BW * (i + 0.5f) / n;
                var p = m.MultiplyPoint3x4(new Vector3(lx, -0.55f, 0.15f));
                if (i == n / 2) edge = p;
                float gnd = T.Height(p.x, p.z);
                if (gnd > p.y + 0.01f && moving)
                {
                    float seg = BW / n / 2;
                    var r = T.CutSegment(p - sideW * seg, p + sideW * seg, p.y, 0.3f, 4f);
                    if (r.vol > 0)
                    {
                        float dir = sp >= 0 ? 1 : -1;
                        T.Deposit(p.x + fwd.x * 0.75f * dir, p.z + fwd.z * 0.75f * dir, r.vol, r.mat, 0.4f);
                        vol += r.vol;
                    }
                }
                var fp = p + fwd * 0.6f;
                front += Mathf.Max(0, T.Height(fp.x, fp.z) - p.y) * 0.6f * (BW / n);
            }
            pushVol = U.Damp(pushVol, front * 1.5f, 4f, dt);
            // Schubwiderstand des Erdwalls (Reibung Boden auf Boden)
            if (pushVol > 0.05f && moving)
            {
                float F = pushVol * 1700f * 9.81f * 0.55f;
                rb.AddForceAtPosition(-fwd * Mathf.Sign(sp) * F, edge + Vector3.up * 0.4f, ForceMode.Force);
            }
            if (vol > 0.002f && Random.value < 0.15f) SoilFX.I.Dust(edge + fwd + Vector3.up * 0.5f, 1, SoilFX.DustColor(SoilMat.Soil), 1f);
            // Abbruch mit dem Schild
            if (Buildings.I)
            {
                var vel = fwd * sp;
                foreach (float lx in new[] { -1.2f, 0f, 1.2f })
                {
                    var p = m.MultiplyPoint3x4(new Vector3(lx, 0.1f, 0.1f));
                    var res = Buildings.I.ToolHit(p, 0.6f, vel, 55f, dt);
                    if (res.hit > 0) hitWarning = 0.3f;
                }
            }
        }

        void LateUpdate()
        {
            cL.Update(transform.TransformPoint(new Vector3(-0.75f, 1.75f, 1.35f)), bladeT.TransformPoint(new Vector3(-0.75f, 0.3f, 0.05f)));
            cR.Update(transform.TransformPoint(new Vector3(0.75f, 1.75f, 1.35f)), bladeT.TransformPoint(new Vector3(0.75f, 0.3f, 0.05f)));
            trackMatL.mainTextureOffset = new Vector2(tracks.distL / 0.22f, 0);
            trackMatR.mainTextureOffset = new Vector2(tracks.distR / 0.22f, 0);
        }

        public override List<(string, string, string)> HudLines()
        {
            float rel = edge.y - transform.position.y;
            return new List<(string, string, string)>
            {
                ("Schild", (rel >= 0 ? "+" : "") + U.Fmt(rel, 2) + " m", "zur Kettenauflage"),
                ("Schubvolumen", U.Fmt(pushVol) + " m³", tracks.slip > 0.4f ? "Ketten rutschen!" : pushVol > 5 ? "schwer" : ""),
                ("Tempo", U.Fmt(Mathf.Abs(tracks.Speed) * 3.6f) + " km/h", tracks.Speed < -0.1f ? "rückwärts" : ""),
            };
        }
    }
}
