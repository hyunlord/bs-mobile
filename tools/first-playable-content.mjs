import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { captureProvenance } from './diagnostic-provenance.mjs';
import { contentRecords } from './content-files.mjs';

export const CONTENT_COUNTS = { weapons: 10, tools: 8, charters: 8, items: 30, evolutions: 8, enemies: 13, heroes: 1, estates: 1 };
export const FORMS = ['sector90', 'sector180', 'projectile', 'volley', 'orbit', 'field', 'piercing', 'chain', 'boomerang', 'nova'];
export const FORM_BEHAVIORS = { chain: 'unique-jumps', piercing: 'pierce-budget', boomerang: 'return-hit', orbit: 'orbital-motion', field: 'lifetime' };
const list = value => Array.isArray(value) ? value : [];
const exactKeys = (actual, expected, label) => assert.deepEqual([...actual].sort(), [...expected].sort(), label);

// This gate consumes definitions and observed executions separately. A selected ID alone is never runtime proof.
export function validateFirstPlayableDefinitions(profile, records, tuning) {
  const byId = new Map(records.map(r => [r.id, r]));
  const selected = { ...profile.selection, ...Object.fromEntries(['charters', 'items', 'evolutions'].map(k => [k, profile.runtime?.[k]])) };
  for (const [kind, count] of Object.entries(CONTENT_COUNTS)) {
    assert.equal(list(selected[kind]).length, count, `first playable ${kind} count`);
    assert.equal(new Set(selected[kind]).size, count, `duplicate ${kind}`);
    for (const id of selected[kind]) assert.ok(byId.has(id), `missing selected definition ${id}`);
  }
  const fp = profile.firstPlayable;
  assert.equal(fp?.contractVersion, 1, 'first playable contract version');
  assert.equal(tuning.tickRate, 30); assert.equal(tuning.durationTicks, 27000);
  assert.equal(tuning.world.seasons.length, 4);
  assert.equal(tuning.world.seasons.reduce((sum, s) => sum + s.durationTicks, 0), 27000);
  exactKeys(Object.keys(fp.weapons), selected.weapons, 'weapon shape coverage');
  exactKeys(Object.values(fp.weapons).map(w => w.form), FORMS, 'ten distinct executable weapon forms');
  for (const id of selected.weapons) {
    const growth = byId.get(id).growth;
    assert.equal(growth?.levels?.length, 12, `${id} twelve growth levels`);
    assert.deepEqual(growth.levels.map(r => r.level), Array.from({ length: 12 }, (_, i) => i + 1));
  }
  const targets = selected.tools.map(id => byId.get(id).growth?.target);
  exactKeys(targets, ['land', 'land', 'land', 'building', 'building', 'building', 'people', 'people'], 'tool domain split');
  for (const id of selected.tools) assert.ok(byId.get(id).activation && byId.get(id).growth, `${id} requires activation and growth`);
  exactKeys(Object.keys(fp.enemies), selected.enemies, 'enemy schedule coverage');
  const ranks = Object.values(fp.enemies).map(e => e.rank);
  exactKeys(ranks, [...Array(10).fill('normal'), 'elite', 'elite', 'boss'], 'enemy rank counts');
  const boss = Object.values(fp.enemies).find(e => e.rank === 'boss');
  const winterStart = tuning.world.seasons.slice(0, -1).reduce((sum, s) => sum + s.durationTicks, 0);
  assert.ok(boss.firstSpawnTick >= winterStart && boss.firstSpawnTick < tuning.durationTicks && boss.repeatTicks === 0, 'boss must arrive once during winter');
  exactKeys(fp.mapEvents.map(e => e.kind), ['merchant', 'shrine', 'cart'], 'three distinct events');
  assert.equal(new Set(fp.mapEvents.map(e => e.id)).size, 3, 'event IDs unique');
  exactKeys(Object.keys(fp.evolutionRequirements), selected.evolutions, 'evolution level requirement coverage');
  assert.ok(selected.evolutions.filter(id => byId.get(id).evolutionKind === 'weapon-tool').length >= 4, 'four weapon-tool evolutions');
  for (const id of selected.evolutions) {
    const definition = byId.get(id), requirements = fp.evolutionRequirements[id];
    exactKeys(requirements.map(r => r.equipmentId), definition.inputIds, `${id} exact evolution inputs`);
    for (const r of requirements) assert.ok(Number.isSafeInteger(r.minimumLevel) && r.minimumLevel > 0 && r.minimumLevel <= 12, `${id} valid required level`);
  }
  return { selected, counts: CONTENT_COUNTS };
}

