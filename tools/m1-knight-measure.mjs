#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { spawn, execFileSync } from 'node:child_process';
import { parseCsv } from './csv.mjs';
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const rows = file => parseCsv(fs.readFileSync(file, 'utf8'));
const save = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const c05 = { replay: path.join(repo, 'benchmarks/wave-c05/run.ssreplay'), replaySha256: 'f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5', terminalHash: 'B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8' };
const damage = { replay: path.join(repo, 'benchmarks/m1-254/dense-approach-candidate2.ssreplay'), replaySha256: '07f211d9d8a1e7a22110d7438c29e69106e6bf619cce681b2b89c28527b9cec6', terminalHash: 'F57632711CE0294F63A95668695B5281EED15E9788D00C769730A701C52F11F6' };
function plans(root) {
  assert(path.isAbsolute(root) && !fs.existsSync(root)); fs.mkdirSync(root, { recursive: true });
  for (const window of ['early', 'mid', 'dense', 'damage']) {
    const short = window === 'damage', fixture = short ? damage : c05;
    save(path.join(root, `${window}-review.json`), { contract: 'm1-knight-review-v1', ...fixture, width: 1920, height: 1080, frames: short ? 286 : 1800, durationLabel: short ? 'distinct candidate2: 4.7667 seconds, not 30 seconds' : '30 seconds original C05', crop: { x: 710, y: 0, width: 500, height: 1080 }, heroCoordinates: 'knight-rendered.csv', runs: ['before', 'after'].map(role => ({ role, directory: path.join(root, 'captures', `${window}-${role}`), args: ['--knight-review', '--camera-scale', '1.4', '--camera-scale-window', window, ...(short ? ['--knight-damage-replay'] : []), ...(role === 'before' ? ['--impact-feedback-baseline'] : [])], expected: { 'knight-review.json': { contract: 'knight-impact-v1', hero: 'a-knight-live', impactFeedback: role === 'after', cameraScale: 1.4, edgeWarnings: true, replayVerified: true, offlineAudio: true, fixture: short ? 'distinct-dense-approach-candidate2' : 'original-frozen-c05' } } })) });
  }
  const entries = [];
  for (const [name, scale, warnings] of [['damage-1-off', '1', false], ['damage-1.4-off', '1.4', false], ['damage-1.4-on', '1.4', true]]) entries.push({ name, ...damage, window: '13500-13643', directory: path.join(root, 'measurements', name), args: ['--knight-review', '--knight-damage-replay', '--camera-scale-window', 'damage', '--camera-scale', scale, ...(warnings ? [] : ['--edge-warnings-off'])], expectedKnight: { cameraScale: Number(scale), edgeWarnings: warnings }, actualDamage: true });
  for (const [name, mode, flags] of [['knight-a', 'a-knight-live', ['--knight-review']], ['preserved-b', 'live', ['--hero-motion', 'live']]]) {
    const directory = path.join(root, 'measurements', name), metricsDirectory = directory + '-metrics';
    entries.push({ name, ...c05, window: '13500-14400', directory, metricsDirectory, mode, args: [...flags, '--camera-scale', '1.4', '--camera-scale-window', 'dense', '--hero-motion-metrics', metricsDirectory] });
  }
  save(path.join(root, 'measurements.json'), { contract: 'm1-knight-measure-v1', entries });
}
async function run(plan, app) {
  try { assert(!execFileSync('pgrep', ['-f', '/Contents/MacOS/Sow and Siege'], { encoding: 'utf8' }).trim(), 'Another game is running'); } catch (error) { if (error.status !== 1) throw error; }
  const parent = path.dirname(plan.entries[0].directory); fs.mkdirSync(parent, { recursive: true });
  const observerBinary = path.join(parent, 'observe-foreground');
  if (!fs.existsSync(observerBinary)) execFileSync('swiftc', [path.join(repo, 'tools/observe-foreground.swift'), '-o', observerBinary]);
  const activator = path.join(parent, 'activate-exact-pid');
  if (!fs.existsSync(activator)) {
    const source = activator + '.swift'; fs.writeFileSync(source, 'import AppKit\nimport Foundation\nif let pid = Int32(CommandLine.arguments[1]), let app = NSRunningApplication(processIdentifier: pid) { if !app.isActive { _ = app.activate() } } else { exit(2) }\n', { flag: 'wx' });
    execFileSync('swiftc', [source, '-o', activator]);
  }
  for (const entry of plan.entries) {
    assert.equal(createHash('sha256').update(fs.readFileSync(entry.replay)).digest('hex'), entry.replaySha256);
    assert(!fs.existsSync(entry.directory), 'Refusing existing result');
    const fd = fs.openSync(entry.directory + '.stdout.log', 'wx');
    const observer = spawn(observerBinary, [entry.directory + '.foreground.jsonl', '1800'], { stdio: ['ignore', fd, fd] });
    const observed = new Promise(resolve => observer.on('close', resolve));
    const binary = path.join(app, 'Contents/MacOS/Sow and Siege');
    const args = ['--wave-benchmark', entry.replay, '--wave-benchmark-output', entry.directory, ...entry.args, '-screen-width', '1920', '-screen-height', '1080', '-logFile', entry.directory + '.player.log'];
    assert(!args.some(value => value.startsWith('--offline-')), 'Measurements cannot capture');
    save(entry.directory + '.invocation.json', { binary, args, utc: new Date().toISOString() });
    const child = spawn(binary, args, { stdio: ['ignore', fd, fd] });
    let activation;
    const activate = setTimeout(() => { activation = spawn(activator, [String(child.pid)], { stdio: ['ignore', fd, fd] }); }, 3000);
    let killTimer; const deadline = setTimeout(() => { child.kill('SIGTERM'); killTimer = setTimeout(() => child.kill('SIGKILL'), 5000); }, 1800000);
    console.log(`START ${entry.name} ${new Date().toISOString()}`);
    try { await new Promise((resolve, reject) => { child.on('error', reject); child.on('close', (code, signal) => code === 0 && !signal ? resolve() : reject(Error(`Native run failed ${code}/${signal}`))); }); }
    finally { clearTimeout(activate); clearTimeout(deadline); clearTimeout(killTimer); activation?.kill('SIGTERM'); observer.kill('SIGTERM'); await observed; fs.closeSync(fd); }
    check(entry); console.log(`END ${entry.name} ${new Date().toISOString()}`);
  }
}
function check(entry) {
  const b = read(path.join(entry.directory, 'benchmark.json'));
  assert(b.verified && !b.error && !b.sourceDirty && b.performanceEligible && !b.developmentBuild && !b.profilerEnabled);
  assert.equal(b.replaySha256, entry.replaySha256); assert.equal(b.stateHash, entry.terminalHash); assert.equal(b.windows, entry.window);
  assert.equal(b.width, 1920); assert.equal(b.height, 1080);
  if (entry.expectedKnight) { const k = read(path.join(entry.directory, 'knight-review.json')); for (const [key, value] of Object.entries(entry.expectedKnight)) { if (typeof value === 'number') assert(Math.abs(k[key] - value) < 1e-6); else assert.equal(k[key], value); } }
  return b;
}
function stats(values) { const ordered = values.filter(value => Number.isFinite(value) && value >= 0).sort((a, b) => a - b); return ordered.length ? { samples: ordered.length, p50: ordered[Math.ceil(ordered.length * .5) - 1], p95: ordered[Math.ceil(ordered.length * .95) - 1], max: ordered.at(-1) } : null; }
function summarize(plan) {
  let source; const result = []; let hitTape;
  for (const entry of plan.entries) {
    const b = check(entry), id = JSON.stringify([b.commit, b.sourceHash]); if (source) assert.equal(id, source); else source = id;
    const frames = rows(path.join(entry.directory, 'frames.csv')).filter(row => Number(row.focused) === 1 && Number(row.partialBoundary) === 0); assert(frames.length);
    const row = { name: entry.name, commit: b.commit, sourceHash: b.sourceHash, replaySha256: b.replaySha256, window: b.windows, frameMs: stats(frames.map(frame => Number(frame.wallMs))), over25Percent: frames.filter(frame => Number(frame.wallMs) > 25).length / frames.length * 100 };
    if (entry.actualDamage) {
      const manifest = read(path.join(entry.directory, 'actual-lord-damage.json')); assert(manifest.observationsComplete && manifest.dropped === 0 && manifest.unclassified === 0);
      const all = rows(path.join(entry.directory, 'actual-lord-damage.csv'));
      const tape = all.map(hit => ['tick', 'attackerId', 'projectileId', 'definitionId', 'kind', 'damage'].map(key => hit[key]).join(',')).join('\n'); if (hitTape !== undefined) assert.equal(tape, hitTape, 'Same-input actual damage changed'); else hitTape = tape;
      const hits = all.filter(hit => Number(hit.foreground) === 1 && Number(hit.damage) > 0);
      const known = hits.filter(hit => Number(hit.knownHistory) === 1 && Number(hit.attackerId) >= 0), recent = known.filter(hit => Number(hit.recentEntry) === 1);
      Object.assign(row, { actualHits: hits.length, damage: hits.reduce((sum, hit) => sum + Number(hit.damage), 0), denominatorKnownHits: known.length, recentEntrantHits: recent.length, recentEntrantPercent: known.length ? recent.length / known.length * 100 : null, unknownHistory: hits.filter(hit => Number(hit.knownHistory) !== 1).length, unknownOwner: hits.filter(hit => Number(hit.attackerId) < 0).length });
    }
    if (entry.metricsDirectory) {
      const m = read(path.join(entry.metricsDirectory, 'hero-motion-metrics.json')); assert(m.performanceEligible && !m.captureActive && m.replayVerified && !m.developmentBuild && m.dropped === 0); assert.equal(m.mode, entry.mode); assert.equal(m.finalStateHash, entry.terminalHash); assert.equal(m.replaySha256, entry.replaySha256);
      const ids = new Set(frames.map(frame => frame.frame)); const samples = rows(path.join(entry.metricsDirectory, 'hero-motion-metrics.csv')).filter(sample => Number(sample.focused) === 1 && ids.has(sample.frame));
      Object.assign(row, { managedBytes: stats(samples.map(s => Number(s.managedBytes))), unityAllocatedBytes: stats(samples.map(s => Number(s.unityAllocatedBytes))), poseMs: stats(samples.map(s => Number(s.previousPoseMs))), skinMs: stats(samples.map(s => Number(s.spriteSkinLateUpdateMs))) });
    }
    const observer = fs.readFileSync(entry.directory + '.foreground.jsonl', 'utf8').trim().split(/\r?\n/).map(JSON.parse); assert(observer.some(event => event.event === 'observer-end'));
    row.foregroundApps = [...new Set(observer.map(event => event.name).filter(Boolean))]; result.push(row);
  }
  return { contract: 'm1-knight-measure-v1', interpretation: 'Capture off, eligible foreground rows only. Replay identity and measured tick window are recorded separately in each row. Same-input warning variants cannot prove avoided damage; actual damage tape must match. Zero known-hit denominator is unavailable. Unity allocated memory is not RSS; recorder-negative values unavailable.', rows: result };
}
const [command, file, extra] = process.argv.slice(2);
if (command === 'plans') { assert(file && !extra); plans(file); }
else { const plan = read(file); assert.equal(plan.contract, 'm1-knight-measure-v1'); if (command === 'run') { assert(extra); await run(plan, extra); } else if (command === 'summary') { assert(!extra); console.log(JSON.stringify(summarize(plan), null, 2)); } else throw Error('Usage: m1-knight-measure.mjs plans NEW_ROOT | run MANIFEST APP | summary MANIFEST'); }
