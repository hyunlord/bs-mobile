import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { parseCsv,formatCsv } from './csv.mjs';
const identity=['cohort','policy','peopleRule','seed','arm'];
const drop=(row,keys)=>Object.fromEntries(Object.entries(row).filter(([key])=>!keys.includes(key)));
const sha=b=>createHash('sha256').update(b).digest('hex');
export async function compact(input,output,budget=4500000){
  const absent=async()=>{try{await fs.lstat(output);throw Object.assign(new Error('output already exists'),{code:'EEXIST'});}catch(error){if(error.code!=='ENOENT')throw error;}};
  await absent();
  const manifest=[];
  const read=async name=>{const bytes=await fs.readFile(path.join(input,name));manifest.push({file:name,sha256:sha(bytes),bytes:bytes.length});return parseCsv(bytes.toString());};
  const [runs,provenance,terminal,snapshots,attacks,population,equipment,cards]=await Promise.all(['runs.csv','provenance.csv','terminal-counters.csv','snapshots.csv','attack-sources.csv','population.csv','equipment.csv','card-choices.csv'].map(read));
  const cases=new Map(runs.map(r=>[r.caseId,r]));assert.equal(cases.size,runs.length,'duplicate caseId');
  for(const rows of [provenance,terminal,snapshots.filter(r=>r.caseId),attacks,population,equipment,cards])for(const row of rows){assert.ok(cases.has(row.caseId));for(const k of identity)assert.equal(row[k],cases.get(row.caseId)[k]);}
  assert.equal(provenance.length,runs.length);assert.equal(terminal.length,runs.length);
  const provenanceColumns=['commit','gitDirty','contentSha256','profileSha256','tuningSha256','sourceTreeSha256','coreAssemblySha256','simulationAssemblySha256'];
  const unique=new Map();for(const p of provenance){const tuple=JSON.stringify(provenanceColumns.map(k=>p[k]));unique.set(tuple,null);}const tuples=[...unique.keys()].sort();tuples.forEach((tuple,i)=>unique.set(tuple,`p${i+1}`));
  const provenanceRows=tuples.map(tuple=>({provenanceId:unique.get(tuple),...Object.fromEntries(provenanceColumns.map((k,i)=>[k,JSON.parse(tuple)[i]]))}));
  const pByCase=new Map(provenance.map(p=>[p.caseId,p]));assert.equal(pByCase.size,runs.length);
  const compactRuns=runs.map(r=>{const p=pByCase.get(r.caseId);return {...r,packetSha256:p.packetSha256,inputHash:p.inputHash,provenanceId:unique.get(JSON.stringify(provenanceColumns.map(k=>p[k])))};});
  const snapshotKeys=[...new Set(snapshots.flatMap(s=>Object.keys(JSON.parse(s.aliveSumsJson))))].sort();
  const compactSnapshots=snapshots.map(s=>{const values=JSON.parse(s.aliveSumsJson);for(const value of Object.values(values))assert.ok(Number.isFinite(value));return {...drop(s,['aliveSumsJson','denominator']),...Object.fromEntries(snapshotKeys.map(k=>[`sum_${k}`,Object.hasOwn(values,k)?values[k]:null]))};});
  const targetDamage=[],interceptKinds=[];
  const compactTerminal=terminal.map(t=>{for(const row of JSON.parse(t.targetDamageJson))targetDamage.push({caseId:t.caseId,...row});for(const[k,v]of Object.entries(JSON.parse(t.interceptedTargetKindsJson)).sort())interceptKinds.push({caseId:t.caseId,targetKind:k,count:v});return drop(t,[...identity,'targetDamageJson','interceptedTargetKindsJson']);});
  const weapons=equipment.filter(e=>e.kind==='weapon'),weaponIds=new Set(weapons.map(e=>e.id));assert.ok(weaponIds.size>0,'missing weapon catalog evidence');
  const weaponSummary=[];
  for(const run of runs)for(const id of [...weaponIds].sort()){
    const offered=cards.filter(c=>c.caseId===run.caseId&&JSON.parse(c.offeredJson).includes(id));
    const chosen=cards.filter(c=>c.caseId===run.caseId&&c.chosen===id);
    const held=weapons.filter(e=>e.caseId===run.caseId&&e.id===id);assert.ok(held.length<=1);
    const finalRank=held.length?Number(held[0].rank):0;
    weaponSummary.push({caseId:run.caseId,weaponId:id,offerCount:offered.length,chosenCount:chosen.length,firstOfferedTick:offered.length?Number(offered[0].tick):null,firstChosenTick:chosen.length?Number(chosen[0].tick):null,lastOfferedTick:offered.length?Number(offered.at(-1).tick):null,lastChosenTick:chosen.length?Number(chosen.at(-1).tick):null,rarityCountsJson:JSON.stringify(Object.fromEntries([...new Set(chosen.map(c=>c.rarity))].sort().map(r=>[r,chosen.filter(c=>c.rarity===r).length]))),finalRank});
  }
  const tables={'runs.csv':compactRuns,'provenance.csv':provenanceRows,'terminal-counters.csv':compactTerminal,'target-damage.csv':targetDamage,'intercept-target-kinds.csv':interceptKinds,'snapshots.csv':compactSnapshots,'attack-sources.csv':attacks.map(r=>drop(r,identity)),'population.csv':population.map(r=>drop(r,identity)),'weapon-equipment.csv':weapons.map(r=>drop(r,[...identity,'kind'])),'weapon-card-summary.csv':weaponSummary};
  for(const name of ['paired.csv','interactions.csv','paired-summary.csv','policy-effect-differences.csv','public-selection.csv'])tables[name]=await read(name);
  const emptyHeaders={'target-damage.csv':['caseId','targetKind','attackAttempts','rawTargetDamage','appliedTargetDamage'],'intercept-target-kinds.csv':['caseId','targetKind','count'],'public-selection.csv':['caseId','reason','controlCaseId']};
  const contents=new Map();for(const[name,rows]of Object.entries(tables))contents.set(name,formatCsv(rows.length?Object.keys(rows[0]):emptyHeaders[name],rows));
  manifest.sort((a,b)=>a.file.localeCompare(b.file));const manifestText=formatCsv(['file','sha256','bytes'],manifest);contents.set('source-csv.csv',manifestText);
  const schema='Canonical compact diagnostics CSV v1. runs.csv joins all caseId detail rows; provenanceId joins deduplicated provenance. Packet filenames derive as packets/<caseId>.json. Snapshots are regular300tick alive-only sums; blank means no observed metric, zero is measuredzero. Actual deathterminal counters retained separately. Attack suppressed damage is hypothetical not added to appliedHP. Intercept counts are not prevented Lorddamage. Weapon summary reports actual offers/choices and observed final rank; no rarity-to-rank conversion or causal attribution; equipment only weapons. Full card chronology/tool+charter equipment stays local; not lossless compression of all source CSVs. Source manifests bind original CSV bytes.\n';contents.set('SCHEMA.txt',schema);
  const totalBytes=[...contents.values()].reduce((sum,text)=>sum+Buffer.byteLength(text),0);
  assert.ok(totalBytes<budget,`compact budget exceeded: ${totalBytes} >= ${budget}`);
  const staging=await fs.mkdtemp(`${path.resolve(output)}.incomplete-`);
  try{for(const[name,text]of contents)await fs.writeFile(path.join(staging,name),text);await absent();await fs.rename(staging,output);}
  catch(error){await fs.rm(staging,{recursive:true,force:true});throw error;}
  return {caseCount:runs.length,snapshotCount:snapshots.length,totalBytes,tables};
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))compact(process.argv[2],process.argv[3],Number(process.argv[4]??4500000)).then(r=>console.log(JSON.stringify({cases:r.caseCount,snapshots:r.snapshotCount,bytes:r.totalBytes}))).catch(e=>{console.error(e.stack);process.exitCode=1;});
