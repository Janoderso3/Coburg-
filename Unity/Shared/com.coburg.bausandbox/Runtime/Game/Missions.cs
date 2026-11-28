using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Laufender Missionszustand.</summary>
    public class MissionRun
    {
        public Mission m;
        public float time;
        public float progress;
        public string status = "";
        public bool done, failed;
        public int stars;
        public int penalties;
        public readonly List<string> penaltyNotes = new List<string>();
        public float startDisposedSoil, startDisposedRubble;
        public ConstructionSite site;
        public GameObject marker;
        public int protectedAlive;
        public void Penalty(string why) { penalties++; penaltyNotes.Add(why); Hud.Toast("Abzug: " + why, ToastKind.Warn); }
    }

    public class Mission
    {
        public string id, title, brief, tip;
        public string[] vehicles;
        public bool house, garage;
        public float threeStar, twoStar; // Sekunden
        public System.Action<World, MissionRun> setup;
        public System.Action<World, MissionRun> check;
        public bool sandbox;

        public int BestStars => PlayerPrefs.GetInt("stars_" + id, 0);
        public float BestTime => PlayerPrefs.GetFloat("time_" + id, 0);

        public static readonly List<Mission> All = new List<Mission>
        {
            new Mission
            {
                id = "m1", title = "1  Erster Aushub", vehicles = new[] { "Bagger" },
                brief = "Hebe in der gelb markierten Fläche 8 m³ Boden aus und setze ihn daneben ab.",
                tip = "Löffel offen ansetzen, Stiel heranziehen und dabei einkippen. Vor dem Schwenken den Löffel aufrecht halten.",
                threeStar = 180, twoStar = 360,
                setup = (w, r) => { r.marker = w.Marker(Layout.DigZone, "Aushubfläche"); },
                check = (w, r) =>
                {
                    var z = Layout.DigZone;
                    float v = w.soil.ExcavatedRect(new Vector3(z.center.x, 0, z.center.y), z.width / 2, z.height / 2, 0, 3f);
                    r.progress = v / 8f; r.status = $"Aushub {U.Fmt(v)} / 8 m³";
                    r.done = v >= 8f;
                },
            },
            new Mission
            {
                id = "m2", title = "2  Lkw beladen", vehicles = new[] { "Bagger", "Kipper" },
                brief = "Belade den Kipper mit 9 m³ Erde vom Erdhaufen. Nichts daneben schütten!",
                tip = "Kipper rückwärts neben den Bagger stellen, dann zum Bagger wechseln. Löffel über der Mulde ausleeren.",
                threeStar = 240, twoStar = 420,
                setup = (w, r) => { },
                check = (w, r) =>
                {
                    float c = w.truck.cargo;
                    r.progress = c / 9f; r.status = $"Ladung {U.Fmt(c)} / 9 m³";
                    r.done = c >= 9f;
                },
            },
            new Mission
            {
                id = "m3", title = "3  Ab zur Deponie", vehicles = new[] { "Radlader", "Kipper", "Bagger" },
                brief = "Fahre 20 m³ vom Erdhaufen zur Deponie (Mulde hinten rechts) und kippe dort ab.",
                tip = "Mit dem Radlader in den Haufen fahren, ankippen, anheben und über der Kippermulde auskippen. In der Deponie Mulde heben.",
                threeStar = 600, twoStar = 900,
                setup = (w, r) => { r.startDisposedSoil = w.Disposed(SoilMat.Soil); },
                check = (w, r) =>
                {
                    float d = w.Disposed(SoilMat.Soil) - r.startDisposedSoil;
                    r.progress = d / 20f; r.status = $"Abgefahren {U.Fmt(d)} / 20 m³";
                    r.done = d >= 20f;
                },
            },
            new Mission
            {
                id = "m4", title = "4  Planum herstellen", vehicles = new[] { "Raupe" },
                brief = "Ebne den Bauplatz für die Fertiggarage ein: Höhenabweichung höchstens ±15 cm.",
                tip = "Schild nur wenig absenken, langsam schieben, überschüssige Erde seitlich wegdrücken. Rückwärts mit Schild glätten.",
                threeStar = 180, twoStar = 360,
                setup = (w, r) => { r.site = new ConstructionSite(w.buildings, "garage", new Vector3(Layout.Plot.x, 0, Layout.Plot.y), 0); },
                check = (w, r) =>
                {
                    r.progress = Mathf.Clamp01(1f - (r.site.dev - 0.15f) / 1.2f);
                    r.status = $"Abweichung ±{U.Fmt(r.site.dev, 2)} m";
                    r.done = r.site.stage != "planum";
                },
            },
            new Mission
            {
                id = "m5", title = "5  Bodenplatte betonieren", vehicles = new[] { "Mischer" },
                brief = "Betoniere die Bodenplatte der Garage. Der Bauplatz ist schon eingeebnet und eingeschalt.",
                tip = "Rückwärts mit dem Heck an die Schalung fahren, Schurre über die Platte schwenken und Beton ablassen.",
                threeStar = 150, twoStar = 300,
                setup = (w, r) =>
                {
                    var p = new Vector3(Layout.Plot.x, 0, Layout.Plot.y);
                    var st = w.soil.StatsRect(p, 2.2f, 3.7f, 0);
                    w.soil.LevelRect(p, 2.3f, 3.8f, 0, st.mean, 3);
                    r.site = new ConstructionSite(w.buildings, "garage", p, 0);
                },
                check = (w, r) =>
                {
                    r.progress = r.site.stage == "beton" ? r.site.poured / r.site.need : 1f;
                    r.status = $"Beton {U.Fmt(r.site.poured)} / {U.Fmt(r.site.need)} m³";
                    r.done = r.site.stage != "planum" && r.site.stage != "beton";
                },
            },
            new Mission
            {
                id = "m6", title = "6  Fertiggarage montieren", vehicles = new[] { "Kran" },
                brief = "Hebe die Fertigteile vom Lagerplatz mit dem Turmdrehkran in die grünen Positionen.",
                tip = "Sanft anfahren und abbremsen, sonst pendelt die Last. Teil knapp über der Bodenplatte zur Ruhe kommen lassen, dann absenken.",
                threeStar = 480, twoStar = 840,
                setup = (w, r) =>
                {
                    var p = new Vector3(Layout.Plot.x, 0, Layout.Plot.y);
                    var st = w.soil.StatsRect(p, 2.2f, 3.7f, 0);
                    w.soil.LevelRect(p, 2.3f, 3.8f, 0, st.mean, 3);
                    r.site = new ConstructionSite(w.buildings, "garage", p, 0);
                    r.site.poured = r.site.need;
                },
                check = (w, r) =>
                {
                    var s = r.site;
                    r.progress = s.stage == "montage" ? s.placed.Count / (float)s.defs.Count : s.stage == "fertig" ? 1 : 0;
                    r.status = s.stage == "montage" ? $"Montiert {s.placed.Count} / {s.defs.Count}" : s.stage == "haerten" ? $"Beton härtet ({Mathf.CeilToInt(s.cure)} s)" : "fertig";
                    if (s.lostElements > r.penalties) r.Penalty("Fertigteil zerbrochen");
                    r.done = s.stage == "fertig";
                },
            },
            new Mission
            {
                id = "m7", title = "7  Abriss Ziegelgarage", vehicles = new[] { "Bagger", "Radlader", "Kipper" },
                brief = "Reiße die alte Garage ab und fahre mindestens 12 m³ Bauschutt zur Deponie.",
                tip = "Von oben nach unten abtragen: erst die Decke, dann die Wände. Schutt mit Bagger oder Radlader auf den Kipper laden.",
                garage = true, threeStar = 600, twoStar = 1000,
                setup = (w, r) => { r.startDisposedRubble = w.Disposed(SoilMat.Rubble); },
                check = (w, r) =>
                {
                    var b = w.oldGarage;
                    float down = 1f - b.alive / (float)b.total;
                    float d = w.Disposed(SoilMat.Rubble) - r.startDisposedRubble;
                    r.progress = Mathf.Min(1f, down) * 0.5f + Mathf.Clamp01(d / 12f) * 0.5f;
                    r.status = $"Abgerissen {Mathf.RoundToInt(down * 100)} % · Schutt {U.Fmt(d)} / 12 m³";
                    r.done = b.demolished && d >= 12f;
                },
            },
            new Mission
            {
                id = "m8", title = "8  Abrissbirne", vehicles = new[] { "Birne" },
                brief = "Bringe das alte Fachwerkhaus mit der Abrissbirne zum Einsturz.",
                tip = "Oberwagen hin und her drehen, um die Birne aufzuschaukeln. Erst das Obergeschoss, dann tragende Wände treffen.",
                house = true, threeStar = 300, twoStar = 600,
                setup = (w, r) => { },
                check = (w, r) =>
                {
                    var b = w.oldHouse;
                    float down = 1f - b.alive / (float)b.total;
                    r.progress = Mathf.Clamp01(down / 0.96f);
                    r.status = $"Abgerissen {Mathf.RoundToInt(down * 100)} %";
                    if (w.wrecker && w.wrecker.tipped && r.penalties == 0) r.Penalty("Seilbagger umgekippt");
                    r.done = b.demolished;
                },
            },
            new Mission
            {
                id = "m9", title = "9  Baugrube", vehicles = new[] { "Bagger", "Kipper", "Radlader" },
                brief = "Hebe eine Baugrube aus: Fläche 8 × 8 m, 1,2 m tief (mindestens 70 m³). Abraum zur Deponie.",
                tip = "Steile Wände brechen nach einiger Zeit ab! Schräg abböschen und nicht zu nah an die Kante fahren.",
                threeStar = 1200, twoStar = 1800,
                setup = (w, r) => { r.marker = w.Marker(Layout.PitZone, "Baugrube"); },
                check = (w, r) =>
                {
                    var z = Layout.PitZone;
                    float v = w.soil.ExcavatedRect(new Vector3(z.center.x, 0, z.center.y), z.width / 2, z.height / 2, 0, 1.3f);
                    r.progress = v / 70f; r.status = $"Aushub {U.Fmt(v)} / 70 m³";
                    r.done = v >= 70f;
                },
            },
            new Mission
            {
                id = "m10", title = "10  Einfamilienhaus", vehicles = new[] { "Raupe", "Bagger", "Mischer", "Kran", "Kipper" },
                brief = "Baue ein komplettes Fertighaus: Planum, zwei Ladungen Beton für die Bodenplatte, 10 Fertigteile montieren.",
                tip = "Der Fahrmischer fasst 9 m³ – für 14 m³ musst du einmal zum Betonwerk (oben links) nachladen.",
                threeStar = 1800, twoStar = 2700,
                setup = (w, r) => { r.site = new ConstructionSite(w.buildings, "house", new Vector3(Layout.Plot.x, 0, Layout.Plot.y), 0); },
                check = (w, r) =>
                {
                    var s = r.site;
                    var st = s.Status();
                    float stageP = s.stage == "planum" ? 0 : s.stage == "beton" ? 0.2f : s.stage == "haerten" ? 0.4f : s.stage == "montage" ? 0.45f + 0.55f * s.placed.Count / s.defs.Count : 1f;
                    r.progress = stageP; r.status = st.title + " – " + st.detail;
                    if (s.lostElements > r.penalties) r.Penalty("Fertigteil zerbrochen");
                    r.done = s.stage == "fertig";
                },
            },
            new Mission
            {
                id = "free", title = "Freies Spiel", vehicles = new[] { "Bagger", "Radlader", "Raupe", "Kipper", "Mischer", "Kran", "Birne" },
                brief = "Alle Maschinen, ein Bauplatz für ein Einfamilienhaus, Altbau und Garage zum Abreißen. Keine Zeitwertung.",
                tip = "Über das Menü kannst du weitere Baustellen und Abrissobjekte anlegen.",
                house = true, garage = true, sandbox = true,
                setup = (w, r) => { r.site = new ConstructionSite(w.buildings, "house", new Vector3(Layout.Plot.x, 0, Layout.Plot.y), 0); },
                check = (w, r) =>
                {
                    var s = r.site;
                    if (s != null && s.stage != "fertig") { var st = s.Status(); r.status = st.title + " – " + st.detail; r.progress = st.prog; }
                    else r.status = $"Gebaut: {w.buildings.builtCount} · Abgerissen: {w.buildings.demolishedCount} · Deponie: {U.Fmt(w.soil.TotalDisposed(SoilMat.Soil, SoilMat.Rubble, SoilMat.Gravel, SoilMat.Concrete))} m³";
                },
            },
        };

        public bool Unlocked
        {
            get
            {
                int i = All.IndexOf(this);
                if (i <= 0 || sandbox) return true;
                return All[i - 1].BestStars > 0;
            }
        }

        public int Stars(MissionRun r)
        {
            int s = r.time <= threeStar ? 3 : r.time <= twoStar ? 2 : 1;
            return Mathf.Max(1, s - r.penalties);
        }

        public void SaveResult(MissionRun r)
        {
            if (r.stars > BestStars) PlayerPrefs.SetInt("stars_" + id, r.stars);
            if (BestTime <= 0 || r.time < BestTime) PlayerPrefs.SetFloat("time_" + id, r.time);
            PlayerPrefs.Save();
        }
    }
}
