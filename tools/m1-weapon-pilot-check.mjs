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
const thresholds = { contrast: 40, arcPeak: 95, corePixels: 4, glowPixels: 16, telegraphFrames: [2, 4], fadeFrames: [8, 10], stationaryRatio: .2 };

// Measurements read the lossless PNG frames when the capture kept them (M1_REVIEW_LOSSLESS=1), else the JPEG frames.
const losslessFrames = directory => fs.existsSync(path.join(directory, 'offline-frames-png'));
function frame(directory, index) {
  const name = `frame-${String(index).padStart(6, '0')}`;
  const file = losslessFrames(directory) ? path.join(directory, 'offline-frames-png', name + '.png') : path.join(directory, 'offline-frames', name + '.jpg');
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

function windowMax(image, x, y, size = 4) {
  let best = 0;
  for (let dy = -size; dy <= size; dy++) for (let dx = -size; dx <= size; dx++) best = Math.max(best, luma(image.at(x + dx, y + dy)));
  return best;
}
const median = values => values.length ? values.slice().sort((a, b) => a - b)[values.length >> 1] : 0;
// Iron blade: wind-up before the arc; the arc centred on the hero with its outer edge on the Core reach, passing over every
// on-screen hit; its brightness against the ground before the swing; gone after its fade; the evolved sword's fall and
// clearance from the hero.
function checkBlade(directory, manifest) {
  const strikes = rows(path.join(directory, 'm1-blade-strikes.csv'));
  const results = [];
  for (const s of strikes) {
    const at = Number(s.frameIndex);
    if (at <= 8) continue;
    // +8: the 9-frame sweep has nearly covered the arc and the fade (from +9) has not begun.
    const before = frame(directory, at - 8), swept = frame(directory, at + 8), gone = frame(directory, at + 19);
    const ox = Number(s.originScreenX), oy = Number(s.originScreenY), hero = Number(s.heroHeightPixels ?? 0);
    const outer = Math.hypot(Number(s.radiusPixelsX), Number(s.radiusPixelsY)), inner = Number(s.innerPixels ?? 0);
    // Samples every 5 degrees across the Core fan, just inside the band's outer edge (its brightest part); off-strip points drop out.
    const aim = Number(s.aim), half = Number(s.halfAngle), points = [];
    for (let a = aim - half; a <= aim + half + .01; a += 5) {
      const x = ox + Math.cos(a * Math.PI / 180) * outer * .93, y = swept.height - (oy + Math.sin(a * Math.PI / 180) * outer * .93);
      if (x >= 3 && y >= 3 && x < swept.width - 3 && y < swept.height - 3 && luma(before.at(x, y)) > 5) points.push([x, y, a]);
    }
    if (points.length < 3) { results.push({ frame: at, skipped: 'arc off screen' }); continue; }
    const ground = points.reduce((n, [x, y]) => n + luma(before.at(x, y)), 0) / points.length;
    // Per arc angle, the brightest pixel across the whole band (inner edge to just past the outer edge).
    const from = Math.max(.3, inner / Math.max(1, outer));
    const band = (image, [x, y]) => { const iy = image.height - oy; let best = 0;
      for (let k = from; k <= 1.02; k += .02) best = Math.max(best, windowMax(image, ox + (x - ox) * k / .93, iy + (y - iy) * k / .93, 2)); return best; };
    const peak = median(points.map(p => band(swept, p)));
    // Band peak for the brightness rule: only the middle 70% of the swing's length (the tapered ends are by design).
    const central = points.filter(([, , a]) => Math.abs(a - aim) <= .7 * half);
    const peakCentral = central.length ? median(central.map(p => band(swept, p))) : peak;
    // Fade: sky-blue samples across the band must be nearly all gone (white damage numbers and grey steel do not count).
    const glow = image => { const iy = image.height - oy; let count = 0;
      for (const [x, y] of points) for (let k = Math.max(.5, inner / Math.max(1, outer)); k <= 1.02; k += .02) { const [r, g, b] = image.at(ox + (x - ox) * k / .93, iy + (y - iy) * k / .93); if (b - r > 25 && b > 140) count++; } return count; };
    const glowSwept = glow(swept), glowGone = glow(gone), glowBefore = glow(before);
    const evolved = s.evolved === 'True';
    const heroX = Number(s.heroScreenX), heroY = Number(s.heroScreenY);
    results.push({ frame: at, tick: Number(s.tick), evolved, windupFrames: Number(s.windupFrames), samples: points.length, heroHeightPixels: +hero.toFixed(1),
      centreFromHeroPixels: +Math.hypot(ox - heroX, oy - heroY).toFixed(1),
      corePixels: +Number(s.coreRadiusPixels).toFixed(1), outerPixels: +outer.toFixed(1), innerPixels: +inner.toFixed(1), thicknessPixels: +Number(s.thicknessPixels ?? 0).toFixed(1),
      hits: Number(s.hits), hitsCovered: Number(s.hitsCovered),
      stage: +Number(s.stage ?? 0).toFixed(3), halfAngle: +Number(s.halfAngle).toFixed(1), arcFullyVisible: s.arcFullyVisible === 'True', shortHalfPixels: +Number(s.shortHalfPixels ?? 0).toFixed(1),
      swordLengthPixels: +Number(s.swordLengthPixels ?? 0).toFixed(1), swordFallFrames: Number(s.swordFallFrames ?? 0), swordShownFallFrames: Number(s.swordShownFallFrames ?? 0),
      swordFromHeroHeroHeights: +Number(s.swordFromHeroHeroHeights ?? 0).toFixed(2), swordInsideMargin: s.swordInsideMargin !== 'False',
      swordFromCrowdPixels: evolved ? +Math.hypot(Number(s.swordTipScreenX) - Number(s.crowdScreenX), Number(s.swordTipScreenY) - Number(s.crowdScreenY)).toFixed(1) : 0,
      ground: +ground.toFixed(1), arcPeak: +peak.toFixed(1), arcPeakCentral: +peakCentral.toFixed(1), frames: losslessFrames(directory) ? 'png' : 'jpg', contrast: +(peak - ground).toFixed(1), glowSwept, glowGone, glowBefore });
  }
  const p = manifest.presentation ?? {}, stage = manifest.definition.stage;
  const measured = results.filter(r => !r.skipped);
  const pass = {
    windup: results.length > 0 && results.every(r => r.skipped || (r.windupFrames >= 3 && r.windupFrames <= 5)),
    // Centred on the drawn hero; radius fixed per stage = min(Core reach, .85 x the play area's short half-width); the
    // whole swing on screen.
    geometry: measured.length > 0 && measured.every(r => r.centreFromHeroPixels <= .1 * r.heroHeightPixels
      && Math.abs(r.outerPixels - Math.min(r.corePixels, .85 * r.shortHalfPixels)) <= 1.5 && r.arcFullyVisible),
    size: measured.length > 0 && measured.every(r => r.thicknessPixels >= .54 * r.heroHeightPixels && (!r.evolved || r.swordLengthPixels >= 3 * r.heroHeightPixels)),
    // Evolved sword: 8-10 frame fall with the blade in view for at least the last 5; plants outside 1.0 hero heights of the
    // hero's centre and 10% inside the play area's edges.
    sword: measured.every(r => !r.evolved || (r.swordFallFrames >= 8 && r.swordFallFrames <= 10 && r.swordShownFallFrames >= 5
      && r.swordFromHeroHeroHeights >= .99 && r.swordInsideMargin)),
    fade: measured.length > 0 && measured.every(r => r.glowSwept > 0 && r.glowGone - r.glowBefore <= Math.max(2, .1 * r.glowSwept)),
    reaction: p.bladeDeferredHits === p.bladePresentedHits && (p.giantBladeKillsOutside ?? 0) === 0 && (p.enemyDeathDropped ?? 0) === 0,
    stage: stage === 'evolved' ? (p.bladeEvolvedStrikes ?? 0) > 0 : (p.bladeEvolvedStrikes ?? 0) === 0,
    // Arc band (painted slash v2): every strike's contrast >= 40, or its brightest band pixel >= 95 (on bright ground pure
    // white is the best a band can do).
    contrast: measured.length > 0 && measured.every(r => r.contrast >= thresholds.contrast || r.arcPeakCentral >= thresholds.arcPeak),
  };
  const recorded = { contrastMin: measured.length ? Math.min(...measured.map(r => r.contrast)) : null, contrastThreshold: thresholds.contrast, arcPeakThreshold: thresholds.arcPeak,
    contrastMet: measured.length > 0 && measured.every(r => r.contrast >= thresholds.contrast),
    strikesByPeakOnly: measured.filter(r => r.contrast < thresholds.contrast && r.arcPeakCentral >= thresholds.arcPeak).map(r => ({ tick: r.tick, contrast: r.contrast, arcPeakCentral: r.arcPeakCentral, arcPeak: r.arcPeak, ground: r.ground })),
    strikesBelow: measured.filter(r => r.contrast < thresholds.contrast && r.arcPeakCentral < thresholds.arcPeak).map(r => ({ tick: r.tick, contrast: r.contrast, arcPeakCentral: r.arcPeakCentral, arcPeak: r.arcPeak, ground: r.ground })),
    hits: measured.reduce((n, r) => n + r.hits, 0), hitsUnderBand: measured.reduce((n, r) => n + r.hitsCovered, 0) };
  return { strikes: results.length, pass, recorded, deferredHits: p.bladeDeferredHits, presentedHits: p.bladePresentedHits, evolvedStrikes: p.bladeEvolvedStrikes,
    note: 'Wind-up = presented frames from the Core attack to the visible arc (4, ±1 for delivery rounding). Geometry = arc centre within .1 hero heights of the drawn hero; radius fixed per stage = min(Core reach, .85 x the play area\'s short half-width); the whole swing on screen. Contrast per strike: >= 40, or the band\'s brightest pixel (median over the arc angles in the middle 70% of the swing) >= 95; frames are the lossless PNGs when the capture kept them. Hits under the band are recorded only. Contrast = median over arc angles (every 5 degrees) of the brightest pixel across the whole band 8 frames after it appears (the 9-frame sweep nearly done), minus the mean ground luma there 8 frames before. Fade = sky-blue samples across the band 19 frames after appearing exceed the pre-swing count by at most 10% of the swept count. Sword = evolved sword falls over 8-10 frames with the blade in view for at least the last 5, plants outside 1.0 hero heights of the hero\'s centre and 10% inside the play area. Size = band >= .55 hero heights thick; evolved sword >= 3 hero heights.',
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
      landingContrast: +(peak - ground).toFixed(1), arrowLengthPixels: +Number(v.arrowLengthPixels ?? 0).toFixed(1), heroHeightPixels: +Number(v.heroHeightPixels ?? 0).toFixed(1) };
    if (evolved) {
      // Warning circle: share of points on the Core radius with a translucent water-blue pixel (over grass, whose blue is ~90)
      // within 3px, 4 frames before landing.
      const warn = frame(directory, due - 4);
      let hits = 0; const total = 32;
      for (let k = 0; k < total; k++) {
        const a = k / total * Math.PI * 2, px = x + Math.cos(a) * radius, py = iy - Math.sin(a) * radius;
        let found = false;
        for (let d = -3; d <= 3 && !found; d++) for (let e = -3; e <= 3 && !found; e++) { const [r, g, b] = warn.at(px + d, py + e); if (b > 130 && b - r > 18) found = true; }
        if (found) hits++;
      }
      entry.warningRing = +(hits / total).toFixed(2);
      // Rain streaks: bright pixels gained in a column above the first impact while the arrows fall (3 frames before landing)
      // against the same column before the cast.
      const rain = frame(directory, due - 3), hero = Math.max(20, entry.heroHeightPixels);
      let gained = 0;
      for (let dy = 0; dy <= hero * 3.5; dy += 2) for (let dx = -radius; dx <= radius; dx += 2) {   // falling streaks are still ~1.8 hero heights up
        if (luma(rain.at(x + dx, iy - dy)) >= 88 && luma(before.at(x + dx, iy - dy)) < 75) gained++;
      }
      entry.rainPixels = gained;
    } else {
      // Arrow contrast: the brightest pixel within 14px of where the renderer put the first arrow's point 5 frames before it
      // lands, against the mean ground there before the cast (the old midpoint box mostly counted white damage numbers).
      const mx = Number(v.midFlightScreenX ?? -1), myRaw = Number(v.midFlightScreenY ?? -1);
      if (mx >= 0) {
        const mid = frame(directory, due + Number(v.midFlightImpactOffset ?? 0) - 5), my = mid.height - myRaw;
        // Ground: the darker of 30 and 40 frames before the cast, so a previous volley's splash or trace lingering in one
        // reference frame does not count as ground.
        const earlier = frame(directory, Math.max(0, at - 40));
        let peakArrow = 0, groundA = 0, groundB = 0, k = 0;
        for (let dy = -14; dy <= 14; dy++) for (let dx = -14; dx <= 14; dx++) { peakArrow = Math.max(peakArrow, luma(mid.at(mx + dx, my + dy))); groundA += luma(before.at(mx + dx, my + dy)); groundB += luma(earlier.at(mx + dx, my + dy)); k++; }
        const groundArrow = Math.min(groundA, groundB);
        entry.arrowPeak = +peakArrow.toFixed(1); entry.arrowContrast = +(peakArrow - groundArrow / k).toFixed(1);
        // The trace left on the path after landing: the same spot 3 frames after the first impact lands.
        const after = frame(directory, due + Number(v.midFlightImpactOffset ?? 0) + 3);
        let peakTrace = 0; for (let dy = -8; dy <= 8; dy++) for (let dx = -8; dx <= 8; dx++) peakTrace = Math.max(peakTrace, luma(after.at(mx + dx, my + dy)));
        entry.traceContrast = +(peakTrace - groundArrow / k).toFixed(1);
      }
      // Bowstring flash at the hand on the frame after launch (flight 10 frames). Ground: the darker of 30 and 40 frames
      // before the cast, as for the arrow (a previous volley's splash lingering in one reference frame is not ground).
      const hx = Number(v.handScreenX ?? -1);
      if (hx >= 0) {
        const launch = frame(directory, due - 10 + 1), hy = launch.height - Number(v.handScreenY), earlierFlash = frame(directory, Math.max(0, at - 40));
        let peakFlash = 0, groundFlashA = 0, groundFlashB = 0, k2 = 0;
        for (let dy = -10; dy <= 10; dy++) for (let dx = -10; dx <= 10; dx++) { peakFlash = Math.max(peakFlash, luma(launch.at(hx + dx, hy + dy))); groundFlashA += luma(before.at(hx + dx, hy + dy)); groundFlashB += luma(earlierFlash.at(hx + dx, hy + dy)); k2++; }
        entry.bowstringContrast = +(peakFlash - Math.min(groundFlashA, groundFlashB) / k2).toFixed(1);
      }
    }
    results.push(entry);
  }
  const p = manifest.presentation ?? {}, stage = manifest.definition.stage;
  const pass = {
    onTime: (p.arrowImpacts ?? 0) > 0 && p.arrowLandedOnTime === p.arrowImpacts,   // per impact: Core lands a cast's impacts one step apart
    landing: results.length > 0 && results.every(r => r.landingContrast >= thresholds.contrast),
    visible: results.length > 0 && results.every(r => r.evolved ? r.rainPixels >= 40 : r.arrowContrast !== undefined && r.arrowContrast >= thresholds.contrast),
    size: results.length > 0 && results.every(r => r.arrowLengthPixels >= .5 * r.heroHeightPixels),
    flight: results.filter(r => !r.evolved).every(r => r.shownToDue >= 8 && r.shownToDue <= 12),
    stage: stage === 'evolved' ? (p.arrowRainVolleys ?? 0) > 0 && p.arrowRainVolleys === p.arrowVolleys : (p.arrowRainVolleys ?? 0) === 0,
    reaction: (p.enemyDeathDropped ?? 0) === 0,
  };
  return { volleys: results.length, pass, impacts: p.arrowImpacts, landedOnTime: p.arrowLandedOnTime, rainVolleys: p.arrowRainVolleys,
    note: 'On time = every Core impact landed within 2 frames of its predicted due frame. Landing contrast = brightest pixel inside the first on-screen Core impact radius on the landing frame minus the mean ground there 30 frames earlier. Visible = arrow pixels near the flight midpoint 5 frames before landing (Lv1/max) or bright rain-streak pixels gained above the first impact 3 frames before landing (evolution; the merged thin warning outline is reported as warningRing, not gated). Size = arrows at least half a hero tall. Stage = sky rain only in the evolved stage.',
    rows: results };
}

