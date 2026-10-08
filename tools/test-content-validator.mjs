import assert from 'node:assert/strict';
import { cp, mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { validateContent } from './validate-content.mjs';

const id = { type: 'string', pattern: '^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$' };
const objectSchema = (properties, required = Object.keys(properties)) => ({
  $schema: 'https://json-schema.org/draft/2020-12/schema',
  type: 'object', properties, required, additionalProperties: false,
});
const positive = { type: 'integer', minimum: 1 };
const toolSchema = objectSchema({
  id,
  activation: { type: 'object', properties: { damage: positive }, required: ['damage'], additionalProperties: false },
  growth: { type: 'object', properties: { yield: positive }, required: ['yield'], additionalProperties: false },
  antiSynergy: { type: 'array', items: id },
});
const initialFiles = {
  'schema/tool.schema.json': toolSchema,
  'schema/hero.schema.json': objectSchema({ id, startingTool: id }),
  'schema/estate.schema.json': objectSchema({ id, startingTool: id }),
  'schema/tuning.schema.json': objectSchema({ defaultHero: id, defaultEstate: id }),
  'tools/seed.json': { id: 'core:seed', activation: { damage: 1 }, growth: { yield: 1 }, antiSynergy: [] },
  'heroes/lord.json': { id: 'core:lord', startingTool: 'core:seed' },
  'estates/farm.json': { id: 'core:farm', startingTool: 'core:seed' },
  'tuning.json': { defaultHero: 'core:lord', defaultEstate: 'core:farm' },
};

async function fixture(t) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'bs-content-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  async function write(relative, value) {
    await mkdir(path.dirname(path.join(directory, relative)), { recursive: true });
    await writeFile(path.join(directory, relative), `${JSON.stringify(value)}\n`);
  }
  for (const [relative, value] of Object.entries(initialFiles)) await write(relative, value);
  async function change(relative, mutate) {
    const value = JSON.parse(await readFile(path.join(directory, relative), 'utf8'));
    mutate(value);
    await write(relative, value);
  }
  return { directory, write, change };
}

test('valid schema and cross-record references pass', async (t) => {
  const { directory } = await fixture(t);
  assert.deepEqual(await validateContent(directory), { valid: true, schemas: 4, records: 4, errors: [] });
});

const mutations = [
  ['damage below minimum', 'tools/seed.json', (v) => { v.activation.damage = -1; }, /schema violation/],
  ['unknown field', 'tools/seed.json', (v) => { v.accidental = true; }, /additional properties/],
  ['missing growth', 'tools/seed.json', (v) => { delete v.growth; }, /requires both activation and growth/],
  ['missing activation', 'tools/seed.json', (v) => { delete v.activation; }, /requires both activation and growth/],
  ['wrong scalar type', 'tools/seed.json', (v) => { v.activation.damage = '1'; }, /must be integer/],
  ['invalid namespace', 'tools/seed.json', (v) => { v.id = 'Seed'; }, /invalid namespace ID/],
  ['unresolved hero tool', 'heroes/lord.json', (v) => { v.startingTool = 'core:absent'; }, /unresolved tool reference/],
  ['wrong-kind reference', 'heroes/lord.json', (v) => { v.startingTool = 'core:farm'; }, /unresolved tool reference/],
  ['unresolved estate tool', 'estates/farm.json', (v) => { v.startingTool = 'core:absent'; }, /unresolved tool reference/],
  ['unresolved tuning hero', 'tuning.json', (v) => { v.defaultHero = 'core:absent'; }, /unresolved hero reference/],
  ['unresolved anti-synergy', 'tools/seed.json', (v) => { v.antiSynergy = ['core:absent']; }, /unresolved tool reference/],
  ['invalid schema', 'schema/tool.schema.json', (v) => { v.properties.id.type = 'not-a-json-schema-type'; }, /invalid schema/],
  ['undeclared schema draft', 'schema/tool.schema.json', (v) => { delete v.$schema; }, /Draft 2020-12/],
  ['unknown schema keyword', 'schema/tool.schema.json', (v) => { v.misspelledRule = true; }, /cannot compile schema/],
];
for (const [name, relative, mutate, expected] of mutations) {
  test(`rejects ${name}`, async (t) => {
    const { directory, change } = await fixture(t);
    await change(relative, mutate);
    const result = await validateContent(directory);
    assert.equal(result.valid, false);
    assert.match(result.errors.join('\n'), expected);
  });
}

