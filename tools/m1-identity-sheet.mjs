#!/usr/bin/env node
// Identity sheet: can each weapon still be named (sword / arrow / spore cloud / shield) when one of its frames is shrunk to
// 64px? For every capture directory it picks a frame where the weapon's main shape is on screen (from the renderer logs),
// crops a square around it, and lays out: the crop at review size, the 64px version, and that 64px enlarged x4 (nearest).
// Usage: node tools/m1-identity-sheet.mjs <out.png> <capture directory>...
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { parseCsv } from './csv.mjs';

const rows = file => fs.existsSync(file) ? parseCsv(fs.readFileSync(file, 'utf8')) : [];
const FrameHeight = 1080;
// Review captures place the 500px-wide portrait game view in the middle of a 1920x1080 frame.
const StripLeft = 710, StripWidth = 500;

// Returns { frame, x, y, size } in image pixels (y down) for the weapon's main shape.
function pick(directory, id) {
  const late = r => Number(r.frameIndex) > 60;
  if (id === 'core:iron_blade') {
    const s = rows(path.join(directory, 'm1-blade-strikes.csv')).filter(late)[1];
    // Evolved: the planted giant sword (it lands 5 frames after the arc appears and stays half a second).
    if (s.evolved === 'True' && Number(s.swordTipScreenX) >= 0) {
      const length = Number(s.swordLengthPixels);
      return { frame: Number(s.frameIndex) + 12, x: Number(s.swordTipScreenX), y: FrameHeight - Number(s.swordTipScreenY) - length * .45, size: length * 1.1 };
    }
    const aim = Number(s.aim) * Math.PI / 180, r = Math.hypot(Number(s.radiusPixelsX), Number(s.radiusPixelsY));
    return { frame: Number(s.frameIndex) + 7, x: Number(s.originScreenX) + Math.cos(aim) * r * .6, y: FrameHeight - (Number(s.originScreenY) + Math.sin(aim) * r * .6), size: r * 1.4 };
  }
  if (id === 'core:canal_bow') {
    const all = rows(path.join(directory, 'm1-arrow-volleys.csv')).filter(late).filter(r => Number(r.firstImpactScreenX) >= 0);
    const v = all[1], due = Number(v.frameIndex) + Number(v.shownToDueFrames), hero = Number(v.heroHeightPixels ?? 115);
    if (v.evolved === 'True')   // the rain falling onto the first impact, 3 frames before it lands
      return { frame: due - 3, x: Number(v.firstImpactScreenX), y: FrameHeight - Number(v.firstImpactScreenY) - hero * .8, size: hero * 2.6 };
    // The arrow where the renderer put its point 5 frames before landing (falls back to the path midpoint); with the hand
    // recorded, also the launch frame (bowstring flash) around the hand.
    const mx = Number(v.midFlightScreenX ?? -1), hx = Number(v.handScreenX ?? -1);
    const mid = mx >= 0 ? { frame: due + Number(v.midFlightImpactOffset ?? 0) - 5, x: mx, y: FrameHeight - Number(v.midFlightScreenY), size: hero * 1.6, label: 'mid-flight' } : null;
    if (mid && hx >= 0) return [{ frame: due - 10 + 1, x: hx, y: FrameHeight - Number(v.handScreenY), size: hero * 1.6, label: 'launch' }, mid];
    if (mid) return mid;
    const lx = Number(v.launchScreenX), ly = Number(v.launchScreenY), ix = Number(v.firstImpactScreenX), iy = Number(v.firstImpactScreenY);
    return { frame: due - 4, x: (lx + ix) / 2, y: FrameHeight - (ly + iy) / 2, size: Math.max(hero * 1.6, Math.hypot(ix - lx, iy - ly) * 1.2) };
  }
  if (id === 'core:spore_fan' || id === 'core:ward_orbit') {
    const a = rows(path.join(directory, 'm1-orbit-activations.csv')).filter(late)[1];
    const hero = Number(a.heroHeightPixels);
    return { frame: Number(a.frameIndex), x: Number(a.firstOrbiterScreenX), y: FrameHeight - Number(a.firstOrbiterScreenY) - (id === 'core:spore_fan' ? hero * .5 : 0), size: Math.max(hero * 1.6, Number(a.orbiterSizePixels) * 1.6) };
  }
  if (id === 'core:storm_fork') {
    // A strike with a sky bolt: from the bolt's top down to its target, on the frame the strike becomes visible.
    const s = rows(path.join(directory, 'm1-lightning-strikes.csv')).filter(late).filter(r => Number(r.firstSkyScreenX) >= 0)[1];
    const tx = Number(s.firstSkyScreenX), ty = FrameHeight - Number(s.firstSkyScreenY), topx = Number(s.firstSkyTopScreenX), topy = FrameHeight - Number(s.firstSkyTopScreenY);
    return { frame: Number(s.frameIndex) + 1, x: (tx + topx) / 2, y: (ty + topy) / 2, size: Math.max(230, Math.hypot(tx - topx, ty - topy) * 1.15) };
  }
  throw new Error('No identity framing for ' + id);
}

const [out, ...directories] = process.argv.slice(2);
assert(out && directories.length > 0, 'Usage: m1-identity-sheet.mjs <out.png> <capture directory>...');
const temp = fs.mkdtempSync(path.join(path.dirname(path.resolve(out)), '.identity-'));
const tiles = [], labels = [];
try {
  for (const directory of directories) {
    const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'm1-combat-review.json'), 'utf8'));
    const id = manifest.definition.id, stage = manifest.definition.stage;
    for (const p of [pick(directory, id)].flat()) {
    const size = Math.round(Math.min(StripWidth, Math.max(96, p.size)));
    const x = Math.round(Math.max(StripLeft, Math.min(StripLeft + StripWidth - size, p.x - size / 2))), y = Math.round(Math.max(0, Math.min(FrameHeight - size, p.y - size / 2)));
    const source = path.join(directory, 'offline-frames', `frame-${String(p.frame).padStart(6, '0')}.jpg`);
    const tile = path.join(temp, `${tiles.length}.png`);
    const label = `${id.replace('core:', '')} ${stage}${p.label ? ' ' + p.label : ''} f${p.frame}`;
    // review-size crop (256) | 64px | 64px x4 nearest; captions go to <out>.labels.txt (this ffmpeg has no drawtext).
    execFileSync('ffmpeg', ['-nostdin', '-v', 'error', '-y', '-i', source, '-filter_complex',
      `[0]crop=${size}:${size}:${x}:${y},split=2[a][b];[a]scale=256:256[big];[b]scale=64:64:flags=area,split=2[s][t];` +
      `[s]pad=256:256:96:96:color=0x141416[small];[t]scale=256:256:flags=neighbor[zoom];` +
      `[big][small][zoom]hstack=3,pad=iw:ih+8:0:4:color=0x141416`,
      '-frames:v', '1', tile]);
    tiles.push(tile); labels.push(`${tiles.length}. ${label} — crop ${size}px at (${x},${y}) | 64px | 64px x4`);
    }
  }
  if (tiles.length === 1) fs.copyFileSync(tiles[0], out);
  else execFileSync('ffmpeg', ['-nostdin', '-v', 'error', '-y', ...tiles.flatMap(t => ['-i', t]), '-filter_complex', `${tiles.map((_, i) => `[${i}]`).join('')}vstack=${tiles.length}`, out]);
  fs.writeFileSync(out.replace(/\.png$/, '') + '.labels.txt', labels.join('\n') + '\n');
  console.log(out);
} finally {
  fs.rmSync(temp, { recursive: true, force: true });
}
