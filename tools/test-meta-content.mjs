import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { validateMeta } from './meta-content.mjs';
const fixture = () => JSON.parse(fs.readFileSync('data/meta/progression.json','utf8'));
test('canonical progression connects all first-playable content', () => assert.equal(validateMeta(fixture()).fullContent,48));
for(const [label,mutate] of [
  ['missing field',c=>delete c.economy.repeatPermille],
  ['invalid namespace ID',c=>c.chapters[0].id='not-a-namespace'],
  ['duplicate chapter',c=>c.chapters[1].index=1],
  ['wrong boss rank',c=>c.chapters[0].bossId='core:raider'],
  ['missing art',c=>c.vassals[0].artRole='not-art'],
  ['unreachable research',c=>c.challenges[0].researchLevel=999],
  ['missing unlock',c=>c.challenges[0].unlockContentIds=[]],
  ['unknown resource',c=>c.economy.idlePerStep.bad=1],
  ['uncapped idle',c=>c.economy.maximumIdleCapSeconds=999999],
  ['terrain outside map',c=>c.chapters[0].terrain[0].widthPermille=1001],
  ['overflow idle increment',c=>c.economy.idleCapSecondsPerLevel=2147483647],
  ['overflow forge increment',c=>c.economy.levelCapPerForgeLevel=2147483647],
  ['zero rank cost',c=>c.vassals[0].rankCosts[0]=0],
]) test(label,()=>{const c=fixture();mutate(c);assert.throws(()=>validateMeta(c));});

test('global validator discovers meta and enforces its cross references', async t => {
  const { mkdtemp, cp, rm, writeFile } = await import('node:fs/promises');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const { validateContent } = await import('./validate-content.mjs');
  const root = await mkdtemp(join(tmpdir(), 'bs-meta-content-'));
  t.after(() => rm(root, {recursive:true,force:true}));
  await cp('data', root, {recursive:true});
  const valid = await validateContent(root, {fullPool:true});
  assert.equal(valid.valid, true, valid.errors.join('\n'));
  const invalid = fixture(); invalid.chapters[0].bossId = 'core:raider';
  await writeFile(join(root,'meta/progression.json'), JSON.stringify(invalid));
  const result = await validateContent(root, {fullPool:true});
  assert.equal(result.valid, false);
  assert.ok(result.errors.some(e => e.includes('meta validation')));
});
