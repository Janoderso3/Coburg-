using UnityEngine;

namespace BauSandbox
{
    /// <summary>
    /// Kettenlaufwerk mit Strahl-Federung je Laufrolle und Reibungskräften je Kontakt.
    /// Lenken durch unterschiedliche Kettengeschwindigkeit (wie hydrostatischer Fahrantrieb):
    /// die Querreibung der Ketten muss beim Drehen überwunden werden.
    /// </summary>
    public class TrackDrive
    {
        public Rigidbody rb;
        public Transform root;
        public float gauge, length, contactY, maxSpeed, accel = 1.0f, mu = 0.75f, muLat = 0.65f;
        public int rollers = 6;
        public float travel = 0.16f;
        public float beltL, beltR;       // Kettengeschwindigkeit m/s
        public float distL, distR;       // zurückgelegt (für Texturverschiebung)
        public int grounded;
        public float slip;               // 0..1
        public float tractionUsed;       // 0..1
        public float powerKW = 120f;
        float k, c;
        Vector3[] pts;
        float[] comp;
        int mask;
        float compactT;

        public void Init(Rigidbody body, Transform t, float gauge, float length, float contactY, float maxSpeed, int rollers = 6)
        {
            rb = body; root = t; this.gauge = gauge; this.length = length; this.contactY = contactY; this.maxSpeed = maxSpeed; this.rollers = rollers;
            int n = rollers * 2 + 4;
            pts = new Vector3[n];
            comp = new float[n];
            int q = 0;
            foreach (float sx in new[] { -gauge / 2, gauge / 2 })
            {
                for (int i = 0; i < rollers; i++)
                {
                    float z = Mathf.Lerp(-length / 2, length / 2, (i + 0.5f) / rollers);
                    pts[q++] = new Vector3(sx, contactY, z);
                }
                // Leit- und Antriebsrad: etwas höher, zum Erklettern von Stufen
                pts[q++] = new Vector3(sx, contactY + 0.22f, length / 2 + 0.15f);
                pts[q++] = new Vector3(sx, contactY + 0.22f, -length / 2 - 0.15f);
            }
            // Federsteifigkeit: statische Einfederung ca. 4 cm
            float perContact = rb.mass * 9.81f / (rollers * 2);
            k = perContact / 0.04f;
            c = 2f * 0.6f * Mathf.Sqrt(k * rb.mass / (rollers * 2));
            mask = Layers.Mask(Layers.Terrain, Layers.Building, Layers.Debris, Layers.Clod, Layers.Element);
        }

        /// <summary>throttle/steer -1..1, resist 0..1 (zusätzlicher Fahrwiderstand), dt = fixedDeltaTime.</summary>
        public void Step(float throttle, float steer, float dt, float powerFactor = 1f)
        {
            float tl = throttle + steer * 0.8f, tr = throttle - steer * 0.8f;
            float mx = Mathf.Max(1f, Mathf.Abs(tl), Mathf.Abs(tr));
            tl /= mx; tr /= mx;
            // Leistungsbegrenzung: bei hoher Zugkraft sinkt die Geschwindigkeit
            float vmax = maxSpeed * Mathf.Lerp(1f, 0.45f, tractionUsed) * powerFactor;
            beltL = U.Approach(beltL, tl * vmax, accel * dt);
            beltR = U.Approach(beltR, tr * vmax, accel * dt);

            grounded = 0;
            var up = root.up;
            float mShare = rb.mass / (rollers * 2);
            float usedSum = 0, slipSum = 0;
            var soil = SoilTerrain.I;
            compactT += dt;
            bool doCompact = compactT > 0.1f;
            if (doCompact) compactT = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                var p = root.TransformPoint(pts[i]);
                var origin = p + up * travel;
                float maxDist = travel * 2f + 0.25f;
                if (!Physics.Raycast(origin, -up, out var hit, maxDist, mask, QueryTriggerInteraction.Ignore))
                {
                    comp[i] = 0;
                    continue;
                }
                float x = travel - (hit.distance - 0.0f);
                bool idler = (i % (rollers + 2)) >= rollers;
                if (x <= 0) { comp[i] = 0; continue; }
                comp[i] = x;
                grounded++;
                var pv = rb.GetPointVelocity(hit.point);
                float vn = Vector3.Dot(pv, up);
                float N = Mathf.Max(0f, k * x - c * vn);
                if (idler) N *= 0.6f;
                // Begrenzung gegen Durchschlagen
                rb.AddForceAtPosition(up * N, hit.point, ForceMode.Force);
                // Reibung entlang/quer zur Kette
                var n = hit.normal;
                var f = Vector3.ProjectOnPlane(root.forward, n).normalized;
                var l = Vector3.Cross(n, f);
                bool left = pts[i].x < 0;
                float belt = left ? beltL : beltR; // links = -X
                float vLong = Vector3.Dot(pv, f) - belt;
                float vLat = Vector3.Dot(pv, l);
                // lose Erde: weniger Haftung, mehr Rollwiderstand
                float loose = soil ? Mathf.Clamp01(soil.LooseAt(hit.point.x, hit.point.z) * 4f) : 0f;
                float muL = mu * (1f - 0.25f * loose);
                float fLong = -vLong * mShare / dt * 0.35f;
                float fLat = -vLat * mShare / dt * 0.35f;
                float maxL = muL * N, maxLat = muLat * N * (1f - 0.2f * loose);
                float aL = Mathf.Abs(fLong);
                if (aL > maxL) { fLong *= maxL / aL; slipSum += 1; }
                if (Mathf.Abs(fLat) > maxLat) fLat = Mathf.Sign(fLat) * maxLat;
                // Rollwiderstand
                float rr = -Mathf.Sign(Vector3.Dot(pv, f)) * N * (0.03f + 0.15f * loose) * Mathf.Clamp01(Mathf.Abs(Vector3.Dot(pv, f)) * 4f);
                rb.AddForceAtPosition(f * (fLong + rr) + l * fLat, hit.point, ForceMode.Force);
                usedSum += maxL > 0 ? Mathf.Abs(fLong) / maxL : 0;
                // Boden: Spurrillen / Verdichtung, Abnutzung der Grasnarbe
                if (doCompact && soil && hit.collider.gameObject.layer == Layers.Terrain && (Mathf.Abs(belt) > 0.05f || Mathf.Abs(vLong) > 0.05f))
                    soil.Compact(hit.point, 0.28f, rb.mass / 20000f * (0.4f + Mathf.Abs(belt)), 0.1f);
            }
            tractionUsed = U.Damp(tractionUsed, grounded > 0 ? usedSum / grounded : 0, 4f, dt);
            slip = U.Damp(slip, grounded > 0 ? slipSum / grounded : 0, 4f, dt);
            distL += beltL * dt;
            distR += beltR * dt;
            // Auflast auf Grubenkanten
            if (soil && grounded > 0) soil.Surcharge(rb.worldCenterOfMass, Mathf.Max(gauge, 2f), rb.mass / 20000f * dt * 0.6f);
        }

        public float Compression(int i) { return comp != null && i < comp.Length ? comp[i] : 0; }
        public float Speed => (beltL + beltR) * 0.5f;
    }
}
