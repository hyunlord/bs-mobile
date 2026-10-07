import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
const mode = process.argv[2] ?? 'smoke';
if (!['smoke','long'].includes(mode)) throw new Error('league mode must be smoke or long');
const policies = Object.keys(JSON.parse(fs.readFileSync('data/tuning.json','utf8')).policies);
const seeds = Array.from({length:mode==='long'?128:3},(_,i)=>42+i);
const directory = `artifacts/league-${mode}`;
fs.mkdirSync(directory,{recursive:true});
const rows=[];
for (const policy of policies) for (const seed of seeds) {
  const prefix=path.join(directory,`${policy}-${seed}`);
  execFileSync('dotnet',['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll','--data','data','--seed',String(seed),'--policy',policy,'--iterations','3','--output',`${prefix}.results.json`,'--metrics',`${prefix}.metrics.json`],{stdio:'pipe'});
  const results=JSON.parse(fs.readFileSync(`${prefix}.results.json`,'utf8'));
  if(results.length!==3 || new Set(results.map(r=>r.hash)).size!==1) throw new Error(`Determinism failure ${policy}/${seed}`);
  rows.push({policy,seed,...JSON.parse(fs.readFileSync(`${prefix}.metrics.json`,'utf8'))});
}
const means=policies.map(policy=>{const group=rows.filter(r=>r.policy===policy);return {policy,damage:group.reduce((a,r)=>a+r.damage,0)/group.length,growth:group.reduce((a,r)=>a+r.growth,0)/group.length};});
const mean=means.reduce((a,r)=>a+r.damage,0)/means.length;
const cv=Math.sqrt(means.reduce((a,r)=>a+(r.damage-mean)**2,0)/means.length)/mean;
const baseline=rows.find(r=>r.policy==='mixed'&&r.seed===42);
if(!baseline) throw new Error('Missing baseline');
const metrics={...baseline,balanceDispersion:null,scaffoldDamageCv:cv,balanceMetric:'coefficient-of-variation of policy mean scaffold damage; NOT gameplay balance',league:{mode,seeds,policies,runCount:rows.length,repeatCount:3,policyMeans:means}};
fs.writeFileSync('artifacts/metrics.json',JSON.stringify(metrics,null,2)+'\n');
fs.writeFileSync(`${directory}/summary.json`,JSON.stringify(metrics,null,2)+'\n');
console.log(`S0 scaffold ${mode}: ${rows.length} policy/seed cases × 3 deterministic repeats; damage CV=${cv}; no gameplay balance claim.`);