// Canal bow ricochet (ADR0062): every flight lands on its Core tick; the arrow stands out 5 frames before it lands; each
// landing splashes; bounces only from the max stage up; the evolved water channel is cut along the flown path.
function checkRicochet(directory, manifest) {
  const hops = rows(path.join(directory, 'm1-ricochet-hops.csv')).filter(h => h.onScreen === 'True' && Number(h.frameIndex) > 48);
  const cache = new Map(); const at = index => { if (!cache.has(index)) cache.set(index, frame(directory, index)); return cache.get(index); };
  const box = (image, x, y, size, fn) => { for (let dy = -size; dy <= size; dy++) for (let dx = -size; dx <= size; dx++) fn(image.at(x + dx, y + dy)); };
  const water = ([r, g, b]) => b > 150 && b - r > 60;
  // At most 48 flights, evenly spread through the capture, are measured (each needs several frames decoded).
  const step = Math.max(1, Math.ceil(hops.length / 48)), results = [];
  for (let i = 0; i < hops.length; i += step) {
    const h = hops[i], landed = Number(h.frameIndex), cast = landed + Number(h.acceptedOffset), evolved = h.evolved === 'True';
    const g30 = at(Math.max(0, cast - 30)), g40 = at(Math.max(0, cast - 40)), height = g30.height;
    const ground = (x, y, size) => { let a = 0, b = 0, k = 0; box(g30, x, y, size, p => { a += luma(p); k++; }); box(g40, x, y, size, p => { b += luma(p); }); return Math.min(a, b) / k; };
    const entry = { frame: landed, tick: Number(h.tick), chain: Number(h.chain), hop: Number(h.hop), evolved, turns: h.turns === 'True',
      arrowLengthPixels: +Number(h.arrowLengthPixels).toFixed(1), heroHeightPixels: +Number(h.heroHeightPixels).toFixed(1) };
    const mx = Number(h.midScreenX), my = height - Number(h.midScreenY);
    if (mx >= 0) { let peak = 0; box(at(landed + Number(h.midOffset)), mx, my, 14, p => { peak = Math.max(peak, luma(p)); }); entry.arrowContrast = +(peak - ground(mx, my, 14)).toFixed(1); }
    const tx = Number(h.toScreenX), ty = height - Number(h.toScreenY);
    let splash = 0; box(at(landed + 1), tx, ty, 20, p => { splash = Math.max(splash, luma(p)); }); entry.landingContrast = +(splash - ground(tx, ty, 20)).toFixed(1);
    entry.pathPixels = +Math.hypot(tx - Number(h.fromScreenX), ty - (height - Number(h.fromScreenY))).toFixed(1);
    if (evolved && entry.pathPixels >= .5 * entry.heroHeightPixels) {
      // Channel (flights at least half a hero long; shorter ones lie under the hero and the crowd): of 9 points along the
      // flown path, the share with water-blue pixels on the frame after landing (the camera follows the walking hero, so
      // later frames no longer match the recorded screen points) that were not water-blue before the cast.
      const fx = Number(h.fromScreenX), fy = height - Number(h.fromScreenY), after = at(landed + 1);
      let cut = 0;
      for (let k = 1; k <= 9; k++) {
        const x = fx + (tx - fx) * k / 10, y = fy + (ty - fy) * k / 10;
        let now = 0, before = 0; box(after, x, y, 3, p => { if (water(p)) now++; }); box(g30, x, y, 3, p => { if (water(p)) before++; });
        if (now >= 6 && before < 6) cut++;
      }
      entry.channelShare = +(cut / 9).toFixed(2);
    }
    results.push(entry);
  }
  const p = manifest.presentation ?? {}, stage = manifest.definition.stage;
  const measured = results.filter(r => r.arrowContrast !== undefined), share = (list, test) => list.length ? list.filter(test).length / list.length : 0;
  const pass = {
    onTime: (p.ricochetLanded ?? 0) > 0 && p.ricochetLandedOnTime === p.ricochetLanded,
    visible: measured.length > 0 && share(measured, r => r.arrowContrast >= thresholds.contrast) >= .9,
    landing: results.length > 0 && share(results, r => r.landingContrast >= thresholds.contrast) >= .9,
    size: results.length > 0 && results.every(r => r.arrowLengthPixels >= .5 * r.heroHeightPixels),
    bounces: stage === 'lv1' ? (p.ricochetBounces ?? 0) === 0 : (p.ricochetBounces ?? 0) > 0,
    stage: stage === 'evolved' ? (p.ricochetChannelDraws ?? 0) > 0 && median(results.filter(r => r.channelShare !== undefined).map(r => r.channelShare)) >= .5 : (p.ricochetChannelDraws ?? 0) === 0,
    reaction: (p.enemyDeathDropped ?? 0) === 0,
  };
  const contrasts = measured.map(r => r.arrowContrast);
  return { flights: p.ricochetHops, bounces: p.ricochetBounces, landed: p.ricochetLanded, landedOnTime: p.ricochetLandedOnTime, channelDraws: p.ricochetChannelDraws,
    measured: results.length, arrowContrastMin: contrasts.length ? Math.min(...contrasts) : null, arrowContrastMedian: median(contrasts),
    landingContrastMedian: median(results.map(r => r.landingContrast)), pathPixelsMedian: median(results.map(r => r.pathPixels)),
    channelMeasured: results.filter(r => r.channelShare !== undefined).length, channelShareMedian: stage === 'evolved' ? median(results.filter(r => r.channelShare !== undefined).map(r => r.channelShare)) : null, pass,
    note: 'On time = every flight that landed did so within 2 frames of its Core due time. Visible = arrow peak within 14px of where it was drawn 5 frames before landing minus the darker of the mean ground 30 and 40 frames before its cast, >= 40 for 90% of measured flights (up to 48 spread through the capture); landing the same for the splash 1 frame after landing (20px). Bounces only from the max stage up. Evolved: water channel drawn and water-blue gained on at least half of 9 points along the flown path 1 frame after landing (median over flights at least half a hero long; shorter flights lie under the hero and the crowd).',
    rows: results };
}

