// Turmdrehkran mit pendelnder Last und Seilbagger mit Abrissbirne
import { clamp, approach, damp } from './util.js';
import { MATS, PartBuilder, makeTrack, HydCylinder, makeBeacon } from './models.js';
import { Vehicle } from './vehicles.js';

const _v = new THREE.Vector3();
const _v2 = new THREE.Vector3();
const _q = new THREE.Quaternion();
const _m = new THREE.Matrix4();
const _s = new THREE.Vector3();
const YAX = new THREE.Vector3(0, 1, 0);

// Fachwerkstäbe als eine instanzierte Geometrie
function lattice(parent, members, thick, mat) {
  const mesh = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1, 1), mat, members.length);
  members.forEach(([a, b, t], i) => {
    const d = _v.subVectors(b, a);
    const L = d.length();
    _q.setFromUnitVectors(YAX, d.normalize());
    _s.set(t || thick, L, t || thick);
    _m.compose(_v2.addVectors(a, b).multiplyScalar(0.5), _q, _s);
    mesh.setMatrixAt(i, _m);
  });
  mesh.castShadow = true;
  mesh.receiveShadow = false;
  parent.add(mesh);
  return mesh;
}
const P = (x, y, z) => new THREE.Vector3(x, y, z);

// Seil zwischen zwei Punkten
class Rope {
  constructor(scene, r = 0.022) {
    this.m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, 1, 5), MATS.dark);
    scene.add(this.m);
  }
  set(a, b) {
    const d = _v.subVectors(b, a);
    const L = d.length();
    this.m.position.addVectors(a, b).multiplyScalar(0.5);
    this.m.quaternion.setFromUnitVectors(YAX, d.divideScalar(L || 1));
    this.m.scale.set(1, L, 1);
  }
}

// Punktmasse am Seil (Verlet-Integration mit Seilbedingung)
export class Pendulum {
  constructor(world, start) {
    this.world = world;
    this.p = start.clone();
    this.pp = start.clone();
    this.vel = new THREE.Vector3();
    this.resting = false;
  }
  // anchor: Aufhängepunkt, L: Seillänge, minY(x,z): tiefster erlaubter Punkt
  step(dt, anchor, L, minYFn, damping = 0.9985) {
    const sub = 4;
    const h = dt / sub;
    for (let i = 0; i < sub; i++) {
      const vx = (this.p.x - this.pp.x) * damping, vy = (this.p.y - this.pp.y) * damping, vz = (this.p.z - this.pp.z) * damping;
      this.pp.copy(this.p);
      this.p.x += vx;
      this.p.y += vy - 9.81 * h * h;
      this.p.z += vz;
      // Seil ist undehnbar
      const d = _v.subVectors(this.p, anchor);
      const len = d.length();
      if (len > L) this.p.copy(anchor).addScaledVector(d, L / len);
      // Boden
      const my = minYFn(this.p.x, this.p.z);
      this.resting = false;
      if (this.p.y < my) {
        this.p.y = my;
        this.resting = true;
        // Reibung
        this.pp.x = this.p.x - (this.p.x - this.pp.x) * 0.3;
        this.pp.z = this.p.z - (this.p.z - this.pp.z) * 0.3;
        if (this.pp.y < this.p.y) this.pp.y = this.p.y;
      }
    }
    this.vel.subVectors(this.p, this.pp).divideScalar(h);
  }
  setVel(v, dt) {
    this.pp.copy(this.p).addScaledVector(v, -dt / 4);
  }
}

