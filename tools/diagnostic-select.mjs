import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { parseCsv,formatCsv } from './csv.mjs';
import { makeCases,selectPublicCases } from './diagnostic-analysis.mjs';
import { validatePacket } from './diagnostic-packet.mjs';
import { cliArgs } from './diagnostic-runner.mjs';
import { runProcess } from './verify-target-parity.mjs';
export async function select(input,output){
  const rows=parseCsv(await fs.readFile(path.join(input,'runs.csv'),'utf8')).map(r=>({...r,seed:Number(r.seed),ticks:Number(r.ticks),survived:r.survived==='true'}));
  const selected=selectPublicCases(rows);assert.ok(selected.length>=8&&selected.length<=16);await fs.mkdir(output);
  const cases=makeCases('full'),proofs=[];
  for(const choice of selected){const c=cases.find(c=>c.caseId===choice.caseId);assert.ok(c);const original=JSON.parse(await fs.readFile(path.join(input,'packets',`${c.caseId}.json`),'utf8'));validatePacket(original,c);const compact=path.resolve(output,`${c.caseId}.packet.json`),raw=path.resolve(output,`${c.caseId}.results.json`);
    await runProcess(process.env.DOTNET??'dotnet',cliArgs(c,compact,raw),`selected/${c.caseId}`);const packet=JSON.parse(await fs.readFile(compact,'utf8'));validatePacket(packet,c);
    for(const key of ['gameplayHash','gameplayDigest','diagnosticDigest','diagnosticIdentity'])assert.equal(packet[key],original[key],`selected rerun changed ${key}`);
    for(const key of ['commit','sourceTreeSha256','contentSha256','simulationAssemblySha256','coreAssemblySha256'])assert.equal(packet.provenance[key],original.provenance[key],`selected rerun changed provenance ${key}`);
    const results=JSON.parse(await fs.readFile(raw,'utf8'));assert.equal(results.length,3);assert.ok(results.every(r=>r.hash===original.gameplayHash));proofs.push({...choice,gameplayHash:packet.gameplayHash,diagnosticDigest:packet.diagnosticDigest,rerunIdentical:true});
  }
  await fs.writeFile(path.join(output,'selection.csv'),formatCsv(Object.keys(proofs[0]),proofs));return proofs;
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))select(process.argv[2],process.argv[3]).catch(e=>{console.error(e.message);process.exitCode=1;});
