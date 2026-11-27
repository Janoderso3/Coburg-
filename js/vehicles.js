// Baufahrzeuge: Fahrdynamik, Bodenkontakt, Hydraulik, Löffel/Schaufel/Schild, Mulde, Fahrmischer
import { clamp, lerp, approach, damp, wrapAngle } from './util.js';
import { MAT, MAT_NAME, MAT_DENSITY } from './terrain.js';
import { MATS, PartBuilder, makeTrack, makeWheel, HydCylinder, makeBeacon } from './models.js';

const G = 9.81;
const _v = new THREE.Vector3();
const _v2 = new THREE.Vector3();
const _v3 = new THREE.Vector3();
const _n = new THREE.Vector3();
const UP = new THREE.Vector3(0, 1, 0);
const _mb = new THREE.Matrix4();

// Werkzeug mit Schneidkante (Tieflöffel / Ladeschaufel)
class Bucket {
  constructor(vehicle, group, o) {
    this.v = vehicle;
    this.world = vehicle.world;
    this.group = group;
    this.cap = o.cap;
    this.width = o.width;
    this.tipL = o.tip;
    this.heelL = o.heel;
    this.openL = o.open; // Öffnungsrichtung (lokal)
    this.tipDirL = o.tipDir;
    this.power = o.power; // Abbruchkraft
    this.load = 0;
    this.mat = MAT.SOIL;
    this.tip = new THREE.Vector3();
    this.heel = new THREE.Vector3();
    this.prevTip = null;
    this.vel = new THREE.Vector3();
    this.openN = new THREE.Vector3();
    this.tipDir = new THREE.Vector3();
    this.side = new THREE.Vector3();
    this.cutRate = 0;
    this.resist = 0;
    this.spillAcc = 0;
    // Inhalt (Erdhaufen im Löffel)
    this.content = new THREE.Mesh(new THREE.SphereGeometry(1, 10, 7), new THREE.MeshLambertMaterial({ color: 0x5a4030 }));
    this.content.position.copy(o.contentPos);
    this.contentR = o.contentR;
    this.content.visible = false;
    this.content.castShadow = true;
    group.add(this.content);
  }

  computePoints(dt) {
    this.group.updateWorldMatrix(true, false);
    this.tip.copy(this.tipL).applyMatrix4(this.group.matrixWorld);
    this.heel.copy(this.heelL).applyMatrix4(this.group.matrixWorld);
    this.openN.copy(this.openL).transformDirection(this.group.matrixWorld);
    this.tipDir.copy(this.tipDirL).transformDirection(this.group.matrixWorld);
    this.side.set(1, 0, 0).transformDirection(this.group.matrixWorld);
    if (this.prevTip && dt > 0) this.vel.subVectors(this.tip, this.prevTip).divideScalar(dt);
    else this.vel.set(0, 0, 0);
    if (!this.prevTip) this.prevTip = new THREE.Vector3();
    this.prevTip.copy(this.tip);
  }

  // Prüft, ob die Bewegung blockiert ist: voller Löffel kann nicht tiefer, max. Eindringtiefe
  wouldBlock() {
    const T = this.world.terrain;
    const depth = T.height(this.tip.x, this.tip.z) - this.tip.y;
    const prev = this.lastDepth || 0;
    if (depth <= prev + 0.004) return false;
    if (this.load >= this.cap * 1.1 && depth > 0.1) return true;
    if (depth > 1.3) return true;
    return false;
  }

  update(dt) {
    const T = this.world.terrain;
    const B = this.world.buildings;
    const E = this.world.soil;
    this.cutRate = 0;
    this.resist = 0;
    this.spillT = Math.max(0, (this.spillT || 0) - dt);
    const speed = this.vel.length();
    // 1) Graben / Schürfen
    const g = T.height(this.tip.x, this.tip.z);
    const depth = g - this.tip.y;
    const hold = this.holdFraction();
    const room = Math.max(0, this.cap * 1.15 * Math.max(0.15, hold) - this.load);
    if (depth > 0.01 && speed > 0.03) {
      const vn = _v.copy(this.vel).divideScalar(speed);
      const into = vn.dot(this.openN) * 0.75 + vn.dot(this.tipDir) * 0.5;
      const hw = this.width / 2;
      const ax = this.tip.x - this.side.x * hw, az = this.tip.z - this.side.z * hw;
      const bx = this.tip.x + this.side.x * hw, bz = this.tip.z + this.side.z * hw;
      const thick = clamp(speed * dt + 0.14, 0.14, 0.4);
      if (into > 0.12 && room > 0) {
        const maxV = Math.min(room, 3.5 * this.cap * dt * clamp(into, 0.2, 1));
        const r = T.cutSegment(ax, az, bx, bz, this.tip.y, thick, maxV);
        if (r.vol > 0) {
          if (this.load < 0.01 || r.vol > this.load * 0.25) this.mat = r.mat;
          this.load += r.vol;
          this.cutRate = r.vol / dt;
          if (Math.random() < 0.15) this.world.effects.dust(this.tip.x, g, this.tip.z, 1, 0x8f7a5c, 0.6);
        }
        this.resist = clamp(depth * 1.4, 0, 0.8);
      } else {
        // seitliches Schieben: Boden wird verdrängt
        const r = T.cutSegment(ax, az, bx, bz, this.tip.y, thick, 0.6 * dt * this.cap * 2);
        if (r.vol > 0) {
          const px = this.tip.x + vn.x * 0.6, pz = this.tip.z + vn.z * 0.6;
          T.deposit(px, pz, r.vol, r.mat, 0.45);
          this.resist = clamp(depth * 1.2, 0, 0.7);
        }
      }
    }
    // Trümmer aufnehmen
    if (room > 0 && speed > 0.05 && B.debris.length) {
      const mid = _v2.addVectors(this.tip, this.heel).multiplyScalar(0.5);
      const got = B.scoopDebris(mid, this.width * 0.55, room);
      if (got > 0) {
        this.load += got;
        if (got > this.load * 0.3) this.mat = MAT.RUBBLE;
      }
    }
    // 2) Abbruch: Kontakt mit Bauteilen
    this.blocked = false;
    if (B.pieces.length && this.power > 0) {
      const mid = _v2.addVectors(this.tip, this.heel).multiplyScalar(0.5);
      let res = B.hitSphere(this.tip, this.width * 0.35, this.vel, this.power, dt);
      const res2 = B.hitSphere(mid, this.width * 0.4, this.vel, this.power * 0.6, dt);
      if (res.blocked || res2.blocked) this.blocked = true;
      if (res.hit || res2.hit) this.resist = Math.max(this.resist, 0.6);
      B.pushDebris(mid, this.width * 0.45, this.vel);
    }
    // 3) Ausschütten, wenn der Löffel kippt
    const maxHold = this.cap * 1.15 * hold;
    if (this.load > maxHold + 0.001) {
      const flow = Math.min(this.load - maxHold, (0.6 + 2.8 * (1 - hold)) * this.cap * dt);
      this.spill(flow);
    }
    // 4) Darstellung des Inhalts
    const f = clamp(this.load / this.cap, 0, 1.15);
    this.content.visible = f > 0.02;
    if (this.content.visible) {
      const s = Math.cbrt(f);
      const r = this.contentR;
      this.content.scale.set(r.x * Math.min(1, s * 1.1), r.y * s, r.z * Math.min(1, s * 1.05));
      const c = this.mat === MAT.RUBBLE ? 0x8d8780 : this.mat === MAT.CONCRETE ? 0x9a9a96 : this.mat === MAT.GRAVEL ? 0x9b988f : 0x5a4030;
      this.content.material.color.setHex(c).convertSRGBToLinear();
    }
  }

  commitDepth() {
    this.lastDepth = this.world.terrain.height(this.tip.x, this.tip.z) - this.tip.y;
  }

  // Anteil, den der Löffel bei aktueller Neigung halten kann
  holdFraction() {
    return clamp((this.openN.y + 0.55) / 0.9, 0, 1);
  }

  spill(vol) {
    if (vol <= 0) return;
    this.load -= vol;
    this.spillT = 0.6;
    this.spillAcc += vol;
    const E = this.world.soil;
    const chunk = 0.025;
    while (this.spillAcc > chunk) {
      this.spillAcc -= chunk;
      const t = (Math.random() - 0.5) * this.width * 0.9;
      const lipX = this.tip.x + this.side.x * t + this.openN.x * 0.15;
      const lipY = this.tip.y + this.openN.y * 0.1;
      const lipZ = this.tip.z + this.side.z * t + this.openN.z * 0.15;
      E.spawn(lipX, lipY, lipZ, this.vel.x * 0.5 + this.openN.x * 0.6 + (Math.random() - 0.5) * 0.3, Math.min(0, this.vel.y * 0.4), this.vel.z * 0.5 + this.openN.z * 0.6 + (Math.random() - 0.5) * 0.3, chunk, this.mat);
    }
    if (Math.random() < 0.3) this.world.audio.shot('soil', 0.8, 0.2);
  }
}

