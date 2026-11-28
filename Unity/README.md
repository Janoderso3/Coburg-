# Bau-Sandbox – Unity-Version (PC + iPhone)

Richtiges Spiel in Unity 6 mit Fahrzeugphysik (PhysX), sandartigem Boden, Statik und Missionen.

| Ordner | Inhalt |
| --- | --- |
| `Shared/com.coburg.bausandbox` | Gemeinsamer Spielcode als Unity-Paket (Boden, Fahrzeuge, Gebäude, Missionen, Sound, Oberfläche) |
| `BauSandbox-PC` | PC-Projekt mit **HDRP** (maximale Grafik: physikalischer Himmel, Volumenwolken, Nebel, SSAO, Spiegelungen, TAA) |
| `BauSandbox-iPhone` | iPhone-Projekt mit **URP** (für Mobilgeräte optimiert, Touch-Joysticks) |

## Bauen

Voraussetzung: Unity 6 LTS (6000.0.x) mit „Windows Build Support (IL2CPP)“ bzw. „iOS Build Support“.

1. Projekt in Unity Hub öffnen (`Unity/BauSandbox-PC` oder `Unity/BauSandbox-iPhone`).
2. Menü **Bau-Sandbox → 1 Alles einrichten** (erzeugt Materialien, Texturen, Terrain-Schichten, Licht, Himmel und die Szene).
3. Menü **Bau-Sandbox → 2 Windows-Spiel bauen** bzw. **iPhone-Xcode-Projekt erzeugen**.
   - Windows: fertige `Builds/Windows/Bau-Sandbox.exe`.
   - iPhone: `Builds/iOS` ist ein Xcode-Projekt; auf einem Mac in Xcode öffnen, signieren, aufs iPhone spielen.

Ohne Oberfläche (Kommandozeile):

```
Unity.exe -batchmode -projectPath Unity/BauSandbox-PC -executeMethod PcSetup.BuildWindows -logFile build.log
```

Falls die HDRP/URP-Paketversion nicht zu deiner Unity-Version passt, in `Packages/manifest.json` die Version an die im Package Manager angebotene anpassen.

## Physik und Realismus

- **Boden wie Sand**: lose Erde rieselt bis zum Schüttwinkel (34°) ab, gelöster Boden lockert um 25 % auf. Fester Boden steht steil, aber Grubenwände brechen nach einiger Zeit als Erdbrocken ab (Physikkörper), die zerfallen und nachrutschen – schneller, wenn schwere Maschinen an der Kante stehen. Schwere Maschinen verdichten lose Erde (Spurrillen).
- **Ketten**: Federung je Laufrolle, Reibung je Kontakt, Lenken durch Kettengeschwindigkeit; lose Erde senkt die Haftung.
- **Räder**: WheelCollider, Dieselkennlinie, Automatikgetriebe mit Schaltpausen, Feststellbremse; Radlader mit echter Knicklenkung (zwei Körper + Gelenk).
- **Bagger**: Pumpenstrom wird auf die Funktionen verteilt, Reißkraft zieht die Maschine, mit dem Ausleger kann sie sich anheben; Schwerpunkt wandert mit Arm und Ladung → kann kippen.
- **Kran**: Seil als Gelenk, Last an Anschlagketten (Doppelpendel), Traglastkurve 10 t bis 14 m, danach 140 t·m.
- **Gebäude**: Bauteile mit Lastpfad und Spannweite; Einsturz als Physik-Trümmer, die zu Bauschutt werden.
- **Sound**: Diesel in Echtzeit synthetisiert (Zündimpulse, Turbolader, Last), Hydraulikpumpe, Druckbegrenzungsventil, Kettenrasseln, Rückfahrwarner, Druckluftbremse.

## Steuerung (PC)

WASD = linker Joystick, IJKL/Pfeiltasten = rechter Joystick, Shift = feinfühlig, Leertaste = Aktion, 1–7 / Tab = Fahrzeug, C = Kamera, Maus ziehen = Kamera drehen, Mausrad = Zoom, R = umgekipptes Fahrzeug bergen, Esc = Pause, F1 = Tastenhilfe.

## Missionen

1 Erster Aushub · 2 Lkw beladen · 3 Ab zur Deponie · 4 Planum herstellen · 5 Bodenplatte betonieren · 6 Fertiggarage montieren · 7 Abriss Ziegelgarage · 8 Abrissbirne · 9 Baugrube · 10 Einfamilienhaus · Freies Spiel. Jede Mission gibt 1–3 Sterne (Zeit, Abzüge für zerbrochene Fertigteile oder umgekippte Maschinen) und schaltet die nächste frei.
