using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>
/// Automatischer Testlauf (nur mit Befehlszeile: -- --test=name --out=ordner). Fährt Maschinen per Skript,
/// protokolliert Messwerte und speichert Bildschirmfotos. Für Entwicklung und Abnahme.
/// </summary>
public partial class TestDriver : Node
{
    public Game game;
    public string[] args;
    string name = "menu", outDir = "user://test";
    int tick, stepTick, step;
    readonly List<Step> steps = new();
    bool started;
    double frameTimeAcc; int frames;

    class Step
    {
        public string label;
        public int ticks;
        public ControlInput inp;
        public Action begin;
        public Func<ControlInput> auto;   // Regler je Tick
        public Func<bool> until;          // vorzeitiges Ende
        public string shot;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        foreach (var a in args)
        {
            if (a.StartsWith("--test=")) name = a[7..];
            if (a.StartsWith("--out=")) outDir = a[6..];
        }
        DirAccess.MakeDirRecursiveAbsolute(outDir);
        Log($"Test '{name}' startet, Qualität {Settings.Quality}");
        Setup();
    }

    void Log(string s) => GD.Print($"[TEST t={tick / 100f:0.00}s] {s}");

    void Add(string label, int ticks, ControlInput inp = default, Action begin = null, string shot = null, Func<ControlInput> auto = null, Func<bool> until = null)
        => steps.Add(new Step { label = label, ticks = ticks, inp = inp, begin = begin, shot = shot, auto = auto, until = until });

    static ControlInput In(float lx = 0, float ly = 0, float rx = 0, float ry = 0) => new() { lx = lx, ly = ly, rx = rx, ry = ry };

    Mission M(string id) => Mission.All.Find(m => m.id == id);

    World W => game.world;

    void Setup()
    {
        switch (name)
        {
            case "menu":
                game.BuildWorld(Mission.All[^1], true);
                Add("Menü", 250, shot: "menu");
                Add("Optionen", 40, begin: () => game.state = Game.State.Options, shot: "options");
                break;
            case "dig":
                game.StartMission(M("m1"));
                Add("Setzen", 80, shot: "dig_start");
                Add("Ausholen", 160, In(0, 1, 1, 1));
                Add("Absenken", 220, In(0, 0, 0.3f, -1), until: () => W.excavator.tool.resist > 0.1f);
                Add("Graben", 320, In(0, -1, -0.7f, -0.25f), shot: "dig_cut");
                Add("Ankippen", 120, In(0, -0.3f, -1, 0.3f));
                Add("Heben", 360, In(0, 0.2f, -0.2f, 1), shot: "dig_lift");
                Add("Schwenken", 260, In(1, 0, 0, 0.2f));
                Add("Auskippen", 160, In(0, 0.3f, 1, 0), shot: "dig_dump");
                Add("Zurück", 260, In(-1, 0, 0, 0));
                Add("Kabine", 60, begin: () => game.rig.Cycle(), shot: "dig_cabin");
                break;
            case "truck":
                game.StartMission(M("m2"));
                Add("Kipper wählen", 60, begin: () => game.Select(1), shot: "truck_start");
                Add("Gas", 700, In(0, 1));
                Add("Lenken", 300, In(1, 0.6f), shot: "truck_drive");
                Add("Bremsen", 300, In(0, -1));
                Add("Mulde", 400, In(0, 0, 0, 1), shot: "truck_bed");
                Add("Mulde ab", 300, In(0, 0, 0, -1));
                break;
            case "loader":
                game.StartMission(M("m3"));
                Add("Setzen", 60, shot: "loader_start");
                Add("Gas", 600, In(0, 1, 0, 0));
                Add("Lenken", 300, In(-1, 0.5f));
                Add("Heben", 300, In(0, 0, -1, 1), shot: "loader_lift");
                Add("Auskippen", 200, In(0, 0, 1, 0));
                Add("Rückwärts", 400, In(0.5f, -1));
                break;
            case "doze":
                game.StartMission(M("m4"));
                Add("Setzen", 60, shot: "doze_start");
                Add("Schild ab", 120, In(0, 0, 0, -1));
                Add("Schieben", 900, In(0, 0.8f), shot: "doze_push");
                Add("Schild auf", 120, In(0, 0, 0, 1));
                Add("Zurück", 600, In(0, -0.8f), shot: "doze_back");
                break;
            case "crane":
                game.StartMission(M("m6"));
                CraneSteps();
                break;
            case "wreck":
                game.StartMission(M("m8"));
                Add("Setzen", 60, shot: "wreck_start");
                Add("Fahrmodus", 1, begin: () => W.wrecker.OnButton("mode"));
                Add("Anfahren", 1500, In(0, 1), until: () => W.wrecker.GlobalPosition.DistanceTo(new Vector3(Layout.House.X, W.wrecker.GlobalPosition.Y, Layout.House.Y)) < 15f);
                Add("Anhalten", 150, In(0, -0.3f));
                Add("Arbeitsmodus", 1, begin: () => W.wrecker.OnButton("mode"));
                Add("Ausrichten", 600, auto: () => WreckAim(0), until: () => MathF.Abs(WreckErr()) < 0.05f);
                Add("Ausleger ab", 120, In(0, 1, 0, 0));
                Add("Aufschaukeln", 1800, auto: () => WreckPump(), shot: "wreck_swing");
                Add("Weiter", 1500, auto: () => WreckPump(), shot: "wreck_end");
                break;
            case "mixer":
                game.StartMission(M("m5"));
                Add("Setzen", 60, shot: "mixer_start");
                Add("Gas", 500, In(0, 1));
                Add("Schurre", 200, In(0, 0, 1, -1), shot: "mixer_chute");
                Add("Ablassen", 300, begin: () => W.mixer.OnButton("pour"), shot: "mixer_pour");
                break;
            case "free":
                game.StartMission(Mission.All[^1]);
                Add("Setzen", 120, shot: "free_bagger");
                string[] names = { "radlader", "raupe", "kipper", "mischer", "kran", "birne" };
                for (int i = 1; i < 7; i++) { int ii = i; Add("Wahl " + i, 90, begin: () => game.Select(ii), shot: "free_" + names[i - 1]); }
                Add("Kabine", 60, begin: () => { game.Select(0); game.rig.Cycle(); }, shot: "free_cabin");
                break;
        }
    }

