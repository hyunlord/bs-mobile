#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { parseCsv } from './csv.mjs';
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const csv = file => parseCsv(fs.readFileSync(file, 'utf8'));
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const save = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const fixture = { replay: path.join(repo, 'benchmarks/wave-c05/run.ssreplay'), replaySha256: 'f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5', terminalHash: 'B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8' };
export function makePerformancePlan(root, baked, drawn) {
  assert([root, baked, drawn].every(path.isAbsolute));
  assert.equal(hash(fixture.replay), fixture.replaySha256);
  const entries = [];
  for (const [name, mode, manifest, allEffects] of [['cutout-baseline', 'a-knight-live', null, false], ['3d-baked', '3d-baked', baked, false], ['frame-drawn', 'frame-drawn', drawn, false], ['all8-view-effects', 'a-knight-live', null, true]]) {
    if (manifest) assert.equal(read(manifest).method, mode);
    const directory = path.join(root, 'measurements', name), metricsDirectory = directory + '-metrics';
    entries.push({ name, mode, ...fixture, window: '13500-14400', directory, metricsDirectory, allEffects, manifest, manifestSha256: manifest ? hash(manifest) : null,
      expectedKnight: { hero: mode, cameraScale: 1.4, offlineAudio: false, ...(manifest ? { productionManifestSha256: hash(manifest), productionDirection: 'follow' } : {}) },
      args: ['--knight-review', '--camera-scale', '1.4', '--camera-scale-window', 'dense', '--hero-motion-metrics', metricsDirectory,
        ...(manifest ? ['--hero-production', manifest, '--hero-production-direction', 'follow'] : []),
        ...(allEffects ? ['--m1-weapon-showcase', 'all', '--showcase-stage', 'evolution', '--m1-showcase-catalog', path.join(repo, 'data/review/m1-weapon-showcase.json')] : [])] });
  }
  return { contract: 'm1-knight-measure-v1', purpose: 'm1-production-measure-v1', entries,
    interpretation: 'Capture OFF, same C05 dense replay and 1.4 camera. All8 adds eight simultaneous View-only effects; it is NOT eight combined Core combat modules. Foreground loss is excluded/resumed by native benchmark; observer records app names without screen/accessibility permission. One run per variant is a sample, not a causal regression estimate.' };
}
export function stats(values) {
  const v = values.filter(x => Number.isFinite(x) && x >= 0).sort((a, b) => a - b);
  return v.length ? { samples: v.length, p50: v[Math.ceil(v.length * .5) - 1], p95: v[Math.ceil(v.length * .95) - 1], max: v.at(-1) } : null;
}
export function foregroundFrames(frames, minimumEnemies = 0) {
  return frames.filter(r => Number(r.focused) === 1 && Number(r.partialBoundary) === 0 && Number(r.enemies) >= minimumEnemies);
}
export function frameSummary(frames) {
  assert(frames.length, 'No eligible foreground frames');
  const times = frames.map(r => Number(r.wallMs)); assert(times.every(x => Number.isFinite(x) && x > 0));
  const over25 = times.filter(x => x > 25).length;
  return { frames: frames.length, foregroundSeconds: times.reduce((a, b) => a + b, 0) / 1000, frameMs: stats(times), over25Frames: over25, over25Percent: over25 / frames.length * 100, maximum50msGate: Math.max(...times) <= 50, over25Gate: over25 / frames.length <= .002, enemyCount: stats(frames.map(r => Number(r.enemies))), coreFrameMs: stats(frames.map(r => Number(r.simulationMs))), presentationMs: stats(frames.map(r => Number(r.presentMs))) };
}
export function summarizePerformance(plan) {
  assert.equal(plan.purpose, 'm1-production-measure-v1'); let source;
  const results = plan.entries.map(entry => {
    const b = read(path.join(entry.directory, 'benchmark.json'));
    assert(b.verified && !b.error && !b.sourceDirty && b.performanceEligible && !b.developmentBuild && !b.profilerEnabled, 'Eligible clean Release benchmark required');
    assert.equal(b.replaySha256, entry.replaySha256); assert.equal(b.stateHash, entry.terminalHash); assert.equal(b.windows, entry.window);
    assert.equal(b.width, 1920); assert.equal(b.height, 1080);
    const identity = JSON.stringify([b.commit, b.sourceHash, b.dataHash]); if (source) assert.equal(identity, source); else source = identity;
    const k = read(path.join(entry.directory, 'knight-review.json'));
    for (const [key, value] of Object.entries(entry.expectedKnight)) {
      if (typeof value === 'number') assert(Math.abs(k[key] - value) < 1e-6); else assert.equal(k[key], value);
    }
    if (entry.manifest) assert.equal(hash(entry.manifest), entry.manifestSha256);
    if (entry.allEffects) {
      const m = read(path.join(entry.directory, 'm1-weapon-showcase.json'));
      assert(m.performanceOnly && m.replayVerified); assert.equal(m.simultaneousShapes, 8); assert.equal(m.contentId, 'all'); assert.equal(m.stage, 'evolution'); assert.equal(m.frames, 0);
    }
    assert(!fs.existsSync(path.join(entry.directory, 'offline-capture.json')), 'Capture invalidates performance evidence');
    const frames = foregroundFrames(csv(path.join(entry.directory, 'frames.csv'))), dense = foregroundFrames(frames, 800);
    const m = read(path.join(entry.metricsDirectory, 'hero-motion-metrics.json'));
    assert(m.performanceEligible && !m.captureActive && m.replayVerified && !m.developmentBuild && m.dropped === 0);
    assert.equal(m.mode, entry.mode); assert.equal(m.finalStateHash, entry.terminalHash); assert.equal(m.replaySha256, entry.replaySha256);
    const ids = new Set(dense.map(f => f.frame)), samples = csv(path.join(entry.metricsDirectory, 'hero-motion-metrics.csv')).filter(r => Number(r.focused) === 1 && ids.has(r.frame));
    assert(samples.length, 'No foreground 800+ memory samples');
    const observer = fs.readFileSync(entry.directory + '.foreground.jsonl', 'utf8').trim().split(/\r?\n/).map(JSON.parse);
    assert(observer.some(r => r.event === 'observer-end'));
    return { name: entry.name, commit: b.commit, sourceHash: b.sourceHash, dataHash: b.dataHash, replaySha256: b.replaySha256, fullWindow: frameSummary(frames), density800Plus: frameSummary(dense),
      memory800Plus: { managedBytes: stats(samples.map(r => Number(r.managedBytes))), unityAllocatedBytes: stats(samples.map(r => Number(r.unityAllocatedBytes))) },
      poseMs: stats(samples.map(r => Number(r.previousPoseMs))), skinMs: stats(samples.map(r => Number(r.spriteSkinLateUpdateMs))),
      foregroundApps: [...new Set(observer.map(r => r.name).filter(Boolean))], foregroundLog: entry.directory + '.foreground.jsonl' };
  });
  const baseline = results.find(r => r.name === 'cutout-baseline'); assert(baseline);
  for (const row of results) row.sampleP95DeltaPercent = (row.density800Plus.frameMs.p95 / baseline.density800Plus.frameMs.p95 - 1) * 100;
  return { contract: plan.purpose, interpretation: plan.interpretation, limitations: 'Unity allocated bytes are not process RSS. Negative/unavailable recorder samples become null, never zero. Pacing remains the same native 60fps setting. Three visual methods retain original replay hash; all8 is visual workload only. Native foreground qualification, not external observer timing, decides included frames.', rows: results };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const [command, file, first, second] = process.argv.slice(2);
  if (command === 'plans') { assert(first && second && !fs.existsSync(file)); const plan = makePerformancePlan(file, first, second); fs.mkdirSync(file, { recursive: true }); save(path.join(file, 'measurements.json'), plan); }
  else { const plan = read(file); assert.equal(plan.purpose, 'm1-production-measure-v1');
    if (command === 'run') { assert(first); execFileSync(process.execPath, [path.join(repo, 'tools/m1-knight-measure.mjs'), 'run', file, first], { stdio: 'inherit' }); }
    else if (command === 'summary') console.log(JSON.stringify(summarizePerformance(plan), null, 2));
    else throw Error('Usage: m1-production-measure.mjs plans NEWROOT BAKED_JSON DRAWN_JSON | run PLAN APP | summary PLAN');
  }
}
