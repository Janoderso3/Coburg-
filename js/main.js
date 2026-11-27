// Bau-Sandbox: Hauptprogramm (Welt, Kamera, Oberfläche, Spielschleife)
import { clamp, lerp, damp, dampAngle, smoothstep, fbm, rng, col, noiseTexture, wrapAngle } from './util.js';
import { Terrain, MAT, MAT_NAME } from './terrain.js';
import { SoilParticles, Effects } from './effects.js';
import { AudioSys } from './audio.js';
import { initMaterials, MATS, PartBuilder, textTexture, mergeGeometries } from './models.js';
import { BuildingSystem, ConstructionSite, DESIGNS, oldHouseBlueprint, oldGarageBlueprint, Blueprint } from './buildings.js';
import { Excavator, WheelLoader, Dozer, DumpTruck, MixerTruck } from './vehicles.js';
import { TowerCrane, WreckingCrane } from './crane.js';
import { Input } from './input.js';

const SAVE_KEY = 'bau-sandbox-save-v1';
const SET_KEY = 'bau-sandbox-settings-v1';
const isTouch = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
const store = {
  get(k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
  set(k, v) { try { localStorage.setItem(k, v); return true; } catch (e) { return false; } },
  del(k) { try { localStorage.removeItem(k); } catch (e) {} },
};
const settings = Object.assign({ iso: false, quality: isTouch ? 'mittel' : 'hoch', sound: true }, JSON.parse(store.get(SET_KEY) || '{}'));
const saveSettings = () => store.set(SET_KEY, JSON.stringify(settings));

// ---------------- Renderer ----------------
const canvas = document.getElementById('c');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: settings.quality !== 'niedrig', powerPreference: 'high-performance' });
renderer.outputEncoding = THREE.sRGBEncoding;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.05;
renderer.shadowMap.enabled = settings.quality !== 'niedrig';
renderer.shadowMap.type = THREE.PCFShadowMap;
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(55, 1, 0.1, 1600);
initMaterials();

// Himmel
const skyMat = new THREE.ShaderMaterial({
  side: THREE.BackSide, depthWrite: false, fog: false,
  uniforms: { top: { value: new THREE.Color(0x5f93d3) }, mid: { value: new THREE.Color(0xa9c8ea) }, bot: { value: new THREE.Color(0xdfe6e8) }, sun: { value: new THREE.Vector3(0.45, 0.6, 0.35).normalize() } },
  vertexShader: 'varying vec3 vD; void main(){ vD = normalize(position); vec4 p = projectionMatrix * modelViewMatrix * vec4(position,1.0); gl_Position = p.xyww; }',
  fragmentShader: `uniform vec3 top; uniform vec3 mid; uniform vec3 bot; uniform vec3 sun; varying vec3 vD;
    void main(){ float h = vD.y; vec3 c = h > 0.0 ? mix(mid, top, pow(clamp(h,0.0,1.0), 0.55)) : bot;
      c = mix(bot, c, smoothstep(-0.02, 0.12, h));
      float s = max(0.0, dot(normalize(vD), sun)); c += vec3(1.0,0.93,0.8) * (pow(s, 600.0) * 1.5 + pow(s, 12.0) * 0.18);
      gl_FragColor = vec4(c, 1.0); }`,
});
const sky = new THREE.Mesh(new THREE.SphereGeometry(1200, 24, 12), skyMat);
sky.frustumCulled = false;
scene.add(sky);
scene.fog = new THREE.Fog(col(0xd3dde3), 120, 700);
const hemi = new THREE.HemisphereLight(col(0xcfe3ff), col(0x6b5a40), 0.62);
scene.add(hemi);
const sunDir = new THREE.Vector3(0.45, 0.6, 0.35).normalize();
const sun = new THREE.DirectionalLight(col(0xfff0d8), 1.25);
sun.castShadow = renderer.shadowMap.enabled;
const SH = settings.quality === 'hoch' ? 2048 : 1024;
sun.shadow.mapSize.set(SH, SH);
const sc = sun.shadow.camera;
sc.left = -34; sc.right = 34; sc.top = 34; sc.bottom = -34; sc.near = 1; sc.far = 200;
sun.shadow.bias = -0.0006;
sun.shadow.normalBias = 0.03;
scene.add(sun, sun.target);

// ---------------- Welt ----------------
const audio = new AudioSys();
audio.enabled = settings.sound;
const world = {
  scene, audio, settings, catchers: [], vehicles: [], colliders: [], ui: null,
  yard: { x0: -21, x1: -9, z0: 4, z1: 13.5 },
  plant: { x: -25, z: 23.5 },
};
const terrain = new Terrain(scene, 64, 0.25);
world.terrain = terrain;
world.effects = new Effects(world);
world.soil = new SoilParticles(world);
const buildings = new BuildingSystem(world);
world.buildings = buildings;

world.groundHeight = (x, z, maxY) => Math.max(terrain.height(x, z), buildings.topAt(x, z, maxY));

// Lage der Objekte
const L = {
  crane: [-3, -3],
  house: { x: 14, z: -14, yaw: 0 },
  garage: { x: 23.5, z: -3.5, yaw: 0 },
  plot: { x: -15, z: -14 },
  heap: { x: 13, z: 13, r: 5.6, h: 3.2 },
  hill: { x: -25, z: -25 },
  dep: { x0: 22.5, x1: 30.5, z0: 23, z1: 30.5 },
};

