import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { boundedMap } from './s4-league.mjs';
import { runProcess } from './verify-target-parity.mjs';
import { validatePacket,digest } from './diagnostic-packet.mjs';
import { captureProvenance,validateProvenance } from './diagnostic-provenance.mjs';
import { canonical } from './s4b-contract.mjs';
import { weaponDefinitionHash } from './weapon-definition-provenance.mjs';
import { formatCsv } from './csv.mjs';
import { makeCases,reportContents,sourceIdentity,evaluate,parseRuns } from './first-playable-report.mjs';
export const PROFILE='first-playable';
const sha=b=>createHash('sha256').update(b).digest('hex');
const git=(...args)=>execFileSync('git',args,{encoding:'utf8'}).trim();
export async function rebuildForFull(commit,{readGit=git,execute=runProcess}={}){
 assert.equal(readGit('rev-parse','HEAD'),commit,'HEAD changed before build');assert.equal(readGit('status','--porcelain=v1'),'','full source must be clean before build');
 await execute(process.env.DOTNET??'dotnet',['build','core/src/SowSiege.Sim/SowSiege.Sim.csproj','--configuration','Release','--no-incremental','--target:Rebuild'],'full holdout clean-source rebuild');
 assert.equal(readGit('rev-parse','HEAD'),commit,'HEAD changed during build');assert.equal(readGit('status','--porcelain=v1'),'','source changed during build');
}
export async function frozenInputs(){const frozen=await captureProvenance(),bytes=await fs.readFile(`data/profiles/${PROFILE}.json`),p=JSON.parse(bytes);assert.equal(p.id,'core:first_playable');assert.equal(p.weaponCombat.contractVersion,2);assert.equal(p.firstPlayable.contractVersion,1);assert.equal(p.tuningFile,'first-playable-tuning.json');frozen.cohorts.r3.profileSha256=sha(bytes);frozen.cohorts.r3.tuningSha256=sha(await fs.readFile(`data/${p.tuningFile}`));frozen.weaponDefinitionSha256=await weaponDefinitionHash('data',p);return frozen;}
export function args(c,file){const result=['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll','--data','data','--profile',PROFILE,'--policy',c.policy,'--people-rule',c.peopleRule,'--seed',String(c.seed),'--movement','circuit','--scenario','normal','--iterations','3','--diagnostic-variant','control','--diagnostic-output',file];if(c.requestedTicks!==27000)result.push('--duration-ticks',String(c.requestedTicks));return result;}
export function weaponCounters(source,coverage){
 assert.equal(source.actorKind,'weapon');assert.equal(source.suppressedHpDamage,0);
 for(const k of ['attackAttempts','noTargetAttempts','hitCount','requestedDamage','appliedHpDamage'])assert.ok(Number.isSafeInteger(source[k])&&source[k]>=0,`first playable weapon counter ${k}`);
 assert.ok(source.noTargetAttempts<=source.attackAttempts,'no-radius-target bounds');assert.ok(source.appliedHpDamage<=source.requestedDamage);
 const key=`equipment:${source.sourceId}:`;
 const completed=coverage[`${key}completed`]??0,empty=coverage[`${key}empty`]??0;
 for(const value of [completed,empty])assert.ok(Number.isSafeInteger(value)&&value>=0,'temporal completion counter');
 assert.ok(empty<=completed&&completed<=source.attackAttempts,'empty/completed/attempt activation bounds');
 return {...source,completedActivations:completed,completedEmptyActivations:empty,overkillRequestedMinusApplied:source.requestedDamage-source.appliedHpDamage,noRadiusTargetRate:source.attackAttempts?source.noTargetAttempts/source.attackAttempts:null,trueEmptyCompletedActivationRate:completed?empty/completed:null};
}
export function validateCoveragePacket(packet){
 assert.ok(packet.firstPlayableCoverage&&typeof packet.firstPlayableCoverage==='object'&&!Array.isArray(packet.firstPlayableCoverage),'observed first playable coverage required');
 for(const[key,count]of Object.entries(packet.firstPlayableCoverage)){assert.ok(key.length>0);assert.ok(Number.isSafeInteger(count)&&count>=0,'coverage count');}
 assert.match(packet.firstPlayableCoverageDigest,/^[a-f0-9]{64}$/i,'coverage digest required');
 assert.equal(digest(packet.firstPlayableCoverage),packet.firstPlayableCoverageDigest.toLowerCase(),'coverage digest mismatch');
 assert.equal(packet.repeatVerification.repeats.length,3);
 for(const repeat of packet.repeatVerification.repeats)assert.equal(repeat.firstPlayableCoverageDigest?.toLowerCase(),packet.firstPlayableCoverageDigest.toLowerCase(),'coverage repeat mismatch');
 assert.ok(packet.firstPlayableRuntime&&typeof packet.firstPlayableRuntime==='object','observed runtime ledgers required');
 assert.match(packet.firstPlayableRuntimeDigest,/^[a-f0-9]{64}$/i,'runtime ledger digest required');
 assert.equal(digest(packet.firstPlayableRuntime),packet.firstPlayableRuntimeDigest.toLowerCase(),'runtime ledger digest mismatch');
 for(const repeat of packet.repeatVerification.repeats)assert.equal(repeat.firstPlayableRuntimeDigest?.toLowerCase(),packet.firstPlayableRuntimeDigest.toLowerCase(),'runtime ledger repeat mismatch');
}
export async function run(mode,output,workers=4){assert.ok(output);assert.ok(Number.isInteger(workers)&&workers>=1&&workers<=4);const cases=makeCases(mode).sort((a,b)=>a.caseId.localeCompare(b.caseId)),commit=git('rev-parse','HEAD'),dirty=Boolean(git('status','--porcelain=v1'));if(mode==='full')assert.equal(dirty,false,'full holdout requires clean source');
 try{await fs.lstat(output);throw Error('output already exists');}catch(e){if(e.code!=='ENOENT')throw e;}
 if(mode==='full')await rebuildForFull(commit);
 const frozen=await frozenInputs();await fs.mkdir(path.dirname(path.resolve(output)),{recursive:true});const staging=await fs.mkdtemp(`${path.resolve(output)}.incomplete-`);await fs.mkdir(path.join(staging,'packets'));const completed=new Set();
 try{
  const rows=await boundedMap(cases,workers,async c=>{const file=path.join(staging,'packets',`${c.caseId}.json`);await runProcess(process.env.DOTNET??'dotnet',args(c,file),c.caseId);const bytes=await fs.readFile(file),packet=JSON.parse(bytes),r=validatePacket(packet,c);validateCoveragePacket(packet);validateProvenance(packet.provenance,frozen,'r3');assert.equal(packet.provenance.profileId,'core:first_playable');assert.equal(packet.provenance.commit,commit);assert.equal(packet.provenance.gitDirty,dirty);assert.equal(packet.provenance.gitStatusAvailable,true);assert.equal(packet.provenance.tickRate,30);assert.equal(packet.provenance.configuredDurationTicks,27000);for(const key of ['runtime','os','architecture'])assert.ok(typeof packet.provenance[key]==='string'&&packet.provenance[key].length>0,`missing runtime ${key}`);completed.add(c.caseId);if(completed.size%8===0||completed.size===cases.length)console.log(`PASS ${completed.size}/${cases.length} ${c.caseId}`);return {...r,packetSha256:sha(bytes)};});
  assert.equal(git('rev-parse','HEAD'),commit);assert.equal(Boolean(git('status','--porcelain=v1')),dirty);assert.equal(canonical(await frozenInputs()),canonical(frozen),'frozen source/data/DLL changed');
  const runs=rows.map(r=>({caseId:r.caseId,policy:r.policy,peopleRule:r.peopleRule,seed:r.seed,...r.packet.outcome,gameplayHash:r.gameplayHash,gameplayDigest:r.gameplayDigest,diagnosticDigest:r.diagnosticDigest,diagnosticIdentity:r.diagnosticIdentity,cardsDigest:r.packet.cardsDigest,packetSha256:r.packetSha256,inputHash:r.packet.provenance.inputHash,repeatCount:r.packet.repeatVerification.repeats.length,repeatVerified:r.packet.repeatVerification.identical}));
  for(let i=0;i<runs.length;i++){const p=rows[i].packet.provenance;runs[i].sourceIdentity=sourceIdentity(runs[i],{sourceCommit:p.commit,sourceTreeSha256:p.sourceTreeSha256,coreAssemblySha256:p.coreAssemblySha256,simulationAssemblySha256:p.simulationAssemblySha256});}
  const weapons=[],terminal=[],equipment=[],coverage=[],effects=[],evolutions=[];for(const r of rows){assert.ok(r.packet.firstPlayableCoverage&&typeof r.packet.firstPlayableCoverage==='object','first playable observed coverage required');for(const[key,count]of Object.entries(r.packet.firstPlayableCoverage)){assert.ok(Number.isSafeInteger(count)&&count>=0,'coverage must be observed nonnegative counts');coverage.push({caseId:r.caseId,key,count});}for(const a of r.packet.diagnostics.attackSources.filter(a=>a.actorKind==='weapon')){weapons.push({caseId:r.caseId,observedTicks:r.ticks,...weaponCounters(a,r.packet.firstPlayableCoverage)});}for(const effect of r.packet.firstPlayableRuntime.effects.filter(e=>e.activationCount>0))effects.push({caseId:r.caseId,...effect});for(const activation of r.packet.firstPlayableRuntime.evolutionActivations)evolutions.push({caseId:r.caseId,...activation});const s=r.packet.diagnostics.snapshots.at(-1);terminal.push({caseId:r.caseId,tick:s.tick,weaponSlots:s.weaponSlots,weaponCount:s.equipment.filter(e=>e.kind==='weapon').length,weaponRankSum:s.equipment.filter(e=>e.kind==='weapon').reduce((sum,e)=>sum+e.rank,0),...s.threat});for(const e of s.equipment.filter(e=>e.kind==='weapon'))equipment.push({caseId:r.caseId,...e});}
  const provenance=[{sourceCommit:commit,sourceDirty:dirty,profile:PROFILE,profileId:'core:first_playable',...frozen.common,...frozen.cohorts.r3,weaponDefinitionSha256:frozen.weaponDefinitionSha256,mode,seeds:mode==='full'?'40000..40031':'9300',repeatCount:3,totalExecutions:cases.length*3,tickRate:rows[0].packet.provenance.tickRate,configuredDurationTicks:rows[0].packet.provenance.configuredDurationTicks,coreTargetFramework:rows[0].packet.provenance.coreAssembly.targetFramework,runtime:rows[0].packet.provenance.runtime,os:rows[0].packet.provenance.os,architecture:rows[0].packet.provenance.architecture,distinctCases:cases.length,seedBlocks:mode==='full'?32:1,requestedTicks:mode==='full'?27000:900}];
  const contents=reportContents(runs,mode);for(const[name,table]of Object.entries({'runs.csv':runs,'coverage.csv':coverage,'weapon-sources.csv':weapons,'terminal.csv':terminal,'weapon-equipment.csv':equipment,'provenance.csv':provenance})){assert.ok(table.length);contents.set(name,formatCsv(Object.keys(table[0]),table));}
  contents.set('runtime-effects.csv',formatCsv(['caseId','effectId','sourceId','sourceKind','trigger','operation','subject','amount','activationCount','appliedTotal','firstActivationTick','lastActivationTick'],effects));contents.set('evolutions.csv',formatCsv(['caseId','tick','evolutionId','baseId'],evolutions));
  const publicBytes=[...contents.values()].reduce((n,t)=>n+Buffer.byteLength(t),0);for(const[name,text]of contents)await fs.writeFile(path.join(staging,name),text);await fs.rename(staging,output);console.log(`FIRST_PLAYABLE ${mode}: ${cases.length} cases, ${publicBytes} canonical bytes; packets local only`);return output;
 }catch(e){await fs.writeFile(path.join(staging,'failure.json'),JSON.stringify({status:'FAILED_NOT_COMPLETE',message:e.message,completed:[...completed].sort(),missing:cases.filter(c=>!completed.has(c.caseId)).map(c=>c.caseId)},null,2)+'\n');throw new Error(`${e.message}; incomplete ${staging}`);}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))run(process.argv[2],process.argv[3],Number(process.argv[4]??4)).then(async output=>{
 const verdict=evaluate(parseRuns(await fs.readFile(path.join(output,'runs.csv'),'utf8')),process.argv[2]);
 console.log(`First playable league gate: ${verdict.overall}`);
 if(verdict.overall==='FAIL')process.exitCode=1;
}).catch(e=>{console.error(e.message);process.exitCode=1;});
