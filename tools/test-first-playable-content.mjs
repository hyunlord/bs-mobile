import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { validateFirstPlayableDefinitions, validateRuntimeCoverage, CONTENT_COUNTS, FORM_BEHAVIORS } from './first-playable-content.mjs';
async function fixture() {
  const profile = JSON.parse(await fs.readFile('data/profiles/first-playable.json'));
  const records = [];
  for (const kind of Object.keys(CONTENT_COUNTS)) for (const file of await fs.readdir(`data/${kind}`)) {
    if (file.endsWith('.json')) records.push(JSON.parse(await fs.readFile(`data/${kind}/${file}`)));
  }
  const tuning = JSON.parse(await fs.readFile(`data/${profile.tuningFile}`));
  return { profile, records, tuning };
}
function evidence(profile) {
  const observations = [];
  const add = (ids, behaviors) => { for (const id of ids) for (const behavior of behaviors) observations.push({ id, behavior, count: 1, tick: 1, stateHash: 'a'.repeat(64) }); };
  add(profile.selection.weapons, ['activation', 'hit']); add(profile.selection.tools, ['activation', 'growth']);
  add(profile.runtime.charters, ['weapon', 'estate']); add(profile.runtime.items, ['condition-match', 'condition-miss']);
  add(profile.runtime.evolutions, ['unlock', 'contribution']); add(profile.selection.enemies, ['spawn']);
  add(profile.firstPlayable.mapEvents.map(e => e.id), ['spawn', 'claim']);
  for (const [id, shape] of Object.entries(profile.firstPlayable.weapons)) if (FORM_BEHAVIORS[shape.form]) add([id], [FORM_BEHAVIORS[shape.form]]);
  for (const [id, enemy] of Object.entries(profile.firstPlayable.enemies)) if (enemy.rank === 'boss') add([id], ['warning', 'defeat']);
  add(profile.firstPlayable.mapEvents.filter(e => e.kind === 'cart').map(e => e.id), ['broken', 'drop']);
  return { contractVersion: 1, profileId: profile.id, dataHash: 'b'.repeat(64), sourceHash: 'c'.repeat(64), profileHash: 'd'.repeat(64), observations };
}
test('actual selected definitions satisfy counts, genuine form diversity, targets and winter boss', async () => {
  const { profile, records, tuning } = await fixture();
  assert.deepEqual(validateFirstPlayableDefinitions(profile, records, tuning).counts, CONTENT_COUNTS);
  const duplicateForm = structuredClone(profile); const shapes = Object.values(duplicateForm.firstPlayable.weapons); shapes[1].form = shapes[0].form;
  assert.throws(() => validateFirstPlayableDefinitions(duplicateForm, records, tuning), /distinct executable/);
  const earlyBoss = structuredClone(profile); Object.values(earlyBoss.firstPlayable.enemies).find(e => e.rank === 'boss').firstSpawnTick = 0;
  assert.throws(() => validateFirstPlayableDefinitions(earlyBoss, records, tuning), /winter/);
  assert.throws(() => validateFirstPlayableDefinitions(profile, records, { ...tuning, durationTicks: 21600 }));
});
test('runtime coverage requires every content behavior and cannot use a directory count', async () => {
  const { profile } = await fixture(); const report = evidence(profile); const identity = { dataHash: report.dataHash, sourceHash: report.sourceHash, profileHash: report.profileHash };
  assert.equal(validateRuntimeCoverage(profile, report, identity).status, 'PASS');
  for (const key of ['dataHash', 'sourceHash', 'profileHash']) assert.throws(() => validateRuntimeCoverage(profile, { ...report, [key]: 'f'.repeat(64) }, identity), /stale or mismatched/);
  assert.throws(() => validateRuntimeCoverage(profile, report), /expected current/);
  assert.throws(() => validateRuntimeCoverage(profile, { ...report, observations: [] }, identity), /actual execution/);
  for (const behavior of ['hit', 'growth', 'weapon', 'estate', 'condition-match', 'condition-miss', 'unlock', 'contribution', 'spawn', 'claim', ...Object.values(FORM_BEHAVIORS), 'warning', 'defeat', 'broken', 'drop']) {
    const removed = report.observations.find(r => r.behavior === behavior);
    assert.throws(() => validateRuntimeCoverage(profile, { ...report, observations: report.observations.filter(r => r !== removed) }, identity), /unexecuted/);
  }
  for (const patch of [{ count: 0 }, { tick: -1 }, { stateHash: 'not-proof' }]) {
    assert.throws(() => validateRuntimeCoverage(profile, { ...report, observations: [{ ...report.observations[0], ...patch }, ...report.observations.slice(1)] }, identity));
  }
  assert.throws(() => validateRuntimeCoverage(profile, { ...report, profileId: 'other:profile' }, identity));
  assert.throws(() => validateRuntimeCoverage(profile, { ...report, observations: [...report.observations, report.observations[0]] }, identity), /duplicate/);
});
test('profile schema and semantic validation reject first playable overrides without weakening legacy scope', async () => {
  const os = await import('node:os'); const path = await import('node:path');
  const { validateContent } = await import('./validate-content.mjs');
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'first-playable-content-'));
  try {
    await fs.cp('data', root, { recursive: true });
    const file = path.join(root, 'profiles/first-playable.json'), original = JSON.parse(await fs.readFile(file));
    for (const mutate of [
      p => { p.firstPlayable.weapons[p.selection.weapons[1]].form = p.firstPlayable.weapons[p.selection.weapons[0]].form; },
      p => { p.runtimeOverrides.charters[p.runtime.charters[0]].effects[0].subject = 'fake-subject'; },
      p => { p.runtimeOverrides.charters[p.runtime.charters[0]].effects[0].conditions = [{ kind: 'equipment-owned', value: 'core:unselected', minimum: 0 }]; },
      p => { p.runtimeOverrides.charters['other:unselected'] = p.runtimeOverrides.charters[p.runtime.charters[0]]; },
      p => { p.tuningFile = '../first-playable-tuning.json'; },
      p => { delete p.firstPlayable; },
    ]) {
      const changed = structuredClone(original); mutate(changed); await fs.writeFile(file, JSON.stringify(changed));
      assert.equal((await validateContent(root, { fullPool: true })).valid, false);
    }
    await fs.writeFile(file, JSON.stringify(original));
    assert.equal((await validateContent(root, { fullPool: true })).valid, true);
  } finally { await fs.rm(root, { recursive: true, force: true }); }
});