function boxDist(x, z, cx, cz, hx, hz) {
  const dx = Math.max(0, Math.abs(x - cx) - hx), dz = Math.max(0, Math.abs(z - cz) - hz);
  return Math.sqrt(dx * dx + dz * dz);
}
function baseHeight(x, z) {
  let h = 0.45 * fbm(x / 24 + 4.1, z / 24 - 2.3, 3) + 0.12 * fbm(x / 7 + 10, z / 7 + 3, 2);
  h += 3.4 * Math.exp(-((x - L.hill.x) ** 2 + (z - L.hill.z) ** 2) / 70);
  // Unebenheiten am Bauplatz
  const dp = Math.hypot(x - L.plot.x, z - L.plot.z);
  h += (0.55 * fbm(x / 3.2 + 7, z / 3.2 - 1, 2) + 0.25) * (1 - smoothstep(4, 9, dp));
  return h;
}
function genTerrain(x, z) {
  let h = baseHeight(x, z);
  let surf = 0, zone = 0, fill = 0;
  const pad = (cx, cz, hx, hz, blend, y) => {
    const d = boxDist(x, z, cx, cz, hx, hz);
    const w = 1 - smoothstep(0, blend, d);
    h = lerp(h, y, w);
    return d;
  };
  const yH = baseHeight(L.house.x, L.house.z);
  pad(L.house.x, L.house.z, 5.6, 4.9, 3, yH);
  const yG = baseHeight(L.garage.x, L.garage.z);
  pad(L.garage.x, L.garage.z, 2.6, 3.8, 2.5, yG);
  const yC = baseHeight(L.crane[0], L.crane[1]);
  pad(L.crane[0], L.crane[1], 3, 3, 2.5, yC);
  // Lagerplatz (Schotter)
  const Y = world.yard;
  const yY = baseHeight((Y.x0 + Y.x1) / 2, (Y.z0 + Y.z1) / 2);
  if (pad((Y.x0 + Y.x1) / 2, (Y.z0 + Y.z1) / 2, (Y.x1 - Y.x0) / 2, (Y.z1 - Y.z0) / 2, 3, yY) < 0.01) surf = MAT.GRAVEL;
  // Baustraße
  const roadD = Math.abs(z - 19.5);
  if (roadD < 2.6) {
    h = lerp(h, 0.05 + 0.02 * x * 0.0, 1 - smoothstep(1.6, 2.6, roadD));
    if (roadD < 2.0) surf = MAT.GRAVEL;
  }
  // Betonwerk
  const yP = 0.1;
  if (pad(world.plant.x, world.plant.z + 1.5, 4.5, 6, 2.5, yP) < 0.01) surf = MAT.GRAVEL;
  // Deponie (Mulde)
  const D = L.dep;
  const dd = boxDist(x, z, (D.x0 + D.x1) / 2, (D.z0 + D.z1) / 2, (D.x1 - D.x0) / 2, (D.z1 - D.z0) / 2);
  const din = Math.min(x - D.x0, D.x1 - x, z - D.z0, D.z1 - z);
  if (dd < 2) h = lerp(h, 0.1, 1 - smoothstep(0, 2, dd));
  if (din > 0) {
    h -= 1.3 * smoothstep(0, 2.5, din);
    if (din > 1.0) zone = 1;
    surf = MAT.GRAVEL;
  }
  // Rand auf Null auslaufen lassen
  const e = Math.max(Math.abs(x), Math.abs(z));
  h = lerp(h, 0, smoothstep(28.5, 31.5, e));
  // Erdhaufen (loser Boden)
  const dh = Math.hypot(x - L.heap.x, z - L.heap.z);
  if (dh < L.heap.r) {
    const t = 1 - dh / L.heap.r;
    fill = L.heap.h * Math.pow(t, 1.15) * (0.92 + 0.08 * fbm(x, z, 1));
    h += fill;
  }
  return { h, surf, fill, fillMat: fill ? MAT.SOIL : 0, zone };
}
terrain.generate(genTerrain);
terrain.zoneBox = null;
{
  const D = L.dep;
  const i0 = Math.floor(terrain.gx(D.x0)), i1 = Math.ceil(terrain.gx(D.x1));
  const j0 = Math.floor(terrain.gx(D.z0)), j1 = Math.ceil(terrain.gx(D.z1));
  terrain.zoneBox = [i0, j0, i1, j1];
}

