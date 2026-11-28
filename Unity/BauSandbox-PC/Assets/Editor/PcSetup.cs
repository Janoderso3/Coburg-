using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor.Rendering.HighDefinition;
using BauSandbox;
using BauSandbox.EditorTools;

/// <summary>
/// PC-Variante (HDRP, maximale Grafik): richtet Materialien, Himmel, Licht, Nachbearbeitung und Szene ein und baut das Spiel.
/// Menü: Bau-Sandbox → 1 Alles einrichten / 2 Windows-Spiel bauen.
/// Kommandozeile: Unity.exe -batchmode -projectPath . -executeMethod PcSetup.BuildWindows -logFile build.log
/// </summary>
public static class PcSetup
{
    const string Dir = "Assets/BauSandbox/Settings";

    [MenuItem("Bau-Sandbox/1  Alles einrichten (PC, HDRP)", priority = 1)]
    public static void SetupAll()
    {
        AssetFactory.EnsureDir(Dir);
        EnsurePipeline();
        var lit = Shader.Find("HDRP/Lit");
        AssetFactory.CreateMaterials(lit, 1024, Finalize);
        AssetFactory.CreateTerrainLayers(2048);
        AssetFactory.CreateSimpleMaterial("terrain", Shader.Find("HDRP/TerrainLit"));
        var dust = AssetFactory.CreateSimpleMaterial("dust", Shader.Find("HDRP/Unlit"));
        dust.SetTexture("_UnlitColorMap", AssetFactory.DustTexture());
        dust.SetColor("_UnlitColor", Color.white);
        dust.SetFloat("_SurfaceType", 1);
        dust.SetFloat("_BlendMode", 0);
        dust.SetFloat("_EnableFogOnTransparent", 1);
        HDMaterial.ValidateMaterial(dust);
        CreateScene();
        BuildCommon.PlayerBasics("Bau-Sandbox");
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        AssetDatabase.SaveAssets();
        Debug.Log("[Bau-Sandbox] PC-Projekt eingerichtet.");
    }

    [MenuItem("Bau-Sandbox/2  Windows-Spiel bauen", priority = 2)]
    public static void BuildWindows()
    {
        SetupAll();
        bool ok = BuildCommon.Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "Builds/Windows/Bau-Sandbox.exe");
        BuildCommon.ExitIfBatch(ok);
    }

    static void Finalize(Material m, MatDef d)
    {
        if (d.transparent)
        {
            m.SetFloat("_SurfaceType", 1);
            m.SetFloat("_BlendMode", 0);
            m.SetFloat("_EnableBlendModePreserveSpecularLighting", 1);
            m.SetFloat("_TransparentWritingMotionVec", 0);
            m.SetColor("_BaseColor", d.color);
        }
        if (d.emissionIntensity > 0)
        {
            m.SetColor("_EmissiveColorLDR", d.emission);
            m.SetFloat("_UseEmissiveIntensity", 1);
            m.SetFloat("_EmissiveIntensityUnit", 0);
            m.SetFloat("_EmissiveIntensity", d.emissionIntensity);
            m.SetColor("_EmissiveColor", d.emission * d.emissionIntensity);
        }
        HDMaterial.ValidateMaterial(m);
    }

    static void EnsurePipeline()
    {
        if (GraphicsSettings.defaultRenderPipeline is HDRenderPipelineAsset) return;
        var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(Dir + "/HDRP Hoch.asset");
        if (!asset)
        {
            asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            AssetDatabase.CreateAsset(asset, Dir + "/HDRP Hoch.asset");
        }
        GraphicsSettings.defaultRenderPipeline = asset;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
        QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
    }

    static VolumeProfile CreateProfile()
    {
        string path = Dir + "/Himmel und Nachbearbeitung.asset";
        var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (old) AssetDatabase.DeleteAsset(path);
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, path);
        var ve = p.Add<VisualEnvironment>(true);
        ve.skyType.Override((int)SkyType.PhysicallyBased);
        ve.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
        var sky = p.Add<PhysicallyBasedSky>(true);
        var exp = p.Add<Exposure>(true);
        exp.mode.Override(ExposureMode.AutomaticHistogram);
        exp.limitMin.Override(9f);
        exp.limitMax.Override(15f);
        exp.compensation.Override(0.3f);
        var fog = p.Add<Fog>(true);
        fog.enabled.Override(true);
        fog.meanFreePath.Override(650f);
        fog.enableVolumetricFog.Override(true);
        fog.maximumHeight.Override(80f);
        var ao = p.Add<ScreenSpaceAmbientOcclusion>(true);
        ao.intensity.Override(1.1f);
        var ssr = p.Add<ScreenSpaceReflection>(true);
        ssr.enabled.Override(true);
        var cs = p.Add<ContactShadows>(true);
        cs.enable.Override(true);
        cs.length.Override(0.25f);
        var tm = p.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.ACES);
        var bloom = p.Add<Bloom>(true);
        bloom.intensity.Override(0.12f);
        var mb = p.Add<MotionBlur>(true);
        mb.intensity.Override(0.2f);
        var sh = p.Add<HDShadowSettings>(true);
        sh.maxShadowDistance.Override(180f);
        var clouds = p.Add<VolumetricClouds>(true);
        clouds.enable.Override(true);
        foreach (var c in p.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, p); }
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        return p;
    }

    static void CreateScene()
    {
        AssetFactory.EnsureDir("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Kamera mit TAA
        var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = cgo.AddComponent<Camera>();
        cam.farClipPlane = 2500f;
        cgo.AddComponent<AudioListener>();
        var hdc = cgo.AddComponent<HDAdditionalCameraData>();
        hdc.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
        // Sonne
        var sgo = new GameObject("Sonne");
        var hdl = sgo.AddHDLight(LightType.Directional);
        var light = sgo.GetComponent<Light>();
        light.intensity = 100000f;
        light.colorTemperature = 5600f;
        light.useColorTemperature = true;
        light.shadows = LightShadows.Soft;
        hdl.SetShadowResolution(4096);
        hdl.angularDiameter = 0.53f;
        sgo.transform.rotation = Quaternion.Euler(42, -35, 0);
        // Himmel, Nebel, Umgebungsverdeckung, Spiegelungen, Tonemapping
        var vgo = new GameObject("Himmel und Licht");
        var vol = vgo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = CreateProfile();
        // Spiel
        new GameObject("Bau-Sandbox").AddComponent<Game>();
        EditorSceneManager.SaveScene(scene, BuildCommon.ScenePath);
    }
}
