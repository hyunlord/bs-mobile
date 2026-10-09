import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import Ajv from 'ajv/dist/2020.js';
const read = p => JSON.parse(fs.readFileSync(p, 'utf8'));
export function validateMeta(c, root = process.cwd(), dataRoot = path.join(root, 'data')) {
  const schema = new Ajv({ allErrors: true }).compile(read(path.join(dataRoot, 'schema/meta.schema.json')));
  assert.ok(schema(c), JSON.stringify(schema.errors));
  const p = read(path.join(dataRoot, 'profiles/first-playable.json'));
  const roles = new Set(read(path.join(root, 'unity/Assets/Art/first-playable-manifest.json')).roles.map(x => x.id));
  const materials = new Set(c.materials.map(x => x.id));
  const money = m => { for (const k of Object.keys(m)) assert.ok(materials.has(k), `material ${k}`); };
  const unique = a => assert.equal(new Set(a).size, a.length, 'duplicate ID');
  const ids = [...c.materials, ...c.chapters, ...c.manorBuildings, ...c.vassals, ...c.challenges].map(x => x.id);
  unique(ids); assert.ok(ids.every(id => /^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$/.test(id)), 'namespace IDs');
  for (const x of [...c.materials, ...c.manorBuildings, ...c.vassals, ...c.chapters.flatMap(x => x.terrain)]) assert.ok(roles.has(x.artRole), `art role ${x.artRole}`);
  assert.deepEqual(c.chapters.map(x => x.index).sort((a,b) => a-b), Array.from({length:10}, (_,i) => i+1));
  for (const ch of c.chapters) {
    unique(ch.enemyIds); assert.ok(ch.enemyIds.includes(ch.bossId)); assert.equal(p.firstPlayable.enemies[ch.bossId]?.rank, 'boss');
    for (const id of ch.enemyIds) assert.ok(p.selection.enemies.includes(id), `enemy ${id}`);
    assert.deepEqual([...new Set(ch.terrain.map(x => x.kind))].sort(), ['forest','hill','river']);
    for (const t of ch.terrain) assert.ok(t.widthPermille > 0 && t.heightPermille > 0 && t.xPermille+t.widthPermille <= 1000 && t.yPermille+t.heightPermille <= 1000, 'terrain bounds');
    for (const key of ['widthPermille','heightPermille','threatPermille','enemyHealthPermille','enemyDamagePermille']) assert.ok(ch[key] >= 100 && ch[key] <= 5000, key);
    assert.ok(ch.siteCount > 0 && ch.siteCount <= 100 && ch.siteSpacing > 0 && ch.siteSpacing <= 2000 && ch.farmCapacity > 0 && ch.farmCapacity <= 1000);
    money(ch.rewardCaps); assert.deepEqual(Object.keys(ch.rewardCaps).sort(), [...materials].sort()); assert.ok(Object.values(ch.rewardCaps).every(x => x > 0));
  }
  for (const m of c.materials) assert.ok(m.walletCap > 0);
  assert.deepEqual(c.manorBuildings.map(x => x.manorEffect).sort(), ['IdleCapacity','Research','VassalLevelCap','VassalSlots']);
  for (const b of c.manorBuildings) { money(b.baseCost); money(b.costPerLevel); assert.ok(b.maxLevel > 0 && b.maxLevel <= 20 && Object.values(b.baseCost).some(x => x > 0)); }
  const canonicalVassals = new Set(fs.readdirSync(path.join(dataRoot,'vassals')).filter(x => x.endsWith('.json')).map(x => read(path.join(dataRoot,'vassals',x)).id));
  assert.equal(c.vassals.filter(x => x.initiallyUnlocked).length, 1);
  assert.deepEqual(c.vassals.map(x => x.ability).sort(), ['Allies','Attack','Experience','Growth','Health','Movement']);
  for (const v of c.vassals) {
    assert.ok(canonicalVassals.has(v.id)); assert.equal(v.maxLevel,20); assert.ok(v.maxRank > 0 && v.maxRank <= 10); assert.equal(v.rankCosts.length,v.maxRank); assert.ok(v.fragmentCap > 0 && v.rankCosts.every(x => x > 0 && x <= v.fragmentCap));
    assert.ok([v.basePermille,v.perLevelPermille,v.perRankPermille].every(x => x <= 500) && v.basePermille+19*v.perLevelPermille+v.maxRank*v.perRankPermille <= 2000);
    money(v.levelCostBase); money(v.levelCostStep); assert.ok(Object.values(v.levelCostBase).some(x => x > 0));
  }
  const all = [...p.selection.weapons,...p.selection.tools,...p.runtime.items];
  const unlocked = [...c.initialContentIds,...c.challenges.flatMap(x => x.unlockContentIds)]; unique(unlocked); assert.deepEqual(unlocked.sort(), all.sort()); assert.ok(c.initialContentIds.length < all.length);
  const tuning = read(path.join(dataRoot,'first-playable-tuning.json'));
  assert.ok(c.initialContentIds.includes(tuning.world.progression.startingWeapon));
  for (const file of fs.readdirSync(path.join(dataRoot,'heroes'))) { const h=read(path.join(dataRoot,'heroes',file)); if(p.selection.heroes.includes(h.id)) assert.ok(c.initialContentIds.includes(h.startingTool)); }
  const research = c.manorBuildings.find(x => x.manorEffect === 'Research').maxLevel;
  for (const ch of c.challenges) { assert.ok(['Runs','Clears','HighestChapter','Kills','Harvests','Buildings','People','Bosses','SurvivalTicks','VassalLevel'].includes(ch.metric)); assert.ok(ch.target > 0 && ch.researchLevel <= research && ch.unlockContentIds.length+ch.unlockVassalIds.length > 0); money(ch.rewards); }
  const vUnlock=c.challenges.flatMap(x => x.unlockVassalIds); unique(vUnlock); assert.deepEqual(vUnlock.sort(),c.vassals.filter(x => !x.initiallyUnlocked).map(x => x.id).sort());
  const e=c.economy; assert.ok([e.repeatPermille,e.deathPermille,e.abandonPermille].every(x => x <= 1000)); assert.ok(e.minimumRewardTicks > 0 && e.minimumRewardTicks <= tuning.durationTicks);
  assert.ok(e.idleStepSeconds > 0 && e.idleStepSeconds <= e.baseIdleCapSeconds && e.baseIdleCapSeconds >= 7200 && e.maximumIdleCapSeconds <= 14400 && e.baseIdleCapSeconds <= e.maximumIdleCapSeconds && e.idleCapSecondsPerLevel <= e.maximumIdleCapSeconds && e.clockToleranceSeconds <= 300); money(e.idlePerStep);
  assert.ok(e.baseVassalLevelCap > 0 && e.baseVassalLevelCap <= 20 && e.levelCapPerForgeLevel > 0 && e.levelCapPerForgeLevel <= 20 && e.baseVassalSlots === 1 && e.maximumVassalSlots >= 1 && e.maximumVassalSlots <= 6 && e.barracksLevelsPerSlot > 0 && e.fragmentsPerClear <= 1000 && e.fragmentsPerDeath <= 1000);
  assert.deepEqual(e.rewardRules.map(x => x.materialId).sort(),[...materials].sort()); for(const r of e.rewardRules) assert.ok(Object.entries(r).filter(([k])=>k!=='materialId').every(([,v])=>v<=1000));
  return { chapters:c.chapters.length,vassals:c.vassals.length,challenges:c.challenges.length,initialContent:c.initialContentIds.length,fullContent:all.length };
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) { const root=path.resolve(process.argv[2] || '.'); console.log(JSON.stringify(validateMeta(read(path.join(root,'data/meta/progression.json')),root))); }
