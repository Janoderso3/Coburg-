// Gebäude aus Bauteilen mit Statik (Auflager / Spannweite), Trümmerphysik, Fertigteile und Baustellen
import { col, clamp, rng, noiseTexture } from './util.js';
import { MAT } from './terrain.js';
import { boxesGeometry, textTexture } from './models.js';

// Bauteilarten: Festigkeit (hp), max. Spannweite in Schritten ohne Auflager, Dichte t/m³
export const PT = {
  brick: { name: 'Mauerwerk', hp: 45, span: 4, dens: 1.8, color: 0x9b4c36 },
  plaster: { name: 'Mauerwerk', hp: 45, span: 4, dens: 1.7, color: 0xe3d6bd },
  prefab: { name: 'Fertigteil', hp: 70, span: 6, dens: 1.3, color: 0xe4dfd4 },
  concrete: { name: 'Beton', hp: 170, span: 6, dens: 2.4, color: 0xa3a29d },
  roof: { name: 'Dach', hp: 10, span: 7, dens: 0.6, color: 0x9a3f2c },
  wood: { name: 'Holz', hp: 16, span: 5, dens: 0.6, color: 0x6f4a2c },
  glass: { name: 'Glas', hp: 1, span: 2, dens: 2.5, color: 0xb9d6e4, glass: true, noSupport: true },
  door: { name: 'Tür', hp: 8, span: 2, dens: 0.6, color: 0x5b3b25, noSupport: true },
  steel: { name: 'Stahl', hp: 60, span: 3, dens: 1.0, color: 0x6f7a80, noSupport: true },
};

const _q = new THREE.Quaternion();
const _qi = new THREE.Quaternion();
const _e = new THREE.Euler();
const _v = new THREE.Vector3();
const _v2 = new THREE.Vector3();
const _s = new THREE.Vector3();
const _m = new THREE.Matrix4();
const ZERO = new THREE.Matrix4().makeScale(0, 0, 0);

class InstPool {
  constructor(scene, mat, cap, cast = true) {
    this.cap = cap;
    this.mesh = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1, 1), mat, cap);
    this.mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    this.mesh.castShadow = cast;
    this.mesh.receiveShadow = true;
    this.mesh.frustumCulled = false;
    const c = new THREE.Color(1, 1, 1);
    for (let i = 0; i < cap; i++) {
      this.mesh.setMatrixAt(i, ZERO);
      this.mesh.setColorAt(i, c);
    }
    this.mesh.count = 0;
    this.free = [];
    this.top = 0;
    scene.add(this.mesh);
    this.dirty = true;
  }
  alloc() {
    let i;
    if (this.free.length) i = this.free.pop();
    else if (this.top < this.cap) i = this.top++;
    else return -1;
    if (i + 1 > this.mesh.count) this.mesh.count = i + 1;
    return i;
  }
  release(i) {
    if (i < 0) return;
    this.mesh.setMatrixAt(i, ZERO);
    this.free.push(i);
    this.dirty = true;
  }
  set(i, pos, q, sx, sy, sz) {
    _s.set(sx, sy, sz);
    _m.compose(pos, q, _s);
    this.mesh.setMatrixAt(i, _m);
    this.dirty = true;
  }
  color(i, c) {
    this.mesh.setColorAt(i, c);
    this.cdirty = true;
  }
  flush() {
    if (this.dirty) {
      this.mesh.instanceMatrix.needsUpdate = true;
      this.dirty = false;
    }
    if (this.cdirty && this.mesh.instanceColor) {
      this.mesh.instanceColor.needsUpdate = true;
      this.cdirty = false;
    }
  }
}

// ---------------- Baupläne ----------------
export class Blueprint {
  constructor(seed = 1) {
    this.boxes = [];
    this.r = rng(seed);
  }
  add(type, x, y, z, sx, sy, sz, yaw = 0, pitch = 0, tint = 1) {
    const base = new THREE.Color(PT[type].color);
    const v = 0.9 + this.r() * 0.16;
    const c = base.multiplyScalar(v * tint);
    this.boxes.push({ type, x, y, z, sx, sy, sz, yaw, pitch, color: c });
  }
  // Mauer zwischen zwei Punkten (lokal), mit Öffnungen und optionalem Giebel
  // openings: [{s0,s1,y0,y1,kind:'window'|'door'|'gate'}], y relativ zu y0 der Mauer
  wall(o) {
    const { x0, z0, x1, z1, y0, h, t = 0.3, type = 'plaster', openings = [], gable = null, block = 0.5, course = 0.5, fachwerk = false, bond = true } = o;
    const dx = x1 - x0, dz = z1 - z0;
    const L = Math.sqrt(dx * dx + dz * dz);
    const ux = dx / L, uz = dz / L;
    const yaw = Math.atan2(-dz, dx);
    const n = Math.max(1, Math.round(L / block));
    const bl = L / n;
    const hTot = gable ? h + gable.span * gable.slope : h;
    const m = Math.max(1, Math.round(h / course));
    const bh = h / m;
    const courses = gable ? Math.ceil(hTot / bh) : m;
    const inOpen = (s, y) => openings.some((op) => s > op.s0 && s < op.s1 && y > op.y0 && y < op.y1);
    for (let c = 0; c < courses; c++) {
      const yc = (c + 0.5) * bh;
      const off = bond && c % 2 ? bl / 2 : 0;
      const segs = [];
      if (off) {
        segs.push([0, off]);
        for (let i = 0; i < n - 1; i++) segs.push([off + i * bl, off + (i + 1) * bl]);
        segs.push([L - off, L]);
      } else for (let i = 0; i < n; i++) segs.push([i * bl, (i + 1) * bl]);
      // Steine an Öffnungskanten passend schneiden
      let cuts = segs;
      for (const op of openings) {
        if (!(yc > op.y0 && yc < op.y1)) continue;
        const out = [];
        for (const [a, b] of cuts) {
          const pts = [a];
          if (op.s0 > a + 0.02 && op.s0 < b - 0.02) pts.push(op.s0);
          if (op.s1 > a + 0.02 && op.s1 < b - 0.02) pts.push(op.s1);
          pts.push(b);
          for (let i = 0; i < pts.length - 1; i++) out.push([pts[i], pts[i + 1]]);
        }
        cuts = out;
      }
      const merged = [];
      for (const sg of cuts) {
        const prev = merged[merged.length - 1];
        const mid = (sg[0] + sg[1]) / 2;
        if (prev && sg[1] - sg[0] < 0.14 && !inOpen(mid, yc) && !inOpen((prev[0] + prev[1]) / 2, yc) && Math.abs(prev[1] - sg[0]) < 1e-6) prev[1] = sg[1];
        else merged.push([sg[0], sg[1]]);
      }
      for (const [a, b] of merged) {
        const s = (a + b) / 2;
        if (inOpen(s, yc)) continue;
        let top = (c + 1) * bh;
        if (gable) {
          const allowed = h + (gable.span - Math.abs(s - gable.sPeak)) * gable.slope;
          if (yc > allowed) continue;
          if (top > allowed + bh * 0.5) top = allowed + bh * 0.5;
        }
        const yy = c * bh;
        const sh = top - yy;
        let ty = type;
        if (fachwerk) {
          const sm = s % 1.5;
          if (c === 0 || c === m - 1 || sm < 0.26 || sm > 1.24 || (c === Math.floor(m / 2) && sm > 0.3 && sm < 1.2)) ty = 'wood';
        }
        this.add(ty, x0 + ux * s, y0 + yy + sh / 2, z0 + uz * s, b - a, sh, t, yaw);
      }
    }
    for (const op of openings) {
      const s = (op.s0 + op.s1) / 2;
      const w = op.s1 - op.s0, hh = op.y1 - op.y0;
      const yc = y0 + (op.y0 + op.y1) / 2;
      if (op.kind === 'window') this.add('glass', x0 + ux * s, yc, z0 + uz * s, w * 0.96, hh * 0.96, 0.05, yaw);
      else if (op.kind === 'door') this.add('door', x0 + ux * s, yc, z0 + uz * s, w * 0.95, hh * 0.97, 0.07, yaw);
      else if (op.kind === 'gate') this.add('steel', x0 + ux * s, yc, z0 + uz * s, w * 0.97, hh * 0.97, 0.06, yaw);
    }
  }
  slab(o) {
    const { x0, z0, x1, z1, y0, t = 0.25, type = 'concrete', tile = 1 } = o;
    const nx = Math.max(1, Math.round((x1 - x0) / tile)), nz = Math.max(1, Math.round((z1 - z0) / tile));
    const sx = (x1 - x0) / nx, sz = (z1 - z0) / nz;
    for (let i = 0; i < nx; i++) for (let j = 0; j < nz; j++) this.add(type, x0 + (i + 0.5) * sx, y0 + t / 2, z0 + (j + 0.5) * sz, sx, t, sz);
  }
  // Satteldach, First entlang X bei z = zc; side: -1, 1 oder 0 (beide)
  gableRoof(o) {
    const { x0, x1, z0, z1, eaveY, pitch, over = 0.4, tile = 1.0, t = 0.14, type = 'roof', side = 0 } = o;
    const zc = (z0 + z1) / 2, hd = (z1 - z0) / 2;
    const tp = Math.tan(pitch);
    const ridgeY = eaveY + hd * tp;
    const Ls = (hd + over) / Math.cos(pitch);
    const m = Math.max(1, Math.round(Ls / tile));
    const tl = Ls / m;
    const nx = Math.max(1, Math.round((x1 - x0 + 2 * over) / tile));
    const tw = (x1 - x0 + 2 * over) / nx;
    for (const sd of [-1, 1]) {
      if (side && sd !== side) continue;
      for (let j = 0; j < m; j++) {
        const d = (j + 0.5) * tl; // Abstand vom First entlang Dachfläche
        const z = zc + sd * d * Math.cos(pitch);
        const y = ridgeY - d * Math.sin(pitch) + t / 2 / Math.cos(pitch) + 0.05;
        for (let i = 0; i < nx; i++) {
          const x = x0 - over + (i + 0.5) * tw;
          this.add(type, x, y, z, tw * 1.0, t, tl * 1.02, 0, sd * pitch, j === 0 ? 0.85 : 1);
        }
      }
    }
    return ridgeY;
  }
  column(type, x, z, y0, y1, w = 0.5) {
    const n = Math.max(1, Math.round((y1 - y0) / 0.5));
    const h = (y1 - y0) / n;
    for (let i = 0; i < n; i++) this.add(type, x, y0 + (i + 0.5) * h, z, w, h, w);
  }
}

