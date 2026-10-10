import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

const [graphPath] = process.argv.slice(2);
assert(graphPath, 'Usage: test-primitive-support.mjs <graph.json>');
const original = JSON.parse(readFileSync(graphPath, 'utf8'));
const directory = mkdtempSync(join(tmpdir(), 'bs-primitive-support-'));
const cases = [
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
  console.log(JSON.stringify({ positive: 'pass', rejectedFalseClaims: cases.map(([name]) => name) }));
} finally { rmSync(directory, { recursive: true, force: true }); }
