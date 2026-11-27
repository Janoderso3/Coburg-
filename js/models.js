// Prozedurale Modellbausteine: zusammengeführte Geometrien, Materialien, Ketten, Räder, Hydraulikzylinder
import { col } from './util.js';

const std = (hex, rough = 0.6, metal = 0.1, extra = {}) => new THREE.MeshStandardMaterial({ color: col(hex), roughness: rough, metalness: metal, ...extra });

export const MATS = {};
export function initMaterials() {
  MATS.yellow = std(0xe3a614, 0.5, 0.15);
  MATS.yellowDirty = std(0xc79a2a, 0.75, 0.1);
  MATS.orange = std(0xe2711d, 0.5, 0.15);
  MATS.white = std(0xe9e9e4, 0.5, 0.1);
  MATS.red = std(0xb3261e, 0.5, 0.15);
  MATS.blue = std(0x2a5a9c, 0.5, 0.2);
  MATS.dark = std(0x2d2d2d, 0.7, 0.3);
  MATS.black = std(0x161616, 0.85, 0.1);
  MATS.steel = std(0x8d8f91, 0.4, 0.7);
  MATS.chrome = std(0xd0d4d8, 0.2, 0.95);
  MATS.rubber = std(0x1b1b1b, 0.95, 0);
  MATS.glass = new THREE.MeshStandardMaterial({ color: col(0x1d2a33), roughness: 0.08, metalness: 0.6, transparent: true, opacity: 0.55 });
  MATS.light = new THREE.MeshStandardMaterial({ color: 0xffffff, emissive: col(0xfff1c8), emissiveIntensity: 0.8, roughness: 0.3 });
  MATS.tail = new THREE.MeshStandardMaterial({ color: col(0x550000), emissive: col(0xff2010), emissiveIntensity: 0.4 });
  MATS.beacon = new THREE.MeshStandardMaterial({ color: col(0xff8a00), emissive: col(0xff7a00), emissiveIntensity: 0.3, transparent: true, opacity: 0.9 });
  MATS.bucketWear = std(0x5a5650, 0.6, 0.5);
  MATS.concrete = std(0x9e9d98, 0.9, 0);
  MATS.drum = std(0xe8e8e2, 0.45, 0.2);
  MATS.wood = std(0x9a7448, 0.9, 0);
  MATS.green = std(0x3c6e3a, 0.8, 0);
  // Kettenglieder-Textur
  const c = document.createElement('canvas');
  c.width = 64; c.height = 64;
  const g = c.getContext('2d');
  g.fillStyle = '#2a2a2a'; g.fillRect(0, 0, 64, 64);
  g.fillStyle = '#4a4a48'; g.fillRect(0, 0, 18, 64);
  g.fillStyle = '#3a3a38'; g.fillRect(18, 0, 6, 64);
  g.fillStyle = '#111'; g.fillRect(60, 0, 4, 64);
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  MATS.trackTex = t;
  // Reifenprofil
  const c2 = document.createElement('canvas');
  c2.width = 64; c2.height = 64;
  const g2 = c2.getContext('2d');
  g2.fillStyle = '#1e1e1e'; g2.fillRect(0, 0, 64, 64);
  g2.fillStyle = '#0c0c0c';
  for (let i = 0; i < 4; i++) {
    g2.beginPath();
    g2.moveTo(i * 16, 0); g2.lineTo(i * 16 + 8, 0); g2.lineTo(i * 16 + 16, 32); g2.lineTo(i * 16 + 8, 32); g2.fill();
    g2.beginPath();
    g2.moveTo(i * 16 + 16, 32); g2.lineTo(i * 16 + 8, 32); g2.lineTo(i * 16, 64); g2.lineTo(i * 16 + 8, 64); g2.fill();
  }
  const t2 = new THREE.CanvasTexture(c2);
  t2.wrapS = t2.wrapT = THREE.RepeatWrapping;
  MATS.tread = new THREE.MeshStandardMaterial({ map: t2, roughness: 0.95, metalness: 0 });
}

