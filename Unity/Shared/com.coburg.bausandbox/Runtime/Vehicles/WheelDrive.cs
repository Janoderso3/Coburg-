using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>
    /// Radantrieb mit Unity-WheelCollidern, Dieselkennlinie und Automatikgetriebe.
    /// </summary>
    public class WheelDrive
    {
        public class W
        {
            public WheelCollider wc;
            public Transform visual;
            public bool steer, drive;
            public float steerSign = 1f;
        }

        public Rigidbody rb;
        public readonly List<W> wheels = new List<W>();
        public float[] gears = { 6.0f, 3.6f, 2.3f, 1.5f, 1.0f, 0.78f };
        public float reverseGear = 5.5f;
        public float finalDrive = 5.2f;
        public float maxTorque = 1900f;  // Nm
        public float idle = 650f, maxRpm = 2100f, shiftUp = 1850f, shiftDown = 1050f;
        public int gear = 1;            // 1..n, -1 = rückwärts, 0 = neutral
        public float rpm = 650f;
        public float brakeMax = 32000f;
        public float maxSteerDeg = 36f;
        public float speedLimit = 13f;  // m/s
        public float steerAngle;
        public float throttleOut;
        public bool reversing;
        public bool braking;
        float shiftTimer;

        public W Add(Transform parent, Vector3 localPos, float radius, float mass, float suspension, float springK, float damper, bool steer, bool drive, Transform visual)
        {
            var go = new GameObject("Radaufhängung");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.layer = Layers.Vehicle;
            var wc = go.AddComponent<WheelCollider>();
            wc.radius = radius;
            wc.mass = mass;
            wc.suspensionDistance = suspension;
            wc.forceAppPointDistance = 0.1f;
            wc.wheelDampingRate = 1.5f;
            wc.suspensionSpring = new JointSpring { spring = springK, damper = damper, targetPosition = 0.45f };
            wc.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1.0f, asymptoteSlip = 0.9f, asymptoteValue = 0.65f, stiffness = 1.0f };
            wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.22f, extremumValue = 1.0f, asymptoteSlip = 0.6f, asymptoteValue = 0.75f, stiffness = 1.05f };
            wc.ConfigureVehicleSubsteps(5f, 15, 20);
            var w = new W { wc = wc, visual = visual, steer = steer, drive = drive };
            wheels.Add(w);
            return w;
        }

        /// <summary>throttle -1..1 (rückwärts negativ), steer -1..1</summary>
        public void Step(float throttle, float steer, float dt, float extraLoad = 0f)
        {
            float v = Vector3.Dot(rb.Vel(), rb.transform.forward);
            float absV = Mathf.Abs(v);
            // Lenkung: bei Tempo weniger Einschlag
            float steerLimit = maxSteerDeg * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(absV / 12f));
            steerAngle = U.Approach(steerAngle, steer * steerLimit, 45f * dt);
            // Fahrtrichtungswechsel nur im Stand
            bool wantRev = throttle < -0.05f;
            bool wantFwd = throttle > 0.05f;
            braking = false;
            float drive = 0f, brake = 0f;
            if (wantFwd)
            {
                if (v < -0.4f) { brake = brakeMax * throttle; braking = true; }
                else { if (gear < 1) gear = 1; drive = throttle; }
            }
            else if (wantRev)
            {
                if (v > 0.4f) { brake = brakeMax * -throttle; braking = true; }
                else { gear = -1; drive = -throttle; }
            }
            else
            {
                // Motorbremse, im Stand Feststellbremse
                brake = absV < 0.6f ? brakeMax * 0.6f : brakeMax * 0.04f;
            }
            reversing = gear == -1 && absV > 0.05f && v < 0.05f;
            // Drehzahl aus Raddrehzahl
            float ratio = gear == -1 ? reverseGear : gear >= 1 ? gears[gear - 1] : 0f;
            float wheelRpm = 0; int dn = 0;
            foreach (var w in wheels) if (w.drive) { wheelRpm += Mathf.Abs(w.wc.rpm); dn++; }
            if (dn > 0) wheelRpm /= dn;
            float engineRpmFromWheels = wheelRpm * ratio * finalDrive;
            float targetRpm = Mathf.Max(idle + (maxRpm - idle) * 0.35f * drive, engineRpmFromWheels);
            if (drive < 0.05f) targetRpm = Mathf.Max(idle, engineRpmFromWheels * 0.9f);
            rpm = U.Damp(rpm, Mathf.Clamp(targetRpm, idle, maxRpm + 100f), 8f, dt);
            // Automatik
            shiftTimer -= dt;
            if (gear >= 1 && shiftTimer <= 0)
            {
                if (rpm > shiftUp && gear < gears.Length) { gear++; shiftTimer = 0.9f; }
                else if (rpm < shiftDown && gear > 1) { gear--; shiftTimer = 0.9f; }
            }
            // Drehmomentkurve (Diesel: Plateau 1000-1600 U/min)
            float rn = Mathf.InverseLerp(idle, maxRpm, rpm);
            float tq = maxTorque * Mathf.Clamp01(0.7f + 0.6f * rn - 0.55f * rn * rn) * (rpm > maxRpm ? 0f : 1f);
            if (absV > speedLimit) drive = 0;
            float wheelTorque = tq * ratio * finalDrive * drive * 0.88f / Mathf.Max(1, dn);
            if (shiftTimer > 0.6f) wheelTorque *= 0.2f; // Zugkraftunterbrechung beim Schalten
            if (gear == -1) wheelTorque = -wheelTorque;
            throttleOut = drive;
            var soil = SoilTerrain.I;
            foreach (var w in wheels)
            {
                if (w.steer) w.wc.steerAngle = steerAngle * w.steerSign;
                w.wc.motorTorque = w.drive ? wheelTorque : 0f;
                w.wc.brakeTorque = brake;
                // Untergrund: lose Erde -> weniger Grip
                if (soil && w.wc.GetGroundHit(out var hit))
                {
                    float loose = Mathf.Clamp01(soil.LooseAt(hit.point.x, hit.point.z) * 4f);
                    var ff = w.wc.forwardFriction; ff.stiffness = Mathf.Lerp(1.0f, 0.7f, loose); w.wc.forwardFriction = ff;
                    var sf = w.wc.sidewaysFriction; sf.stiffness = Mathf.Lerp(1.05f, 0.75f, loose); w.wc.sidewaysFriction = sf;
                }
                if (w.visual)
                {
                    w.wc.GetWorldPose(out var pos, out var rot);
                    w.visual.SetPositionAndRotation(pos, rot);
                }
            }
        }

        public void SetSpring(float k, float damper)
        {
            foreach (var w in wheels) w.wc.suspensionSpring = new JointSpring { spring = k, damper = damper, targetPosition = 0.45f };
        }

        public bool AnyGrounded()
        {
            foreach (var w in wheels) if (w.wc.isGrounded) return true;
            return false;
        }

        public void Wear(float dt)
        {
            var soil = SoilTerrain.I;
            if (!soil) return;
            foreach (var w in wheels)
                if (w.wc.GetGroundHit(out var hit) && hit.collider.gameObject.layer == Layers.Terrain && Mathf.Abs(w.wc.rpm) > 5f)
                    soil.Compact(hit.point, 0.25f, rb.mass / 20000f * 0.7f, dt);
        }
    }
}
