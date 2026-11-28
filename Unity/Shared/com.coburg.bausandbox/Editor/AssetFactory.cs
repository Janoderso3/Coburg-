using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BauSandbox.EditorTools
{
    /// <summary>
    /// Erzeugt alle Material-, Textur- und Terrain-Assets aus den Laufzeit-Definitionen (Mats.All, TerrainLayers.Full).
    /// Die Render-Pipeline-spezifischen Feinheiten (Transparenz, Emission) erledigt ein Rückruf des jeweiligen Projekts.
    /// </summary>
    public static class AssetFactory
    {
        public const string Root = "Assets/BauSandbox/Generated";
        public const string TexDir = Root + "/Textures";
        public const string MatDir = "Assets/Resources/BauSandbox/Materials";
        public const string LayerDir = "Assets/Resources/BauSandbox/TerrainLayers";

        public static void EnsureDir(string path)
        {
            var parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        public static Texture2D SaveTex(Color[] px, int size, string name, bool normal, bool linear)
        {
            EnsureDir(TexDir);
            string path = $"{TexDir}/{name}.png";
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
            t.SetPixels(px);
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !linear && !normal;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 8;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.maxTextureSize = Mathf.Max(256, size);
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Alle Fahrzeug-, Gebäude- und Umgebungsmaterialien.</summary>
        public static void CreateMaterials(Shader lit, int texSize, Action<Material, MatDef> finalize)
        {
            EnsureDir(MatDir);
            bool hdrp = lit.name.StartsWith("HDRP");
            foreach (var d in Mats.All)
            {
                Texture2D alb = null, nrm = null, mask = null;
                if (d.tex != TexKind.None && d.tex != TexKind.Glass)
                {
                    var r = ProcTex.Make(d.tex, Color.white, texSize);
                    alb = SaveTex(r.albedo, texSize, d.key + "_albedo", false, false);
                    nrm = SaveTex(ProcTex.NormalFromHeight(r.height, texSize, d.normalStrength * texSize / 256f), texSize, d.key + "_normal", true, true);
                    mask = SaveTex(ProcTex.MaskMap(r, d.metallic), texSize, d.key + "_mask", false, true);
                }
                string path = $"{MatDir}/{d.key}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
                m.shader = lit;
                m.enableInstancing = true;
                Set(m, "_BaseColor", d.color);
                Set(m, "_Color", d.color);
                SetF(m, "_Metallic", d.metallic);
                SetF(m, "_Smoothness", d.smooth);
                if (alb)
                {
                    SetT(m, hdrp ? "_BaseColorMap" : "_BaseMap", alb);
                    SetT(m, hdrp ? "_NormalMap" : "_BumpMap", nrm);
                    m.EnableKeyword("_NORMALMAP");
                    SetF(m, hdrp ? "_NormalScale" : "_BumpScale", 1f);
                    if (hdrp)
                    {
                        SetT(m, "_MaskMap", mask);
                        m.EnableKeyword("_MASKMAP");
                        SetF(m, "_MetallicRemapMax", d.metallic);
                        SetF(m, "_SmoothnessRemapMin", 0f);
                        SetF(m, "_SmoothnessRemapMax", Mathf.Clamp01(d.smooth * 1.5f));
                        SetF(m, "_AORemapMin", 0.35f);
                    }
                    m.mainTextureScale = new Vector2(1f / d.tile, 1f / d.tile);
                    if (m.HasProperty("_BaseColorMap")) m.SetTextureScale("_BaseColorMap", m.mainTextureScale);
                    if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", m.mainTextureScale);
                }
                finalize?.Invoke(m, d);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Terrain-Schichten mit großen Texturen (Albedo, Normal, Maske).</summary>
        public static void CreateTerrainLayers(int texSize)
        {
            EnsureDir(LayerDir);
            foreach (var d in TerrainLayers.Full)
            {
                var r = ProcTex.Make(d.tex, d.color, texSize);
                var alb = SaveTex(r.albedo, texSize, "terrain_" + d.key + "_albedo", false, false);
                var nrm = SaveTex(ProcTex.NormalFromHeight(r.height, texSize, 5f * texSize / 256f), texSize, "terrain_" + d.key + "_normal", true, true);
                var mask = SaveTex(ProcTex.MaskMap(r, 0f), texSize, "terrain_" + d.key + "_mask", false, true);
                string path = $"{LayerDir}/{d.key}.terrainlayer";
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (!l) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
                l.diffuseTexture = alb;
                l.normalMapTexture = nrm;
                l.maskMapTexture = mask;
                l.tileSize = new Vector2(d.tile, d.tile);
                l.smoothness = d.smooth;
                l.normalScale = 1f;
                l.maskMapRemapMax = new Vector4(0, 1, 1, Mathf.Clamp01(d.smooth * 2f));
                EditorUtility.SetDirty(l);
            }
            AssetDatabase.SaveAssets();
        }

        public static Material CreateSimpleMaterial(string key, Shader sh)
        {
            EnsureDir(MatDir);
            string path = $"{MatDir}/{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Texture2D DustTexture()
        {
            var t = SoilFX.SoftDot();
            var px = t.GetPixels();
            return SaveTex(px, t.width, "dust_softdot", false, false);
        }

        static void Set(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
        static void SetT(Material m, string p, Texture t) { if (t && m.HasProperty(p)) m.SetTexture(p, t); }
    }

    /// <summary>Gemeinsame Build-Schritte.</summary>
    public static class BuildCommon
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        public static void PlayerBasics(string product)
        {
            PlayerSettings.companyName = "Coburg";
            PlayerSettings.productName = product;
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // Altes Eingabesystem aktivieren (beide)
            var ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var so = new SerializedObject(ps);
            var p = so.FindProperty("activeInputHandler");
            if (p != null) { p.intValue = 2; so.ApplyModifiedProperties(); }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        public static bool Build(BuildTarget target, BuildTargetGroup group, string outPath)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target) EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = outPath, target = target, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(opts);
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            Debug.Log($"[Bau-Sandbox] Build {target}: {report.summary.result}, {report.summary.totalErrors} Fehler, Größe {report.summary.totalSize / 1048576} MB -> {outPath}");
            return ok;
        }

        public static void ExitIfBatch(bool ok)
        {
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