// Altbau: zweigeschossiges Fachwerkhaus mit Satteldach (Abrissobjekt)
export function oldHouseBlueprint(seed = 3) {
  const bp = new Blueprint(seed);
  const W = 9, D = 7.5, t = 0.36;
  const hx = W / 2, hz = D / 2;
  bp.slab({ x0: -hx - 0.1, z0: -hz - 0.1, x1: hx + 0.1, z1: hz + 0.1, y0: 0, t: 0.25, type: 'concrete', tile: 1.15 });
  const g0 = 0.25, fh = 2.6;
  const win = (s, y0 = 1.0, y1 = 2.2, w = 1.0) => ({ s0: s, s1: s + w, y0, y1, kind: 'window' });
  // Erdgeschoss (verputzt)
  bp.wall({ x0: -hx, z0: hz - t / 2, x1: hx, z1: hz - t / 2, y0: g0, h: fh, t, type: 'plaster', openings: [win(0.9), win(2.6), { s0: 4.0, s1: 5.0, y0: 0, y1: 2.15, kind: 'door' }, win(6.4), win(7.9, 1.0, 2.2, 0.6)] });
  bp.wall({ x0: -hx, z0: -hz + t / 2, x1: hx, z1: -hz + t / 2, y0: g0, h: fh, t, type: 'plaster', openings: [win(1.4), win(4.0), win(6.6)] });
  bp.wall({ x0: hx - t / 2, z0: -hz + t, x1: hx - t / 2, z1: hz - t, y0: g0, h: fh, t, type: 'plaster', openings: [win(2.9)] });
  bp.wall({ x0: -hx + t / 2, z0: -hz + t, x1: -hx + t / 2, z1: hz - t, y0: g0, h: fh, t, type: 'plaster', openings: [win(1.5), win(4.3, 1.0, 2.2, 0.8)] });
  // Holzbalkendecke
  const g1 = g0 + fh;
  bp.slab({ x0: -hx, z0: -hz, x1: hx, z1: hz, y0: g1, t: 0.25, type: 'wood', tile: 1.15 });
  // Obergeschoss in Fachwerk
  const g2 = g1 + 0.25, fh2 = 2.5;
  bp.wall({ x0: -hx, z0: hz - t / 2, x1: hx, z1: hz - t / 2, y0: g2, h: fh2, t, type: 'plaster', fachwerk: true, bond: false, openings: [win(0.9, 0.8, 2.0), win(2.6, 0.8, 2.0), win(4.25, 0.8, 2.0, 0.5), win(6.4, 0.8, 2.0), win(7.9, 0.8, 2.0, 0.6)] });
  bp.wall({ x0: -hx, z0: -hz + t / 2, x1: hx, z1: -hz + t / 2, y0: g2, h: fh2, t, type: 'plaster', fachwerk: true, bond: false, openings: [win(1.4, 0.8, 2.0), win(4.0, 0.8, 2.0), win(6.6, 0.8, 2.0)] });
  const pitch = 0.78; // ca. 45°
  const span = hz - t; // Giebel
  bp.wall({ x0: hx - t / 2, z0: -hz + t, x1: hx - t / 2, z1: hz - t, y0: g2, h: fh2, t, type: 'plaster', fachwerk: true, bond: false, openings: [win(2.9, 0.8, 2.0), win(3.15, 3.0, 3.8, 0.5)], gable: { sPeak: (D - 2 * t) / 2, span: span + 0.25, slope: Math.tan(pitch) } });
  bp.wall({ x0: -hx + t / 2, z0: -hz + t, x1: -hx + t / 2, z1: hz - t, y0: g2, h: fh2, t, type: 'plaster', fachwerk: true, bond: false, openings: [win(1.5, 0.8, 2.0), win(4.3, 0.8, 2.0, 0.8)], gable: { sPeak: (D - 2 * t) / 2, span: span + 0.25, slope: Math.tan(pitch) } });
  const ridge = bp.gableRoof({ x0: -hx, x1: hx, z0: -hz, z1: hz, eaveY: g2 + fh2, pitch, over: 0.35, tile: 1.0 });
  bp.column('brick', 1.75, -0.75, g0, ridge + 0.8, 0.5);
  return bp;
}

// Alte Garage / Schuppen aus Ziegel mit Flachdach
export function oldGarageBlueprint(seed = 9) {
  const bp = new Blueprint(seed);
  const W = 3.6, D = 6.2, t = 0.3, h = 2.5;
  const hx = W / 2, hz = D / 2;
  bp.slab({ x0: -hx, z0: -hz, x1: hx, z1: hz, y0: 0, t: 0.2, type: 'concrete', tile: 1.2 });
  bp.wall({ x0: -hx, z0: -hz + t / 2, x1: hx, z1: -hz + t / 2, y0: 0.2, h, t, type: 'brick' });
  bp.wall({ x0: -hx + t / 2, z0: -hz + t, x1: -hx + t / 2, z1: hz, y0: 0.2, h, t, type: 'brick', openings: [{ s0: 2.0, s1: 3.0, y0: 1.2, y1: 1.9, kind: 'window' }] });
  bp.wall({ x0: hx - t / 2, z0: -hz + t, x1: hx - t / 2, z1: hz, y0: 0.2, h, t, type: 'brick' });
  bp.wall({ x0: -hx + t, z0: hz - t / 2, x1: hx - t, z1: hz - t / 2, y0: 0.2, h, t, type: 'brick', openings: [{ s0: 0.25, s1: 2.75, y0: 0, y1: 2.05, kind: 'gate' }] });
  bp.slab({ x0: -hx - 0.15, z0: -hz - 0.15, x1: hx + 0.15, z1: hz + 0.15, y0: 0.2 + h, t: 0.2, type: 'concrete', tile: 1.2 });
  return bp;
}

// ---------------- Fertighaus-Entwürfe ----------------
// Liefert Fertigteile in Baustellen-Koordinaten (Ursprung = Mitte Oberkante Bodenplatte)
function makeElement(name, tier, bp) {
  let minx = Infinity, miny = Infinity, minz = Infinity, maxx = -Infinity, maxy = -Infinity, maxz = -Infinity, mass = 0;
  for (const b of bp.boxes) {
    const r = Math.max(b.sx, b.sz) * 0.5;
    minx = Math.min(minx, b.x - r); maxx = Math.max(maxx, b.x + r);
    minz = Math.min(minz, b.z - r); maxz = Math.max(maxz, b.z + r);
    const ry = b.pitch ? Math.abs(Math.sin(b.pitch)) * b.sz * 0.5 + b.sy * 0.5 : b.sy / 2;
    miny = Math.min(miny, b.y - ry); maxy = Math.max(maxy, b.y + ry);
    mass += b.sx * b.sy * b.sz * PT[b.type].dens;
  }
  // genauere horizontale Ausdehnung
  minx = Infinity; maxx = -Infinity; minz = Infinity; maxz = -Infinity;
  for (const b of bp.boxes) {
    const c = Math.abs(Math.cos(b.yaw)), s = Math.abs(Math.sin(b.yaw));
    const ex = (b.sx * c + b.sz * s) / 2, ez = (b.sx * s + b.sz * c) / 2 * (b.pitch ? Math.cos(b.pitch) : 1);
    minx = Math.min(minx, b.x - ex); maxx = Math.max(maxx, b.x + ex);
    minz = Math.min(minz, b.z - ez); maxz = Math.max(maxz, b.z + ez);
  }
  const ox = (minx + maxx) / 2, oz = (minz + maxz) / 2, oy = miny;
  const boxes = bp.boxes.map((b) => ({ ...b, x: b.x - ox, y: b.y - oy, z: b.z - oz }));
  return { name, tier, boxes, local: { x: ox, y: oy, z: oz }, size: { x: maxx - minx, y: maxy - miny, z: maxz - minz }, mass };
}

