using UnityEngine;

namespace BauSandbox
{
    public class LayerDef
    {
        public string key; public Color color; public TexKind tex; public float tile; public float smooth;
        public LayerDef(string k, string hex, TexKind t, float tile, float smooth = 0.1f)
        {
            key = k; ColorUtility.TryParseHtmlString(hex, out color); tex = t; this.tile = tile; this.smooth = smooth;
        }
    }

    /// <summary>Terrain-Schichten (Gras, Mutterboden, Lehm, Sand, Schotter, lose Erde, Schutt, Beton).</summary>
    public static class TerrainLayers
    {
        public static readonly LayerDef[] Full =
        {
            new LayerDef("grass", "#5A7A33", TexKind.Grass, 3f),
            new LayerDef("topsoil", "#4A3524", TexKind.Topsoil, 2f),
            new LayerDef("clay", "#9A7046", TexKind.Clay, 2.5f, 0.2f),
            new LayerDef("sand", "#BCA37B", TexKind.Sand, 2f),
            new LayerDef("gravel", "#9D9A92", TexKind.Gravel, 1.2f),
            new LayerDef("loose", "#5E4530", TexKind.LooseSoil, 1.5f),
            new LayerDef("rubble", "#8F8A84", TexKind.Rubble, 1.5f),
            new LayerDef("concrete", "#7C7B78", TexKind.Concrete, 3f, 0.25f),
        };
        public static readonly LayerDef[] Mobile = { Full[0], Full[1], Full[2], Full[4] };

        public static TerrainLayer[] Create(int count)
        {
            var defs = count == 8 ? Full : Mobile;
            var res = new TerrainLayer[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var asset = Resources.Load<TerrainLayer>("BauSandbox/TerrainLayers/" + d.key);
                if (asset) { res[i] = asset; continue; }
                int size = Application.isMobilePlatform ? 256 : 512;
                var r = ProcTex.Make(d.tex, d.color, size);
                var l = new TerrainLayer
                {
                    diffuseTexture = ProcTex.ToTexture(r.albedo, size, false, d.key + "_alb"),
                    normalMapTexture = ProcTex.ToTexture(ProcTex.NormalFromHeight(r.height, size, 5f), size, true, d.key + "_nrm"),
                    maskMapTexture = ProcTex.ToTexture(ProcTex.MaskMap(r, 0f), size, true, d.key + "_mask"),
                    tileSize = new Vector2(d.tile, d.tile),
                    smoothness = d.smooth,
                    metallic = 0f,
                    normalScale = 1f,
                };
                res[i] = l;
            }
            return res;
        }

        public static Material TerrainMaterial()
        {
            var m = Resources.Load<Material>("BauSandbox/Materials/terrain");
            if (m) return m;
            var sh = Shader.Find("HDRP/TerrainLit");
            if (!sh) sh = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (!sh) sh = Shader.Find("Nature/Terrain/Standard");
            return sh ? new Material(sh) : null;
        }
    }
}
