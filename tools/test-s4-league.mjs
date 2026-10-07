import test from 'node:test';
import assert from 'node:assert/strict';
import { parseS4Options, makeS4Cases, boundedMap, extractS4Case, assembleS4Tables, summarizePolicyDamage } from './s4-league.mjs';

const sha = 'a'.repeat(64);
function fixture() {
  const selectedIds = Object.fromEntries(Object.entries({ weapons: 4, tools: 4, charters: 4, items: 12, evolutions: 2, enemies: 6, heroes: 1, estates: 1 }).map(([kind, count]) => [kind, Array.from({ length: count }, (_, i) => `test:${kind}${i}`)]));
  const caseInfo = makeS4Cases('s4-smoke')[0];
  const effect = { effectId: 'test:effect', sourceId: selectedIds.items[0], sourceKind: 'item', trigger: 'modifier', operation: 'stat-add', subject: 'attack-cooldown', amount: -1 };
  const sample = (tick) => ({ tick, season: 0, level: 1, enemies: 0, farms: 0, buildings: 0, people: 0, food: 1, lordHealth: 10, weaponDamage: tick, toolDamage: 0, growthDamage: 0, allyDamage: 0, killExperience: 0, harvestExperience: 0, taxExperience: 0 });
  const result = { policy: caseInfo.policy, peopleRule: caseInfo.peopleRule, seed: caseInfo.seed, scenario: 'normal', ticks: 900, hash: sha, survived: true, endReason: 'fixture-duration', deathCause: '', level: 1, season: 0, randomDraws: 1, weaponDamage: 900, allyDamage: 0, damage: 900, perTool: {}, killExperience: 0, harvestExperience: 0, taxExperience: 0, food: 1, estateTicks: 900, spawnedEnemies: 0, harvests: 0, ruins: 0, rebuilds: 0, peasants: 0, militia: 0, vassals: 0, timeline: [sample(0), sample(900)], cards: [], runtime: { build: { weapons: [selectedIds.weapons[0]], tools: [], charters: {}, itemStacks: {}, evolutions: [] }, toolGrowthByTarget: {}, evolutionActivations: [], loot: [], effects: [{ ...effect, activationCount: 0, appliedTotal: 0, firstActivationTick: null, lastActivationTick: null }] } };
  const sourceMetadata = { profileId: 'test:profile', profileSha256: sha, contentSha256: sha, commit: 'b'.repeat(40), sourceTreeSha256: sha, gitDirty: true, simulationAssemblySha256: sha, coreAssemblySha256: sha, selectedIds, cardCatalog: [...selectedIds.weapons.map(id=>({id,kind:'weapon',growthTargets:[],tags:[]})),...selectedIds.tools.map(id=>({id,kind:'tool',growthTargets:['land'],tags:[]})),...selectedIds.charters.map(id=>({id,kind:'charter',growthTargets:[],tags:[]}))], effectCatalog: [effect], acquisitionCatalog: {lootSources:[]}, timelineSampleIntervalTicks: 900, charterSlots: 2, tickRate: 30, configuredDurationTicks: 21600, requestedDurationTicks: 900, seasonDurationTicks: [5400,5400,5400,5400], seasonNames: ['spring','summer','autumn','winter'], metaSnapshot: 'neutral-no-meta', advertisementUses: 0, tuningStatus: 'provisional', scenario: 'normal', includeTest: false, peopleRule: 'A' };
  return { caseInfo, results: Array.from({length:3},()=>structuredClone(result)), metrics: { stage: 'S4', tickP95Ms: 0.1, runtime: '8', os: 'test', architecture: 'test', stopwatchFrequency: 1000, sourceMetadata, requestedWarmupTicks: 0, actualWarmupTicks: 0, warmupExcluded: true, policy: caseInfo.policy, seed: caseInfo.seed, ticks: 900, hash: sha, iterations: 3 }, resultsPath: 'raw/result.json', resultsSha256: sha, metricsPath: 'raw/metrics.json', metricsSha256: sha };
}
const extract = packet => extractS4Case(packet, { repeatCount: 3, requestedTicks: 900 });
test('fixed matrix and strict CLI preserve requested sample size', () => {
  assert.equal(makeS4Cases('s4-smoke').length,54); assert.equal(makeS4Cases('s4-stage').length,576);
  assert.deepEqual([...new Set(makeS4Cases('s4-stage').map(x=>x.seed))],Array.from({length:32},(_,i)=>42+i));
  assert.deepEqual(parseS4Options(['s4-smoke']),{mode:'s4-smoke',profile:'s4-stage-one',workers:2});
  for(const args of [['s4-long'],['s4-smoke','--workers','5'],['s4-smoke','--workers','1','--workers','2'],['s4-smoke','--profile','../escape'],['s4-smoke','--unknown','x']]) assert.throws(()=>parseS4Options(args));
});
test('bounded workers retain ordering and wait active jobs on failure', async () => {
  let active=0,maximum=0;
  const output=await boundedMap([3,2,1,0],2,async value=>{active++;maximum=Math.max(maximum,active);await new Promise(resolve=>setTimeout(resolve,value*2));active--;return value;});
  assert.deepEqual(output,[3,2,1,0]);assert.equal(maximum,2);
  let completed=0;await assert.rejects(boundedMap([0,1,2,3],2,async value=>{if(value===0)throw Error('expected');await new Promise(resolve=>setTimeout(resolve,2));completed++;}),/expected/);assert.equal(completed,1);
});
test('extract actual canonical rows and retain signed modifier deltas',()=>{
  const packet=fixture();packet.results[0].runtime.effects[0]={...packet.results[0].runtime.effects[0],activationCount:2,appliedTotal:-2,firstActivationTick:10,lastActivationTick:20};
  const data=extract(packet);assert.equal(data.tables.runs.length,1);assert.equal(data.tables.determinism.length,3);assert.equal(data.tables.effects[0].appliedTotal,-2);
});
test('reject corrupted repeats, timeline, damage, item cards, stacks and zero counters',()=>{
  const mutations=[p=>p.results[1].hash='c'.repeat(64),p=>p.results[0].timeline.shift(),p=>p.results[0].damage++,p=>p.results[0].cards.push({tick:1,level:2,offered:['test:items0'],chosen:'test:items0',rarity:'common'}),p=>p.results[0].runtime.build.itemStacks['test:items0']=1,p=>p.results[0].runtime.effects[0].appliedTotal=1,p=>p.results[0].runtime.effects=[]];
  for(const mutate of mutations){const packet=fixture();mutate(packet);assert.throws(()=>extract(packet));}
});
test('assembly rejects missing/duplicate cases and inconsistent provenance',()=>{
  const options=parseS4Options(['s4-smoke']);const timing={invocationArgs:['s4-smoke'],startedAt:'2026-01-01T00:00:00Z',endedAt:'2026-01-01T00:00:01Z',elapsedWallMs:1000};
  const entries=makeS4Cases('s4-smoke').map(caseInfo=>{const p=fixture();p.caseInfo=caseInfo;for(const r of p.results)Object.assign(r,{policy:caseInfo.policy,peopleRule:caseInfo.peopleRule,seed:caseInfo.seed});Object.assign(p.metrics,{policy:caseInfo.policy,seed:caseInfo.seed});p.metrics.sourceMetadata.peopleRule=caseInfo.peopleRule;return extract(p);});
  assert.equal(assembleS4Tables(entries,options,timing).runs.length,54);
  assert.throws(()=>assembleS4Tables(entries.slice(1),options,timing),/Incomplete/);
  const duplicate=structuredClone(entries);duplicate[1]=duplicate[0];assert.throws(()=>assembleS4Tables(duplicate,options,timing),/case/);
  const changed=structuredClone(entries);changed[1].identity.sourceCommit='c'.repeat(40);assert.throws(()=>assembleS4Tables(changed,options,timing),/mismatch/);
});