test('recursively checks test content and global duplicate IDs', async (t) => {
  const { directory, write } = await fixture(t);
  await write('test/tools/nested/duplicate.json', initialFiles['tools/seed.json']);
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /duplicate ID core:seed/);
});

test('rejects content without matching schema', async (t) => {
  const { directory, write } = await fixture(t);
  await write('weapons/blade.json', { id: 'core:blade' });
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /missing schema/);
});

test('skin numbers fail even if schema permits arbitrary fields', async (t) => {
  const { directory, write } = await fixture(t);
  await write('schema/skin.schema.json', { ...objectSchema({ id }, ['id']), additionalProperties: true });
  await write('skins/robe.json', { id: 'core:robe', nested: { damage: 9 } });
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /skin must not contain numeric stats/);
});

test('valid Draft2020 unevaluatedProperties is enforced', async (t) => {
  const { directory, write } = await fixture(t);
  await write('schema/skin.schema.json', {
    $schema: 'https://json-schema.org/draft/2020-12/schema', type: 'object',
    allOf: [{ properties: { id }, required: ['id'] }], unevaluatedProperties: false,
  });
  await write('skins/robe.json', { id: 'core:robe', mystery: 'unapproved' });
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /unevaluated properties/);
});

test('rejects malformed JSON instead of silently skipping it', async (t) => {
  const { directory } = await fixture(t);
  await writeFile(path.join(directory, 'tools/broken.json'), '{');
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /invalid JSON/);
});

const worldMutations = [
  ['season duration mismatch', (v) => { v.world.seasons[0].durationTicks += 1; }, /season durations must sum/],
  ['missing world numeric tuning', (v) => { delete v.world.people.consumePeriodTicks; }, /consumePeriodTicks/],
  ['unresolved starting weapon', (v) => { v.world.progression.startingWeapon = 'core:missing'; }, /unresolved weapon reference/],
  ['excessive load', (v) => { v.world.load.enemies = v.world.threat.enemyCap + 1; }, /load.enemies exceeds capacity/],
  ['map-incompatible spawn inset', (v) => { v.world.threat.spawnInset = v.world.map.width; }, /spawn inset exceeds map bounds/],
  ['unsupported people rule', (v) => { v.world.defaultPeopleRule = 'D'; }, /schema violation/],
  ['wrong card count', (v) => { v.world.progression.cardCount = 2; }, /schema violation/],
  ['duplicate rarity names', (v) => { v.world.progression.rarities[1].name = v.world.progression.rarities[0].name; }, /rarity names must be unique/],
  ['oversized squad', (v) => { v.world.people.squadSize = v.world.people.maxPeople + 1; }, /squad size exceeds people capacity/],
];
for (const [name, mutate, expected] of worldMutations) {
  test(`S2 rejects ${name}`, async (t) => {
    const directory = await mkdtemp(path.join(os.tmpdir(), 'bs-world-schema-'));
    t.after(() => rm(directory, { recursive: true, force: true }));
    await cp(fileURLToPath(new URL('../data', import.meta.url)), directory, { recursive: true });
    const tuningPath = path.join(directory, 'tuning.json');
    const tuning = JSON.parse(await readFile(tuningPath, 'utf8'));
    mutate(tuning);
    await writeFile(tuningPath, JSON.stringify(tuning));
    const result = await validateContent(directory);
    assert.equal(result.valid, false);
    assert.match(result.errors.join('\n'), expected);
  });
}

