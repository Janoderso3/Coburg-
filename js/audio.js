// Synthetischer Ton: Dieselmotor, Hydraulik, Rückfahrwarner, Kettenrasseln, Einschläge
export class AudioSys {
  constructor() {
    this.ctx = null;
    this.enabled = true;
    this.lastShot = {};
  }

  init() {
    if (this.ctx) {
      if (this.ctx.state === 'suspended') this.ctx.resume();
      return;
    }
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    try {
      this.ctx = new AC();
    } catch (e) {
      return;
    }
    const ctx = this.ctx;
    this.master = ctx.createGain();
    this.master.gain.value = this.enabled ? 0.8 : 0;
    const comp = ctx.createDynamicsCompressor();
    comp.threshold.value = -14;
    comp.ratio.value = 4;
    this.master.connect(comp).connect(ctx.destination);

    // Rauschpuffer
    const len = ctx.sampleRate * 2;
    this.noise = ctx.createBuffer(1, len, ctx.sampleRate);
    const d = this.noise.getChannelData(0);
    let b = 0;
    for (let i = 0; i < len; i++) {
      const w = Math.random() * 2 - 1;
      b = 0.97 * b + 0.03 * w; // etwas brauner Anteil
      d[i] = w * 0.6 + b * 3;
    }

    // Motor
    const e = (this.eng = {});
    e.out = ctx.createGain();
    e.out.gain.value = 0;
    e.lp = ctx.createBiquadFilter();
    e.lp.type = 'lowpass';
    e.lp.frequency.value = 400;
    e.lp.Q.value = 0.7;
    e.o1 = ctx.createOscillator();
    e.o1.type = 'sawtooth';
    e.o2 = ctx.createOscillator();
    e.o2.type = 'sawtooth';
    e.o3 = ctx.createOscillator();
    e.o3.type = 'sine';
    const g1 = ctx.createGain(); g1.gain.value = 0.35;
    const g2 = ctx.createGain(); g2.gain.value = 0.18;
    const g3 = ctx.createGain(); g3.gain.value = 0.5;
    e.o1.connect(g1).connect(e.lp);
    e.o2.connect(g2).connect(e.lp);
    e.o3.connect(g3).connect(e.out);
    // Verbrennungsrauschen
    e.ns = ctx.createBufferSource();
    e.ns.buffer = this.noise;
    e.ns.loop = true;
    e.bp = ctx.createBiquadFilter();
    e.bp.type = 'bandpass';
    e.bp.frequency.value = 600;
    e.bp.Q.value = 0.9;
    e.ng = ctx.createGain();
    e.ng.gain.value = 0.05;
    e.ns.connect(e.bp).connect(e.ng).connect(e.lp);
    // Rauigkeit über Amplitudenmodulation
    e.am = ctx.createGain();
    e.am.gain.value = 1;
    e.lfo = ctx.createOscillator();
    e.lfo.type = 'square';
    e.lfoG = ctx.createGain();
    e.lfoG.gain.value = 0.25;
    e.lfo.connect(e.lfoG).connect(e.am.gain);
    e.lp.connect(e.am).connect(e.out);
    // Turbopfeifen
    e.tb = ctx.createOscillator();
    e.tb.type = 'sine';
    e.tbG = ctx.createGain();
    e.tbG.gain.value = 0;
    e.tb.connect(e.tbG).connect(e.out);
    e.out.connect(this.master);
    for (const o of [e.o1, e.o2, e.o3, e.lfo, e.tb]) o.start();
    e.ns.start();

    // Hydraulik-Singen
    const h = (this.hyd = {});
    h.o = ctx.createOscillator();
    h.o.type = 'triangle';
    h.o.frequency.value = 780;
    h.f = ctx.createBiquadFilter();
    h.f.type = 'bandpass';
    h.f.frequency.value = 900;
    h.f.Q.value = 3;
    h.g = ctx.createGain();
    h.g.gain.value = 0;
    h.ns = ctx.createBufferSource();
    h.ns.buffer = this.noise;
    h.ns.loop = true;
    h.nf = ctx.createBiquadFilter();
    h.nf.type = 'highpass';
    h.nf.frequency.value = 2500;
    h.ng = ctx.createGain();
    h.ng.gain.value = 0;
    h.o.connect(h.f).connect(h.g).connect(this.master);
    h.ns.connect(h.nf).connect(h.ng).connect(this.master);
    h.o.start();
    h.ns.start();

    // Rückfahrwarner
    const bp = (this.beep = {});
    bp.o = ctx.createOscillator();
    bp.o.type = 'square';
    bp.o.frequency.value = 1180;
    bp.f = ctx.createBiquadFilter();
    bp.f.type = 'bandpass';
    bp.f.frequency.value = 1180;
    bp.f.Q.value = 4;
    bp.g = ctx.createGain();
    bp.g.gain.value = 0;
    bp.o.connect(bp.f).connect(bp.g).connect(this.master);
    bp.o.start();
    bp.t = 0;

    // Kettenrasseln
    const tr = (this.track = {});
    tr.ns = ctx.createBufferSource();
    tr.ns.buffer = this.noise;
    tr.ns.loop = true;
    tr.f = ctx.createBiquadFilter();
    tr.f.type = 'bandpass';
    tr.f.frequency.value = 1800;
    tr.f.Q.value = 1.5;
    tr.g = ctx.createGain();
    tr.g.gain.value = 0;
    tr.mod = ctx.createOscillator();
    tr.mod.type = 'square';
    tr.mod.frequency.value = 5;
    tr.modG = ctx.createGain();
    tr.modG.gain.value = 0;
    tr.mod.connect(tr.modG).connect(tr.g.gain);
    tr.ns.connect(tr.f).connect(tr.g).connect(this.master);
    tr.ns.start();
    tr.mod.start();

    // Umgebung: leiser Wind
    const w = ctx.createBufferSource();
    w.buffer = this.noise;
    w.loop = true;
    const wf = ctx.createBiquadFilter();
    wf.type = 'lowpass';
    wf.frequency.value = 350;
    const wg = ctx.createGain();
    wg.gain.value = 0.035;
    w.connect(wf).connect(wg).connect(this.master);
    w.start();
  }