// ---------------- Kulisse ----------------
function addCollider(x, z, hw, hl, yaw = 0) {
  world.colliders.push({ x, z, hw, hl, yaw });
}
function buildScenery() {
  const R = rng(99);
  // Außengelände
  const gTex = noiseTexture(256, { base: 200, amp: 60, seed: 5 });
  gTex.repeat.set(120, 120);
  const outer = new THREE.Mesh(new THREE.PlaneGeometry(2400, 2400), new THREE.MeshLambertMaterial({ color: col(0x5d7a35), map: gTex }));
  outer.rotation.x = -Math.PI / 2;
  outer.position.y = -0.03;
  outer.receiveShadow = true;
  scene.add(outer);
  // Straße außerhalb
  const road = new THREE.Mesh(new THREE.PlaneGeometry(10, 900), new THREE.MeshLambertMaterial({ color: col(0x4a4a4c) }));
  road.rotation.x = -Math.PI / 2;
  road.position.set(40, 0.0, 0);
  road.receiveShadow = true;
  scene.add(road);
  const access = new THREE.Mesh(new THREE.PlaneGeometry(8, 5), new THREE.MeshLambertMaterial({ color: col(0x8f8b82) }));
  access.rotation.x = -Math.PI / 2;
  access.position.set(35.5, 0.01, 19.5);
  scene.add(access);
  // Bauzaun
  const fc = document.createElement('canvas');
  fc.width = 128; fc.height = 64;
  const g = fc.getContext('2d');
  g.strokeStyle = '#c9ced2'; g.lineWidth = 1.2;
  for (let i = 0; i <= 128; i += 6) { g.beginPath(); g.moveTo(i, 0); g.lineTo(i, 64); g.stroke(); }
  for (let i = 0; i <= 64; i += 8) { g.beginPath(); g.moveTo(0, i); g.lineTo(128, i); g.stroke(); }
  g.lineWidth = 5; g.strokeStyle = '#dfe3e6'; g.strokeRect(2, 2, 124, 60);
  const ft = new THREE.CanvasTexture(fc);
  ft.encoding = THREE.sRGBEncoding;
  const panelGeo = new THREE.PlaneGeometry(3.45, 2.0);
  const panelMat = new THREE.MeshLambertMaterial({ map: ft, transparent: true, alphaTest: 0.3, side: THREE.DoubleSide });
  const feetGeo = new THREE.BoxGeometry(0.7, 0.15, 0.22);
  const panels = [];
  const E = 31.7;
  for (let s = 0; s < 4; s++) {
    for (let i = 0; i < 18; i++) {
      const t = -E + 1.75 + i * 3.5;
      let x, z, yaw;
      if (s === 0) { x = t; z = -E; yaw = 0; }
      else if (s === 1) { x = t; z = E; yaw = 0; }
      else if (s === 2) { x = -E; z = t; yaw = Math.PI / 2; }
      else { x = E; z = t; yaw = Math.PI / 2; if (Math.abs(t - 19.5) < 3) continue; }
      if (Math.abs(x) > E + 0.1 || Math.abs(z) > E + 0.1) continue;
      panels.push([x, z, yaw]);
    }
  }
  const pm = new THREE.InstancedMesh(panelGeo, panelMat, panels.length);
  const fm = new THREE.InstancedMesh(feetGeo, new THREE.MeshLambertMaterial({ color: col(0x9a9894) }), panels.length * 2);
  const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler(), v = new THREE.Vector3(), one = new THREE.Vector3(1, 1, 1);
  panels.forEach(([x, z, yaw], i) => {
    e.set(0, yaw, 0); q.setFromEuler(e);
    m4.compose(v.set(x, 1.1, z), q, one);
    pm.setMatrixAt(i, m4);
    for (const k of [0, 1]) {
      const o = (k ? 1.72 : -1.72);
      m4.compose(v.set(x + Math.cos(yaw) * o, 0.07, z - Math.sin(yaw) * o), q, one);
      fm.setMatrixAt(i * 2 + k, m4);
    }
  });
  pm.castShadow = true;
  scene.add(pm, fm);
  // Bäume
  const trunks = [], crowns = [];
  for (let i = 0; i < 140; i++) {
    const a = R() * Math.PI * 2, d = 40 + R() * 160;
    const x = Math.cos(a) * d, z = Math.sin(a) * d;
    if (Math.abs(x - 40) < 8) continue;
    const s = 0.7 + R() * 0.8;
    trunks.push([x, z, s]);
    crowns.push([x, z, s, R()]);
  }
  const tm = new THREE.InstancedMesh(new THREE.CylinderGeometry(0.18, 0.28, 3, 6), new THREE.MeshLambertMaterial({ color: col(0x5a4330) }), trunks.length);
  const cm = new THREE.InstancedMesh(new THREE.IcosahedronGeometry(2.4, 1), new THREE.MeshLambertMaterial({ color: col(0x3f6a2c) }), crowns.length);
  const cc = new THREE.Color();
  trunks.forEach(([x, z, s], i) => {
    m4.compose(v.set(x, 1.5 * s, z), q.identity(), new THREE.Vector3(s, s, s));
    tm.setMatrixAt(i, m4);
    const [, , , r] = crowns[i];
    m4.compose(v.set(x, (3.6 + r) * s, z), q.identity(), new THREE.Vector3(s * (1 + r * 0.3), s * (1.2 + r * 0.4), s * (1 + r * 0.3)));
    cm.setMatrixAt(i, m4);
    cm.setColorAt(i, cc.copy(col(0x3f6a2c)).lerp(col(0x5f7f2c), r));
  });
  tm.castShadow = cm.castShadow = true;
  scene.add(tm, cm);
  // Nachbarhäuser (Kulisse)
  const pb = new PartBuilder();
  const wallMats = [MATS.white, new THREE.MeshLambertMaterial({ color: col(0xe8d9b5) }), new THREE.MeshLambertMaterial({ color: col(0xd9c7c0) })];
  const roofMat = new THREE.MeshLambertMaterial({ color: col(0x9a3f2c) });
  const houses = [[-55, -10], [-58, 22], [-50, 50], [-12, 58], [18, 60], [60, -48], [62, -15], [64, 24], [58, 58], [-40, -58], [-5, -60], [28, -62], [92, 10], [95, -30], [-90, 5]];
  houses.forEach(([x, z], i) => {
    const w = 8 + R() * 4, d = 7 + R() * 3, h = 5 + R() * 3;
    const yaw = R() < 0.5 ? 0 : Math.PI / 2;
    pb.box(wallMats[i % 3], w, h, d, x, h / 2, z, 0, yaw, 0);
    const roof = new THREE.CylinderGeometry(0.01, d * 0.72, w + 0.6, 4, 1);
    roof.rotateY(Math.PI / 4);
    roof.scale(1, 1, 1);
    const rg = new THREE.CylinderGeometry(d * 0.75, d * 0.75, w + 0.8, 3, 1);
    rg.rotateZ(Math.PI / 2);
    rg.scale(1, 0.62, 1);
    pb.add(rg, roofMat, x, h + d * 0.22, z, 0, yaw, 0);
  });
  // Ferne Burg auf einem Hügel
  const hill = new THREE.SphereGeometry(1, 20, 10, 0, Math.PI * 2, 0, Math.PI / 2);
  hill.scale(260, 70, 180);
  pb.add(hill, new THREE.MeshLambertMaterial({ color: col(0x56743a) }), -380, -8, -520);
  const stone = new THREE.MeshLambertMaterial({ color: col(0xcfc6b2) });
  pb.box(stone, 70, 14, 34, -380, 66, -520);
  pb.box(stone, 12, 30, 12, -405, 74, -520);
  pb.box(stone, 16, 22, 16, -350, 70, -512);
  pb.box(roofMat, 22, 6, 36, -372, 76, -520);
  pb.build(scene, false, false);
}
buildScenery();

