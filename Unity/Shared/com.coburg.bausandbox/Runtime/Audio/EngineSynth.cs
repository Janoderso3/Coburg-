using UnityEngine;

namespace BauSandbox
{
    /// <summary>
    /// Echtzeit-Synthese eines Dieselmotors (Zündimpulse auf Auspuff-Resonanzen, Turbolader, Ansaugrauschen),
    /// dazu Hydraulikpumpe, Druckbegrenzungsventil, Kettenrasseln und Rückfahrwarner.
    /// Läuft im Audio-Thread über OnAudioFilterRead; 3D-Positionierung über eine Hilfsspur mit Wert 1.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineSynth : MonoBehaviour
    {
        // --- Parameter (vom Hauptthread gesetzt) ---
        public volatile float rpm = 800f;
        public volatile float load;      // 0..1 Motorlast
        public volatile float hydFlow;   // 0..1 Ölförderung
        public volatile float relief;    // 0..1 Druckbegrenzungsventil spricht an
        public volatile float trackSpeed; // m/s
        public volatile bool beeper;
        public volatile bool on = true;
        public volatile float volume = 1f;
        public volatile float brakeHiss;  // wird ausgelöst und klingt ab
        public int cylinders = 6;
        public float idleRpm = 750, maxRpm = 2100;
        public float exhaustRes1 = 95f, exhaustRes2 = 360f; // Hz, je nach Motor/Auspuff
        public float electric; // >0: Elektroantrieb (Kran): Surren statt Diesel

        AudioSource src;
        int sr = 48000;
        double crank;
        double nextFire;
        uint seed = 12345;
        // Filterzustände
        Biquad r1, r2, r3, pumpBp, reliefBp, trackBp, trackBp2, intakeLp, outLp, hissBp;
        double tPhase, pPhase, bPhase, eMotorPhase;
        float turboRpm, smoothLoad, smoothRpm, fireEnv, trackPhase, beepT, hissEnv, smoothHyd, smoothRelief;

        struct Biquad
        {
            float b0, b1, b2, a1, a2, z1, z2;
            public void BandPass(float f, float q, float sr)
            {
                float w = 2 * Mathf.PI * f / sr, al = Mathf.Sin(w) / (2 * q), c = Mathf.Cos(w), a0 = 1 + al;
                b0 = al / a0; b1 = 0; b2 = -al / a0; a1 = -2 * c / a0; a2 = (1 - al) / a0;
            }
            public void LowPass(float f, float q, float sr)
            {
                float w = 2 * Mathf.PI * f / sr, al = Mathf.Sin(w) / (2 * q), c = Mathf.Cos(w), a0 = 1 + al;
                b0 = (1 - c) / 2 / a0; b1 = (1 - c) / a0; b2 = b0; a1 = -2 * c / a0; a2 = (1 - al) / a0;
            }
            public float Run(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }

        void Awake()
        {
            sr = AudioSettings.outputSampleRate;
            src = GetComponent<AudioSource>();
            src.clip = OnesClip();
            src.loop = true;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0.6f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 6f;
            src.maxDistance = 400f;
            src.volume = 1f;
            src.playOnAwake = true;
            r1.BandPass(exhaustRes1, 3.5f, sr);
            r2.BandPass(exhaustRes2, 2.5f, sr);
            r3.BandPass(1700f, 1.2f, sr);
            pumpBp.BandPass(900f, 6f, sr);
            reliefBp.BandPass(3200f, 2f, sr);
            trackBp.BandPass(1900f, 3f, sr);
            trackBp2.BandPass(520f, 2.5f, sr);
            intakeLp.LowPass(700f, 0.7f, sr);
            outLp.LowPass(6000f, 0.7f, sr);
            hissBp.BandPass(4500f, 0.8f, sr);
            src.Play();
        }

        static AudioClip ones;
        static AudioClip OnesClip()
        {
            if (ones) return ones;
            int n = 4096;
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = 1f;
            ones = AudioClip.Create("ones", n, 1, AudioSettings.outputSampleRate, false);
            ones.SetData(d, 0);
            return ones;
        }

        float Rand()
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFF) / 32768f - 1f;
        }

