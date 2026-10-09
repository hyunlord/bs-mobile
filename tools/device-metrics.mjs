import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseCsv } from './csv.mjs';

export const headers = 'frame,tick,deltaMs,speed,paused,enemies,projectiles,visualProjectiles,people,farms,buildings,width,height,safeX,safeY,safeWidth,safeHeight,thermal,suspended,partial'.split(',');
const thermalStates = new Set(['unavailable', 'none', 'light', 'moderate', 'severe', 'critical', 'emergency', 'shutdown']);
export function readDeviceRecording(directory, { commit, posture = 'unknown' }) {
  const read = (name) => JSON.parse(fs.readFileSync(path.join(directory, name), 'utf8'));
  const identity = read('recording.json'), facts = read('device.json'), summary = read('frame-summary.json');
  if (!/^[a-f\d]{40}$/i.test(commit) || identity.build !== commit || !Number.isInteger(identity.seed) || identity.seed < 0 || !/^[a-f\d]{64}$/i.test(identity.dataHash) || summary.dataHash !== identity.dataHash || summary.sessionId !== identity.sessionId || summary.build !== identity.build) throw new Error('Invalid or mismatched recording identity');
  if (!['folded', 'unfolded', 'mixed', 'unknown'].includes(posture)) throw new Error('Invalid posture');
  if (!Number.isInteger(summary.durationTicks) || summary.durationTicks <= 0 || summary.lateStartTick !== Math.floor(summary.durationTicks * 3 / 4)) throw new Error('Invalid measurement window');
  for (const key of ['model', 'os', 'unityVersion', 'backend', 'sourceHash']) if (typeof facts[key] !== 'string' || !facts[key]) throw new Error(`Missing device ${key}`);
  if (facts.backend !== 'IL2CPP' || !/^Android(?:\s|$)/.test(facts.os)) throw new Error('Phase1A device metrics require Android IL2CPP evidence; Editor and other platforms are unsupported');
  if (!/^[a-f\d]{64}$/i.test(facts.sourceHash) || typeof facts.sourceDirty !== 'boolean' || identity.sourceHash !== facts.sourceHash || identity.sourceDirty !== facts.sourceDirty) throw new Error('Invalid source provenance');
  const rows = parseCsv(fs.readFileSync(path.join(directory, 'frames.csv'), 'utf8'), headers);
  let previousTick = -1;
  for (const [index, row] of rows.entries()) {
    for (const key of headers.filter((key) => key !== 'thermal')) {
      if (!row[key].trim() || !Number.isFinite(Number(row[key])) || Number(row[key]) < 0) throw new Error(`Invalid numeric ${key}`);
      row[key] = Number(row[key]);
    }
    for (const key of ['frame','tick','speed','paused','suspended','partial','enemies','projectiles','visualProjectiles','people','farms','buildings']) if (!Number.isInteger(row[key])) throw new Error(`Noninteger ${key}`);
    if (row.frame !== index || row.tick < previousTick || row.tick > summary.durationTicks || ![1,2,4].includes(row.speed) || ![0,1].includes(row.paused) || ![0,1].includes(row.suspended) || ![0,1].includes(row.partial) || !thermalStates.has(row.thermal) || row.width <= 0 || row.height <= 0 || row.safeWidth <= 0 || row.safeHeight <= 0 || row.safeX + row.safeWidth > row.width || row.safeY + row.safeHeight > row.height) throw new Error('Invalid frame sequence or bounds');
    previousTick = row.tick;
  }
  if (rows.length !== summary.frames) throw new Error('Incomplete frame CSV');
  return { identity, facts, summary, rows };
}
export function summarizeDevice(directory, { commit, posture = 'unknown' }) {
  const { identity, facts, summary, rows } = readDeviceRecording(directory, { commit, posture });
  const late = rows.filter((row) => row.tick >= summary.lateStartTick && !row.paused && !row.suspended);
  const eligible = late.filter((row) => row.speed === 1 && !row.partial).map((row) => row.deltaMs).sort((a,b) => a-b);
  const screens = [...new Set(rows.map((row) => `${row.width}x${row.height}`))];
  const maxCounts = Object.fromEntries(['enemies','projectiles','visualProjectiles','people','farms','buildings'].map((key) => [key, rows.reduce((max,row) => Math.max(max,row[key]),0)]));
  return {
    schemaVersion: 2, timestamp: new Date().toISOString(), commit, stage: 'Phase1A', model: 'unity-frame', seed: identity.seed,
    runtime: { unity: facts.unityVersion, backend: facts.backend, os: facts.os },
    config: { dataHash: identity.dataHash, sourceHash: facts.sourceHash, sourceDirty: facts.sourceDirty, speed: 1, window: [summary.lateStartTick, summary.durationTicks] },
    measurementScope: 'rendered-frame unscaled delta; final quarter of Core ticks; previous-frame context; unpaused unsuspended complete 1x intervals only; nearest-rank p95; all slow frames retained',
    tickP95Ms: null, balanceDispersion: null,
    frameP95Ms: eligible.length ? eligible[Math.ceil(eligible.length * .95)-1] : null,
    frameMaxMs: eligible.length ? eligible.at(-1) : null,
    device: { model: facts.model, os: facts.os, screen: screens.join(',') || 'unobserved', posture },
    sampleCount: eligible.length,
    lateComplete: rows.length > 0 && rows[0].tick <= summary.lateStartTick && rows.at(-1).tick === summary.durationTicks && eligible.length > 0 && late.every((row) => row.speed === 1 && !row.partial) && late.some((row) => row.tick === summary.durationTicks && row.speed === 1 && !row.partial),
    sessionId: identity.sessionId, totalFrameCount: rows.length, pausedFrameCount: rows.filter((row) => row.paused).length, suspendedFrameCount: rows.filter((row) => row.suspended).length, partialFrameCount: rows.filter((row) => row.partial).length, maxCounts,
    thermalStates: [...new Set(rows.map((row) => row.thermal))],
  };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const [directory, ...args] = process.argv.slice(2), options = {};
    if (!directory || args.length % 2) throw new Error('Usage: device-metrics.mjs RUN_DIRECTORY --commit SHA --posture unknown --output FILE');
    for (let i=0;i<args.length;i+=2) { if (!['--commit','--posture','--output'].includes(args[i]) || options[args[i].slice(2)]) throw new Error('Unknown or duplicate argument'); options[args[i].slice(2)] = args[i+1]; }
    const result = summarizeDevice(directory, options);
    if (!options.output) throw new Error('--output is required');
    fs.writeFileSync(options.output, `${JSON.stringify(result,null,2)}\n`);
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