// Bauten auf dem Gelände: Container, Betonwerk, Deponie, Schilder
function sign(text, x, z, yaw, w = 2.4, hgt = 0.9) {
  const tex = textTexture(text, { w: 512, h: 192, font: 'bold 56px sans-serif', bg: '#fdfcf6' });
  const y = terrain.height(x, z);
  const back = new THREE.MeshLambertMaterial({ color: col(0x8d9094) });
  const plate = new THREE.Mesh(new THREE.BoxGeometry(w, hgt, 0.04), [back, back, back, back, new THREE.MeshBasicMaterial({ map: tex }), back]);
  plate.position.set(x, y + 1.6, z);
  plate.rotation.y = yaw;
  const post = new THREE.Mesh(new THREE.BoxGeometry(0.08, 1.6, 0.08), MATS.steel);
  post.position.set(x, y + 0.8, z);
  scene.add(plate, post);
}
function buildProps() {
  const pb = new PartBuilder();
  // Baucontainer
  const cont = (x, z, y0, yaw, mat) => {
    pb.box(mat, 2.45, 2.6, 6.0, x, y0 + 1.3, z, 0, yaw, 0);
    pb.box(MATS.glass, 2.5, 0.8, 1.4, x, y0 + 1.6, z - 1, 0, yaw, 0);
    pb.box(MATS.dark, 2.5, 0.12, 6.05, x, y0 + 2.62, z, 0, yaw, 0);
  };
  const cy = terrain.height(26, 8);
  cont(26, 8, cy, 0, MATS.white);
  cont(26, 8, cy + 2.62, 0, new THREE.MeshStandardMaterial({ color: col(0x3d6fb6), roughness: 0.6 }));
  addCollider(26, 8, 1.3, 3.1);
  // Dixi
  const dy = terrain.height(26, 13);
  pb.box(new THREE.MeshStandardMaterial({ color: col(0x2b6fd0), roughness: 0.5 }), 1.15, 2.3, 1.15, 26, dy + 1.15, 13);
  addCollider(26, 13, 0.6, 0.6);
  // Paletten mit Steinen
  for (let i = 0; i < 3; i++) {
    const x = -7 + i * 1.5, z = 15.5, y = terrain.height(x, z);
    pb.box(MATS.wood, 1.2, 0.14, 0.8, x, y + 0.07, z);
    pb.box(new THREE.MeshLambertMaterial({ color: col(0xb5583e) }), 1.1, 0.8, 0.75, x, y + 0.55, z);
    addCollider(x, z, 0.6, 0.4);
  }
  // Betonwerk (Silo auf Stützen, Lkw fährt darunter)
  const P = world.plant;
  const py = terrain.height(P.x, P.z);
  const steel = MATS.steel;
  for (const [lx, lz] of [[-2.4, -2.0], [2.4, -2.0], [-2.4, 2.0], [2.4, 2.0]]) {
    pb.box(steel, 0.35, 5.2, 0.35, P.x + lx, py + 2.6, P.z + lz);
    addCollider(P.x + lx, P.z + lz, 0.3, 0.3);
  }
  pb.box(steel, 5.2, 0.3, 4.4, P.x, py + 5.3, P.z);
  pb.cyl(new THREE.MeshStandardMaterial({ color: col(0xd8d8d0), roughness: 0.6 }), 2.0, 2.0, 7.5, 18, P.x, py + 9.3, P.z);
  pb.cyl(new THREE.MeshStandardMaterial({ color: col(0xd8d8d0), roughness: 0.6 }), 0.4, 2.0, 1.8, 18, P.x, py + 4.65, P.z);
  pb.cyl(MATS.dark, 0.3, 0.3, 1.0, 10, P.x, py + 3.6, P.z);
  pb.box(MATS.orange, 2.6, 0.5, 0.2, P.x, py + 5.8, P.z + 2.25);
  pb.box(new THREE.MeshStandardMaterial({ color: col(0x9aa0a6), roughness: 0.7 }), 3.2, 6.5, 3.2, P.x - 5.2, py + 3.25, P.z + 3.5);
  addCollider(P.x - 5.2, P.z + 3.5, 1.7, 1.7);
  // Deponie: Legio-Betonblöcke
  const D = L.dep;
  const block = (x, z, yaw) => {
    const y = terrain.height(x, z);
    pb.box(MATS.concrete, 1.6, 0.8, 0.8, x, y + 0.4, z, 0, yaw, 0);
    addCollider(x, z, yaw ? 0.4 : 0.8, yaw ? 0.8 : 0.4);
  };
  for (let x = D.x0 + 0.8; x < D.x1; x += 1.65) block(x, D.z1 + 0.9, 0);
  for (let z = D.z0 + 1; z < D.z1; z += 1.65) block(D.x1 + 0.9, z, Math.PI / 2);
  pb.build(scene, true, true);
  sign('BETONWERK\nTrichter unter Silo', P.x + 3.6, P.z - 3.4, 0.3);
  sign('DEPONIE\nhier abkippen', D.x0 - 1, D.z0 - 1.2, -0.6);
  sign('LAGERPLATZ\nFertigteile', world.yard.x1 + 1, world.yard.z0 - 0.8, 0);
  sign('ERDAUSHUB', L.heap.x - 6, L.heap.z + 4.5, 0.5, 1.9, 0.6);
}
buildProps();

// Gebäude (Abrissobjekte)
function placeBlueprint(bp, x, z, yaw, name) {
  const st = terrain.statsRect(x, z, 5.5, 5, yaw);
  const y = st.mean;
  const b = buildings.createFromBlueprint(bp, x, y, z, yaw, name, 'old');
  b.baseY = y;
  return b;
}
function spawnOldBuildings() {
  placeBlueprint(oldHouseBlueprint(3), L.house.x, L.house.z, L.house.yaw, 'Altes Fachwerkhaus');
  placeBlueprint(oldGarageBlueprint(9), L.garage.x, L.garage.z, L.garage.yaw, 'Alte Garage');
}
spawnOldBuildings();

// Kran
const crane = new TowerCrane(world, L.crane[0], L.crane[1]);
world.crane = crane;
addCollider(L.crane[0], L.crane[1], 2.6, 2.6);

// Fahrzeuge
const VEH = [
  new Excavator(world, 4, 8.5, 0.9),
  new WheelLoader(world, 9, 0.5, -1.9),
  new Dozer(world, -9, -6, -1.7),
  new DumpTruck(world, 3, 16.5, -2.4),
  new MixerTruck(world, -16, 19.5, 1.57),
  new WreckingCrane(world, -3, -14, Math.PI / 2),
];
world.vehicles = VEH;
const CONTROLLABLE = [...VEH.slice(0, 5), crane, VEH[5]];

// OBB-Überlappung (2D)
function obb(a, b) {
  const ax = [Math.cos(a.yaw), -Math.sin(a.yaw)], af = [Math.sin(a.yaw), Math.cos(a.yaw)];
  const bx = [Math.cos(b.yaw), -Math.sin(b.yaw)], bf = [Math.sin(b.yaw), Math.cos(b.yaw)];
  const d = [b.x - a.x, b.z - a.z];
  for (const ax2 of [ax, af, bx, bf]) {
    const proj = (o, l, f) => Math.abs(l[0] * ax2[0] + l[1] * ax2[1]) * o.hw + Math.abs(f[0] * ax2[0] + f[1] * ax2[1]) * o.hl;
    const ra = proj(a, ax, af), rb = proj(b, bx, bf);
    if (Math.abs(d[0] * ax2[0] + d[1] * ax2[1]) > ra + rb) return false;
  }
  return true;
}
world.vehicleBlocked = (v, nx, nz, nyaw) => {
  const off = v.colOff || 0;
  const cx = nx + Math.sin(nyaw) * off, cz = nz + Math.cos(nyaw) * off;
  const me = { x: cx, z: cz, hw: v.colHW, hl: v.colHL, yaw: nyaw };
  const E = terrain.half - 1.2;
  const ext = Math.max(v.colHW, v.colHL) * 0.6;
  if (Math.abs(cx) > E - ext || Math.abs(cz) > E - ext) {
    // nur Bewegung blockieren, die nach außen führt
    const cur = v.footprint();
    if (Math.abs(cx) > Math.abs(cur.x) && Math.abs(cx) > E - ext) return true;
    if (Math.abs(cz) > Math.abs(cur.z) && Math.abs(cz) > E - ext) return true;
  }
  if (buildings.boxBlocked(cx, cz, v.colHW * 0.92, v.colHL * 0.92, nyaw, v.pos.y, v.pos.y + v.height)) return true;
  for (const c of world.colliders) if (obb(me, c)) {
    const cur = v.footprint();
    if (!obb({ x: cur.x, z: cur.z, hw: cur.hw, hl: cur.hl, yaw: v.yaw }, c)) return true;
  }
  for (const o of VEH) {
    if (o === v) continue;
    const f = o.footprint();
    if (obb(me, { x: f.x, z: f.z, hw: f.hw, hl: f.hl, yaw: o.yaw })) {
      const cur = v.footprint();
      if (!obb({ x: cur.x, z: cur.z, hw: cur.hw, hl: cur.hl, yaw: v.yaw }, { x: f.x, z: f.z, hw: f.hw, hl: f.hl, yaw: o.yaw })) return true;
    }
  }
  for (const el of buildings.elements) {
    if (el.state === 'hooked') continue;
    if (obb(me, { x: el.pos.x, z: el.pos.z, hw: el.halfX, hl: el.halfZ, yaw: el.yaw }) && el.pos.y + el.size.y > v.pos.y + 0.4) return true;
  }
  return false;
};

