using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Beschreibung eines Materials. Das Editor-Setup erzeugt daraus Material-Assets (HDRP/URP).</summary>
    public class MatDef
    {
        public string key;
        public Color color;
        public float metallic, smooth;
        public TexKind tex;
        public float tile = 1f;          // Meter pro Texturkachel
        public bool transparent;
        public Color emission = Color.black;
        public float emissionIntensity;  // in Nits (HDRP) bzw. Faktor
        public float normalStrength = 4f;
        public MatDef(string k, Color c, float m, float s, TexKind t, float tile = 1f)
        { key = k; color = c; metallic = m; smooth = s; tex = t; this.tile = tile; }
    }

    public static class Mats
    {
        static Color H(string hex, float a = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a = a;
            return c;
        }

        public static readonly List<MatDef> All = new List<MatDef>
        {
            // Fahrzeuge
            new MatDef("paint_yellow", H("#E3A614"), 0.15f, 0.55f, TexKind.Grime, 2f),
            new MatDef("paint_yellow_worn", H("#C99A2A"), 0.2f, 0.35f, TexKind.Grime, 1f),
            new MatDef("paint_orange", H("#DB6A1C"), 0.15f, 0.5f, TexKind.Grime, 2f),
            new MatDef("paint_white", H("#E6E6E1"), 0.1f, 0.55f, TexKind.Grime, 2f),
            new MatDef("paint_blue", H("#2A5A9C"), 0.15f, 0.55f, TexKind.Paint, 2f),
            new MatDef("paint_red", H("#B3261E"), 0.15f, 0.5f, TexKind.Grime, 2f),
            new MatDef("dark_metal", H("#2D2D2D"), 0.5f, 0.35f, TexKind.Grime, 1.5f),
            new MatDef("black_plastic", H("#161616"), 0f, 0.3f, TexKind.Paint, 1f),
            new MatDef("steel", H("#8D8F91"), 0.85f, 0.55f, TexKind.Steel, 1f),
            new MatDef("chrome", H("#D7DBDF"), 1f, 0.9f, TexKind.None, 1f),
            new MatDef("rubber", H("#1B1B1B"), 0f, 0.15f, TexKind.Paint, 1f),
            new MatDef("tread", H("#202020"), 0f, 0.12f, TexKind.Tread, 1f),
            new MatDef("track", H("#3A3836"), 0.6f, 0.3f, TexKind.Track, 0.22f),
            new MatDef("bucket_wear", H("#5A5650"), 0.7f, 0.4f, TexKind.Steel, 1f),
            new MatDef("drum", H("#E8E8E2"), 0.3f, 0.6f, TexKind.Grime, 2f),
            new MatDef("glass", H("#20303A", 0.35f), 0.2f, 0.95f, TexKind.Glass, 1f) { transparent = true },
            new MatDef("headlight", H("#FFF6E0"), 0f, 0.9f, TexKind.None, 1f) { emission = H("#FFF1C8"), emissionIntensity = 20000f },
            new MatDef("taillight", H("#550000"), 0f, 0.8f, TexKind.None, 1f) { emission = H("#FF2010"), emissionIntensity = 3000f },
            new MatDef("beacon", H("#FF8A00"), 0f, 0.8f, TexKind.None, 1f) { emission = H("#FF7A00"), emissionIntensity = 8000f },
            new MatDef("seat", H("#202224"), 0f, 0.25f, TexKind.Paint, 0.5f),
            // Gebäude
            new MatDef("plaster", H("#E3D6BD"), 0f, 0.15f, TexKind.Plaster, 2f),
            new MatDef("plaster_white", H("#EFEBE3"), 0f, 0.15f, TexKind.Plaster, 2f),
            new MatDef("brick", H("#9B4C36"), 0f, 0.2f, TexKind.Brick, 1f),
            new MatDef("timber", H("#4E3320"), 0f, 0.25f, TexKind.Wood, 1f),
            new MatDef("roof_tile", H("#9A3F2C"), 0f, 0.3f, TexKind.Roof, 1.2f),
            new MatDef("prefab", H("#DCD7CC"), 0f, 0.2f, TexKind.Concrete, 2f),
            new MatDef("concrete", H("#A3A29D"), 0f, 0.2f, TexKind.Concrete, 2f),
            new MatDef("window_glass", H("#9FC4D6", 0.3f), 0.1f, 0.95f, TexKind.Glass, 1f) { transparent = true },
            new MatDef("door", H("#5B3B25"), 0f, 0.35f, TexKind.Wood, 1f),
            new MatDef("gate", H("#6F7A80"), 0.7f, 0.45f, TexKind.Steel, 1f),
            new MatDef("formwork", H("#C9A571"), 0f, 0.3f, TexKind.Wood, 1f),
            new MatDef("fresh_concrete", H("#7F7E7A"), 0f, 0.75f, TexKind.Concrete, 2f),
            // Erde / Schüttgut
            new MatDef("soil_chunk", H("#5E4530"), 0f, 0.1f, TexKind.LooseSoil, 1f),
            new MatDef("clay_chunk", H("#946B40"), 0f, 0.15f, TexKind.Clay, 1f),
            new MatDef("rubble_chunk", H("#8F8A84"), 0f, 0.15f, TexKind.Rubble, 1f),
            new MatDef("gravel_chunk", H("#9D9A92"), 0f, 0.15f, TexKind.Gravel, 1f),
            new MatDef("concrete_wet", H("#8E8E8A"), 0f, 0.7f, TexKind.Concrete, 1f),
            // Umgebung
            new MatDef("asphalt", H("#4A4A4C"), 0f, 0.2f, TexKind.Asphalt, 4f),
            new MatDef("outer_grass", H("#FFFFFF"), 0f, 0.1f, TexKind.Grass, 4f),
            new MatDef("bark", H("#4F3B2A"), 0f, 0.1f, TexKind.Bark, 1f),
            new MatDef("leaves", H("#3C6429"), 0f, 0.25f, TexKind.Leaves, 2f),
            new MatDef("fence", H("#C9CED2"), 0.8f, 0.5f, TexKind.Steel, 1f),
            new MatDef("container_blue", H("#3D6FB6"), 0.4f, 0.4f, TexKind.Grime, 2f),
            new MatDef("silo", H("#D8D8D0"), 0.5f, 0.5f, TexKind.Grime, 3f),
            new MatDef("marker_red", H("#D9381E"), 0f, 0.4f, TexKind.None, 1f),
            new MatDef("ghost", H("#4FB3FF", 0.25f), 0f, 0.8f, TexKind.None, 1f) { transparent = true, emission = H("#4FB3FF"), emissionIntensity = 400f },
            new MatDef("ghost_ok", H("#45E070", 0.35f), 0f, 0.8f, TexKind.None, 1f) { transparent = true, emission = H("#45E070"), emissionIntensity = 600f },
            new MatDef("zone", H("#FFC21A", 0.22f), 0f, 0.5f, TexKind.None, 1f) { transparent = true, emission = H("#FFC21A"), emissionIntensity = 300f },
        };

        public static readonly Dictionary<string, MatDef> Defs = new Dictionary<string, MatDef>();
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Shader litShader;
        public static string Pipeline = "builtin";

        static Mats()
        {
            foreach (var d in All) Defs[d.key] = d;
        }

        public static Shader Lit
        {
            get
            {
                if (litShader) return litShader;
                litShader = Shader.Find("HDRP/Lit");
                if (litShader) { Pipeline = "hdrp"; return litShader; }
                litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (litShader) { Pipeline = "urp"; return litShader; }
                litShader = Shader.Find("Standard");
                Pipeline = "builtin";
                return litShader;
            }
        }

        /// <summary>Material holen: zuerst Asset aus Resources (vom Editor-Setup erstellt), sonst zur Laufzeit erzeugen.</summary>
        public static Material Get(string key)
        {
            if (cache.TryGetValue(key, out var m) && m) return m;
            m = Resources.Load<Material>("BauSandbox/Materials/" + key);
            if (!m)
            {
                Defs.TryGetValue(key, out var d);
                if (d == null) d = new MatDef(key, Color.magenta, 0, 0.5f, TexKind.None);
                m = CreateRuntime(d);
            }
            m.enableInstancing = true;
            cache[key] = m;
            return m;
        }

        /// <summary>Kopie mit anderer Farbe (z. B. Variation).</summary>
        public static Material Tinted(string key, Color tint)
        {
            var m = new Material(Get(key));
            SetColor(m, m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") * tint : tint);
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetEmission(Material m, Color c, float intensity)
        {
            var e = c * intensity;
            if (Pipeline != "hdrp") e = c * Mathf.Clamp(intensity / 2000f, 0f, 6f);
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", e);
            if (m.HasProperty("_EmissionColor")) { m.SetColor("_EmissionColor", e); m.EnableKeyword("_EMISSION"); }
        }

        static Material CreateRuntime(MatDef d)
        {
            var sh = Lit;
            var m = new Material(sh) { name = d.key };
            SetColor(m, d.color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", d.metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", d.smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", d.smooth);
            if (d.tex != TexKind.None && d.tex != TexKind.Glass)
            {
                int size = Application.isMobilePlatform ? 128 : 256;
                var r = ProcTex.Make(d.tex, Color.white, size);
                var alb = ProcTex.ToTexture(r.albedo, size, false, d.key + "_alb");
                var nrm = ProcTex.ToTexture(ProcTex.NormalFromHeight(r.height, size, d.normalStrength), size, true, d.key + "_nrm");
                if (m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", alb);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", alb);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", alb);
                if (m.HasProperty("_NormalMap")) { m.SetTexture("_NormalMap", nrm); m.EnableKeyword("_NORMALMAP"); }
                if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
                m.mainTextureScale = new Vector2(1f / d.tile, 1f / d.tile);
            }
            if (d.emissionIntensity > 0) SetEmission(m, d.emission, d.emissionIntensity);
            if (d.transparent) MakeTransparentRuntime(m, d.color.a);
            return m;
        }

        static void MakeTransparentRuntime(Material m, float alpha)
        {
            // Rückfall ohne Editor-Setup (Standard/URP). HDRP-Transparenz wird im Editor korrekt eingerichtet.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1);
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3);
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
        }
    }
}
