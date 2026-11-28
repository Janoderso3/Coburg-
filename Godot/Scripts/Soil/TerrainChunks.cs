using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

public class LayerDef
{
    public string key; public Color color; public TexKind tex; public float tile; public float smooth;
    public LayerDef(string k, string hex, TexKind t, float tile, float smooth = 0.1f) { key = k; color = Color.FromHtml(hex); tex = t; this.tile = tile; this.smooth = smooth; }
}

/// <summary>
/// Darstellung und Kollision des Bodens in 16-m-Blöcken. Geänderte Blöcke werden neu aufgebaut
/// (Mesh sofort, Kollision gedrosselt). Ein Shader mischt 8 Bodenschichten mit Normal- und Rauheitskarten.
/// </summary>
public partial class TerrainChunks : Node3D
{
    public static readonly LayerDef[] Layers =
    {
        new("grass", "#5C7A35", TexKind.Grass, 3f),
        new("topsoil", "#4A3524", TexKind.Topsoil, 2f),
        new("clay", "#9A7046", TexKind.Clay, 2.5f, 0.2f),
        new("sand", "#BCA37B", TexKind.Sand, 2f),
        new("gravel", "#9D9A92", TexKind.Gravel, 1.2f),
        new("loose", "#5E4530", TexKind.LooseSoil, 1.5f),
        new("rubble", "#8F8A84", TexKind.Rubble, 1.5f),
        new("concrete", "#7C7B78", TexKind.Concrete, 3f, 0.25f),
    };

    const int CC = 64; // Zellen je Block
    readonly SoilTerrain s;
    int nChunks;
    MeshInstance3D[] meshes;
    CollisionShape3D[] shapes;
    ArrayMesh[] arrayMeshes;
    bool[] dirtyMesh, dirtyCol;
    float colTimer;
    ShaderMaterial mat;
    public static int LayerTexSize = 512;

    public TerrainChunks(SoilTerrain soil) { s = soil; Name = "Bodenblöcke"; }
    public TerrainChunks() { }

    const string ShaderCode = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;
uniform sampler2DArray albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray rough_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tile[8];
uniform float smoothness[8];
varying vec4 w0;
varying vec4 w1;
varying vec3 wpos;
varying vec3 wnrm;
void vertex() {
    w0 = COLOR;
    w1 = CUSTOM0;
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
void fragment() {
    float ws[8] = float[8](w0.r, w0.g, w0.b, w0.a, w1.r, w1.g, w1.b, w1.a);
    vec3 alb = vec3(0.0);
    vec3 nrm = vec3(0.0);
    float rgh = 0.0;
    float tot = 0.0;
    float steep = smoothstep(0.45, 0.8, 1.0 - abs(wnrm.y));
    vec2 side = abs(wnrm.x) > abs(wnrm.z) ? wpos.zy : wpos.xy;
    for (int i = 0; i < 8; i++) {
        float w = ws[i];
        if (w < 0.004) continue;
        vec2 uv = wpos.xz / tile[i];
        vec2 uvs = side / tile[i];
        vec3 a = texture(albedo_tex, vec3(uv, float(i))).rgb;
        vec3 n = texture(normal_tex, vec3(uv, float(i))).rgb;
        float r = texture(rough_tex, vec3(uv, float(i))).r;
        if (steep > 0.01) {
            a = mix(a, texture(albedo_tex, vec3(uvs, float(i))).rgb, steep);
            n = mix(n, texture(normal_tex, vec3(uvs, float(i))).rgb, steep);
        }
        alb += a * w;
        nrm += n * w;
        rgh += mix(r, 1.0 - smoothness[i], 0.5) * w;
        tot += w;
    }
    tot = max(tot, 0.001);
    // feine Farbvariation gegen Kachelmuster
    float var_n = sin(wpos.x * 0.13) * sin(wpos.z * 0.11) * 0.05 + sin(wpos.x * 0.031 + 1.7) * sin(wpos.z * 0.027) * 0.07;
    ALBEDO = (alb / tot) * (1.0 + var_n);
    NORMAL_MAP = nrm / tot;
    NORMAL_MAP_DEPTH = 1.2;
    ROUGHNESS = clamp(rgh / tot, 0.05, 1.0);
}
";

    public void BuildAll()
    {
        nChunks = (SoilTerrain.Res - 1) / CC;
        int n = nChunks * nChunks;
        meshes = new MeshInstance3D[n]; shapes = new CollisionShape3D[n]; arrayMeshes = new ArrayMesh[n];
        dirtyMesh = new bool[n]; dirtyCol = new bool[n];
        mat = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        BuildLayerTextures();
        var body = new StaticBody3D { Name = "Bodenkollision", CollisionLayer = BauSandbox.Layers.Terrain, CollisionMask = 0 };
        body.PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.85f, Rough = false };
        AddChild(body);
        for (int c = 0; c < n; c++)
        {
            arrayMeshes[c] = new ArrayMesh();
            meshes[c] = new MeshInstance3D { Mesh = arrayMeshes[c], MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
            AddChild(meshes[c]);
            shapes[c] = new CollisionShape3D { Shape = new ConcavePolygonShape3D { BackfaceCollision = true } };
            body.AddChild(shapes[c]);
            RebuildMesh(c);
            RebuildCollision(c);
        }
    }

    void BuildLayerTextures()
    {
        int size = LayerTexSize;
        var alb = new Godot.Collections.Array<Image>();
        var nrm = new Godot.Collections.Array<Image>();
        var rgh = new Godot.Collections.Array<Image>();
        var tiles = new float[8]; var sm = new float[8];
        for (int i = 0; i < 8; i++)
        {
            var d = Layers[i];
            var r = ProcTex.Make(d.tex, d.color, size);
            var rough = new Color[r.rough.Length];
            for (int q = 0; q < rough.Length; q++) rough[q] = new Color(r.rough[q], r.rough[q], r.rough[q]);
            alb.Add(ProcTex.ToImage(r.albedo, size));
            nrm.Add(ProcTex.ToImage(ProcTex.NormalFromHeight(r.height, size, 5f * size / 256f), size));
            rgh.Add(ProcTex.ToImage(rough, size));
            tiles[i] = d.tile; sm[i] = d.smooth;
        }
        var ta = new Texture2DArray(); ta.CreateFromImages(alb);
        var tn = new Texture2DArray(); tn.CreateFromImages(nrm);
        var tr = new Texture2DArray(); tr.CreateFromImages(rgh);
        mat.SetShaderParameter("albedo_tex", ta);
        mat.SetShaderParameter("normal_tex", tn);
        mat.SetShaderParameter("rough_tex", tr);
        mat.SetShaderParameter("tile", tiles);
        mat.SetShaderParameter("smoothness", sm);
    }

    void RebuildMesh(int c)
    {
        int ci = c % nChunks, cj = c / nChunks;
        int i0 = ci * CC, j0 = cj * CC;
        int V = CC + 1;
        int R = SoilTerrain.Res;
        var verts = new Vector3[V * V];
        var nors = new Vector3[V * V];
        var cols = new Color[V * V];
        var custom = new float[V * V * 4];
        var uvs = new Vector2[V * V];
        var h = s.h;
        float cell = SoilTerrain.Cell;
        for (int j = 0; j < V; j++)
            for (int i = 0; i < V; i++)
            {
                int gi = i0 + i, gj = j0 + j;
                int k = gj * R + gi;
                int q = j * V + i;
                float x = s.Wx(gi), z = s.Wx(gj);
                verts[q] = new Vector3(x, h[k], z);
                float hl = h[gj * R + Math.Max(0, gi - 1)], hr = h[gj * R + Math.Min(R - 1, gi + 1)];
                float hd = h[Math.Max(0, gj - 1) * R + gi], hu = h[Math.Min(R - 1, gj + 1) * R + gi];
                nors[q] = new Vector3(hl - hr, 2 * cell, hd - hu).Normalized();
                var w = s.Weights(k);
                cols[q] = new Color(w[0], w[1], w[2], w[3]);
                custom[q * 4] = w[4]; custom[q * 4 + 1] = w[5]; custom[q * 4 + 2] = w[6]; custom[q * 4 + 3] = w[7];
                uvs[q] = new Vector2(x, z);
            }
        var idx = new int[CC * CC * 6];
        int p = 0;
        for (int j = 0; j < CC; j++)
            for (int i = 0; i < CC; i++)
            {
                int a = j * V + i, b = a + 1, cc = a + V, d = cc + 1;
                // Godot: Vorderseite im Uhrzeigersinn (von oben gesehen)
                if (((i0 + i + j0 + j) & 1) == 1) { idx[p++] = a; idx[p++] = b; idx[p++] = cc; idx[p++] = b; idx[p++] = d; idx[p++] = cc; }
                else { idx[p++] = a; idx[p++] = d; idx[p++] = cc; idx[p++] = a; idx[p++] = b; idx[p++] = d; }
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = nors;
        arrays[(int)Mesh.ArrayType.Color] = cols;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Custom0] = custom;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var m = arrayMeshes[c];
        m.ClearSurfaces();
        var fmt = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, fmt);
        dirtyMesh[c] = false;
    }

