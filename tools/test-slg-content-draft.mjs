import assert from 'node:assert/strict';
import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import Ajv2020 from 'ajv/dist/2020.js';
import { validateContent } from './validate-content.mjs';
import { validateSlgContentDraft } from './validate-slg-content-draft.mjs';

const dataRoot = new URL('../data/', import.meta.url);
const draftPath = 'opt-in/slg/content-draft-v1.json';
const draft = JSON.parse(await readFile(new URL(draftPath, dataRoot)));
const schema = JSON.parse(await readFile(new URL('opt-in/schema/slg-content-draft.schema.json', dataRoot)));
const validate = new Ajv2020({ allErrors: true, strict: true }).compile(schema);

test('SLG draft is schema-valid and semantically consistent without activation', () => {
  assert.equal(validate(draft), true, JSON.stringify(validate.errors));
  assert.deepEqual(validateSlgContentDraft(draft), []);
});
const schemaMutations = {
  'raised shared cap': value => { value.sharedCaps['run.heroDamagePercent'] = 11; },
  'unknown effect domain': value => { value.heroes[0].passive.effect.domain = 'run.unknown'; },
  'unknown effect unit': value => { value.heroes[0].passive.effect.unit = 'ratio'; },
  'runtime activation': value => { value.runtimeEnabled = true; },
  'missing hypothesis marker': value => { delete value.numericStatus; },
  'unrecognised field': value => { value.automaticallyLoad = true; },
  'missing troop': value => { value.troops.pop(); },
  'missing hero': value => { value.heroes.pop(); },
  'missing research node': value => { value.researchNodes.pop(); },
  'missing encounter': value => { value.worldEncounters.pop(); },
  'second research level': value => { value.researchNodes[0].level = 2; },
  'negative training time': value => { value.troops[0].training.secondsPerUnit = -1; },
  'negative reward': value => { value.worldEncounters[0].materialReward.grain = -1; },
};
for (const [label, mutate] of Object.entries(schemaMutations)) test(`SLG draft schema rejects ${label}`, () => {
  const value = structuredClone(draft);
  mutate(value);
  assert.equal(validate(value), false);
});
const semanticMutations = {
  'effect above cap': value => { value.heroes[0].passive.effect.value = 1000; },
  'wrong effect unit': value => { value.heroes[0].passive.effect.unit = 'seconds'; },
  'wrong effect scope': value => { value.heroes[0].passive.effect.scope = 'estate-map-only'; },
  'duplicate ability': value => { value.heroes[1].skill.id = value.heroes[0].skill.id; },
  'missing fallback proof': value => { value.contracts.personalFallback.proofIds.pop(); },
  'impossible objective time': value => { value.worldEncounters[0].objective.holdSeconds = 10000; },
  'unequal hero base power': value => { value.heroes[1].baseCombat.attackDamage += 1; },
  'duplicate ID': value => { value.troops[1].id = value.troops[0].id; },
  'dangling encounter': value => { value.heroes[0].relatedEncounterIds = ['slg:missing']; },
  'dangling research': value => { value.researchNodes[0].prerequisites.nodes = [{ id: 'slg:missing', level: 1 }]; },
  'research cycle': value => { value.researchNodes[0].prerequisites.nodes = [{ id: value.researchNodes[1].id, level: 1 }]; },
  'dangling proof': value => { value.worldEncounters[0].outcomes.researchProofIds = ['slg:missing']; },
  'excess reward': value => { value.worldEncounters[0].materialReward.grain = 100000; },
  'invalid troop recommendation': value => { value.worldEncounters[0].recommendedTroops = 0; },
};
for (const [label, mutate] of Object.entries(semanticMutations)) test(`SLG draft rejects ${label}`, () => {
  const value = structuredClone(draft);
  mutate(value);
  assert.notDeepEqual(validateSlgContentDraft(value), []);
});
test('SLG draft integration validates only the explicit opt-in path and rejects duplicate keys', async t => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'bs-slg-draft-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(dataRoot, directory, { recursive: true });
  assert.deepEqual((await validateContent(directory, { fullPool: true })).errors, []);
  const invalidReference = structuredClone(draft);
  invalidReference.worldEncounters[0].enemyReferences = ['core:missing_enemy'];
  await writeFile(path.join(directory, draftPath), JSON.stringify(invalidReference));
  assert.match((await validateContent(directory)).errors.join('\n'), /unresolved enemy reference/);
  await writeFile(path.join(directory, draftPath), JSON.stringify(draft, null, 2));
  const extra = path.join(directory, 'opt-in/slg/unregistered.json');
  await writeFile(extra, JSON.stringify(draft));
  assert.match((await validateContent(directory)).errors.join('\n'), /unregistered\.json: missing schema/);
  await rm(extra);
  const text = await readFile(path.join(directory, draftPath), 'utf8');
  await writeFile(path.join(directory, draftPath), text.replace('"runtimeEnabled": false', '"runtimeEnabled": true, "runtimeEnabled": false'));
  assert.match((await validateContent(directory)).errors.join('\n'), /duplicate JSON key runtimeEnabled/);
});
