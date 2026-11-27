// Verformbares Gelände: Höhenfeld mit Bodenschichten, losem Material und Böschungswinkel
import { clamp, lerp, srgbToLinear, noiseTexture, noise2 } from './util.js';

// Materialarten für loses Material (fill)
export const MAT = { NATURAL: 0, SOIL: 1, RUBBLE: 2, GRAVEL: 3, CONCRETE: 4 };
export const MAT_NAME = { 1: 'Erde', 2: 'Bauschutt', 3: 'Schotter', 4: 'Beton' };
// Schüttdichte in t/m³ (realistische Werte für loses Material)
export const MAT_DENSITY = { 1: 1.6, 2: 1.45, 3: 1.75, 4: 2.3 };

const lin = (r, g, b) => [srgbToLinear(r), srgbToLinear(g), srgbToLinear(b)];
const C = {
  grass1: lin(0.33, 0.45, 0.19),
  grass2: lin(0.42, 0.50, 0.22),
  grassDry: lin(0.52, 0.52, 0.30),
  mud: lin(0.36, 0.28, 0.19),
  topsoil: lin(0.27, 0.20, 0.13),
  clay: lin(0.60, 0.44, 0.26),
  sand: lin(0.74, 0.64, 0.46),
  deep: lin(0.62, 0.58, 0.50),
  soil: lin(0.40, 0.30, 0.20),
  rubble: lin(0.58, 0.55, 0.52),
  gravel: lin(0.63, 0.61, 0.57),
  concrete: lin(0.66, 0.66, 0.64),
};

export class Terrain {
  constructor(scene, size = 64, cell = 0.25) {
    this.size = size;
    this.cell = cell;
    this.N = Math.round(size / cell);
    this.V = this.N + 1;
    this.half = size / 2;
    this.area = cell * cell;
    const n = this.V * this.V;
    this.h = new Float32Array(n); // aktuelle Höhe
    this.h0 = new Float32Array(n); // ursprüngliche Geländeoberkante
    this.fill = new Float32Array(n); // Dicke losen Materials obenauf
    this.fillMat = new Uint8Array(n); // Art des losen Materials
    this.surf = new Uint8Array(n); // Oberflächenart des gewachsenen Bodens (0 Gras, 3 Schotter)
    this.wear = new Float32Array(n); // Abnutzung der Grasnarbe 0..1
    this.zone = new Uint8Array(n); // 1 = Deponie
    this.tanLoose = Math.tan((34 * Math.PI) / 180); // Schüttwinkel loser Boden
    this.tanNatural = Math.tan((72 * Math.PI) / 180); // gewachsener, bindiger Boden steht steil
    this.dirty = null;
    this.relaxBox = null;
    this.scene = scene;
    this.disposed = { 1: 0, 2: 0, 3: 0, 4: 0 };
  }

  idx(i, j) {
    return j * this.V + i;
  }
  gx(x) {
    return (x + this.half) / this.cell;
  }
  wx(i) {
    return i * this.cell - this.half;
  }

  generate(fn) {
    const { V } = this;
    for (let j = 0; j < V; j++) {
      for (let i = 0; i < V; i++) {
        const k = j * V + i;
        const r = fn(this.wx(i), this.wx(j));
        this.h[k] = this.h0[k] = r.h;
        this.surf[k] = r.surf || 0;
        this.fill[k] = r.fill || 0;
        this.fillMat[k] = r.fillMat || 0;
        this.zone[k] = r.zone || 0;
        // Bei Anfangshaufen: Oberkante des gewachsenen Bodens liegt unter dem Haufen
        if (r.fill) this.h0[k] = r.h - r.fill;
      }
    }
    this.buildMesh();
  }

