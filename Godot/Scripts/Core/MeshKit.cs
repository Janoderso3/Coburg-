using Godot;
using System;
using System.Collections.Generic;

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
            var nn = (p[1] - p[0]).Cross(p[2] - p[0]);
            if (nn.LengthSquared() < 1e-12f && p.Length > 3) nn = (p[2] - p[0]).Cross(p[3] - p[0]);
            nn = nn.Normalized();
            var c = Vector3.Zero;
            foreach (var q in p) c += q;
            c /= p.Length;
            bool flip = nn.Dot(c - inside) < 0;
            if (flip) nn = -nn;
            // UV-Achsen in der Ebene
            var t = MathF.Abs(nn.Y) > 0.7f ? Vector3.Right : Vector3.Up.Cross(nn).Normalized();
            var b = nn.Cross(t);
            int b0 = v.Count;
            foreach (var q in p) { v.Add(q); n.Add(nn); uv.Add(new Vector2(q.Dot(t), q.Dot(b))); }
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
            bevel = MathF.Min(bevel, MathF.Min(h.X, MathF.Min(h.Y, h.Z)) * 0.45f);
            Func<Vector3, Vector3> T = (p) => c + rot * p;
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
                            var p = Vector3.Zero;
                            p[ax] = s * h[ax]; p[a1] = u1 * h[a1]; p[a2] = u2 * h[a2];
                            pts[k++] = T(p);
                        }
                        Poly(mat, pts, c);
                    }
                return this;
            }
            // Eckpunkte mit Fase: je Ecke drei Punkte
            Func<int, int, int, int, Vector3> P = (sx, sy, sz, axis) =>
            {
                var p = new Vector3(sx * (h.X - bevel), sy * (h.Y - bevel), sz * (h.Z - bevel));
                p[axis] = new Vector3(sx * h.X, sy * h.Y, sz * h.Z)[axis];
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
            return Box(mat, c, size, Quaternion.Identity, bevel);
        }

        /// <summary>Balken von a nach b (Breite w in X, Höhe h senkrecht dazu), Oberseite zeigt grob nach "up".</summary>
        public MeshKit Beam(string mat, Vector3 a, Vector3 b, float w, float h, Vector3 up, float bevel = 0.03f)
        {
            var d = b - a;
            var rot = LookRot(d.Normalized(), up);
            return Box(mat, (a + b) * 0.5f, new Vector3(w, h, d.Length()), rot, bevel);
        }

        /// <summary>Zylinder entlang der lokalen Y-Achse (rot dreht ihn).</summary>
        public MeshKit Cyl(string mat, Vector3 c, float rTop, float rBot, float height, int seg, Quaternion rot, bool caps = true, string capMat = null)
        {
            int b0 = v.Count;
            var s = Sub(mat);
            float hh = height * 0.5f;
            float slope = (rBot - rTop) / MathF.Max(0.0001f, height);
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * MathF.PI * 2f;
                float ca = MathF.Cos(a), sa = MathF.Sin(a);
                var nn = rot * new Vector3(ca, slope, sa).Normalized();
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
                        float a = i / (float)seg * MathF.PI * 2f;
                        pts[i] = c + rot * new Vector3(MathF.Cos(a) * r, sy * hh, MathF.Sin(a) * r);
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
            return Cyl(mat, c, r, r, len, seg, Quaternion.FromEuler(new Vector3(0, 0, MathF.PI / 2)));
        }

        /// <summary>Profil in der ZY-Ebene (x=z, y=y), entlang X extrudiert (Breite w, mittig um x0).</summary>
        public MeshKit ProfileX(string mat, Vector2[] prof, float w, float x0 = 0f)
        {
            int cnt = prof.Length;
            float area = 0;
            for (int i = 0; i < cnt; i++) { var p = prof[i]; var q = prof[(i + 1) % cnt]; area += p.X * q.Y - q.X * p.Y; }
            var pr = (Vector2[])prof.Clone();
            if (area < 0) Array.Reverse(pr);
            var tri = Triangulate(pr);
            float xl = x0 - w / 2, xr = x0 + w / 2;
            var center = Vector3.Zero;
            foreach (var p in pr) center += new Vector3(x0, p.Y, p.X);
            center /= cnt;
            // Seitenwände (je Kante ein Quad, flach)
            for (int i = 0; i < cnt; i++)
            {
                var p = pr[i]; var q = pr[(i + 1) % cnt];
                var a = new Vector3(xl, p.Y, p.X); var b = new Vector3(xr, p.Y, p.X);
                var cc = new Vector3(xr, q.Y, q.X); var d = new Vector3(xl, q.Y, q.X);
                // Außennormale der Kante in ZY
                var e = q - p; var outN = new Vector3(0, -e.X, e.Y).Normalized(); // für CCW-Profil zeigt (e.y,-e.x) nach außen -> (z=e.y,y=-e.x)
                Poly(mat, new[] { a, b, cc, d }, (a + b + cc + d) * 0.25f - outN);
            }
            // Deckflächen
            foreach (float xx in new[] { xl, xr })
            {
                int b0 = v.Count;
                var nn = new Vector3(xx < x0 ? -1 : 1, 0, 0);
                foreach (var p in pr) { v.Add(new Vector3(xx, p.Y, p.X)); n.Add(nn); uv.Add(new Vector2(p.X, p.Y)); }
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
                    float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
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
            float d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
            float d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
            float d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);
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
                Vector2 tan = (j < rows - 1 ? prof[j + 1] - prof[j] : prof[j] - prof[j - 1]).Normalized();
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * MathF.PI * 2f;
                    float ca = MathF.Cos(a), sa = MathF.Sin(a);
                    var nn = rot * new Vector3(ca * tan.Y, -tan.X, sa * tan.Y).Normalized();
                    v.Add(c + rot * new Vector3(ca * prof[j].X, prof[j].Y, sa * prof[j].X));
                    n.Add(nn);
                    uv.Add(new Vector2(a * 1.0f, prof[j].Y));
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
                float a = -MathF.PI / 2 + MathF.PI * i / (seg / 2);
                prof[i] = new Vector2(MathF.Max(0.0001f, MathF.Cos(a) * r), MathF.Sin(a) * r);
            }
            return Lathe(mat, prof, seg, c, Quaternion.Identity);
        }

        /// <summary>Kettenband (Stadionform) in der ZY-Ebene, U läuft entlang des Bandes in Metern.</summary>
        public MeshKit TrackBelt(string mat, float x, float y, float len, float rad, float width, int segs = 10)
        {
            var pts = new List<Vector4>();
            float half = len / 2 - rad;
            pts.Add(new Vector4(-half, -rad, 0, -1));
            pts.Add(new Vector4(half, -rad, 0, -1));
            for (int i = 1; i < segs; i++) { float a = -MathF.PI / 2 + MathF.PI * i / segs; pts.Add(new Vector4(half + MathF.Cos(a) * rad, MathF.Sin(a) * rad, MathF.Cos(a), MathF.Sin(a))); }
            pts.Add(new Vector4(half, rad, 0, 1));
            pts.Add(new Vector4(-half, rad, 0, 1));
            for (int i = 1; i < segs; i++) { float a = MathF.PI / 2 + MathF.PI * i / segs; pts.Add(new Vector4(-half + MathF.Cos(a) * rad, MathF.Sin(a) * rad, MathF.Cos(a), MathF.Sin(a))); }
            pts.Add(new Vector4(-half, -rad, 0, -1));
            int b0 = v.Count;
            float sLen = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) sLen += new Vector2(pts[i].X, pts[i].Y).DistanceTo(new Vector2(pts[i - 1].X, pts[i - 1].Y));
                foreach (float xx in new[] { x - width / 2, x + width / 2 })
                {
                    v.Add(new Vector3(xx, y + pts[i].Y, pts[i].X));
                    n.Add(new Vector3(0, pts[i].W, pts[i].Z));
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

        /// <summary>Erzeugt ein ArrayMesh mit einer Fläche je Material (Godot: Vorderseite im Uhrzeigersinn -> Dreiecke umgedreht).</summary>
        public ArrayMesh BuildMesh()
        {
            var mesh = new ArrayMesh();
            foreach (var key in order)
            {
                var idx = subs[key];
                if (idx.Count == 0) continue;
                var st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                var map = new Dictionary<int, int>();
                int next = 0;
                var outIdx = new List<int>(idx.Count);
                foreach (int i in idx)
                {
                    if (!map.TryGetValue(i, out int ni))
                    {
                        ni = next++;
                        map[i] = ni;
                        st.SetNormal(n[i]);
                        st.SetUV(uv[i]);
                        st.AddVertex(v[i]);
                    }
                    outIdx.Add(ni);
                }
                for (int t = 0; t < outIdx.Count; t += 3)
                {
                    st.AddIndex(outIdx[t]);
                    st.AddIndex(outIdx[t + 2]);
                    st.AddIndex(outIdx[t + 1]);
                }
                st.GenerateTangents();
                st.Commit(mesh);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, Mats.Get(key));
            }
            return mesh;
        }

        public MeshInstance3D Build(Node3D parent, string name, bool shadows = true)
        {
            var mi = new MeshInstance3D { Name = name };
            parent.AddChild(mi);
            if (v.Count == 0) return mi;
            mi.Mesh = BuildMesh();
            mi.CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
            Clear();
            return mi;
        }

        public static Quaternion LookRot(Vector3 fwd, Vector3 up)
        {
            // Basis mit Z = fwd (Vorwärts = +Z, wie im restlichen Code)
            var z = fwd.Normalized();
            var x = up.Cross(z);
            if (x.LengthSquared() < 1e-8f) x = Vector3.Right.Cross(z);
            x = x.Normalized();
            var y = z.Cross(x);
            return new Basis(x, y, z).GetRotationQuaternion();
        }

        public void Clear()
        {
            v.Clear(); n.Clear(); uv.Clear(); order.Clear(); subs.Clear();
        }
    }
}