export const DESIGNS = {
  garage: {
    name: 'Fertiggarage',
    hw: 1.85, hd: 3.35, slabT: 0.2,
    build() {
      const W = 3.5, D = 6.5, t = 0.2, h = 2.6, hx = W / 2, hz = D / 2;
      const els = [];
      let bp = new Blueprint(21);
      bp.wall({ x0: -hx, z0: -hz + t / 2, x1: hx, z1: -hz + t / 2, y0: 0, h, t, type: 'prefab', bond: false });
      els.push(makeElement('Rückwand', 0, bp));
      bp = new Blueprint(22);
      bp.wall({ x0: -hx + t / 2, z0: -hz + t, x1: -hx + t / 2, z1: hz, y0: 0, h, t, type: 'prefab', bond: false, openings: [{ s0: 2.5, s1: 3.5, y0: 1.3, y1: 2.0, kind: 'window' }] });
      els.push(makeElement('Seitenwand links', 0, bp));
      bp = new Blueprint(23);
      bp.wall({ x0: hx - t / 2, z0: -hz + t, x1: hx - t / 2, z1: hz, y0: 0, h, t, type: 'prefab', bond: false });
      els.push(makeElement('Seitenwand rechts', 0, bp));
      bp = new Blueprint(24);
      bp.slab({ x0: -hx - 0.1, z0: -hz - 0.1, x1: hx + 0.1, z1: 0, y0: h, t: 0.18, type: 'concrete', tile: 1.1 });
      els.push(makeElement('Dachplatte hinten', 1, bp));
      bp = new Blueprint(25);
      bp.slab({ x0: -hx - 0.1, z0: 0, x1: hx + 0.1, z1: hz + 0.1, y0: h, t: 0.18, type: 'concrete', tile: 1.1 });
      els.push(makeElement('Dachplatte vorne', 1, bp));
      bp = new Blueprint(26);
      bp.add('steel', 0, 1.05, hz - 0.08, W - 2 * t - 0.04, 2.1, 0.06, 0);
      bp.add('steel', 0, 2.3, hz - 0.08, W - 2 * t - 0.04, 0.3, 0.12, 0, 0, 0.8);
      els.push(makeElement('Sektionaltor', 2, bp));
      return els;
    },
  },
  house: {
    name: 'Einfamilienhaus',
    hw: 4.15, hd: 4.15, slabT: 0.2,
    build() {
      const H = 4, t = 0.24, h = 2.75, pitch = 0.61; // ca. 35°
      const slope = Math.tan(pitch);
      const els = [];
      const win = (s0, s1, y0 = 0.95, y1 = 2.2) => ({ s0, s1, y0, y1, kind: 'window' });
      const zf = H - t / 2;
      let bp = new Blueprint(31);
      bp.wall({ x0: -H, z0: zf, x1: 0, z1: zf, y0: 0, h, t, type: 'prefab', bond: false, openings: [{ s0: 1.5, s1: 2.5, y0: 0, y1: 2.15, kind: 'door' }] });
      els.push(makeElement('Außenwand vorne links', 0, bp));
      bp = new Blueprint(32);
      bp.wall({ x0: 0, z0: zf, x1: H, z1: zf, y0: 0, h, t, type: 'prefab', bond: false, openings: [win(1.0, 3.0)] });
      els.push(makeElement('Außenwand vorne rechts', 0, bp));
      bp = new Blueprint(33);
      bp.wall({ x0: -H, z0: -zf, x1: 0, z1: -zf, y0: 0, h, t, type: 'prefab', bond: false, openings: [win(1.0, 2.5)] });
      els.push(makeElement('Außenwand hinten links', 0, bp));
      bp = new Blueprint(34);
      bp.wall({ x0: 0, z0: -zf, x1: H, z1: -zf, y0: 0, h, t, type: 'prefab', bond: false, openings: [win(1.5, 3.0, 0.3, 2.2)] });
      els.push(makeElement('Außenwand hinten rechts', 0, bp));
      // Giebelwände (x = ±), je zwei Hälften
      const zi = H - t;
      for (const sx of [-1, 1]) {
        const x = sx * (H - t / 2);
        bp = new Blueprint(40 + sx);
        bp.wall({ x0: x, z0: -zi, x1: x, z1: 0, y0: 0, h, t, type: 'prefab', bond: false, openings: [win(1.25, 2.75)], gable: { sPeak: zi, span: H, slope } });
        els.push(makeElement(`Giebelwand ${sx < 0 ? 'links' : 'rechts'} hinten`, 0, bp));
        bp = new Blueprint(50 + sx);
        bp.wall({ x0: x, z0: 0, x1: x, z1: zi, y0: 0, h, t, type: 'prefab', bond: false, openings: [win(1.0, 2.5), win(0.4, 1.0, 3.3, 4.1)], gable: { sPeak: 0, span: H, slope } });
        els.push(makeElement(`Giebelwand ${sx < 0 ? 'links' : 'rechts'} vorne`, 0, bp));
      }
      // Dachelemente
      for (const sd of [-1, 1]) {
        bp = new Blueprint(60 + sd);
        bp.gableRoof({ x0: -H, x1: H, z0: -H, z1: H, eaveY: h, pitch, over: 0.45, tile: 1.0, type: 'roof', side: sd });
        els.push(makeElement(`Dachelement ${sd < 0 ? 'hinten' : 'vorne'}`, 1, bp));
      }
      return els;
    },
  },
};

// ---------------- Gebäudesystem ----------------
export class BuildingSystem {
  constructor(world) {
    this.world = world;
    const tex = noiseTexture(128, { base: 228, amp: 30, seams: true, seed: 11 });
    this.solidMat = new THREE.MeshLambertMaterial({ map: tex });
    this.glassMat = new THREE.MeshLambertMaterial({ color: 0xffffff, transparent: true, opacity: 0.42, depthWrite: false });
    this.solid = new InstPool(world.scene, this.solidMat, 9000, true);
    this.glass = new InstPool(world.scene, this.glassMat, 900, false);
    this.pieces = [];
    this.hash = new Map();
    this.stamp = 1;
    this.buildings = [];
    this.debris = [];
    this.dirtyB = new Set();
    this.stabT = 0;
    this.elements = [];
    this.sites = [];
    this.stats = { destroyed: 0, built: 0, demolished: 0 };
    this.listeners = [];
  }

  key(ix, iz) {
    return (ix + 2048) * 4096 + (iz + 2048);
  }
  hashInsert(p) {
    for (let ix = Math.floor(p.min[0]); ix <= Math.floor(p.max[0]); ix++)
      for (let iz = Math.floor(p.min[2]); iz <= Math.floor(p.max[2]); iz++) {
        const k = this.key(ix, iz);
        let a = this.hash.get(k);
        if (!a) this.hash.set(k, (a = []));
        a.push(p.id);
      }
  }
  hashRemove(p) {
    for (let ix = Math.floor(p.min[0]); ix <= Math.floor(p.max[0]); ix++)
      for (let iz = Math.floor(p.min[2]); iz <= Math.floor(p.max[2]); iz++) {
        const a = this.hash.get(this.key(ix, iz));
        if (!a) continue;
        const i = a.indexOf(p.id);
        if (i >= 0) { a[i] = a[a.length - 1]; a.pop(); }
      }
  }
  query(minx, minz, maxx, maxz, cb) {
    const st = ++this.stamp;
    for (let ix = Math.floor(minx); ix <= Math.floor(maxx); ix++)
      for (let iz = Math.floor(minz); iz <= Math.floor(maxz); iz++) {
        const a = this.hash.get(this.key(ix, iz));
        if (!a) continue;
        for (let i = 0; i < a.length; i++) {
          const p = this.pieces[a[i]];
          if (p.st === st || !p.alive) continue;
          p.st = st;
          if (cb(p) === false) return;
        }
      }
  }

  newBuilding(name, kind = 'old') {
    const b = { id: this.buildings.length, name, kind, pieces: [], total: 0, alive: 0, done: false, demolished: false };
    this.buildings.push(b);
    return b;
  }