// Granary sling lure-bomb (ADR0063): every sack lands and bursts on its Core tick; the burst stands out from the ground;
// the giant sack (swirl) only in the evolved stage.
function checkSling(directory, manifest) {
  const casts = rows(path.join(directory, 'm1-sling-casts.csv')).filter(c => c.onScreen === 'True' && Number(c.frameIndex) > 48);
  const results = [];
  for (const c of casts) {
    const burst = Number(c.frameIndex), cast = burst + Number(c.acceptedOffset);
    const now = frame(directory, burst + 2), g30 = frame(directory, Math.max(0, cast - 30)), g40 = frame(directory, Math.max(0, cast - 40));
    const x = Number(c.centreScreenX), y = now.height - Number(c.centreScreenY), r = Math.max(8, .5 * Number(c.radiusPixels));
    let peak = 0, a = 0, b = 0, n = 0;
    for (let dy = -r; dy <= r; dy += 3) for (let dx = -r; dx <= r; dx += 3) {
      if (dx * dx + dy * dy > r * r) continue;
      peak = Math.max(peak, luma(now.at(x + dx, y + dy))); a += luma(g30.at(x + dx, y + dy)); b += luma(g40.at(x + dx, y + dy)); n++;
    }
    results.push({ frame: burst, tick: Number(c.tick), evolved: c.evolved === 'True', flightFrames: Number(c.flightFrames), lureFrames: Number(c.lureFrames),
      burstContrast: +(peak - Math.min(a, b) / Math.max(1, n)).toFixed(1), radiusPixels: +Number(c.radiusPixels).toFixed(1), sackPixels: +Number(c.sackPixels).toFixed(1), heroHeightPixels: +Number(c.heroHeightPixels).toFixed(1) });
  }
  const p = manifest.presentation ?? {}, stage = manifest.definition.stage;
  const pass = {
    landedOnTime: (p.slingLanded ?? 0) > 0 && p.slingLandedOnTime === p.slingLanded,
    burstOnTime: (p.slingBursts ?? 0) > 0 && p.slingBurstsOnTime === p.slingBursts,
    burst: results.length > 0 && results.every(r => r.burstContrast >= thresholds.contrast),
    stage: stage === 'evolved' ? (p.slingVortexFrames ?? 0) > 0 : (p.slingVortexFrames ?? 0) === 0,
    reaction: (p.enemyDeathDropped ?? 0) === 0,
  };
  return { casts: p.slingCasts, landed: p.slingLanded, landedOnTime: p.slingLandedOnTime, bursts: p.slingBursts, burstsOnTime: p.slingBurstsOnTime, vortexFrames: p.slingVortexFrames,
    measured: results.length, burstContrastMin: results.length ? Math.min(...results.map(r => r.burstContrast)) : null, pass,
    note: 'On time = every sack landed (Core review-lure) and burst (review-impact) within 2 frames of its predicted frame. Burst = brightest pixel within half the Core radius 2 frames after the burst minus the darker of the mean ground 30 and 40 frames before the cast, >= 40 on every on-screen burst. Stage = the giant sack swirl only in the evolved stage.',
    rows: results };
}

