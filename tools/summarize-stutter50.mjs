import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseCsv } from './csv.mjs';
import { configFields } from './summarize-wave-benchmark.mjs';

export const order = ['A1', 'B1', 'B2', 'A2', 'A3', 'B3'];
export const baselineCommit = '8f446e5f46273a7f6001f6e09411e9bdd0c9f534';
export const fixtures = {
  crowd411: { fixture: 'rules-off-six-minute-8c94ce2', stateHash: '59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619', replaySha256: '4fe0f97f23d20b487730be03ae76f5d7551ec88b787d43566f0cde41cd5a079c', dataHash: '51DA41A20DF37E4960AFB157AC7AA83D20C9AB1FC7E6B5E6D85643BC9C438B74', tick: 11530, commands: 11546, width: 1920, height: 1080, benchmarkMode: 'normal-mac-vsync', targetFrameRate: -1, vSyncCount: 2, windows: '10800-11530' },
  c05capped: { fixture: 'c05', stateHash: 'B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8', replaySha256: 'f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5', dataHash: '942CFEA33A25F90CBD5053D711C9BBFF1279DBE481950F2081C765B6093C3FDC', tick: 14400, commands: 14403, width: 1600, height: 900, benchmarkMode: 'capped', targetFrameRate: 60, vSyncCount: 0, windows: '7200-9900,10200-12900,13200-14400' }
};
export function frameStats(rows) {
  assert(rows.length > 0, 'Required density group has no frames');
  const values = rows.map(row => row.wallMs).sort((a, b) => a - b);
  assert(values.every(value => Number.isFinite(value) && value > 0), 'Invalid frame duration');
  const over25 = values.filter(value => value > 25).length;
  return { frames: values.length, p95_ms: values[Math.ceil(values.length * .95) - 1], p99_ms: values[Math.ceil(values.length * .99) - 1], max_ms: values.at(-1), over25, over25_percent: 100 * over25 / values.length,
    pass: values.at(-1) <= 50 && over25 * 500 <= values.length };
}
export function foregroundTicks(start, end, events, initialActive = true) {
  assert(Number.isFinite(start) && end >= start, 'Invalid foreground interval');
  let cursor = start, active = initialActive, ticks = 0;
  for (const event of events) {
    if (event.timestamp < start || event.timestamp > end) continue;
    assert(event.timestamp >= cursor, 'Unordered foreground events');
    if (active) ticks += event.timestamp - cursor;
    cursor = event.timestamp; active = event.focus && !event.paused;
  }
  return ticks + (active ? end - cursor : 0);
}
function load(directory, fixture) {
  const identity = JSON.parse(fs.readFileSync(path.join(directory, 'benchmark.json'), 'utf8'));
  for (const key of ['verified', 'performanceEligible', 'frameTimingEnabled']) assert.equal(identity[key], true, `${directory}: ${key}`);
  for (const key of ['sourceDirty', 'developmentBuild', 'profilerEnabled', 'interrupted', 'diagnosticOnly', 'actorGroundingSkipped']) assert.equal(identity[key], false, `${directory}: ${key}`);
  assert.equal(identity.error, '', `${directory}: error`);
  for (const [key, length] of [['commit', 40], ['sourceHash', 64]]) assert.match(identity[key], new RegExp(`^[a-f0-9]{${length}}$`, 'i'), `${directory}: ${key}`);
  for (const [key, value] of Object.entries(fixture)) assert.equal(identity[key], value, `${directory}: ${key} fixture mismatch`);
  assert.equal(identity.expectedStateHash, fixture.stateHash, 'Expected hash differs from frozen replay');
  for (const [key, value] of Object.entries({ platform: 'OSXPlayer', graphicsApi: 'Metal', renderWarmupFrames: 120, gcMode: 'Enabled', metalDisplayLinkEnvironment: 'unset', coreTickSamples: 0 })) assert.equal(identity[key], value, `${directory}: ${key}`);
  assert.equal(typeof identity.quality, 'string'); assert(identity.quality.length > 0);
  const rows = parseCsv(fs.readFileSync(path.join(directory, 'frames.csv'), 'utf8')).map(row => {
    const parsed = {};
    for (const key of ['frame', 'tick', 'enemies', 'wallMs', 'focused']) {
      assert(typeof row[key] === 'string' && row[key].trim(), `${directory}: missing ${key}`);
      parsed[key] = Number(row[key]); assert(Number.isFinite(parsed[key]) && parsed[key] >= 0, `${directory}: invalid ${key}`);
      if (key !== 'wallMs') assert(Number.isSafeInteger(parsed[key]), `${directory}: noninteger ${key}`);
    }
    if (row.partialBoundary === '1') {
      const start = Number(row.entryTimestamp), end = Number(row.nextEntryTimestamp), frequency = identity.stopwatchFrequency;
      assert(Number.isFinite(frequency) && frequency > 0 && Array.isArray(identity.focusEvents), 'Boundary row lacks clock/lifecycle evidence');
      const expected = foregroundTicks(start, end, identity.focusEvents) * 1000 / frequency;
      assert(Math.abs(parsed.wallMs - expected) < .00001, 'Boundary foreground duration mismatch');
      assert(Math.abs(Number(row.originalWallMs) - (end - start) * 1000 / frequency) < .00001, 'Boundary original duration mismatch');
      assert(Math.abs(Number(row.excludedBackgroundMs) - (Number(row.originalWallMs) - expected)) < .00001, 'Boundary excluded duration mismatch');
    }
    assert.equal(parsed.focused, 1, `${directory}: non-foreground frame`);
    return parsed;
  });
  assert(rows.length > 0 && rows.length === identity.frames, `${directory}: missing/truncated frames`);
  const lifecycle = identity.focusEvents ?? [];
  assert(Array.isArray(lifecycle), 'Invalid lifecycle timeline');
  for (let i = 0; i < lifecycle.length; i++) {
    const event = lifecycle[i];
    assert(Number.isFinite(event.timestamp) && typeof event.focus === 'boolean' && typeof event.paused === 'boolean', 'Invalid lifecycle event');
    if (i) assert(event.timestamp >= lifecycle[i - 1].timestamp, 'Unordered lifecycle timeline');
  }
  const exclusions = identity.excludedIntervals ?? [];
  assert(Array.isArray(exclusions), 'Invalid exclusions');
  if (exclusions.length) {
    assert.equal(identity.foregroundPolicy, 'pause-input-exclude-boundary-resume-v1');
    assert(Array.isArray(identity.focusEvents) && identity.focusEvents.some(event => event.focus === false || event.paused === true), 'Exclusions require lifecycle evidence');
    for (const interval of exclusions) {
      assert(Number.isSafeInteger(interval.firstFrame) && Number.isSafeInteger(interval.lastFrame) && interval.firstFrame <= interval.lastFrame, 'Invalid exclusion range');
      assert(['background', 'resume-boundary'].includes(interval.reason), 'Unknown exclusion reason');
      assert(Number.isFinite(interval.startTimestamp) && interval.endTimestamp >= interval.startTimestamp, 'Invalid exclusion clock');
      let activeAtStart = true;
      for (const event of identity.focusEvents) if (event.timestamp <= interval.startTimestamp) activeAtStart = event.focus && !event.paused;
      const transitions = identity.focusEvents.filter(event => event.timestamp >= interval.startTimestamp && event.timestamp <= interval.endTimestamp);
      if (interval.reason === 'background')
        assert(!activeAtStart || transitions.some(event => !event.focus || event.paused), 'Exclusion does not overlap background timeline');
      else
        assert(transitions.some(event => event.focus && !event.paused), 'Resume exclusion lacks matching transition');
      assert(!rows.some(row => row.frame >= interval.firstFrame && row.frame <= interval.lastFrame), 'Excluded frame also counted');
    }
  }
  const windows = fixture.windows.split(',').map(value => value.split('-').map(Number));
  const windowOf = tick => windows.findIndex(([start, end]) => tick >= start && tick <= end);
  for (let i = 0; i < rows.length; i++) {
    const row = rows[i]; assert(windowOf(row.tick) >= 0, `${directory}: tick outside windows`);
    if (i > 0) {
      const previous = rows[i - 1]; assert(row.frame > previous.frame && row.tick >= previous.tick, `${directory}: frame order`);
      if (windowOf(row.tick) === windowOf(previous.tick)) {
        for (let missing = previous.frame + 1; missing < row.frame; missing++)
          assert(exclusions.some(interval => missing >= interval.firstFrame && missing <= interval.lastFrame), `${directory}: dropped frame without exclusion evidence`);
      }
    }
  }
  assert.equal(rows.at(-1).tick, fixture.tick, `${directory}: missing terminal frame`);
  for (let i = 0; i < windows.length; i++) assert(rows.some(row => windowOf(row.tick) === i), `${directory}: missing window`);
  return { identity, rows };
}
export function summarizeStutter50(root) {
  const entries = fs.readdirSync(root, { withFileTypes: true }).filter(entry => entry.isDirectory() && /^[AB]\d+$/.test(entry.name)).map(entry => entry.name).sort();
  assert.deepEqual(entries, [...order].sort(), 'Exactly A1,A2,A3,B1,B2,B3 are required');
  const baseline = new Map(), sources = new Map(), result = [];
  let candidate;
  for (const run of order) for (const [name, fixture] of Object.entries(fixtures)) {
    const { identity, rows } = load(path.join(root, run, name), fixture);
    const side = run[0];
    if (side === 'A') { assert.equal((identity.excludedIntervals ?? []).length, 0, 'Immutable A must be uninterrupted'); assert.equal(identity.commit, baselineCommit, 'A must be immutable 8f446e5'); assert.equal(identity.sourceHash, 'EBAA94181C9B2A4989E4448DB781B828E971F27D497ADD52230BA53F15EA94F5', 'A source differs from immutable 8f build'); }
    else { candidate ??= identity.commit; assert.equal(identity.commit, candidate, 'B mixes commits'); assert.notEqual(candidate, baselineCommit, 'B must identify the modified build'); }
    const source = sources.get(side); if (source) assert.equal(identity.sourceHash, source, 'One side mixes source hashes'); else sources.set(side, identity.sourceHash);
    const config = Object.fromEntries([...configFields, 'benchmarkMode', 'gcMode', 'metalDisplayLinkEnvironment', 'heroRig'].map(key => { assert(Object.hasOwn(identity, key), `Missing configuration ${key}`); return [key, identity[key]]; }));
    if (baseline.has(name)) assert.deepEqual(config, baseline.get(name), `${name}: A/B configuration mismatch`); else baseline.set(name, config);
    const groups = name === 'crowd411' ? [['411', rows]] : [['C05-all', rows], ['C05-800+', rows.filter(row => row.enemies >= 800)]];
    for (const [scope, frames] of groups) result.push({ run, side, scope, ...frameStats(frames), commit: identity.commit, state_hash: identity.stateHash, source_hash: identity.sourceHash, eligible: true, excluded_intervals: (identity.excludedIntervals ?? []).length, foreground_policy: identity.foregroundPolicy ?? 'legacy-uninterrupted-only', pacing_policy: identity.pacingPolicy ?? 'legacy-unbounded-catch-up' });
  }
  return { contract: 'Each B repetition independently: max <=50ms and >25ms ratio <=0.2%. No outlier removal or pooled acceptance. A failures remain visible. Immutable A requires an uninterrupted run; resumable B excludes only documented lifecycle boundaries, never steady-focus stalls. C05-all means every frame in the unchanged C05 measurement windows. Missing historical pacingPolicy denotes legacy-unbounded-catch-up; candidate scheduling changes are shown per row, not silently treated as identical pacing. Directory names encode requested ABBA order; execution chronology requires launch records.', order, baselineCommit, candidateCommit: candidate, pass: result.filter(row => row.side === 'B').every(row => row.pass), rows: result };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const root = process.argv[2]; assert(root, 'Usage: node tools/summarize-stutter50.mjs <ABBA-root> [output-directory]');
  const report = summarizeStutter50(root), output = path.resolve(process.argv[3] ?? root);
  fs.mkdirSync(output, { recursive: true });
  fs.writeFileSync(path.join(output, 'stutter50.json'), JSON.stringify(report, null, 2) + '\n');
  const headers = Object.keys(report.rows[0]);
  fs.writeFileSync(path.join(output, 'stutter50.csv'), [headers.join(','), ...report.rows.map(row => headers.map(key => row[key]).join(','))].join('\n') + '\n');
  console.log(`STUTTER50_${report.pass ? 'PASS' : 'FAIL'} ${report.rows.length} independent rows; ${output}`);
  process.exitCode = report.pass ? 0 : 1;
}
