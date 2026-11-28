using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox;

/// <summary>
/// Eingabe: Tastatur/Maus und Gamepad am PC, zwei Touch-Joysticks am Handy, Kamera per Wischen / Zwei-Finger-Zoom.
/// Kennlinie wie bei Proportionalventilen: feinfühlig um die Mitte.
/// </summary>
public partial class GameInput : Node
{
    public ControlInput state;
    public Vector2 orbit;      // Pixel (positiv = nach rechts/unten gezogen)
    public float zoom = 1f;    // Faktor
    public bool touchMode;
    public bool actionPressed, action2Pressed;
    bool actionAcc, action2Acc;
    float wheelAcc;
    public Func<Vector2, bool> overUI;     // Treffer auf Bedienelemente
    public Action<Vector2> uiPress, uiRelease, uiMove;
    public float wheelUI;

    public class Stick
    {
        public Vector2 center;   // Bildschirm (oben links = 0,0)
        public float radius;
        public int finger = -1;
        public Vector2 value;    // -1..1, y positiv = nach oben
        public Vector2 knob;     // Pixel
    }
    public readonly Stick left = new(), right = new();
    readonly Dictionary<int, Vector2> camTouches = new();
    readonly HashSet<int> uiTouches = new();
    Vector2 kb, kbR, orbitAcc;
    float zoomAcc = 1f;
    bool mouseOrbit, mouseOnUI;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        touchMode = OS.HasFeature("mobile");
    }

    public void Layout()
    {
        var sz = GetViewport().GetVisibleRect().Size;
        float s = MathF.Min(sz.X, sz.Y);
        float r = Math.Clamp(s * 0.15f, 70f, 190f);
        float m = r * 0.45f;
        left.radius = right.radius = r;
        left.center = new Vector2(m + r, sz.Y - m - r);
        right.center = new Vector2(sz.X - m - r, sz.Y - m - r);
    }

    static float Curve(float v)
    {
        float a = MathF.Abs(v);
        if (a < 0.07f) return 0;
        return MathF.Sign(v) * MathF.Pow((a - 0.07f) / 0.93f, 1.5f);
    }

    bool OverUI(Vector2 p) => overUI != null && overUI(p);

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventScreenTouch t:
                touchMode = true;
                if (t.Pressed)
                {
                    if (OverUI(t.Position)) { uiTouches.Add(t.Index); uiPress?.Invoke(t.Position); break; }
                    if (left.finger < 0 && t.Position.DistanceTo(left.center) < left.radius * 1.5f) { left.finger = t.Index; StickMove(left, t.Position); }
                    else if (right.finger < 0 && t.Position.DistanceTo(right.center) < right.radius * 1.5f) { right.finger = t.Index; StickMove(right, t.Position); }
                    else camTouches[t.Index] = t.Position;
                }
                else
                {
                    if (uiTouches.Remove(t.Index)) uiRelease?.Invoke(t.Position);
                    if (t.Index == left.finger) { left.finger = -1; left.value = left.knob = Vector2.Zero; }
                    if (t.Index == right.finger) { right.finger = -1; right.value = right.knob = Vector2.Zero; }
                    camTouches.Remove(t.Index);
                }
                break;
            case InputEventScreenDrag d:
                if (uiTouches.Contains(d.Index)) { uiMove?.Invoke(d.Position); break; }
                if (d.Index == left.finger) StickMove(left, d.Position);
                else if (d.Index == right.finger) StickMove(right, d.Position);
                else if (camTouches.ContainsKey(d.Index))
                {
                    if (camTouches.Count == 1) orbitAcc += d.Relative;
                    else
                    {
                        Vector2 other = Vector2.Zero;
                        foreach (var kv in camTouches) if (kv.Key != d.Index) { other = kv.Value; break; }
                        float d0 = (d.Position - d.Relative).DistanceTo(other), d1 = d.Position.DistanceTo(other);
                        if (d0 > 10 && d1 > 10) zoomAcc *= d0 / d1;
                    }
                    camTouches[d.Index] = d.Position;
                }
                break;
            case InputEventMouseButton mb:
                if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
                {
                    if (!mb.Pressed) break;
                    float dir = mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                    if (OverUI(mb.Position)) wheelAcc += dir; else zoomAcc *= MathF.Exp(-dir * 0.12f * MathF.Max(1f, mb.Factor));
                    break;
                }
                if (mb.Pressed)
                {
                    if (mb.ButtonIndex == MouseButton.Left && OverUI(mb.Position)) { mouseOnUI = true; uiPress?.Invoke(mb.Position); break; }
                    if (mb.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle) mouseOrbit = true;
                }
                else
                {
                    if (mb.ButtonIndex == MouseButton.Left && mouseOnUI) { mouseOnUI = false; uiRelease?.Invoke(mb.Position); }
                    mouseOrbit = false;
                }
                break;
            case InputEventMouseMotion mm:
                if (mouseOnUI) uiMove?.Invoke(mm.Position);
                else if (mouseOrbit) orbitAcc += mm.Relative;
                break;
            case InputEventKey k when k.Pressed && !k.Echo:
                if (k.Keycode is Key.Space or Key.E) actionAcc = true;
                if (k.Keycode == Key.Q) action2Acc = true;
                break;
            case InputEventJoypadButton jb when jb.Pressed:
                if (jb.ButtonIndex == JoyButton.A) actionAcc = true;
                if (jb.ButtonIndex == JoyButton.X) action2Acc = true;
                break;
        }
    }

    /// <summary>Einmal je Frame aufrufen; liefert Werte und setzt die Ereignisse zurück.</summary>
    public void Poll(float dt)
    {
        Layout();
        orbit = orbitAcc; orbitAcc = Vector2.Zero;
        zoom = zoomAcc; zoomAcc = 1f;
        actionPressed = actionAcc; actionAcc = false;
        action2Pressed = action2Acc; action2Acc = false;
        wheelUI = wheelAcc; wheelAcc = 0;
        // ---------- Tastatur ----------
        float ramp = dt * 3.5f;
        var kt = new Vector2(Key2(Godot.Key.D, Godot.Key.A), Key2(Godot.Key.W, Godot.Key.S));
        var kr = new Vector2(Key2(Godot.Key.L, Godot.Key.J) + Key2(Godot.Key.Right, Godot.Key.Left), Key2(Godot.Key.I, Godot.Key.K) + Key2(Godot.Key.Up, Godot.Key.Down));
        kb = kb.MoveToward(kt.LimitLength(1.4f), ramp);
        kbR = kbR.MoveToward(kr.LimitLength(1.4f), ramp);
        float fine = Input.IsKeyPressed(Godot.Key.Shift) ? 0.35f : 1f;
        // ---------- Gamepad ----------
        float jlx = 0, jly = 0, jrx = 0, jry = 0;
        if (Input.GetConnectedJoypads().Count > 0)
        {
            jlx = Curve(Input.GetJoyAxis(0, JoyAxis.LeftX)); jly = -Curve(Input.GetJoyAxis(0, JoyAxis.LeftY));
            jrx = Curve(Input.GetJoyAxis(0, JoyAxis.RightX)); jry = -Curve(Input.GetJoyAxis(0, JoyAxis.RightY));
            float lt = Input.GetJoyAxis(0, JoyAxis.TriggerLeft), rt = Input.GetJoyAxis(0, JoyAxis.TriggerRight);
            if (lt > 0.1f || rt > 0.1f) orbit.X += (rt - lt) * 600f * dt;
            if (Input.IsJoyButtonPressed(0, JoyButton.LeftShoulder)) zoom *= 1f + dt;
            if (Input.IsJoyButtonPressed(0, JoyButton.RightShoulder)) zoom *= 1f - dt * 0.8f;
        }
        state.lx = Math.Clamp(Curve(left.value.X) + kb.X * fine + jlx, -1, 1);
        state.ly = Math.Clamp(Curve(left.value.Y) + kb.Y * fine + jly, -1, 1);
        state.rx = Math.Clamp(Curve(right.value.X) + kbR.X * fine + jrx, -1, 1);
        state.ry = Math.Clamp(Curve(right.value.Y) + kbR.Y * fine + jry, -1, 1);
    }

    static float Key2(Key a, Key b) => (Input.IsKeyPressed(a) ? 1f : 0f) - (Input.IsKeyPressed(b) ? 1f : 0f);

    void StickMove(Stick s, Vector2 pos)
    {
        var d = (pos - s.center) / s.radius;
        if (d.Length() > 1) d = d.Normalized();
        s.value = new Vector2(d.X, -d.Y);
        s.knob = d * s.radius * 0.62f;
    }
}

