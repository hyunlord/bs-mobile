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

// Lightning core pixels in the pillar band: near-white and flanked by violet bloom, so white damage numbers do not count.
function corePixels(directory, index, screenX, screenY) {
  const image = frame(directory, index); const y0 = image.height - screenY;
  let count = 0;
  for (let y = y0 - 110; y <= y0 - 20; y++) for (let x = screenX - 26; x <= screenX + 26; x++)
    if (luma(image.at(x, y)) >= 88 && (violet(image.at(x - 6, y)) || violet(image.at(x + 6, y)))) count++;
  return count;
}
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
// Widths per row over the pillar span: the bright core run and the violet bloom run around each row's brightest pixel.
// The median over rows whose peak is a lightning core (>= 90/100) is reported, so crossing enemies or numbers do not dominate.
function widths(image, y0, screenX, background) {
  const cores = [], glows = [];
  for (let y = y0 - 105; y <= y0 - 25; y += 4) {
    let peak = screenX, best = -1;
    for (let x = screenX - 26; x <= screenX + 26; x++) { const l = luma(image.at(x, y)); if (l > best) { best = l; peak = x; } }
    if (best < 90) continue;
    const cut = background + .6 * (best - background);
    let left = peak, right = peak;
    while (left > peak - 30 && luma(image.at(left - 1, y)) >= cut) left--;
    while (right < peak + 30 && luma(image.at(right + 1, y)) >= cut) right++;
    let gLeft = left, gRight = right;
    while (gLeft > peak - 45 && violet(image.at(gLeft - 1, y))) gLeft--;
    while (gRight < peak + 45 && violet(image.at(gRight + 1, y))) gRight++;
    cores.push(right - left + 1); glows.push(gRight - gLeft + 1);
  }
  const median = values => values.length ? values.sort((a, b) => a - b)[values.length >> 1] : 0;
  return { core: median(cores), glow: median(glows), rows: cores.length };
}

function checkLightning(directory, manifest) {
  const strikes = rows(path.join(directory, 'm1-lightning-strikes.csv'));
  const results = [];
  for (const s of strikes) {
    const x = Number(s.firstSkyScreenX), y = Number(s.firstSkyScreenY), at = Number(s.frameIndex);
    if (x < 0) continue;
    const before = Math.max(0, at - 8);
    const background = ground(directory, before, x, y);
    const strike = pillar(directory, at, x, y);
    const measured = widths(strike.image, strike.y0, x, background);
    const telegraph = Number(s.telegraphFrames);
    // Fade: first frame after the strike with at most 5% of the strike frame's lightning-core pixels left in the pillar band.
    const peak = corePixels(directory, at, x, y);
    let fadeFrame = null;
    for (let k = 1; k <= 14 && peak > 0; k++) { if (corePixels(directory, at + k, x, y) <= peak * .05) { fadeFrame = k; break; } }
    results.push({ frame: at, tick: Number(s.tick), storm: s.storm === 'True', links: Number(s.links), skyBolts: Number(s.skyBolts), core: +strike.core.toFixed(1), background: +background.toFixed(1),
      contrast: +(strike.core - background).toFixed(1), corePixelsReported: +Number(s.corePixels).toFixed(2), glowPixelsReported: +Number(s.glowPixels).toFixed(2),
      corePixelsMeasured: measured.core, glowPixelsMeasured: measured.glow, measuredRows: measured.rows, strikeCorePixelCount: peak, telegraphFrames: telegraph, fadeFrames: fadeFrame });
  }
  const p = manifest.presentation ?? {};
  const pass = {
    contrast: results.length > 0 && results.every(r => r.contrast >= thresholds.contrast),
    size: results.length > 0 && results.every(r => r.corePixelsReported >= thresholds.corePixels && r.glowPixelsReported >= thresholds.glowPixels)
      && results.filter(r => r.measuredRows >= 3).every(r => r.corePixelsMeasured >= thresholds.corePixels && r.glowPixelsMeasured >= thresholds.glowPixels),
    timing: results.length > 0 && results.every(r => r.telegraphFrames >= thresholds.telegraphFrames[0] && r.telegraphFrames <= thresholds.telegraphFrames[1]
      && r.fadeFrames !== null && r.fadeFrames >= 3 && r.fadeFrames <= thresholds.fadeFrames[1] + 1),
    reaction: p.lightningDeferredHits === p.lightningPresentedHits && (p.enemyDeathDropped ?? 0) === 0,
  };
  return { strikes: results.length, stormStrikes: results.filter(r => r.storm).length, pass, thresholds: { contrast: thresholds.contrast, corePixels: thresholds.corePixels, glowPixels: thresholds.glowPixels, telegraphFrames: thresholds.telegraphFrames, fadeFrames: thresholds.fadeFrames },
    note: 'Telegraph frames = presented frames between the chain-link delivery and the visible strike (renderer log). Fade = first frame after the strike with <=5% of its near-white, violet-flanked core pixels left in the pillar band (+1 frame tolerance for JPEG and bloom tails). Pixel widths are medians over rows whose peak is a lightning core; strikes with fewer than 3 such rows (pillar crossing other effects) are reported but not gated on measured width.',
    deferredHits: p.lightningDeferredHits, presentedHits: p.lightningPresentedHits, rows: results };
}

function checkMinions(directory, manifest) {
  const log = rows(path.join(directory, 'm1-minion-thrusts.csv'));
  const hits = log.filter(r => r.kind === 'hit'), thrusts = new Set(log.filter(r => r.kind === 'thrust' && Number(r.soldiers) > 0).map(r => `${r.frameIndex}:${r.group}`));
  const synced = hits.filter(h => thrusts.has(`${h.frameIndex}:${h.group}`)).length;
  const p = manifest.presentation ?? {};
  return { presentedGroupHits: hits.length, hitsWithThrustOnSameFrame: synced, stationaryRatio: p.minionStationaryRatio, bearerFrames: p.minionBearerFrames, chargeShoves: p.minionChargeShoves,
    pass: { attackOnHitFrame: hits.length > 0 && synced === hits.length, moving: hits.length > 0 && p.minionStationaryRatio < thresholds.stationaryRatio },
    note: 'Each Core group hit presented on a frame must start a soldier thrust on that same frame (renderer log). Stationary ratio excludes spawn, attack and despawn animation frames.' };
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
