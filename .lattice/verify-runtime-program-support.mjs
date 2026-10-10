import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

export function verifyRuntimePrograms({ graph, catalog, ledger, root }) {
  assert.equal(ledger.runtimeSource, 'data/runtime/wave-1a.json');
  const runtime = JSON.parse(readFileSync(join(root, ledger.runtimeSource), 'utf8'));
  const programs = runtime.definition.programs;
  const expectedMappings = [
    ['core:sowing_sworddance', 'attack-kill-near-growth', 'BladePlotKill'],
    ['core:warded_masonry', 'repair-completed', 'RepairCompleted'],
    ['core:sheltered_sowing', 'harvest-near-building', 'HarvestNearBuilding'],
  ];
  assert.deepEqual(ledger.runtimeOnlyMappings.map(mapping => [mapping.recordId, mapping.condition, mapping.historyField]), expectedMappings, 'Only the three historical evolution gates are declared runtime-only extensions');
  for (const mapping of ledger.runtimeOnlyMappings) {
    assert.deepEqual(Object.keys(mapping).sort(), ['condition', 'historyField', 'origin', 'reason', 'recordId', 'unitId']);
    assert.equal(mapping.unitId, 'unit:event-gate');
    assert.equal(mapping.origin, 'legacy-wave-1a-eligibility');
    assert(mapping.reason.length > 20);
    assert.deepEqual(programs[mapping.recordId]?.params[mapping.unitId], { on: 'equipment-offer', condition: mapping.condition, scope: 'self', consume: 'none' });
    const source = readFileSync(join(root, 'core/src/SowSiege.Core/WaveGrowthPrimitives.cs'), 'utf8');
    assert(source.includes(`"${mapping.condition}" => state.${mapping.historyField}`), 'Extension points to the actual historical state gate');
  }
  const selected = catalog.implementationWaves.find(wave => wave.id === ledger.profile).contentIds;
  assert.deepEqual(Object.keys(programs).sort(), [...selected].sort(), 'Every selected record has exactly one authored program');
  assert.deepEqual(runtime.bindings.map(binding => binding.id).sort(), [...selected].sort(), 'Runtime program nodes originate from actual bindings');
  const expectedRuntimeEdges = [];
  const actualExtensions = [];
  for (const [id, program] of Object.entries(programs)) {
    const design = catalog.content.find(record => record.id === id);
    for (const unit of design.primitives) assert(Object.hasOwn(program.params, unit), `${id}/${unit}: design unit must remain represented`);
    for (const unit of Object.keys(program.params)) {
      const support = ledger.units.find(row => row.id === unit);
      assert(support && support.staticSupport !== 'unsupported' && support.handlers.length > 0, `${id}/${unit}: every executable unit needs a registered handler boundary`);
      if (!design.primitives.includes(unit)) actualExtensions.push(`${id}/${unit}`);
      expectedRuntimeEdges.push(`runtime-programs:${id}>runtime-support:${unit}`);
    }
  }
  assert.deepEqual(actualExtensions.sort(), ledger.runtimeOnlyMappings.map(mapping => `${mapping.recordId}/${mapping.unitId}`).sort(), 'Undeclared runtime-only unit is forbidden');
  const sourceNode = graph.nodes.find(node => node.kind === 'waveProgramCatalog');
  assert(sourceNode, 'Actual runtime definition is extracted');
  assert.deepEqual(sourceNode.attributes.programs, programs, 'Extracted programs match the actual executable source, not a copied ledger');
  assert(sourceNode.sources.some(source => source.path === ledger.runtimeSource && source.pointer === '/definition'));
  assert.equal(graph.nodes.filter(node => node.kind === 'waveProgram').length, selected.length);
  for (const unit of ledger.units) {
    assert.equal(unit.runtimeProgramReferences, Object.values(programs).filter(program => Object.hasOwn(program.params, unit.id)).length, `${unit.id}: runtime reference count comes from executable programs`);
    assert.equal(unit.referenceSummary, `${unit.designReferences} / ${unit.runtimeProgramReferences}`);
  }
  const edges = graph.edges.filter(edge => edge.kind === 'runtime-program-primitive-support');
  assert.deepEqual(edges.map(edge => `${edge.source}>${edge.target}`).sort(), expectedRuntimeEdges.sort(), 'Actual runtime program edges remain separate from design usages');
  return { runtimeProgramRecords: selected.length, runtimeProgramReferences: edges.length, declaredRuntimeExtensions: actualExtensions.length };
}
