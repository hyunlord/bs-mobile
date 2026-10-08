import test from 'node:test';
import assert from 'node:assert/strict';
import { numeric } from './diagnostic-report.mjs';
import { formatCsv } from './csv.mjs';
import { makeCases, pairedEffects, selectPublicCases, firstDivergence } from './diagnostic-analysis.mjs';
test('full matrix has 768 active plus96 historical distinct cases', () => {
  const cases=makeCases('full');assert.equal(cases.length,864);assert.equal(new Set(cases.map(c=>c.caseId)).size,864);assert.equal(cases.filter(c=>c.cohort==='r2').length,96);
  assert.ok(makeCases('smoke').every(c=>c.seed>=9000&&c.requestedTicks===900));
});
test('paired restricted alive time and interaction count seeds, never repeats',()=>{
  const cases=makeCases('smoke').filter(c=>c.cohort==='r3'&&c.policy==='people'&&c.peopleRule==='A'&&c.seed===9000).map((c,i)=>({...c,ticks:[900,600,300,150][i],survived:i===0}));
  // Sort order is explicit arm order in fixture, not lexical output.
  const byArm=Object.fromEntries(cases.map(c=>[c.arm,c]));for(const [arm,ticks]of Object.entries({'control':900,'offense-off':600,'interception-off':300,'both-off':150}))Object.assign(byArm[arm],{ticks,survived:arm==='control'});
  const out=pairedEffects(cases);assert.equal(out.pairs.length,3);assert.equal(out.interactions[0].ticksInteraction,150);assert.equal(out.interactions[0].survivalInteraction,1);assert.throws(()=>pairedEffects(cases.slice(1)));
});
test('first difference is limited to observed overlap',()=>{assert.equal(firstDivergence([0,1,3],[0,2,3]),1);assert.equal(firstDivergence([0,1],[0,1,2]),null);});
test('public cases include fixed eight; additional budget preserves complete pairs',()=>{
  const rows=makeCases('full').map(c=>({...c,ticks:21600,survived:c.arm==='control'}));const selected=selectPublicCases(rows,[]);assert.equal(selected.filter(x=>x.reason==='fixed').length,8);assert.ok(selected.length<=16);const ids=new Set(selected.map(r=>r.caseId));for(const r of selected.filter(r=>r.reason==='transition'))assert.ok(ids.has(r.controlCaseId));
});

test('terminal numeric headers ignore variable interception dictionary keys',()=>{
 const a=numeric({interception:{interceptCount:1,interceptedTargetKinds:{lord:1}}});
 const b=numeric({interception:{interceptCount:2,interceptedTargetKinds:{seed:2,building:1}}});
 assert.deepEqual(Object.keys(a),Object.keys(b));assert.doesNotThrow(()=>formatCsv(Object.keys(a),[a,b]));
});
