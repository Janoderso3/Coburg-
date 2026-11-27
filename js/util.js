// Hilfsfunktionen: Mathematik, Rauschen, Farben
export const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
export const lerp = (a, b, t) => a + (b - a) * t;
export const smoothstep = (a, b, x) => {
  const t = clamp((x - a) / (b - a), 0, 1);
  return t * t * (3 - 2 * t);
};
// Annäherung eines Wertes an ein Ziel mit begrenzter Rate
export const approach = (v, target, rate) => {
  if (v < target) return Math.min(target, v + rate);
  return Math.max(target, v - rate);
};
// Exponentielle Glättung, bildratenunabhängig
export const damp = (v, target, lambda, dt) => lerp(v, target, 1 - Math.exp(-lambda * dt));
export const wrapAngle = (a) => {
  while (a > Math.PI) a -= Math.PI * 2;
  while (a < -Math.PI) a += Math.PI * 2;
  return a;
};
export const dampAngle = (a, target, lambda, dt) => a + wrapAngle(target - a) * (1 - Math.exp(-lambda * dt));

// Deterministischer Zufall
export function rng(seed = 12345) {
  let s = seed % 2147483647;
  if (s <= 0) s += 2147483646;
  return () => (s = (s * 16807) % 2147483647) / 2147483647;
}

// 2D-Value-Noise mit glatter Interpolation
const PERM = new Uint8Array(512);
(() => {
  const r = rng(1337);
  const p = [];
  for (let i = 0; i < 256; i++) p.push(i);
  for (let i = 255; i > 0; i--) {
    const j = Math.floor(r() * (i + 1));
    [p[i], p[j]] = [p[j], p[i]];
  }
  for (let i = 0; i < 512; i++) PERM[i] = p[i & 255];
})();
function hash2(x, y) {
  return PERM[(PERM[x & 255] + y) & 511] / 255;
}
export function noise2(x, y) {
  const xi = Math.floor(x), yi = Math.floor(y);
  const xf = x - xi, yf = y - yi;
  const u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
  const a = hash2(xi, yi), b = hash2(xi + 1, yi), c = hash2(xi, yi + 1), d = hash2(xi + 1, yi + 1);
  return lerp(lerp(a, b, u), lerp(c, d, u), v) * 2 - 1;
}
export function fbm(x, y, oct = 4) {
  let s = 0, a = 0.5, f = 1;
  for (let i = 0; i < oct; i++) {
    s += a * noise2(x * f, y * f);
    f *= 2.03;
    a *= 0.5;
  }
  return s;
}

// sRGB-Hex -> lineare THREE.Color (für korrektes Licht mit sRGB-Ausgabe)
export function col(hex) {
  return new THREE.Color(hex).convertSRGBToLinear();
}
export function srgbToLinear(c) {
  return c < 0.04045 ? c * 0.0773993808 : Math.pow(c * 0.9478672986 + 0.0521327014, 2.4);
}

// Canvas-Textur mit Rauschen (Detail für Boden, Beton, etc.)
export function noiseTexture(size = 256, opts = {}) {
  const { base = 200, amp = 55, seams = false, scale = 1, seed = 7 } = opts;
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const g = c.getContext('2d');
  const img = g.createImageData(size, size);
  const r = rng(seed);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      // kachelbares Rauschen durch Torus-Abbildung
      const nx = x / size, ny = y / size;
      let n = 0;
      n += 0.5 * tileNoise(nx, ny, 8 * scale);
      n += 0.3 * tileNoise(nx, ny, 24 * scale);
      n += 0.2 * (r() * 2 - 1);
      let v = base + n * amp;
      if (seams) {
        const ex = Math.min(x, size - 1 - x), ey = Math.min(y, size - 1 - y);
        const e = Math.min(ex, ey);
        if (e < 3) v *= 0.72 + e * 0.07;
      }
      const i = (y * size + x) * 4;
      img.data[i] = img.data[i + 1] = img.data[i + 2] = clamp(v, 0, 255);
      img.data[i + 3] = 255;
    }
  }
  g.putImageData(img, 0, 0);
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.anisotropy = 4;
  return t;
}
function tileNoise(nx, ny, f) {
  // periodisches Rauschen: Interpolation auf ganzzahligem Gitter mit Periode f
  const x = nx * f, y = ny * f;
  const xi = Math.floor(x), yi = Math.floor(y);
  const xf = x - xi, yf = y - yi;
  const u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
  const m = (k) => ((k % f) + f) % f;
  const a = hash2(m(xi), m(yi)), b = hash2(m(xi + 1), m(yi)), c = hash2(m(xi), m(yi + 1)), d = hash2(m(xi + 1), m(yi + 1));
  return lerp(lerp(a, b, u), lerp(c, d, u), v) * 2 - 1;
}

// Weicher runder Partikel-Sprite
export function softDotTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d');
  const gr = g.createRadialGradient(32, 32, 0, 32, 32, 32);
  gr.addColorStop(0, 'rgba(255,255,255,1)');
  gr.addColorStop(0.4, 'rgba(255,255,255,0.55)');
  gr.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = gr;
  g.fillRect(0, 0, 64, 64);
  return new THREE.CanvasTexture(c);
}

// Abstand Punkt zu Strecke in XZ
export function distPointSeg2(px, pz, ax, az, bx, bz) {
  const dx = bx - ax, dz = bz - az;
  const l2 = dx * dx + dz * dz;
  let t = l2 > 0 ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0;
  t = clamp(t, 0, 1);
  const qx = ax + dx * t - px, qz = az + dz * t - pz;
  return Math.sqrt(qx * qx + qz * qz);
}

export const V3 = () => new THREE.Vector3();
export const tmpV = [0, 1, 2, 3, 4, 5, 6, 7].map(() => new THREE.Vector3());
