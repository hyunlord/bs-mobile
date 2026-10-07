import assert from 'node:assert/strict';
import test from 'node:test';
import { crossover, dominance } from './s4-report.mjs';

test('crossover accepts a positive tie and excludes zero-only samples', () => {
  const samples = [{ tick: 0, weaponDamage: 0, toolActivationDamage: 0, toolGrowthDamage: 0 }, { tick: 10, weaponDamage: 5, toolActivationDamage: 4, toolGrowthDamage: 1 }];
  const result = crossover(samples);
  assert.deepEqual(result, { tick: 10, state: 'tied-first-positive', intervalTick: 10 });
});
test('dominance fails even when all policies tie in every condition', () => {
  const runs = ['A','B'].flatMap(peopleRule => ['weapon','land'].map(policy => ({ policy, peopleRule, seed: 42, survived: true, ticks: 10, level: 2, weaponDamage: 5, toolActivationDamage: 0, toolGrowthDamage: 0, allyDamage: 0 })));
  const result = dominance(runs);
  assert.deepEqual(result.dominantPolicies, ['land','weapon']);
});

import { mkdtemp, readFile, writeFile, rm, readdir, mkdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { formatCsv, parseCsv } from './csv.mjs';
import { HEADERS, validateTables } from './s4-report-input.mjs';
import { generateReport, summarize } from './s4-report.mjs';

export function fixture() {
  const policies = ['weapon','land','building','people','mixed','random']; const rules=['A','B','C']; const seeds=[42,43,44]; const hash='a'.repeat(64);
  const selected=Object.fromEntries(Object.entries({weapons:4,tools:4,charters:4,items:12,evolutions:2,enemies:6,heroes:1,estates:1}).map(([kind,n])=>[kind,Array.from({length:n},(_,i)=>`test:${kind}_${i}`)]));
  const effect={effectId:'test:effect',sourceId:selected.tools[0],sourceKind:'tool',trigger:'attack',operation:'damage-pulse',subject:'tool-growth',amount:2,radius:1,durationTicks:0,foodCost:0,conditions:[],unit:'HP',implementationNote:'Synthetic fixture, not gameplay evidence.'};
  const meta={schemaVersion:1,stage:'S4',mode:'s4-smoke',profileId:'test:profile',profileSha256:hash,contentSha256:hash,sourceCommit:'a'.repeat(40),sourceTreeSha256:hash,sourceDirty:false,runtimeJson:{framework:'fixture',os:'fixture',architecture:'fixture',stopwatchFrequency:1},policiesJson:policies,peopleRulesJson:rules,seedsJson:seeds,repeatCount:3,tickRate:30,requestedTicks:900,configuredDurationTicks:21600,seasonDurationTicksJson:[5400,5400,5400,5400],seasonNamesJson:['spring','summer','autumn','winter'],independentCaseCount:54,totalExecutions:162,workerLimit:2,requestedWarmupTicks:0,actualWarmupTicksMin:0,actualWarmupTicksMax:0,warmupExcluded:true,scenario:'normal',shortenedSimulation:true,seasonsAccelerated:false,metaSnapshot:'neutral-no-meta',advertisementUses:0,tuningStatus:'fixture',acquisitionCatalogJson:{lootSources:[{id:'test:chest',kind:'chest',weight:1,foodCost:0}],lootPeriodTicks:300,lootSpawnRadius:1,lootPickupRadius:1,maxGroundLoot:2,lootQuantity:1},charterSlots:4,itemStackLimit:'none',selectedIdsJson:selected,cardCatalogJson:[{id:selected.tools[0],kind:'tool',growthTargets:['building','people'],tags:['building','people']}],effectCatalogJson:[effect],timelineSampleIntervalTicks:450,crossoverRule:'first-sampled-positive-tool-total-gte-positive-weapon-total',rankFieldsJson:['survived','ticks','level','totalCombatDamage'],dominanceRule:'policy-cofirst-in-every-peopleRule-seed-condition',foodMaterialScored:false,scope:'Synthetic report fixture',invocationArgsJson:['fixture'],measurementStartedAt:'2026-10-08T00:00:00Z',measurementEndedAt:'2026-10-08T00:00:01Z',elapsedWallMs:1000};
  const rows=Object.fromEntries(Object.keys(HEADERS).map(name=>[name,[]]));
  rows.metadata=Object.entries(meta).map(([key,value])=>({key,value:key.endsWith('Json')?JSON.stringify(value):String(value)}));
  for(const policy of policies) for(const peopleRule of rules) for(const seed of seeds) {
    const caseId=`${policy}__${peopleRule}__${seed}`; const row=Object.fromEntries(HEADERS.runs.map(key=>[key,0]));
    Object.assign(row,{caseId,policy,peopleRule,seed,repeatCount:3,ticks:900,survived:true,endReason:'fixture-duration',deathCause:'',level:2,season:0,hash,weaponDamage:10,toolActivationDamage:6,toolGrowthDamage:4,killExperience:10,harvestExperience:10,estateTicks:450,equipmentIdsJson:JSON.stringify([selected.tools[0]]),charterIdsJson:'[]',charterRanksJson:'{}',itemStacksJson:'{}',evolutionIdsJson:'[]',evolutionActivationsJson:'[]',toolGrowthByTargetJson:JSON.stringify({[selected.tools[0]]:{building:1,people:1}}),sourceResultsPath:`raw/${caseId}.results.json`,sourceResultsSha256:hash,sourceMetricsPath:`raw/${caseId}.metrics.json`,sourceMetricsSha256:hash}); rows.runs.push(row);
    for(const tick of [0,450,900]) rows.timeline.push({caseId,tick,season:0,level:tick===0?1:2,enemies:0,farms:0,buildings:0,people:1,food:0,lordHealth:10,weaponDamage:tick/90,toolActivationDamage:tick/150,toolGrowthDamage:tick/225,allyDamage:0,killExperience:tick/90,harvestExperience:tick/90,taxExperience:0});
    for(let repeatIndex=0;repeatIndex<3;repeatIndex++) rows.determinism.push({caseId,repeatIndex,hash,ticks:900,survived:true,endReason:'fixture-duration'});
    rows.effects.push({caseId,effectId:effect.effectId,sourceId:effect.sourceId,sourceKind:effect.sourceKind,trigger:effect.trigger,operation:effect.operation,subject:effect.subject,amount:effect.amount,activationCount:1,appliedTotal:3,firstActivationTick:450,lastActivationTick:450});
  }
  return Object.fromEntries(Object.entries(rows).map(([name,data])=>[name,parseCsv(formatCsv(HEADERS[name],data),HEADERS[name])]));
}
export async function writeFixture(directory,tables) { await mkdir(directory,{recursive:true}); for(const [name,rows] of Object.entries(tables)) await writeFile(join(directory,`${name}.csv`),formatCsv(HEADERS[name],rows)); }

for(const [name,samples,expected] of [
  ['none',[{tick:0,weaponDamage:0,toolActivationDamage:0,toolGrowthDamage:0},{tick:10,weaponDamage:9,toolActivationDamage:3,toolGrowthDamage:1}],null],
  ['zero only',[{tick:0,weaponDamage:0,toolActivationDamage:0,toolGrowthDamage:0}],null],
  ['later',[{tick:0,weaponDamage:0,toolActivationDamage:0,toolGrowthDamage:0},{tick:10,weaponDamage:9,toolActivationDamage:3,toolGrowthDamage:1},{tick:20,weaponDamage:10,toolActivationDamage:10,toolGrowthDamage:2}],20],
  ['first positive',[{tick:0,weaponDamage:0,toolActivationDamage:0,toolGrowthDamage:0},{tick:10,weaponDamage:2,toolActivationDamage:3,toolGrowthDamage:0}],10]
]) test(`crossover ${name} is computed from direct damage`,()=>assert.equal(crossover(samples).tick,expected));

test('rotating condition winners have no universally co-first policy',()=>{
  const rows=['A','B'].flatMap(peopleRule=>['weapon','land'].map(policy=>({policy,peopleRule,seed:42,survived:true,ticks:1,level:1,weaponDamage:(peopleRule==='A')===(policy==='weapon')?10:1,toolActivationDamage:0,toolGrowthDamage:0,allyDamage:0})));
  assert.deepEqual(dominance(rows).dominantPolicies,[]);
});
test('food and residence differences cannot break a rank tie',()=>{
  const rows=['weapon','land'].map((policy,i)=>({policy,peopleRule:'A',seed:42,survived:true,ticks:1,level:1,weaponDamage:1,toolActivationDamage:0,toolGrowthDamage:0,allyDamage:0,foodFinal:i*100,estateTicks:i}));
  assert.equal(dominance(rows).dominantPolicies.length,2);
});
test('synthetic matrix validates zero-card and zero-loot cases',()=>assert.equal(validateTables(fixture()).runs.length,54));
for(const [name,mutate,pattern] of [
  ['missing case',x=>x.runs.pop(),/matrix/],['duplicate case',x=>x.runs.push(x.runs[0]),/duplicate case/],
  ['terminal mismatch',x=>x.timeline[2].weaponDamage='11',/terminal/],['negative delta',x=>x.timeline[1].weaponDamage='11',/nonmonotonic/],
  ['missing tick0',x=>x.timeline.shift(),/tick0/],['repeat mismatch',x=>x.determinism[0].hash='b'.repeat(64),/repeat hash/],
  ['duplicate repeat',x=>x.determinism[1].repeatIndex='0',/repeat index/],['bad final inventory',x=>x.runs[0].itemStacksJson='{"test:items_0":1}',/loot stacks/],
  ['unmatched effect',x=>x.effects.pop(),/effect counters/],['effect identity drift',x=>x.effects[0].operation='invented',/effect operation/],
  ['numeric garbage',x=>x.runs[0].ticks='900junk',/integer/]
]) test(`rejects ${name}`,()=>{const tables=fixture();mutate(tables);assert.throws(()=>validateTables(tables),pattern);});

test('actual chosen mixed tool is counted once, never as an item',()=>{
  const tables=fixture(); tables.cards.push({caseId:tables.runs[0].caseId,choiceIndex:'0',tick:'450',level:'2',offeredIdsJson:'["test:tools_0"]',chosenId:'test:tools_0',rarity:'common',chosenKind:'tool',chosenGrowthTargetsJson:'["building","people"]',chosenTagsJson:'["building","people"]'});
  const result=summarize(validateTables(tables)); assert.deepEqual(result.choices,[{policy:'weapon',peopleRule:'A',kind:'tool',target:'mixed',choices:1,totalChoices:1,choiceShare:1}]);
});
test('chosen-not-offered card fails closed',()=>{
  const tables=fixture();tables.cards.push({caseId:tables.runs[0].caseId,choiceIndex:'0',tick:'450',level:'2',offeredIdsJson:'[]',chosenId:'test:tools_0',rarity:'common',chosenKind:'tool',chosenGrowthTargetsJson:'["building","people"]',chosenTagsJson:'["building","people"]'});
  assert.throws(()=>validateTables(tables),/not offered/);
});
test('loot quantities reconcile actual unlimited inventory stack counts',()=>{
  const tables=fixture(); tables.loot.push({caseId:tables.runs[0].caseId,acquisitionIndex:'0',tick:'450',sourceKind:'chest',sourceId:'test:chest',sourceEntityId:'1',itemId:'test:items_0',quantity:'20',stackAfter:'20',foodPaid:'0'}); tables.runs[0].itemStacksJson='{"test:items_0":20}';
  assert.equal(summarize(validateTables(tables)).loot[0].quantity,20);
});
test('zero XP ratios are null and do not become damage',()=>{
  const tables=fixture();for(const row of tables.runs)for(const key of ['killExperience','harvestExperience'])row[key]='0';for(const row of tables.timeline)for(const key of ['killExperience','harvestExperience'])row[key]='0';
  const result=summarize(validateTables(tables));assert.equal(result.runs[0].killShare,null);assert.equal(result.runs[0].totalCombatDamage,20);
});
test('observed cohorts do not forward-fill early terminal cases',()=>{
  const tables=fixture();const run=tables.runs[0];run.ticks='450';run.survived='false';run.endReason='death';run.deathCause='test:enemy';tables.timeline[1].lordHealth='0';run.weaponDamage='5';run.toolActivationDamage='3';run.toolGrowthDamage='2';run.killExperience='5';run.harvestExperience='5';tables.timeline=tables.timeline.filter(row=>row.caseId!==run.caseId||row.tick!=='900');for(const row of tables.determinism.filter(row=>row.caseId===run.caseId)){row.ticks='450';row.survived='false';row.endReason='death';}
  const result=summarize(validateTables(tables));const last=result.curves.find(row=>row.policy==='weapon'&&row.peopleRule==='A'&&row.tick===900);assert.equal(last.nObserved,2);
});
test('CLI reproduces report bytes solely from copied seven CSV inputs',async()=>{
  const directory=await mkdtemp(join(tmpdir(),'bs-report-'));
  try {
    const input=join(directory,'raw');await writeFixture(input,fixture());const one=join(directory,'one');const two=join(directory,'two');
    const result=spawnSync(process.execPath,['tools/s4-report.mjs',input,one,'--no-plots'],{encoding:'utf8'});assert.equal(result.status,0,result.stderr);
    await generateReport(join(one,'raw'),two,{noPlots:true});
    for(const name of (await readdir(one)).filter(name=>name.endsWith('.csv')||name.endsWith('.md')))assert.deepEqual(await readFile(join(one,name)),await readFile(join(two,name)),name);
    assert.match(await readFile(join(one,'S4-report.md'),'utf8'),/^관문: 실패/);
  } finally {await rm(directory,{recursive:true,force:true});}
});

import { S4_HEADERS } from './s4-league.mjs';
test('report and producer share identical frozen CSV headers',()=>assert.deepEqual(HEADERS,S4_HEADERS));
test('universally cofirst subset fails even when unique wins vary',()=>{
  const runs=['A','B'].flatMap(peopleRule=>['weapon','land','mixed'].map(policy=>({policy,peopleRule,seed:42,survived:true,ticks:10,level:1,weaponDamage:policy==='mixed'||(peopleRule==='A'&&policy==='weapon')||(peopleRule==='B'&&policy==='land')?5:1,toolActivationDamage:0,toolGrowthDamage:0,allyDamage:0})));
  assert.deepEqual(dominance(runs).dominantPolicies,['mixed']);
});
test('recrossing retains first observed crossing',()=>{
  const rows=[{tick:0,weaponDamage:0,toolActivationDamage:0,toolGrowthDamage:0},{tick:10,weaponDamage:2,toolActivationDamage:3,toolGrowthDamage:0},{tick:20,weaponDamage:8,toolActivationDamage:4,toolGrowthDamage:0},{tick:30,weaponDamage:9,toolActivationDamage:10,toolGrowthDamage:0}];
  assert.equal(crossover(rows).tick,10);
});
test('uppercase .NET SHA256 metadata and result hashes are valid hex digests',()=>{
  const tables=fixture();for(const row of tables.metadata)if(['profileSha256','contentSha256','sourceTreeSha256'].includes(row.key))row.value=row.value.toUpperCase();
  for(const row of tables.runs)for(const key of ['hash','sourceResultsSha256','sourceMetricsSha256'])row[key]=row[key].toUpperCase();
  for(const row of tables.determinism)row.hash=row.hash.toUpperCase();
  assert.equal(validateTables(tables).runs.length,54);
});
for(const [name,mutate,pattern] of [
  ['alive flag with zero terminal health',x=>x.timeline[2].lordHealth='0',/survival/],
  ['dead flag with positive terminal health',x=>{x.runs[0].survived='false';for(const r of x.determinism.filter(r=>r.caseId===x.runs[0].caseId))r.survived='false';},/survival/],
  ['regressing level',x=>x.timeline[1].level='999',/level/],
  ['regressing season',x=>x.timeline[1].season='3',/season/],
  ['inconsistent terminal food',x=>x.runs[0].foodFinal='999',/food/],
  ['missing periodic sample',x=>x.timeline.splice(1,1),/sample/],
  ['invalid source commit',x=>x.metadata.find(r=>r.key==='sourceCommit').value='not-a-commit',/sourceCommit/],
  ['invalid season configuration',x=>x.metadata.find(r=>r.key==='seasonDurationTicksJson').value='[1]',/season/],
  ['foreign catalog card',x=>{const row=x.metadata.find(r=>r.key==='cardCatalogJson');const defs=JSON.parse(row.value);defs.push({id:'foreign:card',kind:'weapon',growthTargets:[],tags:[]});row.value=JSON.stringify(defs);},/card.*selected/]
]) test(`rejects lifecycle/catalog mutation: ${name}`,()=>{const tables=fixture();mutate(tables);assert.throws(()=>validateTables(tables),pattern);});

test('premature alive duration is rejected even when terminal sums match',()=>{
 const tables=fixture();const run=tables.runs[0];run.ticks='450';run.weaponDamage='5';run.toolActivationDamage='3';run.toolGrowthDamage='2';run.killExperience='5';run.harvestExperience='5';tables.timeline=tables.timeline.filter(row=>row.caseId!==run.caseId||row.tick!=='900');for(const row of tables.determinism.filter(row=>row.caseId===run.caseId))row.ticks='450';
 assert.throws(()=>validateTables(tables),/premature alive/);
});
for(const [name,mutate,pattern] of [
 ['alive death label',x=>{x.runs[0].endReason='death';for(const row of x.determinism.filter(row=>row.caseId===x.runs[0].caseId))row.endReason='death';},/endReason/],
 ['smoke falsely unshortened',x=>x.metadata.find(row=>row.key==='shortenedSimulation').value='false',/shortenedSimulation/],
 ['load scenario in league',x=>x.metadata.find(row=>row.key==='scenario').value='load',/scenario/]
])test(`rejects frozen protocol violation: ${name}`,()=>{const tables=fixture();mutate(tables);assert.throws(()=>validateTables(tables),pattern);});

import { failureObservations } from './s4-report.mjs';
test('failure observations count paired neutral policies and survival saturation from CSV',()=>{
 const league=validateTables(fixture());const result=failureObservations(league,summarize(league));
 assert.equal(result.survivedCases,54);assert.equal(result.tiedSurvivalTimeConditions,9);
 assert.equal(result.neutralPairs,9);assert.equal(result.identicalNeutralTimelines,9);assert.equal(result.identicalNeutralChoices,9);
 assert.equal(result.strictLeaderLevelConditions,0);
});
test('failure observations detect a changed paired trajectory rather than asserting equality',()=>{
 const tables=fixture();const changed=tables.timeline.find(row=>row.caseId==='mixed__A__42'&&row.tick==='450');changed.weaponDamage='4';
 const league=validateTables(tables);const result=failureObservations(league,summarize(league));
 assert.equal(result.identicalNeutralTimelines,8);
});
