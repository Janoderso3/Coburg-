using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Lageplan der Baustelle und Aufbau von Gelände, Kulisse und Objekten.</summary>
    public static class Layout
    {
        public static readonly Vector2 Crane = new Vector2(-12, -6);
        public static readonly Vector2 Plot = new Vector2(-28, -26);
        public static readonly Vector2 House = new Vector2(28, -26);
        public static readonly Vector2 Garage = new Vector2(46, -6);
        public static readonly Vector2 Heap = new Vector2(24, 26);
        public const float HeapR = 8.5f, HeapH = 4.2f;
        public static readonly Vector2 Hill = new Vector2(-46, -46);
        public static readonly Rect Dep = new Rect(42, 44, 17, 16);
        public static readonly Vector2 Plant = new Vector2(-50, 46);
        public static readonly Rect Yard = new Rect(-31, 3, 17, 13);
        public static readonly Rect DigZone = new Rect(4, 4, 6, 4);       // Mission 1
        public static readonly Rect PitZone = new Rect(-6, 22, 8, 8);     // Mission Baugrube
        public const float RoadZ = 39f;
    }

    public class World : MonoBehaviour
    {
        public SoilTerrain soil;
        public SoilFX fx;
        public Buildings buildings;
        public TowerCrane crane;
        public readonly List<VehicleBase> vehicles = new List<VehicleBase>();
        public Excavator excavator; public WheelLoader loader; public Dozer dozer; public DumpTruck truck; public MixerTruck mixer; public WreckingCrane wrecker;
        public Building oldHouse, oldGarage;
        RectInt depRect;
        GameObject scenery;
        public static World I;

        static float Box(float x, float z, float cx, float cz, float hx, float hz)
        {
            float dx = Mathf.Max(0, Mathf.Abs(x - cx) - hx), dz = Mathf.Max(0, Mathf.Abs(z - cz) - hz);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float BaseHeight(float x, float z)
        {
            float h = 0.7f * U.Fbm(x / 40f + 4.1f, z / 40f - 2.3f, 3) + 0.18f * U.Fbm(x / 12f + 10, z / 12f + 3, 2);
            h += 5f * Mathf.Exp(-((x - Layout.Hill.x) * (x - Layout.Hill.x) + (z - Layout.Hill.y) * (z - Layout.Hill.y)) / 240f);
            float dp = Mathf.Sqrt((x - Layout.Plot.x) * (x - Layout.Plot.x) + (z - Layout.Plot.y) * (z - Layout.Plot.y));
            h += (0.6f * U.Fbm(x / 5f + 7, z / 5f - 1, 2) + 0.3f) * (1 - U.Smooth(7, 14, dp));
            return h;
        }

        public static SoilGen Gen(float x, float z)
        {
            float h = BaseHeight(x, z);
            byte surf = 0, zone = 0; float fill = 0;
            System.Func<float, float, float, float, float, float, float> pad = (cx, cz, hx, hz, blend, y) =>
            {
                float d = Box(x, z, cx, cz, hx, hz);
                float w = 1 - U.Smooth(0, blend, d);
                h = Mathf.Lerp(h, y, w);
                return d;
            };
            pad(Layout.House.x, Layout.House.y, 6.5f, 5.8f, 4f, BaseHeight(Layout.House.x, Layout.House.y));
            pad(Layout.Garage.x, Layout.Garage.y, 3f, 4.5f, 3f, BaseHeight(Layout.Garage.x, Layout.Garage.y));
            pad(Layout.Crane.x, Layout.Crane.y, 3.5f, 3.5f, 3f, BaseHeight(Layout.Crane.x, Layout.Crane.y));
            var Y = Layout.Yard;
            if (pad(Y.center.x, Y.center.y, Y.width / 2, Y.height / 2, 4f, BaseHeight(Y.center.x, Y.center.y)) < 0.01f) surf = 3;
            float roadD = Mathf.Abs(z - Layout.RoadZ);
            if (roadD < 4f) { h = Mathf.Lerp(h, 0.05f, 1 - U.Smooth(2.5f, 4f, roadD)); if (roadD < 3f) surf = 3; }
            if (pad(Layout.Plant.x, Layout.Plant.y + 2, 7, 8, 4, 0.1f) < 0.01f) surf = 3;
            var D = Layout.Dep;
            float dd = Box(x, z, D.center.x, D.center.y, D.width / 2, D.height / 2);
            float din = Mathf.Min(Mathf.Min(x - D.xMin, D.xMax - x), Mathf.Min(z - D.yMin, D.yMax - z));
            if (dd < 3) h = Mathf.Lerp(h, 0.1f, 1 - U.Smooth(0, 3, dd));
            if (din > 0) { h -= 1.6f * U.Smooth(0, 3.5f, din); if (din > 1.2f) zone = 1; surf = 3; }
            float e = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            h = Mathf.Lerp(h, 0, U.Smooth(58f, 63f, e));
            float dh = Mathf.Sqrt((x - Layout.Heap.x) * (x - Layout.Heap.x) + (z - Layout.Heap.y) * (z - Layout.Heap.y));
            if (dh < Layout.HeapR)
            {
                float t = 1 - dh / Layout.HeapR;
                fill = Layout.HeapH * Mathf.Pow(t, 1.1f) * (0.92f + 0.08f * U.Fbm(x, z, 1));
                h += fill;
            }
            return new SoilGen { h = h, fill = fill, fillMat = fill > 0 ? SoilMat.Soil : SoilMat.None, surf = surf, zone = zone };
        }

        // ---------------- Aufbau ----------------
        public void Build(bool withHouse, bool withGarage, string[] vehicleSet)
        {
            I = this;
            var sgo = new GameObject("Boden");
            sgo.transform.SetParent(transform, false);
            soil = sgo.AddComponent<SoilTerrain>();
            soil.Init(Gen);
            var fgo = new GameObject("Effekte");
            fgo.transform.SetParent(transform, false);
            fx = fgo.AddComponent<SoilFX>();
            soil.OnClod += (p, vol, mat) => fx.SpawnClod(p, vol, mat, Vector3.down * 0.5f);
            var bgo = new GameObject("Gebäude");
            bgo.transform.SetParent(transform, false);
            buildings = bgo.AddComponent<Buildings>();
            depRect = soil.ToGridRect(Layout.Dep.xMin, Layout.Dep.yMin, Layout.Dep.xMax, Layout.Dep.yMax);
            GameServices.Yard = Layout.Yard;
            MixerTruck.PlantPos = new Vector3(Layout.Plant.x, 0, Layout.Plant.y);
            BuildScenery();
            if (withHouse) oldHouse = PlaceOld(Blueprint.OldHouse(), Layout.House, 0, "Altes Fachwerkhaus");
            if (withGarage) oldGarage = PlaceOld(Blueprint.OldGarage(), Layout.Garage, 0, "Alte Ziegelgarage");
            crane = TowerCrane.Create(new Vector3(Layout.Crane.x, soil.Height(Layout.Crane.x, Layout.Crane.y), Layout.Crane.y));
            crane.transform.SetParent(transform, true);
            GameServices.Crane = crane;
            var set = new HashSet<string>(vehicleSet);
            if (set.Contains("Bagger")) Add(excavator = Excavator.Create(Spawn(8, 16), 40));
            if (set.Contains("Radlader")) Add(loader = WheelLoader.Create(Spawn(16, 0), 180));
            if (set.Contains("Raupe")) Add(dozer = Dozer.Create(Spawn(-18, -12), -100));
            if (set.Contains("Kipper")) Add(truck = DumpTruck.Create(Spawn(4, 31), -130));
            if (set.Contains("Mischer")) Add(mixer = MixerTruck.Create(Spawn(-36, Layout.RoadZ), 90));
            if (set.Contains("Kran")) vehicles.Add(crane);
            if (set.Contains("Birne")) Add(wrecker = WreckingCrane.Create(Spawn(8, -26), 90));
        }

        Vector3 Spawn(float x, float z) { return new Vector3(x, soil.Height(x, z) + 0.6f, z); }

        void Add(VehicleBase v)
        {
            v.transform.SetParent(transform, true);
            vehicles.Add(v);
        }

        Building PlaceOld(Blueprint bp, Vector2 p, float yaw, string name)
        {
            var st = soil.StatsRect(new Vector3(p.x, 0, p.y), 5.5f, 5f, yaw * Mathf.Deg2Rad);
            soil.LevelRect(new Vector3(p.x, 0, p.y), 5.2f, 4.5f, yaw * Mathf.Deg2Rad, st.mean, 3);
            return buildings.CreateFromBlueprint(bp, new Vector3(p.x, st.mean, p.y), yaw, name, "old");
        }

        public Building PlaceOldAt(bool house, Vector3 pos, float yaw)
        {
            var st = soil.StatsRect(pos, 5.5f, 5f, yaw * Mathf.Deg2Rad);
            soil.LevelRect(pos, house ? 5.2f : 2.3f, house ? 4.5f : 3.6f, yaw * Mathf.Deg2Rad, st.mean, 3);
            return buildings.CreateFromBlueprint(house ? Blueprint.OldHouse() : Blueprint.OldGarage(), new Vector3(pos.x, st.mean, pos.z), yaw, house ? "Altes Fachwerkhaus" : "Alte Ziegelgarage", "old");
        }

        void Update()
        {
            if (soil) soil.ProcessZone(depRect, Time.deltaTime);
        }

        public float Disposed(SoilMat m) { return soil ? soil.disposed[(int)m] : 0; }

        // ---------------- Kulisse ----------------
        void BuildScenery()
        {
            scenery = new GameObject("Kulisse");
            scenery.transform.SetParent(transform, false);
            var k = new MeshKit();
            // Außengelände (Ring um das Baufeld, etwas tiefer, damit keine Kanten flimmern)
            var ground = new GameObject("Umland");
            ground.transform.SetParent(scenery.transform, false);
            var ok = new MeshKit();
            float R = 900f, inner = 63.5f;
            ok.Poly("outer_grass", new[] { new Vector3(-R, -0.02f, -R), new Vector3(R, -0.02f, -R), new Vector3(R, -0.02f, -inner), new Vector3(-R, -0.02f, -inner) }, new Vector3(0, -10, 0));
            ok.Poly("outer_grass", new[] { new Vector3(-R, -0.02f, inner), new Vector3(R, -0.02f, inner), new Vector3(R, -0.02f, R), new Vector3(-R, -0.02f, R) }, new Vector3(0, -10, 0));
            ok.Poly("outer_grass", new[] { new Vector3(-R, -0.02f, -inner), new Vector3(-inner, -0.02f, -inner), new Vector3(-inner, -0.02f, inner), new Vector3(-R, -0.02f, inner) }, new Vector3(0, -10, 0));
            ok.Poly("outer_grass", new[] { new Vector3(inner, -0.02f, -inner), new Vector3(R, -0.02f, -inner), new Vector3(R, -0.02f, inner), new Vector3(inner, -0.02f, inner) }, new Vector3(0, -10, 0));
            ok.Build(ground.transform, "Wiese", false).GetComponent<MeshRenderer>().receiveShadows = true;
            // Straße
            k.Box("asphalt", new Vector3(76, 0.0f, 0), new Vector3(10, 0.06f, 1200));
            k.Box("asphalt", new Vector3(69.5f, 0.0f, Layout.RoadZ), new Vector3(13, 0.05f, 7));
            for (float z = -600; z < 600; z += 9) k.Box("paint_white", new Vector3(76, 0.04f, z), new Vector3(0.15f, 0.01f, 4));
            k.Build(scenery.transform, "Straße", false);
            // Bauzaun
            float E = 63.4f;
            for (int s = 0; s < 4; s++)
                for (float t = -E; t < E - 0.1f; t += 3.5f)
                {
                    float c = t + 1.75f;
                    Vector3 p; bool alongX = s < 2;
                    if (s == 0) p = new Vector3(c, 0, -E); else if (s == 1) p = new Vector3(c, 0, E); else if (s == 2) p = new Vector3(-E, 0, c); else p = new Vector3(E, 0, c);
                    if (s == 3 && Mathf.Abs(c - Layout.RoadZ) < 4.5f) continue;
                    var rot = alongX ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
                    k.Box("concrete", p + new Vector3(0, 0.08f, 0) + rot * new Vector3(-1.72f, 0, 0), new Vector3(0.7f, 0.16f, 0.22f), rot, 0.02f);
                    k.Box("fence", p + rot * new Vector3(-1.72f, 1.1f, 0), new Vector3(0.05f, 2.0f, 0.05f), rot);
                    k.Box("fence", p + rot * new Vector3(0, 2.07f, 0), new Vector3(3.45f, 0.05f, 0.05f), rot);
                    k.Box("fence", p + rot * new Vector3(0, 0.25f, 0), new Vector3(3.45f, 0.05f, 0.05f), rot);
                    for (float u = -1.6f; u < 1.65f; u += 0.25f) k.Box("fence", p + rot * new Vector3(u, 1.16f, 0), new Vector3(0.012f, 1.8f, 0.012f), rot);
                    for (float v = 0.45f; v < 2.0f; v += 0.3f) k.Box("fence", p + rot * new Vector3(0, v, 0), new Vector3(3.4f, 0.012f, 0.012f), rot);
                }
            k.Build(scenery.transform, "Bauzaun");
            // Bäume
            var r = new Rng(99);
            var tk = new MeshKit();
            for (int i = 0; i < 260; i++)
            {
                float a = r.Next() * Mathf.PI * 2, d = 75 + r.Next() * 300;
                float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
                if (Mathf.Abs(x - 76) < 10) continue;
                float s = 0.8f + r.Next() * 0.9f;
                tk.Cyl("bark", new Vector3(x, 2f * s, z), 0.18f * s, 0.3f * s, 4f * s, 7, Quaternion.identity, false);
                int blobs = 3 + (int)(r.Next() * 3);
                for (int b = 0; b < blobs; b++)
                {
                    var c = new Vector3(x + (r.Next() - 0.5f) * 2.2f * s, (5f + r.Next() * 2.5f) * s, z + (r.Next() - 0.5f) * 2.2f * s);
                    tk.Sphere("leaves", c, (1.6f + r.Next()) * s, 8);
                }
                if (tk.VertexCount > 50000) tk.Build(scenery.transform, "Bäume");
            }
            tk.Build(scenery.transform, "Bäume");
            // Nachbarhäuser
            var hk = new MeshKit();
            var houses = new[] { new Vector2(-110, -20), new Vector2(-116, 44), new Vector2(-100, 100), new Vector2(-24, 116), new Vector2(36, 120), new Vector2(120, -96), new Vector2(124, -30), new Vector2(128, 48), new Vector2(116, 116), new Vector2(-80, -116), new Vector2(-10, -120), new Vector2(56, -124), new Vector2(160, 20), new Vector2(170, -60), new Vector2(-170, 10) };
            for (int i = 0; i < houses.Length; i++)
            {
                var hp = houses[i];
                float w = 9 + r.Next() * 4, dd = 8 + r.Next() * 3, hh = 5.5f + r.Next() * 3;
                var rot = Quaternion.Euler(0, r.Next() < 0.5f ? 0 : 90, 0);
                string wall = i % 3 == 0 ? "plaster_white" : i % 3 == 1 ? "plaster" : "brick";
                hk.Box(wall, new Vector3(hp.x, hh / 2, hp.y), new Vector3(w, hh, dd), rot, 0.05f);
                // Satteldach als Prisma
                var p1 = new Vector3(-w / 2 - 0.4f, hh, -dd / 2 - 0.5f); var p2 = new Vector3(w / 2 + 0.4f, hh, -dd / 2 - 0.5f);
                var p3 = new Vector3(w / 2 + 0.4f, hh + dd * 0.4f, 0); var p4 = new Vector3(-w / 2 - 0.4f, hh + dd * 0.4f, 0);
                var p5 = new Vector3(w / 2 + 0.4f, hh, dd / 2 + 0.5f); var p6 = new Vector3(-w / 2 - 0.4f, hh, dd / 2 + 0.5f);
                var o = new Vector3(hp.x, 0, hp.y);
                var inside = o + new Vector3(0, hh + 0.5f, 0);
                hk.Poly("roof_tile", new[] { o + rot * p1, o + rot * p2, o + rot * p3, o + rot * p4 }, inside);
                hk.Poly("roof_tile", new[] { o + rot * p4, o + rot * p3, o + rot * p5, o + rot * p6 }, inside);
                hk.Poly(wall, new[] { o + rot * new Vector3(-w / 2, hh, -dd / 2), o + rot * new Vector3(-w / 2, hh, dd / 2), o + rot * new Vector3(-w / 2, hh + dd * 0.4f - 0.2f, 0) }, inside);
                hk.Poly(wall, new[] { o + rot * new Vector3(w / 2, hh, -dd / 2), o + rot * new Vector3(w / 2, hh, dd / 2), o + rot * new Vector3(w / 2, hh + dd * 0.4f - 0.2f, 0) }, inside);
                for (int f = 0; f < 4; f++) hk.Box("window_glass", o + rot * new Vector3(-w / 2 + 1.5f + f * (w - 3) / 3, hh * 0.55f, -dd / 2 - 0.02f), new Vector3(1.0f, 1.2f, 0.05f), rot);
            }
            // Ferne Burg auf einem Hügel
            hk.Sphere("outer_grass", new Vector3(-420, -60, -600), 220, 18);
            hk.Box("plaster_white", new Vector3(-420, 160, -600), new Vector3(80, 18, 40), 0.5f);
            hk.Box("plaster_white", new Vector3(-450, 170, -600), new Vector3(14, 36, 14), 0.3f);
            hk.Box("plaster_white", new Vector3(-385, 166, -592), new Vector3(18, 26, 18), 0.3f);
            hk.Box("roof_tile", new Vector3(-412, 172, -600), new Vector3(26, 7, 40), 0.3f);
            hk.Build(scenery.transform, "Nachbarschaft");
            BuildSiteProps();
        }

        void BuildSiteProps()
        {
            var k = new MeshKit();
            var T = soil;
            // Baucontainer (zweistöckig) + Toilette
            float cy = T.Height(52, 16);
            k.Box("paint_white", new Vector3(52, cy + 1.3f, 16), new Vector3(2.45f, 2.6f, 6f), 0.06f);
            k.Box("container_blue", new Vector3(52, cy + 3.92f, 16), new Vector3(2.45f, 2.6f, 6f), 0.06f);
            k.Box("glass", new Vector3(50.76f, cy + 1.6f, 15), new Vector3(0.05f, 0.9f, 1.4f));
            k.Box("glass", new Vector3(50.76f, cy + 4.2f, 15), new Vector3(0.05f, 0.9f, 1.4f));
            k.Box("container_blue", new Vector3(52, T.Height(52, 22) + 1.15f, 22), new Vector3(1.15f, 2.3f, 1.15f), 0.05f);
            AddStatic(new Vector3(52, cy + 2.6f, 16), new Vector3(2.5f, 5.2f, 6f));
            AddStatic(new Vector3(52, cy + 1.15f, 22), new Vector3(1.2f, 2.3f, 1.2f));
            // Paletten mit Steinen
            for (int i = 0; i < 4; i++)
            {
                float x = -12 + i * 1.6f, z = 30; float y = T.Height(x, z);
                k.Box("timber", new Vector3(x, y + 0.07f, z), new Vector3(1.2f, 0.14f, 0.8f), 0.01f);
                k.Box("brick", new Vector3(x, y + 0.55f, z), new Vector3(1.1f, 0.8f, 0.75f), 0.02f);
                AddStatic(new Vector3(x, y + 0.45f, z), new Vector3(1.2f, 0.9f, 0.8f));
            }
            // Betonwerk: Silo auf Stützen, Lkw fährt darunter
            var P = Layout.Plant; float py = T.Height(P.x, P.y);
            foreach (var (lx, lz) in new[] { (-2.6f, -2.2f), (2.6f, -2.2f), (-2.6f, 2.2f), (2.6f, 2.2f) })
            {
                k.Box("steel", new Vector3(P.x + lx, py + 2.7f, P.y + lz), new Vector3(0.35f, 5.4f, 0.35f));
                AddStatic(new Vector3(P.x + lx, py + 2.7f, P.y + lz), new Vector3(0.4f, 5.4f, 0.4f));
            }
            k.Box("steel", new Vector3(P.x, py + 5.5f, P.y), new Vector3(5.6f, 0.3f, 4.8f), 0.03f);
            k.Cyl("silo", new Vector3(P.x, py + 9.6f, P.y), 2.1f, 2.1f, 7.8f, 28, Quaternion.identity);
            k.Cyl("silo", new Vector3(P.x, py + 4.8f, P.y), 0.4f, 2.1f, 1.8f, 28, Quaternion.identity);
            k.Cyl("dark_metal", new Vector3(P.x, py + 3.7f, P.y), 0.3f, 0.3f, 1.0f, 12, Quaternion.identity);
            k.Box("paint_orange", new Vector3(P.x, py + 6.0f, P.y + 2.45f), new Vector3(2.6f, 0.5f, 0.2f), 0.02f);
            k.Box("concrete", new Vector3(P.x - 6, py + 3.4f, P.y + 4), new Vector3(3.4f, 6.8f, 3.4f), 0.05f);
            AddStatic(new Vector3(P.x - 6, py + 3.4f, P.y + 4), new Vector3(3.4f, 6.8f, 3.4f));
            // Deponie: Legio-Betonblöcke
            var D = Layout.Dep;
            for (float x = D.xMin + 0.8f; x < D.xMax; x += 1.65f) { float y = T.Height(x, D.yMax + 1); k.Box("concrete", new Vector3(x, y + 0.4f, D.yMax + 1), new Vector3(1.6f, 0.8f, 0.8f), 0.04f); AddStatic(new Vector3(x, y + 0.4f, D.yMax + 1), new Vector3(1.6f, 0.8f, 0.8f)); }
            for (float z = D.yMin + 1; z < D.yMax; z += 1.65f) { float y = T.Height(D.xMax + 1, z); k.Box("concrete", new Vector3(D.xMax + 1, y + 0.4f, z), new Vector3(0.8f, 0.8f, 1.6f), 0.04f); AddStatic(new Vector3(D.xMax + 1, y + 0.4f, z), new Vector3(0.8f, 0.8f, 1.6f)); }
            // Lagerplatz-Begrenzung
            var Y = Layout.Yard;
            foreach (var c in new[] { new Vector2(Y.xMin, Y.yMin), new Vector2(Y.xMax, Y.yMin), new Vector2(Y.xMin, Y.yMax), new Vector2(Y.xMax, Y.yMax) })
            {
                float y = T.Height(c.x, c.y);
                k.Cyl("paint_red", new Vector3(c.x, y + 0.5f, c.y), 0.08f, 0.08f, 1f, 8, Quaternion.identity);
                k.Cyl("paint_white", new Vector3(c.x, y + 0.75f, c.y), 0.081f, 0.081f, 0.2f, 8, Quaternion.identity);
            }
            k.Build(scenery.transform, "Baustelleneinrichtung");
        }

        void AddStatic(Vector3 c, Vector3 size)
        {
            var go = new GameObject("Hindernis");
            go.layer = Layers.Building;
            go.transform.SetParent(scenery.transform, false);
            go.transform.position = c;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>Markierte Fläche (z. B. Aushub-Zone) als halbtransparenter Rahmen.</summary>
        public GameObject Marker(Rect r, string name)
        {
            var k = new MeshKit();
            float y0 = soil.StatsRect(new Vector3(r.center.x, 0, r.center.y), r.width / 2, r.height / 2, 0).max;
            foreach (var (a, b) in new[] { (new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin)), (new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax)), (new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax)), (new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin)) })
            {
                var pa = new Vector3(a.x, y0, a.y); var pb = new Vector3(b.x, y0, b.y);
                k.Box("zone", (pa + pb) * 0.5f + Vector3.up * 0.6f, new Vector3(Mathf.Abs(b.x - a.x) + 0.1f, 1.2f, Mathf.Abs(b.y - a.y) + 0.1f));
            }
            foreach (var c in new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax) })
                k.Cyl("marker_red", new Vector3(c.x, soil.Height(c.x, c.y) + 0.6f, c.y), 0.05f, 0.05f, 1.2f, 8, Quaternion.identity);
            var go = k.Build(transform, name, false);
            return go;
        }

        public void Teardown()
        {
            if (buildings) buildings.ClearAll();
            foreach (var v in vehicles)
            {
                if (!v) continue;
                if (v is WheelLoader wl && wl.front) Destroy(wl.front.gameObject);
                if (v is TowerCrane tc) { if (tc.hook) Destroy(tc.hook.gameObject); if (tc.trolley) Destroy(tc.trolley.gameObject); }
                if (v is WreckingCrane wc && wc.ball) Destroy(wc.ball.gameObject);
            }
            if (crane) { if (crane.hook) Destroy(crane.hook.gameObject); if (crane.trolley) Destroy(crane.trolley.gameObject); }
            SoilFX.Catchers.Clear();
            Destroy(gameObject);
        }
    }
}
