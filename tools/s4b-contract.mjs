import { createHash } from 'node:crypto';
import { S4_HEADERS, extractS4Case } from './s4-league.mjs';
export const POLICIES = ['weapon','land','building','people','mixed','random'];
export const RULES = ['A','B','C'];
export const MOVEMENTS = ['circuit','harvest','evade'];
export const HEADERS = { ...S4_HEADERS,
  'movement-samples': 'caseId,tick,lordX,lordY,estateX,estateY,distanceSquared,movementMode'.split(','),
  'farm-wait-events': 'caseId,farmId,episode,ripeTick,endTick,endKind,observedWaitTicks,censored'.split(','),
  'movement-equality': 'peopleRule,seed,movementMode,leftCaseId,rightCaseId,leftSourceResultsPath,rightSourceResultsPath,leftTraceSha256,rightTraceSha256,commonEndTick,comparedTickCount,comparedCoordinateValues,mismatchCount'.split(','),
};
export const requireValue = (ok, message) => { if (!ok) throw Error(`S4b: ${message}`); };
export const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
export const canonical = value => Array.isArray(value) ? `[${value.map(canonical).join(',')}]` : value && typeof value === 'object' ? `{${Object.keys(value).sort().map(k=>`${JSON.stringify(k)}:${canonical(value[k])}`).join(',')}}` : JSON.stringify(value);
export const integer = (value, label, minimum = 0) => { requireValue(Number.isSafeInteger(value) && value >= minimum, `${label} integer required`); return value; };
export const totalDamage = run => run.weaponDamage + run.toolActivationDamage + run.toolGrowthDamage + run.allyDamage;
export function configuration(mode) {
  requireValue(['calibration','A','B','smoke-A','smoke-B'].includes(mode), 'unknown mode');
  const smoke = mode.startsWith('smoke-'), experiment = mode.replace('smoke-','');
  return { mode, smoke, experiment, requestedTicks: smoke ? 900 : 21600, seeds: Array.from({length:smoke?3:32},(_,i)=>(smoke?9000:experiment==='calibration'?1000:42)+i), policies: experiment==='A'?POLICIES:['random'], rules: experiment==='calibration'?['C']:RULES, movements: experiment==='B'?MOVEMENTS:['circuit'] };
}
export function makeCases(mode) {
  const c=configuration(mode);
  return c.policies.flatMap(policy=>c.rules.flatMap(peopleRule=>c.seeds.flatMap(seed=>c.movements.map(movementMode=>({caseId:`${policy}__${peopleRule}__${seed}__${movementMode}`,policy,peopleRule,seed,movementMode})))));
}
export function decodeTrace(experiment,ticks) {
  requireValue(experiment?.movementTraceEncoding==='int32le-xy-v1' && experiment.movementTraceCount===ticks+1,'trace encoding/count');
  const encoded=experiment.movementTraceBase64;
  requireValue(typeof encoded==='string' && /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(encoded),'invalid base64');
  const bytes=Buffer.from(encoded,'base64');
  requireValue(bytes.length===(ticks+1)*8 && bytes.toString('base64')===encoded,'trace length/canonical base64');
  return bytes;
}
export function extractCase(packet,mode) {
  const c=configuration(mode), {movementMode,...caseInfo}=packet.caseInfo;
  const base=extractS4Case({...packet,caseInfo},{stage:'S4b',repeatCount:3,requestedTicks:c.requestedTicks});
  const first=packet.results[0], experiment=first.experiment;
  requireValue(experiment?.movementMode===movementMode,'movement identity');
  requireValue(experiment.deathTick===(first.survived?null:first.ticks),'death tick');
  const trace=decodeTrace(experiment,first.ticks);
  for(const repeat of packet.results) requireValue(canonical(repeat.experiment)===canonical(experiment),'experiment repeat mismatch');
  requireValue(Array.isArray(experiment.movementSamples)&&experiment.movementSamples.length===base.tables.timeline.length,'movement sample count');
  const samples=experiment.movementSamples.map((row,i)=>{
    for(const key of ['tick','lordX','lordY','estateX','estateY','distanceSquared'])integer(row[key],key,['lordX','lordY','estateX','estateY'].includes(key)?-2147483648:0);
    requireValue(row.tick===base.tables.timeline[i].tick,'movement grid mismatch');
    requireValue(row.lordX===trace.readInt32LE(row.tick*8)&&row.lordY===trace.readInt32LE(row.tick*8+4),'sample/trace mismatch');
    requireValue(row.distanceSquared===(row.lordX-row.estateX)**2+(row.lordY-row.estateY)**2,'distance squared mismatch');
    return {caseId:caseInfo.caseId,...row,movementMode};
  });
  requireValue(Array.isArray(experiment.farmWaitEvents),'farm events missing');
  const waits=experiment.farmWaitEvents.map(row=>({caseId:caseInfo.caseId,...row}));
  validateWaits(waits,new Map([[caseInfo.caseId,base.tables.runs[0]]]));
  const source=packet.metrics.sourceMetadata;
  requireValue(source.movementMode===movementMode,'metadata movement mode');
  const keys=['experimentContractVersion','tuningFile','tuningSha256','experimentDefinition','experienceCurve','mixedCategoryOrder','worldUnit','threatTuning','enemyTuning'];
  requireValue(keys.every(key=>source[key]!==undefined),'experiment provenance missing');
  requireValue(/^[a-f0-9]{64}$/i.test(source.tuningSha256)&&source.worldUnit==='world-unit','tuning hash/unit');
  for(const key of ['base','linear','quadratic'])integer(source.experienceCurve[key], 'XP '+key);
  base.identity.experiment=Object.fromEntries(keys.map(key=>[key,source[key]]));
  return {...base,caseInfo:packet.caseInfo,trace,traceSha256:sha256(trace),tables:{...base.tables,'movement-samples':samples,'farm-wait-events':waits}};
}
export function validateWaits(rows,runs) {
  const seen=new Map();
  for(const row of rows){const run=runs.get(row.caseId);requireValue(run,'orphan wait');
    for(const key of ['farmId','episode','ripeTick','endTick','observedWaitTicks'])integer(row[key],key);
    requireValue(['harvest','destroyed','death-censored','duration-censored'].includes(row.endKind),'wait outcome');
    requireValue(row.ripeTick<=row.endTick&&row.endTick<=run.ticks&&row.observedWaitTicks===row.endTick-row.ripeTick,'wait interval');
    requireValue(row.censored===row.endKind.endsWith('-censored'),'wait censor flag');
    if(row.censored) requireValue(row.endTick===run.ticks&&row.endKind===(run.survived?'duration-censored':'death-censored'),'censor lifecycle');
    const key=`${row.caseId}/${row.farmId}`,prior=seen.get(key);
    requireValue(!prior || (row.episode>prior.episode&&row.ripeTick>=prior.endTick),'duplicate/overlapping farm episode');seen.set(key,row);
  }
}
export function compareMovement(entries,mode) {
  if(configuration(mode).experiment!=='A')return[];
  const rows=[];
  for(const peopleRule of RULES)for(const seed of configuration(mode).seeds){
    const group=POLICIES.map(policy=>entries.find(e=>e.caseInfo.policy===policy&&e.caseInfo.peopleRule===peopleRule&&e.caseInfo.seed===seed));
    requireValue(group.every(Boolean),'missing paired movement case');
    for(let i=0;i<group.length;i++)for(let j=i+1;j<group.length;j++){
      const left=group[i],right=group[j],l=left.tables.runs[0],r=right.tables.runs[0];
      const commonEndTick=Math.min(l.ticks,r.ticks),comparedTickCount=commonEndTick+1,comparedCoordinateValues=comparedTickCount*2;
      let mismatchCount=0;for(let offset=0;offset<comparedCoordinateValues*4;offset+=4)if(left.trace.readInt32LE(offset)!==right.trace.readInt32LE(offset))mismatchCount++;
      rows.push({peopleRule,seed,movementMode:'circuit',leftCaseId:l.caseId,rightCaseId:r.caseId,leftSourceResultsPath:l.sourceResultsPath,rightSourceResultsPath:r.sourceResultsPath,leftTraceSha256:left.traceSha256,rightTraceSha256:right.traceSha256,commonEndTick,comparedTickCount,comparedCoordinateValues,mismatchCount});
    }
  }return rows;
}