// ---------------- Turmdrehkran ----------------
export class TowerCrane {
  constructor(world, x, z) {
    this.world = world;
    this.name = 'Turmdrehkran';
    this.short = 'Kran';
    this.isCrane = true;
    this.x = x;
    this.z = z;
    const T = world.terrain;
    this.baseY = T.height(x, z);
    this.H = 30; // Hakenhöhe unter Ausleger
    this.jibLen = 40;
    this.slew = -0.79; this.slewV = 0;
    this.r = 15; this.rV = 0;
    this.L = 12; this.LV = 0;
    this.load = null;
    this.active = false;
    this.rpm = 0;
    this.hyd = 0;
    this.releaseArm = 0;
    this.pos = new THREE.Vector3(x, this.baseY, z);
    this.yaw = 0;
    this.build();
    const t = this.trolleyWorld(new THREE.Vector3());
    this.pend = new Pendulum(world, t.clone().add(P(0, -this.L, 0)));
    this.hookOff = 0.9;
    this.update(0, null);
  }

  build() {
    const sc = this.world.scene;
    const mat = MATS.yellow;
    const g = (this.group = new THREE.Group());
    g.position.set(this.x, this.baseY, this.z);
    sc.add(g);
    // Fundament mit Ballast
    const pb = new PartBuilder();
    pb.box(MATS.concrete, 5, 0.8, 5, 0, 0.4, 0);
    for (const [bx, bz] of [[-1.6, -1.6], [1.6, -1.6], [-1.6, 1.6], [1.6, 1.6]]) pb.box(MATS.concrete, 1.4, 0.9, 1.4, bx, 1.25, bz);
    pb.build(g);
    // Turm
    const w = 0.85;
    const mem = [];
    const seg = 2.5;
    const top = this.H + 1;
    for (let y = 0.8; y < top - 0.01; y += seg) {
      const y1 = Math.min(top, y + seg);
      const c = [[-w, -w], [w, -w], [w, w], [-w, w]];
      for (let i = 0; i < 4; i++) {
        const [ax, az] = c[i], [bx, bz] = c[(i + 1) % 4];
        mem.push([P(ax, y, az), P(ax, y1, az), 0.16]);
        mem.push([P(ax, y1, az), P(bx, y1, bz)]);
        mem.push([P(ax, y, az), P(bx, y1, bz)]);
      }
    }
    lattice(g, mem, 0.08, mat);
    // Drehteil
    const sl = (this.slewG = new THREE.Group());
    sl.position.y = top;
    g.add(sl);
    const m2 = [];
    // Turmspitze
    const apex = P(0, 7, 0);
    for (const [x, z] of [[-w, -w], [w, -w], [w, w], [-w, w]]) m2.push([P(x, 0.4, z), apex, 0.14]);
    // Ausleger (Dreiecksquerschnitt), +Z
    const L = this.jibLen + 1.5;
    const step = 2;
    for (let z = 1; z < L - 0.01; z += step) {
      const z1 = Math.min(L, z + step);
      m2.push([P(-0.65, 0, z), P(-0.65, 0, z1), 0.12]);
      m2.push([P(0.65, 0, z), P(0.65, 0, z1), 0.12]);
      m2.push([P(0, 1.4, z), P(0, 1.4, z1), 0.12]);
      m2.push([P(-0.65, 0, z1), P(0.65, 0, z1)]);
      m2.push([P(-0.65, 0, z), P(0, 1.4, z1)]);
      m2.push([P(0.65, 0, z), P(0, 1.4, z1)]);
      m2.push([P(-0.65, 0, z), P(0.65, 0, z1)]);
    }
    // Gegenausleger
    for (let z = -1; z > -13; z -= step) {
      const z1 = z - step;
      m2.push([P(-0.9, 0.3, z), P(-0.9, 0.3, z1), 0.14]);
      m2.push([P(0.9, 0.3, z), P(0.9, 0.3, z1), 0.14]);
      m2.push([P(-0.9, 0.3, z1), P(0.9, 0.3, z1)]);
      m2.push([P(-0.9, 0.3, z), P(0.9, 0.3, z1)]);
    }
    // Abspannungen
    m2.push([apex, P(0, 1.4, 24), 0.06]);
    m2.push([apex, P(0, 1.4, 12), 0.06]);
    m2.push([apex, P(-0.9, 0.3, -12), 0.06]);
    m2.push([apex, P(0.9, 0.3, -12), 0.06]);
    // Verbindung Turm-Ausleger
    m2.push([P(-w, 0, w), P(-0.65, 0, 1.2), 0.14]);
    m2.push([P(w, 0, w), P(0.65, 0, 1.2), 0.14]);
    m2.push([P(-w, 0, -w), P(-0.9, 0.3, -1.2), 0.14]);
    m2.push([P(w, 0, -w), P(0.9, 0.3, -1.2), 0.14]);
    lattice(sl, m2, 0.07, mat);
    pb.box(MATS.concrete, 2.3, 2.2, 2.6, 0, -0.9, -11.6); // Gegengewicht
    pb.box(MATS.dark, 1.4, 1.0, 1.6, 0, 0.9, -7.5); // Hubwerk
    pb.box(MATS.steel, 1.9, 0.08, 11.5, 0, 0.35, -6.5); // Laufsteg
    pb.box(mat, 1.8, 0.4, 1.8, 0, 0.2, 0); // Drehkranz
    // Führerhaus seitlich
    pb.box(MATS.white, 1.3, 1.9, 1.6, 1.55, 0.2, 1.2);
    pb.build(sl);
    const cabGlass = new THREE.Mesh(new THREE.BoxGeometry(1.32, 1.2, 1.62), MATS.glass);
    cabGlass.position.set(1.55, 0.45, 1.25);
    sl.add(cabGlass);
    this.beacon = makeBeacon(sl, 0, 7.05, 0);
    this.cabEye = P(1.55, 0.6, 1.9);
    // Laufkatze
    const tr = (this.trolleyG = new THREE.Group());
    sl.add(tr);
    pb.box(MATS.dark, 1.5, 0.35, 1.3, 0, -0.25, 0);
    pb.cylX(MATS.steel, 0.22, 1.2, 10, 0, -0.55, 0.3);
    pb.cylX(MATS.steel, 0.22, 1.2, 10, 0, -0.55, -0.3);
    pb.build(tr);
    // Unterflasche (Haken)
    const hk = (this.hookG = new THREE.Group());
    sc.add(hk);
    pb.box(MATS.red, 0.7, 0.8, 0.35, 0, 0.35, 0);
    pb.cylX(MATS.steel, 0.25, 0.4, 10, 0, 0.5, 0);
    pb.cyl(MATS.dark, 0.05, 0.05, 0.35, 6, 0, -0.15, 0);
    pb.build(hk);
    const hook = new THREE.Mesh(new THREE.TorusGeometry(0.15, 0.045, 6, 12, Math.PI * 1.4), MATS.dark);
    hook.position.set(0, -0.45, 0);
    hook.rotation.z = Math.PI * 0.8;
    hk.add(hook);
    this.ropes = [new Rope(sc), new Rope(sc)];
    this.slings = [0, 1, 2, 3].map(() => new Rope(sc, 0.015));
  }

