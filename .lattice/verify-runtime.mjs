import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { resolve, join } from 'node:path';

const [graphPath, rootArg = '.', oraclePath] = process.argv.slice(2);
assert(graphPath, 'Usage: verify-runtime.mjs <graph.json> [source-root] [prototype-oracle.json]');
const root = resolve(rootArg);
const read = path => JSON.parse(readFileSync(path, 'utf8'));
const graph = read(graphPath), profile = read(join(root, 'data/profiles/first-playable.json'));
const meta = read(join(root, 'data/meta/progression.json'));
const directories = { weapon: 'weapons', tool: 'tools', charter: 'charters', item: 'items', evolution: 'evolutions', enemy: 'enemies', vassal: 'vassals' };
const records = Object.entries(directories).flatMap(([kind, directory]) => readdirSync(join(root, 'data', directory)).filter(path => path.endsWith('.json')).map(path => ({ ...read(join(root, 'data', directory, path)), kind, path: `data/${directory}/${path}` })));
const selections = { weapon: profile.selection.weapons, tool: profile.selection.tools, enemy: profile.selection.enemies, charter: profile.runtime.charters, item: profile.runtime.items, evolution: profile.runtime.evolutions, vassal: [] };
const selected = record => selections[record.kind].includes(record.id);
const classify = (record, effects) => !selected(record) ? 'design' : effects.some(effect => effect.operation !== 'stat-add') ? 'unique' : ['weapon', 'tool', 'enemy'].includes(record.kind) ? 'base' : effects.length && effects.every(effect => effect.operation === 'stat-add') ? 'stat' : 'design';
const original = record => record.runtimeProjection?.effects ?? [];
const effective = record => (profile.runtimeOverrides.equipment[record.id] ?? profile.runtimeOverrides.charters[record.id] ?? profile.runtimeOverrides.items[record.id] ?? profile.runtimeOverrides.evolutions[record.id] ?? record.runtimeProjection)?.effects ?? [];
const facet = (node, key) => graph.facets.find(value => value.nodeId === node.id && value.key === key);
const classification = records.map(record => ({ id: record.id, kind: record.kind, inProfile: selected(record), depth: classify(record, original(record)) })).sort((a, b) => a.id.localeCompare(b.id, 'en'));
for (const record of records) {
  const layer = record.kind === 'vassal' ? 'candidate' : 'runtime';
  const node = graph.nodes.find(node => node.kind === record.kind && node.attributes.originalId === record.id && node.attributes.layer === layer);
  assert(node, `${record.id}: extracted ${layer} node`);
  assert(node.sources.some(source => source.path === record.path && source.line > 0), `${record.id}: source location`);
  assert.equal(facet(node, 'selected')?.value, selected(record), `${record.id}: profile membership`);
  assert.equal(facet(node, 'referenceDepth')?.value, classify(record, original(record)), `${record.id}: reference depth`);
  assert.deepEqual(facet(node, 'designIntent')?.value, record.effect ?? null, `${record.id}: authored intent`);
  if (layer === 'runtime') {
    assert.equal(facet(node, 'effectiveDepth')?.value, classify(record, effective(record)), `${record.id}: effective depth`);
    assert.deepEqual(facet(node, 'effectiveEffects')?.value ?? [], effective(record), `${record.id}: effective effects`);
  }
}
if (oraclePath) {
  const oracle = read(oraclePath);
  assert.deepEqual(classification, [...oracle.classification].sort((a, b) => a.id.localeCompare(b.id, 'en')), 'Every prototype ID, kind, selection and depth');
}
const picked = kind => records.filter(record => record.kind === kind && selected(record));
const totals = { content: records.length, profileSelected: records.filter(selected).length, referenceUnique: classification.filter(record => record.depth === 'unique').length };
const expectations = {
  'item-stat-projection': { ...totals, selected: picked('item').length, stat: picked('item').filter(record => classify(record, original(record)) === 'stat').length },
  'tool-unique-projection': { selected: picked('tool').length, unique: picked('tool').filter(record => classify(record, original(record)) === 'unique').length, ...Object.fromEntries(['land', 'building', 'people'].map(key => [key, picked('tool').filter(record => record.growth.target === key).length])) },
  'weapon-base-shapes': { selected: picked('weapon').length, forms: new Set(picked('weapon').map(record => record.growth?.attackModel ?? record.activation?.shape)).size, effectiveForms: new Set(picked('weapon').map(record => profile.firstPlayable.weapons[record.id].form)).size, ...Object.fromEntries(['rays', 'disk', 'sector90', 'sector180'].map(key => [key, picked('weapon').filter(record => (record.growth?.attackModel ?? record.activation?.shape) === key).length])) },
  'vassal-design-gap': { designed: records.filter(record => record.kind === 'vassal').length, selected: picked('vassal').length, meta: meta.vassals.length },
  'chapter-repetition': { chapters: meta.chapters.length, bosses: new Set(meta.chapters.map(chapter => chapter.bossId)).size, terrains: new Set(meta.chapters.map(chapter => chapter.terrain.map(terrain => terrain.kind).join('+'))).size },
  'economy-shared-costs': { sinks: meta.manorBuildings.length + meta.vassals.length, materials: meta.materials.length, allMaterials: [...meta.manorBuildings.map(record => record.baseCost), ...meta.vassals.map(record => record.levelCostBase)].filter(cost => meta.materials.every(material => cost[material.id] > 0)).length },
  'estate-loop-membership': Object.fromEntries(['combat', 'growth', 'people', 'harvest', 'food', 'fertility'].map(key => [key, records.filter(record => selected(record) && record.loopLinks?.some(link => link.stage === key)).length])),
  'enemy-target-diversity': { selected: picked('enemy').length, ...Object.fromEntries(['building', 'ripe', 'lord', 'seed', 'people'].map(key => [key, picked('enemy').filter(record => record.target === key).length])) },
};
if (oraclePath) {
  assert.deepEqual(totals, { content: 216, profileSelected: 77, referenceUnique: 20 });
  assert.deepEqual(expectations['estate-loop-membership'], { combat: 20, growth: 16, people: 10, harvest: 9, food: 5, fertility: 4 });
  assert.deepEqual(expectations['economy-shared-costs'], { sinks: 10, materials: 4, allMaterials: 10 });
  assert.deepEqual(expectations['chapter-repetition'], { chapters: 10, bosses: 1, terrains: 1 });
}
const ids = (records, layer = 'runtime') => records.map(record => `${layer}:${record.id}`).sort();
const targetIds = {
  'item-stat-projection': ids(picked('item')),
  'tool-unique-projection': ids(picked('tool')),
  'weapon-base-shapes': ids(picked('weapon')),
  'vassal-design-gap': [...ids(records.filter(record => record.kind === 'vassal'), 'candidate'), ...ids(meta.vassals)].sort(),
  'chapter-repetition': ids(meta.chapters),
  'economy-shared-costs': ids([...meta.manorBuildings, ...meta.vassals]),
  'estate-loop-membership': ids(records.filter(selected)),
  'enemy-target-diversity': ids(picked('enemy')),
};
for (const [id, expected] of Object.entries(expectations)) {
  const finding = graph.findings.find(finding => finding.id === id);
  assert(finding, id);
  assert.deepEqual([...finding.targetIds].sort(), targetIds[id], `${id}: exact target IDs`);
  assert(finding.sources.every(source => Number.isInteger(source.line) && source.line > 0 && typeof source.pointer === 'string'), `${id}: exact source locations`);
  for (const [key, value] of Object.entries(expected)) assert.equal(finding.metrics[key], value, `${id}.${key}`);
  assert.equal(finding.severity, 'warning', `${id}: warning only`);
  assert.equal(finding.basis, 'authored-interpretation', `${id}: interpretation distinct from data`);
  assert.equal(finding.gate, undefined, `${id}: no gate`);
  assert(finding.intent && finding.implementation, `${id}: intent and implementation`);
  assert(finding.sources.some(source => source.path.startsWith('data/') && source.line > 0), `${id}: data provenance`);
}
const support = graph.facets.filter(value => value.key === 'codeSupport:runtime-operations');
assert(support.length > 0, 'Actual code-link projections');
const runtimePath = 'core/src/SowSiege.Core/RuntimeSystem.cs';
const lines = readFileSync(join(root, runtimePath), 'utf8').split('\n');
const supportedValues = support.flatMap(facet => facet.value.values).filter(value => value.status === 'supported');
for (const operation of supportedValues) {
  assert(operation.evidence.some(source => source.path === runtimePath && lines.slice(source.line - 1, source.endLine ?? source.line).join('\n').includes(`"${operation.value}"`)), `${operation.value}: exact source handler evidence`);
}
for (const operation of ['stat-add', 'planting-bias']) assert(supportedValues.some(value => value.value === operation), `${operation}: alternate dispatch selector`);
const sinkRows = graph.views.find(view => view.id === 'sink-cost-matrix')?.query.rows;
assert.equal(sinkRows?.length, expectations['economy-shared-costs'].sinks);
for (const row of sinkRows) {
  const node = graph.nodes.find(node => node.id === row.nodeId);
  const source = [...meta.manorBuildings, ...meta.vassals].find(record => record.id === node.attributes.originalId);
  for (const material of meta.materials) assert.equal(row.values[material.id.split(':')[1]], (source.baseCost ?? source.levelCostBase)[material.id]);
}
const rewardRows = graph.views.find(view => view.id === 'reward-source-matrix')?.query.rows;
assert.equal(rewardRows?.length, meta.economy.rewardRules.length);
for (const row of rewardRows) {
  const source = meta.economy.rewardRules.find(rule => rule.materialId === row.values.material);
  assert(source);
  for (const [key, value] of Object.entries(source)) if (key !== 'materialId') assert.equal(row.values[key], value);
}
console.log(JSON.stringify({ graphHash: graph.hash, source: graph.repository.commit, totals, prototypeOracle: Boolean(oraclePath), findings: expectations, codeSupportValues: [...new Set(supportedValues.map(value => value.value))].sort(), effectiveUnique: records.filter(record => classify(record, effective(record)) === 'unique').length }));