const s3Mutations = [
  ['missing canonical item', 'items', null, /Canonical item count/],

  ['unknown estate loop stage', 'tools/seed_bag.json', v=>{v.loopLinks[0].stage='absent';}, /unresolved estate loop stage|schema violation/],
  ['candidate profile activation', 'tools/seed_bag.json', v=>{v.designStatus='candidate';}, /candidate cannot enter runtime/],
  ['profile wrong kind', 'profiles/s2-baseline.json', v=>{v.selection.tools[0]='core:founder';}, /unresolved tool reference/],
  ['profile test leakage', 'profiles/s2-baseline.json', v=>{v.selection.tools[0]='test:spade';}, /test selection boundary/],
  ['missing anti-synergy rationale', 'tools/seed_bag.json', v=>{v.antiSynergyNotes=[];}, /notes must match/],
  ['hero affinity wrong kind', 'heroes/founder.json', v=>{v.affinityEstateIds=['core:seed_bag'];}, /unresolved estate reference/],
];
for (const [name, relative, mutate, expected] of s3Mutations.filter(row=>row[3])) {
  test(`S3 rejects ${name}`, async t=> {
    const directory=await mkdtemp(path.join(os.tmpdir(),'bs-s3-'));
    t.after(()=>rm(directory,{recursive:true,force:true}));
    await cp(fileURLToPath(new URL('../data',import.meta.url)),directory,{recursive:true});
    const filename=path.join(directory,relative);
    if(mutate) {
      const value=JSON.parse(await readFile(filename,'utf8')); mutate(value);
      await writeFile(filename,JSON.stringify(value));
    } else {
      const {readdir}=await import('node:fs/promises');
      await rm(path.join(filename,(await readdir(filename))[0]));
    }
    const result=await validateContent(directory,{fullPool:true});
    assert.equal(result.valid,false); assert.match(result.errors.join('\n'),expected);
  });
}

test('S3 canonical pool passes all semantic gates',async()=>{
  const result=await validateContent(fileURLToPath(new URL('../data',import.meta.url)),{fullPool:true});
  assert.deepEqual(result.errors,[]);
});

const poolMutations = [
  ['candidate pretending implemented',async root=>mutateFirst(root,'tools',v=>{v.designStatus='s2-runtime';}),/runtime status must match/],
  ['missing profile enemy',async root=>mutateFirst(root,'profiles',v=>{v.selection.enemies.pop();}),/baseline profile selection.enemies/],
  ['missing test profile hero',async root=>mutateFirst(root,'profiles',v=>{v.testSelection.heroes=[];}),/baseline profile testSelection.heroes/],
  ['test metadata orphan',async root=>mutateFirst(root,'test/heroes',v=>{v.affinityEstateIds=['core:missing'];}),/unresolved estate reference/],
  ['reordered duplicate evolution',async root=>{
    const entries=(await allRecords(path.join(root,'evolutions'))).filter(([,v])=>v.kind==='tool-growth');
    const [,first]=entries[0];const [file,second]=entries[1];
    second.inputIds=first.inputIds;second.result.baseId=first.result.baseId;
    second.growthCondition={minimum:first.growthCondition.minimum,state:first.growthCondition.state,target:first.growthCondition.target};
    await writeFile(file,JSON.stringify(second));
  },/duplicate evolution recipe/],
  ['sparse tag coverage', async root=>{
    const files=await allRecords(root);
    for(const [file,value] of files) if(Array.isArray(value.tags)) {
      value.tags=value.tags.filter(tag=>tag!=='economy'); await writeFile(file,JSON.stringify(value));
    }
    const file=path.join(root,'tools/seed_bag.json'); const value=JSON.parse(await readFile(file,'utf8'));
    value.tags.push('economy'); await writeFile(file,JSON.stringify(value));
  },/Tag economy needs at least 3/],
  ['evolution input kind', async root=>mutateFirst(root,'evolutions',v=>{v.inputIds[0]='core:founder';}),/unresolved (weapon|tool) reference/],
  ['evolution result base', async root=>mutateFirst(root,'evolutions',v=>{v.result.baseId='core:founder';}),/result base must be an input/],
  ['vassal owner kind', async root=>mutateFirst(root,'vassals',v=>{v.heroId='core:seed_bag';}),/unresolved hero reference/],
  ['item tool reference', async root=>mutateFirst(root,'items',v=>{v.linkedToolIds=['core:founder'];}),/unresolved tool reference/],
  ['skin numeric metadata', async root=>mutateFirst(root,'skins',v=>{v.stats={damage:1};}),/skin must not contain numeric stats/],
  ['insufficient anti pairs',async root=>{
    for(const [file,value] of await allRecords(path.join(root,'tools'))) {
      value.antiSynergy=[];value.antiSynergyNotes=[];await writeFile(file,JSON.stringify(value));
    }
  },/At least 8 distinct anti-synergy/],
  ['insufficient loop links',async root=>{
    for(const kind of ['tools','weapons','items']) for(const [file,value] of await allRecords(path.join(root,kind))) {
      value.loopLinks=[];await writeFile(file,JSON.stringify(value));
    }
  },/At least 15 base-loop-linked/],
];
async function allRecords(root) {
  const {readdir}=await import('node:fs/promises'); const result=[];
  for(const entry of await readdir(root,{withFileTypes:true})) {
    const file=path.join(root,entry.name);
    if(entry.isDirectory()) result.push(...await allRecords(file));
    else if(entry.name.endsWith('.json')) result.push([file,JSON.parse(await readFile(file,'utf8'))]);
  }
  return result;
}
async function mutateFirst(root,kind,mutate) {
  const [file,value]=(await allRecords(path.join(root,kind)))[0];mutate(value);await writeFile(file,JSON.stringify(value));
}
for(const [name,mutate,expected] of poolMutations) test(`S3 rejects ${name}`,async t=>{
  const directory=await mkdtemp(path.join(os.tmpdir(),'bs-s3-gate-'));
  t.after(()=>rm(directory,{recursive:true,force:true}));
  await cp(fileURLToPath(new URL('../data',import.meta.url)),directory,{recursive:true});
  await mutate(directory); const result=await validateContent(directory,{fullPool:true});
  assert.equal(result.valid,false);assert.match(result.errors.join('\n'),expected);
});

