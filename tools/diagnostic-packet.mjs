import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { canonical } from './s4b-contract.mjs';
import { ARMS } from './diagnostic-analysis.mjs';
export const digest=value=>createHash('sha256').update(canonical(value)).digest('hex');
const hex=value=>assert.match(value,/^[0-9a-f]{64}$/i);
const integer=(value,name)=>assert.ok(Number.isSafeInteger(value)&&value>=0,`invalid ${name}`);
function finiteTree(value){if(typeof value==='number')assert.ok(Number.isSafeInteger(value),'noninteger diagnostic number');else if(value&&typeof value==='object')for(const v of Object.values(value))finiteTree(v);}
export function decodeRng(diagnostics,ticks) {
  assert.equal(diagnostics.rngTraceEncoding,'uint64le-base64-v1');assert.equal(diagnostics.rngTraceCount,ticks+1);
  const bytes=Buffer.from(diagnostics.rngTrace,'base64');assert.equal(bytes.toString('base64'),diagnostics.rngTrace,'noncanonical base64');assert.equal(bytes.length,8*(ticks+1));
  const rng=[];for(let i=0;i<=ticks;i++){const n=bytes.readBigUInt64LE(i*8);assert.ok(n<=BigInt(Number.MAX_SAFE_INTEGER));rng.push(Number(n));if(i)assert.ok(rng[i]>=rng[i-1],'RNG counter decreased');}return rng;
}
export function validatePacket(packet,c) {
  assert.equal(packet.contractVersion,1);
  const id=packet.caseIdentity;for(const field of ['seed','policy','peopleRule'])assert.equal(id[field],c[field],`case ${field}`);
  assert.equal(id.variant,c.arm);assert.equal(id.movement,'circuit');assert.equal(id.requestedTicks,c.requestedTicks);
  for(const key of ['gameplayHash','gameplayDigest','diagnosticDigest','diagnosticIdentity'])hex(packet[key]);
  const p=packet.provenance;
  assert.equal(digest({caseIdentity:id,content:p.contentSha256,profile:p.profileSha256,tuning:p.tuningSha256}),p.inputHash?.toLowerCase(),'input identity digest');
  assert.equal(digest({contractVersion:1,variant:id.variant,inputHash:p.inputHash,gameplayHash:packet.gameplayHash,diagnosticDigest:packet.diagnosticDigest,cardsDigest:packet.cardsDigest}),packet.diagnosticIdentity.toLowerCase(),'diagnostic identity digest');assert.match(p.commit,/^[0-9a-f]{40}$/i);assert.equal(typeof p.gitDirty,'boolean');for(const key of ['inputHash','profileSha256','contentSha256','sourceTreeSha256','simulationAssemblySha256','coreAssemblySha256'])hex(p[key]);
  assert.equal(packet.repeatVerification.identical,true);const repeats=packet.repeatVerification.repeats;assert.equal(repeats.length,3);assert.deepEqual(repeats.map(r=>r.repeatIndex).sort(),[0,1,2]);for(const r of repeats)for(const key of ['gameplayHash','gameplayDigest','diagnosticDigest'])assert.equal(r[key].toLowerCase(),packet[key].toLowerCase(),`repeat ${key}`);
  hex(packet.cardsDigest);assert.ok(Array.isArray(packet.cards));assert.equal(digest(packet.cards),packet.cardsDigest.toLowerCase(),'cards digest');
  let lastCardTick=-1,lastCardLevel=0;const catalog=new Set(p.cardCatalog.map(c=>c.id));
  for(const card of packet.cards){integer(card.tick,'card tick');assert.ok(card.tick>=lastCardTick&&card.tick<=packet.outcome.ticks);assert.ok(card.level>lastCardLevel);assert.ok(Array.isArray(card.offered)&&card.offered.length>0&&new Set(card.offered).size===card.offered.length);assert.ok(card.offered.every(id=>catalog.has(id)));assert.ok(card.offered.includes(card.chosen));assert.equal(typeof card.rarity,'string');lastCardTick=card.tick;lastCardLevel=card.level;}
  const o=packet.outcome;integer(o.ticks,'ticks');assert.ok(o.ticks>0&&o.ticks<=c.requestedTicks);assert.equal(typeof o.survived,'boolean');if(o.survived)assert.equal(o.ticks,c.requestedTicks);else assert.ok(o.deathCause,'death cause missing');
  for(const key of ['level','weaponDamage','toolActivationDamage','toolGrowthDamage','allyDamage','killExperience','harvestExperience','taxExperience','harvests','randomDraws'])integer(o[key],key);
  const d=packet.diagnostics;finiteTree(d);assert.equal(d.variant,ARMS.indexOf(c.arm));const rng=decodeRng(d,o.ticks);assert.equal(rng.at(-1),o.randomDraws);
  const {rngTraceEncoding,rngTraceCount,...full}=d;full.rngTrace=rng.map((draws,tick)=>({tick,draws}));assert.equal(digest(full),packet.diagnosticDigest.toLowerCase(),'diagnostic digest mismatch');
  const expected=[0];for(let t=300;t<o.ticks;t+=300)expected.push(t);expected.push(o.ticks);assert.deepEqual(d.snapshots.map(s=>s.tick),expected,'snapshot grid incomplete');
  for(let i=0;i<d.snapshots.length;i++){const s=d.snapshots[i],last=i===d.snapshots.length-1;assert.equal(s.terminal,last);assert.equal(s.survived,last?o.survived:true);assert.equal(s.censorKind,last?(o.survived?'duration':'death'):'none');for(const key of ['equipment','population','attackSources','targetDamage'])assert.ok(Array.isArray(s[key]));for(const key of ['interception','growth','threat'])assert.ok(s[key]&&typeof s[key]==='object');}
  const terminal=d.snapshots.at(-1);for(const key of ['level','food','harvests','weaponDamage','toolActivationDamage','toolGrowthDamage','allyDamage','killExperience','harvestExperience','taxExperience'])assert.equal(terminal[key],o[key],`terminal ${key}`);
  for(const key of ['attackSources','interception','targetDamage','growth','threat'])assert.equal(canonical(terminal[key]),canonical(d[key]),`terminal ledger ${key}`);
  const applied=d.attackSources.reduce((s,r)=>s+r.appliedHpDamage,0);assert.equal(applied,o.weaponDamage+o.toolActivationDamage+o.toolGrowthDamage+o.allyDamage,'actual damage ledger conservation');
  for(const a of d.attackSources){for(const [k,v]of Object.entries(a))if(typeof v==='number')integer(v,k);assert.ok(a.appliedHpDamage<=a.requestedDamage);}
  if(c.arm==='offense-off'||c.arm==='both-off')assert.equal(d.attackSources.filter(a=>a.actorKind==='person').reduce((s,a)=>s+a.appliedHpDamage,0),0);
  if(c.arm==='interception-off'||c.arm==='both-off')assert.equal(d.interception.interceptCount,0);
  return {...c,...o,gameplayHash:packet.gameplayHash,gameplayDigest:packet.gameplayDigest,diagnosticDigest:packet.diagnosticDigest,diagnosticIdentity:packet.diagnosticIdentity,packet,rng};
}
