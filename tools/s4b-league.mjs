import fs from 'node:fs/promises';
import path from 'node:path';
import { spawn, execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { formatCsv } from './csv.mjs';
import { boundedMap, summarizePolicyDamage } from './s4-league.mjs';
import { HEADERS, configuration, makeCases, extractCase, compareMovement, canonical, sha256, requireValue } from './s4b-contract.mjs';
export function options(args){
  const [mode,...rest]=args;configuration(mode);const result={mode,workers:2};const seen=new Set();
  for(let i=0;i<rest.length;i+=2){const key=rest[i];requireValue(['--profile','--output','--workers'].includes(key)&&rest[i+1]!==undefined&&!seen.has(key),'unknown/duplicate/incomplete option');seen.add(key);result[key.slice(2)]=rest[i+1];}
  requireValue(typeof result.profile==='string'&&/^[a-z0-9][a-z0-9_-]*$/.test(result.profile),'profile stem required');
  requireValue(typeof result.output==='string'&&result.output.length>0,'output required');requireValue(/^[1-4]$/.test(String(result.workers)),'workers 1..4');result.workers=Number(result.workers);return result;
}
function processRun(args){return new Promise((resolve,reject)=>{
  const child=spawn('dotnet',args,{stdio:['ignore','ignore','pipe']});let error='';let timedOut=false;
  const timer=setTimeout(()=>{timedOut=true;child.kill('SIGKILL');},600000);
  child.stderr.on('data',data=>{error=(error+data).slice(-16000);});child.on('error',e=>{clearTimeout(timer);reject(e);});
  child.on('close',(code,signal)=>{clearTimeout(timer);if(code===0&&!timedOut)resolve();else reject(Error(`Sim ${timedOut?'timeout':code??signal}: ${error}`));});
});}
export function assemble(entries,opt,timing){
  const c=configuration(opt.mode),cases=makeCases(opt.mode);requireValue(entries.length===cases.length,'incomplete matrix');
  const identity=entries[0].identity,tables=Object.fromEntries(Object.keys(HEADERS).map(name=>[name,[]]));
  entries.forEach((entry,i)=>{requireValue(entry.caseInfo.caseId===cases[i].caseId,'case order/matrix mismatch');requireValue(canonical(entry.identity)===canonical(identity),'source/config/runtime changed across cases');for(const[name,rows]of Object.entries(entry.tables))tables[name].push(...rows);});
  requireValue(c.smoke||identity.sourceDirty===false,'full measurement requires clean committed source');
  tables['movement-equality']=compareMovement(entries,opt.mode);
  const meta={schemaVersion:1,stage:'S4b',mode:opt.mode,profileId:identity.profileId,profileSha256:identity.profileSha256,contentSha256:identity.contentSha256,sourceCommit:identity.sourceCommit,sourceTreeSha256:identity.sourceTreeSha256,sourceDirty:identity.sourceDirty,simulationAssemblySha256:identity.simulationAssemblySha256,coreAssemblySha256:identity.coreAssemblySha256,
    runtimeJson:JSON.stringify(identity.runtime),identityJson:JSON.stringify(identity),experimentJson:JSON.stringify(identity.experiment),policiesJson:JSON.stringify(c.policies),peopleRulesJson:JSON.stringify(c.rules),seedsJson:JSON.stringify(c.seeds),movementsJson:JSON.stringify(c.movements),repeatCount:3,tickRate:identity.tickRate,requestedTicks:c.requestedTicks,configuredDurationTicks:identity.configuredDurationTicks,distinctCaseCount:cases.length,seedBlockCount:c.seeds.length,totalExecutions:cases.length*3,workerLimit:opt.workers,timelineSampleIntervalTicks:identity.timelineSampleIntervalTicks,
    rankFieldsJson:JSON.stringify(['survived','ticks','level','totalCombatDamage']),damageFormula:'weaponDamage+toolActivationDamage+toolGrowthDamage+allyDamage',foodMaterialScored:false,sourceRawHashesVerified:true,rawHashScope:'Runner verified bytes before CSV extraction. Reports compute from ten CSVs; when raw/ is present its hashes are rechecked, while CSV-only replay trusts recorded source manifests.',shortenedSimulation:c.smoke,seasonsAccelerated:false,
    invocationArgsJson:JSON.stringify(timing.args),measurementStartedAt:timing.start,measurementEndedAt:timing.end,elapsedWallMs:timing.elapsed,scope:'Matched seed design; cases and three deterministic repeats are not independent sample counts. Headless evidence, no mobile/fun claim.'};
  tables.metadata=Object.entries(meta).map(([key,value])=>({key,value}));return tables;
}
export async function run(args){
  const opt=options(args),c=configuration(opt.mode),output=path.resolve(opt.output),repo=execFileSync('git',['rev-parse','--show-toplevel'],{encoding:'utf8'}).trim();
  const relative=path.relative(repo,output);requireValue(relative.startsWith('..'+path.sep)||path.isAbsolute(relative)||relative.startsWith('artifacts'+path.sep),'raw output must be external or artifacts/');
  try{await fs.access(output);throw Error('Output already exists; use a new evidence directory');}catch(e){if(e.code!=='ENOENT')throw e;}
  await fs.mkdir(path.dirname(output),{recursive:true});const staging=await fs.mkdtemp(output+'.incomplete-');await fs.mkdir(path.join(staging,'raw'));
  const started=Date.now();
  try{
    const entries=await boundedMap(makeCases(opt.mode),opt.workers,async caseInfo=>{
      const resultsPath=`raw/${caseInfo.caseId}.results.json`,metricsPath=`raw/${caseInfo.caseId}.metrics.json`;
      const cli=['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll','--data','data','--profile',opt.profile,'--policy',caseInfo.policy,'--people-rule',caseInfo.peopleRule,'--seed',String(caseInfo.seed),'--movement',caseInfo.movementMode,'--scenario','normal','--iterations','3','--output',path.join(staging,resultsPath),'--metrics',path.join(staging,metricsPath)];if(c.smoke)cli.push('--duration-ticks','900');
      await processRun(cli);const[r,m]=await Promise.all([fs.readFile(path.join(staging,resultsPath)),fs.readFile(path.join(staging,metricsPath))]);const decode=b=>JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(b));
      const entry=extractCase({caseInfo,results:decode(r),metrics:decode(m),resultsPath,metricsPath,resultsSha256:sha256(r),metricsSha256:sha256(m)},opt.mode);
      requireValue(c.smoke||!entry.identity.sourceDirty,'full measurement requires clean source');return entry;
    });
    const ended=Date.now(),tables=assemble(entries,opt,{args,start:new Date(started).toISOString(),end:new Date(ended).toISOString(),elapsed:ended-started});
    for(const[name,headers]of Object.entries(HEADERS))await fs.writeFile(path.join(staging,name+'.csv'),formatCsv(headers,tables[name]));
    const baseline=entries.find(e=>e.caseInfo.policy==='random'&&e.caseInfo.peopleRule==='C'&&e.caseInfo.seed===c.seeds[0]&&e.caseInfo.movementMode==='circuit');
    const trend={...baseline.metrics,balanceDispersion:null,gameplayBalanceClaim:false,measurementScope:'S4b random/C/first-seed/circuit case tick p95 across three repeats; matched design, not pooled percentile.',config:{...baseline.metrics.config,mode:opt.mode,profileId:baseline.identity.profileId,seedBlocks:c.seeds,workers:opt.workers,experiment:baseline.identity.experiment},league:{mode:opt.mode,distinctCaseCount:entries.length,seedBlockCount:c.seeds.length,repeats:3}};
    if(c.experiment==='A')Object.assign(trend,summarizePolicyDamage(tables.runs));delete trend.policy;delete trend.ticks;await fs.writeFile(path.join(staging,'metrics.json'),JSON.stringify(trend,null,2)+'\n');
    await fs.rename(staging,output);console.log(`S4b ${opt.mode}: ${entries.length} distinct cases across ${c.seeds.length} matched seed blocks, three deterministic repeats; ${output}`);return output;
  }catch(e){throw Error(`${e.message}; incomplete evidence retained ${staging}`,{cause:e});}
}
if(process.argv[1]&&pathToFileURL(path.resolve(process.argv[1])).href===import.meta.url)run(process.argv.slice(2)).catch(e=>{console.error(e.message);process.exitCode=1;});