for (const candidate of [false, true]) test(`additional subset profile ${candidate ? 'rejects candidate activation' : 'accepts implemented subset'}`, async t => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'bs-subset-profile-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(fileURLToPath(new URL('../data', import.meta.url)), directory, { recursive: true });
  const profile = JSON.parse(await readFile(path.join(directory, 'profiles/s2-baseline.json'), 'utf8'));
  profile.id = 'core:smoke_subset';
  profile.name = '구현된 콘텐츠 부분집합';
  profile.selection.weapons = ['core:iron_blade'];
  profile.selection.tools = candidate ? ['core:bee_hive'] : ['core:seed_bag', 'core:carpenter_hammer'];
  profile.selection.enemies = ['core:raider'];
  for (const kind of Object.keys(profile.testSelection)) profile.testSelection[kind] = [];
  await writeFile(path.join(directory, 'profiles/smoke-subset.json'), JSON.stringify(profile));
  const result = await validateContent(directory, { fullPool: true });
  if (candidate) {
    assert.equal(result.valid, false);
    assert.match(result.errors.join('\n'), /candidate cannot enter runtime profile core:bee_hive/);
  } else {
    assert.equal(result.valid, true, result.errors.join('\n'));
  }
});

test('additional profile can activate a new generic implemented test tool without changing baseline', async t => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'bs-profile-extension-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(fileURLToPath(new URL('../data', import.meta.url)), directory, { recursive: true });
  const baselinePath = path.join(directory, 'profiles/s2-baseline.json');
  const baselineText = await readFile(baselinePath, 'utf8');
  const profile = JSON.parse(baselineText);
  const tool = JSON.parse(await readFile(path.join(directory, 'test/tools/test_spade.json'), 'utf8'));
  tool.id = 'test:extension_spade';
  tool.name = '추가 프로필 검증 삽';
  await writeFile(path.join(directory, 'test/tools/extension_spade.json'), JSON.stringify(tool));
  profile.id = 'test:extension_profile';
  profile.name = '기준 집합을 보존하는 확장';
  for (const kind of Object.keys(profile.testSelection)) profile.testSelection[kind] = [];
  profile.testSelection.tools = [tool.id];
  await writeFile(path.join(directory, 'profiles/extension.json'), JSON.stringify(profile));
  const result = await validateContent(directory, { fullPool: true });
  assert.equal(result.valid, true, result.errors.join('\n'));
  assert.equal(await readFile(baselinePath, 'utf8'), baselineText);
});