  buildMesh() {
    const { V, N, cell, half } = this;
    const n = V * V;
    const pos = new Float32Array(n * 3);
    const nor = new Float32Array(n * 3);
    const colr = new Float32Array(n * 3);
    const uv = new Float32Array(n * 2);
    for (let j = 0; j < V; j++) {
      for (let i = 0; i < V; i++) {
        const k = j * V + i;
        pos[k * 3] = i * cell - half;
        pos[k * 3 + 2] = j * cell - half;
        uv[k * 2] = (i * cell) / 3;
        uv[k * 2 + 1] = (j * cell) / 3;
      }
    }
    const index = new Uint32Array(N * N * 6);
    let p = 0;
    for (let j = 0; j < N; j++) {
      for (let i = 0; i < N; i++) {
        const a = j * V + i, b = a + 1, c = a + V, d = c + 1;
        if ((i + j) & 1) {
          index[p++] = a; index[p++] = c; index[p++] = b;
          index[p++] = b; index[p++] = c; index[p++] = d;
        } else {
          index[p++] = a; index[p++] = c; index[p++] = d;
          index[p++] = a; index[p++] = d; index[p++] = b;
        }
      }
    }
    const g = new THREE.BufferGeometry();
    this.aPos = new THREE.BufferAttribute(pos, 3).setUsage(THREE.DynamicDrawUsage);
    this.aNor = new THREE.BufferAttribute(nor, 3).setUsage(THREE.DynamicDrawUsage);
    this.aCol = new THREE.BufferAttribute(colr, 3).setUsage(THREE.DynamicDrawUsage);
    g.setAttribute('position', this.aPos);
    g.setAttribute('normal', this.aNor);
    g.setAttribute('color', this.aCol);
    g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    g.setIndex(new THREE.BufferAttribute(index, 1));
    this.geo = g;
    const tex = noiseTexture(256, { base: 215, amp: 70, scale: 1, seed: 3 });
    tex.encoding = THREE.LinearEncoding;
    this.material = new THREE.MeshLambertMaterial({ vertexColors: true, map: tex });
    this.mesh = new THREE.Mesh(g, this.material);
    this.mesh.receiveShadow = true;
    this.mesh.castShadow = false;
    this.mesh.frustumCulled = false;
    this.scene.add(this.mesh);
    this.updateRegion(0, 0, N, N, true);
    g.computeBoundingSphere();
  }

  // ---------- Abfragen ----------
  heightGrid(i, j) {
    i = clamp(i, 0, this.N);
    j = clamp(j, 0, this.N);
    return this.h[j * this.V + i];
  }
  height(x, z) {
    const fx = clamp(this.gx(x), 0, this.N - 0.0001);
    const fz = clamp(this.gx(z), 0, this.N - 0.0001);
    const i = Math.floor(fx), j = Math.floor(fz);
    const u = fx - i, v = fz - j;
    const V = this.V, h = this.h;
    const k = j * V + i;
    return lerp(lerp(h[k], h[k + 1], u), lerp(h[k + V], h[k + V + 1], u), v);
  }
  normal(x, z, out) {
    const e = this.cell;
    const hx = this.height(x - e, z) - this.height(x + e, z);
    const hz = this.height(x, z - e) - this.height(x, z + e);
    return out.set(hx, 2 * e, hz).normalize();
  }
  inside(x, z, m = 0) {
    return Math.abs(x) < this.half - m && Math.abs(z) < this.half - m;
  }
  // Volumen über Referenzhöhe im Rechteck (für Prüfungen)
  statsRect(cx, cz, hw, hd, yaw = 0) {
    const c = Math.cos(yaw), s = Math.sin(yaw);
    const r = Math.sqrt(hw * hw + hd * hd);
    const i0 = Math.max(1, Math.floor(this.gx(cx - r))), i1 = Math.min(this.N - 1, Math.ceil(this.gx(cx + r)));
    const j0 = Math.max(1, Math.floor(this.gx(cz - r))), j1 = Math.min(this.N - 1, Math.ceil(this.gx(cz + r)));
    let sum = 0, cnt = 0, mn = Infinity, mx = -Infinity;
    for (let j = j0; j <= j1; j++) {
      for (let i = i0; i <= i1; i++) {
        const dx = this.wx(i) - cx, dz = this.wx(j) - cz;
        const lx = dx * c - dz * s, lz = dx * s + dz * c;
        if (Math.abs(lx) > hw || Math.abs(lz) > hd) continue;
        const hh = this.h[j * this.V + i];
        sum += hh; cnt++;
        if (hh < mn) mn = hh;
        if (hh > mx) mx = hh;
      }
    }
    return { mean: cnt ? sum / cnt : 0, min: mn, max: mx, count: cnt };
  }

  // ---------- Verformung ----------
  _range(cx, cz, r) {
    const m = 2;
    return [
      Math.max(m, Math.floor(this.gx(cx - r))),
      Math.min(this.N - m, Math.ceil(this.gx(cx + r))),
      Math.max(m, Math.floor(this.gx(cz - r))),
      Math.min(this.N - m, Math.ceil(this.gx(cz + r))),
    ];
  }