// ---------------- Basisklasse ----------------
export class Vehicle {
  constructor(world, o) {
    this.world = world;
    this.name = o.name;
    this.short = o.short;
    this.kind = o.kind; // 'tracked' | 'wheeled' | 'articulated'
    this.root = new THREE.Group();
    this.root.rotation.order = 'YXZ';
    world.scene.add(this.root);
    this.pos = new THREE.Vector3(o.x, 0, o.z);
    this.yaw = o.yaw || 0;
    this.speed = 0;
    this.trackL = 0;
    this.trackR = 0;
    this.steer = 0;
    this.yawRate = 0;
    this.hl = o.hl; // halbe Länge (Kontaktpunkte)
    this.hw = o.hw; // halbe Spurweite
    this.colHL = o.colHL || o.hl;
    this.colHW = o.colHW || o.hw;
    this.colOff = o.colOff || 0;
    this.height = o.height || 3;
    this.maxSpeed = o.maxSpeed;
    this.revSpeed = o.revSpeed || o.maxSpeed;
    this.mass = o.mass; // t
    this.susp = { y: 0, vy: 0, sf: 0, vsf: 0, sl: 0, vsl: 0, init: false };
    this.rpm = 0;
    this.load = 0;
    this.hyd = 0;
    this.engineOn = true;
    this.active = false;
    this.beacon = null;
    this.beaconT = Math.random() * 6;
    this.exhaust = new THREE.Vector3();
    this.exhaustL = o.exhaust || new THREE.Vector3(0, 3, -1);
    this.smokeT = 0;
    this.audio = o.audio || { idle: 700, max: 2100, cyl: 6, vol: 1 };
    this.up = new THREE.Vector3(0, 1, 0);
    this.fwd = new THREE.Vector3(0, 0, 1);
    this.tilt = 0;
    this.collidedT = 0;
  }

  get forward() {
    return _v3.set(Math.sin(this.yaw), 0, Math.cos(this.yaw));
  }

  groundAt(x, z, maxY) {
    return this.world.groundHeight(x, z, maxY);
  }

  // Bodenkontakt: Höhe, Längs- und Querneigung aus Kontaktpunkten mit Federung
  updateGround(dt) {
    const c = Math.cos(this.yaw), s = Math.sin(this.yaw);
    const fx = s, fz = c, lx = c, lz = -s;
    const maxY = this.pos.y + 0.7;
    const sample = (a, b) => {
      // a: vorne(+)/hinten(-), b: links(+)/rechts(-)
      let m = -Infinity;
      const n = this.kind === 'tracked' ? 3 : 1;
      for (let i = 0; i < n; i++) {
        const aa = n === 1 ? a : a * (0.35 + 0.65 * (i / (n - 1)));
        const x = this.pos.x + fx * aa + lx * b, z = this.pos.z + fz * aa + lz * b;
        m = Math.max(m, this.groundAt(x, z, maxY));
      }
      return m;
    };
    const hl = this.hl, hw = this.hw;
    const FL = sample(hl, hw), FR = sample(hl, -hw), RL = sample(-hl, hw), RR = sample(-hl, -hw);
    const yT = (FL + FR + RL + RR) / 4;
    const sfT = clamp(((FL + FR) - (RL + RR)) / 2 / (2 * hl), -0.9, 0.9);
    const slT = clamp(((FL + RL) - (FR + RR)) / 2 / (2 * hw), -0.9, 0.9);
    const S = this.susp;
    if (!S.init) {
      S.y = yT; S.sf = sfT; S.sl = slT; S.init = true;
    }
    // Feder-Dämpfer
    const k = this.kind === 'tracked' ? 260 : 140, d = this.kind === 'tracked' ? 32 : 18;
    if (S.y < yT) {
      // Boden drückt hoch
      S.vy += (k * (yT - S.y) - d * S.vy) * dt;
      if (S.y < yT - 0.25) { S.y = yT - 0.25; S.vy = Math.max(S.vy, 0); }
    } else {
      S.vy -= G * dt;
      S.vy += (k * (yT - S.y) * 0.3 - d * S.vy * 0.3) * dt;
    }
    S.y += S.vy * dt;
    if (S.y < yT - 0.02 && S.vy < 0) S.vy *= 0.5;
    S.vsf += (k * 0.8 * (sfT - S.sf) - d * S.vsf) * dt;
    S.vsl += (k * 0.8 * (slT - S.sl) - d * S.vsl) * dt;
    S.sf += S.vsf * dt;
    S.sl += S.vsl * dt;
    this.pos.y = S.y;
    // Orientierung aus Neigung
    _n.set(0, 1, 0).addScaledVector(_v.set(lx, 0, lz), -S.sl).addScaledVector(_v2.set(fx, 0, fz), -S.sf).normalize();
    this.up.copy(_n);
    const fwd = _v.set(fx, 0, fz);
    const left = _v2.crossVectors(this.up, fwd).normalize();
    fwd.crossVectors(left, this.up).normalize();
    this.fwd.copy(fwd);
    _mb.makeBasis(left, this.up, fwd);
    this.root.quaternion.setFromRotationMatrix(_mb);
    this.root.position.copy(this.pos);
    this.tilt = Math.acos(clamp(this.up.y, -1, 1));
    this.pitchSlope = S.sf;
  }

  // Fahrantrieb. throttle/steer: -1..1
  drive(dt, throttle, steer, resist = 0) {
    const slope = this.pitchSlope || 0; // tan Steigung in Fahrtrichtung
    const loadF = this.mass / (this.mass + this.payloadMass());
    let moveF = 0, moveYaw = 0;
    if (this.kind === 'tracked') {
      let tl = throttle + steer * 0.75, tr = throttle - steer * 0.75;
      const mx = Math.max(1, Math.abs(tl), Math.abs(tr));
      tl /= mx; tr /= mx;
      const vmax = this.maxSpeed * (1 - resist * 0.7);
      const acc = 1.1 * dt;
      this.trackL = approach(this.trackL, tl * vmax, acc);
      this.trackR = approach(this.trackR, tr * vmax, acc);
      // Steigung bremst
      let v = (this.trackL + this.trackR) / 2;
      const slip = clamp((Math.abs(slope) - 0.55) * 2, 0, 1);
      if (slope * v > 0) v *= 1 - clamp(slope * 0.8, 0, 0.6);
      if (slip > 0) v -= Math.sign(slope) * slip * 0.8;
      moveF = v;
      moveYaw = (this.trackR - this.trackL) / (this.hw * 2) * 0.9;
      this.speed = v;
    } else {
      const maxSteer = this.kind === 'articulated' ? 0.7 : 0.6;
      this.steer = approach(this.steer, -steer * maxSteer, dt * (this.kind === 'articulated' ? 0.9 : 1.6));
      const vmax = throttle >= 0 ? this.maxSpeed : this.revSpeed;
      const target = throttle * vmax * (1 - resist * 0.8);
      const engAcc = (this.accel || 1.8) * loadF;
      let a = 0;
      if (Math.abs(throttle) < 0.05) a = -Math.sign(this.speed) * Math.min(Math.abs(this.speed) / dt, 1.5);
      else if (Math.sign(target) !== Math.sign(this.speed) && Math.abs(this.speed) > 0.1) a = -Math.sign(this.speed) * 6;
      else a = clamp((target - this.speed) * 2, -6, engAcc);
      // Hangabtrieb
      a -= G * Math.sin(Math.atan(slope)) * (Math.abs(throttle) < 0.05 ? 0.15 : 1) * 0.6;
      this.speed += a * dt;
      if (Math.abs(throttle) < 0.05 && Math.abs(this.speed) < 0.05) this.speed = 0;
      moveF = this.speed;
      if (this.kind === 'articulated') moveYaw = (this.speed * Math.sin(this.steer)) / (this.wheelbase / 2 * (1 + Math.cos(this.steer)));
      else moveYaw = (this.speed * Math.tan(this.steer)) / this.wheelbase;
    }
    // Bewegung mit Kollisionsprüfung
    const nyaw = this.yaw + moveYaw * dt;
    const nx = this.pos.x + Math.sin(nyaw) * moveF * dt;
    const nz = this.pos.z + Math.cos(nyaw) * moveF * dt;
    if (this.world.vehicleBlocked(this, nx, nz, nyaw)) {
      if (Math.abs(moveF) > 0.5) this.world.audio.shot('thud', Math.min(1, Math.abs(moveF) * 0.3), 0.4);
      this.speed *= -0.15;
      this.trackL *= -0.1;
      this.trackR *= -0.1;
      this.collidedT = 0.3;
      // nur drehen, falls möglich
      if (!this.world.vehicleBlocked(this, this.pos.x, this.pos.z, nyaw)) this.yaw = nyaw;
    } else {
      this.pos.x = nx;
      this.pos.z = nz;
      this.yaw = nyaw;
    }
    // Spurrillen in der Grasnarbe
    const sp = Math.abs(moveF) + Math.abs(moveYaw) * this.hw;
    if (sp > 0.1 && Math.random() < 0.5) {
      const c = Math.cos(this.yaw), s = Math.sin(this.yaw);
      const T = this.world.terrain;
      for (const b of [this.hw, -this.hw]) {
        for (const a of [this.hl * 0.8, -this.hl * 0.8]) {
          const x = this.pos.x + s * a + c * b, z = this.pos.z + c * a - s * b;
          T.addWear(x, z, this.kind === 'tracked' ? 0.35 : 0.3, 0.06 * sp * (this.mass / 15));
        }
      }
      if (sp > 1.5 && Math.random() < 0.15) this.world.effects.dust(this.pos.x - Math.sin(this.yaw) * this.hl, this.pos.y + 0.2, this.pos.z - Math.cos(this.yaw) * this.hl, 1, 0xa08d70, 1.0);
    }
    return { moveF, moveYaw };
  }

  payloadMass() {
    return 0;
  }

  // Motor, Abgas, Rundumleuchte
  updateCommon(dt, demand) {
    this.rpm = damp(this.rpm, clamp(demand, 0, 1), 3, dt);
    this.exhaust.copy(this.exhaustL).applyMatrix4((this.exhaustParent || this.root).matrixWorld);
    this.smokeT -= dt;
    if (this.smokeT <= 0) {
      this.smokeT = 0.12 - this.rpm * 0.07;
      this.world.effects.smoke(this.exhaust.x, this.exhaust.y, this.exhaust.z, this.rpm * 0.6 + this.load * 0.4, this.load);
    }
    if (this.beacon) {
      this.beaconT += dt * 6;
      const on = this.active && Math.sin(this.beaconT) > 0.3;
      this.beacon.material.emissiveIntensity = on ? 2.5 : 0.15;
    }
    this.collidedT = Math.max(0, this.collidedT - dt);
  }

