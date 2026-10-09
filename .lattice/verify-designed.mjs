import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [graphPath, ...flags] = process.argv.slice(2);
if (!graphPath) throw new Error('Usage: node .lattice/verify-designed.mjs <graph.json> [--baseline]');
const graph = JSON.parse(await readFile(graphPath, 'utf8'));
const designed = graph.nodes.filter(node => node.attributes.layer === 'designed-v1');
const content = designed.filter(node => !['catalog', 'system', 'wave'].includes(node.kind));
const systems = designed.filter(node => node.kind === 'system');
const catalog = designed.find(node => node.kind === 'catalog');
assert(catalog, 'The original catalog root must be available');
assert.equal(catalog.attributes.originalId, catalog.attributes.revision, 'Catalog identity must come from revision');
assert.equal(catalog.attributes.revision, 'designed-v1');
const waves = catalog.attributes.implementationWaves;
assert(Array.isArray(waves) && waves.length > 0, 'Catalog waves must be available');
const waveFacets = new Map(graph.facets.filter(facet => facet.key === 'wave').map(facet => [facet.nodeId, facet.value]));
for (const node of content) {
  const actual = waveFacets.get(node.id);
  assert(Array.isArray(actual) && actual.length > 0, `Missing wave facet for ${node.id}`);
  const expected = waves.filter(wave => wave.contentIds.includes(node.attributes.originalId)).map(wave => wave.id);
  assert.deepEqual(actual, expected, `Wave membership must match catalog for ${node.id}`);
}
const waveOneEvolutions = content.filter(node => node.kind === 'evolution' && waveFacets.get(node.id).includes(waves[0].id)).length;
const ids = new Set(graph.nodes.map(node => node.id));
assert.equal(ids.size, graph.nodes.length, 'Layer-qualified identities must be unique');
for (const node of designed) assert.equal(node.id, `designed-v1:${node.attributes.originalId}`);
const influence = graph.edges.filter(edge => edge.kind === 'system-influence');
const systemIds = new Set(systems.map(node => node.id));
for (const edge of influence) {
  assert(edge.directed, 'Influence is row to column');
  assert(systemIds.has(edge.source) && systemIds.has(edge.target), 'Influence remains inside the design system layer');
}
assert(graph.views.some(view => view.id === 'designed-influence' && view.type === 'matrix'));
const findings = graph.findings.filter(finding => finding.id.startsWith('designed-'));
assert(findings.length > 0);
for (const finding of findings) {
  assert.equal(finding.severity, 'warning');
  assert.equal(finding.gate, undefined, 'Designed findings are advisory, never gates');
}
const metric = id => {
  const finding = findings.find(value => value.id === id);
  assert(finding, `Missing finding ${id}`);
  return finding.metrics.count;
};
const itemCount = originalId => {
  const id = content.find(node => node.attributes.originalId === originalId)?.id;
  assert(id, `Missing content ${originalId}`);
  return new Set(graph.edges.filter(edge => edge.target === id && ['item-tool', 'item-weapon'].includes(edge.kind)).map(edge => edge.source)).size;
};
const observed = {
  noEvolutionWeapons: metric('designed-no-evolution-weapon'),
  noItemWeapons: metric('designed-no-item-weapon'),
  seedBagLinkedItems: itemCount('core:seed_bag'),
  carpenterHammerLinkedItems: itemCount('core:carpenter_hammer'),
  noXpReturnTools: metric('designed-no-xp-return'),
  waveOneEvolutions,
};
assert.equal(metric('designed-wave-one-evolutions'), waveOneEvolutions, 'Wave finding must agree with actual memberships');
// This oracle describes the requested historical commit only. Current catalogs have no fixed count gate.
if (flags.includes('--baseline')) {
  assert.equal(content.length, 151, 'Historical baseline content count');
  assert.deepEqual(observed, {
  noEvolutionWeapons: 6,
  noItemWeapons: 15,
  seedBagLinkedItems: 11,
  carpenterHammerLinkedItems: 7,
  noXpReturnTools: 12,
  waveOneEvolutions: 0,
  });
}
console.log(JSON.stringify({ baseline: flags.includes('--baseline'), content: content.length, systems: systems.length, influenceEdges: influence.length, findings: findings.map(({ id, metrics }) => ({ id, metrics })), observed }, null, 2));
