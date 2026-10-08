import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { boundedMap } from './s4-league.mjs';
import { runProcess } from './verify-target-parity.mjs';
import { validatePacket } from './diagnostic-packet.mjs';
import { captureProvenance,validateProvenance } from './diagnostic-provenance.mjs';
import { canonical } from './s4b-contract.mjs';
import { weaponDefinitionHash } from './weapon-definition-provenance.mjs';
import { formatCsv } from './csv.mjs';
import { makeCases,reportContents,sourceIdentity } from './weapon-holdout-report.mjs';
export const PROFILE='weapon-growth-79';
const sha=b=>createHash('sha256').update(b).digest('hex');
const git=(...args)=>execFileSync('git',args,{encoding:'utf8'}).trim();
export async function rebuildForFull(commit,{readGit=git,execute=runProcess}={}){
 assert.equal(readGit('rev-parse','HEAD'),commit,'HEAD changed before build');assert.equal(readGit('status','--porcelain=v1'),'','full source must be clean before build');
 await execute(process.env.DOTNET??'dotnet',['build','core/src/SowSiege.Sim/SowSiege.Sim.csproj','--configuration','Release','--no-incremental','--target:Rebuild'],'full holdout clean-source rebuild');
 assert.equal(readGit('rev-parse','HEAD'),commit,'HEAD changed during build');assert.equal(readGit('status','--porcelain=v1'),'','source changed during build');
}
export async function frozenInputs(){const frozen=await captureProvenance(),bytes=await fs.readFile(`data/profiles/${PROFILE}.json`),p=JSON.parse(bytes);assert.equal(p.id,'core:weapon_growth_79');assert.equal(p.weaponCombat.contractVersion,2);assert.equal(p.weaponCombat.definitionsFile,'experiments/weapon-growth-79.json');assert.equal(p.experiment.tuningFile,'experiments/tuning-s4b-02.json');frozen.cohorts.r3.profileSha256=sha(bytes);frozen.cohorts.r3.tuningSha256=sha(await fs.readFile(`data/${p.experiment.tuningFile}`));frozen.weaponDefinitionSha256=await weaponDefinitionHash('data',p);return frozen;}
export function args(c,file){const result=['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll','--data','data','--profile',PROFILE,'--policy',c.policy,'--people-rule',c.peopleRule,'--seed',String(c.seed),'--movement','circuit','--scenario','normal','--iterations','3','--diagnostic-variant','control','--diagnostic-output',file];if(c.requestedTicks!==21600)result.push('--duration-ticks',String(c.requestedTicks));return result;}
export function weaponCounters(source){
 assert.equal(source.actorKind,'weapon');assert.equal(source.suppressedHpDamage,0);
 for(const k of ['attackAttempts','noTargetAttempts','emptyActivations','eligibleBeforeCap','hitCount','requestedDamage','appliedHpDamage'])assert.ok(Number.isSafeInteger(source[k])&&source[k]>=0,`candidate weapon counter ${k}`);
 assert.ok(source.noTargetAttempts<=source.emptyActivations&&source.emptyActivations<=source.attackAttempts,'empty activation bounds');
 assert.ok(source.hitCount<=source.eligibleBeforeCap,'hits exceed pre-cap eligible');assert.ok(source.appliedHpDamage<=source.requestedDamage);
 return {...source,overkillRequestedMinusApplied:source.requestedDamage-source.appliedHpDamage,noRadiusTargetRate:source.attackAttempts?source.noTargetAttempts/source.attackAttempts:null,trueEmptyActivationRate:source.attackAttempts?source.emptyActivations/source.attackAttempts:null};
}
export async function run(mode,output,workers=4){assert.ok(output);assert.ok(Number.isInteger(workers)&&workers>=1&&workers<=4);const cases=makeCases(mode).sort((a,b)=>a.caseId.localeCompare(b.caseId)),commit=git('rev-parse','HEAD'),dirty=Boolean(git('status','--porcelain=v1'));if(mode==='full')assert.equal(dirty,false,'full holdout requires clean source');
 try{await fs.lstat(output);throw Error('output already exists');}catch(e){if(e.code!=='ENOENT')throw e;}
 if(mode==='full')await rebuildForFull(commit);
 const frozen=await frozenInputs();await fs.mkdir(path.dirname(path.resolve(output)),{recursive:true});const staging=await fs.mkdtemp(`${path.resolve(output)}.incomplete-`);await fs.mkdir(path.join(staging,'packets'));const completed=new Set();
 try{
  const rows=await boundedMap(cases,workers,async c=>{const file=path.join(staging,'packets',`${c.caseId}.json`);await runProcess(process.env.DOTNET??'dotnet',args(c,file),c.caseId);const bytes=await fs.readFile(file),packet=JSON.parse(bytes),r=validatePacket(packet,c);validateProvenance(packet.provenance,frozen,'r3');assert.equal(packet.provenance.profileId,'core:weapon_growth_79');assert.equal(packet.provenance.commit,commit);assert.equal(packet.provenance.gitDirty,dirty);assert.equal(packet.provenance.gitStatusAvailable,true);assert.equal(packet.provenance.tickRate,30);assert.equal(packet.provenance.configuredDurationTicks,21600);for(const key of ['runtime','os','architecture'])assert.ok(typeof packet.provenance[key]==='string'&&packet.provenance[key].length>0,`missing runtime ${key}`);completed.add(c.caseId);if(completed.size%8===0||completed.size===cases.length)console.log(`PASS ${completed.size}/${cases.length} ${c.caseId}`);return {...r,packetSha256:sha(bytes)};});
  assert.equal(git('rev-parse','HEAD'),commit);assert.equal(Boolean(git('status','--porcelain=v1')),dirty);assert.equal(canonical(await frozenInputs()),canonical(frozen),'frozen source/data/DLL changed');
  const runs=rows.map(r=>({caseId:r.caseId,policy:r.policy,peopleRule:r.peopleRule,seed:r.seed,...r.packet.outcome,gameplayHash:r.gameplayHash,gameplayDigest:r.gameplayDigest,diagnosticDigest:r.diagnosticDigest,diagnosticIdentity:r.diagnosticIdentity,cardsDigest:r.packet.cardsDigest,packetSha256:r.packetSha256,inputHash:r.packet.provenance.inputHash,repeatCount:r.packet.repeatVerification.repeats.length,repeatVerified:r.packet.repeatVerification.identical}));
  for(let i=0;i<runs.length;i++){const p=rows[i].packet.provenance;runs[i].sourceIdentity=sourceIdentity(runs[i],{sourceCommit:p.commit,sourceTreeSha256:p.sourceTreeSha256,coreAssemblySha256:p.coreAssemblySha256,simulationAssemblySha256:p.simulationAssemblySha256});}
  const weapons=[],terminal=[],equipment=[];for(const r of rows){for(const a of r.packet.diagnostics.attackSources.filter(a=>a.actorKind==='weapon')){weapons.push({caseId:r.caseId,observedTicks:r.ticks,...weaponCounters(a)});}const s=r.packet.diagnostics.snapshots.at(-1);terminal.push({caseId:r.caseId,tick:s.tick,weaponSlots:s.weaponSlots,weaponCount:s.equipment.filter(e=>e.kind==='weapon').length,weaponRankSum:s.equipment.filter(e=>e.kind==='weapon').reduce((sum,e)=>sum+e.rank,0),...s.threat});for(const e of s.equipment.filter(e=>e.kind==='weapon'))equipment.push({caseId:r.caseId,...e});}
  const provenance=[{sourceCommit:commit,sourceDirty:dirty,profile:PROFILE,profileId:'core:weapon_growth_79',...frozen.common,...frozen.cohorts.r3,weaponDefinitionSha256:frozen.weaponDefinitionSha256,mode,seeds:mode==='full'?'20000..20031':'9100',repeatCount:3,totalExecutions:cases.length*3,tickRate:rows[0].packet.provenance.tickRate,configuredDurationTicks:rows[0].packet.provenance.configuredDurationTicks,coreTargetFramework:rows[0].packet.provenance.coreAssembly.targetFramework,runtime:rows[0].packet.provenance.runtime,os:rows[0].packet.provenance.os,architecture:rows[0].packet.provenance.architecture,distinctCases:cases.length,seedBlocks:mode==='full'?32:1,requestedTicks:mode==='full'?21600:900}];
  const contents=reportContents(runs,mode);for(const[name,table]of Object.entries({'runs.csv':runs,'weapon-sources.csv':weapons,'terminal.csv':terminal,'weapon-equipment.csv':equipment,'provenance.csv':provenance})){assert.ok(table.length);contents.set(name,formatCsv(Object.keys(table[0]),table));}
  const publicBytes=[...contents.values()].reduce((n,t)=>n+Buffer.byteLength(t),0);assert.ok(publicBytes<4500000,`canonical CSV/report budget exceeded ${publicBytes}`);for(const[name,text]of contents)await fs.writeFile(path.join(staging,name),text);await fs.rename(staging,output);console.log(`HOLDOUT ${mode}: ${cases.length} cases, ${publicBytes} canonical bytes; packets local only`);return output;
 }catch(e){await fs.writeFile(path.join(staging,'failure.json'),JSON.stringify({status:'FAILED_NOT_COMPLETE',message:e.message,completed:[...completed].sort(),missing:cases.filter(c=>!completed.has(c.caseId)).map(c=>c.caseId)},null,2)+'\n');throw new Error(`${e.message}; incomplete ${staging}`);}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))run(process.argv[2],process.argv[3],Number(process.argv[4]??4)).catch(e=>{console.error(e.message);process.exitCode=1;});
