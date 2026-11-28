using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using BauSandbox;
using BauSandbox.EditorTools;

/// <summary>
/// iPhone-Variante (URP, für Mobilgeräte optimiert): Materialien, Licht, Nachbearbeitung, Szene, Xcode-Export.
/// Menü: Bau-Sandbox → 1 Alles einrichten / 2 iPhone-Xcode-Projekt erzeugen.
/// Das Xcode-Projekt muss anschließend auf einem Mac mit Xcode signiert und aufs iPhone übertragen werden.
/// </summary>
public static class IosSetup
{
    const string Dir = "Assets/BauSandbox/Settings";

    [MenuItem("Bau-Sandbox/1  Alles einrichten (iPhone, URP)", priority = 1)]
    public static void SetupAll()
    {
        AssetFactory.EnsureDir(Dir);
        EnsurePipeline();
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        AssetFactory.CreateMaterials(lit, 512, Finalize);
        AssetFactory.CreateTerrainLayers(1024);
        AssetFactory.CreateSimpleMaterial("terrain", Shader.Find("Universal Render Pipeline/Terrain/Lit"));
        var dust = AssetFactory.CreateSimpleMaterial("dust", Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        dust.SetTexture("_BaseMap", AssetFactory.DustTexture());
        dust.SetFloat("_Surface", 1);
        dust.SetFloat("_Blend", 0);
        dust.SetOverrideTag("RenderType", "Transparent");
        dust.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        dust.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        dust.SetFloat("_ZWrite", 0);
        dust.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        dust.renderQueue = 3000;
        CreateScene();
        BuildCommon.PlayerBasics("Bau-Sandbox");
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "de.coburg.bausandbox");
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        AssetDatabase.SaveAssets();
        Debug.Log("[Bau-Sandbox] iPhone-Projekt eingerichtet.");
    }

    [MenuItem("Bau-Sandbox/2  iPhone-Xcode-Projekt erzeugen", priority = 2)]
    public static void BuildIOS()
    {
        SetupAll();
        bool ok = BuildCommon.Build(BuildTarget.iOS, BuildTargetGroup.iOS, "Builds/iOS");
        BuildCommon.ExitIfBatch(ok);
    }

    static void Finalize(Material m, MatDef d)
    {
        if (d.transparent)
        {
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        }
        if (d.emissionIntensity > 0)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", d.emission * Mathf.Clamp(d.emissionIntensity / 2000f, 0.5f, 6f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
    }

    static void EnsurePipeline()
    {
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset) return;
        var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rd, Dir + "/URP Renderer.asset");
        var asset = UniversalRenderPipelineAsset.Create(rd);
        asset.shadowDistance = 90f;
        asset.shadowCascadeCount = 2;
        asset.msaaSampleCount = 4;
        asset.renderScale = 1f;
        asset.supportsHDR = true;
        AssetDatabase.CreateAsset(asset, Dir + "/URP Mobil.asset");
        GraphicsSettings.defaultRenderPipeline = asset;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
    }

    static void CreateScene()
    {
        AssetFactory.EnsureDir("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = cgo.AddComponent<Camera>();
        cam.farClipPlane = 1500f;
        cgo.AddComponent<AudioListener>();
        var ucd = cgo.AddComponent<UniversalAdditionalCameraData>();
        ucd.renderPostProcessing = true;
        ucd.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        var sgo = new GameObject("Sonne");
        var light = sgo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.color = new Color(1f, 0.95f, 0.86f);
        light.shadows = LightShadows.Soft;
        sgo.transform.rotation = Quaternion.Euler(42, -35, 0);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.56f, 0.52f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.26f, 0.2f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.78f, 0.84f, 0.9f);
        RenderSettings.fogStartDistance = 120f;
        RenderSettings.fogEndDistance = 900f;
        var skyMat = new Material(Shader.Find("Skybox/Procedural"));
        AssetDatabase.CreateAsset(skyMat, Dir + "/Himmel.mat");
        RenderSettings.skybox = skyMat;
        var vgo = new GameObject("Nachbearbeitung");
        var vol = vgo.AddComponent<Volume>();
        vol.isGlobal = true;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, Dir + "/Nachbearbeitung.asset");
        var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
        var bl = p.Add<Bloom>(true); bl.intensity.Override(0.3f);
        var ca = p.Add<ColorAdjustments>(true); ca.postExposure.Override(0.2f); ca.contrast.Override(8f);
        foreach (var c in p.components) AssetDatabase.AddObjectToAsset(c, p);
        vol.sharedProfile = p;
        new GameObject("Bau-Sandbox").AddComponent<Game>();
        EditorSceneManager.SaveScene(scene, BuildCommon.ScenePath);
    }
}