  trolleyWorld(out) {
    const a = this.slew;
    return out.set(this.x + Math.sin(a) * this.r, this.baseY + this.H + 1 - 0.75, this.z + Math.cos(a) * this.r);
  }

  maxLoad(r = this.r) {
    // Traglastkurve: 10 t bis 14 m, dann konstantes Lastmoment 140 tm
    return Math.min(10, 140 / Math.max(r, 1));
  }

  controls() {
    const st = this.load ? 'Ablassen' : 'Einhängen';
    return {
      left: { up: 'Katze aus', down: 'Katze ein', left: 'Drehen L', right: 'Drehen R' },
      right: { up: 'Heben', down: 'Senken', left: '', right: '' },
      buttons: [{ id: 'hook', label: st, hint: 'Last' }],
    };
  }

  onButton(id) {
    if (id !== 'hook') return;
    const ui = this.world.ui;
    if (!this.load) {
      // nächstes Fertigteil am Haken?
      let best = null, bd = 2.2;
      for (const el of this.world.buildings.elements) {
        if (el.state !== 'stored' && el.state !== 'loose') continue;
        const lp = _v.set(el.pos.x, el.pos.y + el.liftY, el.pos.z);
        const d = lp.distanceTo(this.pend.p);
        if (d < bd) { bd = d; best = el; }
      }
      if (!best) {
        ui.toast('Kein Fertigteil in Reichweite des Hakens. Haken über den Anschlagpunkt bringen und absenken.', 'warn');
        return true;
      }
      if (best.mass > this.maxLoad() * 1.0) ui.toast(`Achtung: ${best.mass.toFixed(1)} t – zulässig bei ${this.r.toFixed(0)} m nur ${this.maxLoad().toFixed(1)} t!`, 'warn');
      this.load = best;
      best.state = 'hooked';
      if (best.trestle) { this.world.scene.remove(best.trestle); best.trestle = null; }
      this.hookOff = best.liftY;
      ui.toast(`${best.name} angeschlagen (${best.mass.toFixed(1)} t).`, 'ok');
      return true;
    }
    const el = this.load;
    const site = el.site;
    if (site && site.canPlace(el)) {
      site.place(el);
      this.load = null;
      return true;
    }
    const g = el.groundBelow();
    const above = el.pos.y - g;
    if (above > 0.25 && this.releaseArm <= 0) {
      this.releaseArm = 2.5;
      ui.toast(`Last schwebt ${above.toFixed(1)} m über Grund! Erneut tippen zum Ausklinken.`, 'warn');
      return true;
    }
    this.releaseArm = 0;
    el.state = 'loose';
    el.vy = Math.min(0, this.pend.vel.y);
    this.load = null;
    ui.toast(`${el.name} abgesetzt.`, 'info');
    return true;
  }