// Furrow pike: impacts land on their Core ticks; the pike's point stands out on the frame its last on-screen impact lands;
// the furrow is wider Lv1 < max < evolved; the palisade (erupted pikes) only in the evolved stage.
function checkPikes(directory, manifest) {
  const casts = rows(path.join(directory, 'm1-pike-casts.csv'));
  const results = [];
  for (const c of casts) {
    const at = Number(c.frameIndex);
    if (at <= 40 || c.lastImpactOnScreen !== 'True') continue;
    // The row is written as the cast's last impact lands; lastImpactFrameOffset (<= 0) is the last on-screen impact's landing.
    const last = c.lastImpactFrameOffset !== undefined ? at + Number(c.lastImpactFrameOffset) : at + Number(c.shownToDueFrames) + Number(c.stepFrames) * (Number(c.impacts) - 1);
    const x = Number(c.lastImpactScreenX), image = frame(directory, last + 1), y = image.height - Number(c.lastImpactScreenY);
    const cast = c.lastImpactFrameOffset !== undefined ? at + Number(c.lastImpactFrameOffset) - Number(c.stepFrames) * (Number(c.impacts) - 1) - 30 : at;   // the cast: 15 ticks (30 frames) before the first impact
    const before = frame(directory, Math.max(0, cast - 30)), earlier = frame(directory, Math.max(0, cast - 40));
    let peak = 0, groundA = 0, groundB = 0, k = 0;
    for (let dy = -14; dy <= 14; dy++) for (let dx = -14; dx <= 14; dx++) { peak = Math.max(peak, luma(image.at(x + dx, y + dy))); groundA += luma(before.at(x + dx, y + dy)); groundB += luma(earlier.at(x + dx, y + dy)); k++; }
    results.push({ frame: at, tick: Number(c.tick), impacts: Number(c.impacts), onScreen: Number(c.onScreenImpacts), evolved: c.evolved === 'True', stage: Number(c.stage),
      tipContrast: +(peak - Math.min(groundA, groundB) / k).toFixed(1), pikeLengthPixels: +Number(c.pikeLengthPixels).toFixed(1), furrowWidthPixels: +Number(c.furrowWidthPixels).toFixed(1), heroHeightPixels: +Number(c.heroHeightPixels).toFixed(1) });
  }
  const p = manifest.presentation ?? {}, stage = manifest.definition.stage;
  const pass = {
    // Impacts are counted as they land: those due after the capture's last frame never land and are not late.
    onTime: (p.pikeImpactsLanded ?? p.pikeImpacts ?? 0) > 0 && p.pikeLandedOnTime === (p.pikeImpactsLanded ?? p.pikeImpacts),
    visible: results.length > 0 && results.every(r => r.tipContrast >= thresholds.contrast),
    size: results.length > 0 && results.every(r => r.pikeLengthPixels >= r.heroHeightPixels),
    stage: stage === 'evolved' ? (p.pikeEruptions ?? 0) > 0 : (p.pikeEruptions ?? 0) === 0,
    reaction: (p.enemyDeathDropped ?? 0) === 0,
  };
  return { casts: results.length, pass, impacts: p.pikeImpacts, impactsLanded: p.pikeImpactsLanded, landedOnTime: p.pikeLandedOnTime, eruptions: p.pikeEruptions,
    note: 'On time = every Core impact landed within 2 frames of its predicted tick. Visible = brightest pixel within 14px of the last on-screen impact (its screen position on the frame it lands, inside the 500px portrait column) on the frame after it lands (the point flash and the pike point) minus the darker of the mean ground 30 and 40 frames before the cast. Size = pike at least a hero tall. Stage = erupted pikes only in the evolved stage. Furrow width is recorded (Lv1 < max < evolved by construction).',
    rows: results };
}

