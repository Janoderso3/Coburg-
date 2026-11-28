using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

public class Piece
{
    public int id; public Building b; public string type; public PieceType T;
    public Vector3 c, half; public Quaternion q, qi; public Vector3 min, max;
    public float hp, vol; public bool alive = true, ground;
    public List<int> sup = new(), carry = new(), lat = new();
    public CollisionShape3D shape; public long chunk;
}

public class Building
{
    public int id; public string name, kind; public List<int> pieces = new();
    public int total, alive; public bool done, demolished; public float baseY;
    public StaticBody3D body; public Node3D vis; public Vector3 center;
    public Dictionary<long, Chunk> chunks = new();
}

public class Chunk { public List<int> pieces = new(); public MeshInstance3D mi; public bool dirty = true; }

public struct HitResult { public int hit, broken; public bool blocked; }

/// <summary>
/// Gebäude aus Einzelteilen: Lastpfad zum Fundament, begrenzte Spannweite ohne Auflager.
/// Zerstörte Teile werden zu Physik-Trümmern, die liegen bleiben und dann zu Bauschutt werden.
/// </summary>
public partial class Buildings : Node3D
{
    public static Buildings I;
    public readonly List<Piece> pieces = new();
    public readonly List<Building> buildings = new();
    public readonly List<Element> elements = new();
    public readonly List<ConstructionSite> sites = new();
    readonly HashSet<int> dirtyB = new();
    readonly Dictionary<long, List<int>> hash = new();
    int stamp;
    readonly Dictionary<int, int> stampOf = new();
    float stabT;
    public int destroyedCount, builtCount, demolishedCount;
    public event Action<Building> OnDemolished, OnBuilt;
    readonly List<Debris> debris = new();
    readonly Stack<Debris> debrisPool = new();
    int maxDebris = 400;
    public static Mesh DebrisMesh;

    public override void _Ready()
    {
        I = this;
        var k = new MeshKit();
        k.Box("plaster", Vector3.Zero, Vector3.One, 0.04f);
        DebrisMesh = k.BuildMesh();
    }

    // ---------- räumliches Raster (1 m) ----------
    static long Key(int ix, int iz) => ((long)(ix + 4096) << 20) | (long)(iz + 4096);
    void HashInsert(Piece p)
    {
        for (int ix = (int)MathF.Floor(p.min.X); ix <= (int)MathF.Floor(p.max.X); ix++)
            for (int iz = (int)MathF.Floor(p.min.Z); iz <= (int)MathF.Floor(p.max.Z); iz++)
            {
                var k = Key(ix, iz);
                if (!hash.TryGetValue(k, out var l)) hash[k] = l = new List<int>();
                l.Add(p.id);
            }
    }
    void HashRemove(Piece p)
    {
        for (int ix = (int)MathF.Floor(p.min.X); ix <= (int)MathF.Floor(p.max.X); ix++)
            for (int iz = (int)MathF.Floor(p.min.Z); iz <= (int)MathF.Floor(p.max.Z); iz++)
                if (hash.TryGetValue(Key(ix, iz), out var l)) l.Remove(p.id);
    }
    public void Query(float minx, float minz, float maxx, float maxz, Func<Piece, bool> cb)
    {
        int st = ++stamp;
        for (int ix = (int)MathF.Floor(minx); ix <= (int)MathF.Floor(maxx); ix++)
            for (int iz = (int)MathF.Floor(minz); iz <= (int)MathF.Floor(maxz); iz++)
            {
                if (!hash.TryGetValue(Key(ix, iz), out var l)) continue;
                for (int i = 0; i < l.Count; i++)
                {
                    var p = pieces[l[i]];
                    if (!p.alive) continue;
                    if (stampOf.TryGetValue(p.id, out int s) && s == st) continue;
                    stampOf[p.id] = st;
                    if (!cb(p)) return;
                }
            }
    }