    // ---------------- Abrissbirne ----------------
    float WreckErr()
    {
        var w = W.wrecker;
        float tgt = MathF.Atan2(Layout.House.X - w.GlobalPosition.X, Layout.House.Y - w.GlobalPosition.Z);
        return Mathf.AngleDifference(w.CameraYaw(), tgt);
    }
    ControlInput WreckAim(float off) => In(-Math.Clamp((WreckErr() + off) * 3f, -1, 1));
    ControlInput WreckPump()
    {
        var w = W.wrecker;
        var lat = w.ball.GlobalTransform.Origin - w.BoomTip();
        var side = new Vector3(MathF.Cos(w.CameraYaw()), 0, -MathF.Sin(w.CameraYaw()));
        float vLat = w.ball.LinearVelocity.Dot(side);
        float err = WreckErr();
        float cmd = -MathF.Sign(vLat);
        if (MathF.Abs(err) > 0.45f) cmd = MathF.Sign(err);
        return In(-cmd);
    }

    // ---------------- Kran-Autopilot ----------------
    Element el; float lTarget;
    void CraneSteps()
    {
        Add("Warten", 4000, until: () => Buildings.I.elements.Exists(e => e.state == "stored"));
        Add("Ansicht", 60, shot: "crane_start");
        Add("Teil wählen", 1, begin: () =>
        {
            el = null;
            foreach (var e in Buildings.I.elements) if (e.state == "stored" && e.def.tier == 0) { el = e; break; }
            Log(el != null ? $"Fertigteil {el.def.name} {U.Fmt(el.MassT)} t bei {el.GlobalPosition}" : "kein Fertigteil!");
        });
        Add("Hinfahren", 4000, auto: () => CraneGo(el.LiftPointWorld, el.LiftPointWorld.Y + 4f), until: () => CraneAt(el.LiftPointWorld, 0.5f));
        Add("Absenken", 3000, auto: () => CraneGo(el.LiftPointWorld, el.LiftPointWorld.Y + 0.6f), until: () => game.world.crane.hook.GlobalPosition.Y - el.LiftPointWorld.Y < 0.9f && CraneAt(el.LiftPointWorld, 0.5f), shot: "crane_lower");
        Add("Einhängen", 30, begin: () => { game.world.crane.OnButton("hook"); Log("Last: " + (game.world.crane.load != null)); });
        Add("Anheben", 1500, auto: () => CraneGo(null, el.LiftPointWorld.Y + 0f, liftTo: el.def.target.Y + el.def.size.Y + 4.5f), until: () => el.GlobalPosition.Y > el.def.target.Y + 3.5f);
        Add("Zum Ziel", 6000, auto: () => CraneGo(el.def.target + Vector3.Up * (el.def.size.Y + 0.1f), 0, liftTo: el.def.target.Y + el.def.size.Y + 4.5f), until: () => CraneAt(el.def.target + Vector3.Up * (el.def.size.Y + 0.1f), 0.35f) && U.Flat(el.LinearVelocity).Length() < 0.15f, shot: "crane_move");
        Add("Ablassen", 3000, auto: () => CraneGo(el.def.target + Vector3.Up * (el.def.size.Y + 0.1f), 0, liftTo: el.def.target.Y + el.def.size.Y + 0.25f), until: () => el.site.CanPlace(el), shot: "crane_set");
        Add("Ausklinken", 60, begin: () => { game.world.crane.OnButton("hook"); Log($"Montiert: {el.site.placed.Count}"); }, shot: "crane_done");
    }

    bool CraneAt(Vector3 p, float tol)
    {
        var c = game.world.crane;
        var t = c.TrolleyWorld();
        return new Vector2(t.X - p.X, t.Z - p.Z).Length() < tol;
    }