// Spore fan (designed ground patch): Core pulses land on their ticks; the patch reads on the frame after its first pulse
// (spore colour and contrast inside it against the darker of the ground 30 and 40 frames before it was laid); the vortex
// only in the evolved stage.
function checkSporePatches(directory, manifest) {
  const patches = rows(path.join(directory, 'm1-spore-patches.csv'));
  const results = [];
  for (const c of patches) {
    const at = Number(c.frameIndex);
    if (at <= 40 || c.onScreen !== 'True') continue;
    const x = Number(c.centreScreenX), image = frame(directory, at + 2), y = image.height - Number(c.centreScreenY), radius = Number(c.radiusPixels) * .8;
    const before = frame(directory, at - 30), earlier = frame(directory, Math.max(0, at - 40));
    let peak = 0, groundA = 0, groundB = 0, k = 0, coloured = 0;
    for (let dy = -radius; dy <= radius; dy += 3) for (let dx = -radius; dx <= radius; dx += 3) {
      if (dx * dx + dy * dy > radius * radius || x + dx < 710 || x + dx > 1210) continue;
      const c2 = image.at(x + dx, y + dy); peak = Math.max(peak, luma(c2)); groundA += luma(before.at(x + dx, y + dy)); groundB += luma(earlier.at(x + dx, y + dy)); k++;
      const [r, g] = c2; if (g >= 190 && g - r >= 15) coloured++;
    }
    if (!k) continue;
    results.push({ frame: at, tick: Number(c.tick), pulses: Number(c.pulses), stepFrames: Number(c.stepFrames), evolved: c.evolved === 'True',
      contrast: +(peak - Math.min(groundA, groundB) / k).toFixed(1), colouredPixels: coloured, radiusPixels: +Number(c.radiusPixels).toFixed(1), heroHeightPixels: +Number(c.heroHeightPixels).toFixed(1) });
  }
  const p = manifest.presentation ?? {}, stage = manifest.definition.stage;
  const pass = {
    onTime: (p.sporePulses ?? 0) > 0 && p.sporePulsesOnTime === p.sporePulses,
    contrast: results.length > 0 && results.every(r => r.contrast >= thresholds.contrast),
    colour: results.length > 0 && results.every(r => r.colouredPixels >= 20),
    stage: stage === 'evolved' ? (p.vortexFrames ?? 0) > 0 : (p.vortexFrames ?? 0) === 0,
    reaction: (p.enemyDeathDropped ?? 0) === 0,
  };
  return { patches: results.length, pass, pulses: p.sporePulses, pulsesOnTime: p.sporePulsesOnTime, vortexFrames: p.vortexFrames,
    note: 'On time = every Core pulse landed within 2 frames of its predicted tick. Contrast = brightest pixel inside 0.8 of the patch radius (inside the 500px play column) 2 frames after the first pulse minus the darker of the mean ground there 30 and 40 frames before the patch was laid. Colour = bright yellow-green or teal pixels there. Stage = vortex only in the evolved stage.',
    rows: results };
}