  footprint() {
    return { x: this.pos.x + Math.sin(this.yaw) * this.colOff, z: this.pos.z + Math.cos(this.yaw) * this.colOff, hw: this.colHW, hl: this.colHL };
  }

  soundState() {
    return { on: this.engineOn, rpm: this.rpm, load: this.load, hyd: this.hyd, reverse: false, track: 0, ...this.audio };
  }
  cameraTarget(out) {
    return out.copy(this.pos).add(_v.set(0, 2, 0));
  }
  cameraYaw() {
    return this.yaw;
  }
  serialize() {
    return { x: this.pos.x, z: this.pos.z, yaw: this.yaw };
  }
  deserialize(d) {
    this.pos.x = d.x; this.pos.z = d.z; this.yaw = d.yaw;
    this.susp.init = false;
  }
}

// ---------------- Kettenbagger (20 t) ----------------
export class Excavator extends Vehicle {
  constructor(world, x, z, yaw) {
    super(world, { name: 'Kettenbagger', short: 'Bagger', kind: 'tracked', x, z, yaw, hl: 1.9, hw: 1.2, colHL: 2.3, colHW: 1.5, maxSpeed: 1.5, mass: 21, height: 3.1, exhaust: new THREE.Vector3(-0.7, 3.25, -1.3), audio: { idle: 800, max: 2000, cyl: 6, vol: 1 } });
    this.mode = 'work';
    this.swing = 0; this.swingV = 0;
    this.boom = 0.55; this.boomV = 0;
    this.stick = 1.4; this.stickV = 0;
    this.buck = 1.2; this.buckV = 0;
    this.build();
  }

  build() {
    const pb = new PartBuilder();
    const Y = MATS.yellow, D = MATS.dark, B = MATS.black, S = MATS.steel;
    // Unterwagen
    const under = new THREE.Group();
    this.root.add(under);
    pb.box(D, 1.9, 0.55, 2.3, 0, 0.62, 0);
    for (const sx of [-1.2, 1.2]) {
      pb.box(D, 0.36, 0.42, 3.7, sx, 0.48, 0);
      pb.cylX(S, 0.34, 0.5, 12, sx, 0.4, 1.85);
      pb.cylX(D, 0.38, 0.52, 12, sx, 0.42, -1.85);
      for (let i = 0; i < 6; i++) pb.cylX(S, 0.12, 0.45, 8, sx, 0.15, -1.4 + i * 0.56);
      pb.cylX(S, 0.09, 0.4, 8, sx, 0.8, 0.7);
      pb.cylX(S, 0.09, 0.4, 8, sx, 0.8, -0.7);
    }
    pb.cyl(D, 1.0, 1.0, 0.18, 20, 0, 0.98, 0);
    pb.build(under);
    this.tracks = [makeTrack(under, 1.2, 0.42, 4.5, 0.42, 0.6), makeTrack(under, -1.2, 0.42, 4.5, 0.42, 0.6)];
    // Oberwagen (dreht sich)
    const upper = (this.upper = new THREE.Group());
    upper.position.y = 1.08;
    this.root.add(upper);
    pb.box(D, 2.7, 0.22, 4.2, 0, 0.11, -0.45);
    pb.box(Y, 2.75, 0.95, 1.9, 0, 0.7, -1.55); // Motorhaube
    pb.profileX(D, [[-2.25, 0.25], [-2.6, 0.45], [-2.62, 1.15], [-2.3, 1.3], [-1.9, 1.3], [-1.9, 0.25]], 2.85); // Gegengewicht
    pb.box(Y, 1.1, 0.95, 1.6, -0.82, 0.7, 0.55); // Werkzeugkasten rechts
    pb.box(Y, 0.4, 0.5, 0.9, -0.15, 0.45, 1.0);
    pb.box(B, 2.6, 0.06, 1.8, 0, 1.2, -1.55);
    pb.cyl(S, 0.06, 0.06, 0.6, 8, -0.7, 1.45, -1.3);
    // Kabine (links = +X)
    pb.box(Y, 1.0, 0.25, 1.75, 0.82, 0.35, 0.55);
    pb.box(Y, 1.0, 0.08, 1.75, 0.82, 2.05, 0.55);
    for (const [x, z] of [[0.34, 1.4], [1.3, 1.4], [0.34, -0.3], [1.3, -0.3]]) pb.box(Y, 0.07, 1.65, 0.07, x, 1.22, z);
    pb.box(B, 0.6, 0.6, 0.5, 0.82, 0.75, 0.25); // Sitz
    pb.box(MATS.steel, 0.06, 0.06, 1.0, 1.36, 1.0, -0.8);
    pb.build(upper);
    const glass = new THREE.Mesh(new THREE.BoxGeometry(0.94, 1.55, 1.68), MATS.glass);
    glass.position.set(0.82, 1.2, 0.55);
    upper.add(glass);
    this.beacon = makeBeacon(upper, 0.82, 2.1, 0.1);
    const lamp = new THREE.Mesh(new THREE.BoxGeometry(0.2, 0.12, 0.08), MATS.light);
    lamp.position.set(0.82, 2.0, 1.45);
    upper.add(lamp);
    this.cabEye = new THREE.Vector3(0.82, 1.75, 0.7);
    this.exhaustParent = upper;
    this.exhaustL = new THREE.Vector3(-0.7, 1.8, -1.3);
    // Ausleger (Monoblock, Bananenform). Drehpunkt
    const boom = (this.boomG = new THREE.Group());
    boom.position.set(-0.2, 1.05, 1.05);
    upper.add(boom);
    pb.beamYZ(Y, 0.55, 0.62, 0, 0, 1.25, 2.9);
    pb.beamYZ(Y, 0.5, 0.55, 1.25, 2.9, 0, 5.7);
    pb.box(Y, 0.6, 0.35, 0.5, 0, 1.2, 2.9);
    pb.cylX(S, 0.14, 0.75, 10, 0, 0, 0);
    pb.cylX(S, 0.12, 0.62, 10, 0, 0, 5.7);
    pb.build(boom);
    this.boomLen = 5.7;
    // Stiel
    const stick = (this.stickG = new THREE.Group());
    stick.position.set(0, 0, 5.7);
    boom.add(stick);
    pb.beamYZ(Y, 0.42, 0.48, 0.25, -0.6, 0, 2.95);
    pb.box(Y, 0.44, 0.3, 0.4, 0, 0.35, -0.5);
    pb.cylX(S, 0.1, 0.5, 10, 0, 0, 2.95);
    pb.build(stick);
    this.stickLen = 2.95;
    // Löffel
    const bucket = (this.bucketG = new THREE.Group());
    bucket.position.set(0, 0, 2.95);
    stick.add(bucket);
    const W = 1.1;
    pb.profileX(MATS.yellowDirty, [[-0.12, -0.08], [-0.08, 0.32], [0.25, 0.68], [0.8, 0.78], [1.22, 0.48], [1.42, 0.02], [1.48, -0.16], [1.36, -0.16], [1.2, 0.36], [0.78, 0.62], [0.3, 0.55], [0.05, 0.25], [0.0, -0.08]], W);
    for (const sx of [-W / 2, W / 2]) pb.profileX(MATS.yellowDirty, [[-0.15, -0.1], [-0.1, 0.35], [0.25, 0.7], [0.8, 0.8], [1.25, 0.5], [1.48, -0.18], [0.6, -0.12]], 0.04, sx);
    for (let i = 0; i < 5; i++) pb.box(MATS.bucketWear, 0.09, 0.08, 0.26, -0.44 + i * 0.22, -0.2, 1.58, 0.5, 0, 0);
    pb.box(Y, 0.3, 0.3, 0.35, 0, 0.32, -0.12);
    pb.build(bucket);
    this.bucket = new Bucket(this, bucket, {
      cap: 1.0, width: W, tip: new THREE.Vector3(0, -0.24, 1.66), heel: new THREE.Vector3(0, 0.72, 0.8),
      open: new THREE.Vector3(0, -1, 0.15).normalize(), tipDir: new THREE.Vector3(0, -0.45, 1).normalize(), power: 140,
      contentPos: new THREE.Vector3(0, 0.25, 0.75), contentR: new THREE.Vector3(0.5, 0.42, 0.6),
    });
    // Hydraulikzylinder
    const sc = this.world.scene;
    this.cyl = {
      boomL: new HydCylinder(sc, 0.11, 0.065, 1.4), boomR: new HydCylinder(sc, 0.11, 0.065, 1.4),
      stick: new HydCylinder(sc, 0.12, 0.07, 1.6), bucket: new HydCylinder(sc, 0.1, 0.06, 1.2),
    };
    this.anch = {
      boomBaseL: new THREE.Vector3(0.12, 0.55, 1.35), boomBaseR: new THREE.Vector3(-0.52, 0.55, 1.35),
      boomMidL: new THREE.Vector3(0.33, -0.3, 2.3), boomMidR: new THREE.Vector3(-0.33, -0.3, 2.3),
      stickBase: new THREE.Vector3(0, 1.55, 2.6), stickEnd: new THREE.Vector3(0, 0.5, -0.55),
      bucketBase: new THREE.Vector3(0, 0.45, 0.5), bucketEnd: new THREE.Vector3(0, 0.42, -0.1),
    };
    this.applyJoints();
  }

  applyJoints() {
    this.upper.rotation.y = this.swing;
    this.boomG.rotation.x = -this.boom;
    this.stickG.rotation.x = this.stick;
    this.bucketG.rotation.x = this.buck;
  }

