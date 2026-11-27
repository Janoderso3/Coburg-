// Partikel: fallendes Material (trägt echtes Volumen), Staub und Abgas
import { softDotTexture, col } from './util.js';
import { MAT } from './terrain.js';

const MAT_COLOR = {
  [MAT.SOIL]: [0x6b4d33, 0x7a5a3c, 0x5a4029],
  [MAT.RUBBLE]: [0x8f8a84, 0xa09890, 0x7a6058, 0x9a5a44],
  [MAT.GRAVEL]: [0x9d9a92, 0x8a877f],
  [MAT.CONCRETE]: [0x9c9c98, 0xa8a8a2],
};

export class SoilParticles {
  constructor(world, max = 700) {
    this.world = world;
    this.max = max;
    const geo = new THREE.IcosahedronGeometry(1, 0);
    const mat = new THREE.MeshLambertMaterial({ color: 0xffffff });
    this.mesh = new THREE.InstancedMesh(geo, mat, max);
    this.mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    this.mesh.castShadow = false;
    this.mesh.frustumCulled = false;
    this.mesh.count = 0;
    const c = new THREE.Color();
    for (let i = 0; i < max; i++) this.mesh.setColorAt(i, c.set(0xffffff));
    world.scene.add(this.mesh);
    this.p = [];
    this.m4 = new THREE.Matrix4();
    this.q = new THREE.Quaternion();
    this.e = new THREE.Euler();
    this.s = new THREE.Vector3();
    this.v = new THREE.Vector3();
    this.colorCache = {};
  }

  // vol = echtes Volumen in m³, das bei der Landung abgelegt wird
  spawn(x, y, z, vx, vy, vz, vol, mat, size) {
    if (this.p.length >= this.max) {
      // ältestes Teilchen sofort ablegen
      const o = this.p.shift();
      this.land(o);
    }
    const sz = size || Math.max(0.06, Math.min(0.22, Math.cbrt(Math.max(vol, 0.0002)) * 0.6));
    const cols = MAT_COLOR[mat] || MAT_COLOR[MAT.SOIL];
    this.p.push({ x, y, z, vx, vy, vz, vol, mat, sz, rx: Math.random() * 6, ry: Math.random() * 6, life: 0, col: cols[(Math.random() * cols.length) | 0] });
  }

  land(o) {
    if (o.vol > 0) this.world.terrain.deposit(o.x, o.z, o.vol, o.mat, 0.32);
  }

  update(dt) {
    const { terrain, catchers } = this.world;
    const arr = this.p;
    let n = 0;
    for (let i = 0; i < arr.length; i++) {
      const o = arr[i];
      o.vy -= 9.81 * dt;
      o.vx *= 0.995; o.vz *= 0.995;
      o.x += o.vx * dt; o.y += o.vy * dt; o.z += o.vz * dt;
      o.life += dt;
      o.rx += dt * 5; o.ry += dt * 3;
      let gone = false;
      // Auffangbehälter (Kippermulde, Schalung, ...)
      if (o.vy < 0) {
        for (let c = 0; c < catchers.length; c++) {
          if (catchers[c].tryCatch(o)) { gone = true; break; }
        }
      }
      if (!gone) {
        const g = terrain.height(o.x, o.z);
        if (o.y <= g || o.life > 6 || !terrain.inside(o.x, o.z, 0.6)) {
          if (terrain.inside(o.x, o.z, 0.6)) this.land(o);
          gone = true;
          if (o.vol > 0.01 && Math.random() < 0.3) this.world.effects.dust(o.x, g + 0.1, o.z, 1, o.mat === MAT.SOIL ? 0x8a7458 : 0x9a9690);
        }
      }
      if (!gone) arr[n++] = o;
    }
    arr.length = n;
    // Instanzen schreiben
    const m = this.mesh;
    const c = new THREE.Color();
    for (let i = 0; i < n; i++) {
      const o = arr[i];
      this.e.set(o.rx, o.ry, 0);
      this.q.setFromEuler(this.e);
      this.s.set(o.sz, o.sz * 0.8, o.sz);
      this.v.set(o.x, o.y, o.z);
      this.m4.compose(this.v, this.q, this.s);
      m.setMatrixAt(i, this.m4);
      let lc = this.colorCache[o.col];
      if (!lc) lc = this.colorCache[o.col] = col(o.col);
      m.setColorAt(i, lc);
    }
    m.count = n;
    m.instanceMatrix.needsUpdate = true;
    if (m.instanceColor) m.instanceColor.needsUpdate = true;
  }
}