test('damage dispersion uses independent policy means and keeps zero mean undefined',()=>{
  const runs=makeS4Cases('s4-smoke').map(caseInfo=>({...caseInfo,weaponDamage:10,toolActivationDamage:2,toolGrowthDamage:3,allyDamage:5,survived:true,level:2}));
  const summary=summarizePolicyDamage(runs);assert.equal(summary.policyDamageCv,0);assert.ok(summary.policyMeans.every(row=>row.damage===20 && row.independentCases===9));
  runs[0].allyDamage=25;assert.ok(summarizePolicyDamage(runs).policyDamageCv>0);
  for(const run of runs) for(const key of ['weaponDamage','toolActivationDamage','toolGrowthDamage','allyDamage'])run[key]=0;
  assert.equal(summarizePolicyDamage(runs).policyDamageCv,null);
});
test('reject lifecycle, missing sample, season and catalog contradictions',()=>{
  const mutations = [
    p=>p.results[0].timeline.at(-1).lordHealth=0,
    p=>p.results.forEach(r=>{r.survived=false;r.endReason='death';}),
    p=>p.results.forEach(r=>{r.endReason='duration';}),
    p=>{p.metrics.sourceMetadata.timelineSampleIntervalTicks=300;},
    p=>{p.results[0].timeline[0].level=2;},
    p=>{p.results[0].timeline[0].season=4;},
    p=>{p.results[0].timeline.at(-1).food++;},
    p=>{p.metrics.sourceMetadata.seasonDurationTicks[0]=-1;},
    p=>{p.metrics.sourceMetadata.cardCatalog[0].id='test:foreign';},
    p=>{p.metrics.sourceMetadata.cardCatalog[0].kind='tool';},
  ];
  for(const mutate of mutations){const packet=fixture();mutate(packet);assert.throws(()=>extract(packet));}
});