const runtimeMutations = [
  ['unsupported pulse attribution', 'charters/guarded_harvest.json', v=>{v.runtimeProjection.effects[0].subject='tool-growth';}, /unsupported runtime subject/],
  ['unsupported modifier cost', 'charters/guarded_harvest.json', v=>{v.runtimeProjection.effects[1].foodCost=1;}, /modifier food cost is unsupported/],
  ['missing projection', 'charters/guarded_harvest.json', v=>{delete v.runtimeProjection;}, /requires runtime projection|requires implemented projection/],
  ['unknown operation', 'charters/guarded_harvest.json', v=>{v.runtimeProjection.effects[0].operation='eval';}, /schema violation/],
  ['wrong operation subject', 'charters/guarded_harvest.json', v=>{v.runtimeProjection.effects[0].subject='random-object';}, /unsupported runtime subject/],
  ['zero effect', 'charters/guarded_harvest.json', v=>{v.runtimeProjection.effects[0].amount=0;}, /invalid effect amount/],
  ['duplicate effect identity', 'charters/guarded_harvest.json', v=>{v.runtimeProjection.effects[1].id=v.runtimeProjection.effects[0].id;}, /duplicate runtime effect ID/],
  ['wrong trigger', 'charters/guarded_harvest.json', v=>{v.runtimeProjection.effects[0].trigger='modifier';}, /modifier trigger mismatch/],
  ['missing mixed secondary target', 'tools/hedge_drum.json', v=>{v.runtimeProjection.growthActions[1].target='building';}, /duplicate growth target|unsupported growth action/],
  ['unselected effect equipment', 'items/meadow_buckle.json', v=>{v.runtimeProjection.effects[0].conditions.push({kind:'equipment-id',value:'core:absent',minimum:0});}, /invalid effect equipment reference|outside selected profile/],
  ['empty timed effect', 'charters/meal_oath.json', v=>{v.runtimeProjection.effects[0].durationTicks=0;}, /timed effect requires duration/],
  ['unknown condition tag', 'items/meadow_buckle.json', v=>{v.runtimeProjection.effects[0].conditions.find(c=>c.kind==='equipment-tag').value='absent-tag';}, /condition tag outside/],
  ['invalid condition enum', 'items/meadow_buckle.json', v=>{v.runtimeProjection.effects[0].conditions.find(c=>c.kind==='enemy-target').value='building-plus';}, /invalid condition value/],
  ['duplicate loot source', 'profiles/s4-stage-one.json', v=>{v.runtime.tuning.lootSources[1].id=v.runtime.tuning.lootSources[0].id;}, /duplicate loot channel/],
  ['missing selected evolution input', 'profiles/s4-stage-one.json', v=>{v.selection.tools[0]='core:muster_horn';}, /evolution input outside|duplicate profile selection/],
];
for(const [name,relative,mutate,expected] of runtimeMutations) test(`S4 rejects ${name}`,async t=>{
  const directory=await mkdtemp(path.join(os.tmpdir(),'bs-s4-schema-'));
  t.after(()=>rm(directory,{recursive:true,force:true}));
  await cp(fileURLToPath(new URL('../data',import.meta.url)),directory,{recursive:true});
  const filename=path.join(directory,relative);const value=JSON.parse(await readFile(filename,'utf8'));mutate(value);
  await writeFile(filename,JSON.stringify(value));
  const result=await validateContent(directory,{fullPool:true});
  assert.equal(result.valid,false);assert.match(result.errors.join('\n'),expected);
});