    public Building NewBuilding(string name, string kind)
    {
        var b = new Building { id = buildings.Count, name = name, kind = kind };
        b.body = new StaticBody3D { Name = name, CollisionLayer = Layers.Building, CollisionMask = 0, PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.8f } };
        AddChild(b.body);
        b.vis = new Node3D { Name = "Darstellung" };
        AddChild(b.vis);
        buildings.Add(b);
        return b;
    }

    public Piece AddPiece(Building b, string type, Vector3 c, Vector3 size, Quaternion q)
    {
        var T = PieceType.All[type];
        var p = new Piece { id = pieces.Count, b = b, type = type, T = T, c = c, half = size / 2, q = q, qi = q.Inverse(), hp = T.hp, vol = size.X * size.Y * size.Z };
        var basis = new Basis(q);
        var ex = new Vector3(
            MathF.Abs(basis.X.X) * p.half.X + MathF.Abs(basis.Y.X) * p.half.Y + MathF.Abs(basis.Z.X) * p.half.Z,
            MathF.Abs(basis.X.Y) * p.half.X + MathF.Abs(basis.Y.Y) * p.half.Y + MathF.Abs(basis.Z.Y) * p.half.Z,
            MathF.Abs(basis.X.Z) * p.half.X + MathF.Abs(basis.Y.Z) * p.half.Y + MathF.Abs(basis.Z.Z) * p.half.Z);
        p.min = c - ex; p.max = c + ex;
        p.shape = new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = new Transform3D(basis, c) };
        p.shape.SetMeta("pid", p.id);
        b.body.AddChild(p.shape);
        long ck = ((long)(int)MathF.Floor(c.X / 4f) << 32) ^ (uint)(int)MathF.Floor(c.Z / 4f);
        p.chunk = ck;
        if (!b.chunks.TryGetValue(ck, out var ch)) b.chunks[ck] = ch = new Chunk();
        ch.pieces.Add(p.id); ch.dirty = true;
        pieces.Add(p);
        HashInsert(p);
        b.pieces.Add(p.id);
        b.total++; b.alive++;
        return p;
    }

    public Building CreateFromBlueprint(Blueprint bp, Vector3 pos, float yaw, string name, string kind)
    {
        var b = NewBuilding(name, kind);
        b.baseY = pos.Y; b.center = pos;
        var rot = Quaternion.FromEuler(new Vector3(0, yaw, 0));
        var list = new List<Piece>();
        foreach (var bx in bp.boxes) list.Add(AddPiece(b, bx.type, pos + rot * bx.c, bx.size, rot * bx.Rot));
        Finalize(b, list, pos.Y);
        return b;
    }

    public void Finalize(Building b, List<Piece> list, float groundY)
    {
        foreach (var p in list) p.ground = p.min.Y <= groundY + 0.08f;
        foreach (var p in list) Link(p);
        dirtyB.Add(b.id);
    }

    void Link(Piece p)
    {
        const float e = 0.08f;
        Query(p.min.X - e, p.min.Z - e, p.max.X + e, p.max.Z + e, o =>
        {
            if (o == p) return true;
            if (o.min.Y > p.max.Y + e || o.max.Y < p.min.Y - e) return true;
            if (o.min.X > p.max.X + e || o.max.X < p.min.X - e) return true;
            if (o.min.Z > p.max.Z + e || o.max.Z < p.min.Z - e) return true;
            if (p.carry.Contains(o.id) || p.sup.Contains(o.id) || p.lat.Contains(o.id)) return true;
            if (o.min.Y >= p.max.Y - 0.12f && o.c.Y > p.c.Y) { if (!p.T.noSupport) { p.carry.Add(o.id); o.sup.Add(p.id); } }
            else if (p.min.Y >= o.max.Y - 0.12f && p.c.Y > o.c.Y) { if (!o.T.noSupport) { o.carry.Add(p.id); p.sup.Add(o.id); } }
            else if (!o.T.noSupport && !p.T.noSupport) { p.lat.Add(o.id); o.lat.Add(p.id); }
            else if (p.T.noSupport && !o.T.noSupport) { o.carry.Add(p.id); p.sup.Add(o.id); }
            else if (o.T.noSupport && !p.T.noSupport) { p.carry.Add(o.id); o.sup.Add(p.id); }
            return true;
        });
    }

    // ---------- Statik: 0-1-Breitensuche vom Fundament ----------
    readonly Dictionary<int, int> dist = new();
    readonly List<int> front = new(), dq = new();
    void CheckStability(Building b)
    {
        dist.Clear(); front.Clear(); dq.Clear();
        foreach (int id in b.pieces) { var p = pieces[id]; if (p.alive && p.ground) { dist[id] = 0; front.Add(id); } }
        int head = 0;
        while (front.Count > 0 || head < dq.Count)
        {
            int id;
            if (front.Count > 0) { id = front[^1]; front.RemoveAt(front.Count - 1); } else id = dq[head++];
            var p = pieces[id]; int d = dist[id];
            foreach (int q in p.carry) { if (!pieces[q].alive) continue; if (!dist.TryGetValue(q, out int od) || d < od) { dist[q] = d; front.Add(q); } }
            foreach (int q in p.lat)
            {
                var o = pieces[q]; if (!o.alive) continue;
                int nd = d + 1; if (nd > o.T.span) continue;
                if (!dist.TryGetValue(q, out int od) || nd < od) { dist[q] = nd; dq.Add(q); }
            }
        }
        int fell = 0;
        foreach (int id in b.pieces)
        {
            var p = pieces[id];
            if (!p.alive || dist.ContainsKey(id)) continue;
            Kill(p, new Vector3(U.Rand(-0.3f, 0.3f), 0, U.Rand(-0.3f, 0.3f)));
            fell++;
        }
        if (fell > 0) { SoundFX.I?.Play("crash", b.center + Vector3.Up * 3, Math.Clamp(0.4f + fell * 0.03f, 0.4f, 1f), 0.25f); dirtyB.Add(b.id); }
        if (!b.demolished && b.total > 20 && (b.done || b.kind == "old"))
        {
            int standing = 0;
            foreach (int id in b.pieces) { var p = pieces[id]; if (p.alive && p.max.Y > b.baseY + 0.8f) standing++; }
            if (standing < b.total * 0.04f) { b.demolished = true; demolishedCount++; OnDemolished?.Invoke(b); Hud.Toast($"{b.name} ist abgerissen!", ToastKind.Ok); }
        }
    }

    void Kill(Piece p, Vector3 vel)
    {
        if (!p.alive) return;
        p.alive = false;
        p.shape.Disabled = true;
        HashRemove(p);
        destroyedCount++;
        p.b.alive--;
        dirtyB.Add(p.b.id);
        if (p.b.chunks.TryGetValue(p.chunk, out var ch)) ch.dirty = true;
        stabT = MathF.Min(stabT, 0.05f);
        if (p.T.glass) SoundFX.I?.Play("glass", p.c, 0.8f, 0.1f);
        SpawnDebris(p.c, p.q, p.half * 2, p.type, vel, p.vol);
    }

    public void Destroy(Piece p, Vector3 vel) => Kill(p, vel);
    public void DestroyDeferred(int id) { if (id >= 0 && id < pieces.Count) Kill(pieces[id], Vector3.Down); }

    float SphereDist(Piece p, Vector3 c)
    {
        var v = p.qi * (c - p.c);
        float dx = v.X - Math.Clamp(v.X, -p.half.X, p.half.X), dy = v.Y - Math.Clamp(v.Y, -p.half.Y, p.half.Y), dz = v.Z - Math.Clamp(v.Z, -p.half.Z, p.half.Z);
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Werkzeugkontakt: Schaden pro Sekunde abhängig von Tempo; harte Teile blockieren.</summary>
    public HitResult ToolHit(Vector3 c, float r, Vector3 vel, float power, float dt, float impulse = 0)
    {
        var res = new HitResult();
        float speed = vel.Length();
        var hitList = new List<Piece>();
        Query(c.X - r, c.Z - r, c.X + r, c.Z + r, p =>
        {
            if (p.min.Y > c.Y + r || p.max.Y < c.Y - r) return true;
            if (SphereDist(p, c) > r) return true;
            hitList.Add(p);
            return true;
        });
        foreach (var p in hitList)
        {
            res.hit++;
            float dmg = impulse + power * dt * Math.Clamp(speed / 0.4f, 0.25f, 2.5f);
            p.hp -= dmg;
            if (p.hp <= 0)
            {
                res.broken++;
                Kill(p, vel * 0.6f + new Vector3(U.Rand(-0.6f, 0.6f), U.Rand(0, 0.8f), U.Rand(-0.6f, 0.6f)));
            }
            else if (p.T.hp > 30) res.blocked = true;
        }
        if (res.broken > 0)
        {
            SoundFX.I?.Play("crash", c, Math.Clamp(0.35f + res.broken * 0.15f, 0.3f, 1f), 0.12f);
            SoilFX.I?.Dust(c, 2 + res.broken * 2, new Color(0.72f, 0.69f, 0.64f), 1.5f);
        }
        return res;
    }

    public HitResult Impact(Vector3 c, float r, float dmg, Vector3 vel)
    {
        var res = new HitResult();
        var hitList = new List<(Piece, float)>();
        Query(c.X - r, c.Z - r, c.X + r, c.Z + r, p =>
        {
            if (p.min.Y > c.Y + r || p.max.Y < c.Y - r) return true;
            float d = SphereDist(p, c);
            if (d <= r) hitList.Add((p, d));
            return true;
        });
        foreach (var (p, d) in hitList)
        {
            res.hit++;
            p.hp -= dmg * U.Clamp01(1f - d / r * 0.6f);
            if (p.hp <= 0) { res.broken++; Kill(p, vel + U.RandSphere() * 1.5f); }
            else if (p.T.hp > 30) res.blocked = true;
        }
        if (res.broken > 0) SoilFX.I?.Dust(c, 3 + res.broken * 2, new Color(0.72f, 0.69f, 0.64f), 1.8f);
        return res;
    }

    public Piece PieceFromShape(GodotObject body, int shapeIdx)
    {
        if (body is not StaticBody3D sb) return null;
        var owner = sb.ShapeFindOwner(shapeIdx);
        if (sb.ShapeOwnerGetOwner(owner) is CollisionShape3D cs && cs.HasMeta("pid"))
        {
            var p = pieces[(int)cs.GetMeta("pid")];
            return p.alive ? p : null;
        }
        return null;
    }

    /// <summary>Höchste Oberkante intakter Teile unter (x,z), die nicht höher als maxY liegt.</summary>
    public float TopAt(float x, float z, float maxY)
    {
        float best = float.NegativeInfinity;
        Query(x - 0.01f, z - 0.01f, x + 0.01f, z + 0.01f, p =>
        {
            if (x < p.min.X || x > p.max.X || z < p.min.Z || z > p.max.Z) return true;
            if (p.max.Y <= maxY && p.max.Y > best) best = p.max.Y;
            return true;
        });
        return best;
    }

    // ---------- Trümmer ----------
    public void SpawnDebris(Vector3 c, Quaternion q, Vector3 size, string type, Vector3 vel, float vol)
    {
        if (debris.Count >= maxDebris)
        {
            for (int i = 0; i < debris.Count; i++) if (debris[i].resting) { debris[i].Crumble(true); break; }
            if (debris.Count >= maxDebris) debris[0].Crumble(true);
        }
        Debris d;
        if (debrisPool.Count > 0) d = debrisPool.Pop();
        else { d = new Debris { sys = this }; AddChild(d); }
        d.Activate(c, q, size, type, vel, vol);
        debris.Add(d);
    }

    internal void ReleaseDebris(Debris d)
    {
        debris.Remove(d);
        d.Deactivate();
        debrisPool.Push(d);
    }

    public int DebrisCount => debris.Count;

    void RebuildChunk(Building b, Chunk ch)
    {
        ch.dirty = false;
        var k = new MeshKit();
        int count = 0;
        foreach (int id in ch.pieces)
        {
            var p = pieces[id];
            if (!p.alive) continue;
            float bev = !p.T.glass ? MathF.Min(0.025f, MathF.Min(p.half.X, MathF.Min(p.half.Y, p.half.Z)) * 0.2f) : 0f;
            k.Box(p.T.mat, p.c, p.half * 2, p.q, bev);
            count++;
        }
        ch.mi?.QueueFree();
        ch.mi = null;
        if (count == 0) return;
        ch.mi = k.Build(b.vis, "Block");
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        stabT -= dt;
        if (dirtyB.Count > 0 && stabT <= 0)
        {
            int id = -1;
            foreach (var x in dirtyB) { id = x; break; }
            dirtyB.Remove(id);
            CheckStability(buildings[id]);
            stabT = 0.1f;
        }
        foreach (var b in buildings) foreach (var ch in b.chunks.Values) if (ch.dirty) RebuildChunk(b, ch);
        for (int i = sites.Count - 1; i >= 0; i--) sites[i].Tick(dt);
    }

    public void NotifyBuilt(Building b) { builtCount++; OnBuilt?.Invoke(b); }
}

