using Godot;
using System.Collections.Generic;

namespace BauSandbox;

public enum TexKind { None, Paint, Grime, Brick, Plaster, Roof, Wood, Concrete, Tread, Track, Grass, Topsoil, Clay, Sand, Gravel, LooseSoil, Rubble, Asphalt, Leaves, Bark, Steel, Glass }

public class MatDef
{
    public string key; public Color color; public float metallic, smooth; public TexKind tex; public float tile = 1f;
    public bool transparent; public Color emission = Colors.Black; public float emissionEnergy; public float normalStrength = 4f;
    public bool unshaded, doubleSided;
    public MatDef(string k, Color c, float m, float s, TexKind t, float tile = 1f) { key = k; color = c; metallic = m; smooth = s; tex = t; this.tile = tile; }
}

/// <summary>Materialbibliothek: PBR-Materialien mit prozeduralen Texturen (Albedo, Normal, Rauheit).</summary>
public static class Mats
{
    static Color H(string h, float a = 1f) => U.Hex(h, a);

    public static readonly List<MatDef> All = new()
    {
        new("paint_yellow", H("#E3A614"), 0.15f, 0.55f, TexKind.Grime, 2f),
        new("paint_yellow_worn", H("#C99A2A"), 0.2f, 0.35f, TexKind.Grime, 1f),
        new("paint_orange", H("#DB6A1C"), 0.15f, 0.5f, TexKind.Grime, 2f),
        new("paint_white", H("#E6E6E1"), 0.1f, 0.55f, TexKind.Grime, 2f),
        new("paint_blue", H("#2A5A9C"), 0.15f, 0.55f, TexKind.Paint, 2f),
        new("paint_red", H("#B3261E"), 0.15f, 0.5f, TexKind.Grime, 2f),
        new("dark_metal", H("#2D2D2D"), 0.5f, 0.35f, TexKind.Grime, 1.5f),
        new("black_plastic", H("#161616"), 0f, 0.3f, TexKind.Paint, 1f),
        new("steel", H("#8D8F91"), 0.85f, 0.55f, TexKind.Steel, 1f),
        new("chrome", H("#D7DBDF"), 1f, 0.9f, TexKind.None, 1f),
        new("rubber", H("#1B1B1B"), 0f, 0.15f, TexKind.Paint, 1f),
        new("tread", H("#202020"), 0f, 0.12f, TexKind.Tread, 1f),
        new("track", H("#3A3836"), 0.6f, 0.3f, TexKind.Track, 0.22f),
        new("bucket_wear", H("#5A5650"), 0.7f, 0.4f, TexKind.Steel, 1f),
        new("drum", H("#E8E8E2"), 0.3f, 0.6f, TexKind.Grime, 2f),
        new("glass", H("#20303A", 0.35f), 0.2f, 0.95f, TexKind.None, 1f) { transparent = true },
        new("headlight", H("#FFF6E0"), 0f, 0.9f, TexKind.None, 1f) { emission = H("#FFF1C8"), emissionEnergy = 4f },
        new("taillight", H("#550000"), 0f, 0.8f, TexKind.None, 1f) { emission = H("#FF2010"), emissionEnergy = 2f },
        new("beacon", H("#FF8A00"), 0f, 0.8f, TexKind.None, 1f) { emission = H("#FF7A00"), emissionEnergy = 1f },
        new("seat", H("#202224"), 0f, 0.25f, TexKind.Paint, 0.5f),
        new("plaster", H("#E3D6BD"), 0f, 0.15f, TexKind.Plaster, 2f),
        new("plaster_white", H("#EFEBE3"), 0f, 0.15f, TexKind.Plaster, 2f),
        new("brick", H("#B2624A"), 0f, 0.2f, TexKind.Brick, 1f),
        new("timber", H("#4E3320"), 0f, 0.25f, TexKind.Wood, 1f),
        new("roof_tile", H("#A3442F"), 0f, 0.3f, TexKind.Roof, 1.2f),
        new("prefab", H("#DCD7CC"), 0f, 0.2f, TexKind.Concrete, 2f),
        new("concrete", H("#A3A29D"), 0f, 0.2f, TexKind.Concrete, 2f),
        new("window_glass", H("#9FC4D6", 0.32f), 0.1f, 0.95f, TexKind.None, 1f) { transparent = true },
        new("door", H("#5B3B25"), 0f, 0.35f, TexKind.Wood, 1f),
        new("gate", H("#6F7A80"), 0.7f, 0.45f, TexKind.Steel, 1f),
        new("formwork", H("#C9A571"), 0f, 0.3f, TexKind.Wood, 1f),
        new("fresh_concrete", H("#7F7E7A"), 0f, 0.75f, TexKind.Concrete, 2f),
        new("soil_chunk", H("#6A4E36"), 0f, 0.1f, TexKind.LooseSoil, 1f),
        new("clay_chunk", H("#946B40"), 0f, 0.15f, TexKind.Clay, 1f),
        new("rubble_chunk", H("#9A958F"), 0f, 0.15f, TexKind.Rubble, 1f),
        new("gravel_chunk", H("#A5A29A"), 0f, 0.15f, TexKind.Gravel, 1f),
        new("concrete_wet", H("#8E8E8A"), 0f, 0.7f, TexKind.Concrete, 1f),
        new("asphalt", H("#4A4A4C"), 0f, 0.2f, TexKind.Asphalt, 4f),
        new("outer_grass", H("#FFFFFF"), 0f, 0.1f, TexKind.Grass, 4f),
        new("bark", H("#4F3B2A"), 0f, 0.1f, TexKind.Bark, 1f),
        new("leaves", H("#3C6429"), 0f, 0.25f, TexKind.Leaves, 2f),
        new("fence", H("#C9CED2"), 0.8f, 0.5f, TexKind.Steel, 1f),
        new("container_blue", H("#3D6FB6"), 0.4f, 0.4f, TexKind.Grime, 2f),
        new("silo", H("#D8D8D0"), 0.5f, 0.5f, TexKind.Grime, 3f),
        new("marker_red", H("#D9381E"), 0f, 0.4f, TexKind.None, 1f),
        new("ghost", H("#4FB3FF", 0.25f), 0f, 0.8f, TexKind.None, 1f) { transparent = true, emission = H("#4FB3FF"), emissionEnergy = 0.6f, unshaded = true },
        new("ghost_ok", H("#45E070", 0.35f), 0f, 0.8f, TexKind.None, 1f) { transparent = true, emission = H("#45E070"), emissionEnergy = 0.8f, unshaded = true },
        new("zone", H("#FFC21A", 0.25f), 0f, 0.5f, TexKind.None, 1f) { transparent = true, unshaded = true, doubleSided = true },
        new("rope", H("#222222"), 0.6f, 0.4f, TexKind.None, 1f),
    };

