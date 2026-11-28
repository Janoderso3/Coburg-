using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Spielablauf: Hauptmenü, Missionen, Fahrzeugwechsel, Kamera, Pause.</summary>
    public class Game : MonoBehaviour
    {
        public enum State { Menu, Loading, Playing, Paused, Result }
        public State state = State.Menu;
        public static Game I;
        public World world;
        public GameInput input;
        public CameraRig rig;
        public GameUI ui;
        public MissionRun run;
        public readonly List<VehicleBase> Selectable = new List<VehicleBase>();
        int active = -1;
        float checkT;
        bool resultShown;
        public VehicleBase Active => active >= 0 && active < Selectable.Count ? Selectable[active] : null;

        // Startet das Spiel automatisch, auch in einer leeren Szene
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var existing = Object.FindObjectsOfType<Game>();
            if (existing.Length > 0) return;
            new GameObject("Bau-Sandbox").AddComponent<Game>();
        }

        void Awake()
        {
            I = this;
            Layers.Setup();
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.bounceThreshold = 1.5f;
            Physics.sleepThreshold = 0.02f;
            Application.targetFrameRate = Application.isMobilePlatform ? 60 : -1;
            Settings.Iso = PlayerPrefs.GetInt("iso", 0) == 1;
            Settings.Volume = PlayerPrefs.GetFloat("volume", 1f);
            AudioListener.volume = Settings.Volume;
            if (PlayerPrefs.HasKey("quality")) QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt("quality"), 0, QualitySettings.names.Length - 1), true);
            SoilTerrain.LowLayerCount = Application.isMobilePlatform;
            input = gameObject.AddComponent<GameInput>();
            var cam = Camera.main;
            if (!cam)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            cam.farClipPlane = 2500f;
            cam.nearClipPlane = 0.1f;
            rig = gameObject.AddComponent<CameraRig>();
            rig.cam = cam;
            if (!Object.FindObjectOfType<AudioListener>()) cam.gameObject.AddComponent<AudioListener>();
            if (!SoundFX.I) new GameObject("Geräusche").AddComponent<SoundFX>().transform.SetParent(transform, false);
            EnsureSun();
            ui = gameObject.AddComponent<GameUI>();
            ui.game = this;
        }

        void Start()
        {
            // Hintergrund für das Menü: freie Baustelle
            StartCoroutine(BuildWorld(Mission.All[Mission.All.Count - 1], true));
        }

        void EnsureSun()
        {
            foreach (var l in Object.FindObjectsOfType<Light>()) if (l.type == LightType.Directional) return;
            var go = new GameObject("Sonne");
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.95f, 0.88f);
            sun.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(48, -35, 0);
        }

        IEnumerator BuildWorld(Mission m, bool menu)
        {
            state = State.Loading;
            yield return null;
            if (world) world.Teardown();
            Selectable.Clear();
            active = -1;
            yield return null;
            var wgo = new GameObject("Welt");
            world = wgo.AddComponent<World>();
            world.Build(m.house, m.garage, m.vehicles);
            if (menu)
            {
                run = null;
                rig.menuOrbit = true;
                rig.SetTarget(null);
                state = State.Menu;
                yield break;
            }
            run = new MissionRun { m = m };
            m.setup(world, run);
            foreach (var name in m.vehicles)
                foreach (var v in world.vehicles)
                    if (v.shortName == name && !Selectable.Contains(v)) Selectable.Add(v);
            rig.menuOrbit = false;
            resultShown = false;
            Time.timeScale = 1f;
            Select(0);
            state = State.Playing;
            Hud.Toast(m.brief, ToastKind.Info);
        }

        public void StartMission(Mission m)
        {
            Time.timeScale = 1f;
            StopAllCoroutines();
            StartCoroutine(BuildWorld(m, false));
        }

        public void ToMenu()
        {
            Time.timeScale = 1f;
            StopAllCoroutines();
            StartCoroutine(BuildWorld(Mission.All[Mission.All.Count - 1], true));
        }

        public void Pause()
        {
            if (state != State.Playing) return;
            state = State.Paused;
            Time.timeScale = 0f;
        }

        public void Resume()
        {
            state = State.Playing;
            Time.timeScale = 1f;
        }

        public void ContinueAfterResult()
        {
            state = State.Playing;
            Time.timeScale = 1f;
        }

        public void Select(int i)
        {
            if (Selectable.Count == 0) return;
            i = Mathf.Clamp(i, 0, Selectable.Count - 1);
            if (Active) { Active.isActive = false; Active.input = ControlInput.None; }
            active = i;
            Active.isActive = true;
            rig.SetTarget(Active);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (state == State.Playing)
            {
                // Tastatur-Kürzel
                for (int k = 0; k < Selectable.Count && k < 9; k++) if (Input.GetKeyDown(KeyCode.Alpha1 + k)) Select(k);
                if (Input.GetKeyDown(KeyCode.Tab)) Select((active + 1) % Mathf.Max(1, Selectable.Count));
                if (Input.GetKeyDown(KeyCode.C)) rig.Cycle();
                if (Input.GetKeyDown(KeyCode.Escape)) { Pause(); return; }
                if (Input.GetKeyDown(KeyCode.F1)) ui.showHelp = !ui.showHelp;
                if (Input.GetKeyDown(KeyCode.R) && Active && Active.tipped) Active.Recover();
                var v = Active;
                if (v)
                {
                    v.input = input.state;
                    if (input.actionPressed) { var c = v.Controls(); if (c.buttons.Count > 0) v.OnButton(c.buttons[0].id); }
                }
                // Mission prüfen
                if (run != null)
                {
                    if (!run.done) run.time += dt;
                    checkT -= dt;
                    if (checkT <= 0)
                    {
                        checkT = 0.5f;
                        foreach (var veh in world.vehicles)
                            if (veh.tipped && !run.penaltyNotes.Contains(veh.shortName + " umgekippt") && !run.m.sandbox) run.Penalty(veh.shortName + " umgekippt");
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
            else if (state == State.Paused && Input.GetKeyDown(KeyCode.Escape)) { if (run != null) Resume(); else state = State.Menu; }
            rig.Tick(Time.unscaledDeltaTime, state == State.Playing || state == State.Result ? input : null);
        }
    }
}
