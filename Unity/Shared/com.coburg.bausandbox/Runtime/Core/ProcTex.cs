using UnityEngine;

namespace BauSandbox
{
    public enum TexKind { None, Paint, Grime, Brick, Plaster, Roof, Wood, Concrete, Tread, Track, Grass, Topsoil, Clay, Sand, Gravel, LooseSoil, Rubble, Asphalt, Leaves, Bark, Steel, Glass }

    /// <summary>
    /// Prozedurale, kachelbare PBR-Texturen (Albedo + Höhe -> Normalmap).
    /// Wird zur Laufzeit als Rückfall genutzt und vom Editor-Setup als PNG gespeichert.
    /// </summary>
    public static class ProcTex
    {
        public struct Result
        {
            public Color[] albedo;
            public float[] height;
            public float[] rough; // 0..1 Rauheit
            public int size;
        }

        static float N(float x, float y, int p) { return U.TileNoise(x, y, p); }
        static float F(float u, float v, int baseP, int oct)
        {
            float s = 0, a = 0.5f; int p = baseP;
            for (int i = 0; i < oct; i++) { s += a * N(u * p, v * p, p); p *= 2; a *= 0.5f; }
            return s;
        }
        static float Hash(int x, int y, int s)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        public static Result Make(TexKind k, Color baseCol, int size)
        {
            var r = new Result { size = size, albedo = new Color[size * size], height = new float[size * size], rough = new float[size * size] };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    int i = y * size + x;
                    Color c = baseCol; float h = 0.5f; float ro = 0.6f;
                    switch (k)
                    {
                        case TexKind.Paint:
                            {
                                float n = F(u, v, 4, 4);
                                float spot = Mathf.Clamp01(F(u + 0.3f, v, 8, 3) * 3f - 0.8f);
                                c = baseCol * (0.94f + n * 0.08f);
                                c = Color.Lerp(c, new Color(0.25f, 0.2f, 0.15f), spot * 0.25f);
                                h = 0.5f + n * 0.05f; ro = 0.45f + spot * 0.3f;
                                break;
                            }
                        case TexKind.Grime:
                            {
                                float n = F(u, v, 4, 5);
                                float dirt = Mathf.Clamp01(F(u, v + 0.5f, 6, 4) * 2.5f + 0.2f);
                                c = Color.Lerp(baseCol * (0.9f + n * 0.15f), new Color(0.3f, 0.24f, 0.17f), dirt * 0.45f);
                                h = 0.5f + n * 0.2f; ro = 0.6f + dirt * 0.3f;
                                break;
                            }
                        case TexKind.Steel:
                            {
                                float n = F(u, v * 0.2f, 8, 4);
                                c = baseCol * (0.9f + n * 0.15f); h = 0.5f + n * 0.05f; ro = 0.35f + n * 0.1f;
                                break;
                            }
                        case TexKind.Brick:
                            {
                                // 1 Kachel = 4 Steine breit, 12 Reihen (ca. 1 m)
                                float rows = 12f, cols = 4f;
                                float ry = v * rows; int row = Mathf.FloorToInt(ry);
                                float rx = u * cols + (row % 2) * 0.5f; int col = Mathf.FloorToInt(rx);
                                float fy = ry - row, fx = rx - col;
                                float mortar = Mathf.Min(Mathf.Min(fy, 1 - fy) * 8f, Mathf.Min(fx, 1 - fx) * 24f);
                                float m = Mathf.Clamp01(mortar);
                                float tint = Hash(col, row, 3);
                                Color brick = baseCol * (0.8f + tint * 0.35f);
                                brick = Color.Lerp(brick, new Color(0.35f, 0.15f, 0.1f), Hash(col, row, 9) < 0.15f ? 0.4f : 0f);
                                float n = F(u, v, 16, 3);
                                c = Color.Lerp(new Color(0.62f, 0.6f, 0.56f), brick * (0.92f + n * 0.12f), m);
                                h = m * 0.8f + n * 0.1f; ro = Mathf.Lerp(0.95f, 0.75f, m);
                                break;
                            }
                        case TexKind.Plaster:
                            {
                                float n = F(u, v, 8, 5);
                                float n2 = F(u + 0.4f, v + 0.1f, 32, 2);
                                float stain = Mathf.Clamp01(F(u, v, 2, 3) * 2f);
                                c = baseCol * (0.93f + n * 0.08f + n2 * 0.05f);
                                c = Color.Lerp(c, new Color(0.55f, 0.52f, 0.45f), stain * 0.25f);
                                h = 0.5f + n * 0.3f + n2 * 0.25f; ro = 0.85f;
                                break;
                            }
                        case TexKind.Roof:
                            {
                                float rows = 6f, cols = 5f;
                                float ry = v * rows; int row = Mathf.FloorToInt(ry);
                                float rx = u * cols + (row % 2) * 0.5f; int col = Mathf.FloorToInt(rx);
                                float fy = ry - row, fx = rx - col;
                                float curve = Mathf.Sin(fx * Mathf.PI);
                                float overlap = Mathf.Clamp01(fy * 5f);
                                float tint = Hash(col, row, 5);
                                c = baseCol * (0.7f + 0.3f * curve) * (0.85f + tint * 0.25f) * (0.6f + 0.4f * overlap);
                                c = Color.Lerp(c, new Color(0.2f, 0.22f, 0.15f), Mathf.Clamp01(F(u, v, 4, 3) * 2f) * 0.25f);
                                h = curve * 0.6f + fy * 0.4f; ro = 0.7f;
                                break;
                            }
                        case TexKind.Wood:
                            {
                                float g = Mathf.Sin((u * 40f + F(u, v, 4, 3) * 6f) * Mathf.PI) * 0.5f + 0.5f;
                                float n = F(u, v, 16, 2);
                                c = baseCol * (0.75f + g * 0.3f + n * 0.1f);
                                h = 0.5f + g * 0.2f; ro = 0.8f;
                                break;
                            }
                        case TexKind.Concrete:
                            {
                                float n = F(u, v, 8, 5);
                                float pores = Hash(x, y, 7) > 0.985f ? -0.4f : 0f;
                                float seam = (Mathf.Abs(v * 2f - Mathf.Round(v * 2f)) < 0.006f) ? -0.15f : 0f;
                                c = baseCol * (0.92f + n * 0.14f + pores * 0.5f + seam);
                                h = 0.5f + n * 0.2f + pores; ro = 0.9f;
                                break;
                            }
                        case TexKind.Tread:
                            {
                                float rx = u * 6f; float fx = rx - Mathf.Floor(rx);
                                float chevron = Mathf.Abs(fx - 0.5f + Mathf.Abs(v - 0.5f) * 0.6f);
                                float lug = chevron < 0.22f ? 1f : 0f;
                                c = baseCol * (lug > 0 ? 1.0f : 0.55f);
                                h = lug; ro = 0.95f;
                                break;
                            }
                        case TexKind.Track:
                            {
                                float fx = u * 1f; fx -= Mathf.Floor(fx);
                                float bar = fx < 0.22f ? 1f : fx < 0.3f ? 0.6f : 0.25f;
                                float n = F(u, v, 8, 3);
                                c = baseCol * (0.6f + bar * 0.5f + n * 0.1f);
                                c = Color.Lerp(c, new Color(0.35f, 0.27f, 0.18f), Mathf.Clamp01(n * 2f + 0.3f) * 0.35f);
                                h = bar; ro = 0.6f;
                                break;
                            }
                        case TexKind.Grass:
                            {
                                float n = F(u, v, 4, 5);
                                float blades = Hash(x, y, 1);
                                float dry = Mathf.Clamp01(F(u + 0.2f, v, 3, 3) * 2f);
                                c = Color.Lerp(new Color(0.24f, 0.36f, 0.12f), new Color(0.36f, 0.45f, 0.17f), n * 0.5f + 0.5f);
                                c = Color.Lerp(c, new Color(0.52f, 0.5f, 0.3f), dry * 0.45f);
                                c *= 0.8f + blades * 0.35f;
                                h = blades * 0.6f + n * 0.3f; ro = 0.9f;
                                break;
                            }
                        case TexKind.Topsoil:
                        case TexKind.LooseSoil:
                        case TexKind.Clay:
                        case TexKind.Sand:
                            {
                                float n = F(u, v, 6, 5);
                                float grain = Hash(x, y, 2);
                                float clump = Mathf.Clamp01(F(u, v, 12, 3) * 2.5f);
                                float stones = Hash(x / 3, y / 3, 4) > 0.97f ? 1f : 0f;
                                c = baseCol * (0.88f + n * 0.18f + (grain - 0.5f) * (k == TexKind.Sand ? 0.18f : 0.1f));
                                if (k == TexKind.LooseSoil) c *= 0.9f + clump * 0.2f;
                                c = Color.Lerp(c, new Color(0.55f, 0.52f, 0.47f), stones * 0.6f);
                                h = 0.4f + n * 0.25f + clump * (k == TexKind.LooseSoil ? 0.45f : 0.15f) + stones * 0.3f;
                                ro = k == TexKind.Clay ? 0.75f : 0.92f;
                                break;
                            }
                        case TexKind.Gravel:
                        case TexKind.Rubble:
                            {
                                // Kieselsteine: Voronoi-artig
                                float cells = k == TexKind.Gravel ? 22f : 10f;
                                float px = u * cells, py = v * cells;
                                int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
                                float best = 9f; int bx = 0, by = 0;
                                for (int oy = -1; oy <= 1; oy++)
                                    for (int ox = -1; ox <= 1; ox++)
                                    {
                                        int gx = cx + ox, gy = cy + oy;
                                        int wx = ((gx % (int)cells) + (int)cells) % (int)cells, wy = ((gy % (int)cells) + (int)cells) % (int)cells;
                                        float sx = gx + Hash(wx, wy, 11), sy = gy + Hash(wx, wy, 12);
                                        float d = (px - sx) * (px - sx) + (py - sy) * (py - sy);
                                        if (d < best) { best = d; bx = wx; by = wy; }
                                    }
                                float stone = Mathf.Clamp01(1f - Mathf.Sqrt(best) * 1.3f);
                                float tint = Hash(bx, by, 13);
                                Color sc = baseCol * (0.75f + tint * 0.45f);
                                if (k == TexKind.Rubble && tint > 0.7f) sc = Color.Lerp(sc, new Color(0.6f, 0.3f, 0.22f), 0.6f);
                                c = Color.Lerp(baseCol * 0.45f, sc, Mathf.Clamp01(stone * 3f));
                                h = stone; ro = 0.85f;
                                break;
                            }
                        case TexKind.Asphalt:
                            {
                                float n = F(u, v, 16, 4);
                                float g = Hash(x, y, 21);
                                c = baseCol * (0.85f + n * 0.15f + (g > 0.9f ? 0.25f : 0f));
                                h = 0.5f + n * 0.2f + (g > 0.9f ? 0.2f : 0f); ro = 0.85f;
                                break;
                            }
                        case TexKind.Leaves:
                            {
                                float n = F(u, v, 8, 4);
                                float lf = Hash(x / 2, y / 2, 31);
                                c = baseCol * (0.6f + lf * 0.6f + n * 0.2f);
                                h = lf; ro = 0.8f;
                                break;
                            }
                        case TexKind.Bark:
                            {
                                float g = Mathf.Abs(Mathf.Sin((u * 14f + F(u, v, 4, 3) * 4f) * Mathf.PI));
                                c = baseCol * (0.6f + g * 0.5f);
                                h = g; ro = 0.95f;
                                break;
                            }
                        case TexKind.Glass:
                            {
                                c = baseCol; h = 0.5f; ro = 0.05f;
                                break;
                            }
                        default:
                            c = baseCol; h = 0.5f; ro = 0.6f;
                            break;
                    }
                    c.a = baseCol.a;
                    r.albedo[i] = c;
                    r.height[i] = h;
                    r.rough[i] = ro;
                }
            }
            return r;
        }

        public static Color[] NormalFromHeight(float[] h, int size, float strength)
        {
            var n = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int xl = (x - 1 + size) % size, xr = (x + 1) % size, yd = (y - 1 + size) % size, yu = (y + 1) % size;
                    float dx = (h[y * size + xr] - h[y * size + xl]) * strength;
                    float dy = (h[yu * size + x] - h[yd * size + x]) * strength;
                    var v = new Vector3(-dx, -dy, 1f).normalized;
                    n[y * size + x] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1f);
                }
            return n;
        }

        public static Texture2D ToTexture(Color[] px, int size, bool linear, string name)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, filterMode = FilterMode.Trilinear };
            t.SetPixels(px);
            t.Apply(true, false);
            return t;
        }

        /// <summary>Maskenkarte für HDRP (R=Metallic, G=AO, B=Detail, A=Glätte).</summary>
        public static Color[] MaskMap(Result r, float metallic)
        {
            var m = new Color[r.size * r.size];
            for (int i = 0; i < m.Length; i++)
            {
                float ao = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(r.height[i] * 1.4f));
                m[i] = new Color(metallic, ao, 0f, 1f - r.rough[i]);
            }
            return m;
        }
    }
}