  updateCylinders() {
    const a = this.anch;
    const wp = (v, g) => _v.copy(v).applyMatrix4(g.matrixWorld);
    this.upper.updateWorldMatrix(true, true);
    const p1 = wp(a.boomBaseL, this.upper).clone(), p2 = wp(a.boomMidL, this.boomG).clone();
    this.cyl.boomL.update(p1, p2);
    const p3 = wp(a.boomBaseR, this.upper).clone();
    const p4 = wp(a.boomMidR, this.boomG).clone();
    this.cyl.boomR.update(p3, p4);
    const p5 = wp(a.stickBase, this.boomG).clone(), p6 = wp(a.stickEnd, this.stickG).clone();
    this.cyl.stick.update(p5, p6);
    const p7 = wp(a.bucketBase, this.stickG).clone(), p8 = wp(a.bucketEnd, this.bucketG).clone();
    this.cyl.bucket.update(p7, p8);
  }

  controls() {
    const iso = this.world.settings.iso;
    if (this.mode === 'drive')
      return {
        left: { up: 'Vor', down: 'Zurück', left: 'Links', right: 'Rechts' },
        right: { up: iso ? 'Ausleger ab' : 'Ausleger auf', down: iso ? 'Ausleger auf' : 'Ausleger ab', left: 'Löffel ein', right: 'Löffel aus' },
        buttons: [{ id: 'mode', label: 'Arbeiten', hint: 'Modus' }],
      };
    return {
      left: { up: 'Stiel aus', down: 'Stiel ein', left: 'Drehen L', right: 'Drehen R' },
      right: { up: iso ? 'Ausleger ab' : 'Ausleger auf', down: iso ? 'Ausleger auf' : 'Ausleger ab', left: 'Löffel ein', right: 'Löffel aus' },
      buttons: [{ id: 'mode', label: 'Fahren', hint: 'Modus' }],
    };
  }
  onButton(id) {
    if (id === 'mode') {
      this.mode = this.mode === 'work' ? 'drive' : 'work';
      this.world.ui.toast(this.mode === 'work' ? 'Arbeitsmodus: Oberwagen, Ausleger, Stiel, Löffel' : 'Fahrmodus: Kettenfahrwerk', 'info');
      return true;
    }
  }

  update(dt, inp) {
    const iso = this.world.settings.iso;
    let thr = 0, st = 0, sw = 0, sk = 0, bm = 0, bk = 0;
    if (inp) {
      if (this.mode === 'drive') { thr = inp.ly; st = inp.lx; }
      else { sw = -inp.lx; sk = -inp.ly; }
      bm = iso ? -inp.ry : inp.ry;
      bk = -inp.rx;
    }
    // Hydraulik: Pumpenförderstrom wird auf die Funktionen aufgeteilt
    const demand = Math.abs(sw) + Math.abs(sk) + Math.abs(bm) + Math.abs(bk) + (Math.abs(thr) + Math.abs(st)) * 0.8;
    const share = demand > 1.6 ? 1.6 / demand : 1;
    const res = this.bucket.resist;
    const slow = 1 - res * 0.6;
    const acc = dt * 3.5;
    this.swingV = approach(this.swingV, sw * 0.75 * share, dt * 0.9); // Schwenkträgheit
    this.boomV = approach(this.boomV, bm * 0.42 * share * slow, acc * 0.5);
    this.stickV = approach(this.stickV, sk * 0.62 * share * slow, acc * 0.6);
    this.buckV = approach(this.buckV, bk * 0.95 * share * slow, acc);
    const prev = [this.swing, this.boom, this.stick, this.buck];
    this.swing += this.swingV * dt;
    this.boom = clamp(this.boom + this.boomV * dt, -0.62, 1.0);
    this.stick = clamp(this.stick + this.stickV * dt, 0.5, 2.6);
    this.buck = clamp(this.buck + this.buckV * dt, -0.85, 2.25);
    if (this.boom <= -0.62 || this.boom >= 1.0) this.boomV = 0;
    this.applyJoints();
    // Fahren
    const dr = this.drive(dt, thr, st, 0);
    this.updateGround(dt);
    this.root.updateMatrixWorld(true);
    this.bucket.computePoints(dt);
    // Blockierung: voller Löffel / Beton / max. Grabtiefe
    const tooDeep = this.bucket.tip.y < this.pos.y - 6.6;
    this.bucket.update(dt);
    const blocked = tooDeep || this.bucket.blocked || this.bucket.wouldBlock();
    if (blocked) {
      [this.swing, this.boom, this.stick, this.buck] = prev;
      this.swingV *= -0.1; this.boomV = 0; this.stickV = 0; this.buckV = 0;
      this.applyJoints();
      this.root.updateMatrixWorld(true);
      this.bucket.computePoints(0);
    }
    this.bucket.commitDepth();
    this.updateCylinders();
    // Ketten-Animation
    const vL = this.trackL, vR = this.trackR;
    this.tracks[0].tex.offset.x += (vL * dt) / 0.22;
    this.tracks[1].tex.offset.x += (vR * dt) / 0.22;
    this.hyd = clamp(demand / 1.6, 0, 1) + (blocked ? 0.4 : 0);
    this.load = clamp(res + (blocked ? 0.8 : 0) + this.hyd * 0.3, 0, 1);
    this.updateCommon(dt, 0.15 + this.hyd * 0.8 + this.load * 0.2);
  }

  soundState() {
    const s = super.soundState();
    s.track = (Math.abs(this.trackL) + Math.abs(this.trackR)) / 2;
    return s;
  }
  cameraTarget(out) {
    const a = this.yaw + this.swing;
    return out.set(this.pos.x + Math.sin(a) * 2.5, this.pos.y + 2.2, this.pos.z + Math.cos(a) * 2.5);
  }
  cameraYaw() {
    return this.mode === 'work' ? this.yaw + this.swing : this.yaw;
  }
  cabin(out, look) {
    this.upper.updateWorldMatrix(true, false);
    out.copy(this.cabEye).applyMatrix4(this.upper.matrixWorld);
    look.set(0, -0.25, 1).transformDirection(this.upper.matrixWorld);
  }
  hud() {
    const b = this.bucket;
    const T = this.world.terrain;
    const depth = b.tip.y - this.pos.y;
    return [
      ['Löffel', `${b.load.toFixed(2)} / ${b.cap.toFixed(1)} m³`, b.spillT > 0 && b.load > 0.02 ? 'verliert Material!' : b.load > 0.02 ? MAT_NAME[b.mat] : 'leer'],
      ['Schneidkante', `${depth >= 0 ? '+' : ''}${depth.toFixed(2)} m`, 'zum Planum'],
      ['Modus', this.mode === 'work' ? 'Arbeiten' : 'Fahren', `${(Math.abs(this.speed) * 3.6).toFixed(1)} km/h`],
    ];
  }
  serialize() {
    return { ...super.serialize(), sw: this.swing, bo: this.boom, st: this.stick, bu: this.buck, ld: this.bucket.load, m: this.bucket.mat };
  }
  deserialize(d) {
    super.deserialize(d);
    this.swing = d.sw; this.boom = d.bo; this.stick = d.st; this.buck = d.bu;
    this.bucket.load = d.ld || 0; this.bucket.mat = d.m || 1;
    this.applyJoints();
  }
}

// ---------------- Radlader (Knicklenkung) ----------------
export class WheelLoader extends Vehicle {
  constructor(world, x, z, yaw) {
    super(world, { name: 'Radlader', short: 'Radlader', kind: 'articulated', x, z, yaw, hl: 1.65, hw: 1.0, colHL: 3.15, colHW: 1.45, colOff: -0.6, maxSpeed: 8.0, revSpeed: 4, mass: 17, height: 3.4, exhaust: new THREE.Vector3(-0.45, 3.0, -2.6), audio: { idle: 750, max: 2200, cyl: 6, vol: 1 } });
    this.wheelbase = 3.3;
    this.accel = 1.6;
    this.lift = -0.05; this.liftV = 0;
    this.tiltA = 0.1; this.tiltV = 0;
    this.build();
  }

