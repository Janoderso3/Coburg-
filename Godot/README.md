# Bau-Sandbox – PC-Spiel (Godot 4.7, C#)

Das ist die richtige PC-Version der Bau-Sandbox: ein eigenständiges Windows-Spiel (`Bau-Sandbox.exe`), kein Browserspiel.
Gebaut mit der Godot-Engine 4.7 (.NET/C#) und der Physik-Engine Jolt bei 100 Physikschritten pro Sekunde.

## Spielen (Windows)

1. `Bau-Sandbox-Windows.zip` aus dem Ordner `Releases/` herunterladen und entpacken.
2. `Bau-Sandbox.exe` starten. Den Ordner `data_BauSandbox_windows_x86_64` daneben liegen lassen, er enthält die .NET-Laufzeit.
3. Windows SmartScreen meldet eventuell „Unbekannter Herausgeber“ → „Weitere Informationen“ → „Trotzdem ausführen“.

Voraussetzung: Windows 10/11 (64 Bit) und eine Grafikkarte mit Vulkan oder Direct3D 12. Eine extra .NET-Installation ist nicht nötig.

## Steuerung

| Taste | Funktion |
| --- | --- |
| W A S D | linker Hebel (Fahren bzw. Stiel/Drehen, je nach Maschine) |
| I J K L oder Pfeiltasten | rechter Hebel (Ausleger, Löffel, Schild, Mulde, Hubwerk …) |
| Leertaste / E | Aktion 1 (z. B. Fahr-/Arbeitsmodus, Last einhängen, Beton ablassen) |
| Q | Aktion 2 |
| Umschalt gedrückt halten | feinfühlig (35 %) |
| Maus ziehen / Mausrad | Kamera drehen / zoomen |
| C | Kamera: Verfolgen → Kabine → Frei |
| 1–7, Tab | Maschine wechseln |
| R | umgekippte Maschine bergen |
| Esc | Pause und Optionen |
| F1 | Hilfe ein/aus |
| F11 | Vollbild |
| F12 | Bildschirmfoto |

Gamepad (Xbox-Layout): Sticks = Hebel, A = Aktion 1, X = Aktion 2, Schultertasten = Zoom, Trigger = Kamera drehen.
Auf Touch-Geräten erscheinen zwei Joysticks. Ein Finger dreht die Kamera, zwei Finger zoomen.

Unter Optionen stehen ISO-Steuerung (Hebel vor = Ausleger senken, wie im echten Bagger), Lautstärke, Grafikstufe (Niedrig/Mittel/Hoch/Ultra) und Vollbild zur Wahl.

## Missionen

Zehn Missionen, jeweils mit 1–3 Sternen nach Zeit (Abzug für umgekippte Maschinen oder zerbrochene Fertigteile).
Die nächste Mission wird mit dem ersten Stern freigeschaltet. Der Spielstand liegt in `%APPDATA%\Godot\app_userdata\Bau-Sandbox\spielstand.cfg`.

1. Erster Aushub – 8 m³ in der markierten Fläche ausheben
2. Lkw beladen – 9 m³ vom Erdhaufen auf den Kipper
3. Ab zur Deponie – 20 m³ abfahren und abkippen
4. Planum herstellen – Bauplatz auf ±15 cm einebnen
5. Bodenplatte betonieren – mit dem Fahrmischer
6. Fertiggarage montieren – Fertigteile mit dem Turmdrehkran setzen
7. Abriss Ziegelgarage – abreißen und 12 m³ Schutt zur Deponie
8. Abrissbirne – Fachwerkhaus mit der Birne einreißen
9. Baugrube – 8 × 8 m, 1,2 m tief, Wände brechen nach
10. Einfamilienhaus – vom Planum bis zum fertigen Haus

Dazu kommt das **Freie Spiel** mit allen sieben Maschinen.

## Physik

- **Boden wie Sand und Lehm:** Höhenfeld im 25-cm-Raster mit Volumenerhaltung. Gelöster Boden lockert sich um den Faktor 1,25 auf. Loses Material rieselt bis zum Schüttwinkel (34°) auseinander. Gewachsener Boden steht steil, baut aber Spannung auf und bricht nach einiger Zeit in Brocken ab. Die Brocken sind echte Starrkörper, die zerfallen. Schwere Maschinen verdichten den Boden (Spurrillen) und belasten Grubenkanten.
- **Maschinen:** Kettenfahrwerk mit Federung je Laufrolle, Reibung, Schlupf und Gegenlenkung. Radfahrzeuge mit Federung, Getriebe und Reifenhaftung. Hydraulik mit gemeinsamer Pumpe, Rampen und Druckbegrenzung. Der Schwerpunkt wandert mit Ausleger und Last, deshalb kann man kippen. Grabkräfte wirken auf die Maschine zurück.
- **Krane:** Haken und Last pendeln am Seil (Seil als Impuls-Bedingung). Lastmomentbegrenzung: 10 t bis 14 m, danach 140 t·m. Die Abrissbirne (3 t) wird durch Drehen des Oberwagens aufgeschaukelt, ihr Seilzug wirkt auf die Maschine.
- **Gebäude:** aus Einzelteilen mit Lastpfad und Spannweitengrenzen. Fehlt das Auflager, stürzt der Rest ein. Trümmer sind Starrkörper und werden zu Bauschutt.

## Klang

Der Motorklang wird live synthetisiert: Zündimpulse je Zylinder, Auspuffresonanzen, Turbopfeifen, Hydraulik, Druckbegrenzungsventil, Kettenrasseln, Rückfahrwarner und Druckluftbremse. Dazu kommen Effekte für Erde, Einsturz, Glas und Metall.

## Selbst bauen

Benötigt: Godot 4.7.2 .NET und .NET SDK 8.

```bash
cd Godot
dotnet build
godot --path .                                  # starten
godot --headless --export-release "Windows Desktop" ../build/windows/Bau-Sandbox.exe
```

Automatische Tests (fahren die Maschinen per Skript und speichern Bildschirmfotos):

```bash
godot --path . -- --test=dig --out=/tmp/test     # dig, truck, loader, doze, crane, wreck, mixer, free, menu
```

### iPhone

Godot kann das Projekt für iOS exportieren. Dafür sind ein Mac mit Xcode, die iOS-Exportvorlagen von Godot und ein Apple-Entwicklerkonto nötig.
In Godot unter *Projekt → Exportieren → Hinzufügen → iOS* die Team-ID und eine Bundle-ID (z. B. `de.coburg.bausandbox`) eintragen und exportieren. Danach das Xcode-Projekt öffnen und auf das iPhone übertragen.
Auf dem Handy erscheinen automatisch die Touch-Joysticks.

## Aufbau

```
Scripts/Core       Hilfsfunktionen, prozedurale Texturen und Materialien, Mesh-Baukasten
Scripts/Soil       Bodenmodell, Gelände-Chunks mit Splat-Shader, Erdpartikel und Brocken
Scripts/Vehicles   Bagger, Radlader, Raupe, Kipper, Fahrmischer, Turmdrehkran, Seilbagger
Scripts/Buildings  Bauteile, Statik, Trümmer, Baustellen, Fertigteile
Scripts/Audio      Motorsynthese, Effektklänge
Scripts/Game       Welt, Missionen, Eingabe, Kamera, Oberfläche, Spielablauf, Testfahrer
```