  // Schneidet Boden entlang einer Strecke (Schneidkante) bis auf Höhe y ab.
  // Gibt entnommenes Volumen und überwiegendes Material zurück.
  cutSegment(ax, az, bx, bz, y, halfThick, maxVol) {
    const out = { vol: 0, mat: MAT.SOIL };
    if (maxVol <= 0) return out;
    const r = Math.max(Math.abs(bx - ax), Math.abs(bz - az)) * 0.5 + halfThick + this.cell;
    const cx = (ax + bx) / 2, cz = (az + bz) / 2;
    const [i0, i1, j0, j1] = this._range(cx, cz, r);
    const dx = bx - ax, dz = bz - az;
    const l2 = dx * dx + dz * dz || 1e-6;
    const list = [];
    let avail = 0;
    for (let j = j0; j <= j1; j++) {
      for (let i = i0; i <= i1; i++) {
        const px = this.wx(i), pz = this.wx(j);
        let t = ((px - ax) * dx + (pz - az) * dz) / l2;
        t = clamp(t, 0, 1);
        const qx = ax + dx * t - px, qz = az + dz * t - pz;
        if (qx * qx + qz * qz > halfThick * halfThick) continue;
        const k = j * this.V + i;
        const d = this.h[k] - y;
        if (d > 0.001) {
          list.push(k, d);
          avail += d;
        }
      }
    }
    if (!list.length) return out;
    const f = Math.min(1, maxVol / (avail * this.area));
    const mv = { 1: 0, 2: 0, 3: 0, 4: 0 };
    for (let q = 0; q < list.length; q += 2) {
      const k = list[q], d = list[q + 1] * f;
      this._remove(k, d, mv);
    }
    let best = 1, bv = -1;
    for (const m in mv) if (mv[m] > bv) { bv = mv[m]; best = +m; }
    out.vol = avail * f * this.area;
    out.mat = best;
    this.markDirty(i0, j0, i1, j1);
    return out;
  }

  _remove(k, d, mv) {
    const fl = this.fill[k];
    const fromFill = Math.min(fl, d);
    if (fromFill > 0) {
      mv[this.fillMat[k] || 1] += fromFill;
      this.fill[k] = fl - fromFill;
    }
    const nat = d - fromFill;
    if (nat > 0) mv[this.surf[k] === MAT.GRAVEL && this.h0[k] - (this.h[k] - fl) < 0.3 ? MAT.GRAVEL : MAT.SOIL] += nat;
    this.h[k] -= d;
    this.wear[k] = 1;
  }

  // Material ablegen, verteilt als flacher Hügel (Gaußglocke)
  deposit(x, z, vol, mat = MAT.SOIL, radius = 0.5) {
    if (vol <= 0) return;
    const [i0, i1, j0, j1] = this._range(x, z, radius * 1.6);
    const ks = [];
    let wsum = 0;
    const s2 = radius * radius;
    for (let j = j0; j <= j1; j++) {
      for (let i = i0; i <= i1; i++) {
        const dx = this.wx(i) - x, dz = this.wx(j) - z;
        const d2 = dx * dx + dz * dz;
        if (d2 > s2 * 2.56) continue;
        const w = Math.exp(-d2 / s2);
        ks.push(j * this.V + i, w);
        wsum += w;
      }
    }
    if (!wsum) return;
    const dhTot = vol / this.area;
    for (let q = 0; q < ks.length; q += 2) {
      const k = ks[q];
      const dh = (dhTot * ks[q + 1]) / wsum;
      this._add(k, dh, mat);
    }
    this.markDirty(i0, j0, i1, j1);
    this.markRelax(i0, j0, i1, j1);
  }

  _add(k, dh, mat) {
    if (this.fill[k] < dh * 0.8 || !this.fillMat[k]) this.fillMat[k] = mat;
    this.fill[k] += dh;
    this.h[k] += dh;
  }

  // Grasnarbe abnutzen (Fahrspuren)
  addWear(x, z, r, amount) {
    const [i0, i1, j0, j1] = this._range(x, z, r);
    let changed = false;
    for (let j = j0; j <= j1; j++) {
      for (let i = i0; i <= i1; i++) {
        const dx = this.wx(i) - x, dz = this.wx(j) - z;
        if (dx * dx + dz * dz > r * r) continue;
        const k = j * this.V + i;
        if (this.wear[k] < 1) {
          this.wear[k] = Math.min(1, this.wear[k] + amount);
          changed = true;
        }
      }
    }
    if (changed) this.markDirty(i0, j0, i1, j1);
  }

