using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Fängt fallendes Schüttgut auf (Kippermulde, Schalung ...).</summary>
    public interface ISoilCatcher
    {
        bool TryCatch(Vector3 p, Vector3 v, float vol, SoilMat mat);
    }

    /// <summary>Nimmt zerfallende Brocken/Trümmer auf, die darauf liegen (z. B. Mulde).</summary>
    public interface ISoilReceiver
    {
        void Receive(float vol, SoilMat mat);
    }

    /// <summary>
    /// Schüttgut-Effekte: fallende Erdkrümel (tragen echtes Volumen), Erdbrocken als Physikkörper,
    /// Staub und Abgas als Partikelsystem.
    /// </summary>
    public class SoilFX : MonoBehaviour
    {
        public static SoilFX I;
        public static readonly List<ISoilCatcher> Catchers = new List<ISoilCatcher>();

        struct Grain { public Vector3 p, v; public float vol, size, age; public SoilMat mat; public Quaternion rot; public Vector3 spin; }
        readonly List<Grain> grains = new List<Grain>(2048);
        Mesh rockMesh, clodMesh;
        readonly Dictionary<SoilMat, List<Matrix4x4>> batches = new Dictionary<SoilMat, List<Matrix4x4>>();
        readonly Matrix4x4[] buf = new Matrix4x4[1023];
        int maxGrains = 2000;

        readonly List<Clod> clods = new List<Clod>();
        readonly Stack<Clod> clodPool = new Stack<Clod>();
        int maxClods = 140;

        ParticleSystem dustPS, smokePS;
        public float soundAccum;

        void Awake()
        {
            I = this;
            if (Application.isMobilePlatform) { maxGrains = 900; maxClods = 60; }
            rockMesh = RockMesh(1, 0.25f);
            clodMesh = RockMesh(2, 0.3f);
            foreach (SoilMat m in System.Enum.GetValues(typeof(SoilMat))) batches[m] = new List<Matrix4x4>();
            dustPS = MakePS("Staub", 1600, 1f);
            smokePS = MakePS("Abgas", 800, 0.5f);
        }

        // ---------- Felsbrocken-Mesh (verformte Ikosaeder-Kugel) ----------
        public static Mesh RockMesh(int seed, float rough)
        {
            var r = new Rng(seed * 31 + 7);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] b = { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var disp = new float[12];
            for (int i = 0; i < 12; i++) disp[i] = 1f + (r.Next() - 0.5f) * rough * 2f;
            var mesh = new Mesh();
            for (int i = 0; i < f.Length; i += 3)
            {
                // flach schattiert: eigene Vertices je Dreieck
                for (int q = 0; q < 3; q++) verts.Add(b[f[i + q]].normalized * disp[f[i + q]] * 0.5f);
                // Unity: Uhrzeigersinn = Vorderseite
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            }
            var flat = new Vector3[verts.Count];
            var uvs = new Vector2[verts.Count];
            for (int i = 0; i < verts.Count; i++) { flat[i] = verts[i]; uvs[i] = new Vector2(verts[i].x + verts[i].z, verts[i].y); }
            mesh.vertices = flat;
            mesh.uv = uvs;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // Ausrichtung prüfen: Normalen sollen nach außen zeigen
            var ns = mesh.normals;
            if (Vector3.Dot(ns[0], flat[0] + flat[1] + flat[2]) < 0)
            {
                for (int i = 0; i < tris.Count; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
            }
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static string MatKey(SoilMat m)
        {
            switch (m)
            {
                case SoilMat.Rubble: return "rubble_chunk";
                case SoilMat.Gravel: return "gravel_chunk";
                case SoilMat.Concrete: return "concrete_wet";
                default: return "soil_chunk";
            }
        }

        // ---------- Fallende Krümel ----------
        public void SpawnFalling(Vector3 p, Vector3 v, float vol, SoilMat mat, float size = 0f)
        {
            if (grains.Count >= maxGrains)
            {
                var o = grains[0];
                grains.RemoveAt(0);
                Land(o);
            }
            if (size <= 0) size = Mathf.Clamp(Mathf.Pow(Mathf.Max(vol, 0.0002f), 1f / 3f) * 0.75f, 0.05f, 0.26f);
            grains.Add(new Grain { p = p, v = v, vol = vol, mat = mat, size = size, rot = Random.rotation, spin = Random.insideUnitSphere * 8f });
        }

        void Land(Grain g)
        {
            if (g.vol > 0 && SoilTerrain.I) SoilTerrain.I.Deposit(g.p.x, g.p.z, g.vol, g.mat, 0.32f);
        }

        // ---------- Brocken ----------
        public void SpawnClod(Vector3 p, float vol, SoilMat mat, Vector3 vel)
        {
            // große Volumina in mehrere Brocken teilen
            int n = Mathf.Clamp(Mathf.CeilToInt(vol / 0.12f), 1, 10);
            float each = vol / n;
            for (int i = 0; i < n; i++)
            {
                if (clods.Count >= maxClods)
                {
                    // ältesten Brocken zerfallen lassen
                    clods[0].Crumble(true);
                }
                var c = clodPool.Count > 0 ? clodPool.Pop() : CreateClod();
                var pos = p + Random.insideUnitSphere * Mathf.Pow(vol, 1f / 3f) * 0.4f;
                c.Activate(pos, each, mat, vel + Random.insideUnitSphere * 0.6f, Mats.Get(MatKey(mat)));
                clods.Add(c);
            }
        }

        Clod CreateClod()
        {
            var go = new GameObject("Erdbrocken");
            go.transform.SetParent(transform, false);
            go.layer = Layers.Clod;
            go.AddComponent<MeshFilter>().sharedMesh = clodMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.45f;
            col.sharedMaterial = U.SoilMat;
            var rb = go.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var c = go.AddComponent<Clod>();
            c.fx = this;
            return c;
        }

        internal void Release(Clod c)
        {
            clods.Remove(c);
            c.gameObject.SetActive(false);
            clodPool.Push(c);
        }

        public int ClodCount => clods.Count;

        // ---------- Staub / Rauch ----------
        ParticleSystem MakePS(string name, int max, float soft)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 3f;
            main.startSpeed = 0f;
            main.gravityModifier = -0.02f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(1, 1.6f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.35f);
            vel.y = new ParticleSystem.MinMaxCurve(0.05f);
            vel.z = new ParticleSystem.MinMaxCurve(0.12f);
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.dampen = 0.08f;
            lim.limit = 0.2f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = DustMaterial();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static Material dustMat;
        public static Material DustMaterial()
        {
            if (dustMat) return dustMat;
            dustMat = Resources.Load<Material>("BauSandbox/Materials/dust");
            if (dustMat) return dustMat;
            var sh = Shader.Find("HDRP/Unlit");
            if (!sh) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!sh) sh = Shader.Find("Particles/Standard Unlit");
            if (!sh) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            dustMat = new Material(sh) { name = "dust" };
            var tex = SoftDot();
            if (dustMat.HasProperty("_BaseColorMap")) dustMat.SetTexture("_BaseColorMap", tex);
            if (dustMat.HasProperty("_BaseMap")) dustMat.SetTexture("_BaseMap", tex);
            if (dustMat.HasProperty("_MainTex")) dustMat.SetTexture("_MainTex", tex);
            return dustMat;
        }

        public static Texture2D SoftDot()
        {
            int s = 64;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "softdot" };
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2 - 1, dy = (y + 0.5f) / s * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = 0.75f + 0.25f * U.Noise(x * 0.25f, y * 0.25f);
                    float a = Mathf.Clamp01(1 - d) * n;
                    px[y * s + x] = new Color(1, 1, 1, a * a);
                }
            t.SetPixels(px);
            t.Apply(true, false);
            return t;
        }

        public void Dust(Vector3 p, int count, Color c, float size = 1f)
        {
            if (!dustPS) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.position = p + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0, 0.3f), Random.Range(-0.5f, 0.5f));
                ep.velocity = new Vector3(Random.Range(-0.7f, 0.7f), Random.Range(0.3f, 1.1f), Random.Range(-0.7f, 0.7f));
                ep.startSize = size * Random.Range(0.8f, 1.6f);
                ep.startLifetime = Random.Range(2.5f, 5f);
                var cc = c * Random.Range(0.9f, 1.1f); cc.a = Random.Range(0.25f, 0.45f);
                ep.startColor = cc;
                ep.rotation = Random.Range(0, 360);
                dustPS.Emit(ep, 1);
            }
        }

        public void Smoke(Vector3 p, Vector3 v, float density, float darkness)
        {
            if (!smokePS) return;
            float g = Mathf.Lerp(0.55f, 0.15f, darkness);
            var ep = new ParticleSystem.EmitParams
            {
                position = p,
                velocity = v + new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(1.0f, 1.6f), Random.Range(-0.15f, 0.15f)),
                startSize = Random.Range(0.25f, 0.4f),
                startLifetime = 1.2f + density * 1.8f,
                startColor = new Color(g, g, g, 0.15f + density * 0.35f),
                rotation = Random.Range(0, 360),
            };
            smokePS.Emit(ep, 1);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var T = SoilTerrain.I;
            int n = 0;
            for (int i = 0; i < grains.Count; i++)
            {
                var g = grains[i];
                g.v.y -= 9.81f * dt;
                g.v *= 1f - 0.4f * dt;
                g.p += g.v * dt;
                g.age += dt;
                g.rot = Quaternion.Euler(g.spin * dt) * g.rot;
                bool gone = false;
                if (g.v.y < 0)
                    for (int c = 0; c < Catchers.Count; c++)
                        if (Catchers[c].TryCatch(g.p, g.v, g.vol, g.mat)) { gone = true; break; }
                if (!gone && T)
                {
                    float gy = T.Height(g.p.x, g.p.z);
                    if (g.p.y <= gy || g.age > 8f || !T.Inside(g.p.x, g.p.z, 1f))
                    {
                        if (T.Inside(g.p.x, g.p.z, 1f)) { g.p.y = gy; Land(g); }
                        if (g.vol > 0.01f && Random.value < 0.2f) Dust(new Vector3(g.p.x, gy + 0.1f, g.p.z), 1, DustColor(g.mat), 0.8f);
                        soundAccum += g.vol;
                        gone = true;
                    }
                }
                if (!gone) grains[n++] = g;
            }
            grains.RemoveRange(n, grains.Count - n);
            // Zeichnen (Instancing)
            foreach (var kv in batches) kv.Value.Clear();
            foreach (var g in grains)
                batches[g.mat].Add(Matrix4x4.TRS(g.p, g.rot, Vector3.one * g.size));
            foreach (var kv in batches)
            {
                var list = kv.Value;
                if (list.Count == 0) continue;
                var mat = Mats.Get(MatKey(kv.Key));
                for (int s = 0; s < list.Count; s += 1023)
                {
                    int c = Mathf.Min(1023, list.Count - s);
                    list.CopyTo(s, buf, 0, c);
                    Graphics.DrawMeshInstanced(rockMesh, 0, mat, buf, c, null, UnityEngine.Rendering.ShadowCastingMode.On, true, 0);
                }
            }
        }

        public static Color DustColor(SoilMat m)
        {
            switch (m)
            {
                case SoilMat.Rubble: return new Color(0.68f, 0.65f, 0.6f);
                case SoilMat.Concrete: return new Color(0.7f, 0.7f, 0.68f);
                case SoilMat.Gravel: return new Color(0.66f, 0.64f, 0.6f);
                default: return new Color(0.58f, 0.5f, 0.4f);
            }
        }
    }

    /// <summary>Erdbrocken: rollt physikalisch, zerfällt nach dem Liegenbleiben zu losem Boden.</summary>
    public class Clod : MonoBehaviour, IScoopable
    {
        public float Volume => vol;
        public SoilMat Mat => mat;
        public void Absorb() { if (active) { active = false; fx.Release(this); } }

        public SoilFX fx;
        public float vol;
        public SoilMat mat;
        Rigidbody rb;
        float rest, age;
        bool active;

        public void Activate(Vector3 p, float v, SoilMat m, Vector3 vel, Material material)
        {
            if (!rb) rb = GetComponent<Rigidbody>();
            vol = v; mat = m; rest = 0; age = 0; active = true;
            float size = Mathf.Clamp(Mathf.Pow(v, 1f / 3f) * 1.05f, 0.12f, 0.9f);
            transform.position = p;
            transform.rotation = Random.rotation;
            transform.localScale = new Vector3(size * Random.Range(0.85f, 1.25f), size * Random.Range(0.6f, 0.9f), size * Random.Range(0.85f, 1.2f));
            GetComponent<MeshRenderer>().sharedMaterial = material;
            gameObject.SetActive(true);
            rb.mass = Mathf.Max(5f, v * 1700f);
            rb.SetVel(vel);
            rb.angularVelocity = Random.insideUnitSphere * 3f;
            rb.WakeUp();
        }

        void FixedUpdate()
        {
            if (!active) return;
            float dt = Time.fixedDeltaTime;
            age += dt;
            var v = rb.Vel();
            if (v.sqrMagnitude < 0.04f && rb.angularVelocity.sqrMagnitude < 0.2f) rest += dt; else rest = 0;
            if (rest > 1.2f || age > 14f || transform.position.y < SoilTerrain.BaseY) Crumble(true);
        }

        void OnCollisionEnter(Collision c)
        {
            if (!active) return;
            float imp = c.relativeVelocity.magnitude;
            if (imp > 3f && Random.value < 0.3f && SoundFX.I) SoundFX.I.Play("soil", transform.position, Mathf.Clamp01(vol * 3f));
            // harter Aufprall: Brocken zerplatzt
            if (imp > 6.5f && vol > 0.02f) Crumble(true, true);
        }

        public void Crumble(bool deposit, bool burst = false)
        {
            if (!active) return;
            active = false;
            var p = transform.position;
            if (deposit)
            {
                if (burst || vol < 0.02f)
                {
                    int n = Mathf.Clamp(Mathf.CeilToInt(vol / 0.03f), 1, 12);
                    for (int i = 0; i < n; i++)
                        fx.SpawnFalling(p + Random.insideUnitSphere * 0.2f, rb.Vel() * 0.4f + Random.insideUnitSphere * 1.2f, vol / n, mat);
                }
                else
                {
                    // Auf einem Empfänger (Mulde) oder auf dem Boden zerfallen
                    bool given = false;
                    if (Physics.Raycast(p + Vector3.up * 0.2f, Vector3.down, out var hit, 1.5f, ~Layers.Mask(Layers.Clod, Layers.Debris), QueryTriggerInteraction.Ignore))
                    {
                        var rcv = hit.collider.GetComponentInParent<ISoilReceiver>();
                        if (rcv != null) { rcv.Receive(vol, mat); given = true; }
                    }
                    if (!given && SoilTerrain.I) SoilTerrain.I.Deposit(p.x, p.z, vol, mat, Mathf.Max(0.3f, transform.localScale.x * 0.6f));
                    fx.Dust(p, 2, SoilFX.DustColor(mat), 0.9f);
                }
            }
            fx.Release(this);
        }
    }
}