// Zusammenführen mehrerer BufferGeometrien (gleiche Attribute: position, normal, uv)
export function mergeGeometries(list) {
  let nv = 0, ni = 0;
  const geoms = list.map((g) => (g.index ? g : indexify(g)));
  for (const g of geoms) {
    nv += g.attributes.position.count;
    ni += g.index.count;
  }
  const pos = new Float32Array(nv * 3), nor = new Float32Array(nv * 3), uv = new Float32Array(nv * 2);
  const hasCol = geoms.some((g) => g.attributes.color);
  const colr = hasCol ? new Float32Array(nv * 3) : null;
  const idx = nv > 65535 ? new Uint32Array(ni) : new Uint16Array(ni);
  let ov = 0, oi = 0;
  for (const g of geoms) {
    const n = g.attributes.position.count;
    pos.set(g.attributes.position.array, ov * 3);
    nor.set(g.attributes.normal.array, ov * 3);
    if (g.attributes.uv) uv.set(g.attributes.uv.array, ov * 2);
    if (colr) {
      if (g.attributes.color) colr.set(g.attributes.color.array, ov * 3);
      else colr.fill(1, ov * 3, (ov + n) * 3);
    }
    const ia = g.index.array;
    for (let i = 0; i < ia.length; i++) idx[oi + i] = ia[i] + ov;
    ov += n;
    oi += ia.length;
  }
  const out = new THREE.BufferGeometry();
  out.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  out.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  out.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  if (colr) out.setAttribute('color', new THREE.BufferAttribute(colr, 3));
  out.setIndex(new THREE.BufferAttribute(idx, 1));
  out.computeBoundingSphere();
  return out;
}
function indexify(g) {
  const n = g.attributes.position.count;
  const a = new Uint32Array(n);
  for (let i = 0; i < n; i++) a[i] = i;
  g.setIndex(new THREE.BufferAttribute(a, 1));
  if (!g.attributes.uv) g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(n * 2), 2));
  return g;
}

const _m = new THREE.Matrix4();
const _q = new THREE.Quaternion();
const _e = new THREE.Euler();
const _p = new THREE.Vector3();
const _s = new THREE.Vector3(1, 1, 1);

// Sammelt Grundkörper je Material und erzeugt am Ende wenige Meshes
export class PartBuilder {
  constructor() {
    this.map = new Map();
  }
  add(geo, mat, x = 0, y = 0, z = 0, rx = 0, ry = 0, rz = 0) {
    _e.set(rx, ry, rz);
    _q.setFromEuler(_e);
    _p.set(x, y, z);
    _m.compose(_p, _q, _s);
    geo.applyMatrix4(_m);
    if (!this.map.has(mat)) this.map.set(mat, []);
    this.map.get(mat).push(geo);
    return this;
  }
  box(mat, w, h, d, x, y, z, rx, ry, rz) {
    return this.add(new THREE.BoxGeometry(w, h, d), mat, x, y, z, rx, ry, rz);
  }
  cyl(mat, rt, rb, h, seg, x, y, z, rx, ry, rz) {
    return this.add(new THREE.CylinderGeometry(rt, rb, h, seg), mat, x, y, z, rx, ry, rz);
  }
  // Zylinder entlang X-Achse (Achsen, Räder)
  cylX(mat, r, len, seg, x, y, z) {
    return this.add(new THREE.CylinderGeometry(r, r, len, seg), mat, x, y, z, 0, 0, Math.PI / 2);
  }
  // Balken zwischen zwei Punkten in YZ-Ebene (für Ausleger etc.)
  beamYZ(mat, w, h, y0, z0, y1, z1, x = 0) {
    const dy = y1 - y0, dz = z1 - z0;
    const len = Math.sqrt(dy * dy + dz * dz);
    const ang = Math.atan2(dy, dz);
    return this.add(new THREE.BoxGeometry(w, h, len), mat, x, (y0 + y1) / 2, (z0 + z1) / 2, -ang, 0, 0);
  }
  // Extrudiertes Profil (YZ-Ebene) mit Breite in X
  profileX(mat, pts, width, x = 0) {
    const sh = new THREE.Shape();
    sh.moveTo(pts[0][0], pts[0][1]);
    for (let i = 1; i < pts.length; i++) sh.lineTo(pts[i][0], pts[i][1]);
    sh.closePath();
    const g = new THREE.ExtrudeGeometry(sh, { depth: width, bevelEnabled: false });
    // Form (u,v) -> (z,y), Extrusion -> x
    g.rotateY(-Math.PI / 2);
    g.translate(x + width / 2, 0, 0);
    return this.add(g, mat);
  }
  build(parent, cast = true, receive = false) {
    const meshes = [];
    for (const [mat, list] of this.map) {
      const g = mergeGeometries(list);
      const m = new THREE.Mesh(g, mat);
      m.castShadow = cast && !mat.transparent;
      m.receiveShadow = receive;
      parent.add(m);
      meshes.push(m);
    }
    this.map.clear();
    return meshes;
  }
}

