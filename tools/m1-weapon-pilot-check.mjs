#!/usr/bin/env node
// Automated pre-screen for the weapon presentation pilot (storm_fork lightning, muster_horn soldiers) on offline review
// captures. It only filters out obviously unreadable output; acceptance is the user's video verdict.
// Usage: node tools/m1-weapon-pilot-check.mjs <capture directory> [more capture directories...]
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { parseCsv } from './csv.mjs';

const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const rows = file => fs.existsSync(file) ? parseCsv(fs.readFileSync(file, 'utf8')) : [];
const thresholds = { contrast: 40, corePixels: 4, glowPixels: 16, telegraphFrames: [2, 4], fadeFrames: [8, 10], stationaryRatio: .2 };

function frame(directory, index) {
  const file = path.join(directory, 'offline-frames', `frame-${String(index).padStart(6, '0')}.jpg`);
  const raw = execFileSync('ffmpeg', ['-nostdin', '-v', 'error', '-i', file, '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], { maxBuffer: 64 * 1024 * 1024 });
  const probe = execFileSync('ffprobe', ['-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=width,height', '-of', 'csv=p=0', file], { encoding: 'utf8' }).trim().split(',').map(Number);
  const [width, height] = probe;
  assert.equal(raw.length, width * height * 3);
  return { width, height, at(x, y) { x = Math.max(0, Math.min(width - 1, Math.round(x))); y = Math.max(0, Math.min(height - 1, Math.round(y))); const o = (y * width + x) * 3; return [raw[o], raw[o + 1], raw[o + 2]]; } };
}
const luma = ([r, g, b]) => (.2126 * r + .7152 * g + .0722 * b) / 2.55;   // 0..100
const violet = ([r, g, b]) => b - g > 18 && r - g > 4;

// Pillar region: from 20 to 110 px above the target in image rows (Unity screen y is bottom-up).
function pillar(directory, index, screenX, screenY) {
  const image = frame(directory, index); const y0 = image.height - screenY;
  let core = 0;
  for (let y = y0 - 110; y <= y0 - 20; y++) for (let x = screenX - 14; x <= screenX + 14; x++) core = Math.max(core, luma(image.at(x, y)));
  return { image, y0, core };
}
function ground(directory, index, screenX, screenY) {
  const image = frame(directory, index); const y0 = image.height - screenY;
  let sum = 0, count = 0;
  for (let y = y0 - 110; y <= y0 - 20; y += 2) for (let x = screenX - 30; x <= screenX + 30; x += 2) { sum += luma(image.at(x, y)); count++; }
  return sum / count;
}
// Widths along the row halfway up the pillar: bright core run and violet bloom run around the brightest column.
function widths(image, y0, screenX, core, background) {
  const y = y0 - 65; let peak = screenX, best = -1;
  for (let x = screenX - 20; x <= screenX + 20; x++) { const l = luma(image.at(x, y)); if (l > best) { best = l; peak = x; } }
  const cut = background + .6 * (best - background);
  let left = peak, right = peak;
  while (left > peak - 40 && luma(image.at(left - 1, y)) >= cut) left--;
  while (right < peak + 40 && luma(image.at(right + 1, y)) >= cut) right++;
  let gLeft = left, gRight = right;
  while (gLeft > peak - 60 && (violet(image.at(gLeft - 1, y)) || luma(image.at(gLeft - 1, y)) >= cut)) gLeft--;
  while (gRight < peak + 60 && (violet(image.at(gRight + 1, y)) || luma(image.at(gRight + 1, y)) >= cut)) gRight++;
  return { core: right - left + 1, glow: gRight - gLeft + 1 };
}

function checkLightning(directory, manifest) {
  const strikes = rows(path.join(directory, 'm1-lightning-strikes.csv'));
  const events = rows(path.join(directory, 'm1-combat-events.csv'));
  const linkFrame = new Map();
  for (const e of events) if (e.kind === 'chain-link' && !linkFrame.has(e.eventTick)) linkFrame.set(e.eventTick, Number(e.frameIndex));
  const results = [];
  for (const s of strikes) {
    const x = Number(s.firstSkyScreenX), y = Number(s.firstSkyScreenY), at = Number(s.frameIndex);
    if (x < 0) continue;
    const before = Math.max(0, at - 8);
    const background = ground(directory, before, x, y);
    const strike = pillar(directory, at, x, y);
    const measured = widths(strike.image, strike.y0, x, strike.core, background);
    const telegraph = linkFrame.has(s.tick) ? at - linkFrame.get(s.tick) : null;
    let fadeFrame = null;
    for (let k = 2; k <= 14; k++) { if (pillar(directory, at + k, x, y).core <= background + 12) { fadeFrame = k; break; } }
    results.push({ frame: at, tick: Number(s.tick), storm: s.storm === 'True', links: Number(s.links), skyBolts: Number(s.skyBolts), core: +strike.core.toFixed(1), background: +background.toFixed(1),
      contrast: +(strike.core - background).toFixed(1), corePixelsReported: +Number(s.corePixels).toFixed(2), glowPixelsReported: +Number(s.glowPixels).toFixed(2),
      corePixelsMeasured: measured.core, glowPixelsMeasured: measured.glow, telegraphFrames: telegraph, fadeFrames: fadeFrame });
  }
  const p = manifest.presentation ?? {};
  const pass = {
    contrast: results.length > 0 && results.every(r => r.contrast >= thresholds.contrast),
    size: results.length > 0 && results.every(r => r.corePixelsReported >= thresholds.corePixels && r.glowPixelsReported >= thresholds.glowPixels && r.corePixelsMeasured >= thresholds.corePixels && r.glowPixelsMeasured >= thresholds.glowPixels),
    timing: results.length > 0 && results.every(r => r.telegraphFrames !== null && r.telegraphFrames >= thresholds.telegraphFrames[0] && r.telegraphFrames <= thresholds.telegraphFrames[1] + 1
      && r.fadeFrames !== null && r.fadeFrames <= thresholds.fadeFrames[1] + 1),
    reaction: p.lightningDeferredHits === p.lightningPresentedHits && (p.enemyDeathDropped ?? 0) === 0,
  };
  return { strikes: results.length, stormStrikes: results.filter(r => r.storm).length, pass, thresholds: { contrast: thresholds.contrast, corePixels: thresholds.corePixels, glowPixels: thresholds.glowPixels, telegraphFrames: thresholds.telegraphFrames, fadeFrames: thresholds.fadeFrames },
    note: 'Telegraph frames are measured from the capture frame that recorded the chain-link event (+1 tolerance for event-record order); fade is the first frame after the strike whose pillar region returns within 12/100 of the pre-strike ground.',
    deferredHits: p.lightningDeferredHits, presentedHits: p.lightningPresentedHits, rows: results };
}

function checkMinions(directory, manifest) {
  const events = rows(path.join(directory, 'm1-combat-events.csv')).filter(e => (e.kind === 'group-hit' || e.kind === 'trained-group-hit') && Number(e.amount) > 0);
  const thrusts = rows(path.join(directory, 'm1-minion-thrusts.csv'));
  const thrustFrames = new Set(thrusts.filter(t => Number(t.soldiers) > 0).map(t => t.frameIndex));
  const hitFrames = [...new Set(events.map(e => e.frameIndex))];
  const synced = hitFrames.filter(f => thrustFrames.has(f)).length;
  const p = manifest.presentation ?? {};
  return { groupHitFrames: hitFrames.length, framesWithThrust: synced, stationaryRatio: p.minionStationaryRatio, bearerFrames: p.minionBearerFrames, chargeShoves: p.minionChargeShoves,
    pass: { attackOnHitFrame: hitFrames.length > 0 && synced === hitFrames.length, moving: hitFrames.length > 0 && p.minionStationaryRatio < thresholds.stationaryRatio },
    note: 'Each Core group-hit capture frame must carry a thrust start on the same frame. Stationary ratio excludes spawn, attack and despawn animation frames.' };
}

const directories = process.argv.slice(2);
assert(directories.length > 0, 'Usage: m1-weapon-pilot-check.mjs <capture directory>...');
const report = { contract: 'm1-weapon-pilot-check-v1', interpretation: 'Automated pre-screen only; the user video verdict decides acceptance. Occlusion is covered by the PlayMode bounding-box test; stage distinction is a blind human check on the three stage screenshots; frame cost uses the native combined measurement.', captures: [] };
for (const directory of directories) {
  const manifest = read(path.join(directory, 'm1-combat-review.json'));
  const id = manifest.definition.id, stage = manifest.definition.stage;
  const entry = { directory: path.basename(directory), id, stage };
  if (id === 'core:storm_fork') entry.lightning = checkLightning(directory, manifest);
  if (id === 'core:muster_horn') entry.minions = checkMinions(directory, manifest);
  report.captures.push(entry);
}
console.log(JSON.stringify(report, null, 2));