async function experimentFixture(t) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'bs-experiment-schema-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(fileURLToPath(new URL('../data', import.meta.url)), directory, { recursive: true });
  const tuning = JSON.parse(await readFile(path.join(directory, 'tuning.json'), 'utf8'));
  const profile = JSON.parse(await readFile(path.join(directory, 'profiles/s4-stage-one.json'), 'utf8'));
  profile.id = 'test:experiment_validation';
  profile.experiment = { contractVersion: 1, tuningFile: 'tuning-s4b-validation.json' };
  const wrapper = {
    tuning,
    enemyOverrides: profile.selection.enemies.map(id => ({ id, speed: 1, damage: 1, health: 1, attackCooldownTicks: 1 })),
    experiment: {
      movement: { circuitOffsets: [{x: -1, y: 0}, {x: 1, y: 0}], decisionPeriodTicks: 30, evadeRange: 10, evadeStep: 1 },
      experience: { base: 1, linear: 0, quadratic: 1 },
      mixedCategoryOrder: ['weapon', 'land', 'building', 'people'],
    },
  };
  async function save() {
    await writeFile(path.join(directory, 'profiles/experiment-validation.json'), JSON.stringify(profile));
    await writeFile(path.join(directory, 'tuning-s4b-validation.json'), JSON.stringify(wrapper));
  }
  return { directory, wrapper, profile, save };
}
test('optional experiment wrapper validates without changing legacy tuning or profiles', async t => {
  const f = await experimentFixture(t);
  const before = await readFile(path.join(f.directory, 'tuning.json'), 'utf8');
  await f.save();
  const result = await validateContent(f.directory, { fullPool: true });
  assert.equal(result.valid, true, result.errors.join('\n'));
  assert.equal(await readFile(path.join(f.directory, 'tuning.json'), 'utf8'), before);
});
const experimentMutations = [
  ['unsafe parent path', f => { f.profile.experiment.tuningFile = '../tuning-s4b-validation.json'; }, /schema violation|safe tuning/],
  ['absolute path', f => { f.profile.experiment.tuningFile = '/tmp/tuning-s4b-validation.json'; }, /schema violation|safe tuning/],
  ['Windows path', f => { f.profile.experiment.tuningFile = 'dir\\tuning-s4b-validation.json'; }, /schema violation|safe tuning/],
  ['wrong contract', f => { f.profile.experiment.contractVersion = 2; }, /schema violation/],
  ['missing file', f => { f.profile.experiment.tuningFile = 'tuning-s4b-absent.json'; }, /missing experiment tuning/],
  ['unknown override ID', f => { f.wrapper.enemyOverrides[0].id = 'core:absent'; }, /unresolved enemy|override IDs/],
  ['duplicate override', f => { f.wrapper.enemyOverrides.push(f.wrapper.enemyOverrides[0]); }, /duplicate enemy override|override IDs/],
  ['missing selected enemy', f => { f.wrapper.enemyOverrides.pop(); }, /override IDs/],
  ['zero enemy stat', f => { f.wrapper.enemyOverrides[0].health = 0; }, /schema violation/],
  ['oversized enemy stat', f => { f.wrapper.enemyOverrides[0].speed = 1_000_001; }, /schema violation/],
  ['unknown wrapper field', f => { f.wrapper.extra = 1; }, /schema violation/],
  ['unknown movement field', f => { f.wrapper.experiment.movement.extra = 1; }, /schema violation/],
  ['fractional coefficient', f => { f.wrapper.experiment.experience.linear = 0.5; }, /schema violation/],
  ['negative coefficient', f => { f.wrapper.experiment.experience.linear = -1; }, /schema violation/],
  ['zero quadratic', f => { f.wrapper.experiment.experience.quadratic = 0; }, /schema violation/],
  ['unsafe coefficient', f => { f.wrapper.experiment.experience.quadratic = Number.MAX_SAFE_INTEGER; }, /schema violation/],
  ['decision after duration', f => { f.wrapper.experiment.movement.decisionPeriodTicks = f.wrapper.tuning.durationTicks + 1; }, /decision period exceeds/],
  ['offset outside map', f => { f.wrapper.experiment.movement.circuitOffsets[0].x = f.wrapper.tuning.world.map.width + 1; }, /offset exceeds map/],
  ['clamped duplicate waypoints', f => { const w=f.wrapper.tuning.world.map.width; f.wrapper.experiment.movement.circuitOffsets = [{x:-w,y:0},{x:-w+1,y:0}]; }, /distinct clamped waypoints/],
  ['duplicate mixed category', f => { f.wrapper.experiment.mixedCategoryOrder[0] = 'land'; }, /schema violation/],
  ['nested season mismatch', f => { f.wrapper.tuning.world.seasons[0].durationTicks++; }, /season durations/],
  ['nested unknown hero', f => { f.wrapper.tuning.defaultHero = 'core:missing'; }, /unresolved hero/],
];
for (const [name, mutate, expected] of experimentMutations) test(`S4b rejects ${name}`, async t => {
  const f = await experimentFixture(t); mutate(f); await f.save();
  const result = await validateContent(f.directory, { fullPool: true });
  assert.equal(result.valid, false); assert.match(result.errors.join('\n'), expected);
});
test('maximum bounded XP coefficients remain valid because runtime uses saturating BigInteger arithmetic', async t => {
  const f = await experimentFixture(t);
  f.wrapper.experiment.experience = { base: 1_000_000, linear: 1_000_000, quadratic: 1_000_000 };
  await f.save(); const result = await validateContent(f.directory, { fullPool: true });
  assert.equal(result.valid, true, result.errors.join('\n'));
});

