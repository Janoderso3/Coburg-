using Godot;
using System;
using System.Collections.Generic;

namespace BauSandbox
{
    /// <summary>
    /// Löffel bzw. Schaufel: Schneidkante löst Boden (mit Auflockerung), Inhalt kippt bei Neigung heraus,
    /// nimmt Brocken und Trümmer auf und beschädigt Bauteile (Abbruch).
    /// </summary>
    public class SoilTool
    {
        public Node3D t;
        public VehicleBase owner;
        public float cap, width, power;
        public Vector3 tipL, heelL, openL, tipDirL;
        public float load;
        public SoilMat mat = SoilMat.Soil;
        public Vector3 tip, heel, vel, openN, tipDir, side;
        public float resist, cutRate, spillT, lastDepth;
        public bool blocked;
        public Node3D content;
        public Vector3 contentScale = Vector3.One;
        Vector3 prevTip; bool hasPrev;
        float spillAcc, clodAcc;
        PhysicsShapeQueryParameters3D sq;

        public float LoadMassKg => load * Density(mat) * 1000f;

        public static float Density(SoilMat m)
        {
            switch (m) { case SoilMat.Rubble: return 1.45f; case SoilMat.Gravel: return 1.75f; case SoilMat.Concrete: return 2.3f; default: return 1.6f; }
        }

        public void ComputePoints(float dt)
        {
            var m = t.GlobalTransform;
            tip = m * tipL;
            heel = m * heelL;
            openN = (m.Basis * openL).Normalized();
            tipDir = (m.Basis * tipDirL).Normalized();
            side = (m.Basis * Vector3.Right).Normalized();
            if (hasPrev && dt > 0) vel = (tip - prevTip) / dt; else vel = Vector3.Zero;
            prevTip = tip; hasPrev = true;
        }

        public float HoldFraction() { return U.Clamp01((openN.Y + 0.55f) / 0.9f); }

        public bool WouldBlock()
        {
            var T = SoilTerrain.I;
            float depth = T.Height(tip.X, tip.Z) - tip.Y;
            if (depth <= lastDepth + 0.004f) return false;
            if (load >= cap * 1.1f && depth > 0.1f) return true;
            if (depth > 1.3f) return true;
            return false;
        }

        public void CommitDepth()
        {
            lastDepth = SoilTerrain.I.Height(tip.X, tip.Z) - tip.Y;
        }

