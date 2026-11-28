using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>Einzelgeräusche (Einsturz, Aufprall, Glas, Erde, Metall) – beim Start synthetisiert, über einen Quellen-Pool abgespielt.</summary>
    public class SoundFX : MonoBehaviour
    {
        public static SoundFX I;
        readonly Dictionary<string, List<AudioClip>> clips = new Dictionary<string, List<AudioClip>>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        readonly Dictionary<string, float> last = new Dictionary<string, float>();
        int next;
        int sr;
        uint seed = 777;

        void Awake()
        {
            I = this;
            sr = AudioSettings.outputSampleRate;
            for (int v = 0; v < 4; v++) Add("crash", Crash(1.4f + v * 0.3f, v));
            for (int v = 0; v < 3; v++) Add("thud", Thud(0.5f, 55f + v * 12f));
            for (int v = 0; v < 3; v++) Add("glass", Glass(v));
            for (int v = 0; v < 3; v++) Add("soil", SoilSlide(0.7f + v * 0.2f));
            for (int v = 0; v < 3; v++) Add("clank", Clank(v));
            Add("metal", MetalHit());
            Add("pour", SoilSlide(1.6f));
            Add("success", Chime(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }));
            Add("fail", Chime(new[] { 392f, 311f, 261.6f }));
            Add("click", Click());
            for (int i = 0; i < 16; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 5f;
                s.maxDistance = 300f;
                s.dopplerLevel = 0f;
                pool.Add(s);
            }
        }

        void Add(string k, AudioClip c)
        {
            if (!clips.TryGetValue(k, out var l)) clips[k] = l = new List<AudioClip>();
            l.Add(c);
        }

        public void Play(string key, Vector3 pos, float volume = 1f, float minGap = 0.05f, bool ui = false)
        {
            if (!clips.TryGetValue(key, out var l)) return;
            float now = Time.unscaledTime;
            if (last.TryGetValue(key, out var t) && now - t < minGap) return;
            last[key] = now;
            var s = pool[next];
            next = (next + 1) % pool.Count;
            s.transform.position = pos;
            s.spatialBlend = ui ? 0f : 1f;
            s.clip = l[Random.Range(0, l.Count)];
            s.volume = Mathf.Clamp01(volume);
            s.pitch = Random.Range(0.9f, 1.1f);
            s.Play();
        }

        public void PlayUI(string key, float volume = 0.6f) { Play(key, Vector3.zero, volume, 0.05f, true); }

        float R()
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFF) / 32768f - 1f;
        }

        AudioClip Make(string name, float[] d)
        {
            var c = AudioClip.Create(name, d.Length, 1, sr, false);
            c.SetData(d, 0);
            return c;
        }

        AudioClip Crash(float dur, int variant)
        {
            int n = (int)(dur * sr);
            var d = new float[n];
            float lp = 0, lp2 = 0;
            float thumpF = 48f + variant * 6f;
            // mehrere Teil-Einschläge (Mauerwerk bricht nacheinander)
            var hits = new float[6 + variant * 2];
            for (int h = 0; h < hits.Length; h++) hits[h] = Mathf.Abs(R()) * dur * 0.6f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float env = Mathf.Exp(-t * 3.2f);
                float noise = R();
                lp += (noise - lp) * 0.12f;
                lp2 += (lp - lp2) * 0.08f;
                float thump = Mathf.Sin(2 * Mathf.PI * thumpF * t * (1 - t * 0.3f)) * Mathf.Exp(-t * 7f) * 0.8f;
                float rattle = 0;
                foreach (var ht in hits)
                {
                    float dt2 = t - ht;
                    if (dt2 > 0 && dt2 < 0.25f) rattle += R() * Mathf.Exp(-dt2 * 30f) * 0.5f;
                }
                d[i] = (lp * 0.7f + lp2 * 1.2f) * env + thump + rattle * env;
            }
            Normalize(d, 0.9f);
            return Make("crash", d);
        }

        AudioClip Thud(float dur, float f)
        {
            int n = (int)(dur * sr);
            var d = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                lp += (R() - lp) * 0.05f;
                d[i] = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 12f) + lp * Mathf.Exp(-t * 18f) * 2f;
            }
            Normalize(d, 0.8f);
            return Make("thud", d);
        }

        AudioClip Glass(int v)
        {
            int n = (int)(0.9f * sr);
            var d = new float[n];
            float[] fr = { 2300 + v * 300, 3700 + v * 200, 5200, 7100 };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0;
                for (int k = 0; k < fr.Length; k++) s += Mathf.Sin(2 * Mathf.PI * fr[k] * t + k) * Mathf.Exp(-t * (6 + k * 3));
                float shards = (Random.value < 0.002f ? 1 : 0) * Mathf.Exp(-t * 3f);
                d[i] = s * 0.3f + R() * Mathf.Exp(-t * 25f) * 0.5f + shards;
            }
            Normalize(d, 0.6f);
            return Make("glass", d);
        }

        AudioClip SoilSlide(float dur)
        {
            int n = (int)(dur * sr);
            var d = new float[n];
            float lp = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur)) * (0.7f + 0.3f * Mathf.Sin(t * 37f));
                float x = R();
                lp += (x - lp) * 0.25f;
                hp = lp - prev; prev = lp;
                float grains = Random.value < 0.01f ? R() * 2f : 0;
                d[i] = (lp * 0.6f + hp * 0.8f + grains) * env;
            }
            Normalize(d, 0.5f);
            return Make("soil", d);
        }

        AudioClip Clank(int v)
        {
            int n = (int)(0.4f * sr);
            var d = new float[n];
            float[] fr = { 840 + v * 90, 1320 + v * 60, 2210, 3170 };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0;
                for (int k = 0; k < fr.Length; k++) s += Mathf.Sin(2 * Mathf.PI * fr[k] * t) * Mathf.Exp(-t * (9 + k * 5));
                d[i] = s * 0.4f + R() * Mathf.Exp(-t * 60f) * 0.6f;
            }
            Normalize(d, 0.6f);
            return Make("clank", d);
        }

        AudioClip MetalHit()
        {
            int n = (int)(1.5f * sr);
            var d = new float[n];
            float[] fr = { 180, 410, 690, 1130, 1580 };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0;
                for (int k = 0; k < fr.Length; k++) s += Mathf.Sin(2 * Mathf.PI * fr[k] * t) * Mathf.Exp(-t * (2 + k * 1.5f)) / (k + 1);
                d[i] = s + R() * Mathf.Exp(-t * 40f) * 0.4f;
            }
            Normalize(d, 0.7f);
            return Make("metal", d);
        }

        AudioClip Chime(float[] notes)
        {
            float step = 0.12f;
            int n = (int)((notes.Length * step + 0.8f) * sr);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0;
                for (int k = 0; k < notes.Length; k++)
                {
                    float tt = t - k * step;
                    if (tt < 0) continue;
                    s += (Mathf.Sin(2 * Mathf.PI * notes[k] * tt) + 0.3f * Mathf.Sin(4 * Mathf.PI * notes[k] * tt)) * Mathf.Exp(-tt * 4f);
                }
                d[i] = s;
            }
            Normalize(d, 0.5f);
            return Make("chime", d);
        }

        AudioClip Click()
        {
            int n = (int)(0.05f * sr);
            var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)sr; d[i] = Mathf.Sin(2 * Mathf.PI * 1800 * t) * Mathf.Exp(-t * 120f); }
            return Make("click", d);
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 0.0001f;
            foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }
}
