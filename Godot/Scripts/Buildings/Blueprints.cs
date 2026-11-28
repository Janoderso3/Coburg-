using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox
{
    public class PieceType
    {
        public string name, mat; public float hp, dens; public int span; public bool noSupport, glass;
        public PieceType(string n, string mat, float hp, int span, float dens, bool noSupport = false, bool glass = false)
        { name = n; this.mat = mat; this.hp = hp; this.span = span; this.dens = dens; this.noSupport = noSupport; this.glass = glass; }

        // Festigkeit (Schaden bis Bruch), Spannweite ohne Auflager in Schritten, Dichte t/m³
        public static readonly Dictionary<string, PieceType> All = new Dictionary<string, PieceType>
        {
            { "brick", new PieceType("Mauerwerk", "brick", 45, 4, 1.8f) },
            { "plaster", new PieceType("Mauerwerk", "plaster", 45, 4, 1.7f) },
            { "prefab", new PieceType("Fertigteil", "prefab", 70, 6, 1.3f) },
            { "concrete", new PieceType("Beton", "concrete", 170, 6, 2.4f) },
            { "roof", new PieceType("Dach", "roof_tile", 10, 7, 0.6f) },
            { "wood", new PieceType("Holz", "timber", 16, 5, 0.6f) },
            { "glass", new PieceType("Glas", "window_glass", 1, 2, 2.5f, true, true) },
            { "door", new PieceType("Tür", "door", 8, 2, 0.6f, true) },
            { "steel", new PieceType("Stahl", "gate", 60, 3, 1.0f, true) },
        };
    }

    public class BoxDef
    {
        public string type; public Vector3 c, size; public float yaw, pitch;
        public Quaternion Rot => Quaternion.FromEuler(new Vector3(pitch, yaw, 0));
    }

    /// <summary>Bauplan aus Quadern (Mauersteine, Deckenplatten, Dachziegel-Paneele, Fenster).</summary>
    public class Blueprint
    {
        public readonly List<BoxDef> boxes = new List<BoxDef>();

        public void Add(string type, float x, float y, float z, float sx, float sy, float sz, float yaw = 0, float pitch = 0)
        {
            boxes.Add(new BoxDef { type = type, c = new Vector3(x, y, z), size = new Vector3(sx, sy, sz), yaw = yaw, pitch = pitch });
        }

        public struct Opening { public float s0, s1, y0, y1; public string kind; public Opening(float s0, float s1, float y0, float y1, string k) { this.s0 = s0; this.s1 = s1; this.y0 = y0; this.y1 = y1; kind = k; } }
        public class Gable { public float sPeak, span, slope; }

        /// <summary>Mauer von (x0,z0) nach (x1,z1); Öffnungen in Wandkoordinaten (s entlang, y ab Mauerfuß).</summary>
        public void Wall(float x0, float z0, float x1, float z1, float y0, float h, float t, string type, Opening[] openings = null, Gable gable = null, float block = 0.5f, float course = 0.5f, bool fachwerk = false, bool bond = true)
        {
            openings = openings ?? new Opening[0];
            float dx = x1 - x0, dz = z1 - z0;
            float L = MathF.Sqrt(dx * dx + dz * dz);
            float ux = dx / L, uz = dz / L;
            float yaw = MathF.Atan2(-dz, dx);
            int n = Math.Max(1, (int)MathF.Round(L / block));
            float bl = L / n;
            float hTot = gable != null ? h + gable.span * gable.slope : h;
            int m = Math.Max(1, (int)MathF.Round(h / course));
            float bh = h / m;
            int courses = gable != null ? (int)MathF.Ceiling(hTot / bh) : m;
            Func<float, float, bool> inOpen = (s, y) =>
            {
                foreach (var op in openings) if (s > op.s0 && s < op.s1 && y > op.y0 && y < op.y1) return true;
                return false;
            };
            for (int c = 0; c < courses; c++)
            {
                float yc = (c + 0.5f) * bh;
                float off = bond && (c % 2 == 1) ? bl / 2 : 0;
                var segs = new List<Vector2>();
                if (off > 0)
                {
                    segs.Add(new Vector2(0, off));
                    for (int i = 0; i < n - 1; i++) segs.Add(new Vector2(off + i * bl, off + (i + 1) * bl));
                    segs.Add(new Vector2(L - off, L));
                }
                else for (int i = 0; i < n; i++) segs.Add(new Vector2(i * bl, (i + 1) * bl));
                // an Öffnungskanten schneiden
                foreach (var op in openings)
                {
                    if (!(yc > op.y0 && yc < op.y1)) continue;
                    var outl = new List<Vector2>();
                    foreach (var sg in segs)
                    {
                        var pts = new List<float> { sg.X };
                        if (op.s0 > sg.X + 0.02f && op.s0 < sg.Y - 0.02f) pts.Add(op.s0);
                        if (op.s1 > sg.X + 0.02f && op.s1 < sg.Y - 0.02f) pts.Add(op.s1);
                        pts.Add(sg.Y);
                        for (int i = 0; i < pts.Count - 1; i++) outl.Add(new Vector2(pts[i], pts[i + 1]));
                    }
                    segs = outl;
                }
                var merged = new List<Vector2>();
                foreach (var sg in segs)
                {
                    if (merged.Count > 0)
                    {
                        var prev = merged[merged.Count - 1];
                        if (sg.Y - sg.X < 0.14f && !inOpen((sg.X + sg.Y) / 2, yc) && !inOpen((prev.X + prev.Y) / 2, yc) && MathF.Abs(prev.Y - sg.X) < 1e-5f)
                        { prev.Y = sg.Y; merged[merged.Count - 1] = prev; continue; }
                    }
                    merged.Add(sg);
                }
                foreach (var sg in merged)
                {
                    float s = (sg.X + sg.Y) / 2;
                    if (inOpen(s, yc)) continue;
                    float top = (c + 1) * bh;
                    if (gable != null)
                    {
                        float allowed = h + (gable.span - MathF.Abs(s - gable.sPeak)) * gable.slope;
                        if (yc > allowed) continue;
                        if (top > allowed + bh * 0.5f) top = allowed + bh * 0.5f;
                    }
                    float yy = c * bh, sh = top - yy;
                    string ty = type;
                    if (fachwerk)
                    {
                        float sm = s % 1.5f;
                        if (c == 0 || c == m - 1 || sm < 0.26f || sm > 1.24f || (c == m / 2 && sm > 0.3f && sm < 1.2f)) ty = "wood";
                    }
                    Add(ty, x0 + ux * s, y0 + yy + sh / 2, z0 + uz * s, sg.Y - sg.X, sh, t, yaw);
                }
            }
            foreach (var op in openings)
            {
                float s = (op.s0 + op.s1) / 2, w = op.s1 - op.s0, hh = op.y1 - op.y0, yc = y0 + (op.y0 + op.y1) / 2;
                if (op.kind == "window") Add("glass", x0 + ux * s, yc, z0 + uz * s, w * 0.96f, hh * 0.96f, 0.05f, yaw);
                else if (op.kind == "door") Add("door", x0 + ux * s, yc, z0 + uz * s, w * 0.95f, hh * 0.97f, 0.07f, yaw);
                else if (op.kind == "gate") Add("steel", x0 + ux * s, yc, z0 + uz * s, w * 0.97f, hh * 0.97f, 0.06f, yaw);
            }
        }

        public void Slab(float x0, float z0, float x1, float z1, float y0, float t, string type, float tile = 1f)
        {
            int nx = Math.Max(1, (int)MathF.Round((x1 - x0) / tile)), nz = Math.Max(1, (int)MathF.Round((z1 - z0) / tile));
            float sx = (x1 - x0) / nx, sz = (z1 - z0) / nz;
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) Add(type, x0 + (i + 0.5f) * sx, y0 + t / 2, z0 + (j + 0.5f) * sz, sx, t, sz);
        }

        /// <summary>Satteldach, First entlang X. side: -1, 1 oder 0 (beide Seiten). Gibt Firsthöhe zurück.</summary>
        public float GableRoof(float x0, float x1, float z0, float z1, float eaveY, float pitch, float over = 0.4f, float tile = 1f, float t = 0.14f, string type = "roof", int side = 0)
        {
            float zc = (z0 + z1) / 2, hd = (z1 - z0) / 2;
            float ridgeY = eaveY + hd * MathF.Tan(pitch);
            float Ls = (hd + over) / MathF.Cos(pitch);
            int m = Math.Max(1, (int)MathF.Round(Ls / tile));
            float tl = Ls / m;
            int nx = Math.Max(1, (int)MathF.Round((x1 - x0 + 2 * over) / tile));
            float tw = (x1 - x0 + 2 * over) / nx;
            foreach (int sd in new[] { -1, 1 })
            {
                if (side != 0 && sd != side) continue;
                for (int j = 0; j < m; j++)
                {
                    float d = (j + 0.5f) * tl;
                    float z = zc + sd * d * MathF.Cos(pitch);
                    float y = ridgeY - d * MathF.Sin(pitch) + t / 2 / MathF.Cos(pitch) + 0.05f;
                    for (int i = 0; i < nx; i++) Add(type, x0 - over + (i + 0.5f) * tw, y, z, tw, t, tl * 1.02f, 0, sd * pitch);
                }
            }
            return ridgeY;
        }

        public void Column(string type, float x, float z, float y0, float y1, float w = 0.5f)
        {
            int n = Math.Max(1, (int)MathF.Round((y1 - y0) / 0.5f));
            float h = (y1 - y0) / n;
            for (int i = 0; i < n; i++) Add(type, x, y0 + (i + 0.5f) * h, z, w, h, w);
        }

        // ---------------- Bestandsgebäude (Abriss) ----------------
        static Opening Win(float s, float y0 = 1.0f, float y1 = 2.2f, float w = 1.0f) => new Opening(s, s + w, y0, y1, "window");

        /// <summary>Zweigeschossiges Fachwerkhaus mit Satteldach.</summary>
        public static Blueprint OldHouse()
        {
            var bp = new Blueprint();
            float W = 9, D = 7.5f, t = 0.36f, hx = W / 2, hz = D / 2;
            bp.Slab(-hx - 0.1f, -hz - 0.1f, hx + 0.1f, hz + 0.1f, 0, 0.25f, "concrete", 1.15f);
            float g0 = 0.25f, fh = 2.6f;
            bp.Wall(-hx, hz - t / 2, hx, hz - t / 2, g0, fh, t, "plaster", new[] { Win(0.9f), Win(2.6f), new Opening(4.0f, 5.0f, 0, 2.15f, "door"), Win(6.4f), Win(7.9f, 1.0f, 2.2f, 0.6f) });
            bp.Wall(-hx, -hz + t / 2, hx, -hz + t / 2, g0, fh, t, "plaster", new[] { Win(1.4f), Win(4.0f), Win(6.6f) });
            bp.Wall(hx - t / 2, -hz + t, hx - t / 2, hz - t, g0, fh, t, "plaster", new[] { Win(2.9f) });
            bp.Wall(-hx + t / 2, -hz + t, -hx + t / 2, hz - t, g0, fh, t, "plaster", new[] { Win(1.5f), Win(4.3f, 1.0f, 2.2f, 0.8f) });
            float g1 = g0 + fh;
            bp.Slab(-hx, -hz, hx, hz, g1, 0.25f, "wood", 1.15f);
            float g2 = g1 + 0.25f, fh2 = 2.5f;
            bp.Wall(-hx, hz - t / 2, hx, hz - t / 2, g2, fh2, t, "plaster", new[] { Win(0.9f, 0.8f, 2.0f), Win(2.6f, 0.8f, 2.0f), Win(4.25f, 0.8f, 2.0f, 0.5f), Win(6.4f, 0.8f, 2.0f), Win(7.9f, 0.8f, 2.0f, 0.6f) }, null, 0.5f, 0.5f, true, false);
            bp.Wall(-hx, -hz + t / 2, hx, -hz + t / 2, g2, fh2, t, "plaster", new[] { Win(1.4f, 0.8f, 2.0f), Win(4.0f, 0.8f, 2.0f), Win(6.6f, 0.8f, 2.0f) }, null, 0.5f, 0.5f, true, false);
            float pitch = 0.78f, span = hz - t;
            var gable = new Gable { sPeak = (D - 2 * t) / 2, span = span + 0.25f, slope = MathF.Tan(pitch) };
            bp.Wall(hx - t / 2, -hz + t, hx - t / 2, hz - t, g2, fh2, t, "plaster", new[] { Win(2.9f, 0.8f, 2.0f), Win(3.15f, 3.0f, 3.8f, 0.5f) }, gable, 0.5f, 0.5f, true, false);
            bp.Wall(-hx + t / 2, -hz + t, -hx + t / 2, hz - t, g2, fh2, t, "plaster", new[] { Win(1.5f, 0.8f, 2.0f), Win(4.3f, 0.8f, 2.0f, 0.8f) }, gable, 0.5f, 0.5f, true, false);
            float ridge = bp.GableRoof(-hx, hx, -hz, hz, g2 + fh2, pitch, 0.35f, 1.0f);
            bp.Column("brick", 1.75f, -0.75f, g0, ridge + 0.8f, 0.5f);
            return bp;
        }

        /// <summary>Alte Ziegelgarage mit Betondecke.</summary>
        public static Blueprint OldGarage()
        {
            var bp = new Blueprint();
            float W = 3.6f, D = 6.2f, t = 0.3f, h = 2.5f, hx = W / 2, hz = D / 2;
            bp.Slab(-hx, -hz, hx, hz, 0, 0.2f, "concrete", 1.2f);
            bp.Wall(-hx, -hz + t / 2, hx, -hz + t / 2, 0.2f, h, t, "brick");
            bp.Wall(-hx + t / 2, -hz + t, -hx + t / 2, hz, 0.2f, h, t, "brick", new[] { new Opening(2.0f, 3.0f, 1.2f, 1.9f, "window") });
            bp.Wall(hx - t / 2, -hz + t, hx - t / 2, hz, 0.2f, h, t, "brick");
            bp.Wall(-hx + t, hz - t / 2, hx - t, hz - t / 2, 0.2f, h, t, "brick", new[] { new Opening(0.25f, 2.75f, 0, 2.05f, "gate") });
            bp.Slab(-hx - 0.15f, -hz - 0.15f, hx + 0.15f, hz + 0.15f, 0.2f + h, 0.2f, "concrete", 1.2f);
            return bp;
        }
    }

    /// <summary>Fertigteil-Definition: Quader relativ zum Ursprung (Mitte unten), Ziel auf der Baustelle.</summary>
    public class ElementDef
    {
        public string name; public int tier;
        public List<BoxDef> boxes = new List<BoxDef>();
        public Vector3 local;   // Lage des Ursprungs in Baustellenkoordinaten
        public Vector3 size;
        public float massT;
        public bool hasTarget;
        public Vector3 target;
        public float targetYaw;   // Grad

        public static ElementDef Make(string name, int tier, Blueprint bp)
        {
            float minx = float.MaxValue, miny = float.MaxValue, minz = float.MaxValue, maxx = float.MinValue, maxy = float.MinValue, maxz = float.MinValue, mass = 0;
            foreach (var b in bp.boxes)
            {
                var rot = b.Rot;
                var h = b.size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    var p = b.c + rot * new Vector3((i & 1) == 1 ? h.X : -h.X, (i & 2) == 2 ? h.Y : -h.Y, (i & 4) == 4 ? h.Z : -h.Z);
                    minx = Math.Min(minx, p.X); maxx = Math.Max(maxx, p.X);
                    miny = Math.Min(miny, p.Y); maxy = Math.Max(maxy, p.Y);
                    minz = Math.Min(minz, p.Z); maxz = Math.Max(maxz, p.Z);
                }
                mass += b.size.X * b.size.Y * b.size.Z * PieceType.All[b.type].dens;
            }
            var o = new Vector3((minx + maxx) / 2, miny, (minz + maxz) / 2);
            var e = new ElementDef { name = name, tier = tier, local = o, size = new Vector3(maxx - minx, maxy - miny, maxz - minz), massT = mass };
            foreach (var b in bp.boxes) e.boxes.Add(new BoxDef { type = b.type, c = b.c - o, size = b.size, yaw = b.yaw, pitch = b.pitch });
            return e;
        }
    }

    public class Design
    {
        public string key, name; public float hw, hd, slabT;
        public Func<List<ElementDef>> build;

        public static readonly Dictionary<string, Design> All = new Dictionary<string, Design>
        {
            { "garage", new Design { key = "garage", name = "Fertiggarage", hw = 1.85f, hd = 3.35f, slabT = 0.2f, build = BuildGarage } },
            { "house", new Design { key = "house", name = "Einfamilienhaus", hw = 4.15f, hd = 4.15f, slabT = 0.2f, build = BuildHouse } },
        };

        static List<ElementDef> BuildGarage()
        {
            float W = 3.5f, D = 6.5f, t = 0.2f, h = 2.6f, hx = W / 2, hz = D / 2;
            var els = new List<ElementDef>();
            var bp = new Blueprint(); bp.Wall(-hx, -hz + t / 2, hx, -hz + t / 2, 0, h, t, "prefab", null, null, 0.5f, 0.5f, false, false); els.Add(ElementDef.Make("Rückwand", 0, bp));
            bp = new Blueprint(); bp.Wall(-hx + t / 2, -hz + t, -hx + t / 2, hz, 0, h, t, "prefab", new[] { new Blueprint.Opening(2.5f, 3.5f, 1.3f, 2.0f, "window") }, null, 0.5f, 0.5f, false, false); els.Add(ElementDef.Make("Seitenwand links", 0, bp));
            bp = new Blueprint(); bp.Wall(hx - t / 2, -hz + t, hx - t / 2, hz, 0, h, t, "prefab", null, null, 0.5f, 0.5f, false, false); els.Add(ElementDef.Make("Seitenwand rechts", 0, bp));
            bp = new Blueprint(); bp.Slab(-hx - 0.1f, -hz - 0.1f, hx + 0.1f, 0, h, 0.18f, "concrete", 1.1f); els.Add(ElementDef.Make("Dachplatte hinten", 1, bp));
            bp = new Blueprint(); bp.Slab(-hx - 0.1f, 0, hx + 0.1f, hz + 0.1f, h, 0.18f, "concrete", 1.1f); els.Add(ElementDef.Make("Dachplatte vorne", 1, bp));
            bp = new Blueprint(); bp.Add("steel", 0, 1.05f, hz - 0.08f, W - 2 * t - 0.04f, 2.1f, 0.06f); bp.Add("steel", 0, 2.3f, hz - 0.08f, W - 2 * t - 0.04f, 0.3f, 0.12f); els.Add(ElementDef.Make("Sektionaltor", 2, bp));
            return els;
        }

        static List<ElementDef> BuildHouse()
        {
            float H = 4, t = 0.24f, h = 2.75f, pitch = 0.61f;
            float slope = MathF.Tan(pitch);
            var els = new List<ElementDef>();
            Func<float, float, float, float, Blueprint.Opening> win = (s0, s1, y0, y1) => new Blueprint.Opening(s0, s1, y0, y1, "window");
            float zf = H - t / 2;
            var bp = new Blueprint(); bp.Wall(-H, zf, 0, zf, 0, h, t, "prefab", new[] { new Blueprint.Opening(1.5f, 2.5f, 0, 2.15f, "door") }, null, 0.5f, 0.5f, false, false); els.Add(ElementDef.Make("Außenwand vorne links", 0, bp));
            bp = new Blueprint(); bp.Wall(0, zf, H, zf, 0, h, t, "prefab", new[] { win(1.0f, 3.0f, 0.95f, 2.2f) }, null, 0.5f, 0.5f, false, false); els.Add(ElementDef.Make("Außenwand vorne rechts", 0, bp));
            bp = new Blueprint(); bp.Wall(-H, -zf, 0, -zf, 0, h, t, "prefab", new[] { win(1.0f, 2.5f, 0.95f, 2.2f) }, null, 0.5f, 0.5f, false, false); els.Add(ElementDef.Make("Außenwand hinten links", 0, bp));
            bp = new Blueprint(); bp.Wall(0, -zf, H, -zf, 0, h, t, "prefab", new[] { win(1.5f, 3.0f, 0.3f, 2.2f) }, null, 0.5f, 0.5f, false, false); els.Add(ElementDef.Make("Außenwand hinten rechts", 0, bp));
            float zi = H - t;
            foreach (int sx in new[] { -1, 1 })
            {
                float x = sx * (H - t / 2);
                bp = new Blueprint();
                bp.Wall(x, -zi, x, 0, 0, h, t, "prefab", new[] { win(1.25f, 2.75f, 0.95f, 2.2f) }, new Blueprint.Gable { sPeak = zi, span = H, slope = slope }, 0.5f, 0.5f, false, false);
                els.Add(ElementDef.Make($"Giebelwand {(sx < 0 ? "links" : "rechts")} hinten", 0, bp));
                bp = new Blueprint();
                bp.Wall(x, 0, x, zi, 0, h, t, "prefab", new[] { win(1.0f, 2.5f, 0.95f, 2.2f), win(0.4f, 1.0f, 3.3f, 4.1f) }, new Blueprint.Gable { sPeak = 0, span = H, slope = slope }, 0.5f, 0.5f, false, false);
                els.Add(ElementDef.Make($"Giebelwand {(sx < 0 ? "links" : "rechts")} vorne", 0, bp));
            }
            foreach (int sd in new[] { -1, 1 })
            {
                bp = new Blueprint();
                bp.GableRoof(-H, H, -H, H, h, pitch, 0.45f, 1.0f, 0.14f, "roof", sd);
                els.Add(ElementDef.Make($"Dachelement {(sd < 0 ? "hinten" : "vorne")}", 1, bp));
            }
            return els;
        }
    }
}