  addPiece(b, type, cx, cy, cz, sx, sy, sz, yaw, pitch, color) {
    const T = PT[type];
    const p = {
      id: this.pieces.length, b: b ? b.id : -1, type, c: new THREE.Vector3(cx, cy, cz),
      h: new THREE.Vector3(sx / 2, sy / 2, sz / 2), yaw, pitch, q: new THREE.Quaternion(), qi: new THREE.Quaternion(),
      min: [0, 0, 0], max: [0, 0, 0], hp: T.hp, alive: true, sup: [], carry: [], lat: [], st: 0, color: color.clone(),
      pool: T.glass ? this.glass : this.solid, inst: -1, ground: false, vol: sx * sy * sz,
    };
    _e.set(pitch, yaw, 0, 'YXZ');
    p.q.setFromEuler(_e);
    p.qi.copy(p.q).invert();
    // AABB aus OBB
    const ex = [0, 0, 0];
    _m.makeRotationFromQuaternion(p.q);
    const el = _m.elements;
    for (let r = 0; r < 3; r++) ex[r] = Math.abs(el[r]) * p.h.x + Math.abs(el[r + 4]) * p.h.y + Math.abs(el[r + 8]) * p.h.z;
    for (let r = 0; r < 3; r++) {
      p.min[r] = p.c.getComponent(r) - ex[r];
      p.max[r] = p.c.getComponent(r) + ex[r];
    }
    p.inst = p.pool.alloc();
    if (p.inst >= 0) {
      p.pool.set(p.inst, p.c, p.q, sx, sy, sz);
      p.pool.color(p.inst, _lin(color));
    }
    this.pieces.push(p);
    this.hashInsert(p);
    if (b) {
      b.pieces.push(p.id);
      b.total++;
      b.alive++;
    }
    return p;
  }

  link(p) {
    const e = 0.06;
    this.query(p.min[0] - e, p.min[2] - e, p.max[0] + e, p.max[2] + e, (o) => {
      if (o === p) return;
      if (o.min[1] > p.max[1] + e || o.max[1] < p.min[1] - e) return;
      if (o.min[0] > p.max[0] + e || o.max[0] < p.min[0] - e) return;
      if (o.min[2] > p.max[2] + e || o.max[2] < p.min[2] - e) return;
      // o ruht auf p?
      if (o.min[1] >= p.max[1] - 0.12 && o.c.y > p.c.y) {
        if (!PT[p.type].noSupport) { p.carry.push(o.id); o.sup.push(p.id); }
      } else if (p.min[1] >= o.max[1] - 0.12 && p.c.y > o.c.y) {
        if (!PT[o.type].noSupport) { o.carry.push(p.id); p.sup.push(o.id); }
      } else if (!PT[o.type].noSupport && !PT[p.type].noSupport) {
        p.lat.push(o.id);
        o.lat.push(p.id);
      } else if (PT[p.type].noSupport && !PT[o.type].noSupport) {
        // Fenster/Tür im Rahmen gehalten
        o.carry.push(p.id); p.sup.push(o.id);
      } else if (PT[o.type].noSupport && !PT[p.type].noSupport) {
        p.carry.push(o.id); o.sup.push(p.id);
      }
    });
  }

  // Gebäude aus Bauplan an Position erzeugen
  createFromBlueprint(bp, x, y, z, yaw, name, kind = 'old') {
    const b = this.newBuilding(name, kind);
    const c = Math.cos(yaw), s = Math.sin(yaw);
    const list = [];
    for (const bx of bp.boxes) {
      const wx = x + bx.x * c + bx.z * s;
      const wz = z - bx.x * s + bx.z * c;
      const p = this.addPiece(b, bx.type, wx, y + bx.y, wz, bx.sx, bx.sy, bx.sz, yaw + bx.yaw, bx.pitch, bx.color);
      list.push(p);
    }
    this.finalize(b, list, y);
    return b;
  }

  finalize(b, list, groundY) {
    for (const p of list) {
      const gy = groundY !== undefined ? groundY : this.world.terrain.height(p.c.x, p.c.z);
      p.ground = p.min[1] <= gy + 0.08;
    }
    for (const p of list) this.link(p);
    this.dirtyB.add(b.id);
  }

  // Oberkante begehbarer Bauteile unter (x,z) bis maxY
  topAt(x, z, maxY) {
    let best = -Infinity;
    const a = this.hash.get(this.key(Math.floor(x), Math.floor(z)));
    if (!a) return best;
    for (let i = 0; i < a.length; i++) {
      const p = this.pieces[a[i]];
      if (!p.alive) continue;
      if (x < p.min[0] || x > p.max[0] || z < p.min[2] || z > p.max[2]) continue;
      let top = p.max[1];
      if (p.pitch) {
        // Dachfläche: Höhe an der Stelle genauer
        _v.set(x - p.c.x, 0, z - p.c.z).applyQuaternion(p.qi);
        const lz = clamp(_v.z, -p.h.z, p.h.z);
        top = p.c.y - Math.sin(p.pitch) * lz + p.h.y / Math.cos(p.pitch);
      }
      if (top <= maxY && top > best) best = top;
    }
    return best;
  }
  pieceAtTop(x, z, top) {
    const a = this.hash.get(this.key(Math.floor(x), Math.floor(z)));
    if (!a) return -1;
    for (let i = 0; i < a.length; i++) {
      const p = this.pieces[a[i]];
      if (p.alive && Math.abs(p.max[1] - top) < 0.25 && x >= p.min[0] && x <= p.max[0] && z >= p.min[2] && z <= p.max[2]) return p.id;
    }
    return -1;
  }

  // Abstand Kugel zu Bauteil (OBB)
  sphereDist(p, c) {
    _v.subVectors(c, p.c).applyQuaternion(p.qi);
    const dx = _v.x - clamp(_v.x, -p.h.x, p.h.x);
    const dy = _v.y - clamp(_v.y, -p.h.y, p.h.y);
    const dz = _v.z - clamp(_v.z, -p.h.z, p.h.z);
    return Math.sqrt(dx * dx + dy * dy + dz * dz);
  }

  // Werkzeug-Kontakt (Löffel, Schild, Abrissbirne). power = Schaden/s, impulse = Sofortschaden
  hitSphere(c, r, vel, power, dt, impulse = 0) {
    const res = { hit: 0, broken: 0, blocked: false, hardest: 0 };
    const speed = vel.length();
    this.query(c.x - r, c.z - r, c.x + r, c.z + r, (p) => {
      if (p.min[1] > c.y + r || p.max[1] < c.y - r) return;
      if (this.sphereDist(p, c) > r) return;
      res.hit++;
      const dmg = impulse + power * dt * clamp(speed / 0.4, 0.25, 2.5);
      p.hp -= dmg;
      if (p.hp <= 0) {
        res.broken++;
        _v2.copy(vel).multiplyScalar(0.6);
        _v2.x += (Math.random() - 0.5) * 1.2;
        _v2.y += Math.random() * 0.8;
        _v2.z += (Math.random() - 0.5) * 1.2;
        this.destroy(p, _v2);
      } else if (PT[p.type].hp > 30) {
        res.blocked = true;
        res.hardest = Math.max(res.hardest, PT[p.type].hp);
      }
    });
    if (res.broken) {
      const a = this.world.audio;
      a.shot('crash', Math.min(1.2, 0.35 + res.broken * 0.15));
      this.world.effects.dust(c.x, c.y, c.z, 2 + res.broken * 2, 0xb0a89a, 1.4);
    }
    return res;
  }

  // Fahrzeug-Kollision: OBB (2D) gegen Bauteile über Stufenhöhe
  boxBlocked(cx, cz, hw, hl, yaw, y0, y1) {
    const c = Math.cos(yaw), s = Math.sin(yaw);
    // Achsen: links (c,-s) und vorne (s,c)
    const ex = Math.abs(c) * hw + Math.abs(s) * hl, ez = Math.abs(s) * hw + Math.abs(c) * hl;
    let hitP = null;
    this.query(cx - ex, cz - ez, cx + ex, cz + ez, (p) => {
      if (p.max[1] < y0 + 0.45 || p.min[1] > y1) return;
      const pcx = (p.min[0] + p.max[0]) / 2, pcz = (p.min[2] + p.max[2]) / 2;
      const phx = (p.max[0] - p.min[0]) / 2, phz = (p.max[2] - p.min[2]) / 2;
      const dx = pcx - cx, dz = pcz - cz;
      if (Math.abs(dx) > ex + phx || Math.abs(dz) > ez + phz) return;
      // Achse links
      const dl = Math.abs(dx * c - dz * s), rl = Math.abs(c) * phx + Math.abs(s) * phz;
      if (dl > hw + rl) return;
      const df = Math.abs(dx * s + dz * c), rf = Math.abs(s) * phx + Math.abs(c) * phz;
      if (df > hl + rf) return;
      hitP = p;
      return false;
    });
    return hitP;
  }

  destroy(p, vel) {
    if (!p.alive) return;
    p.alive = false;
    this.hashRemove(p);
    this.stats.destroyed++;
    const b = this.buildings[p.b];
    if (b) {
      b.alive--;
      this.dirtyB.add(b.id);
      this.stabT = Math.min(this.stabT, 0.05);
    }
    if (p.type === 'glass') this.world.audio.shot('glass', 0.8, 0.1);
    this.spawnDebris(p, vel);
  }

