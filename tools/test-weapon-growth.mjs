import assert from 'node:assert/strict';
import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { validateContent } from './validate-content.mjs';

const root = new URL('../data/', import.meta.url);
async function fixture(t) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'weapon-growth-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(root, directory, { recursive: true });
  return directory;
}
const mutations = {
  'missing level': data => data.weapons['core:iron_blade'].levels.pop(),
  'duplicate level': data => { data.weapons['core:iron_blade'].levels[1].level = 1; },
  'damage regression': data => { data.weapons['core:iron_blade'].levels[1].damage = 1; },
  'cooldown regression': data => { data.weapons['core:iron_blade'].levels[1].cooldownTicks = 31; },
  'damage-only final row': data => { const rows = data.weapons['core:iron_blade'].levels; rows[11] = { ...rows[10], level: 12, damage: 88 }; },
  'unknown selected ID': data => { data.weapons['test:unknown'] = data.weapons['core:iron_blade']; },
  'missing selected ID': data => { delete data.weapons['core:iron_blade']; },
  'sector pierce': data => { data.weapons['core:iron_blade'].levels[0].pierce = 1; },
  'disk width': data => { data.weapons['core:ward_orbit'].beamHalfWidth = 1; },
  'ray zero width': data => { data.weapons['core:ember_wand'].beamHalfWidth = 0; },
  'excess target budget': data => { data.weapons['core:iron_blade'].levels[11].count = 65; },
  'fractional damage': data => { data.weapons['core:iron_blade'].levels[0].damage = 1.5; },
};
test('weapon candidate preserves baseline profile except ID and explicit opt-in', async () => {
  const base = JSON.parse(await readFile(new URL('profiles/s4b-02.json', root)));
  const candidate = JSON.parse(await readFile(new URL('profiles/weapon-growth-79.json', root)));
  delete candidate.weaponCombat; candidate.id = base.id;
  assert.deepEqual(candidate, base);
  assert.equal((await validateContent(root.pathname, { fullPool: true })).valid, true);
});
for (const [name, mutate] of Object.entries(mutations)) test(`weapon growth rejects ${name}`, async t => {
  const directory = await fixture(t), filename = path.join(directory, 'weapon-growth-79.json');
  const data = JSON.parse(await readFile(filename)); mutate(data);
  await writeFile(filename, JSON.stringify(data));
  assert.equal((await validateContent(directory)).valid, false);
});
for (const filename of ['../weapon-growth-79.json', 'missing.json', 'weapon-growth-absent.json']) test(`weapon profile rejects ${filename}`, async t => {
  const directory = await fixture(t), file = path.join(directory, 'profiles/weapon-growth-79.json');
  const data = JSON.parse(await readFile(file)); data.weaponCombat.definitionsFile = filename;
  await writeFile(file, JSON.stringify(data));
  assert.equal((await validateContent(directory)).valid, false);
});
test('weapon growth rejects duplicate JSON keys instead of last-key-wins', async t => {
  const directory = await fixture(t), file = path.join(directory, 'weapon-growth-79.json');
  await writeFile(file, (await readFile(file, 'utf8')).replace('"damage": 22', '"damage": 21, "damage": 22'));
  assert.equal((await validateContent(directory)).valid, false);
});