// ---------------- Oberfläche ----------------
const $ = (id) => document.getElementById(id);
const ICONS = {
  Bagger: '<path d="M3 17h13a2 2 0 010 4H3a2 2 0 010-4z"/><path d="M5 16v-4h6v4zM7 11V8h3v3z"/><path d="M11 10l6-6 3 3-1.5 1.5-1.5-1.5-4 4z"/><path d="M19 8l2 3-2 2-1-1z"/>',
  Radlader: '<circle cx="6" cy="18" r="3"/><circle cx="16" cy="18" r="3"/><path d="M4 14V9h4V6h4v8z"/><path d="M12 12l5-1 1-2h4l-1 6h-4z"/>',
  Raupe: '<path d="M3 17h12a2 2 0 010 4H3a2 2 0 010-4z"/><path d="M4 16v-4h3V8h5v8z"/><path d="M15 12h2l3-1v8l-3-2h-2z"/>',
  Kipper: '<circle cx="6" cy="19" r="2.2"/><circle cx="17" cy="19" r="2.2"/><path d="M2 16V11h5v5z"/><path d="M8 16l2-9 12 2-3 7z"/>',
  Mischer: '<circle cx="6" cy="19" r="2.2"/><circle cx="17" cy="19" r="2.2"/><path d="M2 16V11h5v5z"/><ellipse cx="15" cy="11.5" rx="7" ry="4"/>',
  Kran: '<path d="M7 22V6h2v16z"/><path d="M3 5h20v2H3z"/><path d="M8 2l1 3H7z"/><path d="M19 7v6h-1V7z"/><path d="M17 13h3v2h-3z"/><path d="M4 7h3v3H4z"/>',
  Birne: '<path d="M3 18h10a2 2 0 010 4H3a2 2 0 010-4z"/><path d="M4 17v-4h6v4z"/><path d="M8 13L18 3l1 1L9 14z"/><path d="M18 4v8"/><circle cx="18" cy="14" r="2.5"/>',
};
const icon = (k) => `<svg viewBox="0 0 24 24" aria-hidden="true">${ICONS[k] || ''}</svg>`;

class UI {
  constructor() {
    this.bar = $('bar');
    this.hudEl = $('hud');
    this.toasts = $('toasts');
    this.actions = $('actions');
    this.taskEl = $('task');
    this.hudT = 0;
    this.lastCtl = '';
    this.shownHelp = new Set();
    CONTROLLABLE.forEach((v, i) => {
      const b = document.createElement('button');
      b.className = 'veh';
      b.innerHTML = `${icon(v.short)}<span>${v.short}</span>`;
      b.title = v.name;
      b.addEventListener('click', () => game.select(i));
      this.bar.appendChild(b);
    });
  }
  toast(msg, kind = 'info') {
    const t = document.createElement('div');
    t.className = 'toast ' + kind;
    t.textContent = msg;
    this.toasts.prepend(t);
    while (this.toasts.children.length > 3) this.toasts.lastChild.remove();
    setTimeout(() => t.classList.add('out'), 3800);
    setTimeout(() => t.remove(), 4400);
  }
  setActive(i) {
    [...this.bar.children].forEach((b, k) => b.classList.toggle('on', k === i));
    this.lastCtl = '';
  }
  updateControls(v) {
    const c = v.controls();
    const key = JSON.stringify(c) + settings.iso;
    if (key === this.lastCtl) return;
    this.lastCtl = key;
    for (const [side, id] of [['left', 'stickL'], ['right', 'stickR']]) {
      const el = $(id);
      for (const d of ['up', 'down', 'left', 'right']) el.querySelector('.lb.' + d).textContent = c[side][d] || '';
      el.classList.toggle('idle', !Object.values(c[side]).some((x) => x));
    }
    this.actions.innerHTML = '';
    for (const b of c.buttons) {
      const btn = document.createElement('button');
      btn.className = 'act';
      btn.innerHTML = `<small>${b.hint}</small>${b.label}`;
      btn.addEventListener('click', () => {
        audio.init();
        if (v.onButton && v.onButton(b.id)) this.lastCtl = '';
      });
      this.actions.appendChild(btn);
    }
  }
  update(dt, v) {
    this.updateControls(v);
    this.hudT -= dt;
    if (this.hudT > 0) return;
    this.hudT = 0.15;
    const rows = v.hud();
    const tilt = v.tilt !== undefined ? Math.round((v.tilt * 180) / Math.PI) : null;
    let html = rows.map(([a, b, c]) => `<div class="cell"><span class="k">${a}</span><b>${b}</b><span class="s">${c || ''}</span></div>`).join('');
    if (tilt !== null) html += `<div class="cell${tilt > 22 ? ' warn' : ''}"><span class="k">Neigung</span><b>${tilt}°</b><span class="s">${tilt > 22 ? 'Kippgefahr!' : 'stabil'}</span></div>`;
    this.hudEl.innerHTML = html;
    // Baustellenstatus
    const s = buildings.sites[0];
    if (s) {
      const st = s.status();
      this.taskEl.hidden = false;
      $('taskTitle').textContent = st.title;
      $('taskDetail').textContent = st.detail;
      $('taskBar').style.width = `${Math.round(clamp(st.prog, 0, 1) * 100)}%`;
    } else this.taskEl.hidden = true;
    const disp = terrain.disposed;
    $('stats').textContent = `Entsorgt: ${(disp[1] + disp[3]).toFixed(1)} m³ Erde · ${(disp[2] + disp[4]).toFixed(1)} m³ Schutt · Gebaut: ${buildings.stats.built} · Abgerissen: ${buildings.stats.demolished}`;
  }
}

// ---------------- Kamera ----------------
const camState = { mode: 'follow', yawOff: 0, pitch: 0.42, dist: 14, tgt: new THREE.Vector3(), yaw: 0, look: { yaw: 0, pitch: 0 }, init: false, freeTarget: null };
const DEF_DIST = { Bagger: 13, Radlader: 13, Raupe: 12, Kipper: 15, Mischer: 15, Kran: 34, Birne: 26 };

