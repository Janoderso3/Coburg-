using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Fertigteil (Wand, Dachelement) als Physikkörper. Auf dem Lagerplatz im Transportbock fixiert.</summary>
    public class Element : MonoBehaviour
    {
        public ElementDef def;
        public ConstructionSite site;
        public int index;
        public string state = "stored";
        public Rigidbody rb;
        public Vector3 liftLocal;
        public float MassT => def.massT;
        public Vector3 LiftPointWorld => transform.TransformPoint(liftLocal);

        public static Element Create(ElementDef def, ConstructionSite site, int index, Vector3 pos, float yawDeg)
        {
            var go = new GameObject(def.name);
            go.layer = Layers.Element;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yawDeg, 0));
            var k = new MeshKit();
            foreach (var b in def.boxes)
            {
                var T = PieceType.All[b.type];
                k.Box(T.mat, b.c, b.size, b.Rot, T.glass ? 0 : 0.02f);
                var cgo = new GameObject("Kollision");
                cgo.layer = Layers.Element;
                cgo.transform.SetParent(go.transform, false);
                cgo.transform.localPosition = b.c;
                cgo.transform.localRotation = b.Rot;
                var bc = cgo.AddComponent<BoxCollider>();
                bc.size = b.size;
                bc.sharedMaterial = U.RubbleMat;
            }
            k.Build(go.transform, "Fertigteil");
            var e = go.AddComponent<Element>();
            e.def = def; e.site = site; e.index = index;
            e.rb = go.AddComponent<Rigidbody>();
            e.rb.mass = def.massT * 1000f;
            e.rb.interpolation = RigidbodyInterpolation.Interpolate;
            e.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            e.rb.solverIterations = 20;
            e.rb.SetDamping(0.05f, 0.6f);
            e.rb.isKinematic = true;
            e.liftLocal = new Vector3(0, def.size.y + 0.1f, 0);
            if (Buildings.I) Buildings.I.elements.Add(e);
            return e;
        }

        public float HeightAboveGround()
        {
            var p = transform.position + Vector3.up * 0.2f;
            int mask = ~Layers.Mask(Layers.Element, Layers.Hook, Layers.Tool);
            if (Physics.Raycast(p, Vector3.down, out var hit, 60f, mask, QueryTriggerInteraction.Ignore)) return Mathf.Max(0, hit.distance - 0.2f);
            return 0;
        }

        public Vector3[] SlingPoints()
        {
            float hx = def.size.x * 0.35f, hz = def.size.z * 0.35f, y = def.size.y;
            return new[]
            {
                transform.TransformPoint(new Vector3(hx, y, hz)), transform.TransformPoint(new Vector3(-hx, y, hz)),
                transform.TransformPoint(new Vector3(hx, y, -hz)), transform.TransformPoint(new Vector3(-hx, y, -hz)),
            };
        }

        void OnCollisionEnter(Collision c)
        {
            if (state == "placed" || rb.isKinematic) return;
            float v = c.relativeVelocity.magnitude;
            if (v > 3.6f) Shatter(v);
            else if (v > 1.0f) SoundFX.I?.Play("thud", transform.position, Mathf.Clamp(v / 3f, 0.2f, 0.9f), 0.15f);
        }

        /// <summary>Teil zerbricht bei hartem Aufprall in Trümmer.</summary>
        public void Shatter(float impact)
        {
            if (state == "broken") return;
            state = "broken";
            foreach (var b in def.boxes)
            {
                var wp = transform.TransformPoint(b.c);
                var wq = transform.rotation * b.Rot;
                Buildings.I.SpawnDebris(wp, wq, b.size, b.type, rb.Vel() * 0.4f + Random.insideUnitSphere * impact * 0.2f, b.size.x * b.size.y * b.size.z);
            }
            SoundFX.I?.Play("crash", transform.position, 1f);
            SoilFX.I?.Dust(transform.position + Vector3.up, 10, new Color(0.75f, 0.72f, 0.68f), 2f);
            Hud.Toast($"{def.name} ist zerbrochen!", ToastKind.Warn);
            if (Buildings.I) Buildings.I.elements.Remove(this);
            if (site != null) site.OnElementLost(this);
            var crane = GameServices.Crane;
            if (crane && crane.load == this) crane.OnButton("hook");
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (Buildings.I) Buildings.I.elements.Remove(this);
        }
    }

    /// <summary>Verweise auf zentrale Objekte (werden vom Spiel gesetzt).</summary>
    public static class GameServices
    {
        public static TowerCrane Crane;
        public static Rect Yard = new Rect(-44, 6, 22, 18);   // Lagerplatz (x, z, Breite, Tiefe)
        public static int Stat(string key) { return 0; }
    }

    /// <summary>
    /// Baustelle eines Fertighauses: Planum → Schalung/Beton → Aushärten → Montage der Fertigteile per Kran.
    /// </summary>
    public class ConstructionSite : ISoilCatcher
    {
        public Design design;
        public Vector3 pos;
        public float yaw;   // Grad
        public string stage = "planum";
        public float dev = 9f, baseY, poured, need, cure;
        public List<ElementDef> defs;
        public HashSet<int> placed = new HashSet<int>();
        public int tier;
        public Building building;
        public int lostElements;
        readonly Buildings sys;
        readonly GameObject root;
        GameObject formwork, pour, footGhost;
        readonly List<GameObject> ghosts = new List<GameObject>();
        float checkT;
        bool overpourMsg;
        public bool autoFinePlanum = true;

        public ConstructionSite(Buildings sys, string key, Vector3 pos, float yawDeg)
        {
            this.sys = sys;
            design = Design.All[key];
            this.pos = pos; yaw = yawDeg;
            need = design.hw * 2 * design.hd * 2 * design.slabT;
            defs = design.build();
            root = new GameObject("Baustelle " + design.name);
            MakeStakes();
            MakeGhosts();
            SoilFX.Catchers.Add(this);
            sys.sites.Add(this);
        }

        Vector3 W(float lx, float lz) { return pos + Quaternion.Euler(0, yaw, 0) * new Vector3(lx, 0, lz); }
        Vector2 Local(Vector3 w) { var l = Quaternion.Euler(0, -yaw, 0) * (w - pos); return new Vector2(l.x, l.z); }

        void MakeStakes()
        {
            var T = SoilTerrain.I;
            float m = 0.6f, hw = design.hw, hd = design.hd;
            var corners = new[] { W(-hw - m, -hd - m), W(hw + m, -hd - m), W(hw + m, hd + m), W(-hw - m, hd + m) };
            var k = new MeshKit();
            var tops = new List<Vector3>();
            foreach (var c in corners)
            {
                float y = T.Height(c.x, c.z);
                k.Box("formwork", new Vector3(c.x, y + 0.5f, c.z), new Vector3(0.06f, 1.0f, 0.06f));
                k.Box("marker_red", new Vector3(c.x, y + 0.93f, c.z), new Vector3(0.065f, 0.15f, 0.065f));
                tops.Add(new Vector3(c.x, y + 0.8f, c.z));
            }
            k.Build(root.transform, "Pflöcke");
            for (int i = 0; i < 4; i++) new RopeVis(root.transform, 0.006f).Set(tops[i], tops[(i + 1) % 4]);
        }

        void MakeGhosts()
        {
            var fk = new MeshKit();
            fk.Box("ghost", Vector3.zero, new Vector3(design.hw * 2, 0.2f, design.hd * 2));
            footGhost = fk.Build(root.transform, "Umriss", false);
            foreach (var d in defs)
            {
                var k = new MeshKit();
                foreach (var b in d.boxes) k.Box("ghost", b.c, b.size, b.Rot);
                var g = k.Build(root.transform, "Ziel " + d.name, false);
                g.SetActive(false);
                ghosts.Add(g);
            }
            UpdateGhostPos();
        }

        void UpdateGhostPos()
        {
            float y = stage == "planum" ? SoilTerrain.I.StatsRect(pos, design.hw, design.hd, yaw * Mathf.Deg2Rad).mean : baseY;
            footGhost.transform.SetPositionAndRotation(new Vector3(pos.x, y + 0.1f, pos.z), Quaternion.Euler(0, yaw, 0));
            float top = y + design.slabT;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                var wp = W(d.local.x, d.local.z);
                d.target = new Vector3(wp.x, top + d.local.y, wp.z);
                d.targetYaw = yaw;
                d.hasTarget = true;
                ghosts[i].transform.SetPositionAndRotation(d.target, Quaternion.Euler(0, yaw, 0));
            }
        }

        public bool TryCatch(Vector3 p, Vector3 v, float vol, SoilMat mat)
        {
            if (mat != SoilMat.Concrete || stage == "planum") return false;
            if (p.y > baseY + 0.8f || p.y < baseY - 0.4f) return false;
            var l = Local(p);
            if (Mathf.Abs(l.x) > design.hw || Mathf.Abs(l.y) > design.hd) return false;
            if (stage != "beton")
            {
                if (!overpourMsg) { overpourMsg = true; Hud.Toast("Bodenplatte ist schon voll – Beton ablassen stoppen!", ToastKind.Warn); }
                return true;
            }
            poured += vol;
            return true;
        }

        public void Tick(float dt)
        {
            var T = SoilTerrain.I;
            if (stage == "planum")
            {
                checkT -= dt;
                if (checkT > 0) return;
                checkT = 0.5f;
                var st = T.StatsRect(pos, design.hw + 0.3f, design.hd + 0.3f, yaw * Mathf.Deg2Rad);
                dev = Mathf.Max(st.max - st.mean, st.mean - st.min);
                UpdateGhostPos();
                if (dev <= 0.15f)
                {
                    baseY = st.mean;
                    // Feinplanum und Schottertragschicht durch den Bautrupp
                    T.LevelRect(pos, design.hw + 0.3f, design.hd + 0.3f, yaw * Mathf.Deg2Rad, baseY, 3);
                    stage = "beton";
                    MakeFormwork();
                    UpdateGhostPos();
                    footGhost.SetActive(false);
                    Hud.Toast("Planum fertig! Schalung steht – jetzt mit dem Fahrmischer betonieren.", ToastKind.Ok);
                    SoundFX.I?.PlayUI("success");
                }
            }
            else if (stage == "beton")
            {
                float f = Mathf.Min(1f, poured / need);
                pour.transform.localScale = new Vector3(1, Mathf.Max(0.01f, f), 1);
                pour.transform.position = new Vector3(pos.x, baseY + design.slabT * f / 2, pos.z);
                if (f >= 1f)
                {
                    stage = "haerten";
                    cure = 20f;
                    Hud.Toast("Bodenplatte betoniert. Beton härtet aus (Zeitraffer) …", ToastKind.Ok);
                }
            }
            else if (stage == "haerten")
            {
                cure -= dt;
                if (cure <= 0)
                {
                    Object.Destroy(pour); Object.Destroy(formwork);
                    var bp = new Blueprint();
                    bp.Slab(-design.hw, -design.hd, design.hw, design.hd, 0, design.slabT, "concrete", 1.05f);
                    building = sys.CreateFromBlueprint(bp, new Vector3(pos.x, baseY, pos.z), yaw, design.name, "new");
                    stage = "montage";
                    Deliver(0);
                }
            }
            else if (stage == "montage")
            {
                var crane = GameServices.Crane;
                var hooked = crane ? crane.load : null;
                for (int i = 0; i < defs.Count; i++)
                {
                    bool show = defs[i].tier == tier && !placed.Contains(i);
                    if (ghosts[i].activeSelf != show) ghosts[i].SetActive(show);
                    if (show)
                    {
                        bool ok = hooked && hooked.def == defs[i] && CanPlace(hooked);
                        ghosts[i].GetComponent<MeshRenderer>().sharedMaterial = Mats.Get(ok ? "ghost_ok" : "ghost");
                    }
                }
                int remaining = 0;
                for (int i = 0; i < defs.Count; i++) if (defs[i].tier == tier && !placed.Contains(i)) remaining++;
                if (remaining == 0)
                {
                    int next = int.MaxValue;
                    for (int i = 0; i < defs.Count; i++) if (!placed.Contains(i) && defs[i].tier > tier) next = Mathf.Min(next, defs[i].tier);
                    if (next != int.MaxValue) { tier = next; Deliver(tier); }
                    else
                    {
                        stage = "fertig";
                        building.done = true;
                        sys.NotifyBuilt(building);
                        Hud.Toast($"{design.name} fertiggestellt!", ToastKind.Ok);
                        SoundFX.I?.PlayUI("success");
                        Remove();
                    }
                }
            }
        }

        void MakeFormwork()
        {
            var k = new MeshKit();
            float t = 0.04f, h = design.slabT + 0.08f, hw = design.hw, hd = design.hd;
            var rot = Quaternion.Euler(0, yaw, 0);
            System.Action<Vector3, Vector3> add = (lc, size) => k.Box("formwork", new Vector3(pos.x, baseY, pos.z) + rot * lc, size, rot, 0.005f);
            add(new Vector3(0, h / 2, hd + t / 2), new Vector3(hw * 2 + 2 * t, h, t));
            add(new Vector3(0, h / 2, -hd - t / 2), new Vector3(hw * 2 + 2 * t, h, t));
            add(new Vector3(hw + t / 2, h / 2, 0), new Vector3(t, h, hd * 2));
            add(new Vector3(-hw - t / 2, h / 2, 0), new Vector3(t, h, hd * 2));
            // Bewehrung
            for (float x = -hw + 0.15f; x < hw; x += 0.3f) k.Box("dark_metal", new Vector3(pos.x, baseY + 0.08f, pos.z) + rot * new Vector3(x, 0, 0), new Vector3(0.012f, 0.012f, hd * 2 - 0.1f), rot);
            for (float z = -hd + 0.15f; z < hd; z += 0.3f) k.Box("dark_metal", new Vector3(pos.x, baseY + 0.09f, pos.z) + rot * new Vector3(0, 0, z), new Vector3(hw * 2 - 0.1f, 0.012f, 0.012f), rot);
            formwork = k.Build(root.transform, "Schalung");
            var pk = new MeshKit();
            pk.Box("fresh_concrete", Vector3.zero, new Vector3(hw * 2, design.slabT, hd * 2));
            pour = pk.Build(root.transform, "Frischbeton");
            pour.transform.SetPositionAndRotation(new Vector3(pos.x, baseY, pos.z), rot);
            pour.transform.localScale = new Vector3(1, 0.01f, 1);
        }

        /// <summary>Anlieferung der Fertigteile auf den Lagerplatz.</summary>
        void Deliver(int t)
        {
            var yard = GameServices.Yard;
            float cx = yard.xMin + 0.4f, cz = yard.yMin + 0.6f, rowH = 0;
            int n = 0;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d.tier != t || placed.Contains(i)) continue;
                // im Lagerplatz in Zielausrichtung ablegen (gedreht um Baustellen-Yaw)
                var rot = Quaternion.Euler(0, yaw, 0);
                var ext = rot * d.size;
                float sx = Mathf.Abs(ext.x), sz = Mathf.Abs(ext.z);
                if (cx + sx > yard.xMax) { cx = yard.xMin + 0.4f; cz += rowH + 1.0f; rowH = 0; }
                var p = new Vector3(cx + sx / 2, 0, cz + sz / 2);
                p.y = SoilTerrain.I.Height(p.x, p.z) + 0.02f;
                var el = Element.Create(d, this, i, p, yaw);
                if (d.boxes.Exists(b => Mathf.Abs(b.pitch) > 0.01f))
                {
                    // Dachelement auf Transportbock
                    var tk = new MeshKit();
                    tk.Box("timber", p + new Vector3(0, 0.15f, 0), new Vector3(sx * 0.9f, 0.3f, 0.3f), 0.02f);
                    tk.Build(root.transform, "Transportbock");
                    el.transform.position += Vector3.up * 0.3f;
                }
                cx += sx + 1.0f;
                rowH = Mathf.Max(rowH, sz);
                n++;
            }
            if (n > 0) Hud.Toast($"Lkw hat {n} Fertigteile auf den Lagerplatz geliefert. Mit dem Kran montieren!", ToastKind.Ok);
        }

        public bool CanPlace(Element el)
        {
            var d = el.def;
            if (!d.hasTarget || d.tier != tier || placed.Contains(el.index) || stage != "montage") return false;
            var p = el.transform.position;
            float dx = p.x - d.target.x, dz = p.z - d.target.z;
            if (dx * dx + dz * dz > 0.7f * 0.7f) return false;
            float dy = p.y - d.target.y;
            if (dy < -0.4f || dy > 0.5f) return false;
            if (Mathf.Abs(Mathf.DeltaAngle(el.transform.eulerAngles.y, d.targetYaw)) > 12f) return false;
            return true;
        }

        public void Place(Element el)
        {
            var d = el.def;
            var rot = Quaternion.Euler(0, d.targetYaw, 0);
            var list = new List<Piece>();
            foreach (var b in d.boxes) list.Add(sys.AddPiece(building, b.type, d.target + rot * b.c, b.size, rot * b.Rot));
            sys.Finalize(building, list, baseY);
            placed.Add(el.index);
            el.state = "placed";
            sys.elements.Remove(el);
            Object.Destroy(el.gameObject);
            SoundFX.I?.Play("thud", d.target, 0.7f);
            Hud.Toast($"{d.name} montiert ({placed.Count}/{defs.Count})", ToastKind.Ok);
        }

        public void OnElementLost(Element el)
        {
            lostElements++;
            if (stage != "montage") return;
            var yard = GameServices.Yard;
            var p = new Vector3(yard.center.x, 0, yard.yMax - el.def.size.z - 0.5f);
            p.y = SoilTerrain.I.Height(p.x, p.z) + 0.02f;
            Element.Create(el.def, this, el.index, p, yaw);
            Hud.Toast($"Ersatz für „{el.def.name}“ wurde auf den Lagerplatz geliefert.", ToastKind.Warn);
        }

        public void Remove()
        {
            SoilFX.Catchers.Remove(this);
            sys.sites.Remove(this);
            if (root) Object.Destroy(root);
        }

        public (string title, string detail, float prog) Status()
        {
            switch (stage)
            {
                case "planum": return ($"{design.name}: Baugrund planieren", $"Höhenabweichung ±{U.Fmt(dev, 2)} m (Ziel ≤ 0,15 m)", Mathf.Clamp01(1f - (dev - 0.15f) / 1.2f));
                case "beton": return ($"{design.name}: Bodenplatte betonieren", $"{U.Fmt(poured)} / {U.Fmt(need)} m³ Beton", poured / need);
                case "haerten": return ($"{design.name}: Beton härtet aus", $"noch {Mathf.CeilToInt(cure)} s (Zeitraffer)", 1f - cure / 20f);
                case "montage":
                    {
                        string next = "";
                        for (int i = 0; i < defs.Count; i++) if (defs[i].tier == tier && !placed.Contains(i)) { next = $"z. B. {defs[i].name} ({U.Fmt(defs[i].massT)} t)"; break; }
                        return ($"{design.name}: Montage {placed.Count}/{defs.Count}", next, placed.Count / (float)defs.Count);
                    }
                default: return ($"{design.name}: fertig", "", 1);
            }
        }
    }
}
