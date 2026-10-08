import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

export const SAMPLE_RATE = 22050;
export const CUES = ['hit', 'kill', 'harvest', 'level', 'evolution', 'hurt', 'boss'];
const tau = Math.PI * 2;
const frequency = midi => 440 * 2 ** ((midi - 69) / 12);

function tone(out, at, duration, midi, gain, timbre = 'pluck') {
  const start = Math.round(at * SAMPLE_RATE), length = Math.round(duration * SAMPLE_RATE), hz = frequency(midi);
  for (let i = 0; i < length && start + i < out.length; i++) {
    const t = i / SAMPLE_RATE, phase = tau * hz * t;
    const attack = Math.min(1, t / .008), release = Math.min(1, (length - i - 1) / (SAMPLE_RATE * .04));
    const envelope = attack * release * Math.exp(-t * (timbre === 'flute' ? 1.5 : 7));
    let value;
    switch (timbre) {
      case 'bell': value = Math.sin(phase) + .35 * Math.sin(phase * 2.76) + .13 * Math.sin(phase * 5.4); break;
      case 'flute': value = Math.sin(phase + .025 * Math.sin(tau * 4 * t)) + .12 * Math.sin(phase * 2); break;
      case 'horn': value = Math.sin(phase) + .4 * Math.sin(phase * 2) + .2 * Math.sin(phase * 3); break;
      case 'pluck': value = Math.sin(phase) + .22 * Math.sin(phase * 2) + .1 * Math.sin(phase * 3); break;
      default: throw new Error(`Unknown timbre ${timbre}`);
    }
    out[start + i] += value * envelope * gain;
  }
}
function impact(out, duration, startHz, endHz, noiseGain) {
  let state = 97127;
  for (let i = 0; i < duration * SAMPLE_RATE && i < out.length; i++) {
    const t = i / SAMPLE_RATE, u = t / duration;
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    const noise = state / 2147483648 - 1;
    const phase = tau * (startHz * t + (endHz - startHz) * t * t / (2 * duration));
    out[i] += (Math.sin(phase) + noise * noiseGain) * Math.sin(Math.PI * u) * Math.exp(-u * 5);
  }
}
export function synthesize(name) {
  const durations = { hit: .12, kill: .24, harvest: .38, level: .8, evolution: 1.2, hurt: .32, boss: 1.6, 'frontier-loop': 16 };
  if (!Object.hasOwn(durations, name)) throw new Error(`Unknown audio ${name}`);
  const out = new Float64Array(Math.round(durations[name] * SAMPLE_RATE));
  switch (name) {
    case 'hit': impact(out, .12, 950, 160, .8); break;
    case 'kill': impact(out, .16, 210, 65, .22); tone(out, .045, .19, 74, .35, 'pluck'); break;
    case 'harvest': tone(out, 0, .22, 79, 1, 'bell'); tone(out, .12, .26, 86, .7, 'bell'); break;
    case 'level': [62, 65, 69, 74].forEach((n, i) => tone(out, i * .12, .44, n, .8, 'bell')); break;
    case 'evolution': [50, 57, 62, 69, 74, 81].forEach((n, i) => tone(out, i * .12, .6, n, .7, 'flute')); break;
    case 'hurt': impact(out, .3, 190, 38, .3); tone(out, 0, .27, 45, .5, 'horn'); break;
    case 'boss': [38, 39, 38].forEach((n, i) => tone(out, i * .36, .85, n, .8, 'horn')); break;
    case 'frontier-loop': {
      // Original eight-bar D-Dorian ostinato: Dm / C / G / Dm, repeated with an answering phrase.
      const roots = [50, 50, 48, 48, 43, 43, 50, 50];
      const melody = [74, 77, 76, 74, 72, 69, 72, 76, 74, 71, 69, 67, 69, 72, 76, 74];
      for (let bar = 0; bar < 8; bar++) {
        const at = bar * 2, root = roots[bar];
        tone(out, at, 1.9, root - 12, .4, 'flute');
        for (let beat = 0; beat < 4; beat++) tone(out, at + beat * .5, .45, root + [0, 7, 12, 7][beat], .23);
        tone(out, at + .25, .7, melody[bar * 2], .28, 'flute');
        tone(out, at + 1, .8, melody[bar * 2 + 1], .24, 'flute');
      }
      break;
    }
    default: throw new Error(`Unknown audio ${name}`);
  }
  let peak = 0;
  for (const value of out) peak = Math.max(peak, Math.abs(value));
  const limit = name === 'frontier-loop' ? .16 : .14;
  for (let i = 0; i < out.length; i++) out[i] = out[i] / peak * limit;
  out[0] = 0; out[out.length - 1] = 0;
  return out;
}
export function wave(samples) {
  const bytes = Buffer.alloc(44 + samples.length * 2);
  bytes.write('RIFF'); bytes.writeUInt32LE(bytes.length - 8, 4); bytes.write('WAVEfmt ', 8); bytes.writeUInt32LE(16, 16);
  bytes.writeUInt16LE(1, 20); bytes.writeUInt16LE(1, 22); bytes.writeUInt32LE(SAMPLE_RATE, 24);
  bytes.writeUInt32LE(SAMPLE_RATE * 2, 28); bytes.writeUInt16LE(2, 32); bytes.writeUInt16LE(16, 34); bytes.write('data', 36); bytes.writeUInt32LE(samples.length * 2, 40);
  for (let i = 0; i < samples.length; i++) bytes.writeInt16LE(Math.round(Math.max(-1, Math.min(1, samples[i])) * 32767), 44 + i * 2);
  return bytes;
}
function audioMeta(name) {
  const guid = createHash('sha256').update(`sowsiege-original-audio-v1/${name}`).digest('hex').slice(0, 32);
  return `fileFormatVersion: 2\nguid: ${guid}\nAudioImporter:\n  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 22050\n    compressionFormat: 0\n    quality: 1\n    conversionMode: 0\n    preloadAudioData: 1\n  platformSettingOverrides: {}\n  forceToMono: 1\n  normalize: 0\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 0\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n`;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const check = process.argv.includes('--check');
  const destination = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../unity/Assets/Resources/Audio');
  if (!check) fs.mkdirSync(destination, { recursive: true });
  for (const name of [...CUES, 'frontier-loop']) {
    const bytes = wave(synthesize(name)), file = path.join(destination, name + '.wav');
    if (check) {
      if (!fs.readFileSync(file).equals(bytes) || fs.readFileSync(file + '.meta', 'utf8') !== audioMeta(name)) throw new Error(`Stale audio ${name}`);
    } else { fs.writeFileSync(file, bytes); fs.writeFileSync(file + '.meta', audioMeta(name)); }
    console.log(`${name}: ${bytes.length} bytes ${createHash('sha256').update(bytes).digest('hex')}`);
  }
}
