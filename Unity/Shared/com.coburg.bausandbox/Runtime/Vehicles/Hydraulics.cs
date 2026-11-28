using UnityEngine;

namespace BauSandbox
{
    /// <summary>Hydraulisch angetriebenes Gelenk: Proportionalventil mit Rampe, Endanschläge.</summary>
    public class HydJoint
    {
        public float pos, vel, min, max, maxSpeed, ramp;
        public HydJoint(float start, float min, float max, float maxSpeed, float ramp)
        { pos = start; this.min = min; this.max = max; this.maxSpeed = maxSpeed; this.ramp = ramp; }

        /// <summary>cmd -1..1, factor = Durchfluss-Anteil * Lastabfall.</summary>
        public void Step(float cmd, float factor, float dt)
        {
            vel = U.Approach(vel, cmd * maxSpeed * factor, ramp * dt);
            pos += vel * dt;
            if (pos < min) { pos = min; vel = 0; }
            if (pos > max) { pos = max; vel = 0; }
        }
        public bool AtStop => pos <= min + 1e-4f || pos >= max - 1e-4f;
    }

    /// <summary>Sichtbarer Hydraulikzylinder (Rohr + verchromte Kolbenstange) zwischen zwei Punkten.</summary>
    public class HydCylinder
    {
        readonly Transform root, barrel, rod;
        readonly float barrelLen;
        static Mesh unitCyl;

        public HydCylinder(Transform parent, float rBarrel, float rRod, float barrelLen, string mat = "paint_yellow")
        {
            if (!unitCyl)
            {
                var k = new MeshKit();
                k.Cyl("x", new Vector3(0, 0.5f, 0), 1f, 1f, 1f, 14, Quaternion.identity);
                unitCyl = k.BuildMesh(out _);
            }
            this.barrelLen = barrelLen;
            root = new GameObject("Zylinder").transform;
            root.SetParent(parent, false);
            barrel = Part("Rohr", rBarrel, Mats.Get(mat));
            rod = Part("Kolbenstange", rRod, Mats.Get("chrome"));
        }

        Transform Part(string n, float r, Material m)
        {
            var go = new GameObject(n);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = unitCyl;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            go.transform.localScale = new Vector3(r, 1, r);
            return go.transform;
        }

        public void Update(Vector3 a, Vector3 b)
        {
            var d = b - a;
            float L = d.magnitude;
            if (L < 1e-4f) return;
            root.position = a;
            root.rotation = Quaternion.FromToRotation(Vector3.up, d / L);
            float bl = Mathf.Min(barrelLen, L * 0.95f);
            var s = barrel.localScale; s.y = bl; barrel.localScale = s;
            barrel.localPosition = Vector3.zero;
            float rl = Mathf.Max(0.05f, L - bl * 0.85f);
            s = rod.localScale; s.y = rl; rod.localScale = s;
            rod.localPosition = new Vector3(0, L - rl, 0);
        }

        public void SetVisible(bool v)
        {
            root.gameObject.SetActive(v);
        }
    }

    /// <summary>Etwas, das ein Löffel aufnehmen kann (Erdbrocken, Trümmer).</summary>
    public interface IScoopable
    {
        float Volume { get; }
        SoilMat Mat { get; }
        void Absorb();
    }
}