    /// <summary>Regelt Drehwerk, Katze und Hubwerk auf einen Punkt (Katze über p) und eine Hakenhöhe.</summary>
    ControlInput CraneGo(Vector3? p, float hookY, float liftTo = float.NaN)
    {
        var c = game.world.crane;
        var b = c.GlobalPosition;
        var inp = new ControlInput();
        if (p.HasValue)
        {
            var q = p.Value;
            float ang = MathF.Atan2(q.X - b.X, q.Z - b.Z);
            float rad = new Vector2(q.X - b.X, q.Z - b.Z).Length();
            float aErr = Mathf.AngleDifference(c.slew, ang);
            // Drehwerk: Bremsweg berücksichtigen (v² / 2a)
            float stopA = c.slewV * MathF.Abs(c.slewV) / (2 * 0.045f);
            inp.lx = -Math.Clamp((aErr - stopA) * 6f, -1, 1);
            float stopR = c.rV * MathF.Abs(c.rV) / (2 * 0.6f);
            inp.ly = Math.Clamp((rad - c.r - stopR) * 1.5f, -1, 1);
        }
        var hook = c.hook.GlobalPosition;
        float anchorY = c.TrolleyWorld().Y;
        float Lt;
        if (!float.IsNaN(liftTo)) Lt = anchorY - liftTo - 0.1f;        // Last: Anschlagpunkt auf liftTo
        else Lt = anchorY - 0.7f - hookY;
        if (c.load != null && !float.IsNaN(liftTo)) Lt = anchorY - liftTo;
        float stopL = c.LV * MathF.Abs(c.LV) / (2 * 1.5f);
        inp.ry = -Math.Clamp((Lt - c.L - stopL) * 0.8f, -1, 1);
        return inp;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (game.state == Game.State.Loading) return;
        if (!started) { started = true; tick = 0; step = 0; stepTick = 0; if (steps.Count > 0) Begin(steps[0]); }
        tick++;
        if (step >= steps.Count)
        {
            if (tick > 2) Finish();
            return;
        }
        var s = steps[step];
        game.forcedInput = s.auto != null ? s.auto() : s.inp;
        stepTick++;
        if (tick % 100 == 0) Report();
        bool early = s.until != null && stepTick > 5 && s.until();
        if (stepTick >= s.ticks || early)
        {
            if (s.until != null) Log($"Schritt '{s.label}' {(early ? "erreicht" : "ZEITÜBERSCHREITUNG")} nach {stepTick} Ticks");
            if (s.shot != null) Shot(s.shot);
            step++;
            stepTick = 0;
            if (step < steps.Count) Begin(steps[step]);
        }
    }

    public override void _Process(double delta) { frameTimeAcc += delta; frames++; }

    void Begin(Step s)
    {
        Log($"→ {s.label}");
        s.begin?.Invoke();
    }

    void Report()
    {
        var v = game.Active;
        string extra = "";
        if (W != null)
        {
            if (v is Excavator ex) extra = $"Löffel {U.Fmt(ex.tool.load, 2)} m³, Widerstand {U.Fmt(ex.tool.resist, 2)}, Aushub Zone {U.Fmt(W.soil.ExcavatedRect(new Vector3(7, 0, 6), 3, 2, 0, 3), 2)}";
            if (v is WheelLoader wl) extra = $"Schaufel {U.Fmt(wl.tool.load, 2)} m³";
            if (v is DumpTruck dt) extra = $"Ladung {U.Fmt(dt.cargo, 2)} m³";
            if (v is TowerCrane tc) extra = $"r {U.Fmt(tc.r)} slew {U.Fmt(tc.slew, 2)} L {U.Fmt(tc.L)} Haken {tc.hook.GlobalPosition} Last {(tc.load != null ? tc.load.GlobalPosition.ToString() : "-")}";
            if (v is WreckingCrane wc) extra = $"Birne v {U.Fmt(wc.ball.LinearVelocity.Length())} m/s, Haus {W.oldHouse?.alive}/{W.oldHouse?.total}";
        }
        double fps = frames > 0 ? frames / Math.Max(1e-6, frameTimeAcc) : 0;
        frames = 0; frameTimeAcc = 0;
        if (v != null) Log($"{v.shortName} pos {v.GlobalPosition.ToString("0.0")} v {U.Fmt(v.SpeedKmh)} km/h kipp {U.Fmt(v.stability, 2)} rpm {Mathf.RoundToInt(v.rpm)} | {extra} | {fps:0.0} fps | {game.run?.status}");
    }

    void Shot(string n)
    {
        var path = outDir.PathJoin(n + ".png");
        game.Screenshot(path);
        Log("Bild " + path);
    }

    void Finish()
    {
        Report();
        Log("Test fertig: " + (game.run != null ? $"{game.run.status}, Fortschritt {U.Fmt(game.run.progress * 100, 0)} %" : ""));
        GetTree().Quit();
    }
}