/// <summary>Kamera: Verfolgen (hinter dem Fahrer), Kabine (Fahrersicht), Frei (Orbit um die Maschine).</summary>
public partial class CameraRig : Node
{
    public enum Mode { Follow, Cabin, Free }
    public Mode mode = Mode.Follow;
    public Camera3D cam;
    public VehicleBase target;
    public float yawOff, pitch = 22f, dist = 13f, yawFree;
    Vector3 smoothTarget;
    float yawCur;   // Blickrichtung (Radiant, atan2(x, z))
    bool init;
    float lookYaw, lookPitch;
    public bool menuOrbit;
    float menuT;
    const float D2R = MathF.PI / 180f;

    public void SetTarget(VehicleBase v)
    {
        target = v;
        dist = v != null ? v.camDistance : 14f;
        pitch = v is TowerCrane ? 32f : v is Excavator ? 27f : 22f;
        yawOff = v is Excavator ? 0.5f : v is WreckingCrane ? 0.6f : 0f;
        lookYaw = lookPitch = 0;
        init = false;
    }

    public void Cycle()
    {
        mode = mode == Mode.Follow ? Mode.Cabin : mode == Mode.Cabin ? Mode.Free : Mode.Follow;
        lookYaw = lookPitch = 0;
        if (mode == Mode.Free) yawFree = yawCur;
    }

