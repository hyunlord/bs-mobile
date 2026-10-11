// Summarize the two capture-free portrait fixtures without mixing them into historical ABBA runs.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { parseCsv } from './csv.mjs';

const root = process.argv[2];
if (!root) throw new Error('Usage: node tools/summarize-portrait-native.mjs <native-run-root> [output-directory]');
const output = path.resolve(process.argv[3] ?? root);
const summaryHeaders = ['commit', 'fixture', 'density', 'frames', 'p50_ms', 'p95_ms', 'p99_ms', 'max_ms', 'over25', 'over25_percent', 'gc_per_second', 'state_hash',
  'source_hash', 'data_hash', 'replay_sha256', 'width', 'height', 'target_frame_rate', 'vsync_count', 'benchmark_mode', 'graphics_api', 'quality', 'platform', 'source_dirty', 'development_build', 'profiler_enabled', 'performance_eligible', 'all_foreground', 'runs'];
const outlierHeaders = ['commit', 'fixture', 'frame', 'tick', 'enemies', 'advances', 'wall_ms', 'simulation_ms', 'snapshot_ms', 'accept_ms', 'present_ms', 'hud_ms', 'gc'];
const summary = [summaryHeaders];
const outliers = [outlierHeaders];
const number = (row, key) => {
  assert(row[key] !== undefined && row[key] !== '', `Missing ${key}`);
  const value = Number(row[key]);
  assert(Number.isFinite(value), `Invalid ${key}`);
  return value;
};
for (const fixture of ['crowd411', 'c05capped']) {
  const directory = path.join(root, fixture);
  const record = JSON.parse(fs.readFileSync(path.join(directory, 'benchmark.json'), 'utf8'));
  assert(record.verified && !record.interrupted && !record.sourceDirty && !record.developmentBuild && !record.profilerEnabled && record.performanceEligible,
    `${fixture} is not an eligible clean Release run`);
  assert.equal(record.stateHash, record.expectedStateHash, `${fixture} replay hash mismatch`);
  for (const field of ['commit', 'sourceHash', 'dataHash', 'replaySha256', 'stateHash', 'benchmarkMode', 'graphicsApi', 'quality', 'platform']) {
    assert.equal(typeof record[field], 'string', `Missing provenance ${field}`);
    assert(record[field].length > 0, `Empty provenance ${field}`);
  }
  const rows = parseCsv(fs.readFileSync(path.join(directory, 'frames.csv'), 'utf8'));
  assert(rows.length > 0 && rows.length === record.frames, `${fixture} frame count mismatch`);
  for (const row of rows) {
    assert.equal(row.focused, '1', `${fixture} contains non-foreground frames`);
    assert(number(row, 'wallMs') > 0);
    assert(number(row, 'enemies') >= 0 && number(row, 'gcCollections') >= 0);
  }
  const groups = [['all', rows], ...['400-599', '600-799', '800+'].map((density, index) =>
    [density, rows.filter(row => number(row, 'enemies') >= 400 + index * 200 && (index === 2 || number(row, 'enemies') < 600 + index * 200))])];
  for (const [density, frames] of groups) {
    if (!frames.length) continue;
    const wall = frames.map(row => number(row, 'wallMs')).sort((a, b) => a - b);
    const percentile = p => wall[Math.ceil(wall.length * p) - 1];
    const over = wall.filter(value => value > 25).length;
    const seconds = wall.reduce((sum, value) => sum + value, 0) / 1000;
    summary.push([record.commit, fixture, density, wall.length, percentile(.5), percentile(.95), percentile(.99), wall.at(-1), over,
      100 * over / wall.length, frames.reduce((sum, row) => sum + number(row, 'gcCollections'), 0) / seconds, record.stateHash,
      record.sourceHash, record.dataHash, record.replaySha256, record.width, record.height, record.targetFrameRate, record.vSyncCount,
      record.benchmarkMode, record.graphicsApi, record.quality, record.platform, record.sourceDirty, record.developmentBuild,
      record.profilerEnabled, record.performanceEligible, true, 1]);
  }
  for (const row of rows.filter(value => number(value, 'wallMs') > 25)) {
    outliers.push([record.commit, fixture, ...['frame', 'tick', 'enemies', 'advances', 'wallMs', 'simulationMs', 'snapshotMs', 'acceptMs', 'presentMs', 'hudMs', 'gcCollections'].map(key => number(row, key))]);
  }
}
const cell = value => /[",\r\n]/.test(String(value)) ? `"${String(value).replaceAll('"', '""')}"` : String(value);
fs.mkdirSync(output, { recursive: true });
for (const [name, rows] of [['portrait-native-227-summary.csv', summary], ['portrait-native-227-outliers.csv', outliers]]) {
  fs.writeFileSync(path.join(output, name), rows.map(row => row.map(cell).join(',')).join('\n') + '\n');
  console.log(`${name}: ${rows.length - 1} rows`);
}
