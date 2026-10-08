import assert from 'node:assert/strict';
export const ARMS=['control','offense-off','interception-off','both-off'];
export function makeCases(mode) {
  assert.ok(['full','smoke'].includes(mode));
  const seeds=mode==='full'?Array.from({length:32},(_,i)=>42+i):[9000],rows=[];
  for(const cohort of ['r3','r2'])for(const policy of cohort==='r3'?['people','weapon']:['weapon'])for(const peopleRule of ['A','B','C'])for(const seed of seeds)for(const arm of cohort==='r3'?ARMS:['control'])rows.push({caseId:`${cohort}__${policy}__${peopleRule}__${seed}__${arm}`,cohort,policy,peopleRule,seed,arm,requestedTicks:mode==='full'?21600:900});
  return rows.sort((a,b)=>a.caseId.localeCompare(b.caseId));
}
export function firstDivergence(a,b){for(let i=0;i<Math.min(a.length,b.length);i++)if(a[i]!==b[i])return i;return null;}
export function pairedEffects(rows) {
  const groups=new Map();for(const r of rows.filter(r=>r.cohort==='r3')){const key=`${r.policy}/${r.peopleRule}/${r.seed}`;if(!groups.has(key))groups.set(key,new Map());assert.ok(!groups.get(key).has(r.arm),'duplicate arm');groups.get(key).set(r.arm,r);}
  const pairs=[],interactions=[];
  for(const group of groups.values()){
    assert.deepEqual([...group.keys()].sort(),[...ARMS].sort(),'missing arm');const c=group.get('control');
    for(const arm of ARMS.slice(1)){const t=group.get(arm);pairs.push({policy:c.policy,peopleRule:c.peopleRule,seed:c.seed,arm,controlCaseId:c.caseId,treatmentCaseId:t.caseId,controlSurvived:c.survived,treatmentSurvived:t.survived,transition:`${Number(c.survived)}->${Number(t.survived)}`,survivalDifference:Number(t.survived)-Number(c.survived),restrictedTicksDifference:t.ticks-c.ticks,commonAliveTicks:Math.min(c.ticks,t.ticks)});}
    const sum=field=>group.get('both-off')[field]-group.get('offense-off')[field]-group.get('interception-off')[field]+c[field];
    interactions.push({policy:c.policy,peopleRule:c.peopleRule,seed:c.seed,ticksInteraction:sum('ticks'),survivalInteraction:Number(group.get('both-off').survived)-Number(group.get('offense-off').survived)-Number(group.get('interception-off').survived)+Number(c.survived)});
  }
  return {pairs,interactions};
}
export function selectPublicCases(rows,failures=[]) {
  const selected=new Map(),byId=new Map(rows.map(r=>[r.caseId,r]));
  const add=(r,reason,controlCaseId='')=>selected.set(r.caseId,{caseId:r.caseId,reason,controlCaseId});
  for(const r of rows.filter(r=>r.cohort==='r3'&&r.seed===42&&r.peopleRule==='C'))add(r,'fixed');
  const fixed=selected.size;
  for(const id of [...new Set(failures)].sort()){if(selected.size-fixed===8)break;assert.ok(byId.has(id));if(!selected.has(id))add(byId.get(id),'invariant-failure');}
  for(const p of pairedEffects(rows).pairs.filter(p=>p.survivalDifference!==0).sort((a,b)=>a.treatmentCaseId.localeCompare(b.treatmentCaseId))){const ids=[p.controlCaseId,p.treatmentCaseId].filter(id=>!selected.has(id));if(selected.size-fixed+ids.length>8)continue;for(const id of ids)add(byId.get(id),'transition',p.controlCaseId);}
  return [...selected.values()];
}
export function summarizePaired(rows,field,groupFields) {
  const groups=new Map();for(const r of rows){const k=groupFields.map(f=>r[f]).join('/');if(!groups.has(k))groups.set(k,[]);groups.get(k).push(r);}
  return [...groups.values()].map(g=>{const values=g.map(r=>r[field]),mean=values.reduce((a,b)=>a+b,0)/g.length,sd=g.length>1?Math.sqrt(values.reduce((s,v)=>s+(v-mean)**2,0)/(g.length-1)):0;return {...Object.fromEntries(groupFields.map(f=>[f,g[0][f]])),metric:field,nSeedBlocks:g.length,mean,standardError:g.length>1?sd/Math.sqrt(g.length):null,intervalDefinition:'paired seed mean +/- 2.04 SE; approximate t interval for32 blocks; smoke descriptive only',lower:g.length>1?mean-2.04*sd/Math.sqrt(g.length):null,upper:g.length>1?mean+2.04*sd/Math.sqrt(g.length):null};});
}