    public string ModeName => mode == Mode.Follow ? "Verfolgen" : mode == Mode.Cabin ? "Kabine" : "Frei";

    public void Tick(float dt, GameInput inp)
    {
        if (cam == null) return;
        if (menuOrbit || target == null || !IsInstanceValid(target))
        {
            menuT += dt;
            float a = menuT * 0.04f + 0.6f;
            cam.GlobalPosition = new Vector3(MathF.Sin(a) * 78, 30, MathF.Cos(a) * 78);
            cam.LookAt(new Vector3(0, 2, -4), Vector3.Up);
            cam.Fov = 50;
            cam.Near = 0.1f;
            return;
        }
        var o = inp != null ? inp.orbit : Vector2.Zero;
        float z = inp != null ? inp.zoom : 1f;
        float sens = 0.22f * D2R * (inp != null && inp.touchMode ? 0.6f : 1f);
        if (mode == Mode.Cabin)
        {
            lookYaw -= o.X * sens;
            lookPitch = Math.Clamp(lookPitch - o.Y * sens, -70 * D2R, 50 * D2R);
            target.Cabin(out var p, out var f);
            var b = Basis.LookingAt(f, Vector3.Up) * new Basis(Vector3.Up, lookYaw) * new Basis(Vector3.Right, lookPitch);
            cam.GlobalTransform = new Transform3D(b, p);
            cam.Near = 0.05f;
            cam.Fov = Mathf.Lerp(cam.Fov, target.cabinFov, MathF.Min(1, dt * 5));
            return;
        }
        cam.Near = 0.1f;
        cam.Fov = Mathf.Lerp(cam.Fov, 55f, MathF.Min(1, dt * 5));
        var tp = target.CameraTarget();
        if (!init) { smoothTarget = tp; yawCur = target.CameraYaw(); init = true; }
        smoothTarget = new Vector3(U.Damp(smoothTarget.X, tp.X, 6, dt), U.Damp(smoothTarget.Y, tp.Y, 4, dt), U.Damp(smoothTarget.Z, tp.Z, 6, dt));
        dist = Math.Clamp(dist * z, 4f, target is TowerCrane ? 110f : 70f);
        pitch = Math.Clamp(pitch + o.Y * 0.22f * (inp != null && inp.touchMode ? 0.6f : 1f), 2f, 85f);
        if (mode == Mode.Free) { yawFree -= o.X * sens; yawCur = yawFree; }
        else
        {
            yawOff -= o.X * sens;
            yawCur = U.DampAngle(yawCur, target.CameraYaw() + yawOff, 2.5f, dt);
        }
        float pr = pitch * D2R;
        var dir = new Vector3(MathF.Sin(yawCur) * MathF.Cos(pr), -MathF.Sin(pr), MathF.Cos(yawCur) * MathF.Cos(pr));
        var pos = smoothTarget - dir * dist;
        var soil = SoilTerrain.I;
        if (soil != null && soil.Inside(pos.X, pos.Z))
        {
            float g = soil.Height(pos.X, pos.Z) + 0.8f;
            if (pos.Y < g) pos.Y = g;
        }
        else if (pos.Y < 0.8f) pos.Y = 0.8f;
        cam.GlobalPosition = pos;
        if ((smoothTarget - pos).LengthSquared() > 1e-4f) cam.LookAt(smoothTarget, Vector3.Up);
    }
}