// Staub & Rauch als weiche Punkt-Sprites
export class Puffs {
  constructor(world, max = 900) {
    this.world = world;
    this.max = max;
    const g = new THREE.BufferGeometry();
    this.pos = new Float32Array(max * 3);
    this.size = new Float32Array(max);
    this.alpha = new Float32Array(max);
    this.colr = new Float32Array(max * 3);
    g.setAttribute('position', new THREE.BufferAttribute(this.pos, 3).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('size', new THREE.BufferAttribute(this.size, 1).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('alpha', new THREE.BufferAttribute(this.alpha, 1).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('pcol', new THREE.BufferAttribute(this.colr, 3).setUsage(THREE.DynamicDrawUsage));
    this.uniforms = { map: { value: softDotTexture() }, scale: { value: 400 } };
    const mat = new THREE.ShaderMaterial({
      uniforms: this.uniforms,
      vertexShader: `attribute float size; attribute float alpha; attribute vec3 pcol; uniform float scale; varying float vA; varying vec3 vC;
        void main(){ vec4 mv = modelViewMatrix * vec4(position,1.0); gl_PointSize = size * scale / max(0.5, -mv.z); gl_Position = projectionMatrix * mv; vA = alpha; vC = pcol; }`,
      fragmentShader: `uniform sampler2D map; varying float vA; varying vec3 vC;
        void main(){ vec4 t = texture2D(map, gl_PointCoord); float a = t.a * vA; if (a < 0.01) discard; gl_FragColor = vec4(vC, a); }`,
      transparent: true,
      depthWrite: false,
    });
    this.points = new THREE.Points(g, mat);
    this.points.frustumCulled = false;
    this.geo = g;
    world.scene.add(this.points);
    this.p = [];
  }

  emit(x, y, z, vx, vy, vz, size, grow, life, color, alpha = 0.5) {
    if (this.p.length >= this.max) this.p.shift();
    const c = new THREE.Color(color);
    this.p.push({ x, y, z, vx, vy, vz, s: size, grow, life, t: 0, r: c.r, g: c.g, b: c.b, a: alpha });
  }

  update(dt) {
    const arr = this.p;
    let n = 0;
    for (let i = 0; i < arr.length; i++) {
      const o = arr[i];
      o.t += dt;
      if (o.t >= o.life) continue;
      o.x += o.vx * dt; o.y += o.vy * dt; o.z += o.vz * dt;
      o.vx *= 0.97; o.vz *= 0.97; o.vy = o.vy * 0.97 + 0.05 * dt;
      // leichter Wind
      o.x += 0.35 * dt; o.z += 0.12 * dt;
      o.s += o.grow * dt;
      arr[n] = o;
      const k = n * 3;
      this.pos[k] = o.x; this.pos[k + 1] = o.y; this.pos[k + 2] = o.z;
      this.colr[k] = o.r; this.colr[k + 1] = o.g; this.colr[k + 2] = o.b;
      this.size[n] = o.s;
      const f = o.t / o.life;
      this.alpha[n] = o.a * Math.min(1, o.t * 6) * (1 - f) * (1 - f);
      n++;
    }
    arr.length = n;
    this.geo.setDrawRange(0, n);
    for (const k of ['position', 'size', 'alpha', 'pcol']) this.geo.attributes[k].needsUpdate = true;
  }
}

export class Effects {
  constructor(world) {
    this.world = world;
    this.puffs = new Puffs(world);
  }
  setScale(h, fov) {
    this.puffs.uniforms.scale.value = h / (2 * Math.tan((fov * Math.PI) / 360));
  }
  dust(x, y, z, n = 3, color = 0x9b8a70, size = 0.9) {
    for (let i = 0; i < n; i++) {
      this.puffs.emit(
        x + (Math.random() - 0.5) * 0.8, y + Math.random() * 0.3, z + (Math.random() - 0.5) * 0.8,
        (Math.random() - 0.5) * 0.8, 0.3 + Math.random() * 0.6, (Math.random() - 0.5) * 0.8,
        size * (0.6 + Math.random() * 0.6), 1.2, 2 + Math.random() * 1.8, color, 0.45);
    }
  }
  smoke(x, y, z, density = 0.3, dark = 0.3) {
    const g = Math.floor(90 - dark * 60);
    const c = (g << 16) | (g << 8) | g;
    this.puffs.emit(x, y, z, (Math.random() - 0.5) * 0.2, 1.2 + Math.random() * 0.5, (Math.random() - 0.5) * 0.2, 0.25, 0.9, 1.5 + density * 1.5, c, 0.18 + density * 0.4);
  }
  update(dt) {
    this.puffs.update(dt);
  }
}