  build() {
    const pb = new PartBuilder();
    const Y = MATS.yellow, D = MATS.dark, B = MATS.black, S = MATS.steel;
    // Hinterwagen (Ursprung = Knickgelenk)
    const rear = (this.rearG = new THREE.Group());
    this.root.add(rear);
    pb.box(D, 1.0, 0.5, 3.0, 0, 0.95, -1.5);
    pb.box(Y, 1.9, 1.15, 2.1, 0, 1.6, -2.35); // Motor
    pb.box(Y, 2.2, 0.5, 0.6, 0, 1.1, -3.45); // Gegengewicht
    pb.box(D, 2.3, 0.12, 2.6, 0, 1.12, -1.6); // Kotflügel
    pb.box(Y, 2.3, 0.1, 1.5, 0, 1.2, -1.75);
    pb.box(Y, 1.35, 0.25, 1.4, 0, 1.38, -0.75); // Kabinenboden
    pb.box(Y, 1.35, 0.1, 1.45, 0, 3.15, -0.8);
    for (const [x, z] of [[0.64, -0.08], [-0.64, -0.08], [0.64, -1.5], [-0.64, -1.5]]) pb.box(Y, 0.07, 1.65, 0.07, x, 2.32, z);
    pb.box(B, 0.5, 0.6, 0.5, 0, 1.85, -0.9);
    pb.box(B, 1.7, 0.06, 1.0, 0, 2.18, -2.9);
    pb.build(rear);
    const glass = new THREE.Mesh(new THREE.BoxGeometry(1.25, 1.55, 1.35), MATS.glass);
    glass.position.set(0, 2.32, -0.8);
    rear.add(glass);
    this.beacon = makeBeacon(rear, 0, 3.2, -1.1);
    this.cabEye = new THREE.Vector3(0, 2.45, -0.75);
    // Vorderwagen
    const front = (this.frontG = new THREE.Group());
    this.root.add(front);
    pb.box(D, 1.1, 0.55, 2.2, 0, 0.95, 1.1);
    pb.box(Y, 1.3, 0.5, 0.9, 0, 1.3, 0.55);
    pb.box(D, 2.3, 0.12, 1.0, 0, 1.12, 1.65);
    pb.build(front);
    // Räder
    this.wheels = [];
    const wr = 0.8;
    for (const [g, z] of [[front, 1.65], [rear, -1.65]]) {
      for (const sx of [-1, 1]) {
        const w = makeWheel(wr, 0.6);
        w.position.set(sx * 1.0, wr, z);
        g.add(w);
        this.wheels.push(w);
      }
    }
    this.wheelR = wr;
    // Hubarm
    const arm = (this.armG = new THREE.Group());
    arm.position.set(0, 2.0, 0.65);
    front.add(arm);
    for (const sx of [-0.62, 0.62]) pb.beamYZ(Y, 0.2, 0.42, 0, 0, -0.35, 2.75, sx);
    pb.box(Y, 1.4, 0.3, 0.3, 0, -0.2, 1.4);
    pb.cylX(S, 0.1, 1.5, 8, 0, 0, 0);
    pb.build(arm);
    // Schaufel
    const bk = (this.bucketG = new THREE.Group());
    bk.position.set(0, -0.35, 2.75);
    arm.add(bk);
    const W = 2.7;
    // Profil: Drehpunkt hinten oben, Öffnung nach vorne (+Z)
    pb.profileX(MATS.yellowDirty, [[0.05, 0.25], [-0.1, 0.0], [-0.1, -0.7], [0.1, -1.0], [0.75, -1.08], [1.15, -1.1], [1.15, -1.0], [0.75, -0.98], [0.15, -0.9], [0.02, -0.65], [0.02, 0.05], [0.12, 0.3]], W);
    for (const sx of [-W / 2, W / 2]) pb.profileX(MATS.yellowDirty, [[-0.1, 0.3], [-0.12, -0.72], [0.1, -1.05], [1.15, -1.1], [0.6, 0.0]], 0.05, sx);
    pb.box(MATS.bucketWear, W, 0.06, 0.2, 0, -1.08, 1.12);
    pb.build(bk);
    this.bucket = new Bucket(this, bk, {
      cap: 2.8, width: W, tip: new THREE.Vector3(0, -1.08, 1.2), heel: new THREE.Vector3(0, -0.95, 0.1),
      open: new THREE.Vector3(0, 0.55, 1).normalize(), tipDir: new THREE.Vector3(0, -0.05, 1).normalize(), power: 45,
      contentPos: new THREE.Vector3(0, -0.6, 0.6), contentR: new THREE.Vector3(1.25, 0.5, 0.6),
    });
    const sc = this.world.scene;
    this.cyl = { liftL: new HydCylinder(sc, 0.1, 0.06, 1.0), liftR: new HydCylinder(sc, 0.1, 0.06, 1.0), tilt: new HydCylinder(sc, 0.12, 0.07, 1.1), steerL: new HydCylinder(sc, 0.06, 0.035, 0.45, MATS.dark), steerR: new HydCylinder(sc, 0.06, 0.035, 0.45, MATS.dark) };
    this.applyJoints();
  }

  applyJoints() {
    this.frontG.rotation.y = this.steer;
    this.armG.rotation.x = -this.lift;
    // Parallelführung: Schaufelwinkel relativ zum Arm ausgleichen
    this.bucketG.rotation.x = this.lift - this.tiltA;
  }

  updateCylinders() {
    this.root.updateMatrixWorld(true);
    const wp = (x, y, z, g) => new THREE.Vector3(x, y, z).applyMatrix4(g.matrixWorld);
    this.cyl.liftL.update(wp(0.62, 1.0, 0.2, this.frontG), wp(0.62, -0.3, 1.4, this.armG));
    this.cyl.liftR.update(wp(-0.62, 1.0, 0.2, this.frontG), wp(-0.62, -0.3, 1.4, this.armG));
    this.cyl.tilt.update(wp(0, 1.55, 0.95, this.frontG), wp(0, 0.15, -0.05, this.bucketG));
    this.cyl.steerL.update(wp(0.55, 1.0, -0.6, this.rearG), wp(0.5, 1.0, 0.35, this.frontG));
    this.cyl.steerR.update(wp(-0.55, 1.0, -0.6, this.rearG), wp(-0.5, 1.0, 0.35, this.frontG));
  }

  controls() {
    const iso = this.world.settings.iso;
    return {
      left: { up: 'Vor', down: 'Zurück', left: 'Lenken L', right: 'Lenken R' },
      right: { up: iso ? 'Senken' : 'Heben', down: iso ? 'Heben' : 'Senken', left: 'Ankippen', right: 'Auskippen' },
      buttons: [],
    };
  }

  update(dt, inp) {
    const iso = this.world.settings.iso;
    let thr = 0, st = 0, lf = 0, tl = 0;
    if (inp) {
      thr = inp.ly; st = inp.lx;
      lf = iso ? -inp.ry : inp.ry;
      tl = -inp.rx;
    }
    const res = this.bucket.resist;
    this.liftV = approach(this.liftV, lf * 0.38 * (1 - res * 0.5), dt * 1.8);
    this.tiltV = approach(this.tiltV, tl * 0.9, dt * 4);
    const prev = [this.lift, this.tiltA];
    const px = this.pos.x, pz = this.pos.z, pyaw = this.yaw;
    this.lift = clamp(this.lift + this.liftV * dt, -0.27, 1.05);
    this.tiltA = clamp(this.tiltA + this.tiltV * dt, -0.85, 0.75);
    this.drive(dt, thr, st, res * 0.6);
    this.applyJoints();
    this.updateGround(dt);
    this.root.updateMatrixWorld(true);
    this.bucket.computePoints(dt);
    this.bucket.update(dt);
    const blocked = this.bucket.blocked || this.bucket.wouldBlock();
    if (blocked) {
      [this.lift, this.tiltA] = prev;
      this.liftV = 0; this.tiltV = 0;
      this.pos.x = px; this.pos.z = pz; this.yaw = pyaw;
      if (this.speed > 0) this.speed = 0;
      this.applyJoints();
      this.updateGround(0);
      this.root.updateMatrixWorld(true);
      this.bucket.computePoints(0);
    }
    this.bucket.commitDepth();
    this.updateCylinders();
    // Räder drehen
    for (const w of this.wheels) w.userData.spin.rotation.x += (this.speed * dt) / this.wheelR;
    this.hyd = clamp(Math.abs(lf) + Math.abs(tl) * 0.6, 0, 1);
    this.load = clamp(res + (blocked ? 0.6 : 0) + Math.abs(thr) * 0.3, 0, 1);
    this.updateCommon(dt, 0.12 + Math.abs(thr) * 0.65 + this.hyd * 0.5 + this.load * 0.3);
  }

  soundState() {
    const s = super.soundState();
    s.reverse = this.speed < -0.1;
    return s;
  }
  cameraTarget(out) {
    return out.set(this.pos.x + Math.sin(this.yaw) * 1.5, this.pos.y + 2.0, this.pos.z + Math.cos(this.yaw) * 1.5);
  }
  cabin(out, look) {
    this.rearG.updateWorldMatrix(true, false);
    out.copy(this.cabEye).applyMatrix4(this.rearG.matrixWorld);
    look.set(0, -0.2, 1).transformDirection(this.root.matrixWorld);
  }
  hud() {
    const b = this.bucket;
    return [
      ['Schaufel', `${b.load.toFixed(2)} / ${b.cap.toFixed(1)} m³`, b.spillT > 0 && b.load > 0.02 ? 'verliert Material!' : b.load > 0.02 ? MAT_NAME[b.mat] : 'leer'],
      ['Tempo', `${(Math.abs(this.speed) * 3.6).toFixed(1)} km/h`, this.speed < -0.1 ? 'rückwärts' : ''],
      ['Hubhöhe', `${(b.tip.y - this.pos.y).toFixed(1)} m`, 'Schneidkante'],
    ];
  }
  serialize() {
    return { ...super.serialize(), li: this.lift, ti: this.tiltA, ld: this.bucket.load, m: this.bucket.mat };
  }
  deserialize(d) {
    super.deserialize(d);
    this.lift = d.li; this.tiltA = d.ti;
    this.bucket.load = d.ld || 0; this.bucket.mat = d.m || 1;
  }
}

// ---------------- Planierraupe ----------------
export class Dozer extends Vehicle {
  constructor(world, x, z, yaw) {
    super(world, { name: 'Planierraupe', short: 'Raupe', kind: 'tracked', x, z, yaw, hl: 1.45, hw: 0.95, colHL: 2.45, colHW: 1.75, colOff: 0.45, maxSpeed: 2.6, mass: 20, height: 3.2, exhaust: new THREE.Vector3(0.45, 3.0, 0.6), audio: { idle: 750, max: 1950, cyl: 6, vol: 1.1 } });
    this.blade = 0.3; this.bladeV = 0;
    this.bladeTilt = 0;
    this.pushVol = 0;
    this.build();
  }

