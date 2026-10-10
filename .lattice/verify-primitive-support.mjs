import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { verifyRuntimePrograms } from './verify-runtime-program-support.mjs';

const [graphPath, rootArg = '.'] = process.argv.slice(2);
assert(graphPath, 'Usage: verify-primitive-support.mjs <graph.json> [source-root]');
const root = resolve(rootArg);
const json = path => JSON.parse(readFileSync(join(root, path), 'utf8'));
const graph = JSON.parse(readFileSync(graphPath, 'utf8'));
const ledger = json('docs/design/runtime-primitive-support.json');
const catalog = json('data/system-design-v1.json');
assert.deepEqual(Object.keys(ledger).sort(), ['designRevision', 'evidenceLevel', 'parameterSource', 'profile', 'runtimeOnlyMappings', 'runtimeSource', 'schemaVersion', 'units']);
assert.equal(ledger.schemaVersion, 1);
assert.equal(ledger.designRevision, catalog.revision);
assert.equal(ledger.evidenceLevel, 'static-source-audit');
const ids = values => values.map(value => value.id).sort();
assert.deepEqual(ids(ledger.units), ids(catalog.primitiveContract.units), 'Every registered unit has a support boundary');
assert.equal(new Set(ids(ledger.units)).size, ledger.units.length, 'No duplicate support units');
const supportSource = readFileSync(join(root, ledger.parameterSource), 'utf8');
const contracts = new Map([...supportSource.matchAll(/\["(unit:[^"]+)"\]\s*=\s*new IReadOnlyDictionary<string, string>\[\]\s*\{([\s\S]*?)\n\s*\},/g)].map(([, id, body]) => [id, [...body.matchAll(/new Dictionary<string, string>\(StringComparer\.Ordinal\)\s*\{([^}]+)\}/g)].map(([, fields]) => Object.fromEntries([...fields.matchAll(/\["([^"]+)"\]\s*=\s*"([^"]*)"/g)].map(([, key, value]) => [key, value])))]));
assert(contracts.size > 0, 'Core parameter registry must be extracted; parser drift fails closed');
for (const id of contracts.keys()) assert(ledger.units.some(unit => unit.id === id), `${id}: Core registry cannot bypass the design registry`);
const wave = catalog.implementationWaves.find(value => value.id === ledger.profile);
assert(wave, 'Ledger profile exists');
const selected = catalog.content.filter(record => wave.contentIds.includes(record.id));
const keys = ['id', 'name', 'staticSupport', 'supportedParameterSets', 'parameterSummary', 'handlers', 'handlerSummary', 'tests', 'testSummary', 'observed', 'semantic', 'visual', 'limitation', 'designReferences', 'runtimeProgramReferences', 'referenceSummary', 'operationSupportIds'].sort();
for (const unit of ledger.units) {
  assert.deepEqual(Object.keys(unit).sort(), keys, `${unit.id}: strict ledger fields`);
  assert(['partial-runtime', 'projection-boundary', 'loader-boundary', 'unsupported'].includes(unit.staticSupport));
  assert.equal(unit.observed, 'not-assessed', 'Static ledger cannot assert actual runtime observations');
  assert.equal(unit.semantic, 'not-established', 'Static ledger cannot approve whole-unit semantics');
  assert.equal(unit.visual, 'not-assessed', 'Static ledger cannot approve readability');
  assert.equal(unit.designReferences, selected.filter(record => record.primitives.includes(unit.id)).length);
  assert.deepEqual(unit.operationSupportIds, catalog.primitiveContract.units.find(value => value.id === unit.id).operationSupportIds, 'Legacy links preserved');
  assert.deepEqual(unit.supportedParameterSets, contracts.get(unit.id) ?? [], `${unit.id}: exact Core accepted tuples`);
  assert.equal(unit.parameterSummary, `${unit.supportedParameterSets.length}개 조합 · 상세에서 확인`);
  assert.equal(unit.staticSupport === 'unsupported', !contracts.has(unit.id));
  assert.equal(typeof unit.limitation, 'string');
  assert(unit.limitation.length > 20, 'Partial support must explain limits');
  assert(unit.staticSupport === 'unsupported' || unit.handlers.length > 0, 'Supported boundary requires handler evidence');
  for (const evidence of [...unit.handlers, ...unit.tests]) {
    assert.deepEqual(Object.keys(evidence).sort(), ['path', 'symbol']);
    const source = readFileSync(join(root, evidence.path), 'utf8');
    assert(source.includes(evidence.symbol), `${unit.id}: ${evidence.path}#${evidence.symbol} exists; presence is not a passing test`);
  }
  assert.equal(unit.handlerSummary, unit.handlers.map(value => `${value.path.split('/').at(-1)}#${value.symbol}`).join('; ') || 'none');
  assert.equal(unit.testSummary, unit.tests.map(value => value.symbol).join('; ') || 'not assessed');
  const node = graph.nodes.find(value => value.kind === 'runtimePrimitive' && value.attributes.originalId === unit.id && value.attributes.layer === 'runtime-support');
  assert(node, `${unit.id}: actual extracted support node`);
  for (const key of keys) if (key !== 'id') assert.deepEqual(node.attributes[key], unit[key], `${unit.id}.${key}: extracted ledger fidelity`);
  assert(node.sources.some(source => source.path === 'docs/design/runtime-primitive-support.json' && source.line > 0), 'Ledger provenance');
  assert.equal(graph.facets.find(facet => facet.nodeId === node.id && facet.key === 'primitiveStaticSupport')?.value, unit.staticSupport);
}
assert.equal(graph.nodes.filter(value => value.kind === 'runtimePrimitive').length, ledger.units.length);
const table = graph.views.find(view => view.id === 'primitive-support');
assert.equal(table?.query.rows.length, ledger.units.length);
assert.deepEqual(table.query.columns.map(column => column.id), ['staticSupport', 'referenceSummary', 'parameterSummary', 'limitation']);
for (const row of table.query.rows) {
  const unit = ledger.units.find(unit => row.nodeId === `runtime-support:${unit.id}`);
  assert(unit);
  assert.deepEqual(row.values, Object.fromEntries(table.query.columns.map(column => [column.id, unit[column.id]])), 'Rendered table cells match the ledger');
}
const distribution = graph.views.find(view => view.id === 'primitive-support-counts');
assert(distribution);
for (const bucket of distribution.query.buckets) {
  assert.equal(bucket.count, ledger.units.filter(unit => unit.staticSupport === bucket.value).length, 'Distribution preserves distinct support boundaries');
}
assert.equal(distribution.query.buckets.reduce((sum, bucket) => sum + bucket.count, 0), ledger.units.length);
const expectedEdges = catalog.content.flatMap(record => record.primitives.map(unit => `${catalog.revision}:${record.id}>runtime-support:${unit}`)).sort();
const actualEdges = graph.edges.filter(edge => edge.kind === 'designed-primitive-support').map(edge => `${edge.source}>${edge.target}`).sort();
assert.deepEqual(actualEdges, expectedEdges, 'Design usages link to support boundaries without claiming implementation');
assert(graph.facets.some(facet => facet.key === 'codeSupport:runtime-operations'), 'Historical operation code links remain');
const runtimeCoverage = verifyRuntimePrograms({ graph, catalog, ledger, root });
console.log(JSON.stringify({ ...runtimeCoverage, evidence: ledger.evidenceLevel, units: ledger.units.length, staticSupport: Object.fromEntries([...new Set(ledger.units.map(unit => unit.staticSupport))].map(status => [status, ledger.units.filter(unit => unit.staticSupport === status).length])), designReferences: actualEdges.length, graphHash: graph.hash }));