for (const [name, mutate] of [
  ['policy weights', tuning => { tuning.policies.weapon.damageMultiplier++; }],
  ['farm yield', tuning => { tuning.world.farms.foodPerHarvest++; }],
  ['people capacity', tuning => { tuning.world.people.maxPeople++; }],
  ['building count', tuning => { tuning.world.buildings.siteCount++; }],
  ['movement speed', tuning => { tuning.world.map.lordSpeed++; }],
  ['progression budget', tuning => { tuning.world.progression.rerolls++; }],
  ['season growth', tuning => { tuning.world.seasons[0].growthMultiplier++; }],
  ['damage roll', tuning => { tuning.damageRollMax++; }],
]) test(`S4b global-only boundary rejects independent ${name}`, async t => {
  const f = await experimentFixture(t); mutate(f.wrapper.tuning); await f.save();
  const result = await validateContent(f.directory, { fullPool: true });
  assert.equal(result.valid, false); assert.match(result.errors.join('\n'), /outside preregistered global tuning/);
});
test('S4b allows only lord health and threat changes and ignores object key order', async t => {
  const f = await experimentFixture(t);
  f.wrapper.tuning.world.map.lordHealth++;
  f.wrapper.tuning.world.threat.spawnPeriodTicks++;
  f.wrapper.tuning = Object.fromEntries(Object.entries(f.wrapper.tuning).reverse());
  await f.save(); const result = await validateContent(f.directory, { fullPool: true });
  assert.equal(result.valid, true, result.errors.join('\n'));
});
test('S4b compares against current base tuning rather than freezing historical IDs or numbers', async t => {
  const f = await experimentFixture(t); f.wrapper.tuning.world.map.lordSpeed++;
  await writeFile(path.join(f.directory, 'tuning.json'), JSON.stringify(f.wrapper.tuning));
  for (const filename of (await readdir(f.directory)).filter(name => /^tuning-s4b-[a-zA-Z0-9_-]+\.json$/.test(name))) {
    const existingPath = path.join(f.directory, filename);
    const existing = JSON.parse(await readFile(existingPath, 'utf8'));
    existing.tuning.world.map.lordSpeed = f.wrapper.tuning.world.map.lordSpeed;
    await writeFile(existingPath, JSON.stringify(existing));
  }
  await f.save(); const result = await validateContent(f.directory, { fullPool: true });
  assert.equal(result.valid, true, result.errors.join('\n'));
});