  build() {
    const pb = new PartBuilder();
    const Y = MATS.yellow, D = MATS.dark, B = MATS.black, S = MATS.steel;
    const body = new THREE.Group();
    this.root.add(body);
    for (const sx of [-0.95, 0.95]) {
      pb.box(D, 0.35, 0.45, 2.9, sx, 0.45, 0);
      pb.cylX(D, 0.42, 0.5, 12, sx, 0.95, -1.3); // erhöhter Turas
      pb.cylX(S, 0.32, 0.5, 12, sx, 0.38, 1.45);
      for (let i = 0; i < 5; i++) pb.cylX(S, 0.12, 0.45, 8, sx, 0.15, -1.1 + i * 0.55);
    }
    pb.box(D, 1.6, 0.6, 3.2, 0, 0.9, 0);
    pb.box(Y, 1.45, 1.05, 2.0, 0, 1.6, 0.6); // Motorhaube
    pb.box(B, 1.2, 0.6, 0.12, 0, 1.55, 1.62); // Kühlergrill
    pb.box(Y, 2.2, 0.15, 2.3, 0, 1.25, -0.6); // Kotflügel
    pb.box(Y, 1.5, 0.2, 1.4, 0, 1.4, -0.8);
    pb.box(Y, 1.5, 0.1, 1.4, 0, 3.2, -0.85);
    for (const [x, z] of [[0.72, -0.15], [-0.72, -0.15], [0.72, -1.55], [-0.72, -1.55]]) pb.box(Y, 0.07, 1.8, 0.07, x, 2.3, z);
    pb.box(B, 0.5, 0.6, 0.5, 0, 1.8, -1.0);
    pb.box(D, 1.5, 0.5, 0.4, 0, 1.0, -1.75); // Heck
    pb.cyl(S, 0.06, 0.06, 0.8, 8, 0.45, 2.55, 0.6);
    pb.build(body);
    const glass = new THREE.Mesh(new THREE.BoxGeometry(1.38, 1.7, 1.33), MATS.glass);
    glass.position.set(0, 2.3, -0.85);
    body.add(glass);
    this.beacon = makeBeacon(body, 0.5, 3.25, -1.2);
    this.cabEye = new THREE.Vector3(0, 2.5, -0.8);
    this.tracks = [makeTrack(body, 0.95, 0.5, 3.4, 0.45, 0.5), makeTrack(body, -0.95, 0.5, 3.4, 0.45, 0.5)];
    // Schild an Schubrahmen (Drehpunkt hinten an den Laufwerken)
    const frame = (this.frameG = new THREE.Group());
    frame.position.set(0, 0.45, -0.2);
    this.root.add(frame);
    for (const sx of [-1.18, 1.18]) pb.beamYZ(Y, 0.18, 0.25, 0, 0, 0.1, 2.25, sx);
    pb.build(frame);
    const bl = (this.bladeG = new THREE.Group());
    bl.position.set(0, 0.1, 2.35);
    frame.add(bl);
    const BW = 3.4;
    pb.profileX(Y, [[0.0, -0.55], [0.18, -0.55], [0.12, -0.2], [0.18, 0.25], [0.32, 0.65], [0.38, 0.75], [0.22, 0.78], [-0.02, 0.4], [-0.1, 0.0], [-0.08, -0.5]], BW);
    for (const sx of [-BW / 2, BW / 2]) pb.profileX(Y, [[-0.1, -0.5], [0.2, -0.55], [0.38, 0.75], [-0.05, 0.6]], 0.05, sx);
    pb.box(MATS.bucketWear, BW, 0.1, 0.06, 0, -0.52, 0.18);
    pb.build(bl);
    this.bladeW = BW;
    const sc = this.world.scene;
    this.cyl = { l: new HydCylinder(sc, 0.08, 0.05, 1.0), r: new HydCylinder(sc, 0.08, 0.05, 1.0) };
    this.edge = new THREE.Vector3();
  }

  applyJoints() {
    // Schildhöhe über Rotation des Schubrahmens
    this.frameG.rotation.x = -Math.asin(clamp(this.blade / 2.35, -0.5, 0.6));
    this.bladeG.rotation.x = -this.frameG.rotation.x;
    this.bladeG.rotation.z = this.bladeTilt;
  }

  controls() {
    const iso = this.world.settings.iso;
    return {
      left: { up: 'Vor', down: 'Zurück', left: 'Links', right: 'Rechts' },
      right: { up: iso ? 'Schild ab' : 'Schild auf', down: iso ? 'Schild auf' : 'Schild ab', left: 'Neigen L', right: 'Neigen R' },
      buttons: [],
    };
  }

  update(dt, inp) {
    const iso = this.world.settings.iso;
    let thr = 0, st = 0, bl = 0, tl = 0;
    if (inp) {
      thr = inp.ly; st = inp.lx;
      bl = iso ? -inp.ry : inp.ry;
      tl = inp.rx;
    }
    this.bladeV = approach(this.bladeV, bl * 0.35, dt * 2.5);
    this.blade = clamp(this.blade + this.bladeV * dt, -0.45, 1.1);
    this.bladeTilt = clamp(this.bladeTilt + tl * 0.12 * dt, -0.1, 0.1);
    const resist = clamp(this.pushVol / 9, 0, 0.75);
    this.drive(dt, thr, st, resist);
    this.applyJoints();
    this.updateGround(dt);
    this.root.updateMatrixWorld(true);
    this.doBlade(dt);
    // Zylinder
    const wp = (x, y, z, g) => new THREE.Vector3(x, y, z).applyMatrix4(g.matrixWorld);
    this.cyl.l.update(wp(0.75, 1.75, 1.35, this.root), wp(0.75, 0.3, 0.05, this.bladeG));
    this.cyl.r.update(wp(-0.75, 1.75, 1.35, this.root), wp(-0.75, 0.3, 0.05, this.bladeG));
    this.tracks[0].tex.offset.x += (this.trackL * dt) / 0.22;
    this.tracks[1].tex.offset.x += (this.trackR * dt) / 0.22;
    this.hyd = Math.abs(bl) * 0.8;
    this.load = clamp(resist * 1.3 + Math.abs(thr) * 0.3, 0, 1);
    this.updateCommon(dt, 0.15 + Math.abs(thr) * 0.6 + this.load * 0.4 + this.hyd * 0.3);
  }

  // Schild: Boden über Schneidkante abschälen und vor dem Schild anhäufen
  doBlade(dt) {
    const T = this.world.terrain;
    const B = this.world.buildings;
    this.bladeG.updateWorldMatrix(true, false);
    const m = this.bladeG.matrixWorld;
    const n = 9;
    const fwd = this.fwd;
    let vol = 0;
    let front = 0;
    const sp = (this.trackL + this.trackR) / 2;
    const moving = Math.abs(sp) > 0.03;
    for (let i = 0; i < n; i++) {
      const lx = -this.bladeW / 2 + (this.bladeW * (i + 0.5)) / n;
      const p = _v.set(lx, -0.55, 0.15).applyMatrix4(m);
      if (i === (n >> 1)) this.edge.copy(p);
      const g = T.height(p.x, p.z);
      if (g > p.y + 0.01 && moving) {
        const seg = this.bladeW / n / 2;
        const sx = m.elements[0], sz = m.elements[2];
        const ln = Math.sqrt(sx * sx + sz * sz) || 1;
        const r = T.cutSegment(p.x - (sx / ln) * seg, p.z - (sz / ln) * seg, p.x + (sx / ln) * seg, p.z + (sz / ln) * seg, p.y, 0.3, 4);
        if (r.vol > 0) {
          const dir = sp >= 0 ? 1 : -1;
          T.deposit(p.x + fwd.x * 0.75 * dir, p.z + fwd.z * 0.75 * dir, r.vol, r.mat, 0.4);
          vol += r.vol;
        }
      }
      // Material vor dem Schild (für Widerstand)
      const fx = p.x + fwd.x * 0.6, fz = p.z + fwd.z * 0.6;
      front += Math.max(0, T.height(fx, fz) - p.y) * 0.6 * (this.bladeW / n);
    }
    this.pushVol = damp(this.pushVol, front * 1.5, 4, dt);
    if (vol > 0.002 && Math.random() < 0.2) this.world.effects.dust(this.edge.x + fwd.x, this.edge.y + 0.5, this.edge.z + fwd.z, 1, 0x9a8668, 1);
    // Abbruch mit dem Schild
    const vel = _v2.copy(fwd).multiplyScalar(sp);
    for (const lx of [-1.2, 0, 1.2]) {
      const p = _v.set(lx, 0.1, 0.1).applyMatrix4(m);
      const res = B.hitSphere(p, 0.6, vel, 55, dt);
      if (res.blocked) {
        this.trackL *= 0.3; this.trackR *= 0.3;
      }
      B.pushDebris(p, 0.8, vel);
    }
  }

  soundState() {
    const s = super.soundState();
    s.track = (Math.abs(this.trackL) + Math.abs(this.trackR)) / 2;
    s.reverse = (this.trackL + this.trackR) / 2 < -0.1;
    return s;
  }
  cabin(out, look) {
    out.copy(this.cabEye).applyMatrix4(this.root.matrixWorld);
    look.set(0, -0.25, 1).transformDirection(this.root.matrixWorld);
  }
  hud() {
    const rel = this.edge.y - this.pos.y;
    return [
      ['Schild', `${rel >= 0 ? '+' : ''}${rel.toFixed(2)} m`, 'zur Kettenauflage'],
      ['Schubvolumen', `${this.pushVol.toFixed(1)} m³`, this.pushVol > 5 ? 'schwer' : ''],
      ['Tempo', `${(Math.abs(this.speed) * 3.6).toFixed(1)} km/h`, ''],
    ];
  }
  serialize() {
    return { ...super.serialize(), bl: this.blade };
  }
  deserialize(d) {
    super.deserialize(d);
    this.blade = d.bl;
  }
}