// Kettenband als umlaufendes Band (Stadionform); UV.u läuft entlang des Bandes
export function trackGeometry(len, rad, width, pitch = 0.22) {
  const pts = [];
  const half = len / 2 - rad;
  const segs = 10;
  // Unterseite hinten -> vorne
  pts.push([-half, -rad, 0, -1]);
  pts.push([half, -rad, 0, -1]);
  for (let i = 1; i < segs; i++) {
    const a = -Math.PI / 2 + (Math.PI * i) / segs;
    pts.push([half + Math.cos(a) * rad, Math.sin(a) * rad, Math.cos(a), Math.sin(a)]);
  }
  pts.push([half, rad, 0, 1]);
  pts.push([-half, rad, 0, 1]);
  for (let i = 1; i < segs; i++) {
    const a = Math.PI / 2 + (Math.PI * i) / segs;
    pts.push([-half + Math.cos(a) * rad, Math.sin(a) * rad, Math.cos(a), Math.sin(a)]);
  }
  pts.push([-half, -rad, 0, -1]);
  const n = pts.length;
  const pos = [], nor = [], uv = [], idx = [];
  let s = 0;
  for (let i = 0; i < n; i++) {
    if (i > 0) {
      const dx = pts[i][0] - pts[i - 1][0], dy = pts[i][1] - pts[i - 1][1];
      s += Math.sqrt(dx * dx + dy * dy);
    }
    const [z, y, nz, ny] = pts[i];
    for (const x of [-width / 2, width / 2]) {
      pos.push(x, y, z);
      nor.push(0, ny, nz);
      uv.push(s / pitch, x > 0 ? 1 : 0);
    }
  }
  for (let i = 0; i < n - 1; i++) {
    const a = i * 2, b = a + 1, c = a + 2, d = a + 3;
    idx.push(a, c, b, b, c, d);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(nor, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(idx);
  return g;
}

// Komplettes Kettenlaufwerk einer Seite; gibt Mesh mit eigener Textur zurück
export function makeTrack(parent, x, y, len, rad, width) {
  const tex = MATS.trackTex.clone();
  tex.needsUpdate = true;
  const mat = new THREE.MeshStandardMaterial({ map: tex, roughness: 0.9, metalness: 0.3, side: THREE.DoubleSide });
  const m = new THREE.Mesh(trackGeometry(len, rad, width), mat);
  m.position.set(x, y, 0);
  m.castShadow = true;
  parent.add(m);
  return { mesh: m, tex, travel: 0 };
}

// Rad mit Reifen und Felge; dreht sich um X
export function makeWheel(radius, width, rimMat = MATS.yellow, rimR = 0.55) {
  const g = new THREE.Group();
  const spin = new THREE.Group();
  g.add(spin);
  const tire = new THREE.Mesh(new THREE.CylinderGeometry(radius, radius, width, 22, 1, true), MATS.tread);
  tire.rotation.z = Math.PI / 2;
  tire.material.map.repeat.set(6, 1);
  const side = new THREE.Mesh(new THREE.CylinderGeometry(radius * 0.98, radius * 0.98, width * 0.94, 22), MATS.rubber);
  side.rotation.z = Math.PI / 2;
  const rim = new THREE.Mesh(new THREE.CylinderGeometry(radius * rimR, radius * rimR, width * 1.0, 14), rimMat);
  rim.rotation.z = Math.PI / 2;
  const hub = new THREE.Mesh(new THREE.CylinderGeometry(radius * 0.18, radius * 0.22, width * 1.12, 8), MATS.dark);
  hub.rotation.z = Math.PI / 2;
  for (const m of [tire, side, rim, hub]) {
    m.castShadow = true;
    spin.add(m);
  }
  g.userData.spin = spin;
  return g;
}

// Hydraulikzylinder zwischen zwei Weltpunkten
export class HydCylinder {
  constructor(scene, rBarrel = 0.09, rRod = 0.05, barrelLen = 1.0, mat = MATS.yellow) {
    this.g = new THREE.Group();
    this.barrel = new THREE.Mesh(new THREE.CylinderGeometry(rBarrel, rBarrel, 1, 10), mat);
    this.rod = new THREE.Mesh(new THREE.CylinderGeometry(rRod, rRod, 1, 8), MATS.chrome);
    this.barrel.castShadow = this.rod.castShadow = true;
    this.g.add(this.barrel, this.rod);
    this.len = barrelLen;
    scene.add(this.g);
    this.up = new THREE.Vector3(0, 1, 0);
    this.d = new THREE.Vector3();
  }
  update(a, b) {
    this.d.subVectors(b, a);
    const L = this.d.length();
    this.d.divideScalar(L || 1);
    this.g.position.copy(a);
    this.g.quaternion.setFromUnitVectors(this.up, this.d);
    const bl = Math.min(this.len, L * 0.95);
    this.barrel.scale.set(1, bl, 1);
    this.barrel.position.set(0, bl / 2, 0);
    const rl = Math.max(0.05, L - bl * 0.85);
    this.rod.scale.set(1, rl, 1);
    this.rod.position.set(0, L - rl / 2, 0);
  }
  setVisible(v) {
    this.g.visible = v;
  }
}

// Rundumkennleuchte
export function makeBeacon(parent, x, y, z) {
  const base = new THREE.Mesh(new THREE.CylinderGeometry(0.09, 0.1, 0.05, 10), MATS.black);
  base.position.set(x, y, z);
  const dome = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.085, 0.14, 10), MATS.beacon.clone());
  dome.position.set(x, y + 0.09, z);
  parent.add(base, dome);
  return dome;
}

