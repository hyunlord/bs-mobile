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

const white = pixel => luma(pixel) >= 88;
const Window = 120;  // the zigzag wanders sideways and a pillar slanted away from the hero leans up to 45 degrees
// A lightning core pixel is near-white with violet bloom within 8 px on either side, so white numbers and flashes do not count.
function flanked(image, x, y) {
  for (let d = 1; d <= 8; d++) if (violet(image.at(x - d, y)) || violet(image.at(x + d, y))) return true;
  return false;
}
// The pillar runs from its logged top to the target (Unity screen y is bottom-up). Rows from 25% to 85% of its length are
// searched around the straight line between the two, wide enough for the zigzag (.14 of the length at most). The lower part
// overlaps the struck target and the hero (often mid white hit-flash), so it is left out.
function line(height, s) {
  const x = Number(s.firstSkyScreenX), y0 = height - Number(s.firstSkyScreenY);
  const hasTop = s.firstSkyTopScreenX !== undefined && Number(s.firstSkyTopScreenX) >= 0;
  const topX = hasTop ? Number(s.firstSkyTopScreenX) : x, topY = hasTop ? height - Number(s.firstSkyTopScreenY) : y0 - 300;
  const span = Math.max(1, y0 - topY);
  return { y0, from: Math.round(y0 - .85 * span), to: Math.round(y0 - .25 * span), center: y => x + (topX - x) * (y0 - y) / span, half: hasTop ? .16 * span + 12 : Window };
}
function corePixels(directory, index, s) {
  const image = frame(directory, index); const { from, to, center, half } = line(image.height, s);
  let count = 0;
  for (let y = from; y <= to; y++) { const c = center(y); for (let x = Math.round(c - half); x <= c + half; x++)
    if (white(image.at(x, y)) && flanked(image, x, y)) count++; }
  return count;
}
function pillar(directory, index, s) {
  const image = frame(directory, index); const geometry = line(image.height, s);
  let core = 0;
  for (let y = geometry.from; y <= geometry.to; y++) { const c = geometry.center(y);
    for (let x = Math.round(c - geometry.half); x <= c + geometry.half; x++) if (flanked(image, x, y)) core = Math.max(core, luma(image.at(x, y))); }
  return { image, geometry, core };
}
function ground(directory, index, s) {
  const image = frame(directory, index); const { from, to, center } = line(image.height, s);
  let sum = 0, count = 0;
  for (let y = from; y <= to; y += 2) { const c = center(y); for (let x = Math.round(c - 30); x <= c + 30; x += 2) { sum += luma(image.at(x, y)); count++; } }
  return sum / count;
}
// Bolt widths traced along the pillar: every horizontal run of violet-flanked near-white pixels is a core crossing. A slanted
// band crosses a row in h = w / cos and a column in v = w / sin, so the perpendicular width is h*v / sqrt(h^2 + v^2). The
// violet bloom run around the core gives the glow width with the same slant correction. Branches are thinner by design, so
// each row keeps its widest crossing (the main bolt) and the median over rows is reported.
function widths(image, { from, to, center, half }) {
  const cores = [], glows = [];
  for (let y = from; y <= to; y += 2) {
    let rowCore = 0, rowGlow = 0;
    const c = center(y);
    for (let x = Math.round(c - half); x <= c + half; x++) {
      if (!white(image.at(x, y))) continue;
      let right = x; while (right < c + half + 20 && white(image.at(right + 1, y))) right++;
      const left = x; x = right;
      const h = right - left + 1;
      if (h > 40 || !(flanked(image, left, y) && flanked(image, right, y))) continue;
      const cx = (left + right) >> 1;
      let up = y, down = y;
      while (y - up < 60 && white(image.at(cx, up - 1))) up--;
      while (down - y < 60 && white(image.at(cx, down + 1))) down++;
      const v = down - up + 1;
      const slant = v >= 60 ? 1 : v / Math.hypot(h, v);
      let gLeft = left, gRight = right;
      const bloom = px => violet(px) || white(px);
      while (left - gLeft < 40 && (bloom(image.at(gLeft - 1, y)) || bloom(image.at(gLeft - 2, y)))) gLeft--;
      while (gRight - right < 40 && (bloom(image.at(gRight + 1, y)) || bloom(image.at(gRight + 2, y)))) gRight++;
      if (h * slant > rowCore) { rowCore = h * slant; rowGlow = (gRight - gLeft + 1) * slant; }
    }
    if (rowCore > 0) { cores.push(rowCore); glows.push(rowGlow); }
  }
  const median = values => values.length ? +values.sort((a, b) => a - b)[values.length >> 1].toFixed(1) : 0;
  return { core: median(cores), glow: median(glows), rows: cores.length };
}

