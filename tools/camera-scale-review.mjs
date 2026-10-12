#!/usr/bin/env node
// Native review orchestration; offline images never provide frame-cost evidence.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { spawn, execFileSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { parseCsv } from './csv.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const fixture = path.join(repo, 'benchmarks/wave-c05/run.ssreplay');
const replayHash = 'f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5';
const terminalHash = 'B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8';
const scales = ['1', '1.4', '1.8'];
const windows = { early: [0, 900], mid: [10800, 11700], dense: [13500, 14400] };
const label = (scale, window) => `camera-${scale}-${window}`;
const json = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const csv = file => parseCsv(fs.readFileSync(file, 'utf8'));
const save = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const quantiles = values => {
  assert(values.length && values.every(Number.isFinite), 'Missing or invalid metric observations');
  const sorted = [...values].sort((a, b) => a - b);
  return { samples: sorted.length, p50: sorted[Math.ceil(sorted.length * .5) - 1], p95: sorted[Math.ceil(sorted.length * .95) - 1], max: sorted.at(-1) };
};
function benchmark(directory, window, capture, scale) {
  const b = json(path.join(directory, 'benchmark.json'));
  assert(b.verified && !b.error && !b.sourceDirty, `Unverified or dirty source: ${directory}`);
  assert.equal(b.replaySha256, replayHash); assert.equal(b.stateHash, terminalHash);
  assert.equal(b.windows, windows[window].join('-'));
  assert.equal(b.width, 1920); assert.equal(b.height, 1080);
  const camera = json(path.join(directory, 'camera-scale.json'));
  assert.equal(camera.contract, 'camera-scale-frozen-c05-v1'); assert(Number.isFinite(camera.scale) && Math.abs(camera.scale - Number(scale)) <= 1e-6, 'Camera scale differs beyond float serialization tolerance');
  assert.equal(camera.window, window); assert.equal(camera.startTick, windows[window][0]); assert.equal(camera.endTick, windows[window][1]);
  assert(camera.replayVerified); assert.equal(camera.replaySha256, replayHash); assert.equal(camera.offline, capture);
  assert.equal(b.performanceEligible, !capture);
  if (!capture) assert(!b.developmentBuild && !b.profilerEnabled, 'Release capture-off measurements required');
  return b;
}
function identity(b) { return JSON.stringify([b.commit, b.sourceHash, b.replaySha256, b.dataHash, b.stateHash]); }
function newDirectory(directory) {
  assert(path.isAbsolute(directory), 'Absolute output directory required');
  assert(!fs.existsSync(directory), `Output exists: ${directory}`); fs.mkdirSync(directory, { recursive: true });
}
async function executeNative(app, root, scale, window, capture) {
  const name = label(scale, window), fd = fs.openSync(path.join(root, `${name}.stdout.log`), 'wx');
  const helper = path.join(root, 'mac-helper');
  const observerPath = path.join(root, 'observe-foreground');
  fs.accessSync(helper, fs.constants.X_OK); if (!capture) fs.accessSync(observerPath, fs.constants.X_OK);
  const args = ['--wave-benchmark', fixture, '--wave-benchmark-output', path.join(root, name), '--camera-scale', scale,
    '--camera-scale-window', window, '--hero-motion', 'live', '-screen-width', '1920', '-screen-height', '1080', '-logFile', path.join(root, `${name}.player.log`)];
  if (capture) args.push('--offline-replay-capture');
  console.log(`START ${name} ${new Date().toISOString()} ${capture ? 'OFFLINE' : 'CAPTURE OFF'}`);
  let observer, activation, child, timeout, activate;
  const processes = [], failures = [];
  const start = (binary, arguments_, allowedSignal = false) => {
    const process = spawn(binary, arguments_, { stdio: ['ignore', fd, fd] });
    const done = new Promise(resolve => {
      process.on('error', error => failures.push(error.message));
      process.on('close', (code, signal) => { if (code !== 0 && !(allowedSignal && signal === 'SIGTERM')) failures.push(`${binary}: ${code}/${signal}`); resolve(); });
    });
    processes.push({ process, done }); return process;
  };
  const stop = () => { failures.push('Interrupted or timed out'); for (const item of processes) item.process.kill('SIGTERM'); };
  process.once('SIGINT', stop); process.once('SIGTERM', stop);
  try {
    if (!capture) observer = start(observerPath, [path.join(root, `${name}.foreground.jsonl`), '1800'], true);
    child = start(path.join(app, 'Contents/MacOS/Sow and Siege'), args);
    const childDone = processes.at(-1).done;
    timeout = setTimeout(stop, 1800000);
    activate = setTimeout(() => { activation = start(helper, ['activate', String(child.pid)]); }, 3000);
    await childDone; clearTimeout(activate); observer?.kill('SIGTERM');
    await Promise.all(processes.map(item => item.done));
    assert(!failures.length, failures.join('; '));
  } finally {
    clearTimeout(timeout); clearTimeout(activate); activation?.kill('SIGTERM'); observer?.kill('SIGTERM');
    process.removeListener('SIGINT', stop); process.removeListener('SIGTERM', stop); fs.closeSync(fd);
  }
  benchmark(path.join(root, name), window, capture, scale);
  console.log(`END ${name} ${new Date().toISOString()}`);
}
async function run(app, root, capture, resume = false) {
  assert(path.isAbsolute(app), 'Absolute .app path required');
  fs.accessSync(path.join(app, 'Contents/MacOS/Sow and Siege'), fs.constants.X_OK);
  assert.equal(createHash('sha256').update(fs.readFileSync(fixture)).digest('hex'), replayHash);
  const existing = spawnSyncText('pgrep', ['-f', '/Contents/MacOS/Sow and Siege'], true);
  assert(!existing.trim(), 'Another game process is running; do not overlap GPU work');
  const completed = resume ? validateExistingCaptures(root) : new Set();
  if (!resume) { newDirectory(root); buildMacHelper(root); }
  else fs.accessSync(path.join(root, 'mac-helper'), fs.constants.X_OK);
  if (!capture) spawnSyncText('swiftc', [path.join(repo, 'tools/observe-foreground.swift'), '-o', path.join(root, 'observe-foreground')]);
  // Rotate scale order across windows to avoid always assigning one scale the cold run.
  for (const [index, window] of Object.keys(windows).entries())
    for (let i = 0; i < scales.length; i++) {
      const scale = scales[(i + index) % scales.length], name = label(scale, window);
      if (completed.has(name)) { console.log(`SKIP validated complete ${name}`); continue; }
      await executeNative(app, root, scale, window, capture);
    }
}
function buildMacHelper(directory) {
  const source = path.join(directory, 'mac-helper.swift');
  fs.writeFileSync(source, `import AppKit
import Foundation
let args = CommandLine.arguments
if args.count == 3 && args[1] == "activate" {
  guard let pid = Int32(args[2]), let app = NSRunningApplication(processIdentifier: pid) else { exit(2) }
  if !app.isActive && !app.activate() { exit(3) }
} else if args.count == 4 && args[1] == "label" {
  guard let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 500, pixelsHigh: 80, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0), let context = NSGraphicsContext(bitmapImageRep: bitmap) else { exit(4) }
  NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = context
  NSColor(calibratedRed: 0.09, green: 0.09, blue: 0.10, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: 500, height: 80).fill()
  let text = args[2] as NSString
  let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 24), .foregroundColor: NSColor.white]
  let size = text.size(withAttributes: attributes)
  text.draw(at: NSPoint(x: (500-size.width)/2, y: (80-size.height)/2), withAttributes: attributes)
  NSGraphicsContext.restoreGraphicsState()
  guard let png = bitmap.representation(using: .png, properties: [:]) else { exit(5) }
  try png.write(to: URL(fileURLWithPath: args[3]))
} else { exit(2) }
`, { flag: 'wx' });
  spawnSyncText('swiftc', [source, '-o', path.join(directory, 'mac-helper')]);
}
function spawnSyncText(binary, args, emptyExitOne = false) {
  try { return execFileSync(binary, args, { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 }); }
  catch (error) { if (emptyExitOne && error.status === 1) return ''; throw error; }
}
function validateCaptureFrames(directory) {
  const capture = json(path.join(directory, 'offline-capture.json'));
  assert(capture.complete && capture.performanceEligible === false && capture.fps === 60 && capture.frames === 1800 && capture.expectedFrames === 1800);
  const rows = csv(path.join(directory, 'offline-frame-index.csv'));
  assert.equal(rows.length, 1800);
  const tape = rows.map((row, index) => { assert.equal(Number(row.index), index); return [row.index, row.tick, row.alpha, row.seconds].join(','); }).join('\n');
  const files = fs.readdirSync(path.join(directory, 'offline-frames')).filter(file => /^frame-\d{6}\.jpg$/.test(file)).sort();
  assert.equal(files.length, 1800); files.forEach((file, i) => assert.equal(file, `frame-${String(i).padStart(6, '0')}.jpg`));
  return tape;
}
function validateExistingCaptures(root) {
  assert(path.isAbsolute(root) && fs.statSync(root).isDirectory(), 'Existing absolute capture root required');
  const completed = new Set(); let sourceIdentity;
  for (const window of Object.keys(windows)) {
    let reference;
    for (const scale of scales) {
      const name = label(scale, window), directory = path.join(root, name);
      if (!fs.existsSync(directory)) {
        assert(!fs.readdirSync(root).some(file => file.startsWith(name + '.')), `Partial run logs without capture directory: ${name}`);
        continue;
      }
      const b = benchmark(directory, window, true, scale), tape = validateCaptureFrames(directory);
      if (sourceIdentity) assert.equal(identity(b), sourceIdentity); else sourceIdentity = identity(b);
      if (reference) assert.equal(tape, reference, `Different tick/alpha/time: ${window}`); else reference = tape;
      completed.add(name);
    }
  }
  return completed;
}
function assemble(root, output) {
  const sources = []; let sourceIdentity;
  for (const window of Object.keys(windows)) {
    let reference;
    for (const scale of scales) {
      const directory = path.join(root, label(scale, window)), b = benchmark(directory, window, true, scale);
      if (sourceIdentity) assert.equal(identity(b), sourceIdentity); else sourceIdentity = identity(b);
      const tape = validateCaptureFrames(directory);
      if (reference) assert.equal(tape, reference, `Different tick/alpha/time: ${window}`); else reference = tape;
      sources.push({ scale, window, directory, commit: b.commit, sourceHash: b.sourceHash, replayHash: b.replaySha256, terminalHash: b.stateHash });
    }
  }
  newDirectory(output); buildMacHelper(output);
  for (const window of Object.keys(windows)) {
    const inputs = [], filters = [];
    scales.forEach((scale, i) => {
      inputs.push('-framerate', '60', '-i', path.join(root, label(scale, window), 'offline-frames/frame-%06d.jpg'));
      filters.push(`[${i}:v]crop=500:1080:710:0,setsar=1,setpts=PTS-STARTPTS,pad=500:1160:0:80:color=0x171719[p${i}]`);
    });
    scales.forEach((scale, i) => {
      const strip = path.join(output, `label-${scale}-${window}.png`);
      spawnSyncText(path.join(output, 'mac-helper'), ['label', `${scale}x  ${window} · 오프라인 60fps`, strip]);
      inputs.push('-framerate', '60', '-i', strip);
      filters.push(`[p${i}][${i + 3}:v]overlay=x=0:y=0:eof_action=repeat:shortest=0[v${i}]`);
    });
    filters.push('[v0][v1][v2]hstack=inputs=3,setpts=N/(60*TB)[v]');
    spawnSyncText('ffmpeg', ['-nostdin', '-v', 'error', ...inputs, '-filter_complex', filters.join(';'), '-map', '[v]', '-frames:v', '1800', '-an', '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', '-fps_mode', 'passthrough', '-video_track_timescale', '60000', path.join(output, `${window}.mp4`)]);
  }
  spawnSyncText('ffmpeg', ['-nostdin', '-v', 'error', ...Object.keys(windows).flatMap(window => ['-i', path.join(output, `${window}.mp4`)]), '-filter_complex', '[0:v][1:v][2:v]concat=n=3:v=1:a=0,setpts=N/(60*TB)[v]', '-map', '[v]', '-frames:v', '5400', '-an', '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', '-fps_mode', 'passthrough', '-video_track_timescale', '60000', path.join(output, 'camera-scale-comparison.mp4')]);
  const validations = [];
  for (const name of [...Object.keys(windows), 'camera-scale-comparison']) {
    const file = path.join(output, `${name}.mp4`), count = name === 'camera-scale-comparison' ? 5400 : 1800;
    const probe = JSON.parse(spawnSyncText('ffprobe', ['-v', 'error', '-count_frames', '-show_streams', '-of', 'json', file]));
    assert.equal(probe.streams.length, 1); const stream = probe.streams[0];
    assert.equal(Number(stream.nb_read_frames), count); assert.equal(stream.width, 1500); assert.equal(stream.height, 1160);
    assert.equal(stream.r_frame_rate, '60/1'); assert(Math.abs(Number(stream.duration) - count / 60) < .002);
    validations.push({ name, frames: count, width: stream.width, height: stream.height, duration: Number(stream.duration) });
  }
  save(path.join(output, 'video-provenance.json'), { performanceEligible: false, realTime: false, audio: 'none; camera-scale review only', crop: { x: 710, y: 0, width: 500, height: 1080 }, sources, validations });
}
function summarize(root) {
  const rows = []; let sourceIdentity;
  for (const window of Object.keys(windows)) for (const scale of scales) {
    const directory = path.join(root, label(scale, window)), b = benchmark(directory, window, false, scale);
    if (sourceIdentity) assert.equal(identity(b), sourceIdentity); else sourceIdentity = identity(b);
    const manifest = json(path.join(directory, 'camera-scale-ticks.json'));
    assert(manifest.eligible && manifest.capacityFailures === 0 && manifest.discontinuousTicks === 0);
    const ticks = csv(path.join(directory, 'camera-scale-ticks.csv')).filter(row => Number(row.foreground) === 1);
    const frames = csv(path.join(directory, 'frames.csv')).filter(row => Number(row.focused) === 1 && Number(row.partialBoundary) === 0 && Number(row.tick) >= windows[window][0] && Number(row.tick) <= windows[window][1]);
    assert(ticks.length && frames.length, 'No eligible observations');
    const sum = key => ticks.reduce((total, row) => total + Number(row[key]), 0);
    const denominator = sum('derivedContactEntries'), numerator = sum('recentEntrantContactEntries');
    assert(numerator <= denominator);
    const observation = fs.readFileSync(path.join(root, `${label(scale, window)}.foreground.jsonl`), 'utf8').trim().split(/\r?\n/).map(JSON.parse);
    assert(observation.some(row => row.event === 'observer-end'), 'Foreground observer incomplete');
    rows.push({ scale: Number(scale), window, commit: b.commit, sourceHash: b.sourceHash, replayHash, terminalHash,
      visibleEnemies: quantiles(ticks.map(row => Number(row.visibleEnemies))), totalEnemies: quantiles(ticks.map(row => Number(row.enemies))),
      derivedContactEntries: denominator, recentEntrantContactEntries: numerator, entrantContactPercent: denominator ? numerator / denominator * 100 : null,
      unknownHistoryContactEntries: sum('unknownHistoryContactEntries'), actualLordDamageEvents: sum('actualLordDamageEvents'), actualEntrantDamagePercent: null,
      frameMs: quantiles(frames.map(row => Number(row.wallMs))), over25Percent: frames.filter(row => Number(row.wallMs) > 25).length / frames.length * 100,
      foregroundApps: [...new Set(observation.map(row => row.name).filter(Boolean))] });
  }
  save(path.join(root, 'camera-scale-summary.json'), { contract: 'camera-scale-review-v1', interpretation: 'Capture-off native foreground intervals only. Contact entry is a distance-threshold proxy, not physical-body collision or attributed damage. Frozen C05 uses enemy attack Range as the contact threshold and zero lord radius. Known-history contacts are denominator; recent entrants are within one second of observed screen entry. Unknown history is separate. Zero denominator means unavailable. No replica-derived performance claims.', rows });
  console.log(JSON.stringify(rows, null, 2));
}
const [command, first, second, ...extra] = process.argv.slice(2);
assert(!extra.length, 'Too many arguments');
switch (command) {
  case 'run-capture': assert(first && second); await run(first, second, true); break;
  case 'resume-capture': assert(first && second); await run(first, second, true, true); break;
  case 'validate-capture': assert(first && !second); console.log([...validateExistingCaptures(first)].join('\n')); break;
  case 'run-performance': assert(first && second); await run(first, second, false); break;
  case 'assemble': assert(first && second); assemble(first, second); break;
  case 'summary': assert(first && !second); summarize(first); break;
  default: throw Error('Usage: camera-scale-review.mjs run-capture|run-performance APP NEW_ROOT | resume-capture APP EXISTING_ROOT | validate-capture EXISTING_ROOT | assemble CAPTURE_ROOT NEW_VIDEO_ROOT | summary PERFORMANCE_ROOT');
}
