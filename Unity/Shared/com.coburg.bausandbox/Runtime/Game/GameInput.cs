using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>
    /// Eingabe: Tastatur/Maus am PC, zwei Touch-Joysticks am Handy, Kamera per Wischen/Zwei-Finger-Zoom.
    /// Kennlinie wie bei Proportionalventilen: feinfühlig um die Mitte.
    /// </summary>
    public class GameInput : MonoBehaviour
    {
        public ControlInput state;
        public Vector2 orbit;     // Pixel (positiv = nach rechts/oben gezogen)
        public float zoom = 1f;   // Faktor
        public bool touchMode;
        public readonly List<Rect> uiRects = new List<Rect>(); // GUI-Koordinaten (oben links)
        public bool actionPressed;

        public class Stick
        {
            public Vector2 center;  // Bildschirm (unten links)
            public float radius;
            public int finger = -1;
            public Vector2 value;   // -1..1
            public Vector2 knob;
        }
        public readonly Stick left = new Stick(), right = new Stick();
        readonly Dictionary<int, Vector2> camTouches = new Dictionary<int, Vector2>();
        Vector2 kb, kbR;
        Vector3 lastMouse;
        bool mouseOrbit;

        void Awake()
        {
            touchMode = Application.isMobilePlatform || Input.touchSupported && Input.touchCount > 0;
        }

        public void Layout()
        {
            float s = Mathf.Min(Screen.width, Screen.height);
            float r = Mathf.Clamp(s * 0.15f, 70f, 190f);
            float m = r * 0.45f;
            var safe = Screen.safeArea;
            left.radius = right.radius = r;
            left.center = new Vector2(safe.xMin + m + r, safe.yMin + m + r);
            right.center = new Vector2(safe.xMax - m - r, safe.yMin + m + r);
        }

        static float Curve(float v)
        {
            float a = Mathf.Abs(v);
            if (a < 0.07f) return 0;
            return Mathf.Sign(v) * Mathf.Pow((a - 0.07f) / 0.93f, 1.5f);
        }

        bool OverUI(Vector2 screenPos)
        {
            var g = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (var r in uiRects) if (r.Contains(g)) return true;
            return false;
        }

        void Update()
        {
            Layout();
            float dt = Time.unscaledDeltaTime;
            orbit = Vector2.zero;
            zoom = 1f;
            actionPressed = false;
            // ---------- Touch ----------
            if (Input.touchCount > 0) touchMode = true;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                {
                    if (OverUI(t.position)) continue;
                    if (left.finger < 0 && Vector2.Distance(t.position, left.center) < left.radius * 1.5f) left.finger = t.fingerId;
                    else if (right.finger < 0 && Vector2.Distance(t.position, right.center) < right.radius * 1.5f) right.finger = t.fingerId;
                    else camTouches[t.fingerId] = t.position;
                }
                if (t.fingerId == left.finger) StickMove(left, t);
                else if (t.fingerId == right.finger) StickMove(right, t);
                else if (camTouches.ContainsKey(t.fingerId))
                {
                    if (t.phase == TouchPhase.Moved)
                    {
                        if (camTouches.Count == 1) orbit += t.deltaPosition;
                        else if (camTouches.Count >= 2)
                        {
                            // Zwei-Finger-Zoom
                            Vector2 other = Vector2.zero;
                            foreach (var kv in camTouches) if (kv.Key != t.fingerId) { other = kv.Value; break; }
                            float d0 = Vector2.Distance(t.position - t.deltaPosition, other), d1 = Vector2.Distance(t.position, other);
                            if (d0 > 10 && d1 > 10) zoom *= d0 / d1;
                        }
                    }
                    camTouches[t.fingerId] = t.position;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) camTouches.Remove(t.fingerId);
                }
            }
            if (Input.touchCount == 0) { left.finger = -1; right.finger = -1; left.value = right.value = Vector2.zero; camTouches.Clear(); }
            // ---------- Maus (PC) ----------
            if (!touchMode)
            {
                if (Input.GetMouseButtonDown(1) || (Input.GetMouseButtonDown(0) && !OverUI(Input.mousePosition))) { mouseOrbit = true; lastMouse = Input.mousePosition; }
                if (Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(0)) mouseOrbit = false;
                if (mouseOrbit && (Input.GetMouseButton(1) || Input.GetMouseButton(0)))
                {
                    var d = Input.mousePosition - lastMouse;
                    orbit += new Vector2(d.x, d.y);
                    lastMouse = Input.mousePosition;
                }
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f) zoom *= Mathf.Exp(-wheel * 0.12f);
            }
            // ---------- Tastatur ----------
            float ramp = dt * 3.5f;
            Vector2 kt = new Vector2(Key(KeyCode.D, KeyCode.A), Key(KeyCode.W, KeyCode.S));
            Vector2 kr = new Vector2(Key(KeyCode.L, KeyCode.J) + Key(KeyCode.RightArrow, KeyCode.LeftArrow), Key(KeyCode.I, KeyCode.K) + Key(KeyCode.UpArrow, KeyCode.DownArrow));
            kb = Vector2.MoveTowards(kb, Vector2.ClampMagnitude(kt, 1.4f), ramp);
            kbR = Vector2.MoveTowards(kbR, Vector2.ClampMagnitude(kr, 1.4f), ramp);
            // Shift = feinfühlig
            float fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 0.35f : 1f;
            state.lx = Mathf.Clamp(Curve(left.value.x) + kb.x * fine, -1, 1);
            state.ly = Mathf.Clamp(Curve(left.value.y) + kb.y * fine, -1, 1);
            state.rx = Mathf.Clamp(Curve(right.value.x) + kbR.x * fine, -1, 1);
            state.ry = Mathf.Clamp(Curve(right.value.y) + kbR.y * fine, -1, 1);
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E)) actionPressed = true;
        }

        float Key(KeyCode a, KeyCode b) { return (Input.GetKey(a) ? 1f : 0f) - (Input.GetKey(b) ? 1f : 0f); }

        void StickMove(Stick s, Touch t)
        {
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { s.finger = -1; s.value = Vector2.zero; s.knob = Vector2.zero; return; }
            var d = (t.position - s.center) / s.radius;
            if (d.magnitude > 1) d.Normalize();
            s.value = d;
            s.knob = d * s.radius * 0.62f;
        }
    }

    /// <summary>Kamera: Verfolgen (hinter dem Fahrer), Kabine (Fahrersicht), Frei (Orbit).</summary>
    public class CameraRig : MonoBehaviour
    {
        public enum Mode { Follow, Cabin, Free }
        public Mode mode = Mode.Follow;
        public Camera cam;
        public VehicleBase target;
        public float yawOff, pitch = 22f, dist = 13f, yawFree;
        Vector3 smoothTarget;
        float yawCur;
        bool init;
        float lookYaw, lookPitch;
        public bool menuOrbit;
        float menuT;

        public void SetTarget(VehicleBase v)
        {
            target = v;
            dist = v ? v.camDistance : 14f;
            pitch = v is TowerCrane ? 32f : 22f;
            yawOff = 0; lookYaw = lookPitch = 0;
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
            if (!cam) return;
            if (menuOrbit || !target)
            {
                menuT += dt;
                float a = menuT * 0.04f + 0.6f;
                cam.transform.position = new Vector3(Mathf.Sin(a) * 75, 32, Mathf.Cos(a) * 75);
                cam.transform.LookAt(new Vector3(0, 2, -4));
                cam.fieldOfView = 50;
                return;
            }
            var o = inp ? inp.orbit : Vector2.zero;
            float z = inp ? inp.zoom : 1f;
            float sens = 0.22f * (inp && inp.touchMode ? 0.6f : 1f);
            if (mode == Mode.Cabin)
            {
                lookYaw += o.x * sens;
                lookPitch = Mathf.Clamp(lookPitch + o.y * sens, -70, 50);
                target.Cabin(out var p, out var f);
                var rot = Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(-lookPitch, lookYaw, 0);
                cam.transform.SetPositionAndRotation(p, rot);
                cam.nearClipPlane = 0.05f;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target.cabinFov, dt * 5);
                return;
            }
            cam.nearClipPlane = 0.1f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 55f, dt * 5);
            var tp = target.CameraTarget();
            if (!init) { smoothTarget = tp; yawCur = target.CameraYaw() + 180f; init = true; }
            smoothTarget = new Vector3(U.Damp(smoothTarget.x, tp.x, 6, dt), U.Damp(smoothTarget.y, tp.y, 4, dt), U.Damp(smoothTarget.z, tp.z, 6, dt));
            dist = Mathf.Clamp(dist * z, 4f, target is TowerCrane ? 110f : 70f);
            pitch = Mathf.Clamp(pitch - o.y * sens, 2f, 85f);
            if (mode == Mode.Free) { yawFree += o.x * sens; yawCur = yawFree; }
            else
            {
                yawOff += o.x * sens;
                yawCur = U.DampAngle(yawCur, target.CameraYaw() + 180f + yawOff, 2.5f, dt);
            }
            var q = Quaternion.Euler(pitch, yawCur + 180f, 0);
            var pos = smoothTarget - q * Vector3.forward * dist;
            var soil = SoilTerrain.I;
            if (soil && soil.Inside(pos.x, pos.z))
            {
                float g = soil.Height(pos.x, pos.z) + 0.8f;
                if (pos.y < g) pos.y = g;
            }
            else if (pos.y < 0.8f) pos.y = 0.8f;
            cam.transform.position = pos;
            cam.transform.LookAt(smoothTarget);
        }
    }
}