// Geometrie aus vielen Quadern mit Vertexfarben (für Fertigteile, Gebäude-Vorschau)
export function boxesGeometry(boxes) {
  const list = [];
  const c = new THREE.Color();
  for (const b of boxes) {
    const g = new THREE.BoxGeometry(b.sx, b.sy, b.sz);
    _e.set(b.pitch || 0, b.yaw || 0, 0, 'YXZ');
    _q.setFromEuler(_e);
    _p.set(b.x, b.y, b.z);
    _m.compose(_p, _q, _s);
    g.applyMatrix4(_m);
    c.copy(b.color);
    const n = g.attributes.position.count;
    const arr = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) { arr[i * 3] = c.r; arr[i * 3 + 1] = c.g; arr[i * 3 + 2] = c.b; }
    g.setAttribute('color', new THREE.BufferAttribute(arr, 3));
    list.push(g);
  }
  return mergeGeometries(list);
}

// Text-Schild als Sprite/Plane
export function textTexture(text, opts = {}) {
  const { w = 512, h = 128, bg = '#f4f1e8', fg = '#1b1b1b', font = 'bold 64px sans-serif', border = '#1b1b1b' } = opts;
  const c = document.createElement('canvas');
  c.width = w; c.height = h;
  const g = c.getContext('2d');
  g.fillStyle = bg; g.fillRect(0, 0, w, h);
  g.strokeStyle = border; g.lineWidth = 8; g.strokeRect(4, 4, w - 8, h - 8);
  g.fillStyle = fg; g.font = font; g.textAlign = 'center'; g.textBaseline = 'middle';
  const lines = String(text).split('\n');
  const lh = h / (lines.length + 0.4);
  lines.forEach((l, i) => g.fillText(l, w / 2, h / 2 + (i - (lines.length - 1) / 2) * lh));
  const t = new THREE.CanvasTexture(c);
  t.encoding = THREE.sRGBEncoding;
  return t;
}