        public void Step(float dt)
        {
            var T = SoilTerrain.I;
            resist = 0; cutRate = 0; blocked = false;
            spillT = MathF.Max(0, spillT - dt);
            float speed = vel.Length();
            float g = T.Height(tip.X, tip.Z);
            float depth = g - tip.Y;
            float hold = HoldFraction();
            float room = MathF.Max(0, cap * 1.15f * MathF.Max(0.15f, hold) - load);
            // 1) Graben
            if (depth > 0.01f && speed > 0.03f)
            {
                var vn = vel / speed;
                float into = vn.Dot(openN) * 0.75f + vn.Dot(tipDir) * 0.5f;
                float hw = width / 2;
                var a = tip - side * hw; var b = tip + side * hw;
                float thick = Math.Clamp(speed * dt + 0.15f, 0.15f, 0.4f);
                if (into > 0.12f && room > 0)
                {
                    float maxV = MathF.Min(room, 3.5f * cap * dt * Math.Clamp(into, 0.2f, 1f));
                    var r = T.CutSegment(a, b, tip.Y, thick, maxV);
                    if (r.vol > 0)
                    {
                        if (load < 0.01f || r.vol > load * 0.25f) mat = r.mat;
                        load += r.vol;
                        cutRate = r.vol / dt;
                        if (U.Rand() < 0.12f) SoilFX.I.Dust(new Vector3(tip.X, g, tip.Z), 1, SoilFX.DustColor(r.mat), 0.6f);
                    }
                    resist = Math.Clamp(depth * 1.4f, 0, 0.85f);
                }
                else if (vn.Y > 0.35f)
                {
                    // Herausziehen: kaum Widerstand, kein Verdrängen (sonst wächst der Boden mit dem Löffel mit)
                    resist = Math.Clamp(depth * 0.3f, 0, 0.3f);
                }
                else if (vn.Y < -0.6f)
                {
                    // Löffelrücken drückt auf den Boden: blockiert, Maschine stemmt sich hoch
                    resist = 0.85f;
                    if (depth > 0.08f) blocked = true;
                }
                else
                {
                    // seitliches Schieben: Boden wird verdrängt
                    var r = T.CutSegment(a, b, tip.Y, thick, 0.6f * dt * cap * 2f);
                    if (r.vol > 0)
                    {
                        T.Deposit(tip.X + vn.X * 0.6f, tip.Z + vn.Z * 0.6f, r.vol, r.mat, 0.45f);
                        resist = Math.Clamp(depth * 1.2f, 0, 0.7f);
                    }
                }
            }
            // 2) Brocken und Trümmer aufnehmen
            if (room > 0.01f && speed > 0.05f && owner != null)
            {
                var mid = (tip + heel) * 0.5f;
                sq ??= new PhysicsShapeQueryParameters3D { CollisionMask = Layers.Clod | Layers.Debris, Shape = new SphereShape3D() };
                ((SphereShape3D)sq.Shape).Radius = width * 0.55f;
                sq.Transform = new Transform3D(Basis.Identity, mid);
                var hits = owner.GetWorld3D().DirectSpaceState.IntersectShape(sq, 16);
                foreach (var h in hits)
                {
                    if (room <= 0) break;
                    if (h["collider"].AsGodotObject() is not IScoopable sc) continue;
                    var node = (Node3D)sc;
                    if ((node.GlobalPosition - mid).Dot(openN) < -0.3f) continue;
                    float v = sc.Volume;
                    if (v > room * 1.5f) continue;
                    if (v > load * 0.3f) mat = sc.Mat;
                    load += v; room -= v;
                    sc.Absorb();
                }
            }
            // 3) Abbruch: Kontakt mit Bauteilen
            if (Buildings.I != null && power > 0)
            {
                var mid = (tip + heel) * 0.5f;
                var r1 = Buildings.I.ToolHit(tip, width * 0.38f, vel, power, dt);
                var r2 = Buildings.I.ToolHit(mid, width * 0.42f, vel, power * 0.6f, dt);
                if (r1.blocked || r2.blocked) blocked = true;
                if (r1.hit > 0 || r2.hit > 0) { resist = MathF.Max(resist, 0.6f); if (owner != null) owner.hitWarning = 0.3f; }
            }
            // 4) Ausschütten bei Neigung
            float maxHold = cap * 1.15f * hold;
            if (load > maxHold + 0.001f)
            {
                float flow = MathF.Min(load - maxHold, (0.6f + 2.8f * (1 - hold)) * cap * dt);
                Spill(flow);
            }
            // 5) Inhalt darstellen
            if (content != null)
            {
                float f = Math.Clamp(load / cap, 0, 1.15f);
                bool vis = f > 0.02f;
                content.Visible = vis;
                if (vis)
                {
                    float s = MathF.Pow(f, 1f / 3f);
                    content.Scale = new Vector3(contentScale.X * MathF.Min(1, s * 1.1f), contentScale.Y * s, contentScale.Z * MathF.Min(1, s * 1.05f));
                    if (content is MeshInstance3D mi) mi.MaterialOverride = Mats.Get(SoilFX.MatKey(mat));
                }
            }
        }

        public void Spill(float vol)
        {
            if (vol <= 0) return;
            load -= vol;
            spillT = 0.6f;
            spillAcc += vol;
            clodAcc += vol;
            const float chunk = 0.025f;
            while (spillAcc > chunk)
            {
                spillAcc -= chunk;
                float tt = (U.Rand() - 0.5f) * width * 0.9f;
                var lip = tip + side * tt + openN * 0.15f;
                var v = vel * 0.5f + openN * 0.6f + U.RandSphere() * 0.3f;
                v.Y = MathF.Min(v.Y, 0);
                SoilFX.I.SpawnFalling(lip, v, chunk, mat);
            }
            // ein Teil fällt als zusammenhängende Brocken
            if (clodAcc > 0.35f && mat != SoilMat.Concrete)
            {
                float cv = clodAcc * 0.25f;
                clodAcc = 0;
                load -= 0; // Volumen ist im Spill enthalten: Brocken verringern die Krümel
                spillAcc -= cv;
                SoilFX.I.SpawnClod(tip + openN * 0.3f, cv, mat, vel * 0.5f + openN * 0.5f);
            }
            if (U.Rand() < 0.25f) SoundFX.I?.Play("soil", tip, 0.6f, 0.2f);
        }
    }
}