        void OnAudioFilterRead(float[] data, int ch)
        {
            float dt = 1f / sr;
            float targetRpm = on ? rpm : 0f;
            float vol = volume;
            bool bp = beeper;
            for (int i = 0; i < data.Length; i += ch)
            {
                smoothRpm += (targetRpm - smoothRpm) * 0.0006f;
                smoothLoad += (load - smoothLoad) * 0.0008f;
                smoothHyd += (hydFlow - smoothHyd) * 0.001f;
                smoothRelief += (relief - smoothRelief) * 0.002f;
                float s = 0f;
                if (electric > 0f)
                {
                    // Elektromotor/Getriebe (Kran)
                    float f = 60f + smoothHyd * 140f;
                    eMotorPhase += f * dt;
                    float ph = (float)(eMotorPhase - System.Math.Floor(eMotorPhase));
                    s += smoothHyd * (Mathf.Sin(ph * Mathf.PI * 2) * 0.1f + Mathf.Sin(ph * Mathf.PI * 6) * 0.06f + Rand() * 0.015f);
                }
                else if (smoothRpm > 50f)
                {
                    // Kurbelwelle: Zündung alle 720°/Zylinderzahl
                    crank += smoothRpm / 60.0 * dt;
                    double interval = 2.0 / cylinders;
                    if (crank >= nextFire)
                    {
                        nextFire += interval;
                        if (nextFire < crank) nextFire = crank + interval;
                        fireEnv = (0.45f + 0.75f * smoothLoad) * (1f + Rand() * 0.18f);
                    }
                    float imp = fireEnv;
                    fireEnv *= 0.82f;
                    float knock = Rand() * fireEnv * (0.15f + smoothLoad * 0.5f);
                    float e = r1.Run(imp * 1.4f) * 1.1f + r2.Run(imp + knock * 0.5f) * 0.55f + r3.Run(knock) * 0.35f;
                    // Ansaug-/Strömungsrauschen
                    float rn = smoothRpm / maxRpm;
                    float intake = intakeLp.Run(Rand()) * (0.04f + 0.18f * rn * (0.4f + smoothLoad));
                    // Turbolader pfeift mit Verzögerung
                    turboRpm += ((0.25f + 0.75f * rn * (0.3f + smoothLoad)) - turboRpm) * 0.00004f;
                    tPhase += (2500f + 9000f * turboRpm) * dt;
                    float turbo = Mathf.Sin((float)(tPhase % 1.0) * Mathf.PI * 2f) * 0.012f * turboRpm * turboRpm;
                    s += (e * (0.55f + 0.45f * rn) + intake + turbo) * 0.9f;
                    // Hydraulikpumpe (9 Kolben)
                    pPhase += smoothRpm / 60f * 9f * dt;
                    float pph = (float)(pPhase % 1.0) * Mathf.PI * 2f;
                    float whine = (Mathf.Sin(pph) + 0.4f * Mathf.Sin(pph * 2) + 0.2f * Mathf.Sin(pph * 3)) * 0.02f * smoothHyd;
                    s += whine + pumpBp.Run(Rand()) * 0.03f * smoothHyd;
                }
                // Druckbegrenzungsventil: Zischen/Pfeifen bei Blockade
                s += reliefBp.Run(Rand()) * 0.12f * smoothRelief;
                // Ketten: Glieder schlagen über Turas
                float ts = trackSpeed;
                if (ts > 0.03f)
                {
                    trackPhase += ts / 0.19f * dt;
                    if (trackPhase >= 1f)
                    {
                        trackPhase -= 1f;
                        float a = 0.5f + Rand() * 0.3f;
                        s += trackBp.Run(a) * 0.5f * Mathf.Min(1f, ts) + trackBp2.Run(a) * 0.35f * Mathf.Min(1f, ts);
                    }
                    else s += trackBp.Run(Rand() * 0.02f) + trackBp2.Run(0);
                }
                // Druckluftbremse
                if (brakeHiss > 0.01f) { hissEnv = Mathf.Max(hissEnv, brakeHiss); brakeHiss = 0; }
                if (hissEnv > 0.001f) { s += hissBp.Run(Rand()) * 0.25f * hissEnv; hissEnv *= 0.99993f; }
                // Rückfahrwarner 1,2 kHz, 0,5 s an / 0,5 s aus
                if (bp)
                {
                    beepT += dt;
                    if ((beepT % 1f) < 0.5f)
                    {
                        bPhase += 1180f * dt;
                        s += ((bPhase % 1.0) < 0.5 ? 0.06f : -0.06f);
                    }
                }
                else beepT = 0;
                s = outLp.Run(s) * vol;
                // weiche Begrenzung
                s = s / (1f + Mathf.Abs(s));
                for (int c = 0; c < ch; c++) data[i + c] *= s;
            }
        }
    }
}