  spawnDebris(p, vel) {
    if (p.inst < 0) return;
    const d = {
      pool: p.pool, inst: p.inst, pos: p.c.clone(), v: vel ? vel.clone() : new THREE.Vector3(),
      q: p.q.clone(), w: new THREE.Vector3((Math.random() - 0.5) * 1.5, (Math.random() - 0.5) * 1.5, (Math.random() - 0.5) * 1.5),
      h: p.h.clone(), mass: p.vol * PT[p.type].dens, type: p.type, sleep: false, rest: 0, age: 0, sup: -1, hit: false, vol: p.vol,
    };
    p.inst = -1;
    this.debris.push(d);
    if (this.debris.length > 1100) {
      // älteste Trümmer zu Schutt umwandeln
      for (let i = 0; i < this.debris.length && this.debris.length > 1000; i++) {
        if (this.debris[i].sleep) { this.toRubble(this.debris[i]); this.debris.splice(i, 1); i--; }
      }
    }
  }

  toRubble(d) {
    if (d.sup < 0) {
      const r = Math.max(0.3, Math.max(d.h.x, d.h.z) * 0.9);
      this.world.terrain.deposit(d.pos.x, d.pos.z, d.vol * 1.45, MAT.RUBBLE, r);
    }
    d.pool.release(d.inst);
  }

  // Statikprüfung: 0-1-Breitensuche von den Fundamenten
  checkStability(b) {
    const ids = b.pieces;
    const dist = this._dist || (this._dist = new Map());
    dist.clear();
    const dq = [];
    let head = 0;
    const front = [];
    for (const id of ids) {
      const p = this.pieces[id];
      if (p.alive && p.ground) { dist.set(id, 0); front.push(id); }
    }
    // Deque über zwei Listen: front (Kosten 0) wird zuerst abgearbeitet
    const pushFront = (id) => front.push(id);
    const pushBack = (id) => dq.push(id);
    while (front.length || head < dq.length) {
      const id = front.length ? front.pop() : dq[head++];
      const p = this.pieces[id];
      const d = dist.get(id);
      for (const qid of p.carry) {
        const q = this.pieces[qid];
        if (!q.alive) continue;
        const od = dist.get(qid);
        if (od === undefined || d < od) { dist.set(qid, d); pushFront(qid); }
      }
      for (const qid of p.lat) {
        const q = this.pieces[qid];
        if (!q.alive) continue;
        const nd = d + 1;
        if (nd > PT[q.type].span) continue;
        const od = dist.get(qid);
        if (od === undefined || nd < od) { dist.set(qid, nd); pushBack(qid); }
      }
    }
    let fell = 0;
    for (const id of ids) {
      const p = this.pieces[id];
      if (!p.alive) continue;
      if (!dist.has(id)) {
        p.alive = false;
        this.hashRemove(p);
        b.alive--;
        this.stats.destroyed++;
        this.spawnDebris(p, new THREE.Vector3((Math.random() - 0.5) * 0.3, 0, (Math.random() - 0.5) * 0.3));
        fell++;
      }
    }
    if (fell) {
      this.world.audio.shot('crash', Math.min(1.5, 0.4 + fell * 0.02), 0.2);
      this.dirtyB.add(b.id);
    }
    // Abriss erkannt?
    if (!b.demolished && b.total > 20) {
      let standing = 0;
      for (const id of ids) {
        const p = this.pieces[id];
        if (p.alive && p.max[1] > (b.baseY || 0) + 0.8) standing++;
      }
      if (b.done || b.kind === 'old') {
        if (standing < b.total * 0.04) {
          b.demolished = true;
          this.stats.demolished++;
          this.emit('demolished', b);
        }
      }
    }
    return fell;
  }

  emit(ev, data) {
    for (const l of this.listeners) l(ev, data);
  }

  updateDebris(dt) {
    const T = this.world.terrain;
    const g = 9.81;
    const corner = new THREE.Vector3();
    const low = new THREE.Vector3();
    const dqn = new THREE.Quaternion();
    const tmp = new THREE.Vector3();
    let n = 0;
    const arr = this.debris;
    for (let i = 0; i < arr.length; i++) {
      const d = arr[i];
      d.age += dt;
      if (d.sleep) {
        if (d.sup >= 0 && !this.pieces[d.sup].alive) {
          d.sleep = false; d.rest = 0; d.sup = -1;
        } else {
          d.rest += dt;
          const lim = d.type === 'glass' ? 1.5 : 5;
          if (d.sup < 0 && d.rest > lim) { this.toRubble(d); continue; }
          arr[n++] = d;
          continue;
        }
      }
      d.v.y -= g * dt;
      d.pos.addScaledVector(d.v, dt);
      const wl = d.w.length();
      if (wl > 1e-4) {
        tmp.copy(d.w).divideScalar(wl);
        dqn.setFromAxisAngle(tmp, wl * dt);
        d.q.premultiply(dqn);
      }
      // tiefste Ecke
      let minY = Infinity;
      for (let k = 0; k < 8; k++) {
        corner.set(k & 1 ? d.h.x : -d.h.x, k & 2 ? d.h.y : -d.h.y, k & 4 ? d.h.z : -d.h.z).applyQuaternion(d.q).add(d.pos);
        if (corner.y < minY) { minY = corner.y; low.copy(corner); }
      }
      const tg = T.height(low.x, low.z);
      const pt = this.topAt(low.x, low.z, minY + 0.35);
      const gnd = Math.max(tg, pt);
      let contact = false;
      if (minY < gnd) {
        contact = true;
        const onPiece = pt > tg;
        d.pos.y += gnd - minY;
        const impact = -d.v.y;
        if (impact > 3.5 && !d.hit && onPiece) {
          d.hit = true;
          const pid = this.pieceAtTop(low.x, low.z, pt);
          if (pid >= 0) {
            const p = this.pieces[pid];
            p.hp -= d.mass * impact * impact * 1.8;
            if (p.hp <= 0) this.destroy(p, tmp.set(d.v.x * 0.3, -1, d.v.z * 0.3));
          }
        }
        if (impact > 2.5 && d.mass > 0.15) this.world.audio.shot('thud', Math.min(1, d.mass * impact * 0.08), 0.08);
        if (impact > 4 && Math.random() < 0.3) this.world.effects.dust(low.x, gnd + 0.2, low.z, 2, 0xa9a196, 1.2);
        if (d.v.y < 0) d.v.y = -d.v.y * 0.15;
        d.v.x *= 0.82; d.v.z *= 0.82;
        // Kippmoment um die Kontaktecke
        tmp.subVectors(d.pos, low);
        const r2 = tmp.lengthSq() + 0.02;
        const tx = tmp.z * g, tz = -tmp.x * g; // r x (0,-g,0)
        d.w.x += (tx / r2) * dt * 1.2;
        d.w.z += (tz / r2) * dt * 1.2;
        d.w.multiplyScalar(0.9);
        d.sup = onPiece ? this.pieceAtTop(low.x, low.z, pt) : -1;
      }
      // seitlich aus intakten Bauteilen herausdrücken
      const a = this.hash.get(this.key(Math.floor(d.pos.x), Math.floor(d.pos.z)));
      if (a) {
        for (let k = 0; k < a.length; k++) {
          const p = this.pieces[a[k]];
          if (!p.alive || p.id === d.sup) continue;
          if (d.pos.y < p.min[1] || d.pos.y > p.max[1]) continue;
          const px0 = d.pos.x - p.min[0], px1 = p.max[0] - d.pos.x;
          const pz0 = d.pos.z - p.min[2], pz1 = p.max[2] - d.pos.z;
          if (px0 < 0 || px1 < 0 || pz0 < 0 || pz1 < 0) continue;
          const m = Math.min(px0, px1, pz0, pz1);
          if (m > 0.4) continue;
          if (m === px0) { d.pos.x -= px0 + 0.02; d.v.x = -Math.abs(d.v.x) * 0.3; }
          else if (m === px1) { d.pos.x += px1 + 0.02; d.v.x = Math.abs(d.v.x) * 0.3; }
          else if (m === pz0) { d.pos.z -= pz0 + 0.02; d.v.z = -Math.abs(d.v.z) * 0.3; }
          else { d.pos.z += pz1 + 0.02; d.v.z = Math.abs(d.v.z) * 0.3; }
        }
      }
      if (!T.inside(d.pos.x, d.pos.z, 1)) {
        d.pos.x = clamp(d.pos.x, -T.half + 1, T.half - 1);
        d.pos.z = clamp(d.pos.z, -T.half + 1, T.half - 1);
      }
      if (contact && d.v.lengthSq() < 0.09 && d.w.lengthSq() < 0.3) {
        d.rest += dt;
        if (d.rest > 0.4) { d.sleep = true; d.rest = 0; d.v.set(0, 0, 0); d.w.set(0, 0, 0); }
      } else d.rest = 0;
      if (d.age > 25 && d.sup < 0) { this.toRubble(d); continue; }
      d.pool.set(d.inst, d.pos, d.q, d.h.x * 2, d.h.y * 2, d.h.z * 2);
      arr[n++] = d;
    }
    arr.length = n;
  }

