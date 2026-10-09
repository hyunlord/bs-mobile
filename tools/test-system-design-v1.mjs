import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import Ajv2020 from 'ajv/dist/2020.js';
import { validateSystemDesign } from './system-design-v1.mjs';
import { jsonFiles } from './content-files.mjs';

const schema = JSON.parse(await readFile(new URL('../data/schema/system-design-v1.schema.json', import.meta.url), 'utf8'));
const validate = new Ajv2020({ allErrors: true, strict: true }).compile(schema);
const research = await readFile(new URL('../docs/research/mechanics-catalog.md', import.meta.url), 'utf8');
const researchIds = new Set([...research.matchAll(/<a id="(r-[a-z0-9]+-[0-9]+)">/g)].map(match => match[1].toUpperCase()));
const dataRoot = new URL('../data/', import.meta.url);
const currentRecords = [];
const directories = {weapons:'weapon',tools:'tool',items:'item',charters:'charter',evolutions:'evolution',enemies:'enemy',vassals:'vassal',meta:'meta'};
for (const file of await jsonFiles(dataRoot.pathname)) {
  const relative = file.slice(dataRoot.pathname.length);
  const kind = directories[relative.split('/')[0]];
  if (kind) currentRecords.push({relative,kind,record:JSON.parse(await readFile(file,'utf8'))});
}
const catalog = JSON.parse(await readFile(new URL('../data/system-design-v1.json', import.meta.url), 'utf8'));
const first = (design, kind) => design.content.find(record => record.kind === kind);

test('D3 catalog passes strict schema and semantic boundaries', () => {
  assert.equal(validate(catalog), true, JSON.stringify(validate.errors));
  assert.deepEqual(validateSystemDesign(catalog, currentRecords, researchIds), []);
});

for (const [name, mutate] of [
  ['missing vassal action', d => { delete first(d,'vassal').ability.action; }],
  ['numeric vassal bonus', d => { first(d,'vassal').ability.bonus = 10; }],
  ['missing item weapon links', d => { delete first(d,'item').linkedWeaponIds; }],
  ['runtime status', d => { d.content[0].designStatus = 's4-runtime'; }],
  ['runtime projection', d => { d.content[0].runtimeProjection = {}; }],
  ['numeric tuning', d => { first(d,'weapon').damage = 12; }],
  ['nested numeric tuning', d => { first(d,'tool').growth.rate = 2; }],
  ['new source alias', d => { const r=d.content[0]; r.origin='new'; r.sourceId=r.id; }],
  ['missing revised source', d => { delete d.content.find(r=>r.origin==='revised').sourceId; }],
  ['unqualified ID', d => { d.content[0].id='weapon'; }],
  ['foreign namespace', d => { d.content[0].id='proposal:weapon'; }],
  ['blank effect cost', d => { d.content[0].effect.cost=' '; }],
  ['unknown kind', d => { d.content[0].kind='hero'; }],
  ['missing boss phase', d => { delete d.content.find(r=>r.tier==='boss').phasePatterns; }],
  ['duplicate recipe ingredient', d => { const r=first(d,'evolution');r.inputIds[1]=r.inputIds[0]; }],
]) test(`D3 schema rejects ${name}`, () => {
  const design=structuredClone(catalog); mutate(design);
  assert.equal(validate(design),false, name);
});

