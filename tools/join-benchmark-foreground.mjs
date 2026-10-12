import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';

export function joinForeground(benchmark, observations) {
  assert(Array.isArray(benchmark.focusEvents), 'benchmark.focusEvents is required');
  assert(Array.isArray(observations) && observations.length <= 100000, 'Observer event limit exceeded');
  let prior = -Infinity;
  for (const row of observations) {
    assert(['initial', 'activated', 'observer-end'].includes(row.event), 'Unknown observer event');
    assert(Number.isFinite(row.utcUnixMilliseconds) && row.utcUnixMilliseconds >= prior, 'Observer UTC clock is missing or unordered');
    prior = row.utcUnixMilliseconds;
  }
  const start = observations[0], end = observations.at(-1);
  const covered = start?.event === 'initial' && end?.event === 'observer-end'
    && observations.slice(1, -1).every(row => row.event === 'activated');
  return benchmark.focusEvents.map((event, index) => {
    const time = event.utcUnixMilliseconds;
    let latest;
    if (covered && Number.isFinite(time) && time >= start.utcUnixMilliseconds && time <= end.utcUnixMilliseconds) {
      for (const observation of observations) {
        if (observation.utcUnixMilliseconds > time) break;
        latest = observation;
      }
    }
    const known = latest && typeof latest.name === 'string' && latest.name !== 'unavailable';
    return { index, frame: event.frame, tick: event.tick, kind: event.kind, focus: event.focus, paused: event.paused,
      utcUnixMilliseconds: time ?? null, stopwatchTimestamp: event.timestamp,
      status: known ? 'latest-observed-app' : 'unknown', appName: known ? latest.name : 'unknown',
      bundleIdentifier: known ? latest.bundleIdentifier ?? 'unknown' : 'unknown', pid: known ? latest.pid ?? null : null,
      observationUtcUnixMilliseconds: latest?.utcUnixMilliseconds ?? null,
      observationAgeMilliseconds: latest ? time - latest.utcUnixMilliseconds : null };
  });
}
function readBounded(file) {
  assert(fs.statSync(file).size <= 16 * 1024 * 1024, 'Input exceeds 16MiB limit');
  return fs.readFileSync(file, 'utf8');
}
export function writeForegroundJoin(benchmarkPath, observerPath, outputDirectory) {
  const benchmarkText = readBounded(benchmarkPath), observerText = readBounded(observerPath);
  const observations = observerText.split(/\r?\n/).filter(line => line.trim()).map(line => JSON.parse(line));
  const rows = joinForeground(JSON.parse(benchmarkText), observations);
  const report = { contract: 'Offline UTC join, latest observed app at lifecycle callback; asynchronous notification ordering is not causal proof. Missing initial/end coverage or UTC anchor is unknown. Source evidence is unchanged.',
    benchmarkSha256: createHash('sha256').update(benchmarkText).digest('hex'), observerSha256: createHash('sha256').update(observerText).digest('hex'), rows };
  fs.mkdirSync(outputDirectory, { recursive: true });
  const headers = ['index', 'frame', 'tick', 'kind', 'focus', 'paused', 'utcUnixMilliseconds', 'stopwatchTimestamp', 'status', 'appName', 'bundleIdentifier', 'pid', 'observationUtcUnixMilliseconds', 'observationAgeMilliseconds'];
  const escape = value => '"' + String(value ?? '').replaceAll('"', '""') + '"';
  for (const name of ['foreground-app-events.json', 'foreground-app-events.csv'])
    assert(!fs.existsSync(path.join(outputDirectory, name)), `Derived output already exists: ${name}`);
  fs.writeFileSync(path.join(outputDirectory, 'foreground-app-events.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
  fs.writeFileSync(path.join(outputDirectory, 'foreground-app-events.csv'), [headers.join(','), ...rows.map(row => headers.map(key => escape(row[key])).join(','))].join('\n') + '\n', { flag: 'wx' });
  return report;
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  assert.equal(process.argv.length, 5, 'Usage: node tools/join-benchmark-foreground.mjs <benchmark.json> <observer.jsonl> <derived-output-directory>');
  writeForegroundJoin(...process.argv.slice(2));
}
