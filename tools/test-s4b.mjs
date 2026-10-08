import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { HEADERS, makeCases, configuration, extractCase, compareMovement, decodeTrace, totalDamage } from './s4b-contract.mjs';
import { assemble, options } from './s4b-league.mjs';
import { validateTables } from './s4b-input.mjs';
import { summarize, report, rankTuple, median, percentile, wilson } from './s4b-report.mjs';
import { formatCsv, parseCsv } from './csv.mjs';
const sha='a'.repeat(64);
function packet(caseInfo,mode='smoke-A'){
  const ticks=configuration(mode).requestedTicks,selectedIds=Object.fromEntries(Object.entries({weapons:4,tools:4,charters:4,items:12,evolutions:2,enemies:6,heroes:1,estates:1}).map(([key,n])=>[key,Array.from({length:n},(_,i)=>`test:${key}${i}`)]));
  const sample=tick=>({tick,season:0,level:1,enemies:0,farms:0,buildings:0,people:0,food:10,lordHealth:1,weaponDamage:0,toolDamage:0,growthDamage:0,allyDamage:0,killExperience:0,harvestExperience:0,taxExperience:0});
  const trace=Buffer.alloc((ticks+1)*8);for(let t=0;t<=ticks;t++){trace.writeInt32LE(t,t*8);trace.writeInt32LE(0,t*8+4);}
  const result={policy:caseInfo.policy,peopleRule:caseInfo.peopleRule,seed:caseInfo.seed,scenario:'normal',ticks,hash:sha,survived:true,endReason:ticks===900?'fixture-duration':'duration',deathCause:'',level:1,season:0,randomDraws:0,weaponDamage:0,allyDamage:0,damage:0,perTool:{},killExperience:0,harvestExperience:0,taxExperience:0,food:10,estateTicks:0,spawnedEnemies:0,harvests:0,ruins:0,rebuilds:0,peasants:0,militia:0,vassals:0,timeline:[sample(0),sample(ticks)],cards:[],runtime:{build:{weapons:[selectedIds.weapons[0]],tools:[],charters:{},itemStacks:{},evolutions:[]},toolGrowthByTarget:{},evolutionActivations:[],loot:[],effects:[]},experiment:{movementMode:caseInfo.movementMode,deathTick:null,movementTraceEncoding:'int32le-xy-v1',movementTraceCount:ticks+1,movementTraceBase64:trace.toString('base64'),movementSamples:[0,ticks].map(tick=>({tick,lordX:tick,lordY:0,estateX:0,estateY:0,distanceSquared:tick*tick})),farmWaitEvents:[]}};
  const sourceMetadata={profileId:'test:profile',profileSha256:sha,contentSha256:sha,commit:'b'.repeat(40),sourceTreeSha256:sha,gitDirty:mode.startsWith('smoke'),simulationAssemblySha256:sha,coreAssemblySha256:sha,selectedIds,cardCatalog:['weapon','tool','charter'].flatMap(kind=>selectedIds[kind+'s'].map(id=>({id,kind,growthTargets:kind==='tool'?['land']:[],tags:[]}))),effectCatalog:[],acquisitionCatalog:{lootSources:[]},timelineSampleIntervalTicks:ticks,charterSlots:2,tickRate:30,configuredDurationTicks:21600,requestedDurationTicks:ticks,seasonDurationTicks:[5400,5400,5400,5400],seasonNames:['spring','summer','autumn','winter'],metaSnapshot:'neutral-no-meta',advertisementUses:0,tuningStatus:'provisional',scenario:'normal',includeTest:false,peopleRule:caseInfo.peopleRule,experimentContractVersion:1,movementMode:caseInfo.movementMode,tuningFile:'test.json',tuningSha256:sha,experimentDefinition:{},experienceCurve:{base:30,linear:15,quadratic:1},mixedCategoryOrder:['weapon','land','building','people'],worldUnit:'world-unit',threatTuning:{},enemyTuning:[]};
  return{caseInfo,results:Array.from({length:3},()=>structuredClone(result)),metrics:{stage:'S4b',runtime:'8',os:'test',architecture:'test',stopwatchFrequency:1000,sourceMetadata,requestedWarmupTicks:0,actualWarmupTicks:0,warmupExcluded:true,policy:caseInfo.policy,seed:caseInfo.seed,ticks,hash:sha,iterations:3,tickP95Ms:.1},resultsPath:`raw/${caseInfo.caseId}.results.json`,metricsPath:`raw/${caseInfo.caseId}.metrics.json`,resultsSha256:sha,metricsSha256:sha};
}
export function fixture(mode='smoke-A'){
  const entries=makeCases(mode).map(info=>extractCase(packet(info,mode),mode));const opt={mode,workers:2};const tables=assemble(entries,opt,{args:[mode],start:'2026-01-01T00:00:00Z',end:'2026-01-01T00:00:01Z',elapsed:1000});
  return{entries,tables,raw:Object.fromEntries(Object.entries(tables).map(([name,rows])=>[name,parseCsv(formatCsv(HEADERS[name],rows),HEADERS[name])]))};
}
test('fixed modes separate smoke/evaluation/calibration and strict CLI',()=>{
  assert.equal(makeCases('A').length,576);assert.equal(makeCases('B').length,288);assert.equal(makeCases('calibration').length,32);assert.equal(makeCases('smoke-A').length,54);assert.equal(makeCases('smoke-B').length,27);
  assert.deepEqual(configuration('smoke-A').seeds,[9000,9001,9002]);assert.equal(configuration('calibration').seeds[0],1000);
  assert.throws(()=>options(['A','--profile','../x','--output','/tmp/x']));assert.throws(()=>options(['A','--profile','x','--output','/tmp/x','--workers','5']));
});
test('trace validation rejects malformed lengths, sample drift and repeat experiment drift',()=>{
  const info=makeCases('smoke-A')[0];for(const mutate of [p=>p.results[0].experiment.movementTraceCount--,p=>p.results[0].experiment.movementSamples[1].lordX++,p=>p.results[1].experiment.deathTick=3,p=>p.results[0].experiment.movementTraceBase64+='!']){const p=packet(info);mutate(p);assert.throws(()=>extractCase(p,'smoke-A'));}
  assert.throws(()=>decodeTrace({movementTraceEncoding:'bad'},2));
});
test('all15 pair prefixes include later divergence beyond earliest third-policy death',()=>{
  const{entries}=fixture();const group=entries.filter(e=>e.caseInfo.peopleRule==='A'&&e.caseInfo.seed===9000);group[0].tables.runs[0].ticks=10;group[1].trace.writeInt32LE(999,800*8);const rows=compareMovement(entries,'smoke-A');assert.equal(rows.length,135);assert.ok(rows.some(r=>r.leftCaseId===group[1].caseInfo.caseId&&r.rightCaseId===group[2].caseInfo.caseId&&r.mismatchCount===1&&r.commonEndTick===900));
});
test('CSV validation replays all boundaries, rejects trace proof, wait and metadata corruption',()=>{
  const{raw}=fixture();assert.equal(validateTables(raw).data.runs.length,54);
  for(const mutate of [r=>r['movement-equality'].pop(),r=>r['movement-equality'][0].comparedTickCount='1',r=>r['movement-samples'][0].distanceSquared='1',r=>r.metadata.find(x=>x.key==='stage').value='S4',r=>r.runs[0].foodFinal='999',r=>r.determinism[0].hash='f'.repeat(64)]){const copy=structuredClone(raw);mutate(copy);assert.throws(()=>validateTables(copy));}
});
test('harvest, destroyed and right censor remain separate and malformed episodes fail',()=>{
  const p=packet(makeCases('smoke-A')[0]);const events=['harvest','destroyed','duration-censored'].map((endKind,i)=>({farmId:i,episode:0,ripeTick:10,endTick:endKind.endsWith('censored')?900:20,endKind,observedWaitTicks:endKind.endsWith('censored')?890:10,censored:endKind.endsWith('censored')}));for(const r of p.results)r.experiment.farmWaitEvents=structuredClone(events);assert.equal(extractCase(p,'smoke-A').tables['farm-wait-events'].length,3);p.results[0].experiment.farmWaitEvents[2].censored=false;assert.throws(()=>extractCase(p,'smoke-A'));
});
test('rank excludes food/material, uses four combat components; distance is sqrt with explicit quantiles',()=>{
  const r={survived:true,ticks:10,level:2,weaponDamage:1,toolActivationDamage:2,toolGrowthDamage:3,allyDamage:4};assert.equal(totalDamage(r),10);assert.deepEqual(rankTuple(r),rankTuple({...r,foodFinal:1e9,materials:1e9}));assert.equal(median([1,3]),2);assert.equal(percentile([1,2,3,4],.95),4);
  const s=summarize(validateTables(fixture().raw));assert.equal(s.overall,'SMOKE_ONLY');assert.equal(s.baselineSummary.survived,null);assert.equal(s.baselineSummary.wilson95Low,null);assert.equal(s.baselineSummary.truncatedAlive,3);assert.equal(wilson(0,32).low,0);assert.ok(wilson(32,32).high<=1);assert.equal(s.distance[0].max,900);assert.equal(s.distance[0].sampleCount,6);
});
test('report regenerates byte-identically from copied ten CSVs only',async t=>{
  const dir=await fs.mkdtemp(path.join(os.tmpdir(),'s4b-replay-'));t.after(()=>fs.rm(dir,{recursive:true,force:true}));const{tables}=fixture();const input=path.join(dir,'input');await fs.mkdir(input);for(const[name,rows]of Object.entries(tables))await fs.writeFile(path.join(input,name+'.csv'),formatCsv(HEADERS[name],rows));
  const a=path.join(dir,'a'),b=path.join(dir,'b');await report(input,a);const copy=path.join(dir,'copy');await fs.cp(input,copy,{recursive:true});await fs.rm(input,{recursive:true});await report(copy,b);const markdown=await fs.readFile(path.join(a,'report.md'),'utf8');assert.match(markdown,/^관문: 부분 — 54사례·3반복/);assert.ok(!markdown.includes('Wilson 95% ['));const files=await fs.readdir(a);assert.deepEqual(files,await fs.readdir(b));for(const file of files)assert.deepEqual(await fs.readFile(path.join(a,file)),await fs.readFile(path.join(b,file)));
});
function syntheticStage(){
  const c=configuration('A'),runs=[],cards=[];
  for(const info of makeCases('A')){const survived=info.seed%2===0,cofirst=info.peopleRule==='A'?['weapon','land']:info.peopleRule==='B'?['building']:['people'];runs.push({...info,ticks:survived?21600:10000,survived,level:survived?30:20,weaponDamage:cofirst.includes(info.policy)?100:0,toolActivationDamage:0,toolGrowthDamage:0,allyDamage:0});if(['mixed','random'].includes(info.policy))cards.push({caseId:info.caseId,choiceIndex:0,tick:1,level:2,offeredIdsJson:['test:a','test:b'],chosenId:info.policy==='mixed'?'test:a':'test:b',rarity:'common'});}
  return{configuration:c,metadata:{experimentJson:{worldUnit:'world-unit'}},data:{runs,cards,'movement-samples':[],'farm-wait-events':[],'movement-equality':[]}};
}
test('full A gates use exact cofirst groups and require XP plus ledger distinction',()=>{
  const league=syntheticStage(),summary=summarize(league);assert.equal(summary.overall,'PASS');assert.equal(summary.baselineSummary.survived,16);assert.equal(summary.baselineSummary.cases,32);assert.equal(summary.pairs.length,96);assert.equal(summary.conditions.length,96);assert.equal(summary.groupRanks.filter(r=>r.peopleRule==='A'&&r.cofirst).length,2);
  const xp=structuredClone(league);for(const r of xp.data.runs)if(r.survived)r.level=46;assert.equal(summarize(xp).overall,'FAIL');
  const identical=structuredClone(league);for(const card of identical.data.cards)card.chosenId='test:a';assert.equal(summarize(identical).overall,'INVALID');
  const dominant=structuredClone(league);for(const r of dominant.data.runs)r.weaponDamage=0;const allTies=summarize(dominant);assert.equal(allTies.gates.find(g=>g.gate==='b').pass,false);assert.equal(allTies.overall,'FAIL');
});
