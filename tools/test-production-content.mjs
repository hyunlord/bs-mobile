import assert from 'node:assert/strict';
import { cp, mkdir, mkdtemp, readFile, rename, rm, symlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { validateContent } from './validate-content.mjs';
import { weaponDefinitionHash } from './weapon-definition-provenance.mjs';

async function fixture(t) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'production-content-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(new URL('../data/', import.meta.url), directory, { recursive: true });
  return directory;
}

test('canonical growth replaces numeric activation without changing its level one values', async t => {
  const directory = await fixture(t);
  const filename = path.join(directory, 'weapons/iron_blade.json');
  const weapon = JSON.parse(await readFile(filename));
  assert.equal(weapon.growth.levels.length, 12);
  for (const key of ['damage', 'range', 'cooldownTicks', 'knockback']) assert.equal(Object.hasOwn(weapon.activation, key), false);
  const result = await validateContent(directory);
  assert.equal(result.valid, true, result.errors.join('\n'));
});

test('canonical growth rejects a second numeric activation definition', async t => {
  const directory = await fixture(t);
  const filename = path.join(directory, 'weapons/iron_blade.json');
  const weapon = JSON.parse(await readFile(filename));
  weapon.activation.damage = 22;
  await writeFile(filename, JSON.stringify(weapon));
  assert.equal((await validateContent(directory)).valid, false);
});

test('production selects canonical tuning and contains the complete gameplay settings', async () => {
  const profile = JSON.parse(await readFile(new URL('../data/profiles/production.json', import.meta.url)));
  assert.equal(profile.tuningFile, undefined);
  assert.equal(profile.experiment, undefined);
  assert.equal(profile.weaponCombat.contractVersion, 2);
  assert.ok(profile.gameplay.enemyOverrides.length > 0);
  assert.ok(profile.gameplay.experiment.experience.quadratic > 0);
});

test('weapon definition provenance includes canonical rows beyond the manifest', async t => {
  const directory = await fixture(t);
  const profile = JSON.parse(await readFile(path.join(directory, 'profiles/weapon-growth-79.json')));
  const before = await weaponDefinitionHash(directory, profile);
  const filename = path.join(directory, 'weapons/iron_blade.json');
  const weapon = JSON.parse(await readFile(filename));
  weapon.growth.levels[0].damage++;
  await writeFile(filename, JSON.stringify(weapon));
  assert.notEqual(await weaponDefinitionHash(directory, profile), before);
});

test('weapon definition provenance resolves nested canonical records accepted by the loader', async t => {
  const directory = await fixture(t);
  const profile = JSON.parse(await readFile(path.join(directory, 'profiles/weapon-growth-79.json')));
  await mkdir(path.join(directory, 'weapons/melee'));
  await rename(path.join(directory, 'weapons/iron_blade.json'), path.join(directory, 'weapons/melee/iron_blade.json'));
  assert.equal((await validateContent(directory)).valid, true);
  assert.match(await weaponDefinitionHash(directory, profile), /^[a-f0-9]{64}$/);
});

for (const mutation of ['duplicate', 'symlink']) test(`weapon provenance rejects nested ${mutation} sources`, async t => {
  const directory = await fixture(t);
  const profile = JSON.parse(await readFile(path.join(directory, 'profiles/weapon-growth-79.json')));
  await mkdir(path.join(directory, 'weapons/melee'));
  const source = path.join(directory, 'weapons/iron_blade.json');
  const target = path.join(directory, 'weapons/melee/iron_blade.json');
  if (mutation === 'duplicate') await cp(source, target);
  else await symlink(source, target);
  await assert.rejects(weaponDefinitionHash(directory, profile), /duplicate|Symbolic/i);
});