for (const [name, mutate, expected] of [
  ['same-form duplicate action', d=>{const w=d.content.filter(r=>r.kind==='weapon');const pair=w.filter(r=>r.form===w.find(a=>w.some(b=>a.id!==b.id&&a.form===b.form)).form);pair[1].uniqueMechanic=pair[0].uniqueMechanic;}, /requires distinct unique mechanics/],
  ['padded material source signature', d=>{const m=d.content.filter(r=>r.kind==='material');m[1].sources=structuredClone(m[0].sources);m[1].sources.push({...m[1].sources[0],action:'다른 문장으로 같은 획득처를 반복한다.'});}, /duplicate material sources contentId/],
  ['padded material sink signature', d=>{const m=d.content.filter(r=>r.kind==='material');m[1].sinks=structuredClone(m[0].sinks);m[1].sinks.push({...m[1].sinks[0],action:'다른 문장으로 같은 소비처를 반복한다.'});}, /duplicate material sinks contentId/],
  ['weapon link wrong kind', d=>{first(d,'item').linkedWeaponIds=[first(d,'tool').id];}, /unresolved proposed weapon/],
  ['weapon link missing', d=>{first(d,'item').linkedWeaponIds=['core:missing'];}, /unresolved proposed weapon/],
  ['dependency assigned later', d=>{const item=first(d,'item');const dep=item.linkedToolIds[0]??item.linkedWeaponIds[0];assert.ok(dep);d.implementationWaves.forEach(w=>{w.contentIds=w.contentIds.filter(id=>id!==item.id&&id!==dep);});d.implementationWaves[0].contentIds.push(item.id);d.implementationWaves.at(-1).contentIds.push(dep);}, /must be assigned to same or prior wave/],
  ['wrong kind namespace', d=>{first(d,'weapon').id='meta:misplaced_weapon';}, /kind requires core:/],
  ['charter shortage', d=>{d.content=d.content.filter(r=>r.kind!=='charter');}, /charter count/],
  ['tool category shortage', d=>{d.content.filter(r=>r.kind==='tool').forEach(r=>{r.targetKind='land';});}, /people tool count/],
  ['elite shortage', d=>{d.content=d.content.filter(r=>r.tier!=='elite');}, /elite enemy count/],
  ['new unapproved vassal', d=>{const r=first(d,'vassal');r.id='core:unapproved_vassal';r.origin='new';delete r.sourceId;}, /vassal must come from original roster/],
  ['duplicate material sources', d=>{const m=d.content.filter(r=>r.kind==='material');m[1].sources=structuredClone(m[0].sources);}, /sources must have distinct/],
  ['duplicate material sinks', d=>{const m=d.content.filter(r=>r.kind==='material');m[1].sinks=structuredClone(m[0].sinks);}, /sinks must have distinct/],
  ['duplicate ID', d=>{d.content[1].id=d.content[0].id;}, /duplicate proposed ID/],
  ['existing ID declared new', d=>{const r=d.content.find(r=>r.origin==='revised');r.origin='new';delete r.sourceId;}, /existing ID cannot have new origin/],
  ['renamed source', d=>{d.content.find(r=>r.origin==='revised').name='다른 이름';}, /source kind\/name/],
  ['wrong source ID', d=>{d.content.find(r=>r.origin==='revised').sourceId='core:missing';}, /sourceId must preserve/],
  ['fake research', d=>{d.content[0].researchRefs=['R-VS-99'];}, /unknown research reference/],
  ['missing proposed tool', d=>{first(d,'item').linkedToolIds=['core:missing'];}, /unresolved proposed tool/],
  ['wrong linked kind', d=>{first(d,'item').linkedToolIds=[first(d,'weapon').id];}, /unresolved proposed tool/],
  ['missing weapon quota', d=>{d.content=d.content.filter(r=>r.kind!=='weapon');}, /weapon count/],
  ['collapsed forms', d=>{d.content.filter(r=>r.kind==='weapon').forEach(r=>{r.form='blade';});}, /at least 10 forms/],
  ['unbalanced roles', d=>{d.content.filter(r=>r.kind==='weapon').forEach(r=>{r.primaryRole='crowd';});}, /roles must be balanced/],
  ['duplicate tool remnant', d=>{const t=d.content.filter(r=>r.kind==='tool'&&r.targetKind==='land');t[1].remnant.key=t[0].remnant.key;}, /duplicate remnant/],
  ['duplicate tool growth', d=>{const t=d.content.filter(r=>r.kind==='tool'&&r.targetKind==='land');t[1].growth.mechanism=t[0].growth.mechanism;}, /duplicate growth/],
  ['numeric item overflow', d=>{d.content.filter(r=>r.kind==='item').forEach(r=>{r.behaviorClass='numeric-only';});}, /exceed 20 percent/],
  ['reversed duplicate recipe', d=>{const e=d.content.filter(r=>r.kind==='evolution');e[1].inputIds=[...e[0].inputIds].reverse();}, /duplicate unordered/],
  ['self recipe', d=>{const r=first(d,'evolution');r.inputIds[0]=r.id;}, /unresolved proposed weapon\/tool/],
  ['nominal weapon tool link', d=>{const w=d.content.filter(r=>r.kind==='weapon');d.content.filter(r=>r.kind==='evolution').forEach(r=>{r.inputIds=w.slice(0,2).map(x=>x.id);r.linkedToolIds=[first(d,'tool').id];});}, /half of evolution recipes/],
  ['normal enemy as boss', d=>{first(d,'chapter').bossId=d.content.find(r=>r.tier==='normal').id;}, /chapter requires boss tier/],
  ['repeated chapter device', d=>{const c=d.content.filter(r=>r.kind==='chapter');c[1].device.key=c[0].device.key;}, /duplicate chapter device/],
  ['boss changed too soon', d=>{const c=d.content.filter(r=>r.kind==='chapter');c[0].bossId=c.at(-1).bossId;}, /boss must change/],
  ['material sink wrong kind', d=>{first(d,'material').sinks[0].contentId=first(d,'weapon').id;}, /unresolved proposed tool\/manor\/vassal/],
  ['manor input wrong kind', d=>{first(d,'manor').inputIds[0]=first(d,'weapon').id;}, /unresolved proposed material/],
  ['matrix missing cell', d=>{d.influenceMatrix.cells[0].pop();}, /every ordered system pair/],
  ['matrix wrong ordering', d=>{d.influenceMatrix.systemIds.reverse();}, /exactly match system order/],
  ['missing wave assignment', d=>{d.implementationWaves[0].contentIds.pop();}, /assign every proposed/],
  ['duplicate wave assignment', d=>{d.implementationWaves[1].contentIds.push(d.implementationWaves[0].contentIds[0]);}, /multiple implementation waves/],
  ['unknown wave system', d=>{d.implementationWaves[0].systemIds.push('core:system_absent');}, /unknown system reference/],
]) test(`D3 semantics reject ${name}`, () => {
  const design=structuredClone(catalog); mutate(design);
  assert.match(validateSystemDesign(design,currentRecords,researchIds).join('\n'),expected);
});

test('D3 integration preserves canonical pool membership and runtime selections', async () => {
  const { validateContent } = await import('./validate-content.mjs');
  const result = await validateContent(dataRoot.pathname, {fullPool:true});
  assert.deepEqual(result.errors, []);
  for (const kind of Object.values(directories).filter(kind=>kind!=='meta')) {
    assert.equal(result.gateCounts.canonicalCounts[kind], currentRecords.filter(record=>record.kind===kind).length);
  }
});
