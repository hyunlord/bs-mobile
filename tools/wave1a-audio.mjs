import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { SAMPLE_RATE, wave } from './first-playable-audio.mjs';

// Original deterministic physical-material sketches: no recordings or sampled third-party audio.
const tau = Math.PI * 2;
const layers = {
  slash: [['noise', 0, .16, 2400, 600, .8], ['metal', .035, .12, 850, 440, .3]],
  tick: [['metal', 0, .12, 1800, 1500, .7]],
  spark: [['noise', 0, .025, 9000, 4000, 1], ['metal', .045, .09, 1400, 700, .45]],
  fire: [['noise', 0, .11, 700, 1800, .8], ['noise', .12, .018, 5000, 2000, .5], ['noise', .16, .014, 4000, 1500, .3]],
  scythe: [['noise', 0, .3, 1700, 500, 1], ['metal', .07, .24, 430, 280, .22]],
  seeds: [['wood', 0, .06, 850, 650, .5], ['wood', .045, .06, 1200, 900, .6], ['wood', .11, .055, 700, 550, .45]],
  splash: [['noise', 0, .18, 1800, 450, .6], ['water', .045, .12, 550, 1200, .4], ['water', .12, .09, 900, 1700, .3]],
  hammer: [['wood', 0, .13, 240, 130, 1], ['metal', .012, .09, 780, 650, .2]],
  horn: [['horn', 0, .32, 220, 220, 1]],
  grain: [['noise', 0, .18, 2700, 1300, .5], ['wood', .045, .065, 850, 700, .4], ['bell', .1, .32, 880, 880, .5]],
  irrigation: [['water', 0, .14, 480, 1100, .5], ['bell', .1, .28, 1046, 1046, .6], ['water', .2, .15, 850, 1700, .3]],
  shipment: [['wood', 0, .13, 180, 130, .8], ['wood', .12, .12, 300, 230, .6], ['bell', .2, .32, 659, 659, .5]],
  return: [['horn', 0, .23, 330, 330, .7], ['horn', .26, .32, 440, 440, .55]],
  scrape: [['noise', 0, .23, 800, 1300, .5], ['wood', .04, .12, 130, 90, .3]],
  scratch: [['noise', 0, .045, 4200, 2500, .8], ['noise', .08, .04, 3500, 2400, .6], ['wood', .14, .05, 1200, 700, .3]],
  snort: [['noise', 0, .22, 350, 100, .8], ['horn', 0, .18, 90, 65, .3]],
  hoof: [['wood', 0, .13, 160, 80, .8], ['wood', .17, .13, 160, 80, .9], ['noise', .24, .17, 800, 1600, .5]],
  shield: [['metal', 0, .32, 530, 430, .9], ['wood', 0, .09, 120, 80, .3]],
  wasp: [['buzz', 0, .3, 180, 240, .4], ['noise', .25, .06, 7500, 3500, .8]],
  bank: [['wood', 0, .25, 110, 40, .8], ['noise', .09, .35, 1800, 200, .8], ['noise', .4, .45, 500, 1200, .4]],
  charge: [['horn', 0, .36, 65, 90, .5], ['wood', .12, .17, 120, 60, .5], ['wood', .3, .17, 130, 70, .7], ['noise', .4, .3, 600, 1700, .6]],
  stuck: [['wood', 0, .3, 95, 35, 1], ['noise', .02, .2, 1500, 200, .5]],
  transfer: [['noise', 0, .3, 500, 2000, .5], ['metal', .26, .23, 720, 600, .7]],
  roof: [['wood', 0, .17, 240, 140, .7], ['wood', .15, .16, 170, 90, .7], ['wood', .32, .055, 1000, 800, .5]],
  puff: [['noise', 0, .13, 1400, 600, .7]],
  bead: [['metal', 0, .17, 2100, 1900, .4], ['water', .13, .13, 700, 1600, .5]],
  flag: [['noise', 0, .14, 800, 1300, .4], ['horn', .08, .2, 440, 440, .5]],
  corner: [['metal', 0, .06, 1400, 1200, .7], ['metal', .08, .09, 1800, 1600, .6]],
  ration: [['noise', 0, .11, 1700, 900, .5], ['wood', .13, .07, 400, 220, .6]],
  pickup: [['bell', 0, .12, 1320, 1320, .6]],
  step: [['noise', 0, .075, 600, 200, .5], ['wood', 0, .065, 160, 90, .5]],
};
const cue = (id, recipe, duration, priority, cooldownSeconds = .14) => ({ id, recipe, duration, priority, cooldownSeconds });
export const CUES = [
  cue('weapon-iron-blade', ['slash'], .3, 110), cue('weapon-ward-orbit', ['tick'], .16, 120),
  cue('weapon-storm-fork', ['spark'], .2, 105), cue('weapon-ember-wand', ['fire'], .24, 115),
  cue('weapon-harvest-scythe', ['scythe'], .36, 110), cue('tool-seed-bag', ['seeds'], .24, 130),
  cue('tool-rain-ladle', ['splash'], .28, 130), cue('tool-rain-ladle-dry', ['hammer'], .16, 150),
  cue('tool-carpenter-hammer', ['hammer'], .2, 130), cue('tool-muster-horn', ['horn'], .4, 120),
  cue('complete-seed-bag', ['grain'], .5, 70, .25), cue('complete-rain-ladle', ['irrigation'], .48, 70, .25),
  cue('complete-carpenter-hammer', ['shipment'], .6, 70, .25), cue('complete-muster-horn', ['return'], .65, 70, .25),
  cue('evolution-sowing-sworddance', ['slash', 'seeds'], .36, 60, .3),
  cue('evolution-warded-masonry', ['transfer'], .58, 60, .3),
  cue('evolution-sheltered-sowing', ['roof', 'seeds'], .5, 60, .3),
  cue('enemy-raider-tell', ['scrape'], .3, 45, .25), cue('enemy-seed-mite-tell', ['scratch'], .24, 55, .25),
  cue('enemy-crop-grazer-tell', ['snort'], .3, 55, .3), cue('enemy-ram-runner-tell', ['hoof'], .48, 40, .3),
  cue('enemy-shield-raider-tell', ['shield'], .4, 45, .3), cue('enemy-wine-wasp-tell', ['wasp'], .38, 40, .3),
  cue('boss-bank-break', ['bank'], .95, 20, .5), cue('boss-tusk-charge', ['charge'], .8, 15, .5),
  cue('boss-tusk-stuck', ['stuck'], .4, 25, .4),
  cue('item-bitter-seed-dust', ['puff'], .2, 160, .25), cue('item-clay-water-bead', ['bead'], .32, 150, .25),
  cue('item-crop-guard-signet', ['flag'], .35, 100, .4), cue('item-joiner-square', ['corner'], .23, 160, .25),
  cue('item-levy-bread-wrap', ['ration'], .25, 160, .3), cue('item-gathering-loop', ['pickup'], .17, 170, .15),
  cue('item-wayfarer-boots', ['step'], .13, 190, .22),
];
function layer(out, definition, seed) {
  const [type, at, duration, hz, endHz, gain] = definition;
  const start = Math.round(at * SAMPLE_RATE), length = Math.round(duration * SAMPLE_RATE);
  let state = seed, low = 0;
  for (let i = 0; i < length && start + i < out.length; i++) {
    const t = i / SAMPLE_RATE, u = i / length;
    const phase = tau * (hz * t + (endHz - hz) * t * t / (2 * duration));
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    const noise = state / 2147483648 - 1;
    low += Math.min(.9, tau * (hz + (endHz - hz) * u) / SAMPLE_RATE) * (noise - low);
    const attack = Math.min(1, t / (type === 'horn' ? .045 : .004));
    const release = Math.min(1, (length - i - 1) / (SAMPLE_RATE * .025));
    let value, decay = 5;
    switch (type) {
      case 'noise': value = low; decay = 2; break;
      case 'metal': value = Math.sin(phase) + .5 * Math.sin(phase * 2.71) + .2 * Math.sin(phase * 4.13); decay = 7; break;
      case 'wood': value = Math.sin(phase) + .55 * Math.sin(phase * 1.47) + noise * .22; decay = 10; break;
      case 'water': value = Math.sin(phase + .4 * Math.sin(phase * 1.9)); decay = 4; break;
      case 'horn': value = Math.sin(phase) + .35 * Math.sin(phase * 2) + .18 * Math.sin(phase * 3); decay = 1; break;
      case 'bell': value = Math.sin(phase) + .25 * Math.sin(phase * 2.76); decay = 4; break;
      case 'buzz': value = Math.sin(phase) + .6 * Math.sin(phase * 3) + .3 * Math.sin(phase * 5); decay = .6; break;
      default: throw new Error(`Unknown timbre ${type}`);
    }
    out[start + i] += value * gain * attack * release * Math.exp(-u * decay);
  }
}
export function synthesize(id) {
  const definition = CUES.find(value => value.id === id);
  if (!definition) throw new Error(`Unknown wave-1a cue ${id}`);
  const out = new Float64Array(Math.round(definition.duration * SAMPLE_RATE));
  let index = 0;
  for (const recipe of definition.recipe) for (const part of layers[recipe]) layer(out, part, 97127 + index++ * 337);
  const peak = out.reduce((max, sample) => Math.max(max, Math.abs(sample)), 0);
  assert.ok(peak > 0);
  for (let i = 0; i < out.length; i++) out[i] = out[i] / peak * .12;
  out[0] = 0; out[out.length - 1] = 0;
  return out;
}
export function signalStats(samples) {
  let peak = 0, square = 0, active = 0;
  for (const sample of samples) {
    assert.ok(Number.isFinite(sample), 'Non-finite audio');
    peak = Math.max(peak, Math.abs(sample)); square += sample * sample;
    if (Math.abs(sample) > .0001) active++;
  }
  const rms = Math.sqrt(square / samples.length);
  assert.ok(peak > .05 && peak <= .120001, 'Peak/clipping bound');
  assert.ok(rms > .005 && rms < .08, 'Silence/RMS bound');
  assert.ok(active / samples.length > .15, 'Excessive silence');
  assert.equal(samples[0], 0); assert.equal(samples.at(-1), 0);
  return { peak: Number(peak.toFixed(6)), rms: Number(rms.toFixed(6)), peakDbfs: Number((20 * Math.log10(peak)).toFixed(2)), rmsDbfs: Number((20 * Math.log10(rms)).toFixed(2)), activeFraction: Number((active / samples.length).toFixed(4)) };
}
function meta(id) {
  const guid = createHash('sha256').update(`sowsiege-wave1a-audio/${id}`).digest('hex').slice(0, 32);
  return `fileFormatVersion: 2\nguid: ${guid}\nAudioImporter:\n  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 22050\n    compressionFormat: 0\n    quality: 1\n    conversionMode: 0\n    preloadAudioData: 1\n  platformSettingOverrides: {}\n  forceToMono: 1\n  normalize: 0\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 0\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n`;
}
function generate(check) {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const destination = path.join(root, 'unity/Assets/Resources/Audio/Wave1a');
  const records = [], hashes = new Set();
  if (!check) fs.mkdirSync(destination, { recursive: true });
  for (const definition of CUES) {
    const samples = synthesize(definition.id), stats = signalStats(samples), bytes = wave(samples);
    assert.deepEqual(bytes, wave(synthesize(definition.id)), 'Non-deterministic cue');
    const sha256 = createHash('sha256').update(bytes).digest('hex');
    assert.ok(!hashes.has(sha256), 'Duplicate cue waveform'); hashes.add(sha256);
    const file = path.join(destination, definition.id + '.wav');
    if (check) {
      assert.deepEqual(fs.readFileSync(file), bytes, `Stale ${file}`);
      assert.equal(fs.readFileSync(file + '.meta', 'utf8'), meta(definition.id));
    } else { fs.writeFileSync(file, bytes); fs.writeFileSync(file + '.meta', meta(definition.id)); }
    records.push({ ...definition, resourcePath: 'Audio/Wave1a/' + definition.id, sha256, bytes: bytes.length, ...stats });
  }
  assert.throws(() => synthesize('unsupported'), /Unknown/);
  const manifest = {
    version: 1, profile: 'wave-1a', generator: 'tools/wave1a-audio.mjs', sampleRate: SAMPLE_RATE, channels: 1, bitDepth: 16,
    provenance: 'Original deterministic additive/subtractive PCM synthesis authored for Sow & Siege; no samples, external recordings, or third-party sound assets. No CC0 claim. Repository output terms apply.',
    playbackBudget: { maxVoices: 6, reservedThreatVoices: 2, effectsGain: .7, musicGain: .3, musicPeak: .16, worstCasePeak: .552, steal: 'Reject or replace lower-priority active cue; lower numeric priority wins. Preserve boss and enemy tells before attacks/items. No per-entity AudioSource spawning.' },
    bindings: { 'item-meadow-buckle': 'weapon-iron-blade' },
    validation: 'Signal integrity and byte reproducibility checked by --check. Listening and in-game mix acceptance require actual Unity output review.',
    cues: records,
  };
  const output = path.join(root, 'unity/Assets/Art/wave1a-audio.json'), json = JSON.stringify(manifest, null, 2) + '\n';
  if (check) assert.equal(fs.readFileSync(output, 'utf8'), json); else fs.writeFileSync(output, json);
  console.log(`${records.length} original cues: deterministic PCM, unique waveforms, no clipping/silence; ${records.reduce((sum, value) => sum + value.bytes, 0)} bytes; conservative six-voice + music peak .552.`);
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) generate(process.argv.includes('--check'));
