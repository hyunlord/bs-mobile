import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [graphPath, prototypePath] = process.argv.slice(2);
assert(graphPath, 'Usage: verify-loop-map.mjs <graph.json> [approved-prototype.html]');
const graph = JSON.parse(await readFile(graphPath, 'utf8'));
const catalog = graph.nodes.find(node => node.kind === 'catalog' && node.attributes.layer === 'designed-v1');
assert(catalog, 'Catalog must remain available');
const records = catalog.attributes.content;
const full = id => `${catalog.attributes.revision}:${id}`;
const ids = values => [...new Set(values)].sort();
const neighbors = (id, kind, direction) => ids(graph.edges.filter(edge => edge.kind === kind && edge[direction === 'out' ? 'source' : 'target'] === full(id)).map(edge => edge[direction === 'out' ? 'target' : 'source']));
const statuses = new Map(graph.facets.filter(facet => facet.key === 'mapStatus').map(facet => [facet.nodeId, facet.value]));
for (const record of records.filter(record => ['weapon', 'tool'].includes(record.kind))) {
  const evolutions = records.filter(other => other.kind === 'evolution' && other.inputIds.includes(record.id));
  assert.deepEqual(neighbors(record.id, 'evolution-input', 'out'), ids(evolutions.map(other => full(other.id))));
  const partners = ids(evolutions.flatMap(other => other.inputIds).filter(id => id !== record.id).map(full));
  const actual = ids(evolutions.flatMap(other => neighbors(other.id, 'evolution-input', 'in')).filter(id => id !== full(record.id)));
  assert.deepEqual(actual, partners, 'Two-hop focus must recover every evolution partner without self');
}
for (const chapter of records.filter(record => record.kind === 'chapter')) {
  const expected = records.filter(other => other.kind === 'enemy' && other.tier !== 'boss' && chapter.encounterChange?.includes(other.name));
  assert.deepEqual(neighbors(chapter.id, 'chapter-encounter', 'out'), ids(expected.map(other => full(other.id))), 'Encounter links retain authored name mentions, not observed spawns');
}
const live = ids(records.filter(record => statuses.get(full(record.id)) === 'present').map(record => record.id));
if (prototypePath) {
  const html = await readFile(prototypePath, 'utf8');
  const embedded = html.match(/<script type="application\/json" id="data">([\s\S]*?)<\/script>/);
  assert(embedded, 'Approved prototype data block must be preserved');
  const prototype = JSON.parse(embedded[1]);
  assert.equal(prototype.recs.length, 163, 'Approved prototype oracle, not a future catalog gate');
  assert.equal(prototype.recs.filter(record => record.live).length, 28, 'Approved prototype live oracle');
  assert.deepEqual(ids(records.map(record => record.id)), ids(prototype.recs.map(record => record.id)));
  assert.deepEqual(live, ids(prototype.recs.filter(record => record.live).map(record => record.id)));
  const seed = 'core:seed_bag';
  for (const [kind, edge, attribute, direction] of [['item', 'item-tool', 'lt', 'in'], ['charter', 'charter-tool', 'lt', 'in'], ['vassal', 'vassal-tool', 'lt', 'in'], ['evolution', 'evolution-input', 'inp', 'out']]) {
    assert.deepEqual(neighbors(seed, edge, direction), ids(prototype.recs.filter(record => record.k === kind && record[attribute]?.includes(seed)).map(record => full(record.id))));
  }
  assert.deepEqual(neighbors(seed, 'material-source', 'out'), ids(prototype.recs.filter(record => record.k === 'material' && record.src.some(source => source.id === seed)).map(record => full(record.id))));
}
console.log(JSON.stringify({ loopGraphVerified: true, content: records.length, livePrograms: live.length, prototypeParity: Boolean(prototypePath) }));
