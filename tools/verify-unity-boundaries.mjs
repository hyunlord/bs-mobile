import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2] ? path.resolve(process.argv[2]) : path.resolve(import.meta.dirname, '../unity/Assets');
const files = [];
function walk(dir) {
  for (const item of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, item.name);
    if (item.isDirectory()) walk(full); else files.push(full);
  }
}
walk(root);
const assemblies = new Map(files.filter(f => f.endsWith('.asmdef')).map(f => {
  const data = JSON.parse(fs.readFileSync(f, 'utf8'));
  return [data.name, data];
}));
for (const name of ['Game.App', 'Game.View', 'Game.Input', 'Game.Debug', 'Game.Editor', 'Tests.EditMode', 'Tests.PlayMode']) assert(assemblies.has(name), `Missing ${name}`);
function visit(name, stack = []) {
  assert(!stack.includes(name), `Assembly cycle ${[...stack, name]}`);
  for (const dep of assemblies.get(name)?.references ?? []) if (assemblies.has(dep)) visit(dep, [...stack, name]);
}
for (const name of assemblies.keys()) visit(name);
for (const name of ['Game.View', 'Game.Input', 'Game.Debug']) {
  assert(!assemblies.get(name).references.includes('Game.App'), `${name} must not own app state`);
}
for (const file of files) {
  assert(!file.endsWith('SowSiege.Sim.dll'), 'Host Sim DLL cannot ship');
  if (!file.endsWith('.cs') || file.includes('/Generated/')) continue;
  const text = fs.readFileSync(file, 'utf8');
  assert(!/using\s+SowSiege\.Sim|System\.Reflection|JsonSerializer/.test(text), `Host/reflection serializer leaked: ${file}`);
  if (file.includes('/View/')) assert(!/\bSimulation\b|\bInteractiveSession\b|InternalsVisibleTo/.test(text), `View accesses mutable simulation: ${file}`);
}
console.log(`UNITY_BOUNDARIES_PASS assemblies=${assemblies.size}`);
