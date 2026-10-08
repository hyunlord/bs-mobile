import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { parseCsv } from './csv.mjs';
import { canonical } from './s4b-contract.mjs';
export async function verifyDiagnosticBaseline(roots){
  const expected=parseCsv(await fs.readFile(new URL('./fixtures/diagnostic-control-baseline.csv',import.meta.url),'utf8'));
  const manifest=parseCsv(await fs.readFile(new URL('./fixtures/diagnostic-control-sources.csv',import.meta.url),'utf8'));assert.equal(manifest.length,4);assert.equal(new Set(manifest.map(m=>`${m.cohort}/${m.file}`)).size,4);
  const actual=[];for(const cohort of ['r2','r3']){assert.ok(roots[cohort],`full mode requires BS_DIAGNOSTIC_${cohort.toUpperCase()} historical CSV directory`);const tables={};for(const name of ['runs.csv','determinism.csv']){const bytes=await fs.readFile(path.join(roots[cohort],name));const m=manifest.find(m=>m.cohort===cohort&&m.file===name);assert.ok(m);assert.equal(createHash('sha256').update(bytes).digest('hex'),m.sha256,`historical ${cohort}/${name} changed`);tables[name]=parseCsv(bytes.toString());}
    for(const row of tables['runs.csv'].filter(r=>r.policy==='weapon'||cohort==='r3'&&r.policy==='people')){const repeats=tables['determinism.csv'].filter(r=>r.caseId===row.caseId);assert.equal(repeats.length,3);assert.deepEqual(repeats.map(r=>r.repeatIndex).sort(),['0','1','2']);for(const repeat of repeats)for(const k of ['hash','ticks','survived'])assert.equal(repeat[k],row[k]);actual.push({cohort,policy:row.policy,peopleRule:row.peopleRule,seed:row.seed,hash:row.hash,ticks:row.ticks,survived:row.survived});}
  }
  assert.equal(actual.length,288);assert.equal(new Set(actual.map(r=>[r.cohort,r.policy,r.peopleRule,r.seed].join('/'))).size,288);assert.equal(canonical(actual.map(canonical).sort()),canonical(expected.map(canonical).sort()),'historical control fixture differs');return expected;
}
