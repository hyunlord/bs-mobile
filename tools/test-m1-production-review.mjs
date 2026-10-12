import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { makePlan, makeCombatPlan, validateCombatEvents, validateHeroActions, selectSheetWindows, selectWeaponRuns } from './m1-production-review.mjs';
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'production-plan-test-'));
try {
  const baked = path.join(root, 'baked.json'), drawn = path.join(root, 'drawn.json');
  fs.writeFileSync(baked, JSON.stringify({ method: '3d-baked' })); fs.writeFileSync(drawn, JSON.stringify({ method: 'frame-drawn' }));
  const plan = makePlan(path.join(root, 'captures'), baked, drawn);
  assert.equal(plan.runs.length, 27); assert.equal(new Set(plan.runs.map(run => run.directory)).size, 27);
  assert(plan.runs.filter(run => run.kind === 'hero').every(run => run.frames === 1800));
  assert(plan.runs.filter(run => run.kind === 'weapon').every(run => run.frames === 600));
  assert.equal(new Set(plan.runs.filter(run => run.kind === 'weapon').map(run => run.contentId)).size, 8);
  for (const run of plan.runs) { assert(run.args.includes('--knight-review')); assert(run.args.includes('--m1-review-seconds')); assert.equal(run.args[run.args.indexOf('--camera-scale') + 1], '1.4'); }
  assert(!plan.runs[0].args.includes('--hero-production')); assert(plan.runs[1].args.includes(baked)); assert(plan.runs[2].args.includes(drawn));
  for (const run of plan.runs.slice(1, 3)) { assert.equal(run.args[run.args.indexOf('--hero-production-direction') + 1], 'follow'); assert.equal(run.expected['knight-review.json'].productionDirection, 'follow'); }
  const baseline = Array.from({ length: 120 }, () => ({ action: 'live-rig' })), animated = Array.from({ length: 120 }, (_, i) => ({ action: i === 10 ? 'walk' : i === 20 ? 'slash-light' : i === 90 ? 'death' : 'idle' }));
  const sheets = selectSheetWindows([baseline, animated, animated]);
  validateHeroActions([{ method: 'a-knight-live', action: 'live-rig' }], plan.runs[0]);
  for (const run of plan.runs.slice(1, 3)) {
    validateHeroActions([{ method: run.method, action: 'walk' }, { method: run.method, action: 'slash-light' }], run);
    assert.throws(() => validateHeroActions([{ method: run.method, action: 'walk' }], run), /missing actual rendered slash-light/);
    assert.throws(() => validateHeroActions([{ method: run.method, action: 'slash-light' }], run), /missing actual rendered walk/);
  }
  assert.equal(sheets[0].available, true); assert.deepEqual(sheets[0].frames, [10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65]);
  assert.equal(sheets[1].action, 'slash-light'); assert.equal(sheets[1].available, true); assert.equal(sheets[1].startFrame, 20); assert.equal(sheets[1].frames.length, 12);
  assert.equal(sheets[2].available, false); assert.equal(sheets[2].startFrame, null);
  assert.equal(sheets[3].available, false); assert.equal(sheets[3].startFrame, 90);
  fs.writeFileSync(drawn, JSON.stringify({ method: '3d-baked' })); assert.throws(() => makePlan(path.join(root, 'other'), baked, drawn));
  const cases = path.join(root, 'cases'); fs.mkdirSync(cases);
  const ids = [...new Set(plan.runs.filter(run => run.kind === 'weapon').map(run => run.contentId))];
  for (const id of ids) for (const stage of ['lv1', 'max', 'evolved']) {
    const prefix = path.join(cases, id.replace(':', '-') + '-' + stage), bytes = Buffer.from(`fixture-only ${id}/${stage}`);
    fs.writeFileSync(prefix + '.ssreplay', bytes);
    fs.writeFileSync(prefix + '.case.json', JSON.stringify({ contract: 'm1-combat-case-v1', classification: 'hypothesis', proposedContract: 'm1-review-composition-v1', captureStartTick: 300, endTick: 600, id, stage, replaySha256: createHash('sha256').update(bytes).digest('hex'), stateHash: 'A'.repeat(64), catalogHash: 'B'.repeat(64) }));
  }
  const combat = makeCombatPlan(path.join(root, 'combat'), cases);
  assert.equal(combat.runs.length, 24); assert.equal(combat.replay, undefined);
  assert.equal(new Set(combat.runs.map(run => run.replaySha256)).size, 24);
  for (const candidate of [plan, combat]) {
    assert.equal(selectWeaponRuns(candidate).length, 24);
    assert.equal(selectWeaponRuns(candidate, 'core:iron_blade').length, 3);
    assert.throws(() => selectWeaponRuns(candidate, 'core:missing'));
    const selected = selectWeaponRuns(candidate, 'core:iron_blade');
    assert.throws(() => selectWeaponRuns({ ...candidate, runs: selected.slice(1) }, 'core:iron_blade'));
    assert.throws(() => selectWeaponRuns({ ...candidate, runs: [selected[0], selected[0], selected[2]] }, 'core:iron_blade'));
  }
  assert(combat.runs.every(run => run.frames === 600 && run.args.length === 2 && run.args[0] === '--m1-combat-case'));
  const run = combat.runs[0], hit = { eventId: '1', frameIndex: '5', snapshotTick: '303', eventTick: '302', kind: 'hit', source: run.contentId, subjectId: '7', amount: '24', inViewport: 'True' }, death = { ...hit, eventId: '2', kind: 'enemy-killed', source: 'core:raider' }, manifest = { firstVisibleHitFrame: 5, firstVisibleDeathFrame: 5 }, expected = { windowHits: 1, windowKills: 1 };
  assert.deepEqual(validateCombatEvents([hit, death], run, manifest, expected), { hitCount: 1, killCount: 1, firstVisibleHitFrame: 5, firstVisibleDeathFrame: 5 });
  validateCombatEvents([{ ...hit, kind: 'group-hit' }, death], run, manifest, expected);
  assert.throws(() => validateCombatEvents([death], run, manifest, expected));
  assert.throws(() => validateCombatEvents([{ ...hit, source: 'core:unrelated' }, death], run, manifest, expected));
  assert.throws(() => validateCombatEvents([hit, { ...death, eventTick: '301' }], run, manifest, expected));
  assert.throws(() => validateCombatEvents([hit, death], run, { ...manifest, firstVisibleHitFrame: 9 }, expected));
  const noKills = { windowHits: 1, windowKills: 0 }, noDeath = { firstVisibleHitFrame: 5, firstVisibleDeathFrame: -1 };
  assert.equal(validateCombatEvents([hit], { ...run, stage: 'lv1' }, noDeath, noKills).killCount, 0);
  assert.throws(() => validateCombatEvents([hit, death], { ...run, stage: 'lv1' }, manifest, noKills));
  assert.throws(() => validateCombatEvents([hit], { ...run, stage: 'evolved' }, noDeath, noKills));
  fs.appendFileSync(combat.runs[0].replay, 'tampered'); assert.throws(() => makeCombatPlan(path.join(root, 'bad'), cases));
  console.log('PASS production plan: 3 matched methods, 24 staged weapons, exact durations/flags, first-evidence sheet selection, missing/late actions N/A, swapped method rejected');
  console.log('PASS combat plan: 24 independent identities, real selected-source/ally hits and attributed deaths, incorrect source/tick/first-frame and replay tampering rejected');
} finally { fs.rmSync(root, { recursive: true }); }