  update(dt, inp) {
    let sl = 0, tr = 0, ho = 0;
    if (inp) { sl = -inp.lx; tr = inp.ly; ho = inp.ry; }
    this.releaseArm = Math.max(0, this.releaseArm - dt);
    const mass = this.load ? this.load.mass : 0;
    const ml = this.maxLoad();
    const overload = mass > ml;
    if (overload && tr > 0) tr = 0;
    // Antriebe mit Frequenzumrichter: sanfte Rampen
    this.slewV = approach(this.slewV, sl * 0.11, dt * 0.045);
    this.rV = approach(this.rV, tr * 1.0, dt * 0.6);
    const hoistMax = mass > 0 ? 1.0 : 1.6;
    this.LV = approach(this.LV, -ho * hoistMax, dt * 1.5);
    if (overload && this.LV < 0 && this.pend.resting) this.LV = 0;
    this.slew += this.slewV * dt;
    this.r = clamp(this.r + this.rV * dt, 3.5, this.jibLen - 1);
    if (this.r <= 3.5 || this.r >= this.jibLen - 1) this.rV = 0;
    this.L = clamp(this.L + this.LV * dt, 1.5, this.H - 0.5 + (this.baseY - this.world.terrain.height(this.pend.p.x, this.pend.p.z)) + 4);
    this.slewG.rotation.y = this.slew;
    this.trolleyG.position.set(0, -0.05, this.r);
    const T = this.trolleyWorld(_v2.set(0, 0, 0)).clone();
    if (dt > 0) {
      const world = this.world;
      const load = this.load;
      const off = load ? this.hookOff : 0.55;
      const minY = (x, z) => {
        if (load) {
          const g = load.groundBelow(x, z, load.yaw, this.pend.p.y - off);
          return g + off;
        }
        return world.groundHeight(x, z, this.pend.p.y) + 0.55;
      };
      const damping = load ? 0.9975 : 0.9985;
      this.pend.step(dt, T, this.L, minY, damping);
      if (load) {
        load.pos.set(this.pend.p.x, this.pend.p.y - off, this.pend.p.z);
        // Monteure führen das Teil mit Leitseilen in Position
        const site = load.site;
        if (site && load.def.target) {
          const tgt = load.def.target;
          const dx = tgt.x - load.pos.x, dz = tgt.z - load.pos.z;
          const d = Math.sqrt(dx * dx + dz * dz);
          if (d < 8) {
            let dy = load.def.targetYaw - load.yaw;
            while (dy > Math.PI) dy -= Math.PI * 2;
            while (dy < -Math.PI) dy += Math.PI * 2;
            load.yaw += dy * Math.min(1, dt * 0.8);
          }
          if (d < 1.0 && load.pos.y - tgt.y < 1.2) {
            this.pend.p.x += dx * Math.min(1, dt * 1.5);
            this.pend.p.z += dz * Math.min(1, dt * 1.5);
            this.pend.pp.x += dx * Math.min(1, dt * 1.5);
            this.pend.pp.z += dz * Math.min(1, dt * 1.5);
          }
        }
        load.sync();
      }
    }
    // Darstellung
    const hp = this.pend.p;
    this.hookG.position.set(hp.x, hp.y + 0.05, hp.z);
    this.hookG.rotation.y = this.slew;
    const tip = _v2.copy(T);
    this.ropes[0].set(_v.set(tip.x + 0.25, tip.y - 0.4, tip.z), new THREE.Vector3(hp.x + 0.25, hp.y + 0.5, hp.z));
    this.ropes[1].set(_v.set(tip.x - 0.25, tip.y - 0.4, tip.z), new THREE.Vector3(hp.x - 0.25, hp.y + 0.5, hp.z));
    if (this.load) {
      const el = this.load;
      const c = Math.cos(el.yaw), s = Math.sin(el.yaw);
      const hx = el.halfX * 0.7, hz = el.halfZ * 0.7;
      const top = el.pos.y + el.size.y;
      [[hx, hz], [-hx, hz], [hx, -hz], [-hx, -hz]].forEach(([lx, lz], i) => {
        this.slings[i].m.visible = true;
        this.slings[i].set(new THREE.Vector3(hp.x, hp.y - 0.5, hp.z), new THREE.Vector3(el.pos.x + lx * c + lz * s, top, el.pos.z - lx * s + lz * c));
      });
    } else for (const s of this.slings) s.m.visible = false;
    this.hyd = Math.min(1, Math.abs(this.slewV) * 8 + Math.abs(this.rV) * 0.6 + Math.abs(this.LV) * 0.6);
    this.rpm = damp(this.rpm, this.hyd, 3, dt || 0.016);
    if (this.beacon) this.beacon.material.emissiveIntensity = this.active && Math.sin(performance.now() / 160) > 0.3 ? 2.5 : 0.15;
  }

