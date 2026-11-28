using Godot;
using System;

namespace BauSandbox;

/// <summary>Physik-Ebenen als Bitmasken (Godot: Ebene n = Bit n-1).</summary>
public static class Layers
{
    public const uint Terrain = 1 << 0;
    public const uint Vehicle = 1 << 1;
    public const uint Tool = 1 << 2;      // Löffel, Schild: Bodenkontakt wird selbst berechnet
    public const uint Building = 1 << 3;  // intakte Bauteile und feste Hindernisse
    public const uint Debris = 1 << 4;
    public const uint Clod = 1 << 5;
    public const uint Element = 1 << 6;   // Fertigteile
    public const uint Hook = 1 << 7;      // Kranhaken, Abrissbirne

    public const uint Ground = Terrain | Building | Debris | Clod | Element;
    public const uint All = Terrain | Vehicle | Building | Debris | Clod | Element | Hook;
}

public static class U
{
    public static float Approach(float v, float target, float rate)
    {
        if (v < target) return MathF.Min(target, v + rate);
        return MathF.Max(target, v - rate);
    }
    public static float Damp(float v, float target, float lambda, float dt) => Mathf.Lerp(v, target, 1f - MathF.Exp(-lambda * dt));
    public static Vector3 Damp(Vector3 v, Vector3 target, float lambda, float dt) => v.Lerp(target, 1f - MathF.Exp(-lambda * dt));
    public static float DampAngle(float a, float target, float lambda, float dt) => a + Mathf.AngleDifference(a, target) * (1f - MathF.Exp(-lambda * dt));
    public static float Smooth(float a, float b, float x) { float t = Mathf.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
    public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

    static readonly int[] perm = BuildPerm();
    static int[] BuildPerm()
    {
        var p = new int[512];
        var r = new Random(1337);
        var b = new int[256];
        for (int i = 0; i < 256; i++) b[i] = i;
        for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); (b[i], b[j]) = (b[j], b[i]); }
        for (int i = 0; i < 512; i++) p[i] = b[i & 255];
        return p;
    }
    static float Hash(int x, int y) => perm[(perm[x & 255] + y) & 511] / 255f;

    public static float Noise(float x, float y)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) * 2f - 1f;
    }
    public static float Fbm(float x, float y, int oct = 4)
    {
        float s = 0, a = 0.5f, f = 1;
        for (int i = 0; i < oct; i++) { s += a * Noise(x * f, y * f); f *= 2.03f; a *= 0.5f; }
        return s;
    }
    public static float TileNoise(float x, float y, int p)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        int x0 = ((xi % p) + p) % p, y0 = ((yi % p) + p) % p, x1 = (x0 + 1) % p, y1 = (y0 + 1) % p;
        float a = Hash(x0, y0), b = Hash(x1, y0), c = Hash(x0, y1), d = Hash(x1, y1);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) * 2f - 1f;
    }

    static readonly System.Globalization.CultureInfo de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
    public static string Fmt(float v, int d = 1) => v.ToString("F" + d, de);
    public static string Time(float s) => $"{(int)(s / 60)}:{(int)(s % 60):00}";

    public static Vector3 Flat(Vector3 v) { v.Y = 0; return v; }

    public static Color Hex(string h, float a = 1f) { var c = Color.FromHtml(h); c.A = a; return c; }

    /// <summary>Geschwindigkeit eines Punkts eines Körpers.</summary>
    public static Vector3 PointVelocity(RigidBody3D b, Vector3 p)
    {
        var com = b.GlobalTransform * b.CenterOfMass;
        return b.LinearVelocity + b.AngularVelocity.Cross(p - com);
    }

    /// <summary>Kraft an einem Weltpunkt (Godot erwartet den Versatz zum Körperursprung).</summary>
    public static void ForceAt(RigidBody3D b, Vector3 force, Vector3 point) => b.ApplyForce(force, point - b.GlobalPosition);
    public static void ImpulseAt(RigidBody3D b, Vector3 imp, Vector3 point) => b.ApplyImpulse(imp, point - b.GlobalPosition);

    public static readonly Random R = new Random(42);
    public static float Rand() => (float)R.NextDouble();
    public static float Rand(float a, float b) => a + (b - a) * (float)R.NextDouble();
    public static Vector3 RandSphere()
    {
        while (true)
        {
            var v = new Vector3(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1));
            if (v.LengthSquared() <= 1) return v;
        }
    }
}

public class Rng
{
    uint s;
    public Rng(int seed) { s = (uint)(seed * 2654435761u) | 1u; }
    public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216f; }
    public float Range(float a, float b) => a + (b - a) * Next();
}

public enum ToastKind { Info, Ok, Warn }

/// <summary>Meldungen an die Oberfläche.</summary>
public static class Hud
{
    public static event Action<string, ToastKind> OnToast;
    public static void Toast(string msg, ToastKind k = ToastKind.Info) => OnToast?.Invoke(msg, k);
}

public static class Settings
{
    public static bool Iso;
    public static float Volume = 1f;
    public static int Quality = 2; // 0 niedrig, 1 mittel, 2 hoch, 3 ultra
}
