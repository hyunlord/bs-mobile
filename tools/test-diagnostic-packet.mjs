import test from 'node:test';
import assert from 'node:assert/strict';
import { validatePacket, digest, decodeRng } from './diagnostic-packet.mjs';
import { validateProvenance } from './diagnostic-provenance.mjs';
function fixture(){
  const z='A'.repeat(64),c={caseId:'fixture',seed:9000,policy:'people',peopleRule:'C',arm:'control',requestedTicks:300};
  const outcome={ticks:300,survived:true,deathCause:'',endReason:'fixture-duration',level:1,food:0,harvests:0,weaponDamage:0,toolActivationDamage:0,toolGrowthDamage:0,allyDamage:0,killExperience:0,harvestExperience:0,taxExperience:0,randomDraws:0};
  const ledger={attackSources:[],interception:{interceptCount:0},targetDamage:[],growth:{},threat:{}};
  const snapshots=[0,300].map(tick=>({tick,terminal:tick===300,survived:true,censorKind:tick===300?'duration':'none',...Object.fromEntries(['level','food','harvests','weaponDamage','toolActivationDamage','toolGrowthDamage','allyDamage','killExperience','harvestExperience','taxExperience'].map(k=>[k,outcome[k]])),equipment:[],population:[],...ledger}));
  const dense={variant:0,snapshots,rngTrace:Array.from({length:301},(_,tick)=>({tick,draws:0})),...ledger};
  const diagnostics={...dense,rngTrace:Buffer.alloc(301*8).toString('base64'),rngTraceCount:301,rngTraceEncoding:'uint64le-base64-v1'};
  const caseIdentity={caseId:'fixture',variant:c.arm,seed:c.seed,policy:c.policy,peopleRule:c.peopleRule,movement:'circuit',requestedTicks:300};
  const provenance={commit:'b'.repeat(40),gitDirty:false,contentSha256:z,profileSha256:z,tuningSha256:z,sourceTreeSha256:z,simulationAssemblySha256:z,coreAssemblySha256:z,cardCatalog:[]};provenance.inputHash=digest({caseIdentity,content:z,profile:z,tuning:z});
  const p={contractVersion:1,caseIdentity,provenance,gameplayHash:z,gameplayDigest:z,diagnosticDigest:digest(dense),cards:[],cardsDigest:digest([]),outcome,diagnostics};p.diagnosticIdentity=digest({contractVersion:1,variant:c.arm,inputHash:provenance.inputHash,gameplayHash:z,diagnosticDigest:p.diagnosticDigest,cardsDigest:p.cardsDigest});p.repeatVerification={identical:true,repeats:[0,1,2].map(repeatIndex=>({repeatIndex,gameplayHash:z,gameplayDigest:z,diagnosticDigest:p.diagnosticDigest}))};return{p,c};
}
test('compact packet verifies full diagnostic digest and three-repeat proof',()=>{const {p,c}=fixture();validatePacket(p,c);for(const mutate of [x=>x.repeatVerification.repeats.pop(),x=>x.repeatVerification.repeats[1].repeatIndex=0,x=>x.diagnostics.snapshots[1].food=3,x=>x.provenance.inputHash='f'.repeat(64),x=>x.cards.push({tick:1})]){const changed=structuredClone(p);mutate(changed);assert.throws(()=>validatePacket(changed,c));}});
test('RNG rejects length mismatch and decreasing cumulative draws',()=>{const {p}=fixture();assert.throws(()=>decodeRng({...p.diagnostics,rngTraceCount:300},300));const bytes=Buffer.alloc(301*8);bytes.writeBigUInt64LE(2n,0);assert.throws(()=>decodeRng({...p.diagnostics,rngTrace:bytes.toString('base64')},300));});
test('frozen provenance rejects changed DLL or input bytes',()=>{const frozen={common:{coreAssemblySha256:'a'.repeat(64)},cohorts:{r3:{profileSha256:'b'.repeat(64)}}};const p={coreAssemblySha256:'a'.repeat(64),profileSha256:'b'.repeat(64),coreAssembly:{sha256:'a'.repeat(64),targetFramework:'.NETCoreApp,Version=v8.0'}};validateProvenance(p,frozen,'r3');assert.throws(()=>validateProvenance({...p,coreAssemblySha256:'c'.repeat(64)},frozen,'r3'));});
test('full historical verification refuses unavailable source instead of trusting fixture', async()=>{
 const {verifyDiagnosticBaseline}=await import('./diagnostic-baseline.mjs');await assert.rejects(verifyDiagnosticBaseline({}),/requires BS_DIAGNOSTIC_R2/);
});
