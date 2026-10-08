import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { boundedMap } from './s4-league.mjs';
import { runProcess } from './verify-target-parity.mjs';
import { makeCases } from './diagnostic-analysis.mjs';
import { validatePacket } from './diagnostic-packet.mjs';
import { verifyDiagnosticBaseline } from './diagnostic-baseline.mjs';
import { captureProvenance,validateProvenance } from './diagnostic-provenance.mjs';
import { canonical } from './s4b-contract.mjs';
import { writeAnalysis } from './diagnostic-report.mjs';
export function cliArgs(c,output,raw){const args=['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll','--data',c.cohort==='r3'?'data':'core/tests/SowSiege.Tests/Fixtures/phase0-r2/data','--profile','s4b-02','--policy',c.policy,'--people-rule',c.peopleRule,'--seed',String(c.seed),'--movement','circuit','--iterations','3','--diagnostic-variant',c.arm,'--diagnostic-output',output];if(c.requestedTicks!==21600)args.push('--duration-ticks',String(c.requestedTicks));if(raw)args.push('--diagnostic-raw-output',raw);return args;}
export async function run(mode,output,workers=4){
  assert.ok(output,'output directory required');assert.ok(Number.isInteger(workers)&&workers>=1&&workers<=4);
  const cases=makeCases(mode),commit=execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),dirty=Boolean(execFileSync('git',['status','--porcelain=v1'],{encoding:'utf8'}).trim());if(mode==='full')assert.equal(dirty,false,'full experiment requires committed clean source');
  try{await fs.access(output);throw Error('output already exists');}catch(e){if(e.code!=='ENOENT')throw e;}
  await fs.mkdir(path.dirname(path.resolve(output)),{recursive:true});const staging=await fs.mkdtemp(`${path.resolve(output)}.incomplete-`);await fs.mkdir(path.join(staging,'packets'));
  const baseline=mode==='full'?await verifyDiagnosticBaseline({r2:process.env.BS_DIAGNOSTIC_R2,r3:process.env.BS_DIAGNOSTIC_R3}):[];
  const frozen=await captureProvenance();const completed=new Set();
  try{
    const rows=await boundedMap(cases,workers,async c=>{const file=path.join(staging,'packets',`${c.caseId}.json`);await runProcess(process.env.DOTNET??'dotnet',cliArgs(c,file),c.caseId);const bytes=await fs.readFile(file),packet=JSON.parse(bytes);const row=validatePacket(packet,c);validateProvenance(packet.provenance,frozen,c.cohort);assert.equal(packet.provenance.commit,commit);assert.equal(packet.provenance.gitDirty,dirty);
      if(mode==='full'&&c.arm==='control'){const expected=baseline.filter(b=>b.cohort===c.cohort&&b.policy===c.policy&&b.peopleRule===c.peopleRule&&Number(b.seed)===c.seed);assert.equal(expected.length,1);assert.equal(row.gameplayHash,expected[0].hash,'historical control hash changed');assert.equal(row.ticks,Number(expected[0].ticks));assert.equal(row.survived,expected[0].survived==='true');}
      completed.add(c.caseId);if(completed.size%8===0||completed.size===cases.length)console.log(`PASS ${completed.size}/${cases.length} ${c.caseId}`);return {...row,packetSha256:createHash('sha256').update(bytes).digest('hex')};});
    assert.equal(execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),commit,'source commit changed');
    assert.equal(canonical(await captureProvenance()),canonical(frozen),'frozen files changed during run');if(mode==='full')assert.equal(execFileSync('git',['status','--porcelain=v1'],{encoding:'utf8'}).trim(),'','source dirty after full run');
    await writeAnalysis(rows,staging,mode);await fs.rename(staging,output);return output;
  }catch(error){await fs.writeFile(path.join(staging,'failure.json'),JSON.stringify({status:'FAILED_NOT_COMPLETE',message:error.message,completed:[...completed].sort(),missing:cases.filter(c=>!completed.has(c.caseId)).map(c=>c.caseId)},null,2)+'\n');throw new Error(`${error.message}; incomplete evidence: ${staging}`);}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))run(process.argv[2],process.argv[3],Number(process.argv[4]??4)).catch(error=>{console.error(error.message);process.exitCode=1;});
