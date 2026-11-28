using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox
{
    /// <summary>Einzelgeräusche (Einsturz, Aufprall, Glas, Erde, Metall) – beim Start synthetisiert, über einen Quellen-Pool abgespielt.</summary>
    public partial class SoundFX : Node3D
    {
        public static SoundFX I;
        readonly Dictionary<string, List<AudioStreamWav>> clips = new Dictionary<string, List<AudioStreamWav>>();
        readonly List<AudioStreamPlayer3D> pool = new List<AudioStreamPlayer3D>();
        AudioStreamPlayer uiPlayer;
        readonly Dictionary<string, float> last = new Dictionary<string, float>();
        int next;
        int sr;
        uint seed = 777;

        public override void _Ready()
        {
            I = this;
            sr = 44100;
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
                var p = new AudioStreamPlayer3D { UnitSize = 6f, MaxDistance = 300f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Logarithmic };
                AddChild(p);
                pool.Add(p);
            }
            uiPlayer = new AudioStreamPlayer();
            AddChild(uiPlayer);
        }

        void Add(string k, AudioStreamWav c)
        {
            if (!clips.TryGetValue(k, out var l)) clips[k] = l = new List<AudioStreamWav>();
            l.Add(c);
        }

        public void Play(string key, Vector3 pos, float volume = 1f, float minGap = 0.05f)
        {
            if (!clips.TryGetValue(key, out var l)) return;
            float now = Time.GetTicksMsec() / 1000f;
            if (last.TryGetValue(key, out var t) && now - t < minGap) return;
            last[key] = now;
            var s = pool[next];
            next = (next + 1) % pool.Count;
            s.GlobalPosition = pos;
            s.Stream = l[U.R.Next(l.Count)];
            s.VolumeDb = Mathf.LinearToDb(Math.Clamp(volume, 0.01f, 1f) * Settings.Volume);
            s.PitchScale = U.Rand(0.9f, 1.1f);
            s.Play();
        }

        public void PlayUI(string key, float volume = 0.6f)
        {
            if (!clips.TryGetValue(key, out var l)) return;
            uiPlayer.Stream = l[0];
            uiPlayer.VolumeDb = Mathf.LinearToDb(volume * Settings.Volume);
            uiPlayer.Play();
        }

        float R()
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFF) / 32768f - 1f;
        }

        AudioStreamWav Make(string name, float[] d)
        {
            var bytes = new byte[d.Length * 2];
            for (int i = 0; i < d.Length; i++)
            {
                short v = (short)Math.Clamp((int)(d[i] * 32767f), -32768, 32767);
                bytes[i * 2] = (byte)(v & 0xFF);
                bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = sr, Stereo = false, Data = bytes, ResourceName = name };
        }

        AudioStreamWav Crash(float dur, int variant)
        {
            int n = (int)(dur * sr);
            var d = new float[n];
            float lp = 0, lp2 = 0;
            float thumpF = 48f + variant * 6f;
            // mehrere Teil-Einschläge (Mauerwerk bricht nacheinander)
            var hits = new float[6 + variant * 2];
            for (int h = 0; h < hits.Length; h++) hits[h] = MathF.Abs(R()) * dur * 0.6f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float env = MathF.Exp(-t * 3.2f);
                float noise = R();
                lp += (noise - lp) * 0.12f;
                lp2 += (lp - lp2) * 0.08f;
                float thump = MathF.Sin(2 * MathF.PI * thumpF * t * (1 - t * 0.3f)) * MathF.Exp(-t * 7f) * 0.8f;
                float rattle = 0;
                foreach (var ht in hits)
                {
                    float dt2 = t - ht;
                    if (dt2 > 0 && dt2 < 0.25f) rattle += R() * MathF.Exp(-dt2 * 30f) * 0.5f;
                }
                d[i] = (lp * 0.7f + lp2 * 1.2f) * env + thump + rattle * env;
            }
            Normalize(d, 0.9f);
            return Make("crash", d);
        }

        AudioStreamWav Thud(float dur, float f)
        {
            int n = (int)(dur * sr);
            var d = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                lp += (R() - lp) * 0.05f;
                d[i] = MathF.Sin(2 * MathF.PI * f * t) * MathF.Exp(-t * 12f) + lp * MathF.Exp(-t * 18f) * 2f;
            }
            Normalize(d, 0.8f);
            return Make("thud", d);
        }

        AudioStreamWav Glass(int v)
        {
            int n = (int)(0.9f * sr);
            var d = new float[n];
            float[] fr = { 2300 + v * 300, 3700 + v * 200, 5200, 7100 };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0;
                for (int k = 0; k < fr.Length; k++) s += MathF.Sin(2 * MathF.PI * fr[k] * t + k) * MathF.Exp(-t * (6 + k * 3));
                float shards = (U.Rand() < 0.002f ? 1 : 0) * MathF.Exp(-t * 3f);
                d[i] = s * 0.3f + R() * MathF.Exp(-t * 25f) * 0.5f + shards;
            }
            Normalize(d, 0.6f);
            return Make("glass", d);
        }

        AudioStreamWav SoilSlide(float dur)
        {
            int n = (int)(dur * sr);
            var d = new float[n];
            float lp = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float env = MathF.Sin(MathF.PI * U.Clamp01(t / dur)) * (0.7f + 0.3f * MathF.Sin(t * 37f));
                float x = R();
                lp += (x - lp) * 0.25f;
                hp = lp - prev; prev = lp;
                float grains = U.Rand() < 0.01f ? R() * 2f : 0;
                d[i] = (lp * 0.6f + hp * 0.8f + grains) * env;
            }
            Normalize(d, 0.5f);
            return Make("soil", d);
        }

        AudioStreamWav Clank(int v)
        {
            int n = (int)(0.4f * sr);
            var d = new float[n];
            float[] fr = { 840 + v * 90, 1320 + v * 60, 2210, 3170 };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0;
                for (int k = 0; k < fr.Length; k++) s += MathF.Sin(2 * MathF.PI * fr[k] * t) * MathF.Exp(-t * (9 + k * 5));
                d[i] = s * 0.4f + R() * MathF.Exp(-t * 60f) * 0.6f;
            }
            Normalize(d, 0.6f);
            return Make("clank", d);
        }

        AudioStreamWav MetalHit()
        {
            int n = (int)(1.5f * sr);
            var d = new float[n];
            float[] fr = { 180, 410, 690, 1130, 1580 };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0;
                for (int k = 0; k < fr.Length; k++) s += MathF.Sin(2 * MathF.PI * fr[k] * t) * MathF.Exp(-t * (2 + k * 1.5f)) / (k + 1);
                d[i] = s + R() * MathF.Exp(-t * 40f) * 0.4f;
            }
            Normalize(d, 0.7f);
            return Make("metal", d);
        }

        AudioStreamWav Chime(float[] notes)
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
                    s += (MathF.Sin(2 * MathF.PI * notes[k] * tt) + 0.3f * MathF.Sin(4 * MathF.PI * notes[k] * tt)) * MathF.Exp(-tt * 4f);
                }
                d[i] = s;
            }
            Normalize(d, 0.5f);
            return Make("chime", d);
        }

        AudioStreamWav Click()
        {
            int n = (int)(0.05f * sr);
            var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)sr; d[i] = MathF.Sin(2 * MathF.PI * 1800 * t) * MathF.Exp(-t * 120f); }
            return Make("click", d);
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 0.0001f;
            foreach (var x in d) m = MathF.Max(m, MathF.Abs(x));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }
}
