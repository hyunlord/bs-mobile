import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { loadRun, summarizePooled, configFields, deviceFields } from './summarize-wave-benchmark.mjs';

const readJson = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const defaultContract = new URL('../benchmarks/wave-c05/contract-v2.json', import.meta.url);
const metadataFields = ['benchmarkMode', 'profilerEnabled', 'gcMode'];

function loadVariant(root, mode, historicalCapped, contract) {
  const runs = Array.from({ length: contract.repetitions }, (_, index) => {
    const run = loadRun(path.join(root, `run-${index + 1}`), { ...contract, targetFrameRate: contract.modes[mode].targetFrameRate });
    const historical = metadataFields.every(key => !Object.hasOwn(run.identity, key));
    if (historicalCapped && historical) {
      assert.equal(mode, 'capped', 'Historical records are capped only');
    } else {
      assert.equal(run.identity.benchmarkMode, mode, 'Wrong benchmark mode');
      assert.equal(run.identity.profilerEnabled, false, 'Profiler-enabled or unrecorded run is not acceptance evidence');
      assert.ok(typeof run.identity.gcMode === 'string' && run.identity.gcMode.trim(), 'Missing GC mode');
    }
    return { ...run, historical };
  });
  const first = runs[0];
  for (const run of runs) {
    for (const key of ['commit', 'sourceHash', ...configFields, ...metadataFields]) {
      assert.equal(run.identity[key], first.identity[key], `Different ${key} within variant`);
    }
    assert.equal(run.historical, first.historical, 'Mixed historical and current metadata');
    for (const key of deviceFields) assert.equal(run.device[key], first.device[key], `Different device ${key}`);
  }
  const bins = contract.densityBins.map(([min, max]) => {
    const rows = runs.map(run => run.rows.filter(row => row.enemies >= min && (max === null || row.enemies <= max)));
    const repetitions = rows.map((selected, index) => {
      const over33ms = selected.filter(row => row.wallMs > contract.stutter.thresholdMilliseconds).length;
      const over100ms = selected.filter(row => row.wallMs > contract.stutter.severeThresholdMilliseconds).length;
      const over33msRatio = selected.length ? over33ms / selected.length : null;
      return { repetition: index + 1, ...summarizePooled([selected]), over33ms, over33msRatio, over100ms,
        status: !selected.length ? 'incomplete' : mode === 'uncapped' ? 'trend-only' :
          over33msRatio <= contract.stutter.maximumRatio && over100ms === 0 ? 'pass' : 'fail' };
    });
    return { min, max, ...summarizePooled(rows), repetitions,
      status: repetitions.some(run => run.status === 'incomplete') ? 'incomplete' : mode === 'uncapped' ? 'trend-only' :
        repetitions.every(run => run.status === 'pass') ? 'pass' : 'fail' };
  });
  return { commit: first.identity.commit, sourceHash: first.identity.sourceHash,
    historical: first.historical, profilerMetadata: first.historical ? 'unrecorded/historical-opt-in' : 'disabled',
    config: Object.fromEntries([...configFields, ...metadataFields].map(key => [key, first.identity[key] ?? null])),
    device: Object.fromEntries(deviceFields.map(key => [key, first.device[key]])), bins,
    status: bins.some(bin => bin.status === 'incomplete') ? 'incomplete' : mode === 'uncapped' ? 'trend-only' :
      bins.every(bin => bin.status === 'pass') ? 'pass' : 'fail' };
}

export function summarizeWaveBenchmarkV2(root, { mode, referenceRoot, historicalCapped = false }, contract = readJson(defaultContract)) {
  assert.ok(mode === 'uncapped' || mode === 'capped', 'Mode must be uncapped or capped');
  assert.ok(!historicalCapped || mode === 'capped', 'Historical opt-in requires capped mode');
  assert.ok(!referenceRoot || mode === 'uncapped', 'Regression reference requires uncapped mode');
  const measured = loadVariant(root, mode, historicalCapped, contract);
  const reference = referenceRoot ? loadVariant(referenceRoot, mode, false, contract) : null;
  if (reference) {
    for (const key of Object.keys(measured.config)) assert.deepEqual(measured.config[key], reference.config[key], `Different reference config ${key}`);
    for (const key of deviceFields) assert.equal(measured.device[key], reference.device[key], `Different reference device ${key}`);
  }
  const trend = mode === 'uncapped' ? measured.bins.map((bin, index) => {
    const base = reference?.bins[index];
    const complete = bin.status !== 'incomplete' && (!base || base.status !== 'incomplete');
    const increaseRatio = complete && base ? bin.wallMs.p95 / base.wallMs.p95 - 1 : null;
    return { min: bin.min, max: bin.max, p95: bin.wallMs.p95, referenceP95: base?.wallMs.p95 ?? null, increaseRatio,
      status: !complete ? 'incomplete' : !base ? 'baseline' : bin.wallMs.p95 > base.wallMs.p95 * (1 + contract.regression.maximumIncreaseRatio) ? 'warning' : 'within-trend' };
  }) : null;
  return { version: 2, mode, fixture: { replaySha256: contract.replaySha256, dataHash: contract.dataHash, endHash: contract.endHash },
    rule: mode === 'uncapped' ? 'Mac uncapped p95 is a trend only; warn above 20% pooled p95 increase against the same-config three-run reference.' :
      'Each density in each repetition: frames strictly over 33ms <= 0.1%, frames strictly over 100ms = 0. No frames excluded.',
    repetitions: contract.repetitions, windows: contract.windows, measured, reference, trend,
    status: measured.status === 'incomplete' || reference?.status === 'incomplete' ? 'incomplete' : mode === 'capped' ? measured.status :
      trend.some(bin => bin.status === 'warning') ? 'warning' : reference ? 'within-trend' : 'baseline',
    acceptanceScope: mode === 'uncapped' ? 'Mac regression trend only; device performance acceptance remains Fold7 #98.' :
      measured.historical ? 'Retrospective capped stutter result; profiler and GC metadata were not recorded.' : 'Native Release capped stutter gate.' };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const [mode, root, ...options] = process.argv.slice(2);
    assert.ok(root, 'Usage: summarize-wave-benchmark-v2.mjs uncapped ROOT [--reference ROOT] | capped ROOT [--historical-capped]');
    assert.ok(!options.length || options.length === 1 && options[0] === '--historical-capped' || options.length === 2 && options[0] === '--reference', 'Invalid options');
    console.log(JSON.stringify(summarizeWaveBenchmarkV2(root, { mode, historicalCapped: options[0] === '--historical-capped', referenceRoot: options[0] === '--reference' ? options[1] : undefined }), null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
