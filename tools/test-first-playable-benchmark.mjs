import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createHash } from 'node:crypto';
import test from 'node:test';
const root = new URL('../benchmarks/first-playable-fp01/', import.meta.url);
test('FP01 freezes the reviewed input, full terminal and all five species window', () => {
  const contract = JSON.parse(fs.readFileSync(new URL('contract.json', root)));
  const replay = fs.readFileSync(new URL('run.ssreplay', root));
  assert.equal(createHash('sha256').update(replay).digest('hex'), contract.replaySha256);
  assert.equal(replay.readInt32LE(replay.length - 89), 3);
  assert.equal(Number(replay.readBigInt64LE(replay.length - 85)), contract.commands);
  assert.equal(replay.readInt32LE(replay.length - 77), contract.endTick);
  assert.equal(replay.subarray(replay.length - 64).toString(), contract.endHash);
  const lines = fs.readFileSync(new URL('density-selection.csv', root), 'utf8').trim().split('\n');
  assert.equal(lines.length, 901);
  const rows = lines.slice(1).map(line => line.split(',').map(Number));
  assert.equal(rows[0][0], contract.windows[0][0]);
  assert.equal(rows.at(-1)[0], contract.measurementEndTick);
  for (let i = 0; i < rows.length; i++) {
    assert.equal(rows[i][0], contract.windows[0][0] + i);
    assert.ok(rows[i].slice(2).every(n => n > 0), 'all five species present in every selected tick');
  }
  for (const [min, max] of contract.densityBins) assert.ok(rows.filter(row => row[1] >= min && (max === null || row[1] <= max)).length >= 100);
  const host = fs.readFileSync(new URL('../unity/Assets/Game/App/FirstPlayableBenchmark.cs', import.meta.url), 'utf8');
  for (const key of ['replaySha256', 'dataHash', 'endHash']) assert.ok(host.includes(contract[key]));
  for (const value of [contract.commands, contract.endTick, ...contract.windows[0]]) assert.ok(host.includes(String(value)));
  assert.equal(contract.profile, 'first-playable');
  assert.equal(contract.repetitions, 3);
});

