import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [graphPath] = process.argv.slice(2);
assert(graphPath, 'Usage: node .lattice/verify-picture-map.mjs <graph.json>');
const graph = JSON.parse(await readFile(graphPath, 'utf8'));
const catalog = graph.nodes.find(node => node.kind === 'catalog' && node.attributes.layer === 'designed-v1');
assert(catalog, 'Design catalog must remain available');
const content = catalog.attributes.content;
const systems = graph.nodes.filter(node => node.kind === 'system' && node.attributes.layer === 'designed-v1');
const full = id => `${catalog.attributes.revision}:${id}`;
const hub = name => `core:system_${name}`;
const growth = tool => ['land', 'building', 'people'].includes(tool.targetKind) ? hub(tool.targetKind) : hub('tool');
const memberships = graph.edges.filter(edge => edge.kind === 'map-membership');
const facets = key => new Map(graph.facets.filter(facet => facet.key === key).map(facet => [facet.nodeId, facet.value]));
const primary = facets('mapPrimaryHub');
const statuses = facets('mapStatus');
const validHubs = new Set(systems.map(node => node.id));
const expectedHubs = record => {
  if (record.kind === 'tool') return [growth(record), hub('tool')];
  if (record.kind === 'vassal') return [hub('people'), hub('manor')];
  if (['item', 'charter', 'evolution'].includes(record.kind)) {
    const references = [...record.inputIds ?? [], ...record.linkedToolIds ?? [], ...record.linkedWeaponIds ?? []];
    const equipment = content.filter(other => ['tool', 'weapon'].includes(other.kind) && references.includes(other.id));
    if (!equipment.length) return [hub('choice')];
    return [...equipment.filter(other => other.kind === 'tool').map(growth), ...equipment.filter(other => other.kind === 'weapon').map(() => hub('weapon')), ...equipment.filter(other => other.kind === 'tool').map(() => hub('tool'))];
  }
  return [hub({ weapon: 'weapon', material: 'return', manor: 'manor', chapter: 'chapter', enemy: 'enemy' }[record.kind])];
};
for (const record of content) {
  const id = full(record.id);
  const expected = [...new Set(expectedHubs(record).map(full))];
  const actual = memberships.filter(edge => edge.source === id).map(edge => edge.target);
  assert.deepEqual(actual.toSorted(), expected.toSorted(), `${record.id} must inherit actual equipment memberships`);
  assert.equal(primary.get(id), expected[0], `${record.id} has exactly one primary; tool growth wins`);
  assert(actual.includes(primary.get(id)), 'Primary hub must be a real membership');
  for (const target of actual) assert(validHubs.has(target), 'Membership must resolve a catalog system');
  const hasProgram = graph.edges.some(edge => edge.kind === 'wave-program-design' && edge.target === id);
  assert.equal(statuses.get(id), hasProgram ? 'present' : 'absent', 'Map color must agree with actual program binding');
}
for (const name of ['movement', 'fertility', 'threat']) assert(!memberships.some(edge => edge.target === full(hub(name))), `${name} remains an empty region`);
assert.equal(systems.length, catalog.attributes.systems.length, 'Every catalog system remains a hub, including empty systems');
console.log(JSON.stringify({ pictureMapVerified: true, content: content.length, hubs: systems.length, memberships: memberships.length, primary: primary.size }));
