using System.Collections.Generic;
using UnityEngine;

namespace BauSandbox
{
    /// <summary>
    /// Löffel bzw. Schaufel: Schneidkante löst Boden (mit Auflockerung), Inhalt kippt bei Neigung heraus,
    /// nimmt Brocken und Trümmer auf und beschädigt Bauteile (Abbruch).
    /// </summary>
    public class SoilTool
    {
        public Transform t;
        public VehicleBase owner;
        public float cap, width, power;
        public Vector3 tipL, heelL, openL, tipDirL;
        public float load;
        public SoilMat mat = SoilMat.Soil;
        public Vector3 tip, heel, vel, openN, tipDir, side;
        public float resist, cutRate, spillT, lastDepth;
        public bool blocked;
        public Transform content;
        public Vector3 contentScale = Vector3.one;
        Vector3 prevTip; bool hasPrev;
        float spillAcc, clodAcc;
        readonly Collider[] overlap = new Collider[32];

        public float LoadMassKg => load * Density(mat) * 1000f;

        public static float Density(SoilMat m)
        {
            switch (m) { case SoilMat.Rubble: return 1.45f; case SoilMat.Gravel: return 1.75f; case SoilMat.Concrete: return 2.3f; default: return 1.6f; }
        }

        public void ComputePoints(float dt)
        {
            var m = t.localToWorldMatrix;
            tip = m.MultiplyPoint3x4(tipL);
            heel = m.MultiplyPoint3x4(heelL);
            openN = m.MultiplyVector(openL).normalized;
            tipDir = m.MultiplyVector(tipDirL).normalized;
            side = m.MultiplyVector(Vector3.right).normalized;
            if (hasPrev && dt > 0) vel = (tip - prevTip) / dt; else vel = Vector3.zero;
            prevTip = tip; hasPrev = true;
        }

        public float HoldFraction() { return Mathf.Clamp01((openN.y + 0.55f) / 0.9f); }

        public bool WouldBlock()
        {
            var T = SoilTerrain.I;
            float depth = T.Height(tip.x, tip.z) - tip.y;
            if (depth <= lastDepth + 0.004f) return false;
            if (load >= cap * 1.1f && depth > 0.1f) return true;
            if (depth > 1.3f) return true;
            return false;
        }

        public void CommitDepth()
        {
            lastDepth = SoilTerrain.I.Height(tip.x, tip.z) - tip.y;
        }

        public void Step(float dt)
        {
            var T = SoilTerrain.I;
            resist = 0; cutRate = 0; blocked = false;
            spillT = Mathf.Max(0, spillT - dt);
            float speed = vel.magnitude;
            float g = T.Height(tip.x, tip.z);
            float depth = g - tip.y;
            float hold = HoldFraction();
            float room = Mathf.Max(0, cap * 1.15f * Mathf.Max(0.15f, hold) - load);
            // 1) Graben
            if (depth > 0.01f && speed > 0.03f)
            {
                var vn = vel / speed;
                float into = Vector3.Dot(vn, openN) * 0.75f + Vector3.Dot(vn, tipDir) * 0.5f;
                float hw = width / 2;
                var a = tip - side * hw; var b = tip + side * hw;
                float thick = Mathf.Clamp(speed * dt + 0.15f, 0.15f, 0.4f);
                if (into > 0.12f && room > 0)
                {
                    float maxV = Mathf.Min(room, 3.5f * cap * dt * Mathf.Clamp(into, 0.2f, 1f));
                    var r = T.CutSegment(a, b, tip.y, thick, maxV);
                    if (r.vol > 0)
                    {
                        if (load < 0.01f || r.vol > load * 0.25f) mat = r.mat;
                        load += r.vol;
                        cutRate = r.vol / dt;
                        if (Random.value < 0.12f) SoilFX.I.Dust(new Vector3(tip.x, g, tip.z), 1, SoilFX.DustColor(r.mat), 0.6f);
                    }
                    resist = Mathf.Clamp(depth * 1.4f, 0, 0.85f);
                }
                else
                {
                    // seitliches Schieben: Boden wird verdrängt
                    var r = T.CutSegment(a, b, tip.y, thick, 0.6f * dt * cap * 2f);
                    if (r.vol > 0)
                    {
                        T.Deposit(tip.x + vn.x * 0.6f, tip.z + vn.z * 0.6f, r.vol, r.mat, 0.45f);
                        resist = Mathf.Clamp(depth * 1.2f, 0, 0.7f);
                    }
                }
            }
            // 2) Brocken und Trümmer aufnehmen
            if (room > 0.01f && speed > 0.05f)
            {
                var mid = (tip + heel) * 0.5f;
                int n = Physics.OverlapSphereNonAlloc(mid, width * 0.55f, overlap, Layers.Mask(Layers.Clod, Layers.Debris), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n && room > 0; i++)
                {
                    var s = overlap[i].GetComponentInParent<IScoopable>();
                    if (s == null) continue;
                    // nur was vor der Öffnung liegt
                    if (Vector3.Dot(overlap[i].transform.position - mid, openN) < -0.3f) continue;
                    float v = s.Volume;
                    if (v > room * 1.5f) continue;
                    if (v > load * 0.3f) mat = s.Mat;
                    load += v; room -= v;
                    s.Absorb();
                }
            }
            // 3) Abbruch: Kontakt mit Bauteilen
            if (Buildings.I && power > 0)
            {
                var mid = (tip + heel) * 0.5f;
                var r1 = Buildings.I.ToolHit(tip, width * 0.38f, vel, power, dt);
                var r2 = Buildings.I.ToolHit(mid, width * 0.42f, vel, power * 0.6f, dt);
                if (r1.blocked || r2.blocked) blocked = true;
                if (r1.hit > 0 || r2.hit > 0) { resist = Mathf.Max(resist, 0.6f); if (owner) owner.hitWarning = 0.3f; }
            }
            // 4) Ausschütten bei Neigung
            float maxHold = cap * 1.15f * hold;
            if (load > maxHold + 0.001f)
            {
                float flow = Mathf.Min(load - maxHold, (0.6f + 2.8f * (1 - hold)) * cap * dt);
                Spill(flow);
            }
            // 5) Inhalt darstellen
            if (content)
            {
                float f = Mathf.Clamp(load / cap, 0, 1.15f);
                bool vis = f > 0.02f;
                if (content.gameObject.activeSelf != vis) content.gameObject.SetActive(vis);
                if (vis)
                {
                    float s = Mathf.Pow(f, 1f / 3f);
                    content.localScale = new Vector3(contentScale.x * Mathf.Min(1, s * 1.1f), contentScale.y * s, contentScale.z * Mathf.Min(1, s * 1.05f));
                    var r = content.GetComponent<MeshRenderer>();
                    var key = mat == SoilMat.Rubble ? "rubble_chunk" : mat == SoilMat.Gravel ? "gravel_chunk" : mat == SoilMat.Concrete ? "concrete_wet" : "soil_chunk";
                    if (r.sharedMaterial != Mats.Get(key)) r.sharedMaterial = Mats.Get(key);
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
                float tt = (Random.value - 0.5f) * width * 0.9f;
                var lip = tip + side * tt + openN * 0.15f;
                var v = vel * 0.5f + openN * 0.6f + Random.insideUnitSphere * 0.3f;
                v.y = Mathf.Min(v.y, 0);
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
            if (Random.value < 0.25f) SoundFX.I?.Play("soil", tip, 0.6f, 0.2f);
        }
    }
}
