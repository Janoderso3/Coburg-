using System;
using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    public enum SoilMat : byte { None = 0, Soil = 1, Rubble = 2, Gravel = 3, Concrete = 4 }

    public struct SoilGen
    {
        public float h;          // Geländehöhe
        public float fill;       // davon loses Material obenauf
        public SoilMat fillMat;
        public byte surf;        // 0 Gras, 3 Schotter, 5 Asphalt
        public byte zone;        // 1 Deponie
    }

    /// <summary>
    /// Verformbarer Boden auf Basis von Unity-Terrain.
    /// - Volumen bleibt erhalten (gewachsener Boden lockert beim Lösen um Faktor 1,25 auf)
    /// - loses Material rieselt wie Sand bis zum Schüttwinkel (ca. 34°)
    /// - gewachsener, bindiger Boden steht steil, bricht aber mit der Zeit in Brocken ab
    /// - Bodenhorizonte: Mutterboden, Lehm, Sand/Kies
    /// </summary>
    public class SoilTerrain : MonoBehaviour
    {
        public static SoilTerrain I;

        public const float Size = 128f;
        public const int Res = 513;
        public const float Cell = Size / (Res - 1);
        public const float BaseY = -16f;
        public const float Range = 48f;
        public const float Bulking = 1.25f;
        public const float Area = Cell * Cell;
        public static bool LowLayerCount = false; // Handy: 4 statt 8 Terrain-Layer

        public Terrain terrain;
        public TerrainData data;
        public float[] h, h0, fill, wear, stress;
        public byte[] fillMat, surf, zone;
        public float[] disposed = new float[5];
        public float half = Size / 2f;

        public float tanLoose = Mathf.Tan(34f * Mathf.Deg2Rad);
        public float tanLooseWet = Mathf.Tan(38f * Mathf.Deg2Rad);
        public float tanNatural = Mathf.Tan(78f * Mathf.Deg2Rad);
        public float tanCritBase = Mathf.Tan(62f * Mathf.Deg2Rad);

        RectInt? dirtyH, dirtyA, relaxBox;
        float syncTimer, alphaTimer, collapseTimer;
        bool needSync;
        readonly HashSet<int> collapseCand = new HashSet<int>();
        readonly List<int> tmpList = new List<int>();
        float[] critAngle; // pro Zelle zufälliger kritischer Böschungswinkel (tan)
        readonly System.Random rnd = new System.Random(77);
        public int Layers => LowLayerCount ? 4 : 8;

        public event Action<Vector3, float, SoilMat> OnClod; // Brocken abgebrochen
        public float exposedFaces; // Statistik

        // ---------------- Aufbau ----------------
        public void Init(Func<float, float, SoilGen> gen)
        {
            I = this;
            int n = Res * Res;
            h = new float[n]; h0 = new float[n]; fill = new float[n]; wear = new float[n]; stress = new float[n];
            fillMat = new byte[n]; surf = new byte[n]; zone = new byte[n]; critAngle = new float[n];
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    int k = j * Res + i;
                    var g = gen(Wx(i), Wx(j));
                    h[k] = g.h;
                    fill[k] = g.fill;
                    h0[k] = g.h - g.fill;
                    fillMat[k] = (byte)g.fillMat;
                    surf[k] = g.surf;
                    zone[k] = g.zone;
                    critAngle[k] = Mathf.Tan(Mathf.Lerp(58f, 80f, (float)rnd.NextDouble()) * Mathf.Deg2Rad);
                }
            BuildTerrain();
        }

        void BuildTerrain()
        {
            data = new TerrainData { heightmapResolution = Res };
            data.size = new Vector3(Size, Range, Size);
            data.alphamapResolution = Res - 1;
            data.baseMapResolution = 1024;
            data.SetDetailResolution(256, 16);
            data.terrainLayers = TerrainLayers.Create(Layers);
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Baugelände";
            go.layer = Layers_Terrain;
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(-half, BaseY, -half);
            terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = Application.isMobilePlatform ? 4 : 1;
            terrain.basemapDistance = Application.isMobilePlatform ? 60 : 250;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            var mat = TerrainLayers.TerrainMaterial();
            if (mat) terrain.materialTemplate = mat;
            var col = go.GetComponent<TerrainCollider>();
            col.sharedMaterial = U.SoilMat;
            PushHeights(new RectInt(0, 0, Res, Res));
            PushAlpha(new RectInt(0, 0, Res - 1, Res - 1));
            data.SyncHeightmap();
        }
        const int Layers_Terrain = BauSandbox.Layers.Terrain;

        // ---------------- Koordinaten ----------------
        public float Gx(float x) => (x + half) / Cell;
        public float Wx(int i) => i * Cell - half;
        public int Idx(int i, int j) => j * Res + i;
        public bool Inside(float x, float z, float m = 0) => Mathf.Abs(x) < half - m && Mathf.Abs(z) < half - m;

        public float Height(float x, float z)
        {
            float fx = Mathf.Clamp(Gx(x), 0, Res - 1.0001f), fz = Mathf.Clamp(Gx(z), 0, Res - 1.0001f);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            int k = j * Res + i;
            return Mathf.Lerp(Mathf.Lerp(h[k], h[k + 1], u), Mathf.Lerp(h[k + Res], h[k + Res + 1], u), v);
        }

        public Vector3 Normal(float x, float z)
        {
            float e = Cell;
            return new Vector3(Height(x - e, z) - Height(x + e, z), 2 * e, Height(x, z - e) - Height(x, z + e)).normalized;
        }

        /// <summary>Lockermaterial-Dicke an der Stelle (für Fahrwiderstand/Einsinken).</summary>
        public float LooseAt(float x, float z)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(Gx(x)), 0, Res - 1), j = Mathf.Clamp(Mathf.RoundToInt(Gx(z)), 0, Res - 1);
            return fill[j * Res + i];
        }

        public struct Stats { public float mean, min, max; public int count; }
        public Stats StatsRect(Vector3 c, float hw, float hd, float yaw)
        {
            float cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            float r = Mathf.Sqrt(hw * hw + hd * hd);
            int i0 = Mathf.Max(1, Mathf.FloorToInt(Gx(c.x - r))), i1 = Mathf.Min(Res - 2, Mathf.CeilToInt(Gx(c.x + r)));
            int j0 = Mathf.Max(1, Mathf.FloorToInt(Gx(c.z - r))), j1 = Mathf.Min(Res - 2, Mathf.CeilToInt(Gx(c.z + r)));
            var s = new Stats { min = float.MaxValue, max = float.MinValue };
            double sum = 0;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float dx = Wx(i) - c.x, dz = Wx(j) - c.z;
                    float lx = dx * cs - dz * sn, lz = dx * sn + dz * cs;
                    if (Mathf.Abs(lx) > hw || Mathf.Abs(lz) > hd) continue;
                    float hh = h[j * Res + i];
                    sum += hh; s.count++;
                    if (hh < s.min) s.min = hh;
                    if (hh > s.max) s.max = hh;
                }
            s.mean = s.count > 0 ? (float)(sum / s.count) : 0;
            return s;
        }

        /// <summary>Ausgehobenes Volumen (gewachsener Boden) in einem Rechteck unterhalb der Ursprungshöhe.</summary>
        public float ExcavatedRect(Vector3 c, float hw, float hd, float yaw, float maxDepth)
        {
            float cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            float r = Mathf.Sqrt(hw * hw + hd * hd);
            int i0 = Mathf.Max(1, Mathf.FloorToInt(Gx(c.x - r))), i1 = Mathf.Min(Res - 2, Mathf.CeilToInt(Gx(c.x + r)));
            int j0 = Mathf.Max(1, Mathf.FloorToInt(Gx(c.z - r))), j1 = Mathf.Min(Res - 2, Mathf.CeilToInt(Gx(c.z + r)));
            double v = 0;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float dx = Wx(i) - c.x, dz = Wx(j) - c.z;
                    float lx = dx * cs - dz * sn, lz = dx * sn + dz * cs;
                    if (Mathf.Abs(lx) > hw || Mathf.Abs(lz) > hd) continue;
                    int k = j * Res + i;
                    v += Mathf.Clamp(h0[k] - h[k], 0, maxDepth);
                }
            return (float)v * Area;
        }

        // ---------------- Verformung ----------------
        void GridRange(float cx, float cz, float r, out int i0, out int i1, out int j0, out int j1)
        {
            const int m = 3;
            i0 = Mathf.Max(m, Mathf.FloorToInt(Gx(cx - r))); i1 = Mathf.Min(Res - 1 - m, Mathf.CeilToInt(Gx(cx + r)));
            j0 = Mathf.Max(m, Mathf.FloorToInt(Gx(cz - r))); j1 = Mathf.Min(Res - 1 - m, Mathf.CeilToInt(Gx(cz + r)));
        }

        public struct CutResult { public float vol; public SoilMat mat; }

        /// <summary>
        /// Schneidkante von a nach b bis Höhe y. maxVol begrenzt das gelöste (aufgelockerte) Volumen.
        /// Rückgabe: gelöstes Lockervolumen.
        /// </summary>
        public CutResult CutSegment(Vector3 a, Vector3 b, float y, float halfThick, float maxVol)
        {
            var res = new CutResult { mat = SoilMat.Soil };
            if (maxVol <= 0) return res;
            float r = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.z - a.z)) * 0.5f + halfThick + Cell;
            float cx = (a.x + b.x) * 0.5f, cz = (a.z + b.z) * 0.5f;
            GridRange(cx, cz, r, out int i0, out int i1, out int j0, out int j1);
            float dx = b.x - a.x, dz = b.z - a.z;
            float l2 = Mathf.Max(1e-6f, dx * dx + dz * dz);
            tmpList.Clear();
            float avail = 0;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float px = Wx(i), pz = Wx(j);
                    float t = Mathf.Clamp01(((px - a.x) * dx + (pz - a.z) * dz) / l2);
                    float qx = a.x + dx * t - px, qz = a.z + dz * t - pz;
                    if (qx * qx + qz * qz > halfThick * halfThick) continue;
                    int k = j * Res + i;
                    float d = h[k] - y;
                    if (d <= 0.001f) continue;
                    tmpList.Add(k);
                    // Lockervolumen: loser Anteil 1:1, gewachsener mit Auflockerung
                    float fl = Mathf.Min(fill[k], d);
                    avail += fl + (d - fl) * Bulking;
                }
            if (tmpList.Count == 0) return res;
            float f = Mathf.Min(1f, maxVol / (avail * Area));
            var mv = new float[5];
            foreach (int k in tmpList)
            {
                float d = (h[k] - y) * f;
                float fl = Mathf.Min(fill[k], d);
                if (fl > 0) { mv[Mathf.Max(1, (int)fillMat[k])] += fl; fill[k] -= fl; }
                float nat = d - fl;
                if (nat > 0) mv[(int)NaturalMatAt(k)] += nat * Bulking;
                h[k] -= d;
                wear[k] = 1;
                if (fill[k] < 0.001f) fillMat[k] = 0;
            }
            int best = 1; float bv = -1;
            for (int m = 1; m < 5; m++) if (mv[m] > bv) { bv = mv[m]; best = m; }
            res.vol = avail * f * Area;
            res.mat = (SoilMat)best;
            MarkDirty(i0, j0, i1, j1);
            MarkRelax(i0, j0, i1, j1);
            MarkCollapse(i0 - 2, j0 - 2, i1 + 2, j1 + 2);
            return res;
        }

        SoilMat NaturalMatAt(int k)
        {
            if (surf[k] == 3 && h0[k] - (h[k] - fill[k]) < 0.35f) return SoilMat.Gravel;
            return SoilMat.Soil;
        }

        /// <summary>Lockeres Material ablegen (Gaußhügel).</summary>
        public void Deposit(float x, float z, float vol, SoilMat mat, float radius = 0.45f)
        {
            if (vol <= 0) return;
            GridRange(x, z, radius * 1.6f, out int i0, out int i1, out int j0, out int j1);
            float wsum = 0, s2 = radius * radius;
            tmpList.Clear();
            var ws = new List<float>();
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float dx = Wx(i) - x, dz = Wx(j) - z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > s2 * 2.56f) continue;
                    float w = Mathf.Exp(-d2 / s2);
                    tmpList.Add(j * Res + i); ws.Add(w); wsum += w;
                }
            if (wsum <= 0) return;
            float dh = vol / Area;
            for (int q = 0; q < tmpList.Count; q++) Add(tmpList[q], dh * ws[q] / wsum, mat);
            MarkDirty(i0, j0, i1, j1);
            MarkRelax(i0, j0, i1, j1);
        }

        void Add(int k, float dh, SoilMat mat)
        {
            if (zone[k] == 1) { disposed[(int)mat] += dh * Area; return; }
            if (fill[k] < dh * 0.8f || fillMat[k] == 0) fillMat[k] = (byte)mat;
            fill[k] += dh;
            h[k] += dh;
        }

        /// <summary>Verdichtung/Spurrillen durch schwere Fahrzeuge auf losem Material.</summary>
        public void Compact(Vector3 p, float r, float pressure, float dt)
        {
            GridRange(p.x, p.z, r, out int i0, out int i1, out int j0, out int j1);
            bool any = false;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float dx = Wx(i) - p.x, dz = Wx(j) - p.z;
                    if (dx * dx + dz * dz > r * r) continue;
                    int k = j * Res + i;
                    if (wear[k] < 1f) { wear[k] = Mathf.Min(1f, wear[k] + pressure * dt * 0.6f); any = true; }
                    float fl = fill[k];
                    if (fl < 0.02f) continue;
                    // loses Material sackt um bis zu 12 % ein (Verdichtung)
                    float c = Mathf.Min(fl * 0.12f, pressure * dt * 0.05f);
                    if (c <= 0.0005f) continue;
                    fill[k] -= c; h[k] -= c; h0[k] -= 0; any = true;
                }
            if (any) { MarkDirty(i0, j0, i1, j1); MarkRelax(i0, j0, i1, j1); }
        }

        /// <summary>Auflast (z. B. Fahrzeug an der Grubenkante) beschleunigt Böschungsbruch.</summary>
        public void Surcharge(Vector3 p, float r, float amount)
        {
            GridRange(p.x, p.z, r, out int i0, out int i1, out int j0, out int j1);
            for (int j = j0; j <= j1; j += 2)
                for (int i = i0; i <= i1; i += 2)
                {
                    int k = j * Res + i;
                    if (h0[k] - (h[k] - fill[k]) < -0.05f) continue;
                    stress[k] += amount;
                    collapseCand.Add(k);
                }
        }

        public float LevelRect(Vector3 c, float hw, float hd, float yaw, float y, byte surface = 3)
        {
            float cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            float r = Mathf.Sqrt(hw * hw + hd * hd);
            GridRange(c.x, c.z, r, out int i0, out int i1, out int j0, out int j1);
            float removed = 0;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float dx = Wx(i) - c.x, dz = Wx(j) - c.z;
                    float lx = dx * cs - dz * sn, lz = dx * sn + dz * cs;
                    if (Mathf.Abs(lx) > hw || Mathf.Abs(lz) > hd) continue;
                    int k = j * Res + i;
                    removed += h[k] - y;
                    h[k] = y; h0[k] = y; fill[k] = 0; fillMat[k] = 0; surf[k] = surface; wear[k] = 0;
                }
            MarkDirty(i0, j0, i1, j1);
            MarkRelax(i0 - 2, j0 - 2, i1 + 2, j1 + 2);
            return removed * Area;
        }

        // ---------------- Verwaltung geänderter Bereiche ----------------
        static RectInt Union(RectInt? a, int i0, int j0, int i1, int j1)
        {
            if (!a.HasValue) return new RectInt(i0, j0, i1 - i0 + 1, j1 - j0 + 1);
            var r = a.Value;
            int x0 = Mathf.Min(r.xMin, i0), y0 = Mathf.Min(r.yMin, j0), x1 = Mathf.Max(r.xMax - 1, i1), y1 = Mathf.Max(r.yMax - 1, j1);
            return new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }
        public void MarkDirty(int i0, int j0, int i1, int j1)
        {
            i0 = Mathf.Clamp(i0, 0, Res - 1); j0 = Mathf.Clamp(j0, 0, Res - 1); i1 = Mathf.Clamp(i1, 0, Res - 1); j1 = Mathf.Clamp(j1, 0, Res - 1);
            dirtyH = Union(dirtyH, i0, j0, i1, j1);
            dirtyA = Union(dirtyA, i0, j0, i1, j1);
        }
        public void MarkRelax(int i0, int j0, int i1, int j1)
        {
            i0 = Mathf.Max(2, i0 - 1); j0 = Mathf.Max(2, j0 - 1); i1 = Mathf.Min(Res - 3, i1 + 1); j1 = Mathf.Min(Res - 3, j1 + 1);
            relaxBox = Union(relaxBox, i0, j0, i1, j1);
        }
        public void MarkCollapse(int i0, int j0, int i1, int j1)
        {
            i0 = Mathf.Max(3, i0); j0 = Mathf.Max(3, j0); i1 = Mathf.Min(Res - 4, i1); j1 = Mathf.Min(Res - 4, j1);
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) collapseCand.Add(j * Res + i);
        }

        // ---------------- Rieseln (Schüttwinkel) ----------------
        static readonly int[] NB = { 1, 0, -1, 0, 0, 1, 0, -1, 1, 1, -1, -1, 1, -1, -1, 1 };
        public void Relax(int iterations)
        {
            if (!relaxBox.HasValue) return;
            float diag = Cell * Mathf.Sqrt(2);
            float limL = tanLoose * Cell, limLd = tanLoose * diag;
            float limN = tanNatural * Cell, limNd = tanNatural * diag;
            var box = relaxBox;
            for (int it = 0; it < iterations && box.HasValue; it++)
            {
                var b = box.Value;
                int ni0 = int.MaxValue, nj0 = int.MaxValue, ni1 = -1, nj1 = -1;
                bool odd = (it & 1) == 1;
                for (int jj = b.yMin; jj < b.yMax; jj++)
                {
                    int j = odd ? b.yMax - 1 - (jj - b.yMin) : jj;
                    for (int ii = b.xMin; ii < b.xMax; ii++)
                    {
                        int i = odd ? b.xMax - 1 - (ii - b.xMin) : ii;
                        int k = j * Res + i;
                        for (int q = 0; q < 16; q += 2)
                        {
                            int ni = i + NB[q], nj = j + NB[q + 1];
                            if (ni < 2 || nj < 2 || ni >= Res - 2 || nj >= Res - 2) continue;
                            int n = nj * Res + ni;
                            float diff = h[k] - h[n];
                            bool dg = q >= 8;
                            float lim = dg ? limLd : limL;
                            if (diff <= lim) continue;
                            // Sand fließt zügig: ~30 % des Überschusses je Schritt
                            float amt = (diff - lim) * 0.3f;
                            float movable = fill[k];
                            float limNat = dg ? limNd : limN;
                            if (diff > limNat) movable += (diff - limNat) * 0.35f; // Überhang bricht nach
                            if (amt > movable) amt = movable;
                            if (amt < 0.0004f) continue;
                            var mt = fillMat[k] == 0 ? SoilMat.Soil : (SoilMat)fillMat[k];
                            float fromFill = Mathf.Min(fill[k], amt);
                            fill[k] -= fromFill;
                            h[k] -= amt;
                            // gewachsener Anteil lockert auf
                            float natPart = amt - fromFill;
                            Add(n, fromFill + natPart * Bulking, mt);
                            if (fill[k] < 0.001f) fillMat[k] = 0;
                            if (i < ni0) ni0 = i; if (i > ni1) ni1 = i;
                            if (j < nj0) nj0 = j; if (j > nj1) nj1 = j;
                            if (ni < ni0) ni0 = ni; if (ni > ni1) ni1 = ni;
                            if (nj < nj0) nj0 = nj; if (nj > nj1) nj1 = nj;
                        }
                    }
                }
                if (ni1 < 0) box = null;
                else
                {
                    box = new RectInt(Mathf.Max(2, ni0 - 2), Mathf.Max(2, nj0 - 2), 0, 0);
                    var bb = box.Value;
                    bb.xMax = Mathf.Min(Res - 2, ni1 + 3); bb.yMax = Mathf.Min(Res - 2, nj1 + 3);
                    box = bb;
                    MarkDirty(bb.xMin, bb.yMin, bb.xMax - 1, bb.yMax - 1);
                }
            }
            relaxBox = box;
        }

        // ---------------- Böschungsbruch (gewachsener Boden) ----------------
        void CheckCollapse(float dt)
        {
            if (collapseCand.Count == 0) return;
            tmpList.Clear();
            tmpList.AddRange(collapseCand);
            collapseCand.Clear();
            exposedFaces = 0;
            int events = 0;
            foreach (int k in tmpList)
            {
                int i = k % Res, j = k / Res;
                if (i < 3 || j < 3 || i > Res - 4 || j > Res - 4) continue;
                float natTop = h[k] - fill[k];
                // steilster Abfall zu einem Nachbarn
                float worst = 0; int wn = -1;
                for (int q = 0; q < 8; q += 2)
                {
                    int n = (j + NB[q + 1]) * Res + (i + NB[q]);
                    float d = natTop - h[n];
                    if (d > worst) { worst = d; wn = n; }
                }
                float slope = worst / Cell;
                if (slope < 1.2f) { stress[k] = Mathf.Max(0, stress[k] - dt); continue; }
                exposedFaces++;
                // Spannung baut sich auf, je steiler und je höher die Wand
                float crit = critAngle[k];
                float over = slope / crit;
                stress[k] += dt * (0.15f + Mathf.Max(0, over - 0.7f) * 2.2f) * (0.6f + Mathf.Min(worst, 3f) * 0.4f);
                if (stress[k] < 1f || events > 6) { collapseCand.Add(k); continue; }
                // Bruch: Keil hinter der Kante löst sich als Brocken
                stress[k] = 0;
                events++;
                Failure(i, j, wn, worst);
            }
        }

        void Failure(int i, int j, int lowN, float drop)
        {
            int ni = lowN % Res, nj = lowN / Res;
            int di = i - ni, dj = j - nj; // Richtung hangaufwärts
            float vol = 0;
            // Bruchfläche ~ 50-60°: von der Kante nach hinten abnehmend
            float targetSlope = Mathf.Tan(Mathf.Lerp(48f, 60f, (float)rnd.NextDouble()) * Mathf.Deg2Rad);
            int depthCells = Mathf.Clamp(Mathf.CeilToInt(drop / (targetSlope * Cell)), 1, 8);
            int widthCells = 1 + rnd.Next(3);
            float baseH = h[lowN];
            Vector3 center = Vector3.zero; int cnt = 0;
            for (int s = 0; s < depthCells; s++)
                for (int w = -widthCells; w <= widthCells; w++)
                {
                    int ci = i + di * s + (dj != 0 ? w : 0);
                    int cj = j + dj * s + (di != 0 ? w : 0);
                    if (ci < 3 || cj < 3 || ci > Res - 4 || cj > Res - 4) continue;
                    int k = cj * Res + ci;
                    float allowed = baseH + targetSlope * Cell * (s + 1);
                    float natTop = h[k] - fill[k];
                    if (natTop <= allowed) continue;
                    float cut = natTop - allowed;
                    // loses Material obendrauf rutscht mit
                    float total = cut + fill[k];
                    vol += cut * Bulking + fill[k];
                    h[k] -= total;
                    fill[k] = 0; fillMat[k] = 0;
                    center += new Vector3(Wx(ci), h[k] + total * 0.5f, Wx(cj)); cnt++;
                }
            if (vol <= 0 || cnt == 0) return;
            vol *= Area;
            center /= cnt;
            int i0 = i - 10, j0 = j - 10, i1 = i + 10, j1 = j + 10;
            MarkDirty(i0, j0, i1, j1);
            MarkRelax(i0, j0, i1, j1);
            MarkCollapse(i0, j0, i1, j1);
            if (OnClod != null) OnClod(center, vol, SoilMat.Soil);
            else Deposit(Wx(ni), Wx(nj), vol, SoilMat.Soil, 0.5f);
        }

        // ---------------- Übertragen an Unity-Terrain ----------------
        void PushHeights(RectInt r)
        {
            int w = r.width, hh = r.height;
            var arr = new float[hh, w];
            float inv = 1f / Range;
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < w; x++)
                    arr[y, x] = Mathf.Clamp01((h[(r.yMin + y) * Res + r.xMin + x] - BaseY) * inv);
            data.SetHeightsDelayLOD(r.xMin, r.yMin, arr);
            needSync = true;
        }

        readonly float[] wbuf = new float[8];
        void Weights(int k)
        {
            for (int q = 0; q < 8; q++) wbuf[q] = 0;
            float fl = fill[k];
            float natTop = h[k] - fl;
            float depth = h0[k] - natTop;
            // gewachsener Boden
            if (depth > 0.03f)
            {
                if (surf[k] == 3 && depth < 0.3f) wbuf[4] = 1;
                else if (depth < 0.3f) wbuf[1] = 1;
                else if (depth < 0.55f) { float t = (depth - 0.3f) / 0.25f; wbuf[1] = 1 - t; wbuf[2] = t; }
                else if (depth < 2.3f) wbuf[2] = 1;
                else if (depth < 2.8f) { float t = (depth - 2.3f) / 0.5f; wbuf[2] = 1 - t; wbuf[3] = t; }
                else wbuf[3] = 1;
            }
            else
            {
                if (surf[k] == 3) wbuf[4] = 1;
                else if (surf[k] == 5) wbuf[7] = 1;
                else
                {
                    float wr = wear[k];
                    wbuf[0] = 1 - wr; wbuf[1] = wr;
                    if (depth > 0) { float t = Mathf.Clamp01(depth / 0.03f); wbuf[0] *= 1 - t; wbuf[1] = 1 - wbuf[0]; }
                }
            }
            if (fl > 0.003f)
            {
                float t = Mathf.Clamp01(fl / 0.06f);
                for (int q = 0; q < 8; q++) wbuf[q] *= 1 - t;
                int layer = fillMat[k] == (byte)SoilMat.Rubble ? 6 : fillMat[k] == (byte)SoilMat.Gravel ? 4 : fillMat[k] == (byte)SoilMat.Concrete ? 7 : 5;
                wbuf[layer] += t;
            }
        }

        void PushAlpha(RectInt r)
        {
            int A = Res - 1;
            int x0 = Mathf.Clamp(r.xMin, 0, A - 1), y0 = Mathf.Clamp(r.yMin, 0, A - 1);
            int x1 = Mathf.Clamp(r.xMax, 1, A), y1 = Mathf.Clamp(r.yMax, 1, A);
            int w = x1 - x0, hh = y1 - y0;
            if (w <= 0 || hh <= 0) return;
            int L = Layers;
            var arr = new float[hh, w, L];
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < w; x++)
                {
                    Weights((y0 + y) * Res + x0 + x);
                    if (L == 8) for (int q = 0; q < 8; q++) arr[y, x, q] = wbuf[q];
                    else
                    {
                        // Handy: Gras | Erde (Mutterboden, lose Erde) | Lehm/Sand | Schotter/Schutt/Beton
                        arr[y, x, 0] = wbuf[0];
                        arr[y, x, 1] = wbuf[1] + wbuf[5];
                        arr[y, x, 2] = wbuf[2] + wbuf[3];
                        arr[y, x, 3] = wbuf[4] + wbuf[6] + wbuf[7];
                    }
                }
            data.SetAlphamaps(x0, y0, arr);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Relax(3);
            collapseTimer += dt;
            if (collapseTimer > 0.2f) { CheckCollapse(collapseTimer); collapseTimer = 0; }
            if (dirtyH.HasValue)
            {
                var r = dirtyH.Value;
                dirtyH = null;
                r.xMin = Mathf.Max(0, r.xMin - 1); r.yMin = Mathf.Max(0, r.yMin - 1);
                r.xMax = Mathf.Min(Res, r.xMax + 1); r.yMax = Mathf.Min(Res, r.yMax + 1);
                PushHeights(r);
            }
            alphaTimer += dt;
            if (dirtyA.HasValue && alphaTimer > 0.12f)
            {
                alphaTimer = 0;
                var r = dirtyA.Value;
                dirtyA = null;
                PushAlpha(r);
            }
            syncTimer += dt;
            if (needSync && syncTimer > 0.1f)
            {
                syncTimer = 0;
                needSync = false;
                data.SyncHeightmap();
            }
        }

        /// <summary>Deponie: loses Material in der Zone wird nach und nach abgefahren.</summary>
        public void ProcessZone(RectInt zoneRect, float dt)
        {
            bool any = false;
            float rate = Mathf.Min(1f, dt * 0.2f);
            for (int j = zoneRect.yMin; j < zoneRect.yMax; j++)
                for (int i = zoneRect.xMin; i < zoneRect.xMax; i++)
                {
                    int k = j * Res + i;
                    if (zone[k] != 1 || fill[k] < 0.002f) continue;
                    float take = Mathf.Min(fill[k], fill[k] * rate + 0.0005f);
                    disposed[Mathf.Max(1, (int)fillMat[k])] += take * Area;
                    fill[k] -= take; h[k] -= take;
                    any = true;
                }
            if (any) MarkDirty(zoneRect.xMin, zoneRect.yMin, zoneRect.xMax, zoneRect.yMax);
        }

        public RectInt ToGridRect(float x0, float z0, float x1, float z1)
        {
            int i0 = Mathf.FloorToInt(Gx(x0)), i1 = Mathf.CeilToInt(Gx(x1)), j0 = Mathf.FloorToInt(Gx(z0)), j1 = Mathf.CeilToInt(Gx(z1));
            return new RectInt(i0, j0, i1 - i0, j1 - j0);
        }

        public float TotalDisposed(params SoilMat[] mats)
        {
            float s = 0;
            foreach (var m in mats) s += disposed[(int)m];
            return s;
        }
    }
}
