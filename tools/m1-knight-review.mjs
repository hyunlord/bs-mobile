#!/usr/bin/env node
// Sample-locked native captures only. This tool never invents audio or game frames.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync, spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { parseCsv } from './csv.mjs';
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const csv = file => parseCsv(fs.readFileSync(file, 'utf8'));
const write = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const call = (binary, args) => execFileSync(binary, args, { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex');
export function loadPlan(file) {
  const plan = read(file);
  assert.equal(plan.contract, 'm1-knight-review-v1');
  assert(Array.isArray(plan.runs) && plan.runs.length === 2, 'One matched before/after pair per plan');
  assert.deepEqual(plan.runs.map(run => run.role), ['before', 'after']);
  assert(path.isAbsolute(plan.replay) && hash(plan.replay) === plan.replaySha256, 'Replay identity mismatch');
  assert(typeof plan.terminalHash === 'string' && /^[A-F0-9]{64}$/.test(plan.terminalHash));
  assert(Number.isInteger(plan.frames) && plan.frames > 0 && Number.isInteger(plan.width) && Number.isInteger(plan.height));
  assert(plan.runs.every(run => path.isAbsolute(run.directory) && Array.isArray(run.args) && run.args.every(value => typeof value === 'string')));
  assert.notEqual(plan.runs[0].directory, plan.runs[1].directory);
  const c = plan.crop;
  assert(c && [c.x, c.y, c.width, c.height].every(Number.isInteger));
  assert(c.x >= 0 && c.y >= 0 && c.width > 0 && c.height > 0 && c.x + c.width <= plan.width && c.y + c.height <= plan.height);
  assert(c.width % 4 === 0 && c.height % 4 === 0, 'Two-times crop must have integral even dimensions');
  assert(plan.heroCoordinates === 'knight-rendered.csv', 'Actual per-frame hero coordinate contract required');
  assert(plan.runs.every(run => run.expected && typeof run.expected === 'object'), 'Per-run recorded mode assertions required');
  return plan;
}
export function validatePcm(file, audio, frames) {
  const wav = fs.readFileSync(file);
  assert(wav.length >= 44 && wav.toString('ascii', 0, 4) === 'RIFF' && wav.toString('ascii', 8, 12) === 'WAVE');
  assert.equal(wav.toString('ascii', 12, 16), 'fmt '); assert.equal(wav.readUInt32LE(16), 16);
  assert.equal(wav.readUInt16LE(20), 1); assert.equal(wav.readUInt16LE(34), 16);
  assert.equal(wav.toString('ascii', 36, 40), 'data');
  const channels = wav.readUInt16LE(22), rate = wav.readUInt32LE(24), size = wav.readUInt32LE(40);
  assert(channels === 1 || channels === 2); assert(rate > 0 && rate % 60 === 0);
  assert.equal(audio.channels, channels); assert.equal(audio.sampleRate, rate);
  assert.equal(size, frames * (rate / 60) * channels * 2); assert.equal(wav.length, size + 44);
  assert.equal(wav.readUInt32LE(4), size + 36); assert.equal(wav.readUInt32LE(28), rate * channels * 2); assert.equal(wav.readUInt16LE(32), channels * 2);
  let nonzero = 0, sum = 0, peak = 0;
  for (let index = 44; index < wav.length; index += 2) { const sample = wav.readInt16LE(index); if (sample !== 0) nonzero++; peak = Math.max(peak, Math.abs(sample)); sum += sample * sample; }
  assert(nonzero > 0, 'Silent native audio rejected; no replacement soundtrack allowed');
  return { sampleRate: rate, channels, sampleFrames: size / channels / 2, nonzero, peak: peak / 32768, rms: Math.sqrt(sum / (size / 2)) / 32768, sha256: hash(file) };
}
export function validateAudioBlocks(directory, audio, sound) {
  assert.equal(audio.contract, 'unity-audiorenderer-offline-pcm16-v2');
  const perFrame = audio.sampleRate / 60, frameBytes = audio.channels * 2;
  assert.equal(audio.logicalSamplesPerFrame, perFrame); assert.equal(audio.pendingFrames, 0);
  assert(Number.isSafeInteger(audio.firstRenderUnityFrame) && audio.firstRenderUnityFrame >= 0);
  const blocks = csv(path.join(directory, 'offline-audio-blocks.csv'));
  assert(blocks.length > 0, 'Missing native audio block provenance');
  let rawFrames = 0;
  blocks.forEach((block, index) => {
    assert.equal(Number(block.renderFrame), index);
    assert.equal(Number(block.unityFrame), audio.firstRenderUnityFrame + index, 'Native render clock gap');
    assert.equal(Number(block.startSampleFrame), rawFrames, 'Native PCM block gap or overlap');
    const count = Number(block.sampleFrames); assert(Number.isSafeInteger(count) && count >= 0);
    assert.equal(Number(block.logicalEndSampleFrame), (index + 1) * perFrame);
    assert.equal(Number(block.channels), audio.channels); assert.equal(Number(block.sampleRate), audio.sampleRate);
    rawFrames += count;
  });
  assert.equal(audio.rawSampleFrames, rawFrames);
  const raw = fs.readFileSync(path.join(directory, 'offline-audio-raw.pcm'));
  assert.equal(raw.length, rawFrames * frameBytes, 'Native PCM byte count differs from block ledger');
  const start = audio.captureStartRawSampleFrame, end = audio.captureEndRawSampleFrame;
  assert(Number.isSafeInteger(start) && Number.isSafeInteger(end) && start >= 0 && end <= rawFrames && end > start);
  assert.equal(end - start, sound.length * perFrame);
  sound.forEach((frame, index) => {
    const rawStart = Number(frame.rawStartSampleFrame);
    assert.equal(rawStart, start + index * perFrame, 'Capture raw sample sequence gap');
    assert.equal(rawStart, (Number(frame.unityFrame) - audio.firstRenderUnityFrame) * perFrame, 'Captured mixer clock origin shifted');
  });
  const wav = fs.readFileSync(path.join(directory, 'offline-audio.wav'));
  assert(wav.subarray(44).equals(raw.subarray(start * frameBytes, end * frameBytes)), 'Captured PCM differs from original mixer slice: padding, loss or replacement');
  return { rawSha256: hash(path.join(directory, 'offline-audio-raw.pcm')), rawSampleFrames: rawFrames, captureStartRawSampleFrame: start, captureEndRawSampleFrame: end, firstRenderUnityFrame: audio.firstRenderUnityFrame, nativeBlocks: blocks.length };
}
export function validateRun(plan, run) {
  const b = read(path.join(run.directory, 'benchmark.json'));
  assert(b.verified && !b.error && !b.sourceDirty && b.performanceEligible === false, 'Clean verified offline native capture required');
  assert.equal(b.replaySha256, plan.replaySha256); assert.equal(b.stateHash, plan.terminalHash);
  assert.equal(b.width, plan.width); assert.equal(b.height, plan.height);
  for (const [name, expected] of Object.entries(run.expected)) {
    assert(!name.includes('/') && name.endsWith('.json'), 'Expected assertions must name run-local JSON');
    const actual = read(path.join(run.directory, name));
    for (const [key, value] of Object.entries(expected)) {
      if (typeof value === 'number') assert(Number.isFinite(actual[key]) && Math.abs(actual[key] - value) <= 1e-6, `${name}.${key} differs`);
      else assert.deepEqual(actual[key], value, `${name}.${key} differs`);
    }
  }
  const capture = read(path.join(run.directory, 'offline-capture.json')), audio = read(path.join(run.directory, 'offline-audio.json'));
  assert(capture.complete && capture.fps === 60 && capture.performanceEligible === false && capture.frames === plan.frames && capture.expectedFrames === plan.frames);
  assert.equal(audio.contract, 'unity-audiorenderer-offline-pcm16-v2');
  assert(audio.complete && !audio.error && audio.performanceEligible === false && audio.videoFrames === plan.frames && audio.expectedFrames === plan.frames);
  const video = csv(path.join(run.directory, 'offline-frame-index.csv')), sound = csv(path.join(run.directory, 'offline-audio-index.csv'));
  const hero = csv(path.join(run.directory, plan.heroCoordinates)); assert.equal(hero.length, plan.frames);
  assert.equal(video.length, plan.frames); assert.equal(sound.length, plan.frames);
  const pcm = validatePcm(path.join(run.directory, 'offline-audio.wav'), audio, plan.frames);
  assert.equal(audio.sampleFrames, pcm.sampleFrames);
  pcm.nativeProvenance = validateAudioBlocks(run.directory, audio, sound);
  const tape = video.map((frame, index) => {
    assert.equal(Number(frame.index), index); assert.equal(Number(sound[index].videoFrame), index);
    assert.equal(Number(hero[index].frameIndex), index); assert.equal(Number(hero[index].tick), Number(frame.tick)); assert.equal(Number(hero[index].alpha), Number(frame.alpha));
    assert(Number.isFinite(Number(hero[index].heroX)) && Number.isFinite(Number(hero[index].heroY)), 'Missing hero position');
    for (const key of ['unityFrame', 'tick', 'alpha']) assert.equal(Number(sound[index][key]), Number(frame[key]), `Audio/video ${key} mismatch`);
    assert.equal(Number(sound[index].startSampleFrame), index * pcm.sampleRate / 60);
    assert.equal(Number(sound[index].sampleFrames), pcm.sampleRate / 60);
    assert.equal(Number(sound[index].sampleRate), pcm.sampleRate); assert.equal(Number(sound[index].channels), pcm.channels);
    assert(Math.abs(Number(frame.seconds) - index / 60) < .00001, 'Nonuniform presentation time');
    if (index) assert.equal(Number(frame.unityFrame), Number(video[index - 1].unityFrame) + 1, 'Skipped native rendered frame');
    assert(fs.statSync(path.join(run.directory, 'offline-frames', `frame-${String(index).padStart(6, '0')}.jpg`)).size > 0);
    return [frame.index, frame.tick, frame.alpha, frame.seconds].join(',');
  }).join('\n');
  assert.equal(fs.readdirSync(path.join(run.directory, 'offline-frames')).filter(name => /^frame-\d{6}\.jpg$/.test(name)).length, plan.frames);
  return { role: run.role, commit: b.commit, sourceHash: b.sourceHash, replaySha256: b.replaySha256, dataHash: b.dataHash, stateHash: b.stateHash, tape, pcm };
}
export function validatePair(plan) {
  const results = plan.runs.map(run => validateRun(plan, run));
  for (const key of ['commit', 'sourceHash', 'replaySha256', 'dataHash', 'stateHash', 'tape']) assert.equal(results[0][key], results[1][key], `Before/after ${key} differs`);
  assert.equal(results[0].pcm.sampleRate, results[1].pcm.sampleRate); assert.equal(results[0].pcm.channels, results[1].pcm.channels);
  return results;
}
function buildMacHelper(directory) {
  const source = path.join(directory, 'mac-helper.swift');
  fs.writeFileSync(source, `import AppKit
import Foundation
let args = CommandLine.arguments
if args.count == 3 && args[1] == "activate" {
  guard let pid = Int32(args[2]), let app = NSRunningApplication(processIdentifier: pid) else { exit(2) }
  if !app.isActive && !app.activate() { exit(3) }
} else if args.count == 5 && args[1] == "label" {
  guard let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 500, pixelsHigh: 100, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0), let context = NSGraphicsContext(bitmapImageRep: bitmap) else { exit(4) }
  NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = context
  NSColor(calibratedRed: 0.09, green: 0.09, blue: 0.10, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: 500, height: 100).fill()
  let text = args[2] as NSString
  let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 23), .foregroundColor: NSColor.white]
  let size = text.size(withAttributes: attributes)
  text.draw(at: NSPoint(x: (500-size.width)/2, y: 71), withAttributes: attributes)
  let caption = "오프라인 고정 60fps · 실제 게임 소리" as NSString
  let captionAttributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 18), .foregroundColor: NSColor.lightGray]
  caption.draw(at: NSPoint(x: (500-caption.size(withAttributes: captionAttributes).width)/2, y: 45), withAttributes: captionAttributes)
  let limitation = args[4] as NSString
  let limitationAttributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 14), .foregroundColor: NSColor.lightGray]
  limitation.draw(at: NSPoint(x: (500-limitation.size(withAttributes: limitationAttributes).width)/2, y: 15), withAttributes: limitationAttributes)
  NSGraphicsContext.restoreGraphicsState()
  guard let png = bitmap.representation(using: .png, properties: [:]) else { exit(5) }
  try png.write(to: URL(fileURLWithPath: args[3]))
} else { exit(2) }
`, { flag: 'wx' });
  call('swiftc', [source, '-o', path.join(directory, 'mac-helper')]);
}
export function portraitZoomCrop(plan, row) {
  const c = plan.crop, width = c.width / 2, height = c.height / 2;
  const heroX = Number(row.heroX), heroY = plan.height - Number(row.heroY);
  assert(Number.isFinite(heroX) && Number.isFinite(heroY), 'Missing observed hero coordinate');
  const requestedX = Math.round(heroX - width / 2), requestedY = Math.round(heroY - height / 2);
  const x = Math.max(c.x, Math.min(c.x + c.width - width, requestedX));
  const y = Math.max(c.y, Math.min(c.y + c.height - height, requestedY));
  const clamped = x !== requestedX || y !== requestedY;
  return { x, y, width, height, clamped, offCenterPixels: Math.hypot(x + width / 2 - heroX, y + height / 2 - heroY), heroOutsidePortrait: heroX < c.x || heroX > c.x + c.width || heroY < c.y || heroY > c.y + c.height };
}
function encode(plan, output) {
  const provenance = validatePair(plan);
  assert(path.isAbsolute(output) && !fs.existsSync(output), 'Fresh absolute output directory required'); fs.mkdirSync(output, { recursive: true });
  const encodeOptions = ['-frames:v', String(plan.frames), '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', '-fps_mode', 'passthrough', '-video_track_timescale', '60000', '-c:a', 'aac', '-b:a', '192k'];
  const c = plan.crop; assert.equal(c.width, 500, 'Caption contract uses 500px portrait width'); buildMacHelper(output);
  const cropProvenance = [];
  for (const zoom of [1, 2]) {
    const w = c.width / zoom, h = c.height / zoom;
    for (const run of plan.runs) {
      let crop = `crop=${w}:${h}:${c.x}:${c.y}`;
      let limitation = '원본 세로 전장 전체 · 캡처 원본 보존';
      if (zoom === 2) {
        const positions = csv(path.join(run.directory, plan.heroCoordinates));
        const cropSamples = positions.map(row => portraitZoomCrop(plan, row));
        const clampedFrames = cropSamples.filter(sample => sample.clamped).length;
        cropProvenance.push({ role: run.role, frames: cropSamples.length, requestedCropClippedFrames: clampedFrames, offCenterFrames: clampedFrames, heroOutsidePortraitFrames: cropSamples.filter(sample => sample.heroOutsidePortrait).length, maximumOffCenterPixels: Math.max(...cropSamples.map(sample => sample.offCenterPixels)), sourceFrames: path.join(run.directory, 'offline-frames'), sourcePreserved: true, boundary: c });
        limitation = clampedFrames ? `2배 · 세로 전장 경계 제한: ${clampedFrames}장 중심 이탈` : '2배 · 실제 영주 위치 중심 · 원본 보존';
        const commands = cropSamples.map(({ x, y }, i) => {
          return `${(Math.floor(i * 1000000 / 60) / 1000000).toFixed(6)} crop@hero x ${x}, crop@hero y ${y};`;
        });
        const commandFile = path.join(output, run.role + '-crop.commands'); fs.writeFileSync(commandFile, commands.join('\n') + '\n', { flag: 'wx' });
        assert(!/[':;\[\]\\]/.test(commandFile), 'Filter command path contains unsupported delimiters');
        crop = `sendcmd=f='${commandFile}',crop@hero=${w}:${h}:0:0:exact=1`;
      }
      const labelFile = path.join(output, `${run.role}-${zoom}x-label.png`);
      call(path.join(output, 'mac-helper'), ['label', run.role === 'before' ? '적 타격 반응 적용 전' : '적 타격 반응 적용 후', labelFile, limitation]);
      call('ffmpeg', ['-nostdin', '-v', 'error', '-framerate', '60', '-i', path.join(run.directory, 'offline-frames/frame-%06d.jpg'), '-i', path.join(run.directory, 'offline-audio.wav'), '-framerate', '60', '-i', labelFile, '-filter_complex', `[0:v]${crop},scale=${c.width}:${c.height}:flags=lanczos,setsar=1,setpts=N/(60*TB),pad=${c.width}:${c.height + 100}:0:100:color=0x171719[game];[game][2:v]overlay=x=0:y=0:eof_action=repeat:shortest=0[v]`, '-map', '[v]', '-map', '1:a', ...encodeOptions, path.join(output, `${run.role}-${zoom}x.mp4`)]);
    }
    // Two original tracks remain separately selectable; never overlay simultaneous before/after sounds.
    call('ffmpeg', ['-nostdin', '-v', 'error', '-i', path.join(output, `before-${zoom}x.mp4`), '-i', path.join(output, `after-${zoom}x.mp4`), '-filter_complex', '[0:v][1:v]hstack=inputs=2[v]', '-i', path.join(plan.runs[0].directory, 'offline-audio.wav'), '-i', path.join(plan.runs[1].directory, 'offline-audio.wav'), '-map', '[v]', '-map', '2:a', '-map', '3:a', ...encodeOptions, '-metadata:s:a:0', 'title=Before - original Unity mixer', '-metadata:s:a:1', 'title=After - original Unity mixer', '-disposition:a:0', '0', '-disposition:a:1', 'default', path.join(output, `before-after-${zoom}x.mp4`)]);
  }
  const encoded = [];
  for (const name of ['before-1x', 'after-1x', 'before-after-1x', 'before-2x', 'after-2x', 'before-after-2x']) {
    const file = path.join(output, name + '.mp4');
    const probe = JSON.parse(call('ffprobe', ['-v', 'error', '-count_frames', '-show_streams', '-of', 'json', file]));
    const video = probe.streams.filter(s => s.codec_type === 'video'), audio = probe.streams.filter(s => s.codec_type === 'audio');
    assert.equal(video.length, 1); assert.equal(Number(video[0].nb_read_frames), plan.frames); assert.equal(video[0].r_frame_rate, '60/1');
    assert.equal(video[0].width, c.width * (name.startsWith('before-after') ? 2 : 1)); assert.equal(video[0].height, c.height + 100);
    assert.equal(audio.length, name.startsWith('before-after') ? 2 : 1);
    for (const stream of [...video, ...audio]) assert(Math.abs(Number(stream.duration) - plan.frames / 60) < .025, 'Encoded duration mismatch');
    encoded.push({ name, sha256: hash(file), frames: plan.frames, durationSeconds: Number(video[0].duration), audioTracks: audio.length });
  }
  write(path.join(output, 'knight-review-provenance.json'), { label: '오프라인 고정 60fps · Unity 실제 소리', performanceEligible: false, width: c.width, height: c.height + 100, frames: plan.frames, crop: c, heroCoordinates: plan.heroCoordinates, zoomCrop: { policy: '2x viewport is clamped strictly inside the original portrait gameplay rectangle. Hero can be off-center or already outside the native view. No fabricated pixels, camera changes, or removal of original frames; 1x preserves the complete native portrait view.', runs: cropProvenance }, audioPolicy: 'Individual original tracks; pair default plays AFTER, first selectable track plays BEFORE. No generated, silent-padded, mixed or replaced audio.', sources: provenance.map(({ tape, ...result }) => result), encoded });
}
async function runNative(plan, app) {
  assert(path.isAbsolute(app)); const binary = path.join(app, 'Contents/MacOS/Sow and Siege'); fs.accessSync(binary, fs.constants.X_OK);
  for (const run of plan.runs) {
    assert(!fs.existsSync(run.directory), 'Refusing existing/partial native output');
    assert(!run.args.includes('--wave-benchmark') && !run.args.includes('--wave-benchmark-output'), 'Replay/output are owned by manifest');
    assert(!run.args.includes('--offline-replay-capture') && !run.args.includes('--offline-replay-audio'), 'Capture flags are owned by runner');
    const log = run.directory + '.player.log'; assert(!fs.existsSync(log), 'Refusing existing run log'); fs.mkdirSync(path.dirname(log), { recursive: true });
    const args = ['--wave-benchmark', plan.replay, '--wave-benchmark-output', run.directory, ...run.args, '--offline-replay-capture', '--offline-replay-audio', '-screen-width', String(plan.width), '-screen-height', String(plan.height), '-logFile', log];
    write(run.directory + '.invocation.json', { binary, args, utc: new Date().toISOString() });
    await new Promise((resolve, reject) => {
      const child = spawn(binary, args, { stdio: 'inherit' });
      let killTimer; const timeout = setTimeout(() => { child.kill('SIGTERM'); killTimer = setTimeout(() => child.kill('SIGKILL'), 5000); }, 1800000);
      child.on('error', reject); child.on('close', (code, signal) => { clearTimeout(timeout); clearTimeout(killTimer); if (code === 0 && !signal) resolve(); else reject(Error(`Native run failed ${code}/${signal}`)); });
    });
    validateRun(plan, run);
  }
  validatePair(plan);
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const [command, manifest, destination, ...extra] = process.argv.slice(2); assert(!extra.length);
  const plan = loadPlan(manifest);
  switch (command) {
    case 'run': assert(destination); await runNative(plan, destination); break;
    case 'validate': assert(!destination); console.log(JSON.stringify(validatePair(plan).map(({ tape, ...result }) => result), null, 2)); break;
    case 'assemble': assert(destination); encode(plan, destination); break;
    default: throw Error('Usage: m1-knight-review.mjs run MANIFEST APP | validate MANIFEST | assemble MANIFEST NEW_OUTPUT');
  }
}
