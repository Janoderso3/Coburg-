using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>
    /// Baukasten für prozedurale Modelle: Quader mit Fase, Zylinder, Profile, Drehkörper.
    /// Sammelt Geometrie je Material und erzeugt ein Mesh mit Submeshes (wenige Draw Calls).
    /// UVs sind in Metern; die Materialkachelung skaliert sie.
    /// </summary>
    public class MeshKit
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<string> order = new List<string>();
        readonly Dictionary<string, List<int>> subs = new Dictionary<string, List<int>>();

        public int VertexCount => v.Count;

        List<int> Sub(string mat)
        {
            if (!subs.TryGetValue(mat, out var l)) { l = new List<int>(); subs[mat] = l; order.Add(mat); }
            return l;
        }

        /// <summary>Konvexes Polygon (flach schattiert), Wicklung nach außen relativ zu "inside".</summary>
        public void Poly(string mat, Vector3[] p, Vector3 inside)
        {
            var nn = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            if (nn.sqrMagnitude < 1e-12f && p.Length > 3) nn = Vector3.Cross(p[2] - p[0], p[3] - p[0]);
            nn.Normalize();
            var c = Vector3.zero;
            foreach (var q in p) c += q;
            c /= p.Length;
            bool flip = Vector3.Dot(nn, c - inside) < 0;
            if (flip) nn = -nn;
            // UV-Achsen in der Ebene
            var t = Mathf.Abs(nn.y) > 0.7f ? Vector3.right : Vector3.Cross(Vector3.up, nn).normalized;
            var b = Vector3.Cross(nn, t);
            int b0 = v.Count;
            foreach (var q in p) { v.Add(q); n.Add(nn); uv.Add(new Vector2(Vector3.Dot(q, t), Vector3.Dot(q, b))); }
            var s = Sub(mat);
            for (int i = 1; i < p.Length - 1; i++)
            {
                // Unity: Vorderseite im Uhrzeigersinn, Normale = Cross(b-a, c-a)
                if (flip) { s.Add(b0); s.Add(b0 + i + 1); s.Add(b0 + i); }
                else { s.Add(b0); s.Add(b0 + i); s.Add(b0 + i + 1); }
            }
        }

        /// <summary>Quader mit optionaler Fase (bevel in Metern).</summary>
        public MeshKit Box(string mat, Vector3 c, Vector3 size, Quaternion rot, float bevel = 0f)
        {
            var h = size * 0.5f;
            bevel = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.45f);
            System.Func<Vector3, Vector3> T = (p) => c + rot * p;
            if (bevel <= 0.0001f)
            {
                for (int ax = 0; ax < 3; ax++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var pts = new Vector3[4];
                        int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
                        int k = 0;
                        foreach (var (u1, u2) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                        {
                            var p = Vector3.zero;
                            p[ax] = s * h[ax]; p[a1] = u1 * h[a1]; p[a2] = u2 * h[a2];
                            pts[k++] = T(p);
                        }
                        Poly(mat, pts, c);
                    }
                return this;
            }
            // Eckpunkte mit Fase: je Ecke drei Punkte
            System.Func<int, int, int, int, Vector3> P = (sx, sy, sz, axis) =>
            {
                var p = new Vector3(sx * (h.x - bevel), sy * (h.y - bevel), sz * (h.z - bevel));
                p[axis] = new Vector3(sx * h.x, sy * h.y, sz * h.z)[axis];
                return T(p);
            };
            int[] sg = { -1, 1 };
            // Hauptflächen
            for (int ax = 0; ax < 3; ax++)
                foreach (int s in sg)
                {
                    int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
                    var pts = new List<Vector3>();
                    foreach (var (u1, u2) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                    {
                        var sgn = new int[3]; sgn[ax] = s; sgn[a1] = u1; sgn[a2] = u2;
                        pts.Add(P(sgn[0], sgn[1], sgn[2], ax));
                    }
                    Poly(mat, pts.ToArray(), c);
                }
            // Kantenfasen
            for (int ax = 0; ax < 3; ax++)
            {
                int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3; // Kante verläuft entlang ax
                foreach (int s1 in sg)
                    foreach (int s2 in sg)
                    {
                        var g0 = new int[3]; g0[ax] = -1; g0[a1] = s1; g0[a2] = s2;
                        var g1 = new int[3]; g1[ax] = 1; g1[a1] = s1; g1[a2] = s2;
                        Poly(mat, new[] { P(g0[0], g0[1], g0[2], a1), P(g1[0], g1[1], g1[2], a1), P(g1[0], g1[1], g1[2], a2), P(g0[0], g0[1], g0[2], a2) }, c);
                    }
            }
            // Eckdreiecke
            foreach (int sx in sg) foreach (int sy in sg) foreach (int sz in sg)
                        Poly(mat, new[] { P(sx, sy, sz, 0), P(sx, sy, sz, 1), P(sx, sy, sz, 2) }, c);
            return this;
        }

        public MeshKit Box(string mat, Vector3 c, Vector3 size, float bevel = 0f)
        {
            return Box(mat, c, size, Quaternion.identity, bevel);
        }

        /// <summary>Balken von a nach b (Breite w in X, Höhe h senkrecht dazu), Oberseite zeigt grob nach "up".</summary>
        public MeshKit Beam(string mat, Vector3 a, Vector3 b, float w, float h, Vector3 up, float bevel = 0.03f)
        {
            var d = b - a;
            var rot = Quaternion.LookRotation(d.normalized, up);
            return Box(mat, (a + b) * 0.5f, new Vector3(w, h, d.magnitude), rot, bevel);
        }

        /// <summary>Zylinder entlang der lokalen Y-Achse (rot dreht ihn).</summary>
        public MeshKit Cyl(string mat, Vector3 c, float rTop, float rBot, float height, int seg, Quaternion rot, bool caps = true, string capMat = null)
        {
            int b0 = v.Count;
            var s = Sub(mat);
            float hh = height * 0.5f;
            float slope = (rBot - rTop) / Mathf.Max(0.0001f, height);
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                var nn = rot * new Vector3(ca, slope, sa).normalized;
                v.Add(c + rot * new Vector3(ca * rBot, -hh, sa * rBot)); n.Add(nn); uv.Add(new Vector2(a * (rBot + rTop) * 0.5f, 0));
                v.Add(c + rot * new Vector3(ca * rTop, hh, sa * rTop)); n.Add(nn); uv.Add(new Vector2(a * (rBot + rTop) * 0.5f, height));
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = b0 + i * 2;
                s.Add(a0); s.Add(a0 + 1); s.Add(a0 + 2);
                s.Add(a0 + 2); s.Add(a0 + 1); s.Add(a0 + 3);
            }
            if (caps)
            {
                string cm = capMat ?? mat;
                foreach (int sy in new[] { -1, 1 })
                {
                    float r = sy > 0 ? rTop : rBot;
                    if (r < 0.001f) continue;
                    var pts = new Vector3[seg];
                    for (int i = 0; i < seg; i++)
                    {
                        float a = i / (float)seg * Mathf.PI * 2f;
                        pts[i] = c + rot * new Vector3(Mathf.Cos(a) * r, sy * hh, Mathf.Sin(a) * r);
                    }
                    Poly(cm, pts, c);
                }
            }
            return this;
        }

        public MeshKit Cyl(string mat, Vector3 c, float r, float height, int seg, Quaternion rot)
        {
            return Cyl(mat, c, r, r, height, seg, rot);
        }

        /// <summary>Zylinder entlang X (Achsen, Bolzen, Räder).</summary>
        public MeshKit CylX(string mat, Vector3 c, float r, float len, int seg = 16)
        {
            return Cyl(mat, c, r, r, len, seg, Quaternion.Euler(0, 0, 90));
        }

        /// <summary>Profil in der ZY-Ebene (x=z, y=y), entlang X extrudiert (Breite w, mittig um x0).</summary>
        public MeshKit ProfileX(string mat, Vector2[] prof, float w, float x0 = 0f)
        {
            int cnt = prof.Length;
            float area = 0;
            for (int i = 0; i < cnt; i++) { var p = prof[i]; var q = prof[(i + 1) % cnt]; area += p.x * q.y - q.x * p.y; }
            var pr = (Vector2[])prof.Clone();
            if (area < 0) System.Array.Reverse(pr);
            var tri = Triangulate(pr);
            float xl = x0 - w / 2, xr = x0 + w / 2;
            var center = Vector3.zero;
            foreach (var p in pr) center += new Vector3(x0, p.y, p.x);
            center /= cnt;
            // Seitenwände (je Kante ein Quad, flach)
            for (int i = 0; i < cnt; i++)
            {
                var p = pr[i]; var q = pr[(i + 1) % cnt];
                var a = new Vector3(xl, p.y, p.x); var b = new Vector3(xr, p.y, p.x);
                var cc = new Vector3(xr, q.y, q.x); var d = new Vector3(xl, q.y, q.x);
                // Außennormale der Kante in ZY
                var e = q - p; var outN = new Vector3(0, -e.x, e.y).normalized; // für CCW-Profil zeigt (e.y,-e.x) nach außen -> (z=e.y,y=-e.x)
                Poly(mat, new[] { a, b, cc, d }, (a + b + cc + d) * 0.25f - outN);
            }
            // Deckflächen
            foreach (float xx in new[] { xl, xr })
            {
                int b0 = v.Count;
                var nn = new Vector3(xx < x0 ? -1 : 1, 0, 0);
                foreach (var p in pr) { v.Add(new Vector3(xx, p.y, p.x)); n.Add(nn); uv.Add(new Vector2(p.x, p.y)); }
                var s = Sub(mat);
                for (int i = 0; i < tri.Count; i += 3)
                {
                    // CCW im (z,y)-System von +X aus gesehen: Normale -X -> umdrehen
                    if (xx > x0) { s.Add(b0 + tri[i]); s.Add(b0 + tri[i + 2]); s.Add(b0 + tri[i + 1]); }
                    else { s.Add(b0 + tri[i]); s.Add(b0 + tri[i + 1]); s.Add(b0 + tri[i + 2]); }
                }
            }
            return this;
        }

        static List<int> Triangulate(Vector2[] p)
        {
            var res = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < p.Length; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 1000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    var a = p[ia]; var b = p[ib]; var c = p[ic];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (cross <= 1e-7f) continue;
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        if (PointInTri(p[j], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    res.Add(ia); res.Add(ib); res.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { res.Add(idx[0]); res.Add(idx[1]); res.Add(idx[2]); }
            return res;
        }
        static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = (d1 < 0) || (d2 < 0) || (d3 < 0), pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(neg && pos);
        }

        /// <summary>Drehkörper um die lokale Y-Achse. prof: (Radius, Höhe).</summary>
        public MeshKit Lathe(string mat, Vector2[] prof, int seg, Vector3 c, Quaternion rot)
        {
            int b0 = v.Count;
            var s = Sub(mat);
            int rows = prof.Length;
            for (int j = 0; j < rows; j++)
            {
                Vector2 tan = (j < rows - 1 ? prof[j + 1] - prof[j] : prof[j] - prof[j - 1]).normalized;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    var nn = rot * new Vector3(ca * tan.y, -tan.x, sa * tan.y).normalized;
                    v.Add(c + rot * new Vector3(ca * prof[j].x, prof[j].y, sa * prof[j].x));
                    n.Add(nn);
                    uv.Add(new Vector2(a * 1.0f, prof[j].y));
                }
            }
            int w = seg + 1;
            for (int j = 0; j < rows - 1; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = b0 + j * w + i;
                    s.Add(a0); s.Add(a0 + w); s.Add(a0 + 1);
                    s.Add(a0 + 1); s.Add(a0 + w); s.Add(a0 + w + 1);
                }
            return this;
        }

        public MeshKit Sphere(string mat, Vector3 c, float r, int seg = 16)
        {
            var prof = new Vector2[seg / 2 + 1];
            for (int i = 0; i <= seg / 2; i++)
            {
                float a = -Mathf.PI / 2 + Mathf.PI * i / (seg / 2);
                prof[i] = new Vector2(Mathf.Max(0.0001f, Mathf.Cos(a) * r), Mathf.Sin(a) * r);
            }
            return Lathe(mat, prof, seg, c, Quaternion.identity);
        }

        /// <summary>Kettenband (Stadionform) in der ZY-Ebene, U läuft entlang des Bandes in Metern.</summary>
        public MeshKit TrackBelt(string mat, float x, float y, float len, float rad, float width, int segs = 10)
        {
            var pts = new List<Vector4>();
            float half = len / 2 - rad;
            pts.Add(new Vector4(-half, -rad, 0, -1));
            pts.Add(new Vector4(half, -rad, 0, -1));
            for (int i = 1; i < segs; i++) { float a = -Mathf.PI / 2 + Mathf.PI * i / segs; pts.Add(new Vector4(half + Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, Mathf.Cos(a), Mathf.Sin(a))); }
            pts.Add(new Vector4(half, rad, 0, 1));
            pts.Add(new Vector4(-half, rad, 0, 1));
            for (int i = 1; i < segs; i++) { float a = Mathf.PI / 2 + Mathf.PI * i / segs; pts.Add(new Vector4(-half + Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, Mathf.Cos(a), Mathf.Sin(a))); }
            pts.Add(new Vector4(-half, -rad, 0, -1));
            int b0 = v.Count;
            float sLen = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) sLen += Vector2.Distance(new Vector2(pts[i].x, pts[i].y), new Vector2(pts[i - 1].x, pts[i - 1].y));
                foreach (float xx in new[] { x - width / 2, x + width / 2 })
                {
                    v.Add(new Vector3(xx, y + pts[i].y, pts[i].x));
                    n.Add(new Vector3(0, pts[i].w, pts[i].z));
                    uv.Add(new Vector2(sLen, xx > x ? 1 : 0));
                }
            }
            var s = Sub(mat);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int a = b0 + i * 2;
                s.Add(a); s.Add(a + 1); s.Add(a + 2);
                s.Add(a + 1); s.Add(a + 3); s.Add(a + 2);
            }
            return this;
        }

        public Mesh BuildMesh(out Material[] mats)
        {
            var m = new Mesh();
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.subMeshCount = order.Count;
            mats = new Material[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                m.SetTriangles(subs[order[i]], i, false);
                mats[i] = Mats.Get(order[i]);
            }
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        public GameObject Build(Transform parent, string name, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (v.Count == 0) return go;
            var mesh = BuildMesh(out var mats);
            mesh.name = name;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            Clear();
            return go;
        }

        public void Clear()
        {
            v.Clear(); n.Clear(); uv.Clear(); order.Clear(); subs.Clear();
        }
    }
}
