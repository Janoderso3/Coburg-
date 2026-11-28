using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Oberfläche (IMGUI, funktioniert in HDRP und URP, mit Touch und Maus).</summary>
    public class GameUI : MonoBehaviour
    {
        public Game game;
        float s = 1f;
        GUIStyle panel, panelDark, btn, btnOn, btnAccent, title, h2, label, small, value, cellK, cellV, toastS, big;
        Texture2D texPanel, texDark, texBtn, texBtnOn, texAccent, texWhite, texCircle, texRing;
        readonly List<(string msg, ToastKind k, float t)> toasts = new List<(string, ToastKind, float)>();
        public bool showHelp;
        Vector2 scroll;
        static readonly Color Accent = new Color(0.96f, 0.70f, 0.0f);
        static readonly Color Ok = new Color(0.27f, 0.78f, 0.4f);
        static readonly Color Warn = new Color(1f, 0.48f, 0.2f);

        void Awake()
        {
            Hud.OnToast += (m, k) => { toasts.Insert(0, (m, k, Time.unscaledTime)); if (toasts.Count > 4) toasts.RemoveAt(toasts.Count - 1); };
        }

        static Texture2D Rounded(Color c, int r = 12, int size = 32)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r))), dy = Mathf.Max(0, Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    var cc = c; cc.a *= Mathf.Clamp01(r - d + 0.5f);
                    px[y * size + x] = cc;
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D Circle(Color fill, Color ring, int size = 128, float ringW = 0.06f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c = d < 1 - ringW ? fill : d < 1 ? ring : new Color(0, 0, 0, 0);
                    c.a *= Mathf.Clamp01((1 - d) * size * 0.5f);
                    px[y * size + x] = c;
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        void Styles()
        {
            float ns = Mathf.Max(0.75f, Mathf.Min(Screen.height / 760f, Screen.width / 1200f) * (Application.isMobilePlatform ? 1.25f : 1f));
            if (panel != null && Mathf.Abs(ns - s) < 0.01f) return;
            s = ns;
            if (!texPanel)
            {
                texPanel = Rounded(new Color(0.07f, 0.08f, 0.09f, 0.82f));
                texDark = Rounded(new Color(0.05f, 0.05f, 0.06f, 0.95f));
                texBtn = Rounded(new Color(0.16f, 0.17f, 0.19f, 0.92f));
                texBtnOn = Rounded(Accent);
                texAccent = Rounded(new Color(Accent.r, Accent.g, Accent.b, 1f));
                texWhite = Texture2D.whiteTexture;
                texCircle = Circle(new Color(0, 0, 0, 0.35f), new Color(1, 1, 1, 0.35f));
                texRing = Circle(new Color(0.08f, 0.08f, 0.09f, 0.95f), Accent, 64, 0.12f);
            }
            GUIStyle Box(Texture2D bg) { var g = new GUIStyle(); g.normal.background = bg; g.border = new RectOffset(12, 12, 12, 12); g.padding = new RectOffset((int)(12 * s), (int)(12 * s), (int)(10 * s), (int)(10 * s)); return g; }
            panel = Box(texPanel);
            panelDark = Box(texDark);
            btn = Box(texBtn);
            btn.normal.textColor = Color.white; btn.hover.background = texBtn; btn.hover.textColor = Accent; btn.active.background = texBtnOn; btn.active.textColor = Color.black;
            btn.alignment = TextAnchor.MiddleCenter; btn.fontSize = (int)(16 * s); btn.fontStyle = FontStyle.Bold; btn.wordWrap = true;
            btnOn = new GUIStyle(btn); btnOn.normal.background = texBtnOn; btnOn.normal.textColor = Color.black; btnOn.hover.background = texBtnOn; btnOn.hover.textColor = Color.black;
            btnAccent = new GUIStyle(btnOn); btnAccent.fontSize = (int)(20 * s);
            title = new GUIStyle { fontSize = (int)(54 * s), fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            big = new GUIStyle { fontSize = (int)(30 * s), fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, wordWrap = true };
            h2 = new GUIStyle { fontSize = (int)(19 * s), fontStyle = FontStyle.Bold, normal = { textColor = Accent }, wordWrap = true };
            label = new GUIStyle { fontSize = (int)(16 * s), normal = { textColor = new Color(0.92f, 0.91f, 0.87f) }, wordWrap = true, richText = true };
            small = new GUIStyle(label) { fontSize = (int)(13 * s), normal = { textColor = new Color(0.75f, 0.74f, 0.7f) } };
            value = new GUIStyle(label) { fontSize = (int)(18 * s), fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, wordWrap = false, clipping = TextClipping.Clip };
            cellK = new GUIStyle(small) { wordWrap = false, clipping = TextClipping.Clip };
            cellV = new GUIStyle(value);
            toastS = new GUIStyle(label) { fontSize = (int)(15 * s) };
        }

        Rect R(float x, float y, float w, float h) { return new Rect(x, y, w, h); }

        bool Button(Rect r, string text, GUIStyle st = null)
        {
            game.input.uiRects.Add(r);
            bool b = GUI.Button(r, text, st ?? btn);
            if (b) SoundFX.I?.PlayUI("click", 0.4f);
            return b;
        }

        void OnGUI()
        {
            if (!game) return;
            Styles();
            game.input.uiRects.Clear();
            GUI.depth = 0;
            switch (game.state)
            {
                case Game.State.Menu: DrawMenu(); break;
                case Game.State.Loading: DrawLoading(); break;
                case Game.State.Playing: DrawHud(); break;
                case Game.State.Paused: DrawHud(); DrawPause(); break;
                case Game.State.Result: DrawHud(); DrawResult(); break;
            }
            DrawToasts();
        }

        Rect Safe => new Rect(Screen.safeArea.x, Screen.height - Screen.safeArea.yMax, Screen.safeArea.width, Screen.safeArea.height);

        // ---------------- Hauptmenü ----------------
        void DrawMenu()
        {
            var sa = Safe;
            float pad = 24 * s;
            // linke Spalte: Titel
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Mathf.Min(Screen.width, 560 * s + sa.x), Screen.height), texWhite);
            GUI.color = Color.white;
            float x = sa.x + pad, y = sa.y + pad;
            // Warnstreifen
            for (int i = 0; i < 9; i++)
            {
                GUI.color = i % 2 == 0 ? Accent : new Color(0.08f, 0.08f, 0.09f);
                GUI.DrawTexture(new Rect(x + i * 16 * s, y, 16 * s, 10 * s), texWhite);
            }
            GUI.color = Color.white;
            y += 22 * s;
            GUI.Label(R(x, y, 520 * s, 64 * s), "Bau-Sandbox", title);
            y += 66 * s;
            GUI.Label(R(x, y, 500 * s, 70 * s), "Graben, planieren, betonieren, Fertighäuser mit dem Kran montieren und Altbauten abreißen – mit Bodenmechanik, Statik und echter Fahrzeugphysik.", label);
            y += 78 * s;
            float bw = 500 * s, bh = 54 * s;
            GUI.Label(R(x, y, bw, 28 * s), "Missionen", h2);
            y += 30 * s;
            float listH = Screen.height - y - sa.y - bh - 3 * pad;
            int total = 0; foreach (var m in Mission.All) if (!m.sandbox) total++;
            var view = new Rect(0, 0, bw - 16 * s, total * (bh + 8 * s));
            var area = R(x, y, bw, Mathf.Max(bh * 2, listH));
            game.input.uiRects.Add(area);
            scroll = GUI.BeginScrollView(area, scroll, view, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            float yy = 0;
            foreach (var m in Mission.All)
            {
                if (m.sandbox) continue;
                bool un = m.Unlocked;
                GUI.enabled = un;
                string stars = new string('★', m.BestStars) + new string('☆', 3 - m.BestStars);
                string bt = m.BestTime > 0 ? $"   {Mathf.FloorToInt(m.BestTime / 60)}:{Mathf.FloorToInt(m.BestTime % 60):00}" : "";
                if (GUI.Button(R(0, yy, view.width, bh), "", btn)) { SoundFX.I?.PlayUI("click"); game.StartMission(m); }
                GUI.Label(R(14 * s, yy + 6 * s, view.width - 150 * s, bh), m.title, new GUIStyle(label) { fontStyle = FontStyle.Bold, fontSize = (int)(17 * s), wordWrap = false });
                GUI.Label(R(14 * s, yy + 28 * s, view.width - 150 * s, bh), string.Join(", ", m.vehicles), small);
                GUI.Label(R(view.width - 150 * s, yy + 14 * s, 140 * s, bh), un ? stars + bt : "gesperrt", new GUIStyle(value) { alignment = TextAnchor.UpperRight, normal = { textColor = un ? Accent : Color.gray } });
                GUI.enabled = true;
                yy += bh + 8 * s;
            }
            GUI.EndScrollView();
            y = Screen.height - sa.y - bh - pad;
            if (Button(R(x, y, bw * 0.5f - 6 * s, bh), "Freies Spiel", btnAccent)) game.StartMission(Mission.All[Mission.All.Count - 1]);
            if (Button(R(x + bw * 0.5f + 6 * s, y, bw * 0.25f - 9 * s, bh), "Optionen")) game.state = Game.State.Paused;
            if (!Application.isMobilePlatform && Button(R(x + bw * 0.75f + 3 * s, y, bw * 0.25f - 3 * s, bh), "Beenden")) Application.Quit();
        }

        void DrawLoading()
        {
            var r = R(Screen.width / 2 - 200 * s, Screen.height / 2 - 40 * s, 400 * s, 80 * s);
            GUI.Box(r, "", panelDark);
            GUI.Label(new Rect(r.x + 20 * s, r.y + 22 * s, r.width, r.height), "Baustelle wird eingerichtet …", big);
        }

        // ---------------- Im Spiel ----------------
        void DrawHud()
        {
            var sa = Safe;
            float pad = 10 * s;
            var veh = game.Active;
            // Fahrzeugleiste
            float bx = sa.x + pad, by = sa.y + pad, bw = 92 * s, bh = 50 * s;
            for (int i = 0; i < game.Selectable.Count; i++)
            {
                var v = game.Selectable[i];
                if (Button(R(bx, by, bw, bh), $"{i + 1}  {v.shortName}", v == veh ? btnOn : btn)) game.Select(i);
                bx += bw + 6 * s;
            }
            // Werkzeuge rechts oben
            float tw = 104 * s, tx = sa.xMax - pad - tw;
            if (Button(R(tx, by, tw, bh), "Menü")) game.Pause();
            tx -= tw + 6 * s;
            if (Button(R(tx, by, tw, bh), game.rig.ModeName)) game.rig.Cycle();
            if (veh && veh.tipped) { tx -= tw * 1.3f + 6 * s; if (Button(R(tx, by, tw * 1.3f, bh), "Bergen", btnOn)) veh.Recover(); }
            // Missionsanzeige
            var run = game.run;
            if (run != null)
            {
                float mw = Mathf.Min(520 * s, Screen.width - 2 * pad), mx = (Screen.width - mw) / 2, my = by + bh + 8 * s;
                if (bx > mx - 6 * s && game.Selectable.Count > 3) my = by + bh + 8 * s;
                var pr = R(mx, my, mw, 84 * s);
                GUI.Box(pr, "", panel);
                string t = run.m.sandbox ? "Freies Spiel" : run.m.title;
                GUI.Label(R(mx + 12 * s, my + 8 * s, mw - 120 * s, 26 * s), t, h2);
                if (!run.m.sandbox) GUI.Label(R(mx + mw - 110 * s, my + 8 * s, 100 * s, 26 * s), $"{Mathf.FloorToInt(run.time / 60)}:{Mathf.FloorToInt(run.time % 60):00}", new GUIStyle(value) { alignment = TextAnchor.UpperRight });
                GUI.Label(R(mx + 12 * s, my + 34 * s, mw - 24 * s, 24 * s), run.status, label);
                GUI.color = new Color(1, 1, 1, 0.15f);
                GUI.DrawTexture(R(mx + 12 * s, my + 64 * s, mw - 24 * s, 6 * s), texWhite);
                GUI.color = Accent;
                GUI.DrawTexture(R(mx + 12 * s, my + 64 * s, (mw - 24 * s) * Mathf.Clamp01(run.progress), 6 * s), texWhite);
                GUI.color = Color.white;
            }
            if (!veh) return;
            // Messwerte unten Mitte
            var lines = veh.HudLines();
            int n = lines.Count + 1;
            float cw = Mathf.Min(150 * s, (Screen.width - (game.input.touchMode ? 2 * (game.input.left.radius * 2.2f) : 40 * s)) / n - 6 * s);
            float cx = (Screen.width - n * (cw + 6 * s)) / 2, cy = Screen.height - sa.y - 66 * s - pad;
            for (int i = 0; i < n; i++)
            {
                string k, val, sub;
                if (i < lines.Count) (k, val, sub) = lines[i];
                else { k = "Standsicherheit"; val = Mathf.RoundToInt(veh.stability * 100) + " %"; sub = veh.stability < 0.25f ? "Kippgefahr!" : $"Neigung {Mathf.RoundToInt(veh.tiltDeg)}°"; }
                var r = R(cx + i * (cw + 6 * s), cy, cw, 64 * s);
                GUI.Box(r, "", panel);
                bool warn = sub.Contains("!");
                GUI.Label(R(r.x + 8 * s, r.y + 6 * s, cw - 12 * s, 18 * s), k, cellK);
                GUI.Label(R(r.x + 8 * s, r.y + 22 * s, cw - 12 * s, 24 * s), val, new GUIStyle(cellV) { normal = { textColor = warn ? Warn : Color.white } });
                GUI.Label(R(r.x + 8 * s, r.y + 44 * s, cw - 12 * s, 18 * s), sub, new GUIStyle(cellK) { normal = { textColor = warn ? Warn : cellK.normal.textColor } });
            }
            // Joysticks (Touch) mit Beschriftung
            var ctl = veh.Controls();
            if (game.input.touchMode)
            {
                DrawStick(game.input.left, ctl.lUp, ctl.lDown, ctl.lLeft, ctl.lRight);
                DrawStick(game.input.right, ctl.rUp, ctl.rDown, ctl.rLeft, ctl.rRight);
            }
            else if (showHelp || game.run != null && game.run.time < 12f)
            {
                var hr = R(sa.x + pad, Screen.height - sa.y - 200 * s, 330 * s, 180 * s);
                GUI.Box(hr, "", panel);
                string txt = $"<b>Linker Stick (WASD)</b>\nW {ctl.lUp} · S {ctl.lDown}\nA {ctl.lLeft} · D {ctl.lRight}\n<b>Rechter Stick (IJKL / Pfeile)</b>\nI {ctl.rUp} · K {ctl.rDown}\nJ {ctl.rLeft} · L {ctl.rRight}\nShift = feinfühlig · Maus = Kamera · F1 Hilfe";
                GUI.Label(R(hr.x + 12 * s, hr.y + 10 * s, hr.width - 24 * s, hr.height), txt, small);
            }
            // Aktionsknöpfe
            float ax = Screen.width - sa.x - pad - 170 * s;
            float ay = game.input.touchMode ? game.input.right.center.y : 0;
            ay = game.input.touchMode ? Screen.height - (game.input.right.center.y + game.input.right.radius + 70 * s) : Screen.height - sa.y - 150 * s;
            foreach (var b in ctl.buttons)
            {
                if (Button(R(ax, ay, 170 * s, 58 * s), b.label, btnOn)) veh.OnButton(b.id);
                ay -= 66 * s;
            }
            // Treffer-/Kippwarnung
            if (veh.stability < 0.2f && veh.rb)
            {
                GUI.color = new Color(1, 0.3f, 0.1f, 0.25f + 0.2f * Mathf.Sin(Time.unscaledTime * 8));
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), texWhite);
                GUI.color = Color.white;
            }
        }

        void DrawStick(GameInput.Stick st, string up, string down, string left, string right)
        {
            var c = new Vector2(st.center.x, Screen.height - st.center.y);
            float r = st.radius;
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), texCircle);
            var k = new Vector2(st.knob.x, -st.knob.y);
            float kr = r * 0.42f;
            GUI.DrawTexture(new Rect(c.x + k.x - kr, c.y + k.y - kr, 2 * kr, 2 * kr), texRing);
            var ls = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, wordWrap = false };
            GUI.Label(new Rect(c.x - 80 * s, c.y - r - 24 * s, 160 * s, 20 * s), up, ls);
            GUI.Label(new Rect(c.x - 80 * s, c.y + r + 4 * s, 160 * s, 20 * s), down, ls);
            var lsL = new GUIStyle(ls) { alignment = TextAnchor.MiddleRight };
            var lsR = new GUIStyle(ls) { alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(c.x - r - 170 * s, c.y - 10 * s, 162 * s, 20 * s), left, lsL);
            GUI.Label(new Rect(c.x + r + 8 * s, c.y - 10 * s, 162 * s, 20 * s), right, lsR);
        }

        // ---------------- Pause / Optionen ----------------
        void DrawPause()
        {
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), texWhite);
            GUI.color = Color.white;
            float w = Mathf.Min(620 * s, Screen.width - 30 * s), h = Mathf.Min(600 * s, Screen.height - 30 * s);
            var r = R((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            game.input.uiRects.Add(r);
            GUI.Box(r, "", panelDark);
            float x = r.x + 20 * s, y = r.y + 16 * s, iw = w - 40 * s;
            GUI.Label(R(x, y, iw, 40 * s), game.run != null ? "Pause" : "Optionen", big);
            y += 50 * s;
            if (game.run != null)
            {
                GUI.Label(R(x, y, iw, 24 * s), game.run.m.title, h2); y += 26 * s;
                GUI.Label(R(x, y, iw, 60 * s), game.run.m.brief, label); y += 50 * s;
                GUI.Label(R(x, y, iw, 60 * s), "Tipp: " + game.run.m.tip, small); y += 50 * s;
            }
            float bh = 48 * s;
            Settings.Iso = GUI.Toggle(R(x, y, iw, 30 * s), Settings.Iso, "  ISO-Steuerung (Joystick vor = Ausleger senken, wie im echten Bagger)", label); y += 38 * s;
            GUI.Label(R(x, y, 200 * s, 26 * s), $"Lautstärke {Mathf.RoundToInt(Settings.Volume * 100)} %", label);
            Settings.Volume = GUI.HorizontalSlider(R(x + 210 * s, y + 8 * s, iw - 210 * s, 20 * s), Settings.Volume, 0f, 1f); y += 36 * s;
            AudioListener.volume = Settings.Volume;
            GUI.Label(R(x, y, 200 * s, 26 * s), "Grafik: " + QualitySettings.names[QualitySettings.GetQualityLevel()], label);
            float qx = x + 210 * s, qw = (iw - 210 * s) / Mathf.Max(1, QualitySettings.names.Length) - 4 * s;
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (Button(R(qx + i * (qw + 4 * s), y - 4 * s, qw, 34 * s), QualitySettings.names[i], i == QualitySettings.GetQualityLevel() ? btnOn : btn)) { QualitySettings.SetQualityLevel(i, true); PlayerPrefs.SetInt("quality", i); }
            y += 44 * s;
            float bw = (iw - 12 * s) / 3;
            if (game.run != null)
            {
                if (Button(R(x, r.yMax - bh - 16 * s, bw, bh), "Weiter", btnOn)) game.Resume();
                if (Button(R(x + bw + 6 * s, r.yMax - bh - 16 * s, bw, bh), "Neu starten")) game.StartMission(game.run.m);
                if (Button(R(x + 2 * (bw + 6 * s), r.yMax - bh - 16 * s, bw, bh), "Hauptmenü")) game.ToMenu();
            }
            else if (Button(R(x, r.yMax - bh - 16 * s, iw, bh), "Zurück", btnOn)) game.state = Game.State.Menu;
            PlayerPrefs.SetInt("iso", Settings.Iso ? 1 : 0);
            PlayerPrefs.SetFloat("volume", Settings.Volume);
        }

        void DrawResult()
        {
            var run = game.run;
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), texWhite);
            GUI.color = Color.white;
            float w = Mathf.Min(520 * s, Screen.width - 30 * s), h = 330 * s;
            var r = R((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            game.input.uiRects.Add(r);
            GUI.Box(r, "", panelDark);
            float x = r.x + 22 * s, y = r.y + 18 * s;
            GUI.Label(R(x, y, w, 40 * s), "Auftrag erledigt!", big); y += 46 * s;
            GUI.Label(R(x, y, w, 26 * s), run.m.title, h2); y += 34 * s;
            GUI.Label(R(x, y, w, 70 * s), new string('★', run.stars) + new string('☆', 3 - run.stars), new GUIStyle(title) { normal = { textColor = Accent } }); y += 70 * s;
            GUI.Label(R(x, y, w - 40 * s, 26 * s), $"Zeit {Mathf.FloorToInt(run.time / 60)}:{Mathf.FloorToInt(run.time % 60):00}   (3 Sterne unter {Mathf.FloorToInt(run.m.threeStar / 60)} min)", label); y += 28 * s;
            if (run.penaltyNotes.Count > 0) GUI.Label(R(x, y, w - 40 * s, 26 * s), "Abzüge: " + string.Join(", ", run.penaltyNotes), small);
            float bw = (w - 50 * s) / 2, bh = 50 * s;
            int idx = Mission.All.IndexOf(run.m);
            bool hasNext = idx >= 0 && idx + 1 < Mission.All.Count && !Mission.All[idx + 1].sandbox;
            if (hasNext && Button(R(x, r.yMax - bh - 18 * s, bw, bh), "Nächste Mission", btnOn)) game.StartMission(Mission.All[idx + 1]);
            if (Button(R(hasNext ? x + bw + 6 * s : x, r.yMax - bh - 18 * s, hasNext ? bw : w - 44 * s, bh), "Weiterspielen")) game.ContinueAfterResult();
            if (Button(R(r.xMax - 140 * s, r.y + 16 * s, 120 * s, 40 * s), "Menü")) game.ToMenu();
        }

        void DrawToasts()
        {
            float now = Time.unscaledTime;
            float w = Mathf.Min(560 * s, Screen.width - 30 * s), x = (Screen.width - w) / 2, y = Safe.y + 160 * s;
            for (int i = toasts.Count - 1; i >= 0; i--) if (now - toasts[i].t > 5f) toasts.RemoveAt(i);
            foreach (var t in toasts)
            {
                float a = Mathf.Clamp01((5f - (now - t.t)) * 2f);
                float h = toastS.CalcHeight(new GUIContent(t.msg), w - 30 * s) + 18 * s;
                GUI.color = new Color(1, 1, 1, a);
                GUI.Box(R(x, y, w, h), "", panelDark);
                GUI.color = t.k == ToastKind.Ok ? Ok : t.k == ToastKind.Warn ? Warn : new Color(0.55f, 0.7f, 1f);
                GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, a);
                GUI.DrawTexture(R(x + 4 * s, y + 6 * s, 4 * s, h - 12 * s), texWhite);
                GUI.color = new Color(1, 1, 1, a);
                GUI.Label(R(x + 16 * s, y + 9 * s, w - 30 * s, h), t.msg, toastS);
                y += h + 6 * s;
            }
            GUI.color = Color.white;
        }
    }
}
