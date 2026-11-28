using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>
/// Oberfläche im Sofortmodus (gezeichnet in _Draw, Treffer werden je Frame gesammelt). Funktioniert mit Maus, Touch und Gamepad.
/// </summary>
public partial class GameUI : Control
{
    public Game game;
    float s = 1f;
    Font font, bold;
    StyleBoxFlat sbPanel, sbDark, sbBtn, sbBtnHover, sbBtnOn, sbBtnDown, sbBtnOff;
    readonly List<(string msg, ToastKind k, double t)> toasts = new();
    public bool showHelp;
    float listScroll;

    static readonly Color Accent = new(0.96f, 0.70f, 0.0f);
    static readonly Color OkC = new(0.27f, 0.78f, 0.4f);
    static readonly Color WarnC = new(1f, 0.48f, 0.2f);
    static readonly Color InfoC = new(0.55f, 0.7f, 1f);
    static readonly Color Text = new(0.92f, 0.91f, 0.87f);
    static readonly Color TextDim = new(0.75f, 0.74f, 0.7f);

    class Hit { public Rect2 r; public Action click; public Action<float> drag; }
    readonly List<Hit> hits = new();
    Hit pressed;
    Vector2 pointer = new(-1, -1);

    double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        var sys = new SystemFont { FontNames = new[] { "Segoe UI", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Arial" }, Antialiasing = TextServer.FontAntialiasing.Gray, SubpixelPositioning = TextServer.SubpixelPositioning.Auto };
        sys.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        font = sys;
        var sb = new SystemFont { FontNames = sys.FontNames, FontWeight = 700, Antialiasing = TextServer.FontAntialiasing.Gray };
        sb.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        bold = sb;
        sbPanel = Box(new Color(0.07f, 0.08f, 0.09f, 0.82f));
        sbDark = Box(new Color(0.05f, 0.05f, 0.06f, 0.95f));
        sbBtn = Box(new Color(0.16f, 0.17f, 0.19f, 0.92f));
        sbBtnHover = Box(new Color(0.22f, 0.23f, 0.26f, 0.95f)); sbBtnHover.BorderColor = Accent; sbBtnHover.SetBorderWidthAll(2);
        sbBtnOn = Box(Accent);
        sbBtnDown = Box(new Color(0.75f, 0.55f, 0.0f));
        sbBtnOff = Box(new Color(0.12f, 0.12f, 0.13f, 0.7f));
        Hud.OnToast += AddToast;
    }

    public override void _ExitTree() { Hud.OnToast -= AddToast; }

    void AddToast(string m, ToastKind k)
    {
        toasts.Insert(0, (m, k, Now));
        if (toasts.Count > 4) toasts.RemoveAt(toasts.Count - 1);
    }

    static StyleBoxFlat Box(Color c)
    {
        var b = new StyleBoxFlat { BgColor = c, AntiAliasing = true };
        b.SetCornerRadiusAll(12);
        return b;
    }

    // ---------------- Treffer / Eingabe ----------------
    public bool OverUI(Vector2 p)
    {
        for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].r.HasPoint(p)) return true;
        return false;
    }

    public void Press(Vector2 p)
    {
        pointer = p;
        for (int i = hits.Count - 1; i >= 0; i--)
        {
            var h = hits[i];
            if (!h.r.HasPoint(p)) continue;
            if (h.click == null && h.drag == null) return; // Blocker
            pressed = h;
            if (h.drag != null) h.drag(U.Clamp01((p.X - h.r.Position.X) / h.r.Size.X));
            return;
        }
    }

    public void Move(Vector2 p)
    {
        pointer = p;
        if (pressed?.drag != null) pressed.drag(U.Clamp01((p.X - pressed.r.Position.X) / pressed.r.Size.X));
    }

    public void Release(Vector2 p)
    {
        var h = pressed;
        pressed = null;
        if (h?.click != null && h.r.Grow(6).HasPoint(p))
        {
            SoundFX.I?.PlayUI("click", 0.4f);
            h.click();
        }
    }

    void Block(Rect2 r) => hits.Add(new Hit { r = r });

    // ---------------- Zeichenhilfen ----------------
    int Fs(float px) => Math.Max(8, (int)(px * s));

    void Panel(Rect2 r, StyleBoxFlat st = null, float alpha = 1f)
    {
        st ??= sbPanel;
        if (alpha < 1f)
        {
            var c = st.BgColor; var c0 = c; c.A *= alpha; st.BgColor = c;
            DrawStyleBox(st, r);
            st.BgColor = c0;
        }
        else DrawStyleBox(st, r);
    }

    void Label(Rect2 r, string text, float size, Color c, bool isBold = false, HorizontalAlignment al = HorizontalAlignment.Left, bool vcenter = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var f = isBold ? bold : font;
        int fs = Fs(size);
        float y = r.Position.Y + f.GetAscent(fs);
        if (vcenter) y = r.Position.Y + (r.Size.Y - f.GetHeight(fs)) * 0.5f + f.GetAscent(fs);
        DrawString(f, new Vector2(r.Position.X, y), text, al, r.Size.X, fs, c, TextServer.JustificationFlag.None);
    }

    float Wrap(Rect2 r, string text, float size, Color c, bool isBold = false, int maxLines = -1)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var f = isBold ? bold : font;
        int fs = Fs(size);
        DrawMultilineString(f, new Vector2(r.Position.X, r.Position.Y + f.GetAscent(fs)), text, HorizontalAlignment.Left, r.Size.X, fs, maxLines, c);
        return f.GetMultilineStringSize(text, HorizontalAlignment.Left, r.Size.X, fs, maxLines).Y;
    }

    float TextH(string text, float w, float size) => font.GetMultilineStringSize(text, HorizontalAlignment.Left, w, Fs(size)).Y;

    bool Button(Rect2 r, string text, bool on = false, Action click = null, float size = 16, bool enabled = true)
    {
        bool hover = enabled && r.HasPoint(pointer) && !game.input.touchMode;
        bool down = pressed != null && pressed.r == r;
        var st = !enabled ? sbBtnOff : down ? sbBtnDown : on ? sbBtnOn : hover ? sbBtnHover : sbBtn;
        DrawStyleBox(st, r);
        var col = !enabled ? new Color(0.5f, 0.5f, 0.5f) : on || down ? Colors.Black : hover ? Accent : Colors.White;
        Label(new Rect2(r.Position.X + 6, r.Position.Y, r.Size.X - 12, r.Size.Y), text, size, col, true, HorizontalAlignment.Center, true);
        if (enabled && click != null) hits.Add(new Hit { r = r, click = click });
        else Block(r);
        return hover;
    }

    void Slider(Rect2 r, float v, Action<float> set)
    {
        float cy = r.Position.Y + r.Size.Y / 2;
        DrawRect(new Rect2(r.Position.X, cy - 3 * s, r.Size.X, 6 * s), new Color(1, 1, 1, 0.18f));
        DrawRect(new Rect2(r.Position.X, cy - 3 * s, r.Size.X * v, 6 * s), Accent);
        DrawCircle(new Vector2(r.Position.X + r.Size.X * v, cy), 10 * s, Colors.White);
        hits.Add(new Hit { r = r.Grow(6 * s), drag = set });
    }

    void Stars(Vector2 pos, float size, int n, Color on, Color off)
    {
        for (int i = 0; i < 3; i++)
        {
            var c = pos + new Vector2(i * size * 1.1f + size / 2, size / 2);
            var pts = new Vector2[10];
            for (int k = 0; k < 10; k++)
            {
                float a = -MathF.PI / 2 + k * MathF.PI / 5;
                float rr = k % 2 == 0 ? size * 0.5f : size * 0.21f;
                pts[k] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
            }
            DrawColoredPolygon(pts, i < n ? on : off);
        }
    }

    // ---------------- Haupt-Zeichnen ----------------
    public override void _Process(double delta)
    {
        if (!game.input.touchMode) pointer = GetViewport().GetMousePosition();
        var vs = GetViewportRect().Size;
        if (Size != vs) { Position = Vector2.Zero; Size = vs; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (game == null) return;
        var sz = Size;
        float ns = MathF.Max(0.75f, MathF.Min(sz.Y / 760f, sz.X / 1200f) * (game.input.touchMode ? 1.2f : 1f));
        s = ns;
        foreach (var st in new[] { sbPanel, sbDark, sbBtn, sbBtnHover, sbBtnOn, sbBtnDown, sbBtnOff }) st.SetCornerRadiusAll((int)(10 * s));
        hits.Clear();
        switch (game.state)
        {
            case Game.State.Menu: DrawMenu(); break;
            case Game.State.Loading: DrawLoading(); break;
            case Game.State.Playing: DrawHud(); break;
            case Game.State.Paused: DrawHud(); DrawPause(); break;
            case Game.State.Options: DrawMenu(); DrawPause(); break;
            case Game.State.Result: DrawHud(); DrawResult(); break;
        }
        DrawToasts();
    }

    // ---------------- Hauptmenü ----------------
    void DrawMenu()
    {
        var sz = Size;
        float pad = 24 * s;
        float colW = MathF.Min(sz.X, 560 * s);
        DrawRect(new Rect2(0, 0, colW, sz.Y), new Color(0, 0, 0, 0.6f));
        Block(new Rect2(0, 0, colW, sz.Y));
        float x = pad, y = pad;
        for (int i = 0; i < 9; i++) DrawRect(new Rect2(x + i * 16 * s, y, 16 * s, 10 * s), i % 2 == 0 ? Accent : new Color(0.08f, 0.08f, 0.09f));
        y += 22 * s;
        Label(new Rect2(x, y, colW - 2 * pad, 64 * s), "Bau-Sandbox", 54, Colors.White, true);
        y += 70 * s;
        y += Wrap(new Rect2(x, y, colW - 2 * pad, 80 * s), "Graben, planieren, betonieren, Fertighäuser mit dem Kran montieren und Altbauten abreißen – mit Bodenmechanik, Statik und echter Fahrzeugphysik.", 16, Text) + 14 * s;
        float bw = colW - 2 * pad, bh = 54 * s;
        Label(new Rect2(x, y, bw, 28 * s), "Missionen", 19, Accent, true);
        y += 30 * s;
        float listTop = y, listBottom = sz.Y - bh - 2 * pad;
        var list = new List<Mission>();
        foreach (var m in Mission.All) if (!m.sandbox) list.Add(m);
        float rowH = bh + 8 * s, totalH = list.Count * rowH;
        float maxScroll = MathF.Max(0, totalH - (listBottom - listTop));
        if (game.input.wheelUI != 0) listScroll = Math.Clamp(listScroll - game.input.wheelUI * rowH * 0.6f, 0, maxScroll);
        listScroll = Math.Clamp(listScroll, 0, maxScroll);
        float yy = listTop - listScroll;
        foreach (var m in list)
        {
            if (yy + bh > listTop - 1 && yy < listBottom - bh * 0.4f)
            {
                bool un = m.Unlocked;
                var r = new Rect2(x, yy, bw, bh);
                var mm = m;
                Button(r, "", false, () => game.StartMission(mm), 16, un);
                Label(new Rect2(x + 14 * s, yy + 6 * s, bw - 160 * s, 24 * s), m.title, 17, un ? Colors.White : Colors.Gray, true);
                Label(new Rect2(x + 14 * s, yy + 30 * s, bw - 160 * s, 20 * s), string.Join(", ", m.vehicles), 13, TextDim);
                if (un)
                {
                    Stars(new Vector2(x + bw - 150 * s, yy + 10 * s), 20 * s, m.BestStars, Accent, new Color(1, 1, 1, 0.18f));
                    if (m.BestTime > 0) Label(new Rect2(x + bw - 150 * s, yy + 33 * s, 136 * s, 18 * s), "Bestzeit " + U.Time(m.BestTime), 12, TextDim);
                }
                else Label(new Rect2(x + bw - 150 * s, yy + 16 * s, 136 * s, 24 * s), "gesperrt", 15, Colors.Gray, true, HorizontalAlignment.Right);
            }
            yy += rowH;
        }
        if (maxScroll > 0)
        {
            float trackH = listBottom - listTop;
            float th = trackH * (trackH / totalH);
            DrawRect(new Rect2(x + bw + 6 * s, listTop + (trackH - th) * (listScroll / maxScroll), 4 * s, th), new Color(1, 1, 1, 0.3f));
        }
        y = sz.Y - bh - pad;
        Button(new Rect2(x, y, bw * 0.5f - 6 * s, bh), "Freies Spiel", true, () => game.StartMission(Mission.All[^1]), 20);
        Button(new Rect2(x + bw * 0.5f + 6 * s, y, bw * 0.25f - 9 * s, bh), "Optionen", false, () => game.state = Game.State.Options);
        if (!OS.HasFeature("mobile")) Button(new Rect2(x + bw * 0.75f + 3 * s, y, bw * 0.25f - 3 * s, bh), "Beenden", false, () => GetTree().Quit());
        // Hinweis unten rechts
        if (sz.X > colW + 300 * s)
        {
            var hr = new Rect2(sz.X - 380 * s - pad, sz.Y - 150 * s - pad, 380 * s, 150 * s);
            Panel(hr);
            Label(new Rect2(hr.Position.X + 14 * s, hr.Position.Y + 10 * s, hr.Size.X, 24 * s), "Steuerung am PC", 16, Accent, true);
            Wrap(new Rect2(hr.Position.X + 14 * s, hr.Position.Y + 38 * s, hr.Size.X - 28 * s, 110 * s),
                "WASD linker Hebel · IJKL / Pfeile rechter Hebel\nLeertaste/E Aktion · Q zweite Aktion · Shift feinfühlig\nMaus ziehen = Kamera · Rad = Zoom · C Kamera\n1–7 / Tab Maschine · R Bergen · Esc Pause · F11 Vollbild", 13, TextDim);
        }
    }

    void DrawLoading()
    {
        var sz = Size;
        var r = new Rect2(sz.X / 2 - 220 * s, sz.Y / 2 - 44 * s, 440 * s, 88 * s);
        Panel(r, sbDark);
        Label(r, "Baustelle wird eingerichtet …", 26, Colors.White, true, HorizontalAlignment.Center, true);
    }

    // ---------------- Im Spiel ----------------
    void DrawHud()
    {
        var sz = Size;
        float pad = 10 * s;
        var veh = game.Active;
        // Fahrzeugleiste
        float bx = pad, by = pad, bw = 96 * s, bh = 48 * s;
        for (int i = 0; i < game.Selectable.Count; i++)
        {
            var v = game.Selectable[i]; int ii = i;
            string lbl = $"{i + 1}  {v.shortName}";
            float w = MathF.Max(bw, bold.GetStringSize(lbl, HorizontalAlignment.Left, -1, Fs(15)).X + 28 * s);
            Button(new Rect2(bx, by, w, bh), lbl, v == veh, () => game.Select(ii), 15);
            bx += w + 6 * s;
        }
        // Werkzeuge rechts oben
        float tw = 110 * s, tx = sz.X - pad - tw;
        Button(new Rect2(tx, by, tw, bh), "Menü", false, () => game.Pause());
        tx -= tw + 6 * s;
        Button(new Rect2(tx, by, tw, bh), game.rig.ModeName, false, () => game.rig.Cycle());
        if (veh != null && veh.tipped) { tx -= tw * 1.2f + 6 * s; Button(new Rect2(tx, by, tw * 1.2f, bh), "Bergen", true, () => veh.Recover()); }
        // Missionsanzeige
        var run = game.run;
        if (run != null)
        {
            float mw = MathF.Min(680 * s, sz.X - 2 * pad), mx = (sz.X - mw) / 2, my = by + bh + 8 * s;
            if (bx < mx - 4 && tx > mx + mw + 4) my = by;
            var pr = new Rect2(mx, my, mw, 84 * s);
            Panel(pr);
            Label(new Rect2(mx + 14 * s, my + 8 * s, mw - 130 * s, 26 * s), run.m.sandbox ? "Freies Spiel" : run.m.title, 19, Accent, true);
            if (!run.m.sandbox) Label(new Rect2(mx + mw - 120 * s, my + 8 * s, 106 * s, 26 * s), U.Time(run.time), 19, Colors.White, true, HorizontalAlignment.Right);
            Label(new Rect2(mx + 14 * s, my + 37 * s, mw - 28 * s, 24 * s), run.status, run.status.Length > 70 ? 13 : 15, Text);
            DrawRect(new Rect2(mx + 14 * s, my + 66 * s, mw - 28 * s, 6 * s), new Color(1, 1, 1, 0.15f));
            DrawRect(new Rect2(mx + 14 * s, my + 66 * s, (mw - 28 * s) * U.Clamp01(run.progress), 6 * s), Accent);
        }
        if (veh == null || !IsInstanceValid(veh)) return;
        // Messwerte unten Mitte
        var lines = veh.HudLines();
        int n = lines.Count + 2;
        float side = game.input.touchMode ? 2 * game.input.left.radius * 2.3f : 40 * s;
        float cw = MathF.Min(156 * s, (sz.X - side) / n - 6 * s);
        float cx = (sz.X - n * (cw + 6 * s)) / 2, cy = sz.Y - 70 * s - pad;
        for (int i = 0; i < n; i++)
        {
            string k, val, sub;
            if (i < lines.Count) (k, val, sub) = lines[i];
            else if (i == lines.Count) { k = "Motor"; val = $"{Mathf.RoundToInt(veh.rpm)} 1/min"; sub = veh.SpeedKmh != 0 ? $"{U.Fmt(MathF.Abs(veh.SpeedKmh))} km/h" : "Drehzahl"; }
            else { k = "Standsicherheit"; val = Mathf.RoundToInt(veh.stability * 100) + " %"; sub = veh.stability < 0.25f ? "Kippgefahr!" : $"Neigung {Mathf.RoundToInt(veh.tiltDeg)}°"; }
            var r = new Rect2(cx + i * (cw + 6 * s), cy, cw, 66 * s);
            Panel(r);
            bool warn = sub != null && sub.Contains('!');
            Label(new Rect2(r.Position.X + 10 * s, r.Position.Y + 6 * s, cw - 16 * s, 18 * s), k, 12, TextDim);
            Label(new Rect2(r.Position.X + 10 * s, r.Position.Y + 22 * s, cw - 16 * s, 24 * s), val, 18, warn ? WarnC : Colors.White, true);
            Label(new Rect2(r.Position.X + 10 * s, r.Position.Y + 45 * s, cw - 16 * s, 18 * s), sub, 12, warn ? WarnC : TextDim);
        }
        var ctl = veh.Controls();
        if (game.input.touchMode)
        {
            DrawStick(game.input.left, ctl.lUp, ctl.lDown, ctl.lLeft, ctl.lRight);
            DrawStick(game.input.right, ctl.rUp, ctl.rDown, ctl.rLeft, ctl.rRight);
        }
        else if (showHelp || (run != null && run.time < 15f))
        {
            var hr = new Rect2(pad, MathF.Min(sz.Y - 236 * s - pad, cy - 230 * s), 340 * s, 222 * s);
            Panel(hr);
            float hy = hr.Position.Y + 10 * s, hx = hr.Position.X + 14 * s, hw = hr.Size.X - 28 * s;
            Label(new Rect2(hx, hy, hw, 20 * s), "Linker Hebel (WASD)", 14, Accent, true); hy += 22 * s;
            Label(new Rect2(hx, hy, hw, 18 * s), $"W {ctl.lUp} · S {ctl.lDown}", 13, Text); hy += 19 * s;
            Label(new Rect2(hx, hy, hw, 18 * s), $"A {ctl.lLeft} · D {ctl.lRight}", 13, Text); hy += 24 * s;
            Label(new Rect2(hx, hy, hw, 20 * s), "Rechter Hebel (IJKL / Pfeile)", 14, Accent, true); hy += 22 * s;
            if (ctl.rUp != "") Label(new Rect2(hx, hy, hw, 18 * s), $"I {ctl.rUp} · K {ctl.rDown}", 13, Text);
            hy += 19 * s;
            if (ctl.rLeft != "") Label(new Rect2(hx, hy, hw, 18 * s), $"J {ctl.rLeft} · L {ctl.rRight}", 13, Text);
            hy += 24 * s;
            Wrap(new Rect2(hx, hy, hw, 60 * s), "Leertaste/E: " + (ctl.buttons.Count > 0 ? ctl.buttons[0].label : "–") + (ctl.buttons.Count > 1 ? " · Q: " + ctl.buttons[1].label : "") + "\nShift feinfühlig · Maus Kamera · F1 Hilfe", 12, TextDim);
        }
        // Aktionsknöpfe
        float aw = 180 * s, ah = 58 * s;
        float ax = sz.X - pad - aw;
        float ay = game.input.touchMode ? game.input.right.center.Y - game.input.right.radius - ah - 46 * s : sz.Y - 150 * s - pad;
        for (int i = 0; i < ctl.buttons.Count; i++)
        {
            var b = ctl.buttons[i];
            Button(new Rect2(ax, ay, aw, ah), b.label, true, () => veh.OnButton(b.id), 17);
            if (!game.input.touchMode) Label(new Rect2(ax, ay - 18 * s, aw, 16 * s), (i == 0 ? "Leertaste · " : "Q · ") + b.hint, 11, TextDim, false, HorizontalAlignment.Center);
            ay -= ah + 26 * s;
        }
        // Kippwarnung
        if (veh.stability < 0.15f && !veh.Freeze && !veh.tipped)
        {
            float a = 0.35f + 0.25f * MathF.Sin((float)Now * 9);
            float bw2 = 26 * s;
            for (int i = 0; i < 6; i++)
            {
                var c = new Color(1, 0.25f, 0.05f, a * (1 - i / 6f) * 0.5f);
                float o = i * bw2 / 6;
                DrawRect(new Rect2(o, o, sz.X - 2 * o, sz.Y - 2 * o), c, false, bw2 / 6);
            }
            Label(new Rect2(0, sz.Y * 0.3f, sz.X, 40 * s), "KIPPGEFAHR", 30, new Color(1, 0.35f, 0.1f, 0.6f + a * 0.5f), true, HorizontalAlignment.Center);
        }
    }

    void DrawStick(GameInput.Stick st, string up, string down, string left, string right)
    {
        var c = st.center; float r = st.radius;
        DrawCircle(c, r, new Color(0, 0, 0, 0.35f));
        DrawArc(c, r, 0, MathF.Tau, 64, new Color(1, 1, 1, 0.35f), 3 * s, true);
        float kr = r * 0.42f;
        DrawCircle(c + st.knob, kr, new Color(0.08f, 0.08f, 0.09f, 0.95f));
        DrawArc(c + st.knob, kr, 0, MathF.Tau, 48, Accent, 4 * s, true);
        Label(new Rect2(c.X - 90 * s, c.Y - r - 26 * s, 180 * s, 20 * s), up, 13, Colors.White, true, HorizontalAlignment.Center);
        Label(new Rect2(c.X - 90 * s, c.Y + r + 6 * s, 180 * s, 20 * s), down, 13, Colors.White, true, HorizontalAlignment.Center);
        Label(new Rect2(c.X - r - 172 * s, c.Y - 10 * s, 162 * s, 20 * s), left, 13, Colors.White, true, HorizontalAlignment.Right);
        Label(new Rect2(c.X + r + 10 * s, c.Y - 10 * s, 162 * s, 20 * s), right, 13, Colors.White, true);
    }

    // ---------------- Pause / Optionen ----------------
    static readonly string[] QualityNames = { "Niedrig", "Mittel", "Hoch", "Ultra" };

    void DrawPause()
    {
        var sz = Size;
        DrawRect(new Rect2(Vector2.Zero, sz), new Color(0, 0, 0, 0.55f));
        float w = MathF.Min(660 * s, sz.X - 30 * s), h = MathF.Min(640 * s, sz.Y - 30 * s);
        var r = new Rect2((sz.X - w) / 2, (sz.Y - h) / 2, w, h);
        Block(new Rect2(Vector2.Zero, sz));
        Panel(r, sbDark);
        float x = r.Position.X + 22 * s, y = r.Position.Y + 18 * s, iw = w - 44 * s;
        bool inRun = game.run != null && game.state == Game.State.Paused;
        Label(new Rect2(x, y, iw, 40 * s), inRun ? "Pause" : "Optionen", 30, Colors.White, true);
        y += 48 * s;
        if (inRun)
        {
            Label(new Rect2(x, y, iw, 24 * s), game.run.m.title, 19, Accent, true); y += 28 * s;
            y += Wrap(new Rect2(x, y, iw, 60 * s), game.run.m.brief, 15, Text) + 8 * s;
            y += Wrap(new Rect2(x, y, iw, 60 * s), "Tipp: " + game.run.m.tip, 13, TextDim) + 14 * s;
        }
        float lw = 220 * s, cx = x + lw, cw = iw - lw;
        Label(new Rect2(x, y, lw, 34 * s), "Steuerung", 15, Text, false, HorizontalAlignment.Left, true);
        Button(new Rect2(cx, y, cw / 2 - 3 * s, 34 * s), "Standard", !Settings.Iso, () => { Settings.Iso = false; game.SaveSettings(); }, 14);
        Button(new Rect2(cx + cw / 2 + 3 * s, y, cw / 2 - 3 * s, 34 * s), "ISO (wie im Bagger)", Settings.Iso, () => { Settings.Iso = true; game.SaveSettings(); }, 14);
        y += 44 * s;
        Label(new Rect2(x, y, lw, 30 * s), $"Lautstärke {Mathf.RoundToInt(Settings.Volume * 100)} %", 15, Text, false, HorizontalAlignment.Left, true);
        Slider(new Rect2(cx + 10 * s, y, cw - 20 * s, 30 * s), Settings.Volume, v => { Settings.Volume = v; game.ApplyVolume(); game.SaveSettings(); });
        y += 42 * s;
        Label(new Rect2(x, y, lw, 34 * s), "Grafik", 15, Text, false, HorizontalAlignment.Left, true);
        float qw = (cw - 3 * 6 * s) / 4;
        for (int i = 0; i < 4; i++) { int q = i; Button(new Rect2(cx + i * (qw + 6 * s), y, qw, 34 * s), QualityNames[i], Settings.Quality == i, () => { game.ApplyQuality(q); game.SaveSettings(); }, 14); }
        y += 44 * s;
        bool fs = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
        if (!OS.HasFeature("mobile"))
        {
            Label(new Rect2(x, y, lw, 34 * s), "Anzeige", 15, Text, false, HorizontalAlignment.Left, true);
            Button(new Rect2(cx, y, cw / 2 - 3 * s, 34 * s), "Fenster", !fs, () => game.SetFullscreen(false), 14);
            Button(new Rect2(cx + cw / 2 + 3 * s, y, cw / 2 - 3 * s, 34 * s), "Vollbild", fs, () => game.SetFullscreen(true), 14);
            y += 44 * s;
        }
        Label(new Rect2(x, y, lw, 34 * s), "Sterne", 15, Text, false, HorizontalAlignment.Left, true);
        bool all = Save.GetInt("unlock_all") == 1;
        Button(new Rect2(cx, y, cw, 34 * s), all ? "Alle Missionen freigeschaltet" : "Alle Missionen freischalten", all, () => { Save.Set("unlock_all", all ? 0 : 1); Save.Flush(); }, 14);
        float bh = 50 * s, bw = (iw - 12 * s) / 3, byy = r.End.Y - bh - 18 * s;
        if (inRun)
        {
            Button(new Rect2(x, byy, bw, bh), "Weiter", true, () => game.Resume());
            Button(new Rect2(x + bw + 6 * s, byy, bw, bh), "Neu starten", false, () => game.StartMission(game.run.m));
            Button(new Rect2(x + 2 * (bw + 6 * s), byy, bw, bh), "Hauptmenü", false, () => game.ToMenu());
        }
        else Button(new Rect2(x, byy, iw, bh), "Zurück", true, () => game.state = Game.State.Menu);
    }

    void DrawResult()
    {
        var run = game.run;
        var sz = Size;
        DrawRect(new Rect2(Vector2.Zero, sz), new Color(0, 0, 0, 0.5f));
        Block(new Rect2(Vector2.Zero, sz));
        float w = MathF.Min(540 * s, sz.X - 30 * s), h = 350 * s;
        var r = new Rect2((sz.X - w) / 2, (sz.Y - h) / 2, w, h);
        Panel(r, sbDark);
        float x = r.Position.X + 22 * s, y = r.Position.Y + 18 * s;
        Label(new Rect2(x, y, w, 40 * s), "Auftrag erledigt!", 30, Colors.White, true); y += 46 * s;
        Label(new Rect2(x, y, w, 26 * s), run.m.title, 19, Accent, true); y += 36 * s;
        Stars(new Vector2(x, y), 58 * s, run.stars, Accent, new Color(1, 1, 1, 0.15f)); y += 74 * s;
        Label(new Rect2(x, y, w - 40 * s, 26 * s), $"Zeit {U.Time(run.time)}   (3 Sterne unter {Mathf.FloorToInt(run.m.threeStar / 60)} min)", 16, Text); y += 28 * s;
        if (run.penaltyNotes.Count > 0) Wrap(new Rect2(x, y, w - 44 * s, 40 * s), "Abzüge: " + string.Join(", ", run.penaltyNotes), 13, WarnC);
        float bw = (w - 50 * s) / 2, bh = 50 * s;
        int idx = Mission.All.IndexOf(run.m);
        bool hasNext = idx >= 0 && idx + 1 < Mission.All.Count && !Mission.All[idx + 1].sandbox;
        if (hasNext) Button(new Rect2(x, r.End.Y - bh - 18 * s, bw, bh), "Nächste Mission", true, () => game.StartMission(Mission.All[idx + 1]));
        Button(new Rect2(hasNext ? x + bw + 6 * s : x, r.End.Y - bh - 18 * s, hasNext ? bw : w - 44 * s, bh), "Weiterspielen", false, () => game.ContinueAfterResult());
        Button(new Rect2(r.End.X - 140 * s, r.Position.Y + 16 * s, 120 * s, 40 * s), "Menü", false, () => game.ToMenu());
    }

    void DrawToasts()
    {
        var sz = Size;
        double now = Now;
        float w = MathF.Min(580 * s, sz.X - 30 * s), x = (sz.X - w) / 2, y = 160 * s;
        if (game.state == Game.State.Menu || game.state == Game.State.Options) { x = sz.X - w - 24 * s; y = 24 * s; }
        for (int i = toasts.Count - 1; i >= 0; i--) if (now - toasts[i].t > 6.0) toasts.RemoveAt(i);
        foreach (var t in toasts)
        {
            float a = U.Clamp01((float)(6.0 - (now - t.t)) * 2f);
            float h = TextH(t.msg, w - 34 * s, 15) + 20 * s;
            Panel(new Rect2(x, y, w, h), sbDark, a);
            var c = t.k == ToastKind.Ok ? OkC : t.k == ToastKind.Warn ? WarnC : InfoC;
            c.A = a;
            DrawRect(new Rect2(x + 5 * s, y + 7 * s, 4 * s, h - 14 * s), c);
            var tc = Text; tc.A = a;
            Wrap(new Rect2(x + 18 * s, y + 10 * s, w - 34 * s, h), t.msg, 15, tc);
            y += h + 6 * s;
        }
    }
}