  // Trümmer, die von Werkzeugen getroffen werden, wegschieben
  pushDebris(c, r, vel) {
    for (const d of this.debris) {
      const dx = d.pos.x - c.x, dy = d.pos.y - c.y, dz = d.pos.z - c.z;
      const rr = r + Math.max(d.h.x, d.h.y, d.h.z);
      if (dx * dx + dy * dy + dz * dz > rr * rr) continue;
      d.sleep = false;
      d.rest = 0;
      d.v.x = vel.x * 0.9 + dx * 0.5;
      d.v.y = Math.max(d.v.y, vel.y * 0.5 + 0.5);
      d.v.z = vel.z * 0.9 + dz * 0.5;
    }
  }

  // Trümmer im Löffel aufnehmen: liefert Volumen (wird zu Schutt)
  scoopDebris(c, r, maxVol) {
    let got = 0;
    for (let i = 0; i < this.debris.length && got < maxVol; i++) {
      const d = this.debris[i];
      const dx = d.pos.x - c.x, dy = d.pos.y - c.y, dz = d.pos.z - c.z;
      if (dx * dx + dy * dy + dz * dz > r * r) continue;
      got += d.vol * 1.45;
      d.pool.release(d.inst);
      this.debris.splice(i, 1);
      i--;
    }
    return got;
  }

  // ---------- Speichern / Laden ----------
  reset() {
    for (const p of this.pieces) if (p.inst >= 0) p.pool.release(p.inst);
    for (const d of this.debris) d.pool.release(d.inst);
    for (const el of [...this.elements]) el.remove();
    for (const s of [...this.sites]) s.finish();
    this.pieces = [];
    this.hash.clear();
    this.buildings = [];
    this.debris = [];
    this.elements = [];
    this.sites = [];
    this.dirtyB.clear();
  }
  serialize() {
    const types = Object.keys(PT);
    const r = (v) => Math.round(v * 1000) / 1000;
    const pcs = [];
    for (const p of this.pieces) {
      if (!p.alive || p.b < 0) continue;
      pcs.push([p.b, types.indexOf(p.type), r(p.c.x), r(p.c.y), r(p.c.z), r(p.h.x * 2), r(p.h.y * 2), r(p.h.z * 2), r(p.yaw), r(p.pitch), p.color.getHex(), Math.round(p.hp), p.ground ? 1 : 0]);
    }
    return {
      blds: this.buildings.map((b) => ({ name: b.name, kind: b.kind, total: b.total, done: b.done, demolished: b.demolished, baseY: b.baseY || 0 })),
      pcs,
      stats: this.stats,
      sites: this.sites.map((s) => s.serialize()),
      els: this.elements.filter((e) => e.site && e.state !== 'placed').map((e) => ({ s: this.sites.indexOf(e.site), i: e.index, x: e.pos.x, y: e.pos.y, z: e.pos.z, yaw: e.yaw })),
    };
  }
  deserialize(d) {
    this.reset();
    for (const bd of d.blds) {
      const b = this.newBuilding(bd.name, bd.kind);
      b.done = bd.done; b.demolished = bd.demolished; b.baseY = bd.baseY; b.savedTotal = bd.total;
    }
    const types = Object.keys(PT);
    const list = [];
    for (const a of d.pcs) {
      const p = this.addPiece(this.buildings[a[0]], types[a[1]], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9], new THREE.Color(a[10]));
      p.hp = a[11];
      p.ground = !!a[12];
      list.push(p);
    }
    for (const b of this.buildings) b.total = b.savedTotal;
    for (const p of list) this.link(p);
    for (const b of this.buildings) this.dirtyB.add(b.id);
    if (d.stats) this.stats = d.stats;
    for (const sd of d.sites || []) {
      const s = new ConstructionSite(this, sd.key, sd.x, sd.z, sd.yaw);
      s.restore(sd, this.buildings);
      this.sites.push(s);
    }
    for (const e of d.els || []) {
      const site = this.sites[e.s];
      if (!site) continue;
      const el = new Element(this, site, site.elementsDef[e.i]);
      el.index = e.i;
      el.pos.set(e.x, e.y, e.z);
      el.yaw = e.yaw;
      el.pos.y = Math.max(el.pos.y, el.groundBelow(e.x, e.z, e.yaw, e.y + 0.2));
      el.state = 'loose';
      el.sync();
      this.elements.push(el);
    }
  }

  update(dt) {
    this.stabT -= dt;
    if (this.dirtyB.size && this.stabT <= 0) {
      const id = this.dirtyB.values().next().value;
      this.dirtyB.delete(id);
      this.checkStability(this.buildings[id]);
      this.stabT = 0.12;
    }
    this.updateDebris(dt);
    for (const el of this.elements) el.update(dt);
    for (const s of this.sites) s.update(dt);
    this.solid.flush();
    this.glass.flush();
  }
}

const _lc = new THREE.Color();
function _lin(c) {
  return _lc.copy(c).convertSRGBToLinear();
}

// ---------------- Fertigteil (am Kran hängend oder gelagert) ----------------
export class Element {
  constructor(sys, site, def) {
    this.sys = sys;
    this.site = site;
    this.def = def;
    this.name = def.name;
    this.tier = def.tier;
    this.mass = def.mass;
    const geo = boxesGeometry(def.boxes.map((b) => ({ ...b, color: b.color.clone().convertSRGBToLinear() })));
    this.mesh = new THREE.Mesh(geo, new THREE.MeshLambertMaterial({ vertexColors: true, map: sys.solidMat.map }));
    this.mesh.castShadow = true;
    this.mesh.receiveShadow = true;
    sys.world.scene.add(this.mesh);
    this.pos = new THREE.Vector3();
    this.yaw = 0;
    this.vy = 0;
    this.state = 'stored';
    this.size = def.size;
    this.liftY = def.size.y + 0.15;
    this.marker = null;
    this.trestle = null;
  }
  get halfX() { return this.size.x / 2; }
  get halfZ() { return this.size.z / 2; }
  // Untergrund unter dem Element (Gelände oder Bauteile)
  groundBelow(x = this.pos.x, z = this.pos.z, yaw = this.yaw, bottom = this.pos.y) {
    const c = Math.cos(yaw), s = Math.sin(yaw);
    const hx = Math.max(0.05, this.halfX - 0.12), hz = Math.max(0.05, this.halfZ - 0.12);
    let g = -Infinity;
    for (const [lx, lz] of [[0, 0], [hx, hz], [-hx, hz], [hx, -hz], [-hx, -hz], [hx, 0], [-hx, 0], [0, hz], [0, -hz]]) {
      const wx = x + lx * c + lz * s, wz = z - lx * s + lz * c;
      const h = Math.max(this.sys.world.terrain.height(wx, wz), this.sys.topAt(wx, wz, bottom + 0.3));
      if (h > g) g = h;
    }
    return g;
  }
  sync() {
    this.mesh.position.copy(this.pos);
    this.mesh.rotation.set(0, this.yaw, 0);
  }
  update(dt) {
    if (this.state === 'loose' || this.state === 'falling') {
      const g = this.groundBelow();
      if (this.pos.y > g + 0.01) {
        this.state = 'falling';
        this.vy -= 9.81 * dt;
        this.pos.y += this.vy * dt;
        if (this.pos.y <= g) {
          const impact = -this.vy;
          this.pos.y = g;
          this.vy = 0;
          this.state = 'loose';
          if (impact > 3.2) this.shatter(impact);
          else if (impact > 0.8) this.sys.world.audio.shot('thud', 0.6);
        }
      } else {
        this.pos.y = Math.max(this.pos.y, g);
        this.vy = 0;
        this.state = 'loose';
      }
      this.sync();
    }
  }
  // Absturz: Element zerbricht in Trümmer
  shatter(impact) {
    const sys = this.sys;
    const c = Math.cos(this.yaw), s = Math.sin(this.yaw);
    for (const b of this.def.boxes) {
      const wx = this.pos.x + b.x * c + b.z * s, wz = this.pos.z - b.x * s + b.z * c;
      const p = sys.addPiece(null, b.type, wx, this.pos.y + b.y, wz, b.sx, b.sy, b.sz, this.yaw + b.yaw, b.pitch, b.color);
      p.alive = false;
      sys.hashRemove(p);
      sys.spawnDebris(p, new THREE.Vector3((Math.random() - 0.5) * 2, Math.random() * impact * 0.2, (Math.random() - 0.5) * 2));
    }
    sys.world.audio.shot('crash', 1.4);
    sys.world.effects.dust(this.pos.x, this.pos.y + 0.5, this.pos.z, 10, 0xb5ada0, 1.8);
    this.remove();
    this.state = 'broken';
    sys.world.ui && sys.world.ui.toast(`${this.name} ist abgestürzt und zerbrochen!`, 'warn');
    if (this.site) this.site.onElementLost(this);
  }
  remove() {
    this.sys.world.scene.remove(this.mesh);
    if (this.trestle) this.sys.world.scene.remove(this.trestle);
    const i = this.sys.elements.indexOf(this);
    if (i >= 0) this.sys.elements.splice(i, 1);
  }
}

