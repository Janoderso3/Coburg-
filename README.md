# Bau-Sandbox

Realistischer Baustellen-Simulator für Handy und Browser (Three.js, ohne Build-Schritt).

Graben, planieren, betonieren, Fertighäuser mit dem Turmdrehkran aufstellen und alte Häuser abreißen.

## Fahrzeuge

| Fahrzeug | Was es kann |
| --- | --- |
| Kettenbagger (20 t) | Graben mit echter ISO-Joystickbelegung, Löffel kann Material verlieren, Abbruch von Wänden |
| Radlader | Knicklenkung, Schaufel mit Parallelführung, Haufen aufnehmen und Lkw beladen |
| Planierraupe | Schild schält Boden ab und schiebt ihn vor sich her, zum Planieren des Bauplatzes |
| Kipper-Lkw | Wird beladen, Nutzlast in Tonnen, Mulde kippt ab ca. 22° ab |
| Fahrmischer | 9 m³ Beton, Schurre ausrichten, Nachladen am Betonwerk |
| Turmdrehkran | Pendelnde Last, Traglastkurve (10 t bis 14 m, danach 140 t·m), Fertigteile montieren |
| Seilbagger mit Abrissbirne | 3-t-Birne am Seil, Schwung durch Drehen des Oberwagens |

## Was realistisch simuliert wird

- **Boden:** Höhenfeld mit 25-cm-Raster. Das Volumen bleibt erhalten (was der Löffel aufnimmt, fehlt im Boden). Lose Erde rutscht bis zum Schüttwinkel von 34° ab. Gewachsener Boden steht steil. Beim Graben kommen nacheinander Mutterboden, Lehm und Sand/Kies zum Vorschein.
- **Statik:** Gebäude bestehen aus einzelnen Bauteilen. Jedes Teil braucht einen Lastpfad zum Fundament, die Spannweite ohne Auflager ist begrenzt. Reißt man tragende Wände heraus, stürzen Decken und Dach ein. Herabfallende Teile beschädigen darunterliegende Bauteile. Trümmer werden zu Bauschutt, den man abfahren kann.
- **Neubau:** Bauplatz auf ±15 cm planieren → Schalung → Bodenplatte betonieren → aushärten (Zeitraffer) → Fertigteile werden geliefert → mit dem Kran montieren.
- **Hydraulik:** Gelenke haben Rampen und Endanschläge. Die Pumpenleistung wird auf mehrere Funktionen aufgeteilt. Im Boden und an Beton wird die Maschine langsamer.
- **Sound:** synthetischer Dieselmotor (Drehzahl und Last), Hydraulik, Rückfahrwarner, Kettenrasseln.

## Steuerung

- **Handy:** zwei Joysticks unten links und rechts (die Beschriftung zeigt die Funktion je Fahrzeug). Ein Finger auf dem Bild dreht die Kamera, zwei Finger zoomen. Querformat empfohlen.
- **Tastatur:** WASD = linker Stick, Pfeiltasten oder IJKL = rechter Stick, Leertaste = Aktion, 1–7 = Fahrzeug, C = Kamera.
- Im Menü lässt sich die **ISO-Steuerung** einschalten (Joystick nach vorn = Ausleger senken, wie im echten Bagger).

## Starten

Das Spiel nutzt ES-Module und muss deshalb über einen Webserver laufen:

```bash
python3 -m http.server 8000
# dann http://localhost:8000 öffnen (oder im Handy-Browser die IP des Rechners)
```

Alternativ GitHub Pages für das Repository aktivieren; dann läuft es direkt unter der Pages-URL.

## Aufbau

```
index.html        Oberfläche (Joysticks, HUD, Menü)
js/main.js        Welt, Kamera, Spielschleife, Speichern/Laden
js/terrain.js     verformbares Gelände, Bodenschichten, Böschungsrutschung
js/vehicles.js    Bagger, Radlader, Raupe, Kipper, Fahrmischer
js/crane.js       Turmdrehkran, Seilbagger, Pendelphysik
js/buildings.js   Bauteile, Statik, Trümmer, Fertigteile, Baustellen
js/effects.js     Erdpartikel (mit Volumen), Staub, Abgas
js/audio.js       Motor- und Effektsound (Web Audio)
js/models.js      prozedurale 3D-Modelle
```