  // Gelände in einem Rechteck exakt auf Höhe y bringen (Feinplanum durch Bautrupp)
  levelRect(cx, cz, hw, hd, yaw, y, surf = MAT.GRAVEL) {
    const c = Math.cos(yaw), s = Math.sin(yaw);
    const r = Math.sqrt(hw * hw + hd * hd);
    const [i0, i1, j0, j1] = this._range(cx, cz, r);
    let removed = 0;
    for (let j = j0; j <= j1; j++) {
      for (let i = i0; i <= i1; i++) {
        const dx = this.wx(i) - cx, dz = this.wx(j) - cz;
        const lx = dx * c - dz * s, lz = dx * s + dz * c;
        if (Math.abs(lx) > hw || Math.abs(lz) > hd) continue;
        const k = j * this.V + i;
        removed += this.h[k] - y;
        this.h[k] = y;
        this.h0[k] = y;
        this.fill[k] = 0;
        this.surf[k] = surf;
        this.wear[k] = 0;
      }
    }
    this.markDirty(i0, j0, i1, j1);
    this.markRelax(i0 - 2, j0 - 2, i1 + 2, j1 + 2);
    return removed * this.area;
  }

  markDirty(i0, j0, i1, j1) {
    if (!this.dirty) this.dirty = [i0, j0, i1, j1];
    else {
      const d = this.dirty;
      d[0] = Math.min(d[0], i0); d[1] = Math.min(d[1], j0);
      d[2] = Math.max(d[2], i1); d[3] = Math.max(d[3], j1);
    }
  }
  markRelax(i0, j0, i1, j1) {
    i0 = Math.max(1, i0 - 1); j0 = Math.max(1, j0 - 1);
    i1 = Math.min(this.N - 1, i1 + 1); j1 = Math.min(this.N - 1, j1 + 1);
    if (!this.relaxBox) this.relaxBox = [i0, j0, i1, j1];
    else {
      const d = this.relaxBox;
      d[0] = Math.min(d[0], i0); d[1] = Math.min(d[1], j0);
      d[2] = Math.max(d[2], i1); d[3] = Math.max(d[3], j1);
    }
  }

  // Böschungsrutschung: loses Material rutscht, bis der Schüttwinkel erreicht ist
  relax(iterations = 2) {
    if (!this.relaxBox) return;
    const { V, h, fill, cell } = this;
    const diagD = cell * Math.SQRT2;
    const limL = this.tanLoose * cell, limLd = this.tanLoose * diagD;
    const limN = this.tanNatural * cell, limNd = this.tanNatural * diagD;
    const NB = [1, 0, -1, 0, 0, 1, 0, -1, 1, 1, -1, -1, 1, -1, -1, 1];
    let box = this.relaxBox;
    for (let it = 0; it < iterations && box; it++) {
      const [i0, j0, i1, j1] = box;
      let ni0 = 1e9, nj0 = 1e9, ni1 = -1, nj1 = -1;
      const odd = it & 1;
      for (let jj = j0; jj <= j1; jj++) {
        const j = odd ? j1 - (jj - j0) : jj;
        for (let ii = i0; ii <= i1; ii++) {
          const i = odd ? i1 - (ii - i0) : ii;
          const k = j * V + i;
          for (let q = 0; q < 16; q += 2) {
            const ni = i + NB[q], nj = j + NB[q + 1];
            if (ni < 1 || nj < 1 || ni >= this.N || nj >= this.N) continue;
            const n = nj * V + ni;
            const diff = h[k] - h[n];
            const diag = q >= 8;
            const lim = diag ? limLd : limL;
            if (diff <= lim) continue;
            let amt = (diff - lim) * 0.22;
            // loses Material rutscht; gewachsener Boden nur bei Überhang
            const limNat = diag ? limNd : limN;
            let movable = fill[k];
            if (diff > limNat) movable += (diff - limNat) * 0.3;
            if (amt > movable) amt = movable;
            if (amt < 0.0005) continue;
            const mt = this.fillMat[k] || 1;
            fill[k] = Math.max(0, fill[k] - amt);
            h[k] -= amt;
            if (this.zone[n] === 1) {
              // Deponie: Material wird abgefahren
              this.disposed[mt] = (this.disposed[mt] || 0) + amt * this.area;
            } else {
              this._add(n, amt, mt);
            }
            if (i < ni0) ni0 = i; if (i > ni1) ni1 = i;
            if (j < nj0) nj0 = j; if (j > nj1) nj1 = j;
          }
        }
      }
      if (ni1 < 0) box = null;
      else {
        box = [Math.max(1, ni0 - 2), Math.max(1, nj0 - 2), Math.min(this.N - 1, ni1 + 2), Math.min(this.N - 1, nj1 + 2)];
        this.markDirty(box[0], box[1], box[2], box[3]);
      }
    }
    this.relaxBox = box;
  }

