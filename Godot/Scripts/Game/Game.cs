using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>Spielablauf: Hauptmenü, Missionen, Fahrzeugwechsel, Kamera, Pause, Grafik.</summary>
public partial class Game : Node3D
{
    public enum State { Menu, Loading, Playing, Paused, Result, Options }
    public State state = State.Loading;
    public static Game I;
    public World world;
    public GameInput input;
    public CameraRig rig;
    public GameUI ui;
    public MissionRun run;
    public readonly List<VehicleBase> Selectable = new();
    int active = -1;
    float checkT;
    bool resultShown;
    int buildToken;
    Camera3D cam;
    DirectionalLight3D sun;
    WorldEnvironment we;
    Godot.Environment env;
    public ControlInput? forcedInput;   // für automatische Tests
    public VehicleBase Active => active >= 0 && active < Selectable.Count ? Selectable[active] : null;

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        Settings.Iso = Save.GetInt("iso") == 1;
        Settings.Volume = Save.GetFloat("volume", 0.85f);
        Settings.Quality = Save.GetInt("quality", 2);
        var args = OS.GetCmdlineUserArgs();
        foreach (var a in args) if (a.StartsWith("--quality=")) Settings.Quality = int.Parse(a[10..]);
        SoilTerrain.LowLayerCount = OS.HasFeature("mobile");
        input = new GameInput { Name = "Eingabe" };
        AddChild(input);
        cam = new Camera3D { Name = "Kamera", Far = 3000f, Near = 0.1f, Fov = 55f, Current = true };
        AddChild(cam);
        rig = new CameraRig { Name = "Kameraführung", cam = cam, ProcessMode = ProcessModeEnum.Always };
        AddChild(rig);
        var sfx = new SoundFX { Name = "Geraeusche" };
        AddChild(sfx);
        BuildEnvironment();
        var layer = new CanvasLayer { Name = "Oberflaeche", Layer = 10 };
        AddChild(layer);
        ui = new GameUI { Name = "UI", game = this };
        layer.AddChild(ui);
        input.overUI = p => ui.OverUI(p);
        input.uiPress = p => ui.Press(p);
        input.uiRelease = p => ui.Release(p);
        input.uiMove = p => ui.Move(p);
        ApplyVolume();
        ApplyQuality(Settings.Quality);
        if (Save.GetInt("fullscreen") == 1 && !OS.HasFeature("mobile")) SetFullscreen(true);
        Engine.MaxFps = 0;
        foreach (var a in args)
            if (a.StartsWith("--test"))
            {
                AddChild(new TestDriver { Name = "Test", game = this, args = args });
                return;
            }
        BuildWorld(Mission.All[^1], true);
    }

    // ---------------- Grafik ----------------
    void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.22f, 0.42f, 0.78f),
            SkyHorizonColor = new Color(0.66f, 0.74f, 0.84f),
            SkyCurve = 0.12f,
            GroundBottomColor = new Color(0.20f, 0.22f, 0.18f),
            GroundHorizonColor = new Color(0.62f, 0.68f, 0.72f),
            SunAngleMax = 30f,
            SunCurve = 0.12f,
            SkyEnergyMultiplier = 1.0f,
        };
        env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky, ProcessMode = Sky.ProcessModeEnum.Realtime, RadianceSize = Sky.RadianceSizeEnum.Size256 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1f,
            AmbientLightEnergy = 1f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            TonemapWhite = 6f,
            SsaoRadius = 1.4f,
            SsaoIntensity = 2.2f,
            SsaoPower = 1.4f,
            SsaoDetail = 0.6f,
            SsaoLightAffect = 0.15f,
            SsilRadius = 4f,
            SsilIntensity = 0.8f,
            GlowIntensity = 0.35f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 1.2f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogLightColor = new Color(0.72f, 0.78f, 0.86f),
            FogDensity = 0.0009f,
            FogAerialPerspective = 0.6f,
            FogSunScatter = 0.15f,
            FogSkyAffect = 0.35f,
            VolumetricFogDensity = 0.0012f,
            VolumetricFogAlbedo = new Color(0.9f, 0.9f, 0.9f),
            VolumetricFogLength = 120f,
            VolumetricFogAnisotropy = 0.5f,
            VolumetricFogSkyAffect = 0.0f,
            SsrMaxSteps = 48,
            AdjustmentEnabled = true,
            AdjustmentBrightness = 1.0f,
            AdjustmentContrast = 1.06f,
            AdjustmentSaturation = 1.08f,
        };
        we = new WorldEnvironment { Name = "Umgebung", Environment = env };
        AddChild(we);
        sun = new DirectionalLight3D
        {
            Name = "Sonne",
            LightColor = new Color(1f, 0.95f, 0.87f),
            LightEnergy = 1.35f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 260f,
            DirectionalShadowSplit1 = 0.06f,
            DirectionalShadowSplit2 = 0.18f,
            DirectionalShadowSplit3 = 0.45f,
            DirectionalShadowBlendSplits = true,
            ShadowBias = 0.03f,
            ShadowNormalBias = 1.2f,
            LightAngularDistance = 0.6f,
        };
        sun.RotationDegrees = new Vector3(-46, 150, 0);
        AddChild(sun);
    }

    public void ApplyQuality(int q)
    {
        q = Math.Clamp(q, 0, 3);
        Settings.Quality = q;
        var vp = GetViewport();
        env.SsaoEnabled = q >= 1;
        env.SsilEnabled = q >= 2;
        env.GlowEnabled = q >= 1;
        env.SsrEnabled = q >= 3;
        env.VolumetricFogEnabled = q >= 3;
        sun.DirectionalShadowMaxDistance = q == 0 ? 120 : q == 1 ? 200 : 300;
        sun.LightAngularDistance = q >= 2 ? 0.6f : 0f;
        RenderingServer.DirectionalShadowAtlasSetSize(q == 0 ? 2048 : q == 1 ? 4096 : 8192, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(q switch { 0 => RenderingServer.ShadowQuality.Hard, 1 => RenderingServer.ShadowQuality.SoftLow, 2 => RenderingServer.ShadowQuality.SoftHigh, _ => RenderingServer.ShadowQuality.SoftUltra });
        RenderingServer.EnvironmentSetSsaoQuality(q >= 3 ? RenderingServer.EnvironmentSsaoQuality.Ultra : q == 2 ? RenderingServer.EnvironmentSsaoQuality.High : RenderingServer.EnvironmentSsaoQuality.Medium, true, 0.5f, 2, 50, 300);
        RenderingServer.EnvironmentSetSsilQuality(q >= 3 ? RenderingServer.EnvironmentSsilQuality.High : RenderingServer.EnvironmentSsilQuality.Medium, true, 0.5f, 4, 50, 300);
        vp.Msaa3D = q >= 3 ? Viewport.Msaa.Msaa4X : q == 2 ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
        vp.UseTaa = q >= 2;
        vp.ScreenSpaceAA = q == 1 ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        vp.Scaling3DScale = q == 0 ? 0.75f : 1f;
        vp.MeshLodThreshold = q >= 2 ? 0.5f : 1f;
        vp.AnisotropicFilteringLevel = q >= 2 ? Viewport.AnisotropicFiltering.Anisotropy16X : Viewport.AnisotropicFiltering.Anisotropy4X;
    }

    public void ApplyVolume()
    {
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(MathF.Max(0.0001f, Settings.Volume)));
        AudioServer.SetBusMute(0, Settings.Volume <= 0.001f);
    }

    public void SetFullscreen(bool on)
    {
        DisplayServer.WindowSetMode(on ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Maximized);
        Save.Set("fullscreen", on ? 1 : 0); Save.Flush();
    }

    public void SaveSettings()
    {
        Save.Set("iso", Settings.Iso ? 1 : 0);
        Save.Set("volume", Settings.Volume);
        Save.Set("quality", Settings.Quality);
        Save.Flush();
    }

    // ---------------- Ablauf ----------------
    public async void BuildWorld(Mission m, bool menu)
    {
        int token = ++buildToken;
        state = State.Loading;
        GetTree().Paused = false;
        if (Active != null) { Active.isActive = false; }
        rig.SetTarget(null);
        rig.menuOrbit = true;
        Selectable.Clear();
        active = -1;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (world != null) { world.QueueFree(); world = null; }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (token != buildToken) return;
        world = new World();
        AddChild(world);
        world.Build(m.house, m.garage, m.vehicles);
        if (menu)
        {
            run = null;
            rig.menuOrbit = true;
            state = State.Menu;
            return;
        }
        run = new MissionRun { m = m };
        m.setup(world, run);
        foreach (var name in m.vehicles)
            foreach (var v in world.vehicles)
                if (v.shortName == name && !Selectable.Contains(v)) Selectable.Add(v);
        rig.menuOrbit = false;
        resultShown = false;
        checkT = 0.3f;
        Select(0);
        state = State.Playing;
        Hud.Toast(m.brief, ToastKind.Info);
    }

    public void StartMission(Mission m) => BuildWorld(m, false);
    public void ToMenu() => BuildWorld(Mission.All[^1], true);

    public void Pause()
    {
        if (state != State.Playing) return;
        state = State.Paused;
        GetTree().Paused = true;
    }

    public void Resume() { state = State.Playing; GetTree().Paused = false; }
    public void ContinueAfterResult() { state = State.Playing; GetTree().Paused = false; }

    public void Select(int i)
    {
        if (Selectable.Count == 0) return;
        i = Math.Clamp(i, 0, Selectable.Count - 1);
        if (Active != null) { Active.isActive = false; Active.input = ControlInput.None; }
        active = i;
        Active.isActive = true;
        rig.SetTarget(Active);
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;
        if (k.Keycode == Key.F11 && !OS.HasFeature("mobile")) { SetFullscreen(DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen); return; }
        if (k.Keycode == Key.F12) { Screenshot(); return; }
        if (state == State.Playing || state == State.Result)
        {
            if (k.Keycode >= Key.Key1 && k.Keycode <= Key.Key9) { int n = (int)(k.Keycode - Key.Key1); if (n < Selectable.Count) Select(n); }
            else if (k.Keycode == Key.Tab) Select((active + 1) % Math.Max(1, Selectable.Count));
            else if (k.Keycode == Key.C) rig.Cycle();
            else if (k.Keycode == Key.Escape) { if (state == State.Result) ContinueAfterResult(); else Pause(); }
            else if (k.Keycode == Key.F1) ui.showHelp = !ui.showHelp;
            else if (k.Keycode == Key.R && Active != null && Active.tipped) Active.Recover();
        }
        else if (k.Keycode == Key.Escape)
        {
            if (state == State.Paused) Resume();
            else if (state == State.Options) state = State.Menu;
        }
    }

    public void Screenshot(string path = null)
    {
        var img = GetViewport().GetTexture().GetImage();
        if (path == null)
        {
            DirAccess.MakeDirRecursiveAbsolute("user://Bildschirmfotos");
            path = $"user://Bildschirmfotos/bau-sandbox-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            Hud.Toast("Bildschirmfoto gespeichert: " + ProjectSettings.GlobalizePath(path), ToastKind.Ok);
        }
        img.SavePng(path);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        input.Poll(dt);
        if (state == State.Playing || state == State.Result)
        {
            var v = Active;
            if (v != null && IsInstanceValid(v))
            {
                v.input = forcedInput ?? input.state;
                var c = v.Controls();
                if (input.actionPressed && c.buttons.Count > 0) v.OnButton(c.buttons[0].id);
                if (input.action2Pressed && c.buttons.Count > 1) v.OnButton(c.buttons[1].id);
            }
            if (run != null && state == State.Playing)
            {
                if (!run.done) run.time += dt;
                checkT -= dt;
                if (checkT <= 0)
                {
                    checkT = 0.5f;
                    foreach (var veh in world.vehicles)
                        if (IsInstanceValid(veh) && veh.tipped && !run.m.sandbox && !run.penaltyNotes.Contains(veh.shortName + " umgekippt")) run.Penalty(veh.shortName + " umgekippt");
                    run.m.check(world, run);
                    if (run.done && !resultShown && !run.m.sandbox)
                    {
                        resultShown = true;
                        run.stars = run.m.Stars(run);
                        run.m.SaveResult(run);
                        SoundFX.I?.PlayUI("success", 0.8f);
                        state = State.Result;
                    }
                }
            }
        }
        rig.Tick(dt, state == State.Playing || state == State.Result ? input : null);
    }
}