// ---------------- Lkw-Fahrgestell (3-Achser) ----------------
class Truck extends Vehicle {
  constructor(world, o) {
    super(world, { kind: 'wheeled', hl: 2.6, hw: 1.0, colHL: 4.3, colHW: 1.3, colOff: 0.1, maxSpeed: 11, revSpeed: 3, height: 3.4, exhaust: new THREE.Vector3(-0.9, 3.4, 2.2), audio: { idle: 650, max: 1900, cyl: 6, vol: 0.9 }, ...o });
    this.wheelbase = 4.2;
    this.accel = 1.9;
  }
  buildChassis(cabMat) {
    const pb = new PartBuilder();
    const D = MATS.dark, B = MATS.black, S = MATS.steel, W = cabMat;
    const ch = (this.chassis = new THREE.Group());
    this.root.add(ch);
    pb.box(D, 0.9, 0.3, 8.0, 0, 0.95, -0.3);
    for (const z of [3.3, -1.5, -2.85]) pb.cylX(D, 0.08, 2.0, 6, 0, 0.55, z);
    // Fahrerhaus
    pb.box(W, 2.45, 1.75, 1.9, 0, 2.15, 3.15);
    pb.box(W, 2.45, 0.45, 2.0, 0, 3.2, 3.05);
    pb.box(B, 2.3, 0.5, 0.12, 0, 1.35, 4.12); // Stoßstange
    pb.box(D, 2.35, 0.45, 0.1, 0, 1.85, 4.12); // Grill
    pb.box(W, 2.5, 0.35, 1.4, 0, 1.2, 3.35);
    pb.box(B, 0.08, 0.45, 0.25, 1.35, 2.6, 3.95);
    pb.box(B, 0.08, 0.45, 0.25, -1.35, 2.6, 3.95);
    pb.box(S, 0.6, 0.5, 1.0, 0.8, 1.15, 1.5); // Tank
    pb.box(B, 0.5, 0.45, 0.8, -0.8, 1.15, 1.6);
    pb.cyl(S, 0.07, 0.07, 1.3, 8, -0.9, 2.8, 2.25);
    pb.build(ch);
    const glass = new THREE.Mesh(new THREE.BoxGeometry(2.3, 0.85, 0.08), MATS.glass);
    glass.position.set(0, 2.55, 4.08);
    glass.rotation.x = -0.08;
    const sideG = new THREE.Mesh(new THREE.BoxGeometry(2.48, 0.7, 1.1), MATS.glass);
    sideG.position.set(0, 2.55, 3.35);
    ch.add(glass, sideG);
    for (const sx of [-0.85, 0.85]) {
      const l = new THREE.Mesh(new THREE.BoxGeometry(0.35, 0.18, 0.06), MATS.light);
      l.position.set(sx, 1.65, 4.17);
      ch.add(l);
      const t = new THREE.Mesh(new THREE.BoxGeometry(0.25, 0.15, 0.05), MATS.tail);
      t.position.set(sx * 1.2, 1.0, -4.3);
      ch.add(t);
    }
    this.beacon = makeBeacon(ch, 0.6, 3.45, 3.4);
    this.cabEye = new THREE.Vector3(0.55, 2.75, 3.2);
    // Räder: Lenkachse vorne, Tandem hinten
    this.wheels = [];
    this.steerWheels = [];
    const r = 0.53;
    for (const z of [3.3, -1.5, -2.85]) {
      for (const sx of [-1, 1]) {
        const w = makeWheel(r, 0.42, MATS.steel, 0.6);
        w.position.set(sx * 1.02, r, z);
        ch.add(w);
        this.wheels.push(w);
        if (z > 0) this.steerWheels.push(w);
      }
    }
    this.wheelR = r;
  }
  spinWheels(dt) {
    for (const w of this.wheels) w.userData.spin.rotation.x += (this.speed * dt) / this.wheelR;
    for (const w of this.steerWheels) w.rotation.y = this.steer;
  }
  controlsBase() {
    return { up: 'Gas', down: 'Bremse/Rück', left: 'Lenken L', right: 'Lenken R' };
  }
  soundState() {
    const s = super.soundState();
    s.reverse = this.speed < -0.1;
    return s;
  }
  cameraTarget(out) {
    return out.set(this.pos.x, this.pos.y + 2.4, this.pos.z);
  }
  cabin(out, look) {
    out.copy(this.cabEye).applyMatrix4(this.root.matrixWorld);
    look.set(0, -0.12, 1).transformDirection(this.root.matrixWorld);
  }
}

// ---------------- Dreiseitenkipper / Muldenkipper ----------------
export class DumpTruck extends Truck {
  constructor(world, x, z, yaw) {
    super(world, { name: 'Kipper-Lkw', short: 'Kipper', x, z, yaw, mass: 13 });
    this.bedA = 0; this.bedV = 0;
    this.cargo = 0;
    this.cargoMat = MAT.SOIL;
    this.cap = 12;
    this.spillAcc = 0;
    this.build();
    world.catchers.push(this);
  }
  build() {
    this.buildChassis(MATS.white);
    const pb = new PartBuilder();
    const Y = MATS.orange, D = MATS.dark;
    // Mulde mit Kipppunkt am Heck
    const bed = (this.bedG = new THREE.Group());
    bed.position.set(0, 1.2, -3.85);
    this.chassis.add(bed);
    const L = 5.4, Wd = 2.4, H = 1.15;
    this.bedDims = { L, W: Wd, H, z0: 0.15 };
    pb.box(Y, Wd, 0.12, L, 0, 0.06, L / 2 + 0.15);
    pb.box(Y, 0.08, H, L, Wd / 2 - 0.04, H / 2 + 0.1, L / 2 + 0.15);
    pb.box(Y, 0.08, H, L, -Wd / 2 + 0.04, H / 2 + 0.1, L / 2 + 0.15);
    pb.box(Y, Wd, H + 0.35, 0.1, 0, (H + 0.35) / 2 + 0.1, L + 0.15);
    pb.box(Y, Wd, 0.1, 0.6, 0, H + 0.4, L + 0.45); // Schutzdach
    for (let i = 1; i < 5; i++) {
      pb.box(D, 0.1, H * 0.9, 0.1, Wd / 2 + 0.03, H / 2 + 0.1, 0.15 + (L * i) / 5);
      pb.box(D, 0.1, H * 0.9, 0.1, -Wd / 2 - 0.03, H / 2 + 0.1, 0.15 + (L * i) / 5);
    }
    pb.build(bed);
    // Heckklappe (hängt oben)
    const gate = (this.gateG = new THREE.Group());
    gate.position.set(0, H + 0.1, 0.1);
    bed.add(gate);
    pb.box(Y, Wd, H, 0.08, 0, -H / 2, 0);
    pb.build(gate);
    // Ladung
    const cg = new THREE.BoxGeometry(Wd - 0.2, 1, L - 0.25, 4, 1, 6);
    const pos = cg.attributes.position;
    for (let i = 0; i < pos.count; i++) {
      if (pos.getY(i) > 0) {
        const x = pos.getX(i) / (Wd / 2), z = (pos.getZ(i)) / (L / 2);
        pos.setY(i, 0.5 + 0.35 * (1 - x * x) * (1 - z * z * 0.6));
      }
    }
    cg.computeVertexNormals();
    cg.translate(0, 0.5, 0);
    this.cargoMesh = new THREE.Mesh(cg, new THREE.MeshLambertMaterial({ color: 0x5a4030 }));
    this.cargoMesh.position.set(0, 0.12, L / 2 + 0.15);
    this.cargoMesh.castShadow = true;
    this.cargoMesh.visible = false;
    bed.add(this.cargoMesh);
    this.ram = new HydCylinder(this.world.scene, 0.13, 0.09, 1.0, MATS.steel);
    this.bedInv = new THREE.Matrix4();
  }
  payloadMass() {
    return this.cargo * (MAT_DENSITY[this.cargoMat] || 1.6);
  }
  // Fängt fallendes Material in der Mulde auf
  tryCatch(o) {
    if (o.mat === MAT.CONCRETE && o.vol <= 0) return false;
    const L = this.bedDims.L, W = this.bedDims.W;
    _v.set(o.x, o.y, o.z).applyMatrix4(this.bedInv);
    if (Math.abs(_v.x) > W / 2 - 0.05 || _v.z < 0.2 || _v.z > L + 0.1) return false;
    const fill = this.cargo / this.cap;
    const top = 0.15 + fill * 1.35;
    if (_v.y > top + 0.25 || _v.y < -0.4) return false;
    if (this.cargo >= this.cap * 1.05) {
      // überläuft seitlich
      o.vx += _v.x > 0 ? 1.5 : -1.5;
      return false;
    }
    if (this.cargo < 0.05 || o.vol > this.cargo * 0.1) this.cargoMat = o.mat;
    this.cargo += o.vol;
    return true;
  }
  controls() {
    return { left: this.controlsBase(), right: { up: 'Mulde heben', down: 'Mulde senken', left: '', right: '' }, buttons: [] };
  }
  update(dt, inp) {
    let thr = 0, st = 0, bd = 0;
    if (inp) { thr = inp.ly; st = inp.lx; bd = inp.ry; }
    this.drive(dt, thr, st, 0);
    this.updateGround(dt);
    this.bedV = approach(this.bedV, bd > 0.2 ? 0.16 : bd < -0.2 ? -0.22 : 0, dt * 1.5);
    this.bedA = clamp(this.bedA + this.bedV * dt, 0, 0.9);
    if (this.bedA <= 0 || this.bedA >= 0.9) this.bedV = 0;
    this.bedG.rotation.x = -this.bedA;
    this.gateG.rotation.x = this.bedA * 0.92;
    this.root.updateMatrixWorld(true);
    this.bedInv.copy(this.bedG.matrixWorld).invert();
    // Abkippen: Material rutscht ab ca. 22°
    if (this.cargo > 0 && this.bedA > 0.38) {
      const flow = Math.min(this.cargo, this.cap * 0.32 * (this.bedA - 0.36) * dt * 3);
      this.cargo -= flow;
      this.spillAcc += flow;
      const chunk = 0.06;
      while (this.spillAcc > chunk) {
        this.spillAcc -= chunk;
        const p = _v.set((Math.random() - 0.5) * 2.0, 0.25 + Math.random() * 0.3, -0.1).applyMatrix4(this.bedG.matrixWorld);
        const dir = this.forward;
        this.world.soil.spawn(p.x, p.y, p.z, -dir.x * 1.2 + (Math.random() - 0.5) * 0.4, -0.3, -dir.z * 1.2 + (Math.random() - 0.5) * 0.4, chunk, this.cargoMat);
      }
      if (Math.random() < 0.2) this.world.audio.shot('soil', 1, 0.15);
      if (Math.random() < 0.1) this.world.effects.dust(this.pos.x - this.fwd.x * 5, this.pos.y + 0.5, this.pos.z - this.fwd.z * 5, 2, 0x9b8a70, 1.4);
    }
    const f = this.cargo / this.cap;
    this.cargoMesh.visible = f > 0.01;
    if (this.cargoMesh.visible) {
      this.cargoMesh.scale.y = Math.max(0.05, f * 1.15);
      const c = this.cargoMat === MAT.RUBBLE ? 0x8d8780 : this.cargoMat === MAT.GRAVEL ? 0x9b988f : this.cargoMat === MAT.CONCRETE ? 0x9a9a96 : 0x5a4030;
      this.cargoMesh.material.color.setHex(c).convertSRGBToLinear();
    }
    const wp = (x, y, z, g) => new THREE.Vector3(x, y, z).applyMatrix4(g.matrixWorld);
    this.ram.update(wp(0, 1.05, 1.45, this.chassis), wp(0, 0.05, 5.2, this.bedG));
    this.ram.setVisible(this.bedA > 0.02);
    this.spinWheels(dt);
    this.hyd = Math.abs(this.bedV) * 5;
    this.load = clamp(Math.abs(thr) * (0.4 + f * 0.5) + this.hyd * 0.4, 0, 1);
    this.updateCommon(dt, 0.1 + Math.abs(thr) * 0.6 + Math.abs(this.speed) / this.maxSpeed * 0.3 + this.hyd * 0.4);
  }
  hud() {
    const t = this.payloadMass();
    return [
      ['Ladung', `${this.cargo.toFixed(1)} / ${this.cap} m³`, this.cargo > 0.05 ? `${MAT_NAME[this.cargoMat]}, ${t.toFixed(1)} t` : 'leer'],
      ['Tempo', `${(Math.abs(this.speed) * 3.6).toFixed(0)} km/h`, this.speed < -0.1 ? 'rückwärts' : ''],
      ['Mulde', `${Math.round((this.bedA * 180) / Math.PI)}°`, t > 15 ? 'überladen!' : ''],
    ];
  }
  serialize() {
    return { ...super.serialize(), c: this.cargo, cm: this.cargoMat };
  }
  deserialize(d) {
    super.deserialize(d);
    this.cargo = d.c || 0;
    this.cargoMat = d.cm || 1;
  }
}

