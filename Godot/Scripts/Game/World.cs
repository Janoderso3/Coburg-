using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>Lageplan der Baustelle.</summary>
public static class Layout
{
    public static readonly Vector2 Crane = new(-12, -6);
    public static readonly Vector2 Plot = new(-28, -26);
    public static readonly Vector2 House = new(28, -26);
    public static readonly Vector2 Garage = new(46, -6);
    public static readonly Vector2 Heap = new(24, 26);
    public const float HeapR = 8.5f, HeapH = 4.2f;
    public static readonly Vector2 Hill = new(-46, -46);
    public static readonly Rect2 Dep = new(42, 44, 17, 16);
    public static readonly Vector2 Plant = new(-50, 46);
    public static readonly Rect2 Yard = new(-31, 3, 17, 13);
    public static readonly Rect2 DigZone = new(4, 4, 6, 4);       // Mission 1
    public static readonly Rect2 PitZone = new(-6, 22, 8, 8);     // Mission Baugrube
    public const float RoadZ = 39f;
}

/// <summary>Gelände, Kulisse, Baustelleneinrichtung und Maschinen.</summary>
public partial class World : Node3D
{
    public SoilTerrain soil;
    public SoilFX fx;
    public Buildings buildings;
    public TowerCrane crane;
    public readonly List<VehicleBase> vehicles = new();
    public Excavator excavator; public WheelLoader loader; public Dozer dozer; public DumpTruck truck; public MixerTruck mixer; public WreckingCrane wrecker;
    public Building oldHouse, oldGarage;
    RectI depRect;
    Node3D scenery;
    StaticBody3D obstacles;
    public static World I;