  soundState() {
    // Elektroantrieb: kein Dieselmotor, nur Getriebe-/Motorsurren
    return { on: false, rpm: 0, load: 0, hyd: this.hyd * 0.8, reverse: false, track: 0, idle: 0, max: 1, cyl: 6, vol: 0 };
  }
  cameraTarget(out) {
    return out.set((this.pend.p.x * 2 + this.x) / 3, this.pend.p.y + 2, (this.pend.p.z * 2 + this.z) / 3);
  }
  cameraYaw() {
    return this.slew;
  }
  cabin(out, look) {
    this.slewG.updateWorldMatrix(true, false);
    out.copy(this.cabEye).applyMatrix4(this.slewG.matrixWorld);
    look.subVectors(this.pend.p, out).normalize();
  }
  footprint() {
    return { x: this.x, z: this.z, hw: 2.6, hl: 2.6 };
  }
  hud() {
    const g = this.world.terrain.height(this.pend.p.x, this.pend.p.z);
    const mass = this.load ? this.load.mass : 0;
    const ml = this.maxLoad();
    return [
      ['Ausladung', `${this.r.toFixed(1)} m`, `Drehwinkel ${Math.round(((((this.slew * 180) / Math.PI) % 360) + 360) % 360)}°`],
      ['Haken', `${(this.pend.p.y - g).toFixed(1)} m`, 'über Grund'],
      ['Last', `${mass.toFixed(1)} t / ${ml.toFixed(1)} t`, mass > ml ? 'ÜBERLAST!' : this.load ? this.load.name : 'ohne Last'],
    ];
  }
  serialize() {
    return { sl: this.slew, r: this.r, L: this.L };
  }
  deserialize(d) {
    this.slew = d.sl; this.r = d.r; this.L = d.L;
    const t = this.trolleyWorld(new THREE.Vector3());
    this.pend.p.copy(t).y -= this.L;
    this.pend.pp.copy(this.pend.p);
  }
}