  // Deponie: loses Material in der Zone wird abtransportiert
  processZones(dt) {
    if (!this.zoneBox) return;
    const [i0, j0, i1, j1] = this.zoneBox;
    let any = false;
    const rate = Math.min(1, dt * 0.25);
    for (let j = j0; j <= j1; j++) {
      for (let i = i0; i <= i1; i++) {
        const k = j * this.V + i;
        if (this.zone[k] !== 1 || this.fill[k] < 0.002) continue;
        const d = this.fill[k] * rate + 0.0005;
        const take = Math.min(this.fill[k], d);
        const mt = this.fillMat[k] || 1;
        this.disposed[mt] += take * this.area;
        this.fill[k] -= take;
        this.h[k] -= take;
        any = true;
      }
    }
    if (any) this.markDirty(i0, j0, i1, j1);
  }

  // ---------- Darstellung ----------
  _color(k, out, o) {
    const fl = this.fill[k];
    const natTop = this.h[k] - fl;
    const depth = this.h0[k] - natTop;
    let r, g, b;
    // Gewachsener Boden: Farbe nach Tiefe (Bodenhorizonte)
    if (depth > 0.04) {
      let c1, c2, t;
      if (depth < 0.35) { c1 = C.topsoil; c2 = C.topsoil; t = 0; }
      else if (depth < 0.6) { c1 = C.topsoil; c2 = C.clay; t = (depth - 0.35) / 0.25; }
      else if (depth < 2.3) { c1 = C.clay; c2 = C.clay; t = 0; }
      else if (depth < 2.7) { c1 = C.clay; c2 = C.sand; t = (depth - 2.3) / 0.4; }
      else if (depth < 5.5) { c1 = C.sand; c2 = C.sand; t = 0; }
      else { c1 = C.sand; c2 = C.deep; t = Math.min(1, (depth - 5.5) / 1.0); }
      r = lerp(c1[0], c2[0], t); g = lerp(c1[1], c2[1], t); b = lerp(c1[2], c2[2], t);
      const surf = this.surf[k];
      if (surf === MAT.GRAVEL && depth < 0.3) { r = C.gravel[0] * 0.9; g = C.gravel[1] * 0.9; b = C.gravel[2] * 0.9; }
    } else {
      const surf = this.surf[k];
      if (surf === MAT.GRAVEL) { r = C.gravel[0]; g = C.gravel[1]; b = C.gravel[2]; }
      else if (surf === MAT.CONCRETE) { r = C.concrete[0]; g = C.concrete[1]; b = C.concrete[2]; }
      else {
        const x = (k % this.V) * 0.11, z = Math.floor(k / this.V) * 0.11;
        const nv = noise2(x * 0.35, z * 0.35) * 0.5 + 0.5;
        const dry = Math.max(0, noise2(x * 0.08 + 9, z * 0.08)) * 0.8;
        r = lerp(C.grass1[0], C.grass2[0], nv); g = lerp(C.grass1[1], C.grass2[1], nv); b = lerp(C.grass1[2], C.grass2[2], nv);
        r = lerp(r, C.grassDry[0], dry); g = lerp(g, C.grassDry[1], dry); b = lerp(b, C.grassDry[2], dry);
        const w = this.wear[k];
        if (w > 0) {
          r = lerp(r, C.mud[0], w); g = lerp(g, C.mud[1], w); b = lerp(b, C.mud[2], w);
        }
      }
      if (depth > 0.0) {
        const t = depth / 0.04;
        r = lerp(r, C.topsoil[0], t); g = lerp(g, C.topsoil[1], t); b = lerp(b, C.topsoil[2], t);
      }
    }
    // Loses Material obenauf
    if (fl > 0.002) {
      const m = this.fillMat[k];
      const c = m === MAT.RUBBLE ? C.rubble : m === MAT.GRAVEL ? C.gravel : m === MAT.CONCRETE ? C.concrete : C.soil;
      const t = Math.min(1, fl / 0.05);
      r = lerp(r, c[0], t); g = lerp(g, c[1], t); b = lerp(b, c[2], t);
    }
    out[o] = r; out[o + 1] = g; out[o + 2] = b;
  }