// Spore fan / guard ring: the orbiter stands out from the ground, is at least hero sized, carries its weapon colour (teal /
// gold), and the evolution element appears only in the evolved stage.
function checkOrbits(directory, manifest, shape) {
  const activations = rows(path.join(directory, 'm1-orbit-activations.csv'));
  const results = [];
  for (const a of activations) {
    const at = Number(a.frameIndex), x = Number(a.firstOrbiterScreenX), y = Number(a.firstOrbiterScreenY);
    if (at <= 20 || x < 0) continue;
    const image = frame(directory, at), before = frame(directory, at - 20);
    const iy = image.height - y, size = Number(a.orbiterSizePixels), hero = Number(a.heroHeightPixels);
    // Spore fan (shape "tornado" in Core): a box around the orbiter wide enough for its outward fan (the Core orbiter radius
    // plus a puff, ~1.8 puff heights each way); shield: around its centre.
    const half = shape === 'tornado' ? size * 1.8 : size / 2, top = iy - half, bottom = iy + half;
    // Orbit activations run back to back, so the same box earlier still holds orbiters: the ground reference is the median
    // luma of the play area on the frame before (mostly grass).
    const field = [];
    for (let py = 100; py < before.height - 80; py += 12) for (let px = before.width / 2 - 240; px <= before.width / 2 + 240; px += 12) field.push(luma(before.at(px, py)));
    const ground = median(field);
    let peak = 0, coloured = 0;
    for (let py = top; py <= bottom; py += 2) for (let px = x - half; px <= x + half; px += 2) {
      const c = image.at(px, py); peak = Math.max(peak, luma(c));
      const [r, g, b] = c;
      if (shape === 'tornado' ? (g >= 190 && g - r >= 15) : (r > 170 && g > 130 && b < 130 && r - b > 70)) coloured++;   // spore: bright yellow-green or teal
    }
    results.push({ frame: at, activation: Number(a.activation), evolved: a.evolved === 'True', count: Number(a.count), drawn: Number(a.drawn ?? a.count), heroHeightPixels: hero, orbiterSizePixels: +size.toFixed(1),
      contrast: +(peak - ground).toFixed(1), colouredPixels: coloured });
  }
  const p = manifest.presentation ?? {}, stage = manifest.definition.stage;
  const evolvedElement = shape === 'tornado' ? (p.vortexFrames ?? 0) : (p.shieldThrows ?? 0);
  const contrastMet = results.length > 0 && results.every(r => r.contrast >= thresholds.contrast);
  const pass = {
    // Guard ring contrast is recorded only until the shield sprite is in.
    ...(shape === 'tornado' ? { contrast: contrastMet } : {}),
    size: results.length > 0 && results.every(r => shape === 'tornado' ? r.orbiterSizePixels >= .49 * r.heroHeightPixels && r.orbiterSizePixels <= .61 * r.heroHeightPixels
      : r.orbiterSizePixels >= .34 * r.heroHeightPixels && r.orbiterSizePixels <= .46 * r.heroHeightPixels && r.drawn <= 6),
    colour: results.length > 0 && results.every(r => r.colouredPixels >= 20),
    stage: stage === 'evolved' ? evolvedElement > 0 : evolvedElement === 0,
    reaction: (p.orbitContactFlashes ?? 0) > 0 && (p.enemyDeathDropped ?? 0) === 0,
  };
  const recorded = shape === 'shield' ? { contrastMet, contrastMin: results.length ? Math.min(...results.map(r => r.contrast)) : null, contrastThreshold: thresholds.contrast } : undefined;
  return { activations: results.length, pass, recorded, contactFlashes: p.orbitContactFlashes, vortexFrames: p.vortexFrames, shieldThrows: p.shieldThrows,
    note: 'Measured on the frame each activation finishes spreading, around its first orbiter (spore fan: a box 1.8 puff heights each way; shield: around its centre), against the median luma of the play area 20 frames earlier (orbit activations run back to back, so the same box would still hold orbiters). Size: spore puff .5-.6 and shield .35-.45 hero heights whatever the Core collision radius, at most 6 shields drawn per activation. Colour: bright yellow-green or teal (spore) or gold (guard) pixels in the box. Stage: vortex (spore) or thrown shields (guard) only in the evolved stage.',
    rows: results };
}