test('FP01 summary accepts its command count and measured endpoint, requires exact profile and fixture', async t => {
  const { default: os } = await import('node:os');
  const { default: path } = await import('node:path');
  const { formatCsv, parseCsv } = await import('./csv.mjs');
  const { frameHeaders, timingHeaders } = await import('./summarize-wave-benchmark.mjs');
  const { summarizeWaveBenchmarkV2 } = await import('./summarize-wave-benchmark-v2.mjs');
  const { spawnSync } = await import('node:child_process');
  const contract = JSON.parse(fs.readFileSync(new URL('contract.json', root)));
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'fp01-test-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  for (let repeat = 1; repeat <= 3; repeat++) {
    const run = path.join(dir, `run-${repeat}`); fs.mkdirSync(run);
    const densities = fs.readFileSync(new URL('density-selection.csv', root), 'utf8').trim().split('\n').slice(1).map(line => line.split(',').map(Number));
    const rows = densities.flatMap(([tick, enemies], i) => [0, 1].map(half => ({ frame: 2 * i + half, tick,
      enemies, commands: half === 0 ? 1 : 0, wallMs: 10, simulationMs: 1, snapshotMs: .5, acceptMs: .1, presentMs: 2, hudMs: .1, allocatedBytes: 128, gcCollections: 0, focused: 1 })));
    const identity = { commit: 'a'.repeat(40), sourceHash: 'b'.repeat(64), sourceDirty: false, verified: true, interrupted: false,
      developmentBuild: false, platform: 'OSXPlayer', fixture: contract.fixture, profile: contract.profile,
      dataHash: contract.dataHash, replaySha256: contract.replaySha256, stateHash: contract.endHash, expectedStateHash: contract.endHash,
      error: '', tick: contract.endTick, commands: contract.commands, width: 1600, height: 900, targetFrameRate: -1, vSyncCount: 0,
      renderWarmupFrames: 120, windows: contract.windows.map(w => w.join('-')).join(','), graphicsApi: 'Metal', quality: 'High',
      frameTimingEnabled: true, frames: rows.length, renderTimingSamples: 0, benchmarkMode: 'uncapped', profilerEnabled: false, gcMode: 'Enabled' };
    fs.writeFileSync(path.join(run, 'benchmark.json'), JSON.stringify(identity));
    fs.writeFileSync(path.join(run, 'device.json'), JSON.stringify({ model: 'Mac16,5', os: 'Mac OS X 26', unityVersion: '6000.6.4f1', backend: 'Mono', graphics: 'Metal', sourceHash: identity.sourceHash, sourceDirty: false }));
    fs.writeFileSync(path.join(run, 'frames.csv'), formatCsv(frameHeaders, rows));
    fs.writeFileSync(path.join(run, 'render-timings.csv'), formatCsv(timingHeaders, []));
  }
  const summarize = () => summarizeWaveBenchmarkV2(dir, { mode: 'uncapped' }, contract);
  assert.equal(summarize().status, 'baseline');
  const cli = spawnSync(process.execPath, ['tools/summarize-wave-benchmark-v2.mjs', 'uncapped', dir, '--contract', 'benchmarks/first-playable-fp01/contract.json'], { encoding: 'utf8' });
  assert.equal(cli.status, 0, cli.stderr); assert.equal(JSON.parse(cli.stdout).status, 'baseline');
  const file = path.join(dir, 'run-1/benchmark.json'), original = fs.readFileSync(file, 'utf8');
  for (const change of [{ profile: 'wave-1a' }, { fixture: 'c05' }, { commands: 14403 }, { tick: 20790 }]) {
    fs.writeFileSync(file, JSON.stringify({ ...JSON.parse(original), ...change }));
    assert.throws(summarize); fs.writeFileSync(file, original);
  }
  const frameFile = path.join(dir, 'run-1/frames.csv');
  const frameText = fs.readFileSync(frameFile, 'utf8');
  const fullRows = parseCsv(frameText, frameHeaders);
  for (const [name, selected] of [
    ['missing start', fullRows.slice(2)],
    ['missing middle tick', fullRows.filter(row => Number(row.tick) !== 20000)],
    ['truncated end', fullRows.slice(0, -2)],
    ['three sparse frames', [fullRows[0], fullRows[2], fullRows.at(-1)]],
  ]) {
    fs.writeFileSync(frameFile, formatCsv(frameHeaders, selected.map((row, frame) => ({ ...row, frame }))));
    fs.writeFileSync(file, JSON.stringify({ ...JSON.parse(original), frames: selected.length }));
    assert.throws(summarize, /FP01.*coverage|Missing terminal rendered state/, name);
    fs.writeFileSync(frameFile, frameText); fs.writeFileSync(file, original);
  }
  for (const args of [['--contract'], ['--contract', 'a', '--contract', 'b'], ['--unknown']]) {
    const bad = spawnSync(process.execPath, ['tools/summarize-wave-benchmark-v2.mjs', 'uncapped', dir, ...args], { encoding: 'utf8' });
    assert.notEqual(bad.status, 0);
  }
});

test('historical C05 contracts and original input remain byte identical', () => {
  const pins = {
  "contract.json": "4bf53c0a5cd69b7bad33f6418630b1042558c3dd4a639ea575f27053691f6790",
  "contract-v2.json": "18f15645836c4e855f542964807438d1aed905afd75c3af5cb06e70709375c4c",
  "run.ssreplay": "f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5"
};
  for (const [file, sha] of Object.entries(pins)) assert.equal(createHash('sha256').update(fs.readFileSync(new URL('../benchmarks/wave-c05/' + file, import.meta.url))).digest('hex'), sha);
});
