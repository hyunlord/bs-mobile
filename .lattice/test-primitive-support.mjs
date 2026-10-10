import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, rmSync, mkdirSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { verifyRuntimePrograms } from './verify-runtime-program-support.mjs';
import { spawnSync } from 'node:child_process';

const [graphPath] = process.argv.slice(2);
assert(graphPath, 'Usage: test-primitive-support.mjs <graph.json>');
const original = JSON.parse(readFileSync(graphPath, 'utf8'));
const directory = mkdtempSync(join(tmpdir(), 'bs-primitive-support-'));
const cases = [
  ['fabricated runtime reference count', graph => { graph.nodes.find(node => node.kind === 'runtimePrimitive').attributes.runtimeProgramReferences++; }],
  ['missing runtime program edge', graph => { graph.edges.splice(graph.edges.findIndex(edge => edge.kind === 'runtime-program-primitive-support'), 1); }],
  ['unsupported presented as implemented', graph => { graph.nodes.find(node => node.kind === 'runtimePrimitive' && node.attributes.staticSupport === 'unsupported').attributes.staticSupport = 'partial-runtime'; }],
  ['missing design usage edge', graph => { graph.edges.splice(graph.edges.findIndex(edge => edge.kind === 'designed-primitive-support'), 1); }],
  ['fabricated runtime observation', graph => { graph.nodes.find(node => node.kind === 'runtimePrimitive').attributes.observed = 'passed'; }],
];
try {
  const positive = spawnSync(process.execPath, ['.lattice/verify-primitive-support.mjs', graphPath], { encoding: 'utf8' });
  assert.equal(positive.status, 0, positive.stderr);
  for (const [name, mutate] of cases) {
    const graph = structuredClone(original);
    mutate(graph);
    const path = join(directory, 'graph.json');
    writeFileSync(path, JSON.stringify(graph));
    const result = spawnSync(process.execPath, ['.lattice/verify-primitive-support.mjs', path], { encoding: 'utf8' });
    assert.equal(result.status, 1, `${name}: verifier must reject fabricated support`);
    assert(result.stderr.includes('AssertionError'), `${name}: rejection must be assertion, not process failure`);
  }
  const ledger = JSON.parse(readFileSync('docs/design/runtime-primitive-support.json', 'utf8'));
  const catalog = JSON.parse(readFileSync('data/system-design-v1.json', 'utf8'));
  const runtime = JSON.parse(readFileSync(ledger.runtimeSource, 'utf8'));
  runtime.definition.programs['core:iron_blade'].params['unit:event-gate'] = structuredClone(runtime.definition.programs['core:sowing_sworddance'].params['unit:event-gate']);
  mkdirSync(join(directory, 'data/runtime'), { recursive: true });
  writeFileSync(join(directory, ledger.runtimeSource), JSON.stringify(runtime));
  symlinkSync(resolve('core'), join(directory, 'core'), 'dir');
  assert.throws(() => verifyRuntimePrograms({ graph: original, catalog, ledger, root: directory }), /Undeclared runtime-only unit is forbidden/);
  console.log(JSON.stringify({ positive: 'pass', rejectedFalseClaims: [...cases.map(([name]) => name), 'undeclared runtime-only unit in executable source'] }));
} finally { rmSync(directory, { recursive: true, force: true }); }