const directories = process.argv.slice(2);
assert(directories.length > 0, 'Usage: m1-weapon-pilot-check.mjs <capture directory>...');
const report = { contract: 'm1-weapon-pilot-check-v1', interpretation: 'Automated pre-screen only; the user video verdict decides acceptance. Occlusion is covered by the PlayMode bounding-box test; stage distinction is a blind human check on the three stage screenshots; frame cost uses the native combined measurement.', captures: [] };
for (const directory of directories) {
  const manifest = read(path.join(directory, 'm1-combat-review.json'));
  const id = manifest.definition.id, stage = manifest.definition.stage;
  const entry = { directory: path.basename(directory), id, stage };
  // P5 budget: share of enemy sprite pixels covered by own weapon effects, per frame (renderer mask readback).
  const p = manifest.presentation ?? {};
  if (p.occlusionFrames > 0)
    entry.occlusion = { frames: p.occlusionFrames, p95: +Number(p.occlusionP95).toFixed(3), max: +Number(p.occlusionMax).toFixed(3), framesOver25: p.occlusionFramesOver25,
      uncappedP95: +Number(p.occlusionUncappedP95 ?? p.occlusionP95).toFixed(3), uncappedMax: +Number(p.occlusionUncappedMax ?? p.occlusionMax).toFixed(3),
      pass: p.occlusionP95 <= .25, note: 'Covered enemy pixels / enemy pixels per frame on a half-resolution mask: enemy sprite alpha vs own weapon effect alpha x opacity on layers 70, 71, 74-76 (attack edges, impact particles, projectiles, glow, shield emblems), with effect alpha capped at 0.5 over enemy pixels as the renderer draws it (uncapped kept for comparison). Full-screen tints (storm sky 73, storm flash 77) are excluded and checked under tint. Gate: p95 <= 25%.' };
  // Full-screen tint, checked apart from the occlusion budget: alpha <= .25 and enemy-ground contrast >= 70% of untinted.
  if ((p.tintFrames ?? 0) > 0)
    entry.tint = { frames: p.tintFrames, maxAlpha: +Number(p.tintMaxAlpha).toFixed(3), minContrast: +Number(p.tintMinContrast).toFixed(3),
      framesOverAlpha: p.tintFramesOverAlpha, framesUnderContrast: p.tintFramesUnderContrast,
      pass: p.tintMaxAlpha <= .25 && p.tintMinContrast >= .7, recordOnly: id === 'core:storm_fork',
      note: 'Strongest normal-blend full-screen darkening per frame. A uniform tint of alpha a leaves (1 - a) of every luminance difference, enemy against ground included; the additive storm flash shifts both equally. Gate: alpha <= .25 and contrast >= .70. Lightning evolution is recorded only, to be reviewed again in the combined six-weapon scene.' };
  if (id === 'core:storm_fork') entry.lightning = checkLightning(directory, manifest);
  if (id === 'core:muster_horn') entry.minions = checkMinions(directory, manifest);
  if (id === 'core:iron_blade') entry.blade = checkBlade(directory, manifest);
  if (id === 'core:canal_bow') { if (rows(path.join(directory, 'm1-ricochet-hops.csv')).length > 0) entry.ricochet = checkRicochet(directory, manifest); else entry.arrows = checkArrows(directory, manifest); }
  if (id === 'core:spore_fan') entry.patches = checkSporePatches(directory, manifest);
  if (id === 'core:granary_sling' && rows(path.join(directory, 'm1-sling-casts.csv')).length > 0) entry.sling = checkSling(directory, manifest);
  if (id === 'core:ward_orbit') entry.orbits = checkOrbits(directory, manifest, 'shield');
  if (id === 'core:ditch_pike') entry.pikes = checkPikes(directory, manifest);
  report.captures.push(entry);
}
console.log(JSON.stringify(report, null, 2));
