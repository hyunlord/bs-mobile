import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import * as productionMeasure from './m1-production-measure.mjs';
import { makePerformancePlan, foregroundFrames, frameSummary, stats } from './m1-production-measure.mjs';
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'production-perf-test-'));
try {
  const a = path.join(root, 'a.json'), b = path.join(root, 'b.json'); fs.writeFileSync(a, JSON.stringify({ method: '3d-baked' })); fs.writeFileSync(b, JSON.stringify({ method: 'frame-drawn' }));
  const plan = makePerformancePlan(path.join(root, 'plan'), a, b);
  assert.equal(plan.entries.length, 4); assert.equal(new Set(plan.entries.map(x => x.replaySha256)).size, 1);
  assert(plan.entries.every(e => e.window === '13500-14400' && !e.args.some(arg => arg.startsWith('--offline'))));
  assert(plan.entries[3].args.includes('all')); assert(plan.entries.slice(1, 3).every(e => e.args.includes('follow')));
  assert.equal(typeof productionMeasure.summaryInterpretation, 'function');
  assert.match(productionMeasure.summaryInterpretation(plan), /View-only/);
  assert.doesNotMatch(productionMeasure.summaryInterpretation({ ...plan, entries: plan.entries.slice(0, 3) }), /all8|eight|View-only/i);
  const neutralPlan = path.join(root, 'neutral.json');
  fs.writeFileSync(neutralPlan, JSON.stringify({ contract: 'm1-knight-measure-v1', entries: [] }));
  const neutral = JSON.parse(execFileSync(process.execPath, [new URL('./m1-knight-measure.mjs', import.meta.url).pathname, 'summary', neutralPlan], { encoding: 'utf8' }));
  assert.doesNotMatch(neutral.interpretation, /143|candidate2|30 seconds|original C05/);
  const f = { focused: '1', partialBoundary: '0', enemies: '800', wallMs: '16.7', simulationMs: '2', presentMs: '1' };
  const frames = [f, { ...f, focused: '0', wallMs: '1000' }, { ...f, partialBoundary: '1', wallMs: '500' }, { ...f, enemies: '799', wallMs: '26' }];
  assert.equal(foregroundFrames(frames).length, 2); assert.equal(foregroundFrames(frames, 800).length, 1);
  const summary = frameSummary(foregroundFrames(frames)); assert.equal(summary.frameMs.max, 26); assert.equal(summary.over25Percent, 50); assert.equal(summary.maximum50msGate, true); assert.equal(summary.over25Gate, false);
  assert.equal(stats([-1, NaN]), null); assert.throws(() => frameSummary([]));
  console.log('PASS production performance: four matched capture-OFF plans, follow directions, visual-only all8, foreground/boundary/density exclusion, thresholds and unavailable counters');
} finally { fs.rmSync(root, { recursive: true }); }
