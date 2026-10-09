import assert from 'node:assert/strict';
import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { validateContent } from './validate-content.mjs';

for (const [label, file, mutate] of [
  ['missing content kind', 'tools/seed_bag.json', value => { delete value.kind; }],
  ['wrong content kind', 'tools/seed_bag.json', value => { value.kind = 'weapon'; }],
  ['missing design trigger', 'tools/seed_bag.json', value => { if (value.effect) delete value.effect.trigger; }],
  ['missing evolution subtype', 'evolutions/sowing_sworddance.json', value => { delete value.evolutionKind; }],
  ['legacy subtype in common kind', 'evolutions/sowing_sworddance.json', value => { value.kind = 'weapon-tool'; }],
  ['wrong-kind linked ID', 'items/harvest_basket_lid.json', value => { value.linkedToolIds = ['core:frontier_knight']; }],
  ['name instead of linked ID', 'items/harvest_basket_lid.json', value => { value.linkedToolIds = ['씨앗 자루']; }],
]) {
  test(`normalized contract rejects ${label}`, async t => {
    const root = await mkdtemp(path.join(os.tmpdir(), 'bs-normalized-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    await cp('data', root, { recursive: true });
    const filename = path.join(root, file);
    const value = JSON.parse(await readFile(filename, 'utf8'));
    mutate(value);
    await writeFile(filename, JSON.stringify(value));
    const result = await validateContent(root, { fullPool: true });
    assert.equal(result.valid, false, `accepted ${label}`);
    assert.ok(result.errors.some(error => error.startsWith(file + ':')), result.errors.join('\n'));
  });
}