// ---------------- Spiel ----------------
const game = {
  active: 0,
  started: false,
  placing: null,
  t: 0,
  select(i) {
    audio.init();
    const prev = CONTROLLABLE[this.active];
    if (prev) prev.active = false;
    this.active = i;
    const v = CONTROLLABLE[i];
    v.active = true;
    camState.dist = DEF_DIST[v.short] || 14;
    camState.pitch = v.isCrane ? 0.55 : 0.42;
    camState.yawOff = 0;
    camState.look.yaw = camState.look.pitch = 0;
    camState.init = false;
    ui.setActive(i);
    if (!ui.shownHelp.has(v.short)) {
      ui.shownHelp.add(v.short);
      ui.toast(HELP[v.short], 'info');
    }
  },
};
const HELP = {
  Bagger: 'Bagger: Links = Stiel & Drehen, rechts = Ausleger & Löffel (wie im echten Bagger). Löffel aufrecht halten, sonst fällt Erde heraus.',
  Radlader: 'Radlader: Schaufel flach ansetzen, in den Haufen fahren, ankippen und anheben. Über der Mulde auskippen.',
  Raupe: 'Planierraupe: Schild leicht absenken und fahren – das Schild schält den Boden ab und schiebt ihn vor sich her.',
  Kipper: 'Kipper: Unter den Bagger fahren und beladen lassen. In der Deponie (Mulde hinten rechts) abkippen.',
  Mischer: 'Fahrmischer: Mit dem Heck an die Schalung fahren, Schurre ausrichten und „Beton ablassen“. Nachladen am Betonwerk.',
  Kran: 'Kran: Haken über ein Fertigteil (Lagerplatz) bringen, absenken, „Einhängen“. Vorsichtig fahren – die Last pendelt!',
  Birne: 'Seilbagger: Oberwagen drehen, um die Birne schwingen zu lassen. Mit Schwung gegen die Wand – Abstand halten!',
};

const ui = new UI();
world.ui = ui;
const input = new Input(canvas);

// Baustelle am Bauplatz vorbereiten (Startzustand)
function newSite(key, x, z, yaw) {
  const s = new ConstructionSite(buildings, key, x, z, yaw);
  buildings.sites.push(s);
  return s;
}

// ---------------- Platzieren (Bauen-Menü) ----------------
const raycaster = new THREE.Raycaster();
const ghostMat = new THREE.MeshBasicMaterial({ color: 0x45e070, transparent: true, opacity: 0.35, depthWrite: false });
const ghostBad = new THREE.MeshBasicMaterial({ color: 0xff4a3d, transparent: true, opacity: 0.35, depthWrite: false });
function startPlacing(kind) {
  closeMenu();
  let hw, hd, label;
  if (kind === 'old') { hw = 4.9; hd = 4.2; label = 'Altbau'; }
  else if (kind === 'oldgarage') { hw = 2.0; hd = 3.3; label = 'Alte Garage'; }
  else { hw = DESIGNS[kind].hw; hd = DESIGNS[kind].hd; label = DESIGNS[kind].name; }
  const ghost = new THREE.Mesh(new THREE.BoxGeometry(hw * 2, 3, hd * 2), ghostMat);
  scene.add(ghost);
  const t = camState.tgt;
  game.placing = { kind, hw, hd, yaw: 0, x: t.x, z: t.z, ghost, ok: false, label };
  $('placebar').hidden = false;
  $('placeTitle').textContent = `${label} platzieren`;
  updatePlacing();
}
function updatePlacing() {
  const P = game.placing;
  if (!P) return;
  const y = terrain.statsRect(P.x, P.z, P.hw, P.hd, P.yaw).mean;
  P.ghost.position.set(P.x, y + 1.5, P.z);
  P.ghost.rotation.y = P.yaw;
  let ok = terrain.inside(P.x, P.z, Math.max(P.hw, P.hd) + 2.5);
  let why = ok ? '' : 'zu nah am Rand';
  if (ok) {
    buildings.query(P.x - P.hw - 1, P.z - P.hd - 1, P.x + P.hw + 1, P.z + P.hd + 1, () => { ok = false; why = 'Fläche nicht frei'; return false; });
  }
  if (ok && P.kind !== 'old' && P.kind !== 'oldgarage') {
    const d = Math.hypot(P.x - crane.x, P.z - crane.z);
    if (d > 27) { ok = false; why = `außerhalb der Kranreichweite (${d.toFixed(0)} m > 27 m)`; }
  }
  if (ok) {
    for (const v of VEH) if (Math.hypot(v.pos.x - P.x, v.pos.z - P.z) < Math.max(P.hw, P.hd) + 1) { ok = false; why = `${v.short} steht im Weg`; }
    if (Math.hypot(crane.x - P.x, crane.z - P.z) < Math.max(P.hw, P.hd) + 3) { ok = false; why = 'Kranfundament im Weg'; }
  }
  P.ok = ok;
  P.ghost.material = ok ? ghostMat : ghostBad;
  $('placeInfo').textContent = ok ? 'Fläche antippen zum Verschieben' : `Nicht möglich: ${why}`;
  $('placeOk').disabled = !ok;
}
function confirmPlacing() {
  const P = game.placing;
  if (!P || !P.ok) return;
  if (P.kind === 'old' || P.kind === 'oldgarage') {
    const st = terrain.statsRect(P.x, P.z, P.hw + 0.4, P.hd + 0.4, P.yaw);
    terrain.levelRect(P.x, P.z, P.hw + 0.4, P.hd + 0.4, P.yaw, st.mean, MAT.GRAVEL);
    const bp = P.kind === 'old' ? oldHouseBlueprint(Math.floor(Math.random() * 100)) : oldGarageBlueprint(Math.floor(Math.random() * 100));
    const b = buildings.createFromBlueprint(bp, P.x, st.mean, P.z, P.yaw, P.label, 'old');
    b.baseY = st.mean;
    ui.toast(`${P.label} steht – bereit zum Abriss.`, 'ok');
  } else {
    if (buildings.sites.length) {
      ui.toast('Es gibt schon eine aktive Baustelle. Erst fertigstellen oder im Menü abbrechen.', 'warn');
      return cancelPlacing();
    }
    newSite(P.kind, P.x, P.z, P.yaw);
    ui.toast(`Baustelle „${DESIGNS[P.kind].name}“ eingerichtet. Erst planieren!`, 'ok');
  }
  cancelPlacing();
}
function cancelPlacing() {
  if (game.placing) scene.remove(game.placing.ghost);
  game.placing = null;
  $('placebar').hidden = true;
}
input.onTap = (x, y) => {
  if (!game.placing) return;
  const r = canvas.getBoundingClientRect();
  const ndc = new THREE.Vector2(((x - r.left) / r.width) * 2 - 1, -((y - r.top) / r.height) * 2 + 1);
  raycaster.setFromCamera(ndc, camera);
  const hit = raycaster.intersectObject(terrain.mesh, false)[0];
  if (hit) {
    game.placing.x = hit.point.x;
    game.placing.z = hit.point.z;
    updatePlacing();
  }
};
$('placeRot').addEventListener('click', () => { if (game.placing) { game.placing.yaw = (game.placing.yaw + Math.PI / 2) % (Math.PI * 2); updatePlacing(); } });
$('placeOk').addEventListener('click', confirmPlacing);
$('placeNo').addEventListener('click', cancelPlacing);

