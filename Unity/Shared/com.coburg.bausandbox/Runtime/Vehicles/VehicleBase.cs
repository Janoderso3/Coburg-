using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Zustand der beiden Joysticks (-1..1) und Aktionstasten.</summary>
    public struct ControlInput
    {
        public float lx, ly, rx, ry;
        public bool action;      // einmalig gedrückt (Leertaste / Knopf)
        public bool horn;
        public static ControlInput None => new ControlInput();
    }

    public class ControlsInfo
    {
        public string lUp = "", lDown = "", lLeft = "", lRight = "";
        public string rUp = "", rDown = "", rLeft = "", rRight = "";
        public List<(string id, string label, string hint)> buttons = new List<(string, string, string)>();
    }

    public enum ToastKind { Info, Ok, Warn }

    /// <summary>Meldungen an die Oberfläche (entkoppelt).</summary>
    public static class Hud
    {
        public static event System.Action<string, ToastKind> OnToast;
        public static void Toast(string msg, ToastKind k = ToastKind.Info) { OnToast?.Invoke(msg, k); }
    }

    public static class Settings
    {
        public static bool Iso; // ISO-Steuerung: Joystick vor = Ausleger senken
        public static float Volume = 1f;
    }

    /// <summary>Gemeinsame Basis aller Maschinen: Motor, Abgas, Rundumleuchte, Stabilität, Sound.</summary>
    public abstract class VehicleBase : MonoBehaviour
    {
        public string displayName = "Fahrzeug";
        public string shortName = "Fzg";
        public Rigidbody rb;
        public EngineSynth engine;
        public bool isActive;
        public ControlInput input;
        public float rpm = 750f, idleRpm = 750f, maxRpm = 2100f;
        public float engineLoad;
        public float hydFlow, relief;
        public Transform exhaust;
        public List<Renderer> beacons = new List<Renderer>();
        public float tiltDeg;
        public bool tipped;
        public float stability = 1f; // 0 = kippt
        public float hitWarning;
        protected float beaconT, smokeT;
        public Vector3 spawnPos; public Quaternion spawnRot;
        public float cabinFov = 70f;
        public Transform cabEye;
        public float camDistance = 13f;
        public float camHeight = 2.2f;

        public abstract ControlsInfo Controls();
        public virtual bool OnButton(string id) { return false; }
        public abstract List<(string label, string value, string sub)> HudLines();
        public virtual Vector3 CameraTarget() { return transform.position + Vector3.up * camHeight; }
        public virtual float CameraYaw() { return transform.eulerAngles.y; }
        public virtual float SpeedKmh => rb ? Vector3.Dot(rb.Vel(), transform.forward) * 3.6f : 0f;
        public virtual bool IsMoving => rb && rb.Vel().sqrMagnitude > 0.05f;

        protected void SetupEngine(int cyl, float idle, float max, float res1, float res2, float vol = 1f)
        {
            idleRpm = idle; maxRpm = max; rpm = idle;
            var go = new GameObject("Motor");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 1.5f, -1f);
            go.AddComponent<AudioSource>();
            engine = go.AddComponent<EngineSynth>();
            engine.cylinders = cyl; engine.idleRpm = idle; engine.maxRpm = max;
            engine.exhaustRes1 = res1; engine.exhaustRes2 = res2; engine.volume = vol;
        }

        protected void SetupBody(float massKg, Vector3 com)
        {
            rb = gameObject.GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = massKg;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.SetDamping(0.02f, 0.3f);
            rb.solverIterations = 14;
            rb.solverVelocityIterations = 4;
            rb.centerOfMass = com;
            gameObject.layer = Layers.Vehicle;
        }

        /// <summary>Motor-Drehzahl folgt dem Bedarf (Fahren + Hydraulik), Last für Klang/Abgas.</summary>
        protected void UpdateEngine(float demand, float load, float dt)
        {
            float target = idleRpm + (maxRpm - idleRpm) * Mathf.Clamp01(demand);
            // unter Last sackt die Drehzahl etwas ab
            target -= load * 180f;
            rpm = U.Damp(rpm, target, demand > 0.05f ? 3f : 2f, dt);
            engineLoad = U.Damp(engineLoad, Mathf.Clamp01(load), 5f, dt);
            if (engine)
            {
                engine.rpm = rpm;
                engine.load = engineLoad;
                engine.hydFlow = hydFlow;
                engine.relief = relief;
                engine.volume = Settings.Volume * (isActive ? 1f : 0.55f);
            }
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            // Abgas
            if (exhaust && SoilFX.I)
            {
                smokeT -= dt;
                if (smokeT <= 0)
                {
                    float rn = (rpm - idleRpm) / Mathf.Max(1f, maxRpm - idleRpm);
                    smokeT = Mathf.Lerp(0.16f, 0.05f, rn);
                    SoilFX.I.Smoke(exhaust.position, rb ? rb.Vel() * 0.8f : Vector3.zero, rn * 0.6f + engineLoad * 0.4f, engineLoad);
                }
            }
            // Neigung / Umkippen
            tiltDeg = Vector3.Angle(transform.up, Vector3.up);
            if (tiltDeg > 55f && !tipped) { tipped = true; Hud.Toast($"{displayName} ist umgekippt!", ToastKind.Warn); SoundFX.I?.Play("crash", transform.position, 1f); }
            hitWarning = Mathf.Max(0, hitWarning - dt);
        }

        /// <summary>Stabilitätsreserve: Abstand der Schwerpunktprojektion zum Rand der Aufstandsfläche.</summary>
        protected void ComputeStability(float halfLen, float halfWidth)
        {
            if (!rb) return;
            var com = transform.InverseTransformPoint(rb.worldCenterOfMass);
            float mx = halfWidth - Mathf.Abs(com.x), mz = halfLen - Mathf.Abs(com.z);
            float margin = Mathf.Min(mx / halfWidth, mz / halfLen);
            // Neigung reduziert die Reserve
            margin -= tiltDeg / 40f;
            stability = Mathf.Clamp01(margin);
        }

        public virtual void ResetVehicle()
        {
            transform.SetPositionAndRotation(spawnPos, spawnRot);
            if (rb) { rb.SetVel(Vector3.zero); rb.angularVelocity = Vector3.zero; }
            tipped = false;
        }

        /// <summary>Aufrichten nach Umkippen (Bergung mit Autokran).</summary>
        public void Recover()
        {
            var p = transform.position;
            float y = SoilTerrain.I ? SoilTerrain.I.Height(p.x, p.z) : p.y;
            var yaw = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            transform.SetPositionAndRotation(new Vector3(p.x, y + 0.6f, p.z), yaw);
            if (rb) { rb.SetVel(Vector3.zero); rb.angularVelocity = Vector3.zero; }
            tipped = false;
            Hud.Toast($"{displayName} wurde geborgen und aufgerichtet.", ToastKind.Info);
        }

        public virtual void Cabin(out Vector3 pos, out Vector3 fwd)
        {
            var t = cabEye ? cabEye : transform;
            pos = t.position;
            fwd = t.forward;
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    }
}