    static float Box(float x, float z, float cx, float cz, float hx, float hz)
    {
        float dx = MathF.Max(0, MathF.Abs(x - cx) - hx), dz = MathF.Max(0, MathF.Abs(z - cz) - hz);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    static float BaseHeight(float x, float z)
    {
        float h = 0.7f * U.Fbm(x / 40f + 4.1f, z / 40f - 2.3f, 3) + 0.18f * U.Fbm(x / 12f + 10, z / 12f + 3, 2);
        h += 5f * MathF.Exp(-((x - Layout.Hill.X) * (x - Layout.Hill.X) + (z - Layout.Hill.Y) * (z - Layout.Hill.Y)) / 240f);
        float dp = MathF.Sqrt((x - Layout.Plot.X) * (x - Layout.Plot.X) + (z - Layout.Plot.Y) * (z - Layout.Plot.Y));
        h += (0.6f * U.Fbm(x / 5f + 7, z / 5f - 1, 2) + 0.3f) * (1 - U.Smooth(7, 14, dp));
        return h;
    }

    public static SoilGen Gen(float x, float z)
    {
        float h = BaseHeight(x, z);
        byte surf = 0, zone = 0; float fill = 0;
        float Pad(float cx, float cz, float hx, float hz, float blend, float y)
        {
            float d = Box(x, z, cx, cz, hx, hz);
            float w = 1 - U.Smooth(0, blend, d);
            h = Mathf.Lerp(h, y, w);
            return d;
        }
        Pad(Layout.House.X, Layout.House.Y, 6.5f, 5.8f, 4f, BaseHeight(Layout.House.X, Layout.House.Y));
        Pad(Layout.Garage.X, Layout.Garage.Y, 3f, 4.5f, 3f, BaseHeight(Layout.Garage.X, Layout.Garage.Y));
        Pad(Layout.Crane.X, Layout.Crane.Y, 3.5f, 3.5f, 3f, BaseHeight(Layout.Crane.X, Layout.Crane.Y));
        var Y = Layout.Yard; var yc = Y.GetCenter();
        if (Pad(yc.X, yc.Y, Y.Size.X / 2, Y.Size.Y / 2, 4f, BaseHeight(yc.X, yc.Y)) < 0.01f) surf = 3;
        float roadD = MathF.Abs(z - Layout.RoadZ);
        if (roadD < 4f && x > -58) { h = Mathf.Lerp(h, 0.05f, 1 - U.Smooth(2.5f, 4f, roadD)); if (roadD < 3f) surf = 3; }
        if (Pad(Layout.Plant.X, Layout.Plant.Y + 2, 7, 8, 4, 0.1f) < 0.01f) surf = 3;
        var D = Layout.Dep; var dc = D.GetCenter();
        float dd = Box(x, z, dc.X, dc.Y, D.Size.X / 2, D.Size.Y / 2);
        float din = MathF.Min(MathF.Min(x - D.Position.X, D.End.X - x), MathF.Min(z - D.Position.Y, D.End.Y - z));
        if (dd < 3) h = Mathf.Lerp(h, 0.1f, 1 - U.Smooth(0, 3, dd));
        if (din > 0) { h -= 1.6f * U.Smooth(0, 3.5f, din); if (din > 1.2f) zone = 1; surf = 3; }
        float e = MathF.Max(MathF.Abs(x), MathF.Abs(z));
        h = Mathf.Lerp(h, 0, U.Smooth(58f, 63f, e));
        float dh = MathF.Sqrt((x - Layout.Heap.X) * (x - Layout.Heap.X) + (z - Layout.Heap.Y) * (z - Layout.Heap.Y));
        if (dh < Layout.HeapR)
        {
            float t = 1 - dh / Layout.HeapR;
            fill = Layout.HeapH * MathF.Pow(t, 1.1f) * (0.92f + 0.08f * U.Fbm(x, z, 1));
            h += fill;
        }
        return new SoilGen { h = h, fill = fill, fillMat = fill > 0 ? SoilMat.Soil : SoilMat.None, surf = surf, zone = zone };
    }

    // ---------------- Aufbau ----------------
    public void Build(bool withHouse, bool withGarage, string[] vehicleSet)
    {
        I = this;
        Name = "Welt";
        SoilFX.Catchers.Clear();
        soil = new SoilTerrain { Name = "Boden" };
        AddChild(soil);
        soil.Init(Gen);
        fx = new SoilFX { Name = "Effekte" };
        AddChild(fx);
        soil.OnClod += (p, vol, mat) => fx.SpawnClod(p, vol, mat, Vector3.Down * 0.5f);
        buildings = new Buildings { Name = "Gebaeude" };
        AddChild(buildings);
        depRect = soil.ToGridRect(Layout.Dep.Position.X, Layout.Dep.Position.Y, Layout.Dep.End.X, Layout.Dep.End.Y);
        GameServices.Yard = Layout.Yard;
        MixerTruck.PlantPos = new Vector3(Layout.Plant.X, 0, Layout.Plant.Y);
        BuildScenery();
        if (withHouse) oldHouse = PlaceOldAt(true, new Vector3(Layout.House.X, 0, Layout.House.Y), 0);
        if (withGarage) oldGarage = PlaceOldAt(false, new Vector3(Layout.Garage.X, 0, Layout.Garage.Y), 0);
        crane = TowerCrane.Create(this, new Vector3(Layout.Crane.X, soil.Height(Layout.Crane.X, Layout.Crane.Y), Layout.Crane.Y));
        GameServices.Crane = crane;
        var set = new HashSet<string>(vehicleSet);
        float D2R = MathF.PI / 180f;
        // Gierwinkel: Drehung um +Y, +Z wird auf (sin, 0, cos) abgebildet
        if (set.Contains("Bagger")) Add(excavator = Excavator.Create(this, Spawn(8, 16), 40 * D2R));
        if (set.Contains("Radlader")) Add(loader = WheelLoader.Create(this, Spawn(16, 0), 180 * D2R));
        if (set.Contains("Raupe")) Add(dozer = Dozer.Create(this, Spawn(-18, -12), -100 * D2R));
        if (set.Contains("Kipper")) Add(truck = DumpTruck.Create(this, Spawn(4, 31), -130 * D2R));
        if (set.Contains("Mischer")) Add(mixer = MixerTruck.Create(this, Spawn(-36, Layout.RoadZ), 90 * D2R));
        if (set.Contains("Kran")) vehicles.Add(crane);
        if (set.Contains("Birne")) Add(wrecker = WreckingCrane.Create(this, Spawn(8, -26), 90 * D2R));
    }

    Vector3 Spawn(float x, float z) => new(x, soil.Height(x, z) + 0.6f, z);

    void Add(VehicleBase v) => vehicles.Add(v);

    public Building PlaceOldAt(bool house, Vector3 pos, float yaw)
    {
        var st = soil.StatsRect(pos, 5.5f, 5f, yaw);
        soil.LevelRect(pos, house ? 5.2f : 2.3f, house ? 4.5f : 3.6f, yaw, st.mean, 3);
        return buildings.CreateFromBlueprint(house ? Blueprint.OldHouse() : Blueprint.OldGarage(), new Vector3(pos.X, st.mean, pos.Z), yaw, house ? "Altes Fachwerkhaus" : "Alte Ziegelgarage", "old");
    }

    public override void _Process(double delta)
    {
        if (soil != null) soil.ProcessZone(depRect, (float)delta);
    }

    public float Disposed(SoilMat m) => soil != null ? soil.disposed[(int)m] : 0;

    // ---------------- Kulisse ----------------
    void BuildScenery()
    {
        scenery = new Node3D { Name = "Kulisse" };
        AddChild(scenery);
        obstacles = new StaticBody3D { Name = "Hindernisse", CollisionLayer = Layers.Building, CollisionMask = 0 };
        scenery.AddChild(obstacles);
        var k = new MeshKit();
        // Umland (Ring um das Baufeld)
        var ok = new MeshKit();
        float R = 1400f, inner = 63.5f, gy = -0.03f;
        var dn = new Vector3(0, -10, 0);
        ok.Poly("outer_grass", new[] { new Vector3(-R, gy, -R), new Vector3(R, gy, -R), new Vector3(R, gy, -inner), new Vector3(-R, gy, -inner) }, dn);
        ok.Poly("outer_grass", new[] { new Vector3(-R, gy, inner), new Vector3(R, gy, inner), new Vector3(R, gy, R), new Vector3(-R, gy, R) }, dn);
        ok.Poly("outer_grass", new[] { new Vector3(-R, gy, -inner), new Vector3(-inner, gy, -inner), new Vector3(-inner, gy, inner), new Vector3(-R, gy, inner) }, dn);
        ok.Poly("outer_grass", new[] { new Vector3(inner, gy, -inner), new Vector3(R, gy, -inner), new Vector3(R, gy, inner), new Vector3(inner, gy, inner) }, dn);
        ok.Build(scenery, "Wiese", false);
        var floor = new CollisionShape3D { Shape = new WorldBoundaryShape3D { Plane = new Plane(Vector3.Up, -0.03f) } };
        var outerBody = new StaticBody3D { Name = "Umland", CollisionLayer = Layers.Terrain, CollisionMask = 0 };
        outerBody.AddChild(floor);
        scenery.AddChild(outerBody);
        // Straße
        k.Box("asphalt", new Vector3(76, 0.0f, 0), new Vector3(10, 0.06f, 2000));
        k.Box("asphalt", new Vector3(69.5f, 0.0f, Layout.RoadZ), new Vector3(13, 0.05f, 7));
        for (float z = -1000; z < 1000; z += 9) k.Box("paint_white", new Vector3(76, 0.04f, z), new Vector3(0.15f, 0.01f, 4));
        k.Build(scenery, "Strasse", false);
        // Bauzaun
        float E = 63.4f;
        for (int s = 0; s < 4; s++)
            for (float t = -E; t < E - 0.1f; t += 3.5f)
            {
                float c = t + 1.75f;
                Vector3 p; bool alongX = s < 2;
                if (s == 0) p = new Vector3(c, 0, -E); else if (s == 1) p = new Vector3(c, 0, E); else if (s == 2) p = new Vector3(-E, 0, c); else p = new Vector3(E, 0, c);
                if (s == 3 && MathF.Abs(c - Layout.RoadZ) < 4.5f) continue;
                var rot = alongX ? Quaternion.Identity : new Quaternion(Vector3.Up, MathF.PI / 2);
                k.Box("concrete", p + new Vector3(0, 0.08f, 0) + rot * new Vector3(-1.72f, 0, 0), new Vector3(0.7f, 0.16f, 0.22f), rot, 0.02f);
                k.Box("fence", p + rot * new Vector3(-1.72f, 1.1f, 0), new Vector3(0.05f, 2.0f, 0.05f), rot);
                k.Box("fence", p + rot * new Vector3(0, 2.07f, 0), new Vector3(3.45f, 0.05f, 0.05f), rot);
                k.Box("fence", p + rot * new Vector3(0, 0.25f, 0), new Vector3(3.45f, 0.05f, 0.05f), rot);
                for (float u = -1.6f; u < 1.65f; u += 0.25f) k.Box("fence", p + rot * new Vector3(u, 1.16f, 0), new Vector3(0.012f, 1.8f, 0.012f), rot);
                for (float v = 0.45f; v < 2.0f; v += 0.3f) k.Box("fence", p + rot * new Vector3(0, v, 0), new Vector3(3.4f, 0.012f, 0.012f), rot);
                AddStatic(p + new Vector3(0, 1.1f, 0), alongX ? new Vector3(3.5f, 2.2f, 0.3f) : new Vector3(0.3f, 2.2f, 3.5f));
            }
        k.Build(scenery, "Bauzaun");
        // Bäume
        var r = new Rng(99);
        var tk = new MeshKit();
        for (int i = 0; i < 320; i++)
        {
            float a = r.Next() * MathF.PI * 2, d = 75 + r.Next() * 320;
            float x = MathF.Cos(a) * d, z = MathF.Sin(a) * d;
            if (MathF.Abs(x - 76) < 10) continue;
            float s = 0.8f + r.Next() * 0.9f;
            tk.Cyl("bark", new Vector3(x, 2f * s, z), 0.18f * s, 0.3f * s, 4f * s, 7, Quaternion.Identity, false);
            int blobs = 3 + (int)(r.Next() * 3);
            for (int b = 0; b < blobs; b++)
            {
                var c = new Vector3(x + (r.Next() - 0.5f) * 2.2f * s, (5f + r.Next() * 2.5f) * s, z + (r.Next() - 0.5f) * 2.2f * s);
                tk.Sphere("leaves", c, (1.6f + r.Next()) * s, 8);
            }
            if (tk.VertexCount > 50000) tk.Build(scenery, "Baeume");
        }
        tk.Build(scenery, "Baeume");
        // Nachbarhäuser
        var hk = new MeshKit();
        var houses = new[] { new Vector2(-110, -20), new Vector2(-116, 44), new Vector2(-100, 100), new Vector2(-24, 116), new Vector2(36, 120), new Vector2(120, -96), new Vector2(124, -30), new Vector2(128, 48), new Vector2(116, 116), new Vector2(-80, -116), new Vector2(-10, -120), new Vector2(56, -124), new Vector2(160, 20), new Vector2(170, -60), new Vector2(-170, 10) };
        for (int i = 0; i < houses.Length; i++)
        {
            var hp = houses[i];
            float w = 9 + r.Next() * 4, dd = 8 + r.Next() * 3, hh = 5.5f + r.Next() * 3;
            var rot = r.Next() < 0.5f ? Quaternion.Identity : new Quaternion(Vector3.Up, MathF.PI / 2);
            string wall = i % 3 == 0 ? "plaster_white" : i % 3 == 1 ? "plaster" : "brick";
            hk.Box(wall, new Vector3(hp.X, hh / 2, hp.Y), new Vector3(w, hh, dd), rot, 0.05f);
            var p1 = new Vector3(-w / 2 - 0.4f, hh, -dd / 2 - 0.5f); var p2 = new Vector3(w / 2 + 0.4f, hh, -dd / 2 - 0.5f);
            var p3 = new Vector3(w / 2 + 0.4f, hh + dd * 0.4f, 0); var p4 = new Vector3(-w / 2 - 0.4f, hh + dd * 0.4f, 0);
            var p5 = new Vector3(w / 2 + 0.4f, hh, dd / 2 + 0.5f); var p6 = new Vector3(-w / 2 - 0.4f, hh, dd / 2 + 0.5f);
            var o = new Vector3(hp.X, 0, hp.Y);
            var inside = o + new Vector3(0, hh + 0.5f, 0);
            hk.Poly("roof_tile", new[] { o + rot * p1, o + rot * p2, o + rot * p3, o + rot * p4 }, inside);
            hk.Poly("roof_tile", new[] { o + rot * p4, o + rot * p3, o + rot * p5, o + rot * p6 }, inside);
            hk.Poly(wall, new[] { o + rot * new Vector3(-w / 2, hh, -dd / 2), o + rot * new Vector3(-w / 2, hh, dd / 2), o + rot * new Vector3(-w / 2, hh + dd * 0.4f - 0.2f, 0) }, inside);
            hk.Poly(wall, new[] { o + rot * new Vector3(w / 2, hh, -dd / 2), o + rot * new Vector3(w / 2, hh, dd / 2), o + rot * new Vector3(w / 2, hh + dd * 0.4f - 0.2f, 0) }, inside);
            for (int f = 0; f < 4; f++)
            {
                hk.Box("window_glass", o + rot * new Vector3(-w / 2 + 1.5f + f * (w - 3) / 3, hh * 0.55f, -dd / 2 - 0.02f), new Vector3(1.0f, 1.2f, 0.05f), rot);
                hk.Box("window_glass", o + rot * new Vector3(-w / 2 + 1.5f + f * (w - 3) / 3, hh * 0.55f, dd / 2 + 0.02f), new Vector3(1.0f, 1.2f, 0.05f), rot);
            }
        }
        // Ferne Burg auf einem Hügel (Veste)
        hk.Sphere("outer_grass", new Vector3(-520, -330, -900), 400, 32);
        hk.Box("plaster_white", new Vector3(-520, 78, -900), new Vector3(80, 18, 40), 0.5f);
        hk.Box("plaster_white", new Vector3(-550, 88, -900), new Vector3(14, 36, 14), 0.3f);
        hk.Box("plaster_white", new Vector3(-485, 84, -892), new Vector3(18, 26, 18), 0.3f);
        hk.Box("roof_tile", new Vector3(-512, 90, -900), new Vector3(26, 7, 40), 0.3f);
        hk.Build(scenery, "Nachbarschaft");
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
        var P = Layout.Plant; float py = T.Height(P.X, P.Y);
        foreach (var (lx, lz) in new[] { (-2.6f, -2.2f), (2.6f, -2.2f), (-2.6f, 2.2f), (2.6f, 2.2f) })
        {
            k.Box("steel", new Vector3(P.X + lx, py + 2.7f, P.Y + lz), new Vector3(0.35f, 5.4f, 0.35f));
            AddStatic(new Vector3(P.X + lx, py + 2.7f, P.Y + lz), new Vector3(0.4f, 5.4f, 0.4f));
        }
        k.Box("steel", new Vector3(P.X, py + 5.5f, P.Y), new Vector3(5.6f, 0.3f, 4.8f), 0.03f);
        k.Cyl("silo", new Vector3(P.X, py + 9.6f, P.Y), 2.1f, 2.1f, 7.8f, 28, Quaternion.Identity);
        k.Cyl("silo", new Vector3(P.X, py + 4.8f, P.Y), 0.4f, 2.1f, 1.8f, 28, Quaternion.Identity);
        k.Cyl("dark_metal", new Vector3(P.X, py + 3.7f, P.Y), 0.3f, 0.3f, 1.0f, 12, Quaternion.Identity);
        k.Box("paint_orange", new Vector3(P.X, py + 6.0f, P.Y + 2.45f), new Vector3(2.6f, 0.5f, 0.2f), 0.02f);
        k.Box("concrete", new Vector3(P.X - 6, py + 3.4f, P.Y + 4), new Vector3(3.4f, 6.8f, 3.4f), 0.05f);
        AddStatic(new Vector3(P.X - 6, py + 3.4f, P.Y + 4), new Vector3(3.4f, 6.8f, 3.4f));
        // Deponie: Legio-Betonblöcke
        var D = Layout.Dep;
        for (float x = D.Position.X + 0.8f; x < D.End.X; x += 1.65f) { float y = T.Height(x, D.End.Y + 1); k.Box("concrete", new Vector3(x, y + 0.4f, D.End.Y + 1), new Vector3(1.6f, 0.8f, 0.8f), 0.04f); AddStatic(new Vector3(x, y + 0.4f, D.End.Y + 1), new Vector3(1.6f, 0.8f, 0.8f)); }
        for (float z = D.Position.Y + 1; z < D.End.Y; z += 1.65f) { float y = T.Height(D.End.X + 1, z); k.Box("concrete", new Vector3(D.End.X + 1, y + 0.4f, z), new Vector3(0.8f, 0.8f, 1.6f), 0.04f); AddStatic(new Vector3(D.End.X + 1, y + 0.4f, z), new Vector3(0.8f, 0.8f, 1.6f)); }
        // Schild Deponie / Betonwerk
        Sign(k, new Vector3(D.Position.X - 1.5f, 0, D.Position.Y - 1.5f), "paint_red");
        Sign(k, new Vector3(P.X + 5, 0, P.Y - 6), "paint_orange");
        // Lagerplatz-Begrenzung
        var Y = Layout.Yard;
        foreach (var c in new[] { Y.Position, new Vector2(Y.End.X, Y.Position.Y), new Vector2(Y.Position.X, Y.End.Y), Y.End })
        {
            float y = T.Height(c.X, c.Y);
            k.Cyl("paint_red", new Vector3(c.X, y + 0.5f, c.Y), 0.08f, 0.08f, 1f, 8, Quaternion.Identity);
            k.Cyl("paint_white", new Vector3(c.X, y + 0.75f, c.Y), 0.081f, 0.081f, 0.2f, 8, Quaternion.Identity);
        }
        // Leitkegel und Absperrbaken an der Zufahrt
        for (int i = 0; i < 6; i++)
        {
            float x = 56 + i * 1.4f, z = Layout.RoadZ - 3.6f; float y = T.Height(x, z);
            k.Cyl("paint_orange", new Vector3(x, y + 0.36f, z), 0.04f, 0.17f, 0.7f, 12, Quaternion.Identity);
            k.Cyl("paint_white", new Vector3(x, y + 0.45f, z), 0.085f, 0.11f, 0.12f, 12, Quaternion.Identity);
            k.Box("black_plastic", new Vector3(x, y + 0.02f, z), new Vector3(0.38f, 0.04f, 0.38f));
        }
        k.Build(scenery, "Baustelleneinrichtung");
    }

    void Sign(MeshKit k, Vector3 p, string mat)
    {
        float y = soil.Height(p.X, p.Z);
        k.Cyl("steel", new Vector3(p.X - 0.8f, y + 1.1f, p.Z), 0.04f, 0.04f, 2.2f, 8, Quaternion.Identity);
        k.Cyl("steel", new Vector3(p.X + 0.8f, y + 1.1f, p.Z), 0.04f, 0.04f, 2.2f, 8, Quaternion.Identity);
        k.Box(mat, new Vector3(p.X, y + 1.9f, p.Z), new Vector3(2.0f, 0.8f, 0.05f), 0.01f);
        k.Box("paint_white", new Vector3(p.X, y + 1.9f, p.Z + 0.03f), new Vector3(1.6f, 0.12f, 0.01f));
    }

    void AddStatic(Vector3 c, Vector3 size)
    {
        obstacles.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = c });
    }

    /// <summary>Markierte Fläche (z. B. Aushub-Zone) als halbtransparenter Rahmen.</summary>
    public Node3D Marker(Rect2 r, string name)
    {
        var k = new MeshKit();
        var c = r.GetCenter();
        float y0 = soil.StatsRect(new Vector3(c.X, 0, c.Y), r.Size.X / 2, r.Size.Y / 2, 0).max;
        var x0 = r.Position.X; var z0 = r.Position.Y; var x1 = r.End.X; var z1 = r.End.Y;
        foreach (var (a, b) in new[] { (new Vector2(x0, z0), new Vector2(x1, z0)), (new Vector2(x1, z0), new Vector2(x1, z1)), (new Vector2(x1, z1), new Vector2(x0, z1)), (new Vector2(x0, z1), new Vector2(x0, z0)) })
        {
            var pa = new Vector3(a.X, y0, a.Y); var pb = new Vector3(b.X, y0, b.Y);
            k.Box("zone", (pa + pb) * 0.5f + Vector3.Up * 0.6f, new Vector3(MathF.Abs(b.X - a.X) + 0.1f, 1.2f, MathF.Abs(b.Y - a.Y) + 0.1f));
        }
        foreach (var q in new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x0, z1), new Vector2(x1, z1) })
            k.Cyl("marker_red", new Vector3(q.X, soil.Height(q.X, q.Y) + 0.6f, q.Y), 0.05f, 0.05f, 1.2f, 8, Quaternion.Identity);
        return k.Build(this, name, false);
    }

    public override void _ExitTree()
    {
        SoilFX.Catchers.Clear();
        if (I == this) I = null;
        if (GameServices.Crane == crane) GameServices.Crane = null;
    }
}