  updateRegion(i0, j0, i1, j1) {
    const { V, h, cell } = this;
    i0 = clamp(i0 - 1, 0, this.N); j0 = clamp(j0 - 1, 0, this.N);
    i1 = clamp(i1 + 1, 0, this.N); j1 = clamp(j1 + 1, 0, this.N);
    const pos = this.aPos.array, nor = this.aNor.array, colr = this.aCol.array;
    for (let j = j0; j <= j1; j++) {
      for (let i = i0; i <= i1; i++) {
        const k = j * V + i;
        pos[k * 3 + 1] = h[k];
        const hl = h[j * V + Math.max(0, i - 1)], hr = h[j * V + Math.min(this.N, i + 1)];
        const hd = h[Math.max(0, j - 1) * V + i], hu = h[Math.min(this.N, j + 1) * V + i];
        let nx = hl - hr, ny = 2 * cell, nz = hd - hu;
        const l = Math.sqrt(nx * nx + ny * ny + nz * nz);
        nor[k * 3] = nx / l; nor[k * 3 + 1] = ny / l; nor[k * 3 + 2] = nz / l;
        this._color(k, colr, k * 3);
      }
    }
    const off = j0 * V, cnt = (j1 - j0 + 1) * V;
    for (const a of [this.aPos, this.aNor, this.aCol]) {
      a.updateRange.offset = off * 3;
      a.updateRange.count = cnt * 3;
      a.needsUpdate = true;
    }
  }

  flush() {
    if (this.dirty) {
      const d = this.dirty;
      this.dirty = null;
      this.updateRegion(d[0], d[1], d[2], d[3]);
    }
  }

  setZoneRect(x0, z0, x1, z1) {
    const i0 = Math.floor(this.gx(x0)), i1 = Math.ceil(this.gx(x1));
    const j0 = Math.floor(this.gx(z0)), j1 = Math.ceil(this.gx(z1));
    for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) this.zone[j * this.V + i] = 1;
    this.zoneBox = [i0, j0, i1, j1];
  }

  // Speichern / Laden (Differenz zur Ausgangslage)
  serialize() {
    const toB64 = (arr) => {
      const u8 = new Uint8Array(arr.buffer);
      let s = '';
      for (let i = 0; i < u8.length; i += 8192) s += String.fromCharCode.apply(null, u8.subarray(i, i + 8192));
      return btoa(s);
    };
    const n = this.V * this.V;
    const dh = new Int16Array(n), fl = new Uint16Array(n), wr = new Uint8Array(n);
    for (let k = 0; k < n; k++) {
      dh[k] = Math.round((this.h[k] - this.h0[k]) * 1000);
      fl[k] = Math.round(this.fill[k] * 1000);
      wr[k] = Math.round(this.wear[k] * 255);
    }
    return { dh: toB64(dh), fl: toB64(fl), fm: toB64(this.fillMat), wr: toB64(wr), h0: toB64(new Int16Array(Array.from(this.h0, (v) => Math.round(v * 1000)))), sf: toB64(this.surf), disp: this.disposed };
  }
  deserialize(d) {
    const fromB64 = (s, T) => {
      const bin = atob(s);
      const u8 = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) u8[i] = bin.charCodeAt(i);
      return new T(u8.buffer);
    };
    const n = this.V * this.V;
    const h0 = fromB64(d.h0, Int16Array), dh = fromB64(d.dh, Int16Array), fl = fromB64(d.fl, Uint16Array);
    const fm = fromB64(d.fm, Uint8Array), wr = fromB64(d.wr, Uint8Array), sf = fromB64(d.sf, Uint8Array);
    if (h0.length !== n) return false;
    for (let k = 0; k < n; k++) {
      this.h0[k] = h0[k] / 1000;
      this.h[k] = this.h0[k] + dh[k] / 1000;
      this.fill[k] = fl[k] / 1000;
      this.fillMat[k] = fm[k];
      this.wear[k] = wr[k] / 255;
      this.surf[k] = sf[k];
    }
    if (d.disp) this.disposed = d.disp;
    this.updateRegion(0, 0, this.N, this.N);
    return true;
  }
}
