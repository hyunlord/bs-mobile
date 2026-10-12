import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { loadPlan, validatePair, validatePcm } from './m1-knight-review.mjs';
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'knight-review-contract-'));
const save = (file, value) => fs.writeFileSync(file, JSON.stringify(value));
const replay = path.join(root, 'test.ssreplay'); fs.writeFileSync(replay, 'test fixture, never game evidence');
const plan = { contract: 'm1-knight-review-v1', replay, replaySha256: createHash('sha256').update(fs.readFileSync(replay)).digest('hex'), terminalHash: 'A'.repeat(64), width: 1920, height: 1080, frames: 2, crop: { x: 710, y: 0, width: 500, height: 1080 }, heroCoordinates: 'knight-rendered.csv', runs: ['before', 'after'].map(role => ({ role, directory: path.join(root, role), args: [], expected: { 'knight-review.json': { contract: 'knight-impact-v1', hero: 'a-knight-live', impactFeedback: role === 'after', cameraScale: 1.4, replayVerified: true } } })) };
function wave() {
  const samples = 1600, bytes = samples * 2, b = Buffer.alloc(bytes + 44);
  b.write('RIFF'); b.writeUInt32LE(bytes + 36, 4); b.write('WAVEfmt ', 8); b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(1, 22); b.writeUInt32LE(48000, 24); b.writeUInt32LE(96000, 28); b.writeUInt16LE(2, 32); b.writeUInt16LE(16, 34); b.write('data', 36); b.writeUInt32LE(bytes, 40); b.writeInt16LE(123, 44); return b;
}
try {
  for (const run of plan.runs) {
    fs.mkdirSync(path.join(run.directory, 'offline-frames'), { recursive: true });
    save(path.join(run.directory, 'benchmark.json'), { verified: true, error: '', sourceDirty: false, performanceEligible: false, replaySha256: plan.replaySha256, stateHash: plan.terminalHash, width: 1920, height: 1080, commit: 'test', sourceHash: 'test', dataHash: 'test' });
    save(path.join(run.directory, 'knight-review.json'), { ...run.expected['knight-review.json'], cameraScale: Math.fround(1.4) });
    save(path.join(run.directory, 'offline-capture.json'), { complete: true, fps: 60, performanceEligible: false, frames: 2, expectedFrames: 2 });
    save(path.join(run.directory, 'offline-audio.json'), { contract: 'unity-audiorenderer-offline-pcm16-v2', complete: true, performanceEligible: false, videoFrames: 2, expectedFrames: 2, sampleFrames: 1600, sampleRate: 48000, channels: 1, logicalSamplesPerFrame: 800, rawSampleFrames: 3072, captureStartRawSampleFrame: 800, captureEndRawSampleFrame: 2400, firstRenderUnityFrame: 99, pendingFrames: 0 });
    fs.writeFileSync(path.join(run.directory, 'offline-frame-index.csv'), 'index,unityFrame,tick,alpha,seconds\n0,100,0,0,0\n1,101,0,0.5,0.016666666666666666\n');
    fs.writeFileSync(path.join(run.directory, 'offline-audio-index.csv'), 'videoFrame,unityFrame,tick,alpha,startSampleFrame,sampleFrames,sampleRate,channels,rawStartSampleFrame\n0,100,0,0,0,800,48000,1,800\n1,101,0,0.5,800,800,48000,1,1600\n');
    fs.writeFileSync(path.join(run.directory, 'knight-rendered.csv'), 'frameIndex,tick,alpha,heroX,heroY\n0,0,0,960,540\n1,0,0.5,961,540\n');
    fs.writeFileSync(path.join(run.directory, 'offline-audio.wav'), wave());
    const raw = Buffer.alloc(3072 * 2); wave().subarray(44).copy(raw, 800 * 2); fs.writeFileSync(path.join(run.directory, 'offline-audio-raw.pcm'), raw);
    fs.writeFileSync(path.join(run.directory, 'offline-audio-blocks.csv'), 'renderFrame,unityFrame,startSampleFrame,sampleFrames,logicalEndSampleFrame,channels,sampleRate\n0,99,0,1024,800,1,48000\n1,100,1024,1024,1600,1,48000\n2,101,2048,1024,2400,1,48000\n');
    for (let i = 0; i < 2; i++) fs.writeFileSync(path.join(run.directory, 'offline-frames', `frame-${String(i).padStart(6, '0')}.jpg`), 'contract fixture only');
  }
  const file = path.join(root, 'plan.json'); save(file, plan); const parsed = loadPlan(file);
  assert.equal(validatePair(parsed).length, 2);
  const wav = path.join(plan.runs[0].directory, 'offline-audio.wav'); fs.writeFileSync(wav, Buffer.from(wave()).fill(0, 44));
  assert.throws(() => validatePcm(wav, { sampleRate: 48000, channels: 1 }, 2), /Silent/); fs.writeFileSync(wav, wave());
  const index = path.join(plan.runs[1].directory, 'offline-audio-index.csv'), original = fs.readFileSync(index, 'utf8');
  fs.writeFileSync(index, original.replace('1,101,0,0.5,800', '1,101,0,0.5,799')); assert.throws(() => validatePair(parsed)); fs.writeFileSync(index, original);
  const rawFile = path.join(plan.runs[1].directory, 'offline-audio-raw.pcm'), rawOriginal = fs.readFileSync(rawFile);
  const altered = Buffer.from(rawOriginal); altered[1600] ^= 1; fs.writeFileSync(rawFile, altered); assert.throws(() => validatePair(parsed), /original mixer slice/); fs.writeFileSync(rawFile, rawOriginal);
  const blocksFile = path.join(plan.runs[1].directory, 'offline-audio-blocks.csv'), blocksOriginal = fs.readFileSync(blocksFile, 'utf8');
  fs.writeFileSync(blocksFile, 'renderFrame,unityFrame,startSampleFrame,sampleFrames,logicalEndSampleFrame,channels,sampleRate\n0,99,0,1024,800,1,48000\n1,100,1024,0,1600,1,48000\n2,101,1024,1024,2400,1,48000\n3,102,2048,1024,3200,1,48000\n');
  assert.equal(validatePair(parsed).length, 2); fs.writeFileSync(blocksFile, blocksOriginal);
  fs.writeFileSync(blocksFile, blocksOriginal.replace('1,100,1024,1024', '1,100,1023,1024')); assert.throws(() => validatePair(parsed), /block gap/); fs.writeFileSync(blocksFile, blocksOriginal);
  const manifest = path.join(plan.runs[1].directory, 'knight-review.json'); save(manifest, { ...plan.runs[1].expected['knight-review.json'], impactFeedback: false });
  assert.throws(() => validatePair(parsed), /impactFeedback/);
  console.log('PASS native audio contract: matched pair, float tolerance, silence rejected, sample-boundary mismatch, raw PCM replacement and block gap rejected; incorrect impact mode rejected');
} finally { fs.rmSync(root, { recursive: true }); }