function checkLightning(directory, manifest) {
  const strikes = rows(path.join(directory, 'm1-lightning-strikes.csv'));
  const results = [];
  for (const s of strikes) {
    const x = Number(s.firstSkyScreenX), y = Number(s.firstSkyScreenY), at = Number(s.frameIndex);
    // Frame 0 rows were logged before recording started (warm-up), so there is no strike frame to look at.
    if (x < 0 || at <= 0) continue;
    const before = Math.max(0, at - 8);
    const background = ground(directory, before, s);
    const strike = pillar(directory, at, s);
    const measured = widths(strike.image, strike.geometry);
    const telegraph = Number(s.telegraphFrames);
    // Fade: first frame after the strike with at most 5% of the strike frame's lightning-core pixels left in the pillar band.
    const peak = corePixels(directory, at, s);
    let fadeFrame = null;
    for (let k = 1; k <= 14 && peak > 0; k++) { if (corePixels(directory, at + k, s) <= peak * .05) { fadeFrame = k; break; } }
    results.push({ frame: at, tick: Number(s.tick), storm: s.storm === 'True', links: Number(s.links), skyBolts: Number(s.skyBolts), screenFlash: Number(s.screenFlash ?? NaN), coveredSkyBolts: Number(s.coveredSkyBolts ?? 0), core: +strike.core.toFixed(1), background: +background.toFixed(1),
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
    // Whole-screen flash only on storm strikes and at most .065 alpha; ordinary strikes flash around their targets only.
    screenFlash: results.length > 0 && results.every(r => r.storm ? r.screenFlash > 0 && r.screenFlash <= .065 + 1e-6 : r.screenFlash === 0),
  };
  return { strikes: results.length, stormStrikes: results.filter(r => r.storm).length, skyBolts: results.reduce((n, r) => n + r.skyBolts, 0), coveredSkyBolts: results.reduce((n, r) => n + r.coveredSkyBolts, 0), pass, thresholds: { contrast: thresholds.contrast, corePixels: thresholds.corePixels, glowPixels: thresholds.glowPixels, telegraphFrames: thresholds.telegraphFrames, fadeFrames: thresholds.fadeFrames },
    note: 'Telegraph frames = presented frames between the chain-link delivery and the visible strike (renderer log). Fade = first frame after the strike with <=5% of its near-white, violet-flanked core pixels left along the pillar (+1 frame tolerance for JPEG and bloom tails). Pixel widths are medians over rows of the widest violet-flanked core crossing along the pillar between 25% and 85% of its length (the main bolt; branches are thinner by design), slant-corrected to the perpendicular width; strikes with fewer than 3 such rows are reported but not gated on measured width.',
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

// Iron blade: wind-up frames before the crescent, the crescent's brightness along the blade-tip arc against the ground
// before the swing, and that it is gone after its fade. Arc samples run every 5 degrees just inside the drawn radius.
function arcSamples(row, image) {
  const ox = Number(row.originScreenX), oy = Number(row.originScreenY);
  const r = Math.hypot(Number(row.radiusPixelsX), Number(row.radiusPixelsY)) * .93;
  const aim = Number(row.aim), half = Number(row.halfAngle);
  const points = [];
  for (let a = aim - half; a <= aim + half + .01; a += 5) {
    const x = ox + Math.cos(a * Math.PI / 180) * r, y = image.height - (oy + Math.sin(a * Math.PI / 180) * r);
    if (x >= 3 && y >= 3 && x < image.width - 3 && y < image.height - 3) points.push([x, y]);
  }
  return points;
}
function windowMax(image, x, y, size = 4) {
  let best = 0;
  for (let dy = -size; dy <= size; dy++) for (let dx = -size; dx <= size; dx++) best = Math.max(best, luma(image.at(x + dx, y + dy)));
  return best;
}
const median = values => values.length ? values.slice().sort((a, b) => a - b)[values.length >> 1] : 0;
function checkBlade(directory, manifest) {
  const strikes = rows(path.join(directory, 'm1-blade-strikes.csv'));
  const results = [];
  for (const s of strikes) {
    const at = Number(s.frameIndex);
    if (at <= 8) continue;
    const before = frame(directory, at - 8), swept = frame(directory, at + 9), gone = frame(directory, at + 19);
    const points = arcSamples(s, swept).filter(([x, y]) => luma(before.at(x, y)) > 5);
    if (points.length < 3) { results.push({ frame: at, skipped: 'arc off screen' }); continue; }
    const ground = points.reduce((n, [x, y]) => n + luma(before.at(x, y)), 0) / points.length;
    const peak = median(points.map(([x, y]) => windowMax(swept, x, y)));
    const after = median(points.map(([x, y]) => windowMax(gone, x, y)));
    const groundMax = median(points.map(([x, y]) => windowMax(before, x, y)));
    results.push({ frame: at, tick: Number(s.tick), evolved: s.evolved === 'True', windupFrames: Number(s.windupFrames), samples: points.length,
      ground: +ground.toFixed(1), arcPeak: +peak.toFixed(1), contrast: +(peak - ground).toFixed(1), afterFade: +after.toFixed(1), groundPeak: +groundMax.toFixed(1) });
  }
  const p = manifest.presentation ?? {}, stage = manifest.definition.stage;
  const measured = results.filter(r => !r.skipped);
  const pass = {
    windup: results.length > 0 && results.every(r => r.skipped || (r.windupFrames >= 3 && r.windupFrames <= 5)),
    contrast: measured.length > 0 && measured.every(r => r.contrast >= thresholds.contrast),
    fade: measured.length > 0 && measured.every(r => r.afterFade <= r.groundPeak + 12),
    reaction: p.bladeDeferredHits === p.bladePresentedHits && (p.giantBladeKillsOutside ?? 0) === 0 && (p.enemyDeathDropped ?? 0) === 0,
    stage: stage === 'evolved' ? (p.bladeEvolvedStrikes ?? 0) > 0 : (p.bladeEvolvedStrikes ?? 0) === 0,
  };
  return { strikes: results.length, pass, deferredHits: p.bladeDeferredHits, presentedHits: p.bladePresentedHits, evolvedStrikes: p.bladeEvolvedStrikes,
    note: 'Wind-up = presented frames from the Core attack to the visible crescent (4, ±1 for delivery rounding). Contrast = median over blade-tip arc samples of the brightest pixel within 4px at 9 frames after the crescent appears, minus the mean ground luma at the same samples 8 frames before. Fade = the arc is back to the ground level 19 frames after appearing (sweep 9 + hold 0 + fade 7, +3). Stage = giant sword strikes only in the evolved stage.',
    rows: results };
}

// Canal bow: arrows land on the Core impact tick; the arrow is visible in flight (or the warning circle before the rain);
// the landing splash stands out from the ground.
function checkArrows(directory, manifest) {
  const volleys = rows(path.join(directory, 'm1-arrow-volleys.csv'));
  const results = [];
  for (const v of volleys) {
    const at = Number(v.frameIndex), x = Number(v.firstImpactScreenX), y = Number(v.firstImpactScreenY);
    if (at <= 8 || x < 0) continue;
    const due = at + Number(v.shownToDueFrames), evolved = v.evolved === 'True';
    const before = frame(directory, Math.max(0, at - 30)), landing = frame(directory, due + 1);
    const iy = landing.height - y, radius = Math.max(8, Number(v.firstImpactRadiusPixels));
    let ground = 0, n = 0, peak = 0;
    for (let dy = -radius; dy <= radius; dy += 3) for (let dx = -radius; dx <= radius; dx += 3) {
      if (dx * dx + dy * dy > radius * radius) continue;
      ground += luma(before.at(x + dx, iy + dy)); n++; peak = Math.max(peak, luma(landing.at(x + dx, iy + dy)));
    }
    ground /= Math.max(1, n);
    const entry = { frame: at, tick: Number(v.tick), evolved, impacts: Number(v.impacts), onScreen: Number(v.onScreenImpacts), shownToDue: Number(v.shownToDueFrames),
      landingContrast: +(peak - ground).toFixed(1) };
    if (evolved) {
      // Warning circle: share of points on the Core radius with a water-blue pixel within 3px, 4 frames before landing.
      const warn = frame(directory, due - 4);
      let hits = 0; const total = 32;
      for (let k = 0; k < total; k++) {
        const a = k / total * Math.PI * 2, px = x + Math.cos(a) * radius, py = iy - Math.sin(a) * radius;
        let found = false;
        for (let d = -3; d <= 3 && !found; d++) for (let e = -3; e <= 3 && !found; e++) { const [r, g, b] = warn.at(px + d, py + e); if (b > 170 && b - r > 30 && g > 120) found = true; }
        if (found) hits++;
      }
      entry.warningRing = +(hits / total).toFixed(2);
    } else {
      // Arrow in flight halfway: bright silver pixels near the straight path's midpoint.
      const mid = frame(directory, due - 5);
      const lx = Number(v.launchScreenX), ly = Number(v.launchScreenY);
      const mx = (lx + x) / 2, my = mid.height - (ly + y) / 2;
      let bright = 0;
      for (let dy = -45; dy <= 45; dy++) for (let dx = -45; dx <= 45; dx++) { const px = mid.at(mx + dx, my + dy); if (luma(px) >= 85 && Math.max(...px) - Math.min(...px) < 60) bright++; }
      entry.arrowPixels = bright;
    }
    results.push(entry);
  }
  const p = manifest.presentation ?? {}, stage = manifest.definition.stage;
  const pass = {
    onTime: (p.arrowImpacts ?? 0) > 0 && p.arrowLandedOnTime === p.arrowImpacts,
    landing: results.length > 0 && results.every(r => r.landingContrast >= thresholds.contrast),
    visible: results.length > 0 && results.every(r => r.evolved ? r.warningRing >= .4 : r.arrowPixels >= 6),
    flight: results.filter(r => !r.evolved).every(r => r.shownToDue >= 8 && r.shownToDue <= 12),
    stage: stage === 'evolved' ? (p.arrowRainVolleys ?? 0) > 0 && p.arrowRainVolleys === p.arrowVolleys : (p.arrowRainVolleys ?? 0) === 0,
    reaction: (p.enemyDeathDropped ?? 0) === 0,
  };
  return { volleys: results.length, pass, impacts: p.arrowImpacts, landedOnTime: p.arrowLandedOnTime, rainVolleys: p.arrowRainVolleys,
    note: 'On time = every Core impact landed within 2 frames of its predicted due frame. Landing contrast = brightest pixel inside the first on-screen Core impact radius on the landing frame minus the mean ground there 30 frames earlier. Visible = arrow pixels near the flight midpoint 5 frames before landing (Lv1/max) or water-blue warning ring around the Core radius 4 frames before landing (evolution). Stage = sky rain only in the evolved stage.',
    rows: results };
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
  if (id === 'core:iron_blade') entry.blade = checkBlade(directory, manifest);
  if (id === 'core:canal_bow') entry.arrows = checkArrows(directory, manifest);
  report.captures.push(entry);
}
console.log(JSON.stringify(report, null, 2));