    public static readonly Dictionary<string, MatDef> Defs = new();
    static readonly Dictionary<string, StandardMaterial3D> cache = new();
    static readonly Dictionary<TexKind, (Texture2D alb, Texture2D nrm, Texture2D rough)> texCache = new();
    public static int TexSize = 256;

    static Mats() { foreach (var d in All) Defs[d.key] = d; }

    public static (Texture2D alb, Texture2D nrm, Texture2D rough) Textures(TexKind k, float normalStrength)
    {
        if (texCache.TryGetValue(k, out var t)) return t;
        var r = ProcTex.Make(k, Colors.White, TexSize);
        var rough = new Color[r.rough.Length];
        for (int i = 0; i < rough.Length; i++) rough[i] = new Color(r.rough[i], r.rough[i], r.rough[i]);
        t = (ProcTex.ToTexture(r.albedo, TexSize), ProcTex.ToTexture(ProcTex.NormalFromHeight(r.height, TexSize, normalStrength * TexSize / 256f), TexSize), ProcTex.ToTexture(rough, TexSize));
        texCache[k] = t;
        return t;
    }

    public static StandardMaterial3D Get(string key)
    {
        if (cache.TryGetValue(key, out var m)) return m;
        if (!Defs.TryGetValue(key, out var d)) d = new MatDef(key, Colors.Magenta, 0, 0.5f, TexKind.None);
        m = new StandardMaterial3D
        {
            AlbedoColor = d.color,
            Metallic = d.metallic,
            Roughness = 1f - d.smooth,
            ResourceName = d.key,
        };
        if (d.tex != TexKind.None && d.tex != TexKind.Glass)
        {
            var t = Textures(d.tex, d.normalStrength);
            m.AlbedoTexture = t.alb;
            m.NormalEnabled = true;
            m.NormalTexture = t.nrm;
            m.NormalScale = 1f;
            m.RoughnessTexture = t.rough;
            m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
            m.Roughness = Mathf.Clamp(1.4f - d.smooth, 0.1f, 1f);
            m.Uv1Scale = new Vector3(1f / d.tile, 1f / d.tile, 1f);
            m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        }
        if (d.transparent)
        {
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            m.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly;
        }
        if (d.unshaded) m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        if (d.doubleSided) m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        if (d.emissionEnergy > 0)
        {
            m.EmissionEnabled = true;
            m.Emission = d.emission;
            m.EmissionEnergyMultiplier = d.emissionEnergy;
        }
        cache[key] = m;
        return m;
    }

    public static StandardMaterial3D Copy(string key) => (StandardMaterial3D)Get(key).Duplicate();
}