    void RebuildCollision(int c)
    {
        int ci = c % nChunks, cj = c / nChunks;
        int i0 = ci * CC, j0 = cj * CC;
        int R = SoilTerrain.Res;
        var faces = new Vector3[CC * CC * 6];
        int p = 0;
        var h = s.h;
        for (int j = 0; j < CC; j++)
            for (int i = 0; i < CC; i++)
            {
                int gi = i0 + i, gj = j0 + j;
                var a = new Vector3(s.Wx(gi), h[gj * R + gi], s.Wx(gj));
                var b = new Vector3(s.Wx(gi + 1), h[gj * R + gi + 1], s.Wx(gj));
                var cc = new Vector3(s.Wx(gi), h[(gj + 1) * R + gi], s.Wx(gj + 1));
                var d = new Vector3(s.Wx(gi + 1), h[(gj + 1) * R + gi + 1], s.Wx(gj + 1));
                faces[p++] = a; faces[p++] = b; faces[p++] = cc;
                faces[p++] = b; faces[p++] = d; faces[p++] = cc;
            }
        ((ConcavePolygonShape3D)shapes[c].Shape).SetFaces(faces);
        dirtyCol[c] = false;
    }

    public void MarkDirty(int i0, int j0, int i1, int j1)
    {
        int c0 = Math.Clamp(i0 / CC, 0, nChunks - 1), c1 = Math.Clamp(i1 / CC, 0, nChunks - 1);
        int r0 = Math.Clamp(j0 / CC, 0, nChunks - 1), r1 = Math.Clamp(j1 / CC, 0, nChunks - 1);
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++) { dirtyMesh[r * nChunks + c] = true; dirtyCol[r * nChunks + c] = true; }
    }

    public void Flush(float dt)
    {
        for (int c = 0; c < dirtyMesh.Length; c++) if (dirtyMesh[c]) RebuildMesh(c);
        colTimer += dt;
        if (colTimer > 0.12f)
        {
            colTimer = 0;
            for (int c = 0; c < dirtyCol.Length; c++) if (dirtyCol[c]) RebuildCollision(c);
        }
    }
}
