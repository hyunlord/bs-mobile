import fs from 'node:fs/promises';
import path from 'node:path';
import { parseCsv } from './csv.mjs';
import { HEADERS, makeCases, configuration, canonical, requireValue as need, integer, validateWaits, sha256 } from './s4b-contract.mjs';
import { extractS4Case } from './s4-league.mjs';
const TEXT=new Set('caseId policy peopleRule endReason deathCause hash sourceResultsPath sourceResultsSha256 sourceMetricsPath sourceMetricsSha256 chosenId rarity chosenKind sourceKind sourceId itemId effectId trigger operation subject movementMode endKind leftCaseId rightCaseId leftSourceResultsPath rightSourceResultsPath leftTraceSha256 rightTraceSha256'.split(' '));
const BOOL=new Set(['survived','censored']);
const NULLABLE=new Set(['firstActivationTick','lastActivationTick']);
function parseRow(row){return Object.fromEntries(Object.entries(row).map(([key,value])=>{
  if(key.endsWith('Json'))return[key,JSON.parse(value)];if(TEXT.has(key))return[key,value];
  if(BOOL.has(key)){need(value==='true'||value==='false',`invalid boolean ${key}`);return[key,value==='true'];}
  if(NULLABLE.has(key)&&value==='')return[key,null];need(/^-?(0|[1-9][0-9]*)$/.test(value),`invalid integer ${key}`);const number=Number(value);integer(number,key,-Number.MAX_SAFE_INTEGER);return[key,number];
}));}
export function validateTables(raw){
  const metadata={};for(const{key,value}of raw.metadata){need(key&&!Object.hasOwn(metadata,key),'duplicate metadata');metadata[key]=key.endsWith('Json')?JSON.parse(value):value;}
  need(metadata.stage==='S4b'&&metadata.schemaVersion==='1','stage/schema');const c=configuration(metadata.mode),cases=makeCases(metadata.mode);
  const identity=metadata.identityJson;need(identity&&typeof identity==='object','identity');
  for(const[key,expected]of Object.entries({repeatCount:3,tickRate:30,configuredDurationTicks:21600,requestedTicks:c.requestedTicks,distinctCaseCount:cases.length,seedBlockCount:c.seeds.length,totalExecutions:cases.length*3}))need(metadata[key]===String(expected),`metadata ${key}`);
  need(metadata.shortenedSimulation===String(c.smoke)&&metadata.seasonsAccelerated==='false'&&metadata.foodMaterialScored==='false'&&metadata.sourceRawHashesVerified==='true','protocol booleans');
  need(canonical(metadata.rankFieldsJson)===canonical(['survived','ticks','level','totalCombatDamage'])&&metadata.damageFormula==='weaponDamage+toolActivationDamage+toolGrowthDamage+allyDamage','rank whitelist');
  for(const[key,expected]of [['policiesJson',c.policies],['peopleRulesJson',c.rules],['seedsJson',c.seeds],['movementsJson',c.movements]])need(canonical(metadata[key])===canonical(expected),`matrix metadata ${key}`);
  for(const key of ['profileId','profileSha256','contentSha256','sourceCommit','sourceTreeSha256','simulationAssemblySha256','coreAssemblySha256'])need(metadata[key]===identity[key],`identity ${key}`);
  need(metadata.sourceDirty===String(identity.sourceDirty)&&(c.smoke||identity.sourceDirty===false),'source dirty');
  need(canonical(metadata.experimentJson)===canonical(identity.experiment),'experiment provenance');
  const data=Object.fromEntries(Object.entries(raw).filter(([name])=>name!=='metadata').map(([name,rows])=>[name,rows.map(parseRow)]));
  const runs=new Map(data.runs.map(r=>[r.caseId,r]));need(runs.size===cases.length&&data.runs.length===cases.length,'case count/duplicates');
  for(const info of cases){const r=runs.get(info.caseId);need(r&&r.policy===info.policy&&r.peopleRule===info.peopleRule&&r.seed===info.seed,'missing/wrong case');need(r.sourceResultsPath===`raw/${r.caseId}.results.json`&&r.sourceMetricsPath===`raw/${r.caseId}.metrics.json`,'unsafe/noncanonical raw path');}
  for(const[name,rows]of Object.entries(data))if(name!=='movement-equality')for(const row of rows)need(runs.has(row.caseId),`orphan ${name}`);
  const byCase=(name,id)=>data[name].filter(row=>row.caseId===id);
  // Reuse the original producer's validation on the lossless CSV projection of its consumed fields.
  for(const info of cases){const r=runs.get(info.caseId),timeline=byCase('timeline',r.caseId),cards=byCase('cards',r.caseId),loot=byCase('loot',r.caseId),effects=byCase('effects',r.caseId),repeats=byCase('determinism',r.caseId);
    need(repeats.length===3&&repeats.every((x,i)=>x.repeatIndex===i),'repeat set/order');
    const {caseId,...run}=r;need(r.repeatCount===3,'run repeats');
    const result={...run,scenario:'normal',damage:r.weaponDamage+r.toolActivationDamage+r.toolGrowthDamage+r.allyDamage,food:r.foodFinal,perTool:{csvAggregate:{activationDamage:r.toolActivationDamage,growthDamage:r.toolGrowthDamage}},
      timeline:timeline.map(row=>({...row,toolDamage:row.toolActivationDamage,growthDamage:row.toolGrowthDamage})),cards:cards.map((row,i)=>{need(row.choiceIndex===i,'choice order');return{tick:row.tick,level:row.level,offered:row.offeredIdsJson,chosen:row.chosenId,rarity:row.rarity};}),
      runtime:{build:{weapons:r.equipmentIdsJson.filter(id=>identity.selectedIds.weapons.includes(id)),tools:r.equipmentIdsJson.filter(id=>identity.selectedIds.tools.includes(id)),charters:r.charterRanksJson,itemStacks:r.itemStacksJson,evolutions:r.evolutionIdsJson},toolGrowthByTarget:r.toolGrowthByTargetJson,evolutionActivations:r.evolutionActivationsJson,loot:loot.map((row,i)=>{need(row.acquisitionIndex===i,'loot order');return row;}),effects}};
    need(r.equipmentIdsJson.every(id=>identity.selectedIds.weapons.includes(id)||identity.selectedIds.tools.includes(id))&&new Set(r.equipmentIdsJson).size===r.equipmentIdsJson.length,'unknown/duplicate equipment');
    need(canonical([...r.charterIdsJson].sort())===canonical(Object.keys(r.charterRanksJson).sort()),'charter IDs/ranks');
    const sourceMetadata={...identity,commit:identity.sourceCommit,gitDirty:identity.sourceDirty,requestedDurationTicks:identity.requestedTicks,scenario:'normal',includeTest:false,peopleRule:r.peopleRule};
    const metrics={...identity.runtime,runtime:identity.runtime.framework,stage:'S4b',sourceMetadata,policy:r.policy,seed:r.seed,ticks:r.ticks,hash:r.hash,iterations:3,requestedWarmupTicks:identity.requestedWarmupTicks,actualWarmupTicks:0,warmupExcluded:true,tickP95Ms:0};
    const projected=extractS4Case({caseInfo:{caseId:r.caseId,policy:r.policy,peopleRule:r.peopleRule,seed:r.seed},results:repeats.map(repeat=>({...result,...repeat})),metrics,resultsPath:r.sourceResultsPath,metricsPath:r.sourceMetricsPath,resultsSha256:r.sourceResultsSha256,metricsSha256:r.sourceMetricsSha256},{stage:'S4b',repeatCount:3,requestedTicks:c.requestedTicks});
    need(canonical(projected.tables.cards)===canonical(cards.map(row=>Object.fromEntries(HEADERS.cards.map(key=>[key,key.endsWith('Json')?JSON.stringify(row[key]):row[key]])))),'card metadata mismatch');
    const samples=byCase('movement-samples',r.caseId);need(samples.length===timeline.length,'movement sample count');
    samples.forEach((row,i)=>{need(row.tick===timeline[i].tick&&row.movementMode===info.movementMode,'movement grid/mode');for(const key of ['lordX','lordY','estateX','estateY'])integer(row[key],key,-2147483648);need(Number.isSafeInteger(row.distanceSquared)&&row.distanceSquared===(row.lordX-row.estateX)**2+(row.lordY-row.estateY)**2,'distance squared');});
  }
  validateWaits(data['farm-wait-events'],runs);
  const proofs=data['movement-equality'],expectedPairs=[];
  if(c.experiment==='A')for(const rule of c.rules)for(const seed of c.seeds)for(let i=0;i<c.policies.length;i++)for(let j=i+1;j<c.policies.length;j++)expectedPairs.push([`${c.policies[i]}__${rule}__${seed}__circuit`,`${c.policies[j]}__${rule}__${seed}__circuit`]);
  need(proofs.length===expectedPairs.length,'pairwise proof count');
  proofs.forEach((p,i)=>{const[l,r]=expectedPairs[i],left=runs.get(l),right=runs.get(r);need(p.leftCaseId===l&&p.rightCaseId===r&&p.peopleRule===left.peopleRule&&p.seed===left.seed&&p.movementMode==='circuit','pair proof identity');
    need(p.leftSourceResultsPath===left.sourceResultsPath&&p.rightSourceResultsPath===right.sourceResultsPath,'pair raw reference');need(/^[a-f0-9]{64}$/.test(p.leftTraceSha256)&&/^[a-f0-9]{64}$/.test(p.rightTraceSha256),'trace hash');
    need(p.commonEndTick===Math.min(left.ticks,right.ticks)&&p.comparedTickCount===p.commonEndTick+1&&p.comparedCoordinateValues===p.comparedTickCount*2,'pair coverage');integer(p.mismatchCount,'mismatch');need(p.mismatchCount<=p.comparedCoordinateValues,'mismatch bound');
  });
  return{metadata,configuration:c,data,runs,cases};
}
export async function load(input){const raw={},manifest=[];for(const[name,headers]of Object.entries(HEADERS)){const file=name+'.csv',bytes=await fs.readFile(path.join(input,file));raw[name]=parseCsv(new TextDecoder('utf-8',{fatal:true}).decode(bytes),headers);manifest.push({file,bytes:bytes.length,sha256:sha256(bytes)});}const league=validateTables(raw);
  let hasRaw=false;try{hasRaw=(await fs.stat(path.join(input,'raw'))).isDirectory();}catch(e){if(e.code!=='ENOENT')throw e;}
  if(hasRaw)for(const run of league.data.runs)for(const[k,h]of [['sourceResultsPath','sourceResultsSha256'],['sourceMetricsPath','sourceMetricsSha256']]){const bytes=await fs.readFile(path.join(input,run[k]));need(sha256(bytes)===run[h].toLowerCase(),'raw byte hash mismatch');}
  return{...league,manifest};}
