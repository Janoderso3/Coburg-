// Eingabe: zwei virtuelle Joysticks (Touch), Kamera-Gesten, Tastatur
import { clamp } from './util.js';

class Stick {
  constructor(el) {
    this.el = el;
    this.base = el.querySelector('.base');
    this.knob = el.querySelector('.knob');
    this.x = 0;
    this.y = 0;
    this.id = null;
    const down = (e) => {
      if (this.id !== null) return;
      e.preventDefault();
      this.id = e.pointerId;
      try { el.setPointerCapture(e.pointerId); } catch (_) {}
      el.classList.add('on');
      this.move(e);
    };
    const move = (e) => {
      if (e.pointerId !== this.id) return;
      e.preventDefault();
      this.move(e);
    };
    const up = (e) => {
      if (e.pointerId !== this.id) return;
      this.id = null;
      this.x = this.y = 0;
      this.knob.style.transform = 'translate(-50%,-50%)';
      el.classList.remove('on');
    };
    el.addEventListener('pointerdown', down);
    el.addEventListener('pointermove', move);
    el.addEventListener('pointerup', up);
    el.addEventListener('pointercancel', up);
    el.addEventListener('lostpointercapture', up);
  }
  move(e) {
    const r = this.base.getBoundingClientRect();
    const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
    const R = r.width / 2;
    let dx = (e.clientX - cx) / R, dy = (e.clientY - cy) / R;
    const l = Math.hypot(dx, dy);
    if (l > 1) { dx /= l; dy /= l; }
    this.x = dx;
    this.y = -dy;
    this.knob.style.transform = `translate(calc(-50% + ${dx * R * 0.62}px), calc(-50% + ${dy * R * 0.62}px))`;
  }
}

// Proportionalventil-Kennlinie: feinfühlig um die Mitte
function curve(v) {
  const a = Math.abs(v);
  if (a < 0.07) return 0;
  const t = (a - 0.07) / 0.93;
  return Math.sign(v) * Math.pow(t, 1.5);
}

export class Input {
  constructor(canvas) {
    this.canvas = canvas;
    this.left = new Stick(document.getElementById('stickL'));
    this.right = new Stick(document.getElementById('stickR'));
    this.keys = new Set();
    this.state = { lx: 0, ly: 0, rx: 0, ry: 0 };
    this.orbit = { dx: 0, dy: 0, zoom: 1 };
    this.onTap = null;
    this.onKey = null;
    this.ptrs = new Map();
    this.kb = { lx: 0, ly: 0, rx: 0, ry: 0 };
    window.addEventListener('keydown', (e) => {
      if (e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT')) return;
      this.keys.add(e.code);
      if (this.onKey) this.onKey(e.code, e);
      if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Space'].includes(e.code)) e.preventDefault();
    });
    window.addEventListener('keyup', (e) => this.keys.delete(e.code));
    window.addEventListener('blur', () => this.keys.clear());
    canvas.addEventListener('pointerdown', (e) => {
      e.preventDefault();
      try { canvas.setPointerCapture(e.pointerId); } catch (_) {}
      this.ptrs.set(e.pointerId, { x: e.clientX, y: e.clientY, sx: e.clientX, sy: e.clientY, t: performance.now() });
    });
    canvas.addEventListener('pointermove', (e) => {
      const p = this.ptrs.get(e.pointerId);
      if (!p) return;
      e.preventDefault();
      if (this.ptrs.size === 1) {
        this.orbit.dx += e.clientX - p.x;
        this.orbit.dy += e.clientY - p.y;
      } else if (this.ptrs.size === 2) {
        const others = [...this.ptrs.entries()].filter(([id]) => id !== e.pointerId);
        const o = others[0][1];
        const d0 = Math.hypot(p.x - o.x, p.y - o.y);
        const d1 = Math.hypot(e.clientX - o.x, e.clientY - o.y);
        if (d0 > 10 && d1 > 10) this.orbit.zoom *= d0 / d1;
      }
      p.x = e.clientX;
      p.y = e.clientY;
    });
    const end = (e) => {
      const p = this.ptrs.get(e.pointerId);
      if (!p) return;
      this.ptrs.delete(e.pointerId);
      const moved = Math.hypot(e.clientX - p.sx, e.clientY - p.sy);
      if (moved < 10 && performance.now() - p.t < 400 && this.onTap) this.onTap(e.clientX, e.clientY);
    };
    canvas.addEventListener('pointerup', end);
    canvas.addEventListener('pointercancel', (e) => this.ptrs.delete(e.pointerId));
    canvas.addEventListener('wheel', (e) => {
      e.preventDefault();
      this.orbit.zoom *= Math.exp(e.deltaY * 0.0012);
    }, { passive: false });
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());
  }

  key(a, b) {
    return (this.keys.has(a) ? 1 : 0) - (this.keys.has(b) ? 1 : 0);
  }

  update(dt) {
    const K = this.kb;
    const ramp = (v, t) => (Math.abs(t - v) < dt * 3 ? t : v + Math.sign(t - v) * dt * 3);
    K.lx = ramp(K.lx, this.key('KeyD', 'KeyA'));
    K.ly = ramp(K.ly, this.key('KeyW', 'KeyS'));
    K.rx = ramp(K.rx, this.key('KeyL', 'KeyJ') || this.key('ArrowRight', 'ArrowLeft'));
    K.ry = ramp(K.ry, this.key('KeyI', 'KeyK') || this.key('ArrowUp', 'ArrowDown'));
    const s = this.state;
    s.lx = clamp(curve(this.left.x) + K.lx, -1, 1);
    s.ly = clamp(curve(this.left.y) + K.ly, -1, 1);
    s.rx = clamp(curve(this.right.x) + K.rx, -1, 1);
    s.ry = clamp(curve(this.right.y) + K.ry, -1, 1);
    return s;
  }

  takeOrbit() {
    const o = { dx: this.orbit.dx, dy: this.orbit.dy, zoom: this.orbit.zoom };
    this.orbit.dx = this.orbit.dy = 0;
    this.orbit.zoom = 1;
    return o;
  }
}