// ---------------- Menü ----------------
function openMenu() {
  $('menu').hidden = false;
  $('optIso').checked = settings.iso;
  $('optQuality').value = settings.quality;
  $('optSound').checked = settings.sound;
  $('siteCancel').hidden = !buildings.sites.length;
  $('saveInfo').textContent = store.get(SAVE_KEY) ? 'Ein Spielstand ist gespeichert.' : 'Noch kein Spielstand gespeichert.';
}
function closeMenu() {
  $('menu').hidden = true;
}
$('btnMenu').addEventListener('click', openMenu);
$('menuClose').addEventListener('click', closeMenu);
$('menu').addEventListener('click', (e) => { if (e.target.id === 'menu') closeMenu(); });
document.querySelectorAll('[data-build]').forEach((b) => b.addEventListener('click', () => startPlacing(b.dataset.build)));
$('siteCancel').addEventListener('click', () => {
  for (const s of [...buildings.sites]) s.cancel();
  ui.toast('Baustelle abgebrochen.', 'info');
  closeMenu();
});
$('optIso').addEventListener('change', (e) => { settings.iso = e.target.checked; saveSettings(); ui.lastCtl = ''; });
$('optSound').addEventListener('change', (e) => { settings.sound = e.target.checked; audio.setEnabled(settings.sound); saveSettings(); updateSoundBtn(); });
$('optQuality').addEventListener('change', (e) => {
  settings.quality = e.target.value;
  saveSettings();
  ui.toast('Grafik wird beim nächsten Laden der Seite vollständig übernommen.', 'info');
  applyQuality();
});
function applyQuality() {
  const pr = settings.quality === 'hoch' ? Math.min(window.devicePixelRatio, 2) : settings.quality === 'mittel' ? Math.min(window.devicePixelRatio, 1.4) : 1;
  renderer.setPixelRatio(pr);
  resize();
}
$('btnSave').addEventListener('click', () => {
  const ok = saveGame();
  ui.toast(ok ? 'Spielstand gespeichert.' : 'Speichern nicht möglich (Browser-Speicher gesperrt).', ok ? 'ok' : 'warn');
  closeMenu();
});
$('btnLoad').addEventListener('click', () => {
  const raw = store.get(SAVE_KEY);
  if (!raw) return ui.toast('Kein Spielstand vorhanden.', 'warn');
  try {
    loadGame(JSON.parse(raw));
    ui.toast('Spielstand geladen.', 'ok');
  } catch (e) {
    console.error(e);
    ui.toast('Spielstand beschädigt.', 'warn');
  }
  closeMenu();
});
let resetArm = 0;
$('btnReset').addEventListener('click', () => {
  if (performance.now() - resetArm > 3000) {
    resetArm = performance.now();
    $('btnReset').textContent = 'Wirklich? Nochmal tippen';
    return;
  }
  store.del(SAVE_KEY);
  location.reload();
});
$('btnHelp').addEventListener('click', () => { $('help').hidden = !$('help').hidden; });
$('btnCam').addEventListener('click', () => {
  camState.mode = camState.mode === 'follow' ? 'cab' : camState.mode === 'cab' ? 'free' : 'follow';
  camState.look.yaw = camState.look.pitch = 0;
  if (camState.mode === 'free') camState.yaw = camState.yawCur || 0;
  ui.toast(camState.mode === 'follow' ? 'Kamera: Verfolgen' : camState.mode === 'cab' ? 'Kamera: Fahrerkabine' : 'Kamera: Frei (Wischen zum Drehen)', 'info');
  $('btnCam').querySelector('span').textContent = camState.mode === 'follow' ? 'Verfolgen' : camState.mode === 'cab' ? 'Kabine' : 'Frei';
});
function updateSoundBtn() {
  $('btnSound').classList.toggle('off', !settings.sound);
  $('btnSound').querySelector('span').textContent = settings.sound ? 'Ton an' : 'Ton aus';
}
$('btnSound').addEventListener('click', () => {
  audio.init();
  settings.sound = !settings.sound;
  audio.setEnabled(settings.sound);
  saveSettings();
  updateSoundBtn();
});
updateSoundBtn();
input.onKey = (code) => {
  if (!game.started) return;
  const d = /^Digit([1-7])$/.exec(code);
  if (d) game.select(+d[1] - 1);
  if (code === 'Space' || code === 'KeyE') {
    const v = CONTROLLABLE[game.active];
    const c = v.controls();
    if (c.buttons[0] && v.onButton) { v.onButton(c.buttons[0].id); ui.lastCtl = ''; }
  }
  if (code === 'KeyC') $('btnCam').click();
  if (code === 'Escape') { cancelPlacing(); closeMenu(); }
};
document.addEventListener('gesturestart', (e) => e.preventDefault());

// ---------------- Speichern / Laden ----------------
function snapshot() {
  return {
    v: 1,
    terrain: terrain.serialize(),
    veh: VEH.map((v) => v.serialize()),
    crane: crane.serialize(),
    b: buildings.serialize(),
    active: game.active,
  };
}
function saveGame() {
  try {
    return store.set(SAVE_KEY, JSON.stringify(snapshot()));
  } catch (e) {
    return false;
  }
}
function loadGame(d) {
  if (!d || d.v !== 1) return;
  if (crane.load) crane.load = null;
  terrain.deserialize(d.terrain);
  VEH.forEach((v, i) => d.veh[i] && v.deserialize(d.veh[i]));
  crane.deserialize(d.crane);
  buildings.deserialize(d.b);
  world.soil.p.length = 0;
  if (game.started) game.select(d.active || 0);
}
window.claude?.hot?.snapshot?.(() => (game.started ? snapshot() : {}));

// ---------------- Größe ----------------
function resize() {
  const w = window.innerWidth, h = window.innerHeight;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  world.effects.setScale(h * renderer.getPixelRatio(), camera.fov);
  document.body.classList.toggle('portrait', h > w);
}
window.addEventListener('resize', resize);
applyQuality();