// ---------------- Fahrmischer ----------------
export class MixerTruck extends Truck {
  constructor(world, x, z, yaw) {
    super(world, { name: 'Fahrmischer', short: 'Mischer', x, z, yaw, mass: 14 });
    this.cap = 9;
    this.concrete = 9;
    this.pouring = false;
    this.chuteYaw = 0;
    this.chuteP = 0.5;
    this.drumA = 0;
    this.pourAcc = 0;
    this.fillT = 0;
    this.build();
  }
  build() {
    this.buildChassis(MATS.blue);
    const pb = new PartBuilder();
    // Trommel, geneigt
    const drumG = (this.drumG = new THREE.Group());
    drumG.position.set(0, 2.45, -0.9);
    drumG.rotation.x = -0.2;
    this.chassis.add(drumG);
    const spin = (this.drumSpin = new THREE.Group());
    drumG.add(spin);
    const prof = [];
    for (let i = 0; i <= 14; i++) {
      const t = i / 14;
      const r = t < 0.12 ? 0.55 + t * 3.5 : t < 0.6 ? 1.0 : 1.0 - (t - 0.6) * 1.4;
      prof.push(new THREE.Vector2(Math.max(0.35, r), (t - 0.5) * 5.0));
    }
    const lg = new THREE.LatheGeometry(prof, 20);
    lg.rotateX(-Math.PI / 2);
    const drum = new THREE.Mesh(lg, MATS.drum);
    drum.castShadow = true;
    spin.add(drum);
    // Mischwendel (sichtbare Streifen)
    for (let i = 0; i < 6; i++) {
      const stripe = new THREE.Mesh(new THREE.BoxGeometry(0.12, 0.05, 3.2), MATS.red);
      const a = (i / 6) * Math.PI * 2;
      stripe.position.set(Math.cos(a) * 1.0, Math.sin(a) * 1.0, 0.2);
      stripe.rotation.z = a;
      spin.add(stripe);
    }
    pb.box(MATS.dark, 1.4, 0.3, 4.6, 0, 1.25, -0.9);
    pb.box(MATS.blue, 1.3, 1.0, 0.25, 0, 1.8, 1.4);
    pb.box(MATS.steel, 0.9, 0.7, 0.9, 0, 3.3, -3.6); // Einfülltrichter
    pb.build(this.chassis);
    // Schurre
    const ch = (this.chuteG = new THREE.Group());
    ch.position.set(0, 2.2, -3.95);
    this.chassis.add(ch);
    const chA = (this.chuteArm = new THREE.Group());
    ch.add(chA);
    pb.box(MATS.steel, 0.42, 0.06, 1.8, 0, 0, -0.9);
    pb.box(MATS.steel, 0.04, 0.18, 1.8, 0.21, 0.08, -0.9);
    pb.box(MATS.steel, 0.04, 0.18, 1.8, -0.21, 0.08, -0.9);
    pb.build(chA);
    this.chuteEnd = new THREE.Vector3(0, 0, -1.8);
  }
  payloadMass() {
    return this.concrete * 2.35;
  }
  controls() {
    return {
      left: this.controlsBase(),
      right: { up: 'Schurre auf', down: 'Schurre ab', left: 'Schurre L', right: 'Schurre R' },
      buttons: [{ id: 'pour', label: this.pouring ? 'Stopp' : 'Beton ablassen', hint: 'Trommel' }],
    };
  }
  onButton(id) {
    if (id === 'pour') {
      if (!this.pouring && this.concrete <= 0.01) {
        this.world.ui.toast('Trommel leer – am Betonwerk neu beladen.', 'warn');
        return true;
      }
      this.pouring = !this.pouring;
      return true;
    }
  }
  update(dt, inp) {
    let thr = 0, st = 0;
    if (inp) {
      thr = inp.ly; st = inp.lx;
      this.chuteYaw = clamp(this.chuteYaw - inp.rx * 0.8 * dt, -1.2, 1.2);
      this.chuteP = clamp(this.chuteP - inp.ry * 0.5 * dt, 0.15, 0.9);
    }
    this.drive(dt, thr, st, 0);
    this.updateGround(dt);
    this.chuteG.rotation.y = this.chuteYaw;
    this.chuteArm.rotation.x = -this.chuteP;
    // Trommel: Mischen langsam, Entleeren rückwärts schnell
    this.drumA += dt * (this.pouring ? -2.6 : 0.9);
    this.drumSpin.rotation.z = this.drumA;
    this.root.updateMatrixWorld(true);
    if (this.pouring) {
      if (this.concrete <= 0) {
        this.pouring = false;
        this.world.ui.toast('Fahrmischer leer.', 'info');
      } else {
        const rate = 0.45 * dt;
        const v = Math.min(rate, this.concrete);
        this.concrete -= v;
        this.pourAcc += v;
        const end = _v.copy(this.chuteEnd).applyMatrix4(this.chuteArm.matrixWorld);
        const dir = _v2.set(0, 0, -1).transformDirection(this.chuteArm.matrixWorld);
        while (this.pourAcc > 0.03) {
          this.pourAcc -= 0.03;
          this.world.soil.spawn(end.x + (Math.random() - 0.5) * 0.2, end.y, end.z + (Math.random() - 0.5) * 0.2, dir.x * 1.2, dir.y * 1.2, dir.z * 1.2, 0.03, MAT.CONCRETE, 0.12);
        }
      }
    }
    // Betonwerk: Beladung
    const plant = this.world.plant;
    if (plant) {
      const dx = this.pos.x - plant.x, dz = this.pos.z - plant.z;
      if (dx * dx + dz * dz < 9 && Math.abs(this.speed) < 0.2 && this.concrete < this.cap) {
        this.fillT += dt;
        this.concrete = Math.min(this.cap, this.concrete + dt * 1.2);
        if (Math.random() < 0.3) this.world.effects.dust(plant.x, this.pos.y + 3.6, plant.z, 1, 0xc8c8c2, 0.8);
        if (this.concrete >= this.cap) this.world.ui.toast('Fahrmischer beladen: 9 m³ Beton C25/30.', 'ok');
      }
    }
    this.spinWheels(dt);
    this.hyd = this.pouring ? 0.5 : 0.1;
    this.load = clamp(Math.abs(thr) * 0.5 + (this.pouring ? 0.4 : 0.1), 0, 1);
    this.updateCommon(dt, 0.15 + Math.abs(thr) * 0.6 + (this.pouring ? 0.35 : 0));
  }
  hud() {
    return [
      ['Beton', `${this.concrete.toFixed(1)} / ${this.cap} m³`, this.pouring ? 'Entleeren…' : 'Mischen'],
      ['Tempo', `${(Math.abs(this.speed) * 3.6).toFixed(0)} km/h`, ''],
      ['Schurre', `${Math.round((this.chuteYaw * 180) / Math.PI)}°`, ''],
    ];
  }
  serialize() {
    return { ...super.serialize(), c: this.concrete };
  }
  deserialize(d) {
    super.deserialize(d);
    this.concrete = d.c ?? 9;
  }
}
