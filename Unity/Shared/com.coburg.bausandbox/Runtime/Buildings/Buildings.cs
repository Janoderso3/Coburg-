using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    public class Piece
    {
        public int id; public Building b; public string type; public PieceType T;
        public Vector3 c, size; public Quaternion q; public float hp; public bool alive = true, ground;
        public List<int> sup = new List<int>(), carry = new List<int>(), lat = new List<int>();
        public BoxCollider col; public long chunk; public float vol;
    }

    public class Building
    {
        public int id; public string name, kind; public List<int> pieces = new List<int>();
        public int total, alive; public bool done, demolished; public float baseY; public Transform root;
        public Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
        public Vector3 center;
    }

    public class Chunk { public List<int> pieces = new List<int>(); public GameObject go; public bool dirty = true; }

    public class PieceRef : MonoBehaviour { public int id; }

    public struct HitResult { public int hit, broken; public bool blocked; }

    /// <summary>
    /// Gebäude aus Einzelteilen mit Statik: jedes Teil braucht einen Lastpfad zum Fundament,
    /// seitliche Spannweite ohne Auflager ist begrenzt. Zerstörte Teile werden zu Trümmern (Physikkörper),
    /// die liegen bleiben und später zu Bauschutt im Gelände werden.
    /// </summary>
    public class Buildings : MonoBehaviour
    {
        public static Buildings I;
        public readonly List<Piece> pieces = new List<Piece>();
        public readonly List<Building> buildings = new List<Building>();
        public readonly List<Element> elements = new List<Element>();
        public readonly List<ConstructionSite> sites = new List<ConstructionSite>();
        readonly HashSet<int> dirtyB = new HashSet<int>();
        float stabT;
        public int destroyedCount, builtCount, demolishedCount;
        readonly Collider[] overlap = new Collider[64];
        public event System.Action<Building> OnDemolished, OnBuilt;

        // Trümmer
        readonly List<Debris> debris = new List<Debris>();
        readonly Stack<Debris> debrisPool = new Stack<Debris>();
        int maxDebris = 380;
        Mesh debrisMesh;
        public static bool BevelPieces = true;

        void Awake()
        {
            I = this;
            if (Application.isMobilePlatform) { maxDebris = 140; BevelPieces = false; }
            var k = new MeshKit();
            k.Box("x", Vector3.zero, Vector3.one, 0.04f);
            debrisMesh = k.BuildMesh(out _);
        }

        public Building NewBuilding(string name, string kind)
        {
            var b = new Building { id = buildings.Count, name = name, kind = kind };
            b.root = new GameObject(name).transform;
            b.root.SetParent(transform, false);
            buildings.Add(b);
            return b;
        }

        public Piece AddPiece(Building b, string type, Vector3 c, Vector3 size, Quaternion q)
        {
            var T = PieceType.All[type];
            var p = new Piece { id = pieces.Count, b = b, type = type, T = T, c = c, size = size, q = q, hp = T.hp, vol = size.x * size.y * size.z };
            var go = new GameObject("Teil");
            go.layer = Layers.Building;
            go.transform.SetParent(b.root, false);
            go.transform.SetPositionAndRotation(c, q);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            bc.sharedMaterial = U.RubbleMat;
            go.AddComponent<PieceRef>().id = p.id;
            p.col = bc;
            long ck = ((long)Mathf.FloorToInt(c.x / 4f) << 32) ^ (uint)Mathf.FloorToInt(c.z / 4f);
            p.chunk = ck;
            if (!b.chunks.TryGetValue(ck, out var ch)) b.chunks[ck] = ch = new Chunk();
            ch.pieces.Add(p.id);
            ch.dirty = true;
            pieces.Add(p);
            b.pieces.Add(p.id);
            b.total++; b.alive++;
            return p;
        }

        public Building CreateFromBlueprint(Blueprint bp, Vector3 pos, float yawDeg, string name, string kind)
        {
            var b = NewBuilding(name, kind);
            b.baseY = pos.y;
            b.center = pos;
            var rot = Quaternion.Euler(0, yawDeg, 0);
            var list = new List<Piece>();
            foreach (var bx in bp.boxes)
                list.Add(AddPiece(b, bx.type, pos + rot * bx.c, bx.size, rot * bx.Rot));
            Finalize(b, list, pos.y);
            return b;
        }

        public void Finalize(Building b, List<Piece> list, float groundY)
        {
            Physics.SyncTransforms();
            foreach (var p in list) p.ground = p.col.bounds.min.y <= groundY + 0.08f;
            foreach (var p in list) Link(p);
            dirtyB.Add(b.id);
        }

        void Link(Piece p)
        {
            var bb = p.col.bounds;
            bb.Expand(0.12f);
            int n = Physics.OverlapBoxNonAlloc(bb.center, bb.extents, overlap, Quaternion.identity, 1 << Layers.Building, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var r = overlap[i].GetComponent<PieceRef>();
                if (!r || r.id == p.id) continue;
                var o = pieces[r.id];
                if (!o.alive || p.carry.Contains(o.id) || p.sup.Contains(o.id) || p.lat.Contains(o.id)) continue;
                var pb = p.col.bounds; var ob = o.col.bounds;
                if (ob.min.y >= pb.max.y - 0.12f && o.c.y > p.c.y)
                { if (!p.T.noSupport) { p.carry.Add(o.id); o.sup.Add(p.id); } }
                else if (pb.min.y >= ob.max.y - 0.12f && p.c.y > o.c.y)
                { if (!o.T.noSupport) { o.carry.Add(p.id); p.sup.Add(o.id); } }
                else if (!o.T.noSupport && !p.T.noSupport) { p.lat.Add(o.id); o.lat.Add(p.id); }
                else if (p.T.noSupport && !o.T.noSupport) { o.carry.Add(p.id); p.sup.Add(o.id); }
                else if (o.T.noSupport && !p.T.noSupport) { p.carry.Add(o.id); o.sup.Add(p.id); }
            }
        }

        // ---------- Statik ----------
        readonly Dictionary<int, int> dist = new Dictionary<int, int>();
        readonly List<int> front = new List<int>();
        readonly List<int> dq = new List<int>();

        void CheckStability(Building b)
        {
            dist.Clear(); front.Clear(); dq.Clear();
            foreach (int id in b.pieces) { var p = pieces[id]; if (p.alive && p.ground) { dist[id] = 0; front.Add(id); } }
            int head = 0;
            while (front.Count > 0 || head < dq.Count)
            {
                int id;
                if (front.Count > 0) { id = front[front.Count - 1]; front.RemoveAt(front.Count - 1); }
                else id = dq[head++];
                var p = pieces[id]; int d = dist[id];
                foreach (int q in p.carry)
                {
                    if (!pieces[q].alive) continue;
                    if (!dist.TryGetValue(q, out int od) || d < od) { dist[q] = d; front.Add(q); }
                }
                foreach (int q in p.lat)
                {
                    var o = pieces[q];
                    if (!o.alive) continue;
                    int nd = d + 1;
                    if (nd > o.T.span) continue;
                    if (!dist.TryGetValue(q, out int od) || nd < od) { dist[q] = nd; dq.Add(q); }
                }
            }
            int fell = 0;
            foreach (int id in b.pieces)
            {
                var p = pieces[id];
                if (!p.alive || dist.ContainsKey(id)) continue;
                Kill(p, new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f)), false);
                fell++;
            }
            if (fell > 0)
            {
                SoundFX.I?.Play("crash", b.center + Vector3.up * 3, Mathf.Clamp(0.4f + fell * 0.03f, 0.4f, 1f), 0.25f);
                dirtyB.Add(b.id);
            }
            if (!b.demolished && b.total > 20 && (b.done || b.kind == "old"))
            {
                int standing = 0;
                foreach (int id in b.pieces) { var p = pieces[id]; if (p.alive && p.col.bounds.max.y > b.baseY + 0.8f) standing++; }
                if (standing < b.total * 0.04f) { b.demolished = true; demolishedCount++; OnDemolished?.Invoke(b); }
            }
        }

        void Kill(Piece p, Vector3 vel, bool countAsHit)
        {
            if (!p.alive) return;
            p.alive = false;
            p.col.gameObject.SetActive(false);
            destroyedCount++;
            p.b.alive--;
            dirtyB.Add(p.b.id);
            if (p.b.chunks.TryGetValue(p.chunk, out var ch)) ch.dirty = true;
            stabT = Mathf.Min(stabT, 0.05f);
            if (p.T.glass) SoundFX.I?.Play("glass", p.c, 0.8f, 0.1f);
            SpawnDebris(p.c, p.q, p.size, p.type, vel, p.vol);
        }

        public void Destroy(Piece p, Vector3 vel) { Kill(p, vel, true); }

        public int AliveIn(Building b) { return b.alive; }

        // ---------- Werkzeugkontakt ----------
        public HitResult ToolHit(Vector3 c, float r, Vector3 vel, float power, float dt, float impulse = 0)
        {
            var res = new HitResult();
            float speed = vel.magnitude;
            int n = Physics.OverlapSphereNonAlloc(c, r, overlap, 1 << Layers.Building, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var pr = overlap[i].GetComponent<PieceRef>();
                if (!pr) continue;
                var p = pieces[pr.id];
                if (!p.alive) continue;
                res.hit++;
                float dmg = impulse + power * dt * Mathf.Clamp(speed / 0.4f, 0.25f, 2.5f);
                p.hp -= dmg;
                if (p.hp <= 0)
                {
                    res.broken++;
                    var v = vel * 0.6f + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0, 0.8f), Random.Range(-0.6f, 0.6f));
                    Kill(p, v, true);
                }
                else if (p.T.hp > 30) res.blocked = true;
            }
            if (res.broken > 0)
            {
                SoundFX.I?.Play("crash", c, Mathf.Clamp(0.35f + res.broken * 0.15f, 0.3f, 1f), 0.12f);
                SoilFX.I?.Dust(c, 2 + res.broken * 2, new Color(0.72f, 0.69f, 0.64f), 1.5f);
            }
            return res;
        }

        /// <summary>Stoß (Abrissbirne, herabfallende Teile): Schaden mit Abfall zum Rand.</summary>
        public HitResult Impact(Vector3 c, float r, float dmg, Vector3 vel)
        {
            var res = new HitResult();
            int n = Physics.OverlapSphereNonAlloc(c, r, overlap, 1 << Layers.Building, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var pr = overlap[i].GetComponent<PieceRef>();
                if (!pr) continue;
                var p = pieces[pr.id];
                if (!p.alive) continue;
                res.hit++;
                float d = Vector3.Distance(overlap[i].ClosestPoint(c), c);
                p.hp -= dmg * Mathf.Clamp01(1f - d / r * 0.6f);
                if (p.hp <= 0) { res.broken++; Kill(p, vel + Random.insideUnitSphere * 1.5f, true); }
                else if (p.T.hp > 30) res.blocked = true;
            }
            if (res.broken > 0) SoilFX.I?.Dust(c, 3 + res.broken * 2, new Color(0.72f, 0.69f, 0.64f), 1.8f);
            return res;
        }

        public Piece PieceOf(Collider col)
        {
            var pr = col ? col.GetComponent<PieceRef>() : null;
            return pr ? pieces[pr.id] : null;
        }

        // ---------- Trümmer ----------
        public void SpawnDebris(Vector3 c, Quaternion q, Vector3 size, string type, Vector3 vel, float vol)
        {
            if (debris.Count >= maxDebris)
            {
                // ältesten ruhenden Brocken zu Schutt machen
                for (int i = 0; i < debris.Count; i++) if (debris[i].resting) { debris[i].Crumble(true); break; }
                if (debris.Count >= maxDebris) debris[0].Crumble(true);
            }
            var d = debrisPool.Count > 0 ? debrisPool.Pop() : CreateDebris();
            d.Activate(c, q, size, type, vel, vol);
            debris.Add(d);
        }

        Debris CreateDebris()
        {
            var go = new GameObject("Trümmer");
            go.transform.SetParent(transform, false);
            go.layer = Layers.Debris;
            go.AddComponent<MeshFilter>().sharedMesh = debrisMesh;
            go.AddComponent<MeshRenderer>();
            var bc = go.AddComponent<BoxCollider>();
            bc.sharedMaterial = U.RubbleMat;
            var rb = go.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var d = go.AddComponent<Debris>();
            d.sys = this;
            return d;
        }

        internal void ReleaseDebris(Debris d)
        {
            debris.Remove(d);
            d.gameObject.SetActive(false);
            debrisPool.Push(d);
        }

        public int DebrisCount => debris.Count;
        public float DebrisVolume()
        {
            float v = 0;
            foreach (var d in debris) v += d.vol;
            return v;
        }

        // ---------- Darstellung: Teile je 4-m-Block zu einem Mesh zusammengefasst ----------
        void RebuildChunk(Building b, Chunk ch)
        {
            ch.dirty = false;
            var k = new MeshKit();
            int count = 0;
            foreach (int id in ch.pieces)
            {
                var p = pieces[id];
                if (!p.alive) continue;
                float bev = BevelPieces && !p.T.glass ? Mathf.Min(0.025f, Mathf.Min(p.size.x, Mathf.Min(p.size.y, p.size.z)) * 0.1f) : 0f;
                k.Box(p.T.mat, p.c, p.size, p.q, bev);
                count++;
            }
            if (ch.go) Destroy(ch.go);
            ch.go = null;
            if (count == 0) return;
            ch.go = k.Build(b.root, "Block");
            ch.go.transform.SetParent(b.root, true);
            ch.go.transform.position = Vector3.zero;
            ch.go.transform.rotation = Quaternion.identity;
            var mr = ch.go.GetComponent<MeshRenderer>();
            mr.receiveShadows = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            stabT -= dt;
            if (dirtyB.Count > 0 && stabT <= 0)
            {
                int id = -1;
                foreach (var x in dirtyB) { id = x; break; }
                dirtyB.Remove(id);
                CheckStability(buildings[id]);
                stabT = 0.1f;
            }
            foreach (var b in buildings)
                foreach (var ch in b.chunks.Values)
                    if (ch.dirty) RebuildChunk(b, ch);
            for (int i = sites.Count - 1; i >= 0; i--) sites[i].Tick(dt);
        }

        public void NotifyBuilt(Building b) { builtCount++; OnBuilt?.Invoke(b); }

        public void ClearAll()
        {
            foreach (var d in debris.ToArray()) { d.gameObject.SetActive(false); debrisPool.Push(d); }
            debris.Clear();
            foreach (var e in elements.ToArray()) if (e) Object.Destroy(e.gameObject);
            elements.Clear();
            foreach (var s in sites.ToArray()) s.Remove();
            sites.Clear();
            foreach (var b in buildings) if (b.root) Object.Destroy(b.root.gameObject);
            buildings.Clear();
            pieces.Clear();
            dirtyB.Clear();
            destroyedCount = builtCount = demolishedCount = 0;
        }
    }

    /// <summary>Trümmerteil: echter Physikkörper, zerfällt nach dem Liegenbleiben zu Bauschutt.</summary>
    public class Debris : MonoBehaviour, IScoopable
    {
        public Buildings sys;
        public float vol;
        public string type;
        public bool resting;
        Rigidbody rb;
        float rest, age;
        bool active, hitDone;
        public float Volume => vol * 1.45f;
        public SoilMat Mat => SoilMat.Rubble;

        public void Activate(Vector3 c, Quaternion q, Vector3 size, string t, Vector3 vel, float v)
        {
            if (!rb) rb = GetComponent<Rigidbody>();
            type = t; vol = v; rest = 0; age = 0; active = true; resting = false; hitDone = false;
            transform.SetPositionAndRotation(c, q);
            transform.localScale = size;
            GetComponent<MeshRenderer>().sharedMaterial = Mats.Get(PieceType.All[t].mat);
            GetComponent<BoxCollider>().size = Vector3.one;
            gameObject.SetActive(true);
            rb.mass = Mathf.Max(10f, v * PieceType.All[t].dens * 1000f);
            rb.SetVel(vel);
            rb.angularVelocity = Random.insideUnitSphere * 1.5f;
            rb.WakeUp();
        }

        void FixedUpdate()
        {
            if (!active) return;
            float dt = Time.fixedDeltaTime;
            age += dt;
            bool still = rb.Vel().sqrMagnitude < 0.04f && rb.angularVelocity.sqrMagnitude < 0.2f;
            if (still) rest += dt; else rest = 0;
            resting = rest > 0.5f;
            float lim = type == "glass" ? 1.5f : 6f;
            if (rest > lim || age > 40f || transform.position.y < SoilTerrain.BaseY) Crumble(transform.position.y < SoilTerrain.BaseY);
        }

        void OnCollisionEnter(Collision c)
        {
            if (!active || hitDone) return;
            float v = c.relativeVelocity.magnitude;
            if (v < 3.5f || rb.mass < 120f) return;
            if (c.collider.gameObject.layer == Layers.Building)
            {
                hitDone = true;
                var p = sys.PieceOf(c.collider);
                if (p != null && p.alive)
                {
                    p.hp -= rb.mass / 1000f * v * v * 1.8f;
                    if (p.hp <= 0) sys.Destroy(p, rb.Vel() * 0.3f + Vector3.down);
                }
            }
            if (rb.mass > 150f && v > 2.5f) SoundFX.I?.Play("thud", transform.position, Mathf.Clamp(rb.mass / 1000f * v * 0.15f, 0.2f, 1f), 0.06f);
            if (v > 4.5f && Random.value < 0.3f) SoilFX.I?.Dust(transform.position, 2, new Color(0.72f, 0.69f, 0.64f), 1.2f);
        }

        public void Absorb() { if (active) { active = false; sys.ReleaseDebris(this); } }

        /// <summary>Zerfällt zu Bauschutt: in einer Mulde zur Ladung, sonst ins Gelände.</summary>
        public void Crumble(bool force = false)
        {
            if (!active) return;
            var p = transform.position;
            bool given = false;
            if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var hit, 2f, ~Layers.Mask(Layers.Debris, Layers.Clod), QueryTriggerInteraction.Ignore))
            {
                var rcv = hit.collider.GetComponentInParent<ISoilReceiver>();
                if (rcv != null) { rcv.Receive(Volume, SoilMat.Rubble); given = true; }
                else if (hit.collider.gameObject.layer == Layers.Building && !force)
                {
                    // liegt auf einer Decke: bleibt als Trümmer liegen, bis darunter abgebrochen wird
                    rest = 0; age = 0;
                    return;
                }
            }
            active = false;
            if (!given && SoilTerrain.I)
            {
                float r = Mathf.Max(0.3f, Mathf.Max(transform.localScale.x, transform.localScale.z) * 0.9f);
                SoilTerrain.I.Deposit(p.x, p.z, Volume, SoilMat.Rubble, r);
            }
            sys.ReleaseDebris(this);
        }
    }
}