// ---------------- Kamera-Update ----------------
const _t = new THREE.Vector3(), _p = new THREE.Vector3(), _l = new THREE.Vector3();
function updateCamera(dt) {
  const v = CONTROLLABLE[game.active];
  const o = input.takeOrbit();
  if (window.__camOverride) {
    const c = window.__camOverride;
    camera.position.set(c.p[0], c.p[1], c.p[2]);
    camera.lookAt(c.l[0], c.l[1], c.l[2]);
    return;
  }
  if (!game.started) {
    // Vorspann: langsame Kamerafahrt über die Baustelle
    game.t += dt;
    const a = game.t * 0.05 + 0.6;
    camera.position.set(Math.sin(a) * 46, 22, Math.cos(a) * 46);
    camera.lookAt(2, 2, -2);
    return;
  }
  if (camState.mode === 'cab' && v.cabin) {
    camState.look.yaw -= o.dx * 0.004;
    camState.look.pitch = clamp(camState.look.pitch - o.dy * 0.004, -1.2, 1.0);
    v.cabin(_p, _l);
    camera.position.copy(_p);
    const yaw = Math.atan2(_l.x, _l.z) + camState.look.yaw;
    const pitch = Math.asin(clamp(_l.y, -1, 1)) + camState.look.pitch;
    _t.set(Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), Math.cos(yaw) * Math.cos(pitch)).add(_p);
    camera.lookAt(_t);
    camera.near = 0.05;
    camera.updateProjectionMatrix();
    return;
  }
  camera.near = 0.1;
  const P = game.placing;
  const tgt = P ? _t.set(P.x, terrain.height(P.x, P.z) + 1, P.z) : v.cameraTarget(_t);
  if (!camState.init) {
    camState.tgt.copy(tgt);
    camState.yawCur = v.cameraYaw() + Math.PI;
    camState.init = true;
  }
  camState.tgt.x = damp(camState.tgt.x, tgt.x, 6, dt);
  camState.tgt.y = damp(camState.tgt.y, tgt.y, 4, dt);
  camState.tgt.z = damp(camState.tgt.z, tgt.z, 6, dt);
  camState.dist = clamp(camState.dist * o.zoom, 4, v.isCrane ? 90 : 60);
  camState.pitch = clamp(camState.pitch + o.dy * 0.004, 0.02, 1.45);
  if (camState.mode === 'free' || P) {
    camState.yaw -= o.dx * 0.005;
    camState.yawCur = camState.yaw;
  } else {
    camState.yawOff -= o.dx * 0.005;
    const target = v.cameraYaw() + Math.PI + camState.yawOff;
    camState.yawCur = dampAngle(camState.yawCur, target, 2.5, dt);
  }
  const yaw = camState.yawCur;
  const d = P ? Math.max(camState.dist, 26) : camState.dist;
  const pitch = P ? Math.max(camState.pitch, 0.75) : camState.pitch;
  _p.set(Math.sin(yaw) * Math.cos(pitch) * d, Math.sin(pitch) * d, Math.cos(yaw) * Math.cos(pitch) * d).add(camState.tgt);
  const gy = terrain.inside(_p.x, _p.z) ? terrain.height(_p.x, _p.z) : 0;
  if (_p.y < gy + 0.8) _p.y = gy + 0.8;
  camera.position.copy(_p);
  camera.lookAt(camState.tgt);
}

// ---------------- Start ----------------
$('btnStart').addEventListener('click', () => {
  audio.init();
  $('splash').hidden = true;
  $('ui').classList.add('play');
  game.started = true;
  const el = document.documentElement;
  if (isTouch && el.requestFullscreen) el.requestFullscreen().catch(() => {});
  game.select(game.active);
});

// Gespeicherten Stand beim Start laden (falls vorhanden)
(() => {
  const hot = window.claude?.hot;
  const apply = (data) => {
    if (data && data.v === 1) {
      try { loadGame(data); return true; } catch (e) { console.error(e); }
    }
    return false;
  };
  const boot = (data) => {
    if (!apply(data)) {
      const raw = store.get(SAVE_KEY);
      let loaded = false;
      if (raw) { try { loaded = apply(JSON.parse(raw)); } catch (e) {} }
      if (!loaded) newSite('house', L.plot.x, L.plot.z, 0);
      if (loaded) $('splashSave').hidden = false;
    }
  };
  if (hot && hot.ready) hot.ready(boot);
  else boot(hot && hot.data);
})();

// Automatisch speichern
setInterval(() => { if (game.started) saveGame(); }, 45000);
window.addEventListener('pagehide', () => { if (game.started) saveGame(); });

// ---------------- Schleife ----------------
let last = performance.now();
let fpsT = 0, frames = 0, fpsLast = performance.now();
const perf = (window.__perf = { upd: 0, ren: 0 });
function frame(now) {
  requestAnimationFrame(frame);
  let dt = (now - last) / 1000;
  last = now;
  if (dt > 0.1) dt = 0.1;
  if (dt <= 0) return;
  const t0 = performance.now();
  const inp = input.update(dt);
  const active = game.started ? CONTROLLABLE[game.active] : null;
  // Bei niedriger Bildrate in Teilschritten simulieren (Echtzeit bleibt erhalten)
  const steps = Math.min(3, Math.ceil(dt / 0.034));
  const h = dt / steps;
  for (let s = 0; s < steps; s++) {
    for (const v of CONTROLLABLE) v.update(h, v === active && !game.placing ? inp : null);
    buildings.update(h);
    world.soil.update(h);
    terrain.relax(s === steps - 1 ? 2 : 1);
  }
  world.effects.update(dt);
  terrain.processZones(dt);
  terrain.flush();
  updateCamera(dt);
  // Schatten folgt der Kamera
  const st = camState.init ? camState.tgt : _t.set(0, 0, 0);
  sun.position.copy(st).addScaledVector(sunDir, 80);
  sun.target.position.copy(st);
  sky.position.copy(camera.position);
  if (active) {
    audio.update(dt, active.soundState());
    ui.update(dt, active);
  }
  const t1 = performance.now();
  renderer.render(scene, camera);
  const t2 = performance.now();
  perf.upd = perf.upd * 0.95 + (t1 - t0) * 0.05;
  perf.ren = perf.ren * 0.95 + (t2 - t1) * 0.05;
  frames++;
  fpsT += (now - fpsLast) / 1000;
  fpsLast = now;
  if (fpsT > 1) {
    window.__fps = frames / fpsT;
    frames = 0;
    fpsT = 0;
  }
}
requestAnimationFrame(frame);
window.__game = { world, game, VEH, crane, buildings, terrain, CONTROLLABLE, camState, renderer };