// ---------------- Baustelle ----------------
export const STAGES = {
  planum: 'Baugrund planieren',
  beton: 'Bodenplatte betonieren',
  haerten: 'Beton härtet aus',
  montage: 'Fertigteile montieren',
  fertig: 'Fertig',
};

export class ConstructionSite {
  constructor(sys, designKey, x, z, yaw) {
    this.sys = sys;
    this.world = sys.world;
    this.key = designKey;
    this.design = DESIGNS[designKey];
    this.x = x;
    this.z = z;
    this.yaw = yaw;
    this.stage = 'planum';
    this.dev = 9;
    this.checkT = 0;
    this.baseY = 0;
    this.poured = 0;
    this.slabT = this.design.slabT;
    this.need = (this.design.hw * 2) * (this.design.hd * 2) * this.slabT;
    this.cure = 0;
    this.elementsDef = this.design.build();
    this.placed = new Set();
    this.tier = 0;
    this.group = new THREE.Group();
    this.world.scene.add(this.group);
    this.building = null;
    this.makeStakes();
    this.makeGhost();
    this.world.catchers.push(this);
  }

  toWorld(lx, lz) {
    const c = Math.cos(this.yaw), s = Math.sin(this.yaw);
    return [this.x + lx * c + lz * s, this.z - lx * s + lz * c];
  }
  toLocal(wx, wz) {
    const c = Math.cos(this.yaw), s = Math.sin(this.yaw);
    const dx = wx - this.x, dz = wz - this.z;
    return [dx * c - dz * s, dx * s + dz * c];
  }

  makeStakes() {
    const T = this.world.terrain;
    const { hw, hd } = this.design;
    const m = 0.6;
    const stakeMat = new THREE.MeshLambertMaterial({ color: col(0xc8a26a) });
    const tipMat = new THREE.MeshLambertMaterial({ color: col(0xd9381e) });
    this.stakes = [];
    const pts = [[-hw - m, -hd - m], [hw + m, -hd - m], [hw + m, hd + m], [-hw - m, hd + m]];
    for (const [lx, lz] of pts) {
      const [wx, wz] = this.toWorld(lx, lz);
      const s = new THREE.Mesh(new THREE.BoxGeometry(0.06, 1.0, 0.06), stakeMat);
      const tip = new THREE.Mesh(new THREE.BoxGeometry(0.065, 0.15, 0.065), tipMat);
      tip.position.y = 0.42;
      s.add(tip);
      s.position.set(wx, T.height(wx, wz) + 0.5, wz);
      s.castShadow = true;
      this.group.add(s);
      this.stakes.push(s);
    }
    // Schnurgerüst
    const lineMat = new THREE.LineBasicMaterial({ color: 0xff5522 });
    const g = new THREE.BufferGeometry().setFromPoints(this.stakes.concat([this.stakes[0]]).map((s) => s.position.clone().setY(s.position.y + 0.3)));
    this.line = new THREE.Line(g, lineMat);
    this.group.add(this.line);
    // Schild
    const tex = textTexture(`BAUPLATZ\n${this.design.name}`, { w: 512, h: 200, font: 'bold 54px sans-serif', bg: '#ffffff' });
    const back = new THREE.MeshLambertMaterial({ color: col(0x8d9094) });
    const sign = new THREE.Mesh(new THREE.BoxGeometry(1.6, 0.62, 0.03), [back, back, back, back, new THREE.MeshBasicMaterial({ map: tex }), back]);
    const [sx, sz] = this.toWorld(-hw - m - 0.2, hd + m + 0.4);
    sign.position.set(sx, T.height(sx, sz) + 1.4, sz);
    sign.rotation.y = this.yaw;
    const post = new THREE.Mesh(new THREE.BoxGeometry(0.08, 1.4, 0.08), stakeMat);
    post.position.set(sx, T.height(sx, sz) + 0.7, sz);
    this.group.add(sign, post);
  }

  makeGhost() {
    this.ghostMat = new THREE.MeshBasicMaterial({ color: 0x4fb3ff, transparent: true, opacity: 0.22, depthWrite: false });
    this.ghostOk = new THREE.MeshBasicMaterial({ color: 0x45e070, transparent: true, opacity: 0.35, depthWrite: false });
    this.ghosts = this.elementsDef.map((d) => {
      const geo = boxesGeometry(d.boxes.map((b) => ({ ...b, color: new THREE.Color(1, 1, 1) })));
      const m = new THREE.Mesh(geo, this.ghostMat);
      m.visible = false;
      this.group.add(m);
      return m;
    });
    // Umriss der Bodenplatte
    const { hw, hd } = this.design;
    this.footGhost = new THREE.Mesh(new THREE.BoxGeometry(hw * 2, 0.2, hd * 2), this.ghostMat);
    this.group.add(this.footGhost);
    this.updateGhostPos();
  }

  updateGhostPos() {
    const T = this.world.terrain;
    const y = this.stage === 'planum' ? T.statsRect(this.x, this.z, this.design.hw, this.design.hd, this.yaw).mean : this.baseY;
    this.footGhost.position.set(this.x, y + 0.1, this.z);
    this.footGhost.rotation.y = this.yaw;
    const top = y + this.slabT;
    this.elementsDef.forEach((d, i) => {
      const [wx, wz] = this.toWorld(d.local.x, d.local.z);
      this.ghosts[i].position.set(wx, top + d.local.y, wz);
      this.ghosts[i].rotation.y = this.yaw;
      d.target = new THREE.Vector3(wx, top + d.local.y, wz);
      d.targetYaw = this.yaw;
    });
  }

  tryCatch(o) {
    if (o.mat !== MAT.CONCRETE || this.stage === 'planum') return false;
    if (o.y > this.baseY + 0.8 || o.y < this.baseY - 0.4) return false;
    const [lx, lz] = this.toLocal(o.x, o.z);
    if (Math.abs(lx) > this.design.hw || Math.abs(lz) > this.design.hd) return false;
    if (this.stage !== 'beton') {
      // Überschüssiger Beton wird vom Bautrupp abgezogen
      if (!this.overpourMsg) {
        this.overpourMsg = true;
        this.world.ui.toast('Bodenplatte ist schon voll – Beton ablassen stoppen!', 'warn');
      }
      return true;
    }
    this.poured += o.vol;
    return true;
  }

  update(dt) {
    const T = this.world.terrain;
    const { hw, hd } = this.design;
    if (this.stage === 'planum') {
      this.checkT -= dt;
      if (this.checkT <= 0) {
        this.checkT = 0.5;
        const st = T.statsRect(this.x, this.z, hw + 0.3, hd + 0.3, this.yaw);
        this.dev = Math.max(st.max - st.mean, st.mean - st.min);
        this.meanY = st.mean;
        this.updateGhostPos();
        if (this.dev <= 0.15) {
          // Feinplanum und Schottertragschicht durch den Bautrupp
          this.baseY = st.mean;
          T.levelRect(this.x, this.z, hw + 0.3, hd + 0.3, this.yaw, this.baseY, MAT.GRAVEL);
          this.stage = 'beton';
          this.makeFormwork();
          this.updateGhostPos();
          this.footGhost.visible = false;
          this.world.ui.toast('Planum fertig! Schalung gestellt – jetzt mit dem Fahrmischer betonieren.', 'ok');
        }
      }
    } else if (this.stage === 'beton') {
      const f = Math.min(1, this.poured / this.need);
      this.pour.scale.y = Math.max(0.01, f);
      this.pour.position.y = this.baseY + (this.slabT * f) / 2;
      if (f >= 1) {
        this.stage = 'haerten';
        this.cure = 20;
        this.world.ui.toast('Bodenplatte betoniert. Beton härtet aus (Zeitraffer)…', 'ok');
      }
    } else if (this.stage === 'haerten') {
      this.cure -= dt;
      this.pour.material.color.lerp(new THREE.Color(col(0xb4b3ae)), dt * 0.1);
      if (this.cure <= 0) {
        this.group.remove(this.pour);
        this.group.remove(this.formwork);
        const bp = new Blueprint(77);
        bp.slab({ x0: -hw, z0: -hd, x1: hw, z1: hd, y0: 0, t: this.slabT, type: 'concrete', tile: 1.05 });
        this.building = this.sys.createFromBlueprint(bp, this.x, this.baseY, this.z, this.yaw, this.design.name, 'new');
        this.building.baseY = this.baseY;
        this.stage = 'montage';
        this.deliver(0);
      }
    } else if (this.stage === 'montage') {
      // Geister für aktuelle Stufe
      const crane = this.world.crane;
      const hooked = crane && crane.load;
      this.elementsDef.forEach((d, i) => {
        const show = d.tier === this.tier && !this.placed.has(i);
        this.ghosts[i].visible = show;
        if (show) this.ghosts[i].material = hooked && hooked.def === d && this.canPlace(hooked) ? this.ghostOk : this.ghostMat;
      });
      const remaining = this.elementsDef.filter((d, i) => d.tier === this.tier && !this.placed.has(i)).length;
      if (remaining === 0) {
        const next = this.elementsDef.filter((d, i) => d.tier > this.tier && !this.placed.has(i));
        if (next.length) {
          this.tier = Math.min(...next.map((d) => d.tier));
          this.deliver(this.tier);
        } else {
          this.stage = 'fertig';
          this.building.done = true;
          this.sys.stats.built++;
          this.world.ui.toast(`${this.design.name} fertiggestellt! 🎉`, 'ok');
          this.finish();
        }
      }
    }
  }