export function validateRuntimeCoverage(profile, evidence, expectedIdentity) {
  assert.equal(evidence.contractVersion, 1, 'runtime evidence version');
  assert.equal(evidence.profileId, profile.id, 'runtime profile identity');
  for (const key of ['dataHash', 'sourceHash', 'profileHash']) {
    assert.match(evidence[key], /^[a-f0-9]{64}$/i, `coverage ${key}`);
    assert.match(expectedIdentity?.[key], /^[a-f0-9]{64}$/i, `expected current ${key} required`);
    assert.equal(evidence[key].toLowerCase(), expectedIdentity[key].toLowerCase(), `stale or mismatched coverage ${key}`);
  }
  assert.ok(Array.isArray(evidence.observations) && evidence.observations.length > 0, 'actual execution observations required');
  const observed = new Map();
  for (const row of evidence.observations) {
    assert.equal(typeof row.id, 'string'); assert.equal(typeof row.behavior, 'string');
    assert.ok(Number.isSafeInteger(row.count) && row.count > 0, 'positive observed count required');
    assert.ok(Number.isSafeInteger(row.tick) && row.tick >= 0 && row.tick <= 27000, 'execution tick required');
    assert.match(row.stateHash, /^[a-f0-9]{64}$/i, 'executed state hash required');
    const key = `${row.id}\0${row.behavior}`;
    assert.ok(!observed.has(key), `duplicate coverage observation ${row.id}/${row.behavior}`); observed.set(key, row);
  }
  const requireBehavior = (id, behavior) => assert.ok(observed.has(`${id}\0${behavior}`), `unexecuted ${id}/${behavior}`);
  for (const id of profile.selection.weapons) for (const behavior of ['activation', 'hit']) requireBehavior(id, behavior);
  for (const [id, shape] of Object.entries(profile.firstPlayable.weapons)) if (FORM_BEHAVIORS[shape.form]) requireBehavior(id, FORM_BEHAVIORS[shape.form]);
  for (const id of profile.selection.tools) for (const behavior of ['activation', 'growth']) requireBehavior(id, behavior);
  for (const id of profile.runtime.charters) for (const behavior of ['weapon', 'estate']) requireBehavior(id, behavior);
  for (const id of profile.runtime.items) for (const behavior of ['condition-match', 'condition-miss']) requireBehavior(id, behavior);
  for (const id of profile.runtime.evolutions) for (const behavior of ['unlock', 'contribution']) requireBehavior(id, behavior);
  for (const id of profile.selection.enemies) requireBehavior(id, 'spawn');
  for (const [id, enemy] of Object.entries(profile.firstPlayable.enemies)) if (enemy.rank === 'boss') for (const behavior of ['warning', 'defeat']) requireBehavior(id, behavior);
  for (const event of profile.firstPlayable.mapEvents) for (const behavior of ['spawn', 'claim']) requireBehavior(event.id, behavior);
  for (const event of profile.firstPlayable.mapEvents.filter(e => e.kind === 'cart')) for (const behavior of ['broken', 'drop']) requireBehavior(event.id, behavior);
  return { status: 'PASS', observedBehaviors: observed.size };
}

export async function check(dataDirectory, evidenceFile) {
  const root = path.resolve(dataDirectory);
  const profile = JSON.parse(await fs.readFile(path.join(root, 'profiles/first-playable.json'), 'utf8'));
  const records = [];
  for (const directory of Object.keys(CONTENT_COUNTS)) {
    for (const { record } of (await contentRecords(path.join(root, directory))).values()) records.push(record);
  }
  const tuning = JSON.parse(await fs.readFile(path.join(root, profile.tuningFile), 'utf8'));
  const definitions = validateFirstPlayableDefinitions(profile, records, tuning);
  let execution = { status: 'DEFINITIONS_ONLY_NOT_RUNTIME_PROOF' };
  if (evidenceFile) {
    assert.equal(root, path.resolve('data'), 'runtime proof requires canonical repository data root');
    const provenance = await captureProvenance();
    const identity = { sourceHash: provenance.common.sourceTreeSha256, dataHash: provenance.cohorts.r3.contentSha256,
      profileHash: createHash('sha256').update(await fs.readFile(path.join(root, 'profiles/first-playable.json'))).digest('hex') };
    execution = validateRuntimeCoverage(profile, JSON.parse(await fs.readFile(evidenceFile, 'utf8')), identity);
  }
  return { counts: definitions.counts, execution };
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  check(process.argv[2] ?? 'data', process.argv[3]).then(result => console.log(JSON.stringify(result, null, 2))).catch(error => { console.error(error.message); process.exitCode = 1; });
}
