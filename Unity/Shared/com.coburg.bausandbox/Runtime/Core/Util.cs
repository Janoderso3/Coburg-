using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PM = UnityEngine.PhysicsMaterial;
using PMC = UnityEngine.PhysicsMaterialCombine;
#else
using PM = UnityEngine.PhysicMaterial;
using PMC = UnityEngine.PhysicMaterialCombine;
#endif

namespace BauSandbox
{
    /// <summary>Physik-Ebenen. Nummern statt Namen, damit keine Projekteinstellungen nötig sind.</summary>
    public static class Layers
    {
        public const int Terrain = 8;
        public const int Vehicle = 9;
        public const int Tool = 10;      // Löffel, Schild, Ausleger: Bodenkontakt wird selbst berechnet
        public const int Building = 11;  // intakte Bauteile (statisch)
        public const int Debris = 12;    // Trümmer
        public const int Clod = 13;      // Erdbrocken
        public const int Element = 14;   // Fertigteile am Kran
        public const int Hook = 15;      // Kranhaken / Abrissbirne

        public static void Setup()
        {
            for (int a = 0; a < 32; a++)
                for (int b = 0; b < 32; b++)
                    Physics.IgnoreLayerCollision(a, b, false);
            Physics.IgnoreLayerCollision(Tool, Terrain, true);
            Physics.IgnoreLayerCollision(Tool, Building, true);
            Physics.IgnoreLayerCollision(Tool, Tool, true);
            Physics.IgnoreLayerCollision(Hook, Element, true);
        }

        public static int Mask(params int[] layers)
        {
            int m = 0;
            foreach (var l in layers) m |= 1 << l;
            return m;
        }
    }

    public static class U
    {
        public static float Approach(float v, float target, float rate)
        {
            if (v < target) return Mathf.Min(target, v + rate);
            return Mathf.Max(target, v - rate);
        }

        public static float Damp(float v, float target, float lambda, float dt)
        {
            return Mathf.Lerp(v, target, 1f - Mathf.Exp(-lambda * dt));
        }

        public static Vector3 Damp(Vector3 v, Vector3 target, float lambda, float dt)
        {
            return Vector3.Lerp(v, target, 1f - Mathf.Exp(-lambda * dt));
        }

        public static float DampAngle(float a, float target, float lambda, float dt)
        {
            return a + Mathf.DeltaAngle(a, target) * (1f - Mathf.Exp(-lambda * dt));
        }

        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ---------- Rauschen ----------
        static readonly int[] perm = BuildPerm();
        static int[] BuildPerm()
        {
            var p = new int[512];
            var r = new System.Random(1337);
            var b = new int[256];
            for (int i = 0; i < 256; i++) b[i] = i;
            for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); int t = b[i]; b[i] = b[j]; b[j] = t; }
            for (int i = 0; i < 512; i++) p[i] = b[i & 255];
            return p;
        }
        static float Hash(int x, int y) { return perm[(perm[x & 255] + y) & 511] / 255f; }

        public static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) * 2f - 1f;
        }

        public static float Fbm(float x, float y, int oct = 4)
        {
            float s = 0, a = 0.5f, f = 1;
            for (int i = 0; i < oct; i++) { s += a * Noise(x * f, y * f); f *= 2.03f; a *= 0.5f; }
            return s;
        }

        /// <summary>Kachelbares Rauschen (Periode p) für Texturen.</summary>
        public static float TileNoise(float x, float y, int p)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            int x0 = ((xi % p) + p) % p, y0 = ((yi % p) + p) % p, x1 = (x0 + 1) % p, y1 = (y0 + 1) % p;
            float a = Hash(x0, y0), b = Hash(x1, y0), c = Hash(x0, y1), d = Hash(x1, y1);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) * 2f - 1f;
        }

        // ---------- Rigidbody (Unity 6 hat neue Namen) ----------
        public static Vector3 Vel(this Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
        public static void SetVel(this Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }
        public static void SetDamping(this Rigidbody rb, float lin, float ang)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = lin;
            rb.angularDamping = ang;
#else
            rb.drag = lin;
            rb.angularDrag = ang;
#endif
        }

        static PM soilMat, steelMat, rubberMat, rubbleMat;
        public static PM SoilMat => soilMat ?? (soilMat = MakeMat("Erde", 0.75f, 0.85f, 0.02f));
        public static PM SteelMat => steelMat ?? (steelMat = MakeMat("Stahl", 0.45f, 0.55f, 0.05f));
        public static PM RubberMat => rubberMat ?? (rubberMat = MakeMat("Gummi", 0.9f, 1.0f, 0.1f));
        public static PM RubbleMat => rubbleMat ?? (rubbleMat = MakeMat("Schutt", 0.8f, 0.95f, 0.05f));

        static PM MakeMat(string n, float dyn, float stat, float bounce)
        {
            var m = new PM(n) { dynamicFriction = dyn, staticFriction = stat, bounciness = bounce };
            m.frictionCombine = PMC.Average;
            m.bounceCombine = PMC.Minimum;
            return m;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }

        public static string Fmt(float v, int d = 1)
        {
            return v.ToString("F" + d, System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
        }
    }

    /// <summary>Deterministischer Zufallsgenerator (für reproduzierbare Baupläne).</summary>
    public class Rng
    {
        uint s;
        public Rng(int seed) { s = (uint)(seed * 2654435761u) | 1u; }
        public float Next()
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return (s & 0xFFFFFF) / 16777216f;
        }
        public float Range(float a, float b) { return a + (b - a) * Next(); }
    }
}
