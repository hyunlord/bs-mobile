#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn, execFileSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { parseCsv } from './csv.mjs';
import { validateRun, validatePcm, validateAudioBlocks, portraitZoomCrop } from './m1-knight-review.mjs';
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const rows = file => parseCsv(fs.readFileSync(file, 'utf8'));
const save = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const call = (binary, args) => execFileSync(binary, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const stages = ['level1', 'max', 'evolution'];
function fresh(directory) { assert(path.isAbsolute(directory) && !fs.existsSync(directory), 'Fresh absolute output required'); fs.mkdirSync(directory, { recursive: true }); }
export function makePlan(root, baked, drawn) {
  assert([root, baked, drawn].every(path.isAbsolute));
  const catalogPath = path.join(repo, 'data/review/m1-weapon-showcase.json'), catalog = read(catalogPath);
  assert.equal(catalog.entries.length, 8); assert.equal(new Set(catalog.entries.map(entry => entry.id)).size, 8);
  const plan = { contract: 'm1-production-review-v1', replay: path.join(repo, 'benchmarks/wave-c05/run.ssreplay'), replaySha256: 'f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5', terminalHash: 'B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8', width: 1920, height: 1080, crop: { x: 710, y: 0, width: 500, height: 1080 }, heroCoordinates: 'knight-rendered.csv', sheetRule: 'First recorded walk, slash-light, hit, death action respectively; same source frame indices across all methods; 12 frames at 5-frame intervals spanning one second. Missing actions are N/A, never inferred.', runs: [] };
  for (const [method, manifest, title] of [['cutout-baseline', null, '현재 컷아웃'], ['3d-baked', baked, '3D 제작 후 이미지 굽기'], ['frame-drawn', drawn, '프레임 그림']]) {
    if (manifest) assert.equal(read(manifest).method, method);
    plan.runs.push({ role: method, kind: 'hero', title, directory: path.join(root, 'captures', method), frames: 1800, method: manifest ? method : "a-knight-live", args: ['--knight-review', '--camera-scale', '1.4', '--camera-scale-window', 'early', '--m1-review-seconds', '30', ...(manifest ? ['--hero-production', manifest, '--hero-production-direction', 'follow'] : [])], expected: { 'knight-review.json': { contract: 'knight-impact-v1', hero: manifest ? method : 'a-knight-live', replayVerified: true, cameraScale: 1.4, offlineAudio: true, ...(manifest ? { productionManifestSha256: hash(manifest), productionDirection: 'follow' } : {}) } } });
  }
  for (const entry of catalog.entries) for (const stage of stages) plan.runs.push({ role: entry.id.replace(':', '-') + '-' + stage, kind: 'weapon', contentId: entry.id, stage, title: entry.name, limitation: entry.runtimeSupport, directory: path.join(root, 'captures', entry.id.replace(':', '-') + '-' + stage), frames: 600, args: ['--knight-review', '--camera-scale', '1.4', '--camera-scale-window', 'mid', '--m1-review-seconds', '10', '--m1-weapon-showcase', entry.id, '--showcase-stage', stage, '--m1-showcase-catalog', catalogPath], expected: { 'knight-review.json': { replayVerified: true, cameraScale: 1.4, offlineAudio: true }, 'm1-weapon-showcase.json': { contract: 'm1-weapon-showcase-v1', contentId: entry.id, stage, frames: 600, replayVerified: true, userVisualApproval: false, defaultContentChanged: false } } });
  return plan;
}
export function makeCombatPlan(root, casesDirectory) {
  assert(path.isAbsolute(root) && path.isAbsolute(casesDirectory));
  const catalog = read(path.join(repo, 'data/review/m1-weapon-showcase.json'));
  const files = fs.readdirSync(casesDirectory).filter(name => name.endsWith('.case.json')).sort();
  assert.equal(files.length, 24, 'Exactly eight shapes times three stages required');
  const runs = files.map(name => {
    const sidecar = path.join(casesDirectory, name), definition = read(sidecar), replay = sidecar.replace(/\.case\.json$/, '.ssreplay');
    assert.equal(definition.contract, 'm1-combat-case-v1'); assert.equal(definition.classification, 'hypothesis');
    assert.equal(definition.proposedContract, 'm1-review-composition-v1');
    assert.equal(definition.captureStartTick, 300); assert.equal(definition.endTick, 600);
    assert(['lv1', 'max', 'evolved'].includes(definition.stage));
    const entry = catalog.entries.find(entry => entry.id === definition.id); assert(entry, 'Unknown review content ID');
    assert.equal(hash(replay), definition.replaySha256, 'Case replay SHA mismatch');
    for (const key of ['stateHash', 'catalogHash']) assert(/^[A-F0-9]{64}$/.test(definition[key]), `Missing ${key}`);
    const role = definition.id.replace(':', '-') + '-' + definition.stage;
    return { role, kind: 'combat', contentId: definition.id, stage: definition.stage, title: entry.name, directory: path.join(root, 'captures', role), frames: 600, replay, replaySha256: definition.replaySha256, terminalHash: definition.stateHash, catalogHash: definition.catalogHash, sidecar, sidecarSha256: hash(sidecar), args: ['--m1-combat-case', sidecar] };
  });
  assert.equal(new Set(runs.map(run => run.role)).size, 24, 'Duplicate content/stage');
  assert.equal(new Set(runs.map(run => run.contentId)).size, 8);
  return { contract: 'm1-production-review-v1', purpose: 'isolated-combat-hypotheses', width: 1920, height: 1080, crop: { x: 710, y: 0, width: 500, height: 1080 }, runs };
}
export function validateCombatEvents(events, run, manifest, expected) {
  const hits = new Set(); let previous = -1, hitCount = 0, killCount = 0, firstHit = -1, firstDeath = -1;
  for (const e of events) {
    const id = Number(e.eventId), frame = Number(e.frameIndex), tick = Number(e.eventTick), snapshot = Number(e.snapshotTick);
    assert(Number.isSafeInteger(id) && id > previous, 'Combat events must be unique and ordered'); previous = id;
    assert(Number.isInteger(frame) && frame >= 0 && frame < run.frames);
    assert(snapshot === tick + 1 && tick >= 300 && snapshot <= 600, 'Event snapshot clock mismatch');
    assert(['True', 'False', 'true', 'false'].includes(e.inViewport));
    const visible = e.inViewport.toLowerCase() === 'true', key = `${tick}:${e.subjectId}`;
    if (['hit', 'group-hit', 'trained-group-hit'].includes(e.kind) && Number(e.amount) > 0) {
      assert.equal(e.source, run.contentId); hits.add(key); hitCount++;
      if (visible && firstHit < 0) firstHit = frame;
    } else if (e.kind === 'enemy-killed') {
      assert(hits.has(key), 'Kill has no selected-source hit on the same subject/tick'); killCount++;
      if (visible && firstDeath < 0) firstDeath = frame;
    } else assert.equal(e.source, run.contentId, 'Unattributed background event');
  }
  assert.equal(manifest.firstVisibleHitFrame, firstHit); assert.equal(manifest.firstVisibleDeathFrame, firstDeath);
  assert(expected && Number.isSafeInteger(expected.windowHits) && expected.windowHits >= 0 && Number.isSafeInteger(expected.windowKills) && expected.windowKills >= 0, 'Expected fixture window event counts required');
  assert.equal(hitCount, expected.windowHits, 'Native damage event count differs from fixture');
  assert.equal(killCount, expected.windowKills, 'Native death count differs from fixture');
  assert(hitCount > 0, 'Combat review requires authentic damage');
  if (run.stage === 'evolved') assert(killCount > 0, 'Evolved stage requires actual kills');
  return { hitCount, killCount, firstVisibleHitFrame: firstHit, firstVisibleDeathFrame: firstDeath };
}
function validateCombatRun(plan, run) {
  assert.equal(hash(run.replay), run.replaySha256); assert.equal(hash(run.sidecar), run.sidecarSha256);
  const b = read(path.join(run.directory, 'benchmark.json')), manifest = read(path.join(run.directory, 'm1-combat-review.json'));
  assert(b.verified && !b.error && !b.sourceDirty && b.performanceEligible === false, 'Clean verified offline native capture required');
  assert.equal(b.fixture, 'isolated-m1-combat-proposal'); assert.equal(b.replaySha256, run.replaySha256);
  assert.equal(b.stateHash, run.terminalHash); assert.equal(b.expectedStateHash, run.terminalHash); assert.equal(b.dataHash, run.catalogHash);
  assert.equal(b.width, plan.width); assert.equal(b.height, plan.height);
  assert.equal(manifest.contract, 'm1-combat-review-v1'); assert.equal(manifest.sidecarSha256, run.sidecarSha256);
  assert(manifest.replayVerified); assert.equal(manifest.frames, run.frames);
  const sidecar = read(run.sidecar);
  for (const key of ['id', 'stage', 'catalogHash', 'replaySha256', 'captureStartTick', 'endTick', 'classification', 'proposedContract', 'arena']) assert.deepEqual(manifest.definition[key], sidecar[key], `Combat definition ${key} mismatch`);
  for (const key of ['id', 'designRef', 'kind', 'damage', 'range', 'cooldownTicks', 'speed', 'count', 'lifetimeTicks', 'knockback', 'workTicks', 'workRadius', 'rewardExperience', 'capacity']) assert.deepEqual(manifest.definition.gear[key], sidecar.gear[key], `Combat gear ${key} mismatch`);
  assert.equal(manifest.definition.gear.aim || null, sidecar.gear.aim || null);
  if (sidecar.gear.composition) assert.deepEqual(manifest.definition.gear.composition, sidecar.gear.composition);
  const capture = read(path.join(run.directory, 'offline-capture.json')), audio = read(path.join(run.directory, 'offline-audio.json'));
  assert(capture.complete && capture.fps === 60 && capture.performanceEligible === false && capture.frames === run.frames && capture.expectedFrames === run.frames);
  assert(audio.complete && !audio.error && audio.performanceEligible === false && audio.videoFrames === run.frames && audio.expectedFrames === run.frames);
  const video = rows(path.join(run.directory, 'offline-frame-index.csv')), sound = rows(path.join(run.directory, 'offline-audio-index.csv'));
  assert.equal(video.length, run.frames); assert.equal(sound.length, run.frames);
  const pcm = validatePcm(path.join(run.directory, 'offline-audio.wav'), audio, run.frames); assert.equal(audio.sampleFrames, pcm.sampleFrames);
  pcm.nativeProvenance = validateAudioBlocks(run.directory, audio, sound);
  const tape = video.map((frame, index) => {
    assert.equal(Number(frame.index), index); assert.equal(Number(sound[index].videoFrame), index);
    for (const key of ['unityFrame', 'tick', 'alpha']) assert.equal(Number(sound[index][key]), Number(frame[key]));
    assert(Number(frame.tick) >= 300 && Number(frame.tick) <= 600);
    assert(Math.abs(Number(frame.seconds) - index / 60) < .00001);
    if (index) assert.equal(Number(frame.unityFrame), Number(video[index - 1].unityFrame) + 1);
    assert.equal(Number(sound[index].startSampleFrame), index * pcm.sampleRate / 60);
    assert.equal(Number(sound[index].sampleFrames), pcm.sampleRate / 60);
    assert.equal(Number(sound[index].sampleRate), pcm.sampleRate); assert.equal(Number(sound[index].channels), pcm.channels);
    assert(fs.statSync(path.join(run.directory, 'offline-frames', `frame-${String(index).padStart(6, '0')}.jpg`)).size > 0);
    return [frame.index, frame.tick, frame.alpha, frame.seconds].join(',');
  }).join('\n');
  assert.equal(fs.readdirSync(path.join(run.directory, 'offline-frames')).filter(name => /^frame-\d{6}\.jpg$/.test(name)).length, run.frames);
  const events = rows(path.join(run.directory, 'm1-combat-events.csv'));
  const evidence = validateCombatEvents(events, run, manifest, sidecar);
  for (const event of events) assert.equal(Number(event.snapshotTick), Number(video[Number(event.frameIndex)].tick), 'Event does not belong to its captured frame');
  return { role: run.role, commit: b.commit, sourceHash: b.sourceHash, replaySha256: b.replaySha256, dataHash: b.dataHash, stateHash: b.stateHash, tape, pcm, ...evidence };
}
export function validateProductionRun(plan, run) {
  if (run.kind === 'combat') return validateCombatRun(plan, run);
  const result = validateRun({ ...plan, frames: run.frames }, run);
  const hero = rows(path.join(run.directory, plan.heroCoordinates));
  if (run.kind === 'hero') validateHeroActions(hero, run);
  if (run.kind === 'weapon') {
    const frames = rows(path.join(run.directory, 'm1-weapon-showcase-frames.csv'));
    assert.equal(frames.length, run.frames);
    for (let i = 0; i < frames.length; i++) { assert.equal(Number(frames[i].frameIndex), i); assert.equal(frames[i].tick, hero[i].tick); assert.equal(frames[i].alpha, hero[i].alpha); }
  }
  return result;
}
export function validateHeroActions(hero, run) {
  assert(hero.every(row => row.method === run.method), `Native method mismatch: ${run.role}`);
  if (run.method === '3d-baked' || run.method === 'frame-drawn') {
    for (const action of ['walk', 'slash-light']) assert(hero.some(row => row.action === action), `${run.role}: missing actual rendered ${action}; production review cannot be delivered`);
  }
}
export function selectSheetWindows(methodRows) {
  const result = [];
  for (const action of ['walk', 'slash-light', 'hit', 'death']) {
    // Choose the first evidence-bearing occurrence across methods, then use that exact instant for all.
    const starts = methodRows.map(list => list.findIndex(row => row.action === action)).filter(index => index >= 0);
    const start = starts.length ? Math.min(...starts) : -1;
    result.push({ action, available: start >= 0 && methodRows.every(list => start + 55 < list.length), startFrame: start >= 0 ? start : null, frames: start >= 0 ? Array.from({ length: 12 }, (_, i) => start + i * 5) : [], limitation: 'Actual first action event; other methods may show different actions at the same replay time. Missing/late events are N/A.' });
  }
  return result;
}
function validateAll(plan, selected = plan.runs) {
  let source; const tapes = new Map(); const results = [];
  for (const run of selected) {
    const result = validateProductionRun(plan, run), id = JSON.stringify(run.kind === 'combat' ? [result.commit, result.sourceHash] : [result.commit, result.sourceHash, result.replaySha256, result.dataHash, result.stateHash]);
    if (source) assert.equal(id, source); else source = id;
    if (tapes.has(run.kind)) assert.equal(result.tape, tapes.get(run.kind), 'Comparison replay frame/tick/alpha differs'); else tapes.set(run.kind, result.tape);
    results.push({ ...result, tape: undefined, role: run.role });
  }
  return results;
}
async function runCaptures(plan, app, selection) {
  fs.accessSync(path.join(app, 'Contents/MacOS/Sow and Siege'), fs.constants.X_OK);
  if (plan.replay) assert.equal(hash(plan.replay), plan.replaySha256);
  try { assert(!call('pgrep', ['-f', '/Contents/MacOS/Sow and Siege']).trim(), 'Another game is running'); } catch (error) { if (error.status !== 1) throw error; }
  const selected = plan.runs.filter(run => !selection || run.kind === selection || run.role === selection); assert(selected.length, 'No matching run');
  for (const run of selected) {
    const replay = run.replay ?? plan.replay; assert.equal(hash(replay), run.replaySha256 ?? plan.replaySha256);
    if (fs.existsSync(run.directory)) { validateProductionRun(plan, run); console.log(`SKIP validated ${run.role}`); continue; }
    const log = run.directory + '.player.log'; assert(!fs.existsSync(log), 'Partial capture logs exist'); fs.mkdirSync(path.dirname(log), { recursive: true });
    const args = ['--wave-benchmark', replay, '--wave-benchmark-output', run.directory, ...run.args, '--offline-replay-capture', '--offline-replay-audio', '-screen-width', String(plan.width), '-screen-height', String(plan.height), '-logFile', log];
    save(run.directory + '.invocation.json', { app, args, utc: new Date().toISOString() }); console.log(`START ${run.role}`);
    await new Promise((resolve, reject) => { const child = spawn(path.join(app, 'Contents/MacOS/Sow and Siege'), args, { stdio: 'inherit' }); let kill;
      const timeout = setTimeout(() => { child.kill('SIGTERM'); kill = setTimeout(() => child.kill('SIGKILL'), 5000); }, 1800000);
      child.on('error', reject); child.on('close', (code, signal) => { clearTimeout(timeout); clearTimeout(kill); code === 0 && !signal ? resolve() : reject(Error(`Native capture failed: ${code}/${signal}`)); }); });
    validateProductionRun(plan, run); console.log(`END ${run.role}`);
  }
  validateAll(plan, selected);
}
function labelHelper(output) {
  const source = path.join(output, 'labels.swift');
  fs.writeFileSync(source, `import AppKit
import Foundation
let a = CommandLine.arguments
if a.count == 4 {
  let start = Int(a[1])!, step = Int(a[2])!
  let b = NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:1500,pixelsHigh:48,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
  NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep:b)
  NSColor(calibratedWhite:0.08,alpha:1).setFill(); NSRect(x:0,y:0,width:1500,height:48).fill()
  let attrs:[NSAttributedString.Key:Any]=[.font:NSFont.systemFont(ofSize:12),.foregroundColor:NSColor.white]
  for i in 0..<12 { let x=CGFloat(i*125+8); (String(format:"+%.1f ms",Double(i*step)*1000/60) as NSString).draw(at:NSPoint(x:x,y:27),withAttributes:attrs); ("frame " + String(start+i*step) as NSString).draw(at:NSPoint(x:x,y:8),withAttributes:attrs) }
  NSGraphicsContext.restoreGraphicsState(); try b.representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:a[3])); exit(0)
}
let bitmap = NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:500,pixelsHigh:100,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep:bitmap)
NSColor(calibratedWhite:0.08,alpha:1).setFill(); NSRect(x:0,y:0,width:500,height:100).fill()
for i in 1...3 { let text=a[i] as NSString; let attrs:[NSAttributedString.Key:Any]=[.font:NSFont.systemFont(ofSize:i==1 ? 21:14),.foregroundColor:NSColor.white]; text.draw(at:NSPoint(x:(500-text.size(withAttributes:attrs).width)/2,y:CGFloat(100-i*29)),withAttributes:attrs) }
NSGraphicsContext.restoreGraphicsState(); try bitmap.representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:a[4]))
`, { flag: 'wx' }); call('swiftc', [source, '-o', path.join(output, 'labels')]);
}
function encodeRun(plan, run, output, zoom) {
  const c = plan.crop, size = { width: c.width / zoom, height: c.height / zoom }; let crop = `crop=${c.width}:${c.height}:${c.x}:${c.y}`, clipped = 0;
  if (zoom === 2) {
    const samples = rows(path.join(run.directory, plan.heroCoordinates)).map(row => portraitZoomCrop(plan, row)); clipped = samples.filter(sample => sample.clamped).length;
    const commands = path.join(output, run.role + '-crop.commands'); assert(!/[':;\[\]\\]/.test(commands));
    fs.writeFileSync(commands, samples.map((sample, i) => `${(Math.floor(i * 1e6 / 60) / 1e6).toFixed(6)} crop@hero x ${sample.x}, crop@hero y ${sample.y};`).join('\n') + '\n', { flag: 'wx' });
    crop = `sendcmd=f='${commands}',crop@hero=${size.width}:${size.height}:0:0:exact=1`;
  }
  const label = path.join(output, `${run.role}-${zoom}x-label.png`), video = path.join(output, `${run.role}-${zoom}x.mp4`);
  call(path.join(output, 'labels'), [run.title, run.kind === 'combat' ? `${run.stage} · 실제 판정 · 검토용 수치 가설` : run.kind === 'weapon' ? `${run.stage} · 시각 제안 · 추가 판정 미구현` : '같은 입력 · 오프라인 60fps · 실제 게임 소리', clipped ? `2배: 전장 경계로 ${clipped}장 중심 이탈` : run.kind === 'combat' ? '단계 사전 구성 · 실제 소리 · 오프라인 60fps' : run.kind === 'weapon' ? '배경 처치는 시연 공격의 결과가 아님' : `${zoom}배 · 원본 프레임 보존`, label]);
  call('ffmpeg', ['-nostdin', '-v', 'error', '-framerate', '60', '-i', path.join(run.directory, 'offline-frames/frame-%06d.jpg'), '-i', path.join(run.directory, 'offline-audio.wav'), '-i', label, '-filter_complex', `[0:v]${crop},scale=500:1080:flags=lanczos,setsar=1,pad=500:1180:0:100:color=0x141414[g];[g][2:v]overlay=eof_action=repeat:shortest=0[v]`, '-map', '[v]', '-map', '1:a', ...videoOptions(run.frames), video]);
  return { role: run.role, zoom, clippedFrames: clipped, video };
}
function videoOptions(frames) { return ['-frames:v', String(frames), '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', '-fps_mode', 'passthrough', '-video_track_timescale', '60000', '-c:a', 'aac', '-b:a', '192k']; }
function verifyVideo(file, frames, width, tracks) {
  const probe = JSON.parse(call('ffprobe', ['-v', 'error', '-count_frames', '-show_streams', '-of', 'json', file]));
  const video = probe.streams.filter(s => s.codec_type === 'video'), audio = probe.streams.filter(s => s.codec_type === 'audio');
  assert.equal(video.length, 1); assert.equal(Number(video[0].nb_read_frames), frames); assert.equal(video[0].width, width); assert.equal(video[0].height, 1180); assert.equal(video[0].r_frame_rate, '60/1'); assert.equal(audio.length, tracks);
  for (const stream of [...video, ...audio]) assert(Math.abs(Number(stream.duration) - frames / 60) < .025, 'Encoded duration mismatch');
}
export function sourceHeadRatio(run, action) {
  const originalRatio = 53 / 441, at = run.args.indexOf('--hero-production');
  if (at < 0) return { originalRatio, ratio: null, basis: 'Baseline source landmarks not remeasured; native pixels unmeasured', label: `원화 ${originalRatio.toFixed(5)} · 현재 비율 미검증` };
  const file = run.args[at + 1], manifest = read(file);
  let ratio = null, basis;
  if (manifest.method === '3d-baked') { ratio = manifest.headHeightRatio; basis = '3D manifest headHeightRatio; source/model landmarks, not native pixel inspection'; }
  else if (manifest.method === 'frame-drawn') {
    const clip = manifest.clips.find(clip => clip.id === action && clip.direction === 'E');
    if (clip && Number.isFinite(clip.headHeightPixels) && Number.isFinite(clip.bodyHeightPixels) && clip.headHeightPixels > 0 && clip.bodyHeightPixels > 0) ratio = clip.headHeightPixels / clip.bodyHeightPixels;
    basis = `E ${action} source clip headHeightPixels/bodyHeightPixels; not native pixel inspection`;
  } else throw Error('Unknown production source ratio method');
  assert(ratio === null || Number.isFinite(ratio) && ratio > 0 && ratio < 1, 'Invalid source head/body ratio');
  return { originalRatio, ratio, basis, manifest: file, manifestSha256: hash(file), label: `원화 ${originalRatio.toFixed(5)} · 소스 ${ratio === null ? '미검증' : ratio.toFixed(5)} · 실화면 미측정` };
}
function assembleHeroes(plan, output, sheetsOnly = false) {
  const runs = plan.runs.filter(run => run.kind === 'hero'), proof = validateAll(plan, runs); assert.equal(runs.length, 3); fresh(output); labelHelper(output); const outputs = [];
  for (const zoom of sheetsOnly ? [] : [1, 2]) {
    const clips = runs.map(run => encodeRun(plan, run, output, zoom)); outputs.push(...clips);
    const file = path.join(output, `three-methods-${zoom}x.mp4`), inputs = [...clips.flatMap(clip => ['-i', clip.video]), ...runs.map(run => ['-i', path.join(run.directory, 'offline-audio.wav')]).flat()];
    const maps = runs.flatMap((run, index) => ['-map', `${index + 3}:a`, `-metadata:s:a:${index}`, `title=${run.title} original game audio`, `-disposition:a:${index}`, index === 0 ? 'default' : '0']);
    call('ffmpeg', ['-nostdin', '-v', 'error', ...inputs, '-filter_complex', '[0:v][1:v][2:v]hstack=inputs=3[v]', '-map', '[v]', ...maps, ...videoOptions(runs[0].frames), file]); verifyVideo(file, runs[0].frames, 1500, 3);
    for (const clip of clips) verifyVideo(clip.video, runs[0].frames, 500, 1);
  }
  const sheetWindows = selectSheetWindows(runs.map(run => rows(path.join(run.directory, plan.heroCoordinates))));
  for (const sheet of sheetWindows.filter(item => item.available)) {
    sheet.sourceHeadRatios = runs.map(run => ({ role: run.role, ...sourceHeadRatio(run, sheet.action) }));
    const inputs = runs.flatMap(run => ['-framerate', '60', '-i', path.join(run.directory, 'offline-frames/frame-%06d.jpg')]);
    const filters = runs.map((run, i) => `[${i}:v]select='${sheet.frames.map(frame => `eq(n,${frame})`).join('+')}',crop=500:1080:710:0,scale=125:270,tile=12x1[s${i}]`);
    runs.forEach((run, i) => {
      const label = path.join(output, `${sheet.action}-${run.role}-sheet-label.png`);
      call(path.join(output, 'labels'), [run.title, `${({walk:'걷기','slash-light':'휘두르기',hit:'피격',death:'쓰러짐'})[sheet.action]} · ${sheet.startFrame}장부터 12장 · 간격 5장`, sheet.sourceHeadRatios[i].label, label]);
      inputs.push('-i', label); filters.push(`[${i + 3}:v]pad=500:270:0:85:color=0x141414[l${i}];[l${i}][s${i}]hstack=inputs=2[r${i}]`);
    });
    call('ffmpeg', ['-nostdin', '-v', 'error', ...inputs, '-filter_complex', [...filters, '[r0][r1][r2]vstack=inputs=3[v]'].join(';'), '-map', '[v]', '-frames:v', '1', path.join(output, `${sheet.action}-12frames.png`)]);
  }
  save(path.join(output, 'production-review-provenance.json'), { contract: plan.contract, sheetsOnly, performanceEligible: false, sources: proof, outputs, sheetWindows, sheetOrder: runs.map(run => run.title), sheetTiming: '12 originals at 0,5,...55 frames after first recorded action; no new animation/interpolation. Missing death/hit means N/A.', ratioMeaning: 'Original A reference head/body 53/441; source/model landmark ratios, not native screen pixel measurements. 2D uses the E clip corresponding to each action. Baseline ratio is unverified.', limitations: 'Methods are authoring candidates, not approved default. 2x crop clamps to original portrait; sourceframes retained. Each comparison track retains one method original game mix, selectable independently.' });
}
export function selectWeaponRuns(plan, contentId) {
  const combat = plan.purpose === 'isolated-combat-hypotheses', order = combat ? ['lv1', 'max', 'evolved'] : stages;
  const runs = plan.runs.filter(run => run.kind === (combat ? 'combat' : 'weapon') && (!contentId || run.contentId === contentId));
  assert.equal(runs.length, contentId ? 3 : 24, 'Require all three stages for each selected weapon');
  const ids = [...new Set(runs.map(run => run.contentId))]; assert.equal(ids.length, contentId ? 1 : 8);
  for (const id of ids) assert.deepEqual(runs.filter(run => run.contentId === id).map(run => run.stage).sort(), [...order].sort(), `Missing or duplicate stage: ${id}`);
  return runs;
}
export function combatSheetTiming(events, event, start, totalFrames) {
  const kinds = event === 'hit' ? ['hit', 'group-hit', 'trained-group-hit'] : ['enemy-killed'];
  const source = events.find(e => Number(e.frameIndex) === start && kinds.includes(e.kind) && e.inViewport.toLowerCase() === 'true' && (event !== 'hit' || Number(e.amount) > 0));
  assert(start < 0 || source, 'Missing first visible event metadata');
  const eventTime = source ? { eventId: Number(source.eventId), subjectId: Number(source.subjectId), coreEventTick: Number(source.eventTick), coreEventSeconds: Number(source.eventTick) / 30, snapshotTick: Number(source.snapshotTick), firstEventRenderFrame: start } : null;
  return { eventTime, pixelVerified: false, interpretation: 'Event center entered viewport; does not prove the reaction was drawn on that frame. No inferred +2-frame offset. Contiguous strip preserves the following render frames so delayed flashes can be inspected.', strips: [5, 1].map(step => ({ step, available: start >= 0 && start + 11 * step < totalFrames, frames: start < 0 ? [] : Array.from({ length: 12 }, (_, i) => ({ index: start + i * step, elapsedRenderMs: i * step * 1000 / 60 })), spanMs: 11 * step * 1000 / 60 })) };
}
function assembleWeapons(plan, output, contentId) {
  const combat = plan.purpose === 'isolated-combat-hypotheses', order = combat ? ['lv1', 'max', 'evolved'] : stages;
  const runs = selectWeaponRuns(plan, contentId), proof = validateAll(plan, runs); fresh(output); labelHelper(output); const outputs = [];
  for (const id of [...new Set(runs.map(run => run.contentId))]) {
    const group = order.map(stage => runs.find(run => run.contentId === id && run.stage === stage)); const clips = group.map(run => encodeRun(plan, run, output, 1));
    const file = path.join(output, id.replace(':', '-') + '-three-stages.mp4');
    call('ffmpeg', ['-nostdin', '-v', 'error', ...clips.flatMap(clip => ['-i', clip.video]), ...group.flatMap(run => ['-i', path.join(run.directory, 'offline-audio.wav')]), '-filter_complex', '[0:v][3:a][1:v][4:a][2:v][5:a]concat=n=3:v=1:a=1[v][a]', '-map', '[v]', '-map', '[a]', ...videoOptions(1800), file]); verifyVideo(file, 1800, 500, 1);
    outputs.push({ contentId: id, file, stages: order, limitation: group[0].limitation });
  }
  const sheets = [];
  if (combat) for (const run of runs) {
    const evidence = proof.find(result => result.role === run.role);
    const events = rows(path.join(run.directory, 'm1-combat-events.csv'));
    for (const [event, start] of [['hit', evidence.firstVisibleHitFrame], ['death', evidence.firstVisibleDeathFrame]]) {
      const timing = combatSheetTiming(events, event, start, run.frames);
      const available = start >= 0 && start + 55 < run.frames, file = path.join(output, `${run.role}-${event}-12frames.png`);
      const record = { role: run.role, event, startFrame: start, available, file: available ? file : null, timing, subject: 'Enemy selected-source damage/death, not hero hit/death', rule: 'First viewport-visible event center, 12 originals at 5-frame intervals; readability requires user review.' }; sheets.push(record);
      const close = timing.strips[1];
      if (close.available) {
        const closeFile = path.join(output, `${run.role}-${event}-contiguous12.png`), label = path.join(output, `${run.role}-${event}-contiguous-label.png`), ticks = path.join(output, `${run.role}-${event}-render-times.png`);
        call(path.join(output, 'labels'), [run.title, `Core ${timing.eventTime.coreEventTick}틱 (${timing.eventTime.coreEventSeconds.toFixed(3)}초)`, '렌더 연속 12장 · 효과 표시 시점은 미검증', label]);
        call(path.join(output, 'labels'), [String(start), '1', ticks]);
        call('ffmpeg', ['-nostdin', '-v', 'error', '-framerate', '60', '-i', path.join(run.directory, 'offline-frames/frame-%06d.jpg'), '-i', label, '-i', ticks, '-filter_complex', `[0:v]select='${close.frames.map(f => `eq(n,${f.index})`).join('+')}',crop=500:1080:710:0,scale=125:270,tile=12x1[s];[2:v][s]vstack=inputs=2[t];[1:v]pad=500:318:0:100:color=0x141414[l];[l][t]hstack=inputs=2[v]`, '-map', '[v]', '-frames:v', '1', closeFile]);
        record.contiguousFile = closeFile;
      }
      if (!available) continue;
      const frames = Array.from({ length: 12 }, (_, i) => start + i * 5), label = path.join(output, `${run.role}-${event}-sheet-label.png`);
      call(path.join(output, 'labels'), [run.title, `${run.stage} · 적 ${event === 'hit' ? '피격' : '처치'} · 첫 사건 ${start}장`, '실제 12장 · 5프레임 간격 · 원본 보존', label]);
      call('ffmpeg', ['-nostdin', '-v', 'error', '-framerate', '60', '-i', path.join(run.directory, 'offline-frames/frame-%06d.jpg'), '-i', label, '-filter_complex', `[0:v]select='${frames.map(frame => `eq(n,${frame})`).join('+')}',crop=500:1080:710:0,scale=125:270,tile=12x1[s];[1:v]pad=500:270:0:85:color=0x141414[l];[l][s]hstack=inputs=2[v]`, '-map', '[v]', '-frames:v', '1', file]);
      if (record.contiguousFile) {
        record.pairedFile = path.join(output, `${run.role}-${event}-event-render-pair.png`);
        call('ffmpeg', ['-nostdin', '-v', 'error', '-i', file, '-i', record.contiguousFile, '-filter_complex', '[0:v][1:v]vstack=inputs=2[v]', '-map', '[v]', '-frames:v', '1', record.pairedFile]);
      }
    }
  }
  save(path.join(output, 'weapon-review-provenance.json'), { contract: plan.contract, performanceEligible: false, sources: proof, outputs, sheets, limitation: combat ? 'Actual selected-source Core damage and linked kills under isolated numeric hypotheses. Each stage is preconfigured, not earned level/evolution progression. No production balance approval. First event center inside viewport does not establish pixel readability.' : 'Ten seconds each of level1/max/evolution VISUAL proposals, not balance or collision implementation. Existing replay damage/deaths cannot be attributed to showcase overlays.' });
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const [command, file, first, second] = process.argv.slice(2);
  if (command === 'plans') { assert(first && second); const plan = makePlan(file, first, second); fresh(file); save(path.join(file, 'production-plan.json'), plan); }
  else if (command === 'combat-plans') { assert(first); const plan = makeCombatPlan(file, first); fresh(file); save(path.join(file, 'production-plan.json'), plan); }
  else { const plan = read(file); assert.equal(plan.contract, 'm1-production-review-v1');
    switch (command) {
      case 'run': assert(first); await runCaptures(plan, first, second); break;
      case 'validate': console.log(JSON.stringify(validateAll(plan, plan.runs.filter(run => !first || run.kind === first || run.role === first)), null, 2)); break;
      case 'heroes': assert(first); assembleHeroes(plan, first); break;
      case 'hero-sheets': assert(first); assembleHeroes(plan, first, true); break;
      case 'weapons': assert(first); assembleWeapons(plan, first, second); break;
      default: throw Error('Usage: m1-production-review.mjs plans NEWROOT BAKED_JSON DRAWN_JSON | combat-plans NEWROOT CASES_DIRECTORY | run PLAN APP [hero|weapon|combat|ROLE] | validate PLAN [hero|weapon|combat|ROLE] | heroes PLAN NEWVIDEO | hero-sheets PLAN NEWOUTPUT | weapons PLAN NEWVIDEO [contentId]');
    }
  }
}