/// <summary>Trümmerteil: Physikkörper, zerfällt nach dem Liegenbleiben zu Bauschutt.</summary>
public partial class Debris : RigidBody3D, IScoopable
{
    public Buildings sys;
    public float vol;
    public string type;
    public bool resting;
    float rest, age;
    bool active, hitDone;
    MeshInstance3D mesh;
    CollisionShape3D shape;
    public float Volume => vol * 1.45f;
    public SoilMat Mat => SoilMat.Rubble;

    public override void _Ready()
    {
        CollisionLayer = Layers.Debris;
        CollisionMask = Layers.All;
        ContinuousCd = true;
        ContactMonitor = true;
        MaxContactsReported = 4;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.8f, Bounce = 0.05f };
        mesh = new MeshInstance3D { Mesh = Buildings.DebrisMesh };
        AddChild(mesh);
        shape = new CollisionShape3D { Shape = new BoxShape3D() };
        AddChild(shape);
        BodyShapeEntered += OnHit;
    }

    public void Activate(Vector3 c, Quaternion q, Vector3 size, string t, Vector3 vel, float v)
    {
        type = t; vol = v; rest = 0; age = 0; active = true; resting = false; hitDone = false;
        mesh.Scale = size;
        mesh.MaterialOverride = Mats.Get(PieceType.All[t].mat);
        ((BoxShape3D)shape.Shape).Size = size;
        Visible = true;
        Freeze = false;
        CollisionLayer = Layers.Debris;
        GlobalTransform = new Transform3D(new Basis(q), c);
        Mass = MathF.Max(10f, v * PieceType.All[t].dens * 1000f);
        LinearVelocity = vel;
        AngularVelocity = U.RandSphere() * 1.5f;
        Sleeping = false;
    }

    public void Deactivate()
    {
        active = false; Visible = false; Freeze = true; CollisionLayer = 0;
        GlobalPosition = new Vector3(0, -120, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!active) return;
        float dt = (float)delta;
        age += dt;
        bool still = LinearVelocity.LengthSquared() < 0.04f && AngularVelocity.LengthSquared() < 0.2f;
        if (still) rest += dt; else rest = 0;
        resting = rest > 0.5f;
        float lim = type == "glass" ? 1.5f : 6f;
        if (rest > lim || age > 40f || GlobalPosition.Y < SoilTerrain.BaseY) Crumble(GlobalPosition.Y < SoilTerrain.BaseY);
    }

    void OnHit(Rid bodyRid, Node body, long bodyShapeIndex, long localShapeIndex)
    {
        if (!active || hitDone) return;
        float v = LinearVelocity.Length();
        if (v > 2.5f && Mass > 150f) SoundFX.I?.Play("thud", GlobalPosition, Math.Clamp(Mass / 1000f * v * 0.15f, 0.2f, 1f), 0.06f);
        if (v < 3.5f || Mass < 120f) return;
        var p = sys.PieceFromShape(body, (int)bodyShapeIndex);
        if (p != null)
        {
            hitDone = true;
            p.hp -= Mass / 1000f * v * v * 1.8f;
            if (p.hp <= 0) sys.CallDeferred(nameof(Buildings.DestroyDeferred), p.id);
        }
        if (v > 4.5f && U.Rand() < 0.3f) SoilFX.I?.Dust(GlobalPosition, 2, new Color(0.72f, 0.69f, 0.64f), 1.2f);
    }

    public void Absorb() { if (active) sys.ReleaseDebris(this); }

    public void Crumble(bool force = false)
    {
        if (!active) return;
        var p = GlobalPosition;
        bool given = false;
        var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 0.3f, p + Vector3.Down * 2f, Layers.All & ~(Layers.Debris | Layers.Clod));
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0)
        {
            var col = hit["collider"].AsGodotObject() as Node;
            var rcv = Clod.FindReceiver(col);
            if (rcv != null) { rcv.Receive(Volume, SoilMat.Rubble); given = true; }
            else if (col is StaticBody3D && col.GetParent() is Buildings && !force) { rest = 0; age = 0; return; }
        }
        if (!given && SoilTerrain.I != null) SoilTerrain.I.Deposit(p.X, p.Z, Volume, SoilMat.Rubble, MathF.Max(0.3f, MathF.Max(mesh.Scale.X, mesh.Scale.Z) * 0.9f));
        sys.ReleaseDebris(this);
    }
}