// ---------------- Seilbagger mit Abrissbirne ----------------
export class WreckingCrane extends Vehicle {
  constructor(world, x, z, yaw) {
    super(world, { name: 'Seilbagger (Abrissbirne)', short: 'Birne', kind: 'tracked', x, z, yaw, hl: 2.2, hw: 1.6, colHL: 2.7, colHW: 2.0, maxSpeed: 0.9, mass: 55, height: 4, audio: { idle: 700, max: 1900, cyl: 6, vol: 1.1 } });
    this.mode = 'work';
    this.swing = 0; this.swingV = 0;
    this.luff = 1.12; this.luffV = 0;
    this.boomLen = 19;
    this.ropeL = 9; this.ropeV = 0;
    this.ballR = 0.55;
    this.ballMass = 3.0;
    this.build();
    this.applyJoints();
    this.updateGround(0.016);
    this.root.updateMatrixWorld(true);
    const tip = this.boomTip(new THREE.Vector3());
    this.pend = new Pendulum(world, tip.clone().add(P(0, -this.ropeL, 0)));
    this.hitCool = 0;
  }
  build() {
    const pb = new PartBuilder();
    const Y = MATS.red, D = MATS.dark, S = MATS.steel;
    const under = new THREE.Group();
    this.root.add(under);
    pb.box(D, 2.2, 0.6, 2.8, 0, 0.7, 0);
    for (const sx of [-1.6, 1.6]) {
      pb.box(D, 0.45, 0.5, 4.4, sx, 0.5, 0);
      pb.cylX(S, 0.38, 0.6, 12, sx, 0.45, 2.2);
      pb.cylX(D, 0.42, 0.62, 12, sx, 0.47, -2.2);
    }
    pb.build(under);
    this.tracks = [makeTrack(under, 1.6, 0.47, 5.3, 0.47, 0.75), makeTrack(under, -1.6, 0.47, 5.3, 0.47, 0.75)];
    const up = (this.upper = new THREE.Group());
    up.position.y = 1.15;
    this.root.add(up);
    pb.box(D, 3.0, 0.3, 4.8, 0, 0.15, -0.6);
    pb.box(Y, 2.9, 1.5, 3.0, 0, 1.05, -1.4);
    pb.box(D, 3.0, 1.2, 1.0, 0, 0.9, -3.3); // Gegengewicht
    pb.box(Y, 1.0, 1.8, 1.6, 1.0, 1.2, 1.0); // Kabine
    pb.cylX(S, 0.45, 1.2, 12, 0, 1.2, -1.0); // Seilwinde
    pb.build(up);
    const gl = new THREE.Mesh(new THREE.BoxGeometry(1.02, 1.0, 1.5), MATS.glass);
    gl.position.set(1.0, 1.55, 1.05);
    up.add(gl);
    this.beacon = makeBeacon(up, 1.0, 2.15, 0.6);
    this.cabEye = P(1.0, 1.7, 1.3);
    this.exhaustParent = up;
    this.exhaustL = P(-1.0, 2.1, -2.2);
    pb.cyl(S, 0.07, 0.07, 0.5, 8, -1.0, 2.0, -2.2);
    pb.build(up);
    // A-Bock
    const am = [[P(-0.9, 0.3, -2.6), P(0, 3.6, -1.6), 0.15], [P(0.9, 0.3, -2.6), P(0, 3.6, -1.6), 0.15], [P(-0.9, 0.3, -0.2), P(0, 3.6, -1.6), 0.15], [P(0.9, 0.3, -0.2), P(0, 3.6, -1.6), 0.15]];
    lattice(up, am, 0.12, MATS.dark);
    // Gittermastausleger
    const bm = (this.boomG = new THREE.Group());
    bm.position.set(0, 0.6, 1.2);
    up.add(bm);
    const mem = [];
    const L = this.boomLen;
    const wAt = (t) => 0.55 - 0.25 * t;
    for (let z = 0; z < L - 0.01; z += 1.5) {
      const z1 = Math.min(L, z + 1.5);
      const w0 = wAt(z / L), w1 = wAt(z1 / L);
      const c0 = [[-w0, -w0], [w0, -w0], [w0, w0], [-w0, w0]], c1 = [[-w1, -w1], [w1, -w1], [w1, w1], [-w1, w1]];
      for (let i = 0; i < 4; i++) {
        mem.push([P(c0[i][0], c0[i][1], z), P(c1[i][0], c1[i][1], z1), 0.1]);
        const j = (i + 1) % 4;
        mem.push([P(c0[i][0], c0[i][1], z), P(c1[j][0], c1[j][1], z1)]);
        mem.push([P(c1[i][0], c1[i][1], z1), P(c1[j][0], c1[j][1], z1)]);
      }
    }
    lattice(bm, mem, 0.05, MATS.red);
    pb.cylX(S, 0.3, 0.6, 10, 0, 0, L);
    pb.build(bm);
    // Abspannung
    this.pendant = new Rope(this.world.scene, 0.03);
    this.hoistRope = new Rope(this.world.scene, 0.03);
    // Birne
    const ball = (this.ballM = new THREE.Mesh(new THREE.SphereGeometry(this.ballR, 16, 12), MATS.dark));
    ball.castShadow = true;
    this.world.scene.add(ball);
  }
  applyJoints() {
    this.upper.rotation.y = this.swing;
    this.boomG.rotation.x = -this.luff;
  }
  boomTip(out) {
    this.boomG.updateWorldMatrix(true, false);
    return out.set(0, 0, this.boomLen).applyMatrix4(this.boomG.matrixWorld);
  }
  controls() {
    if (this.mode === 'drive') return { left: { up: 'Vor', down: 'Zurück', left: 'Links', right: 'Rechts' }, right: { up: 'Heben', down: 'Senken', left: '', right: '' }, buttons: [{ id: 'mode', label: 'Arbeiten', hint: 'Modus' }] };
    return { left: { up: 'Ausleger ab', down: 'Ausleger auf', left: 'Drehen L', right: 'Drehen R' }, right: { up: 'Heben', down: 'Senken', left: '', right: '' }, buttons: [{ id: 'mode', label: 'Fahren', hint: 'Modus' }] };
  }
  onButton(id) {
    if (id === 'mode') {
      this.mode = this.mode === 'work' ? 'drive' : 'work';
      return true;
    }
  }
  update(dt, inp) {
    let thr = 0, st = 0, sw = 0, lf = 0, ho = 0;
    if (inp) {
      if (this.mode === 'drive') { thr = inp.ly; st = inp.lx; }
      else { sw = -inp.lx; lf = -inp.ly; }
      ho = inp.ry;
    }
    this.swingV = approach(this.swingV, sw * 0.32, dt * 0.35);
    this.luffV = approach(this.luffV, lf * 0.16, dt * 0.45);
    this.ropeV = approach(this.ropeV, -ho * 1.4, dt * 2);
    this.swing += this.swingV * dt;
    this.luff = clamp(this.luff + this.luffV * dt, 0.55, 1.3);
    this.ropeL = clamp(this.ropeL + this.ropeV * dt, 1.5, 22);
    this.applyJoints();
    this.drive(dt, thr, st, 0);
    this.updateGround(dt);
    this.root.updateMatrixWorld(true);
    const tip = this.boomTip(new THREE.Vector3());
    const W = this.world;
    const r = this.ballR;
    this.pend.step(dt, tip, this.ropeL, (x, z) => W.terrain.height(x, z) + r, 0.999);
    // Treffer an Gebäuden
    this.hitCool -= dt;
    const v = this.pend.vel;
    const sp = v.length();
    if (sp > 1.2 && this.hitCool <= 0) {
      const e = 0.5 * this.ballMass * sp * sp; // kJ
      const res = W.buildings.hitSphere(this.pend.p, r + 0.1, v, 0, dt, e * 7);
      if (res.hit) {
        this.hitCool = 0.08;
        const keep = clamp(1 - res.broken * 0.08 - (res.blocked ? 0.6 : 0), 0.15, 0.9);
        const nv = _v.copy(v).multiplyScalar(res.blocked ? -0.3 : keep);
        this.pend.setVel(nv, dt);
        W.audio.shot(res.broken ? 'crash' : 'thud', clamp(e / 20, 0.4, 1.5), 0.05);
      }
    } else if (sp > 0.3) W.buildings.pushDebris(this.pend.p, r, v);
    this.ballM.position.copy(this.pend.p);
    this.hoistRope.set(tip, _v.copy(this.pend.p).add(P(0, r, 0)));
    this.upper.updateWorldMatrix(true, false);
    const aTop = P(0, 3.6, -1.6).applyMatrix4(this.upper.matrixWorld);
    this.pendant.set(aTop, _v2.set(0, 0, this.boomLen * 0.98).applyMatrix4(this.boomG.matrixWorld));
    this.tracks[0].tex.offset.x += (this.trackL * dt) / 0.22;
    this.tracks[1].tex.offset.x += (this.trackR * dt) / 0.22;
    this.hyd = clamp(Math.abs(sw) + Math.abs(lf) + Math.abs(ho) * 0.6, 0, 1);
    this.load = clamp(this.hyd * 0.5, 0, 1);
    this.updateCommon(dt, 0.15 + this.hyd * 0.6 + Math.abs(thr) * 0.5);
  }
  soundState() {
    const s = super.soundState();
    s.track = (Math.abs(this.trackL) + Math.abs(this.trackR)) / 2;
    return s;
  }
  cameraTarget(out) {
    return out.set((this.pos.x + this.pend.p.x) / 2, this.pos.y + 5, (this.pos.z + this.pend.p.z) / 2);
  }
  cameraYaw() {
    return this.mode === 'work' ? this.yaw + this.swing : this.yaw;
  }
  cabin(out, look) {
    this.upper.updateWorldMatrix(true, false);
    out.copy(this.cabEye).applyMatrix4(this.upper.matrixWorld);
    look.subVectors(this.pend.p, out).normalize();
  }
  hud() {
    const tip = this.boomTip(new THREE.Vector3());
    const reach = Math.hypot(tip.x - this.pos.x, tip.z - this.pos.z);
    return [
      ['Birne', `${this.ballMass.toFixed(1)} t`, `${(this.pend.vel.length() * 3.6).toFixed(0)} km/h`],
      ['Ausladung', `${reach.toFixed(1)} m`, `Seil ${this.ropeL.toFixed(1)} m`],
      ['Modus', this.mode === 'work' ? 'Arbeiten' : 'Fahren', ''],
    ];
  }
  serialize() {
    return { ...super.serialize(), sw: this.swing, lu: this.luff, rl: this.ropeL };
  }
  deserialize(d) {
    super.deserialize(d);
    this.swing = d.sw; this.luff = d.lu; this.ropeL = d.rl;
    this.applyJoints();
    this.updateGround(0.016);
    this.root.updateMatrixWorld(true);
    const tip = this.boomTip(new THREE.Vector3());
    this.pend.p.copy(tip).y -= this.ropeL;
    this.pend.pp.copy(this.pend.p);
  }
}