  setEnabled(on) {
    this.enabled = on;
    if (this.master) this.master.gain.setTargetAtTime(on ? 0.8 : 0, this.ctx.currentTime, 0.05);
  }

  // rpm: 0..1 (Leerlauf..Nenndrehzahl), load: 0..1, kind: Motortyp
  update(dt, s) {
    if (!this.ctx || this.ctx.state !== 'running') return;
    const t = this.ctx.currentTime;
    const e = this.eng;
    const idle = s.idle || 750, max = s.max || 2100;
    const rpm = idle + (max - idle) * s.rpm;
    const f = (rpm / 60) * (s.cyl || 6) / 2; // Zündfrequenz
    e.o1.frequency.setTargetAtTime(f, t, 0.06);
    e.o2.frequency.setTargetAtTime(f * 1.503, t, 0.06);
    e.o3.frequency.setTargetAtTime(f * 0.5, t, 0.06);
    e.lfo.frequency.setTargetAtTime(f * 0.5, t, 0.06);
    e.lp.frequency.setTargetAtTime(220 + 700 * s.rpm + 900 * s.load, t, 0.08);
    e.bp.frequency.setTargetAtTime(500 + 900 * s.rpm, t, 0.1);
    e.ng.gain.setTargetAtTime(0.04 + 0.12 * s.load, t, 0.1);
    e.tb.frequency.setTargetAtTime(1800 + 3500 * s.rpm, t, 0.3);
    e.tbG.gain.setTargetAtTime(0.004 + 0.012 * s.load * s.rpm, t, 0.3);
    e.out.gain.setTargetAtTime(s.on ? (0.16 + 0.16 * s.rpm + 0.1 * s.load) * (s.vol || 1) : 0, t, 0.12);

    const h = this.hyd;
    h.o.frequency.setTargetAtTime(600 + 500 * s.hyd + 200 * s.rpm, t, 0.1);
    h.g.gain.setTargetAtTime(0.012 * s.hyd, t, 0.08);
    h.ng.gain.setTargetAtTime(0.02 * s.hyd * s.hyd, t, 0.08);

    // Rückfahrwarner: 0,5 s an / 0,5 s aus
    const bp = this.beep;
    if (s.reverse) {
      bp.t += dt;
      const on = bp.t % 1 < 0.5;
      bp.g.gain.setTargetAtTime(on ? 0.05 : 0, t, 0.01);
    } else {
      bp.t = 0;
      bp.g.gain.setTargetAtTime(0, t, 0.02);
    }

    const tr = this.track;
    const sp = Math.abs(s.track || 0);
    tr.mod.frequency.setTargetAtTime(2 + sp * 6, t, 0.1);
    const tg = Math.min(0.06, sp * 0.05);
    tr.g.gain.setTargetAtTime(tg, t, 0.1);
    tr.modG.gain.setTargetAtTime(tg, t, 0.1);
  }

  // Einmalige Geräusche: 'crash', 'thud', 'soil', 'glass', 'clank'
  shot(kind, strength = 1, minGap = 0.06) {
    if (!this.ctx || this.ctx.state !== 'running' || !this.enabled) return;
    const now = this.ctx.currentTime;
    if (this.lastShot[kind] && now - this.lastShot[kind] < minGap) return;
    this.lastShot[kind] = now;
    const ctx = this.ctx;
    const src = ctx.createBufferSource();
    src.buffer = this.noise;
    const f = ctx.createBiquadFilter();
    const g = ctx.createGain();
    let dur = 0.5, vol = 0.3 * strength, freq = 300, type = 'lowpass';
    if (kind === 'crash') { dur = 0.9; freq = 900; vol = 0.5 * strength; }
    if (kind === 'thud') { dur = 0.35; freq = 160; vol = 0.6 * strength; }
    if (kind === 'soil') { dur = 0.6; freq = 500; vol = 0.12 * strength; type = 'bandpass'; }
    if (kind === 'glass') { dur = 0.4; freq = 4200; vol = 0.25 * strength; type = 'highpass'; }
    if (kind === 'clank') { dur = 0.15; freq = 2400; vol = 0.2 * strength; type = 'bandpass'; }
    f.type = type;
    f.frequency.value = freq;
    f.Q.value = type === 'bandpass' ? 1.2 : 0.7;
    g.gain.setValueAtTime(Math.min(1, vol), now);
    g.gain.exponentialRampToValueAtTime(0.001, now + dur);
    src.connect(f).connect(g).connect(this.master);
    src.start(now, Math.random() * 1.5, dur + 0.05);
    if (kind === 'crash' || kind === 'thud') {
      // tiefer Schlag
      const o = ctx.createOscillator();
      o.type = 'sine';
      o.frequency.setValueAtTime(kind === 'thud' ? 70 : 55, now);
      o.frequency.exponentialRampToValueAtTime(30, now + 0.3);
      const og = ctx.createGain();
      og.gain.setValueAtTime(Math.min(0.8, 0.4 * strength), now);
      og.gain.exponentialRampToValueAtTime(0.001, now + 0.35);
      o.connect(og).connect(this.master);
      o.start(now);
      o.stop(now + 0.4);
    }
  }
}