  makeFormwork() {
    const { hw, hd } = this.design;
    this.formwork = new THREE.Group();
    const mat = new THREE.MeshLambertMaterial({ color: col(0xc9a571) });
    const t = 0.04, h = this.slabT + 0.08;
    const add = (w, d, x, z) => {
      const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
      m.position.set(x, h / 2, z);
      m.castShadow = true;
      this.formwork.add(m);
    };
    add(hw * 2 + 2 * t, t, 0, hd + t / 2);
    add(hw * 2 + 2 * t, t, 0, -hd - t / 2);
    add(t, hd * 2, hw + t / 2, 0);
    add(t, hd * 2, -hw - t / 2, 0);
    // Bewehrungsmatte angedeutet
    const rebar = new THREE.Mesh(new THREE.PlaneGeometry(hw * 2 - 0.1, hd * 2 - 0.1, 12, 12), new THREE.MeshBasicMaterial({ color: 0x5a4030, wireframe: true }));
    rebar.rotation.x = -Math.PI / 2;
    rebar.position.y = 0.08;
    this.formwork.add(rebar);
    this.formwork.position.set(this.x, this.baseY, this.z);
    this.formwork.rotation.y = this.yaw;
    this.group.add(this.formwork);
    this.pour = new THREE.Mesh(new THREE.BoxGeometry(hw * 2, this.slabT, hd * 2), new THREE.MeshLambertMaterial({ color: col(0x8d8c88) }));
    this.pour.position.set(this.x, this.baseY, this.z);
    this.pour.rotation.y = this.yaw;
    this.pour.receiveShadow = true;
    this.pour.scale.y = 0.01;
    this.group.add(this.pour);
  }

  // Anlieferung der Fertigteile auf den Lagerplatz
  deliver(tier) {
    const yard = this.world.yard;
    let cx = yard.x0 + 0.4, cz = yard.z0 + 0.4, rowH = 0;
    let n = 0;
    this.elementsDef.forEach((d, i) => {
      if (d.tier !== tier || this.placed.has(i)) return;
      const sx = d.size.x, sz = d.size.z;
      if (cx + sx > yard.x1) { cx = yard.x0 + 0.4; cz += rowH + 0.8; rowH = 0; }
      const el = new Element(this.sys, this, d);
      el.index = i;
      el.pos.set(cx + sx / 2, 0, cz + sz / 2);
      el.yaw = 0;
      el.pos.y = el.groundBelow(el.pos.x, el.pos.z, 0, 50);
      el.state = 'stored';
      el.sync();
      if (d.boxes.some((b) => b.pitch)) {
        // Dachelement auf Transportbock
        const tr = new THREE.Mesh(new THREE.BoxGeometry(sx * 0.9, 0.12, 0.12), new THREE.MeshLambertMaterial({ color: col(0x8a6a40) }));
        tr.position.set(el.pos.x, el.pos.y + 0.06, el.pos.z);
        this.world.scene.add(tr);
        el.trestle = tr;
      }
      this.sys.elements.push(el);
      cx += sx + 0.8;
      rowH = Math.max(rowH, sz);
      n++;
    });
    if (n) this.world.ui.toast(`Lkw hat ${n} Fertigteile auf den Lagerplatz geliefert. Mit dem Kran montieren!`, 'ok');
  }

  canPlace(el) {
    const d = el.def;
    if (!d.target || d.tier !== this.tier || this.placed.has(el.index)) return false;
    const dx = el.pos.x - d.target.x, dz = el.pos.z - d.target.z;
    if (dx * dx + dz * dz > 0.7 * 0.7) return false;
    const dy = el.pos.y - d.target.y;
    if (dy < -0.4 || dy > 0.45) return false;
    return true;
  }

  // Wird beim Ablassen am Kran aufgerufen
  place(el) {
    const d = el.def;
    const c = Math.cos(d.targetYaw), s = Math.sin(d.targetYaw);
    const list = [];
    for (const b of d.boxes) {
      const wx = d.target.x + b.x * c + b.z * s, wz = d.target.z - b.x * s + b.z * c;
      const p = this.sys.addPiece(this.building, b.type, wx, d.target.y + b.y, wz, b.sx, b.sy, b.sz, d.targetYaw + b.yaw, b.pitch, b.color);
      list.push(p);
    }
    this.sys.finalize(this.building, list, this.baseY);
    this.placed.add(el.index);
    el.remove();
    el.state = 'placed';
    this.world.audio.shot('thud', 0.7);
    this.world.ui.toast(`${d.name} montiert (${this.placed.size}/${this.elementsDef.length})`, 'ok');
  }

  onElementLost(el) {
    // Ersatzteil wird nachgeliefert
    setTimeout(() => {
      if (this.stage === 'montage') {
        const e2 = new Element(this.sys, this, el.def);
        e2.index = el.index;
        const yard = this.world.yard;
        e2.pos.set((yard.x0 + yard.x1) / 2, 0, yard.z1 - el.def.size.z / 2 - 0.3);
        e2.pos.y = e2.groundBelow(e2.pos.x, e2.pos.z, 0, 50);
        e2.sync();
        this.sys.elements.push(e2);
        this.world.ui.toast(`Ersatz für „${el.def.name}“ geliefert (Lagerplatz).`, 'warn');
      }
    }, 6000);
  }

  finish() {
    this.world.scene.remove(this.group);
    const i = this.world.catchers.indexOf(this);
    if (i >= 0) this.world.catchers.splice(i, 1);
    const j = this.sys.sites.indexOf(this);
    if (j >= 0) this.sys.sites.splice(j, 1);
  }
  cancel() {
    for (const el of [...this.sys.elements]) if (el.site === this) el.remove();
    this.finish();
  }

  serialize() {
    return { key: this.key, x: this.x, z: this.z, yaw: this.yaw, stage: this.stage, poured: this.poured, cure: this.cure, baseY: this.baseY, placed: [...this.placed], tier: this.tier, bid: this.building ? this.building.id : -1 };
  }
  restore(d, blds) {
    this.stage = d.stage;
    this.poured = d.poured;
    this.cure = d.cure;
    this.baseY = d.baseY;
    this.placed = new Set(d.placed);
    this.tier = d.tier;
    if (this.stage !== 'planum') {
      this.footGhost.visible = false;
      if (this.stage === 'beton' || this.stage === 'haerten') this.makeFormwork();
      if (d.bid >= 0) this.building = blds[d.bid];
    }
    this.updateGhostPos();
  }

  status() {
    switch (this.stage) {
      case 'planum':
        return { title: `${this.design.name}: Baugrund planieren`, detail: `Höhenabweichung ±${this.dev.toFixed(2)} m (Ziel ≤ 0,15 m)`, prog: clamp(1 - (this.dev - 0.15) / 1.2, 0, 1) };
      case 'beton':
        return { title: `${this.design.name}: Bodenplatte betonieren`, detail: `${this.poured.toFixed(1)} / ${this.need.toFixed(1)} m³ Beton`, prog: this.poured / this.need };
      case 'haerten':
        return { title: `${this.design.name}: Beton härtet aus`, detail: `noch ${Math.ceil(this.cure)} s (Zeitraffer)`, prog: 1 - this.cure / 20 };
      case 'montage': {
        const tot = this.elementsDef.length;
        const next = this.elementsDef.find((d, i) => d.tier === this.tier && !this.placed.has(i));
        return { title: `${this.design.name}: Montage ${this.placed.size}/${tot}`, detail: next ? `Nächstes Teil z. B.: ${next.name} (${next.mass.toFixed(1)} t)` : '', prog: this.placed.size / tot };
      }
      default:
        return { title: `${this.design.name}: fertig`, detail: '', prog: 1 };
    }
  }
}
