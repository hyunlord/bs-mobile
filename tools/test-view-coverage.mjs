import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { analyze, contractFile, fixture, fixtureFile, parseEnemyRig, parseEvolutionBase, parseWaveForms, verify } from './view-coverage.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const copied = ['data/profiles', 'data/runtime', 'data/evolutions', 'data/tools', 'data/first-playable-tuning.json', 'unity/Assets/Game/View', 'unity/Assets/Game/App',
  'unity/Assets/Art/first-playable-manifest.json', 'unity/Assets/Art/wave1a-manifest.json', 'core/src/SowSiege.Core', contractFile];
function sandbox(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'view-coverage-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  for (const item of copied) fs.cpSync(path.join(root, item), path.join(dir, item), { recursive: true, filter: source => !source.endsWith('.meta') });
  return dir;
}
const json = (dir, file) => JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'));
const write = (dir, file, value) => fs.writeFileSync(path.join(dir, file), typeof value === 'string' ? value : JSON.stringify(value, null, 2));
const edit = (dir, file, from, to) => {
  const text = fs.readFileSync(path.join(dir, file), 'utf8');
  assert.ok(text.includes(from), `${file} lacks ${from}`);
  write(dir, file, text.replace(from, to));
};
const problems = dir => { const contract = json(dir, contractFile); return verify(analyze(dir, contract), contract.knownGaps); };

test('repository passes and the committed EditMode fixture is current', () => {
  const contract = json(root, contractFile);
  const result = analyze(root, contract);
  assert.deepEqual(verify(result, contract.knownGaps), []);
  assert.equal(fs.readFileSync(path.join(root, fixtureFile), 'utf8'), JSON.stringify(fixture(result, contract.knownGaps), null, 2) + '\n');
  const wave = result.profiles['wave-1a'];
  assert.ok(wave.requirements.some(item => item.key === 'enemy.rig.ram_runner.tell'));
  assert.ok(wave.requirements.some(item => item.key === 'hero.preview-b.idle'));
  assert.ok(wave.requirements.some(item => item.key === 'attack|core:sowing_sworddance|sector90'));
  assert.ok(!result.profiles['first-playable'].requirements.some(item => item.key.startsWith('hero.preview-b.')), 'rig roles are wave-scoped on main');
});

test('a Core enemy added to the wave runtime without art fails', t => {
  const dir = sandbox(t);
  const file = 'data/runtime/wave-1a.json';
  const runtime = json(dir, file);
  runtime.definition.enemies['core:mud_sleeper'] = { ...runtime.definition.enemies['core:raider'], id: 'core:mud_sleeper', kind: 'Pursuer' };
  write(dir, file, runtime);
  const found = problems(dir).join('\n');
  assert.match(found, /missing binding enemy\|core:mud_sleeper\|idle in wave-1a \(required by enemy core:mud_sleeper\)/);
  assert.match(found, /missing binding icon\|core:mud_sleeper\|default in wave-1a/);
});

test('a wave tool whose gear kind changes requires the attack form the View will resolve', t => {
  const dir = sandbox(t);
  const file = 'data/runtime/wave-1a.json';
  const runtime = json(dir, file);
  runtime.definition.gear['core:rain_ladle'].kind = 'Arc';
  write(dir, file, runtime);
  assert.match(problems(dir).join('\n'), /missing binding attack\|core:rain_ladle\|sector90 in wave-1a \(required by tool core:rain_ladle \(Arc\)\)/);
});

test('a first-playable weapon form without its attack binding fails', t => {
  const dir = sandbox(t);
  const file = 'data/profiles/first-playable.json';
  const profile = json(dir, file);
  profile.firstPlayable.weapons['core:iron_blade'].form = 'sector180';
  write(dir, file, profile);
  assert.match(problems(dir).join('\n'), /missing binding attack\|core:iron_blade\|sector180 in first-playable/);
});

test('#268 shape: an unscoped View role literal is required in every profile', t => {
  const dir = sandbox(t);
  edit(dir, 'unity/Assets/Game/View/WorldAnnouncements.cs', 'art.ResolveRole("ui.hint")', 'art.ResolveRole("hero.preview-b.idle")');
  const found = problems(dir).join('\n');
  assert.match(found, /missing role hero\.preview-b\.idle in first-playable \(required by literal unity\/Assets\/Game\/View\/WorldAnnouncements\.cs\)/);
  assert.match(found, /hero\\\.preview-b\\\. occurs 6 times/);   // the stack's concept review adds one wave-only use (pin 5)
});

test('removing the wave-only rig gate invalidates the profile scope', t => {
  const dir = sandbox(t);
  edit(dir, 'unity/Assets/Game/App/RunCoordinator.Feel.cs', 'activeHeroRig = IsWave && preferences.HeroRigPreview;', 'activeHeroRig = preferences.HeroRigPreview;');
  assert.match(problems(dir).join('\n'), /scope unity\/Assets\/Game\/View\/WorldHeroRig\.cs: cited code no longer present/);
});

test('allowing combat review in FP01 invalidates the wave-only presentation scope', t => {
  const dir = sandbox(t);
  edit(dir, 'unity/Assets/Game/App/FirstPlayableBenchmark.cs', '"--m1-combat-case", ', '');
  assert.match(problems(dir).join('\n'), /scope unity\/Assets\/Game\/View\/M1CombatPresentation\.cs: cited code no longer present/);
});

test('a new enemy rig kind requires its rig roles', t => {
  const dir = sandbox(t);
  edit(dir, 'unity/Assets/Game/View/EnemyRigAtlas.cs', '"ram_runner", "wine_wasp" }', '"ram_runner", "wine_wasp", "flood_tusk" }');
  edit(dir, 'unity/Assets/Game/View/EnemyRigAtlas.cs', 'WaveEnemyKind.Ranged => 5,', 'WaveEnemyKind.Ranged => 5, WaveEnemyKind.FloodBoss => 6,');
  assert.match(problems(dir).join('\n'), /missing role enemy\.rig\.flood_tusk\.idle in wave-1a/);
});

test('known gaps must cite an issue and are removed once resolved', t => {
  const dir = sandbox(t);
  edit(dir, 'unity/Assets/Game/View/WorldAnnouncements.cs', 'art.ResolveRole("ui.hint")', 'art.ResolveRole("ui.absent")');
  const contract = json(dir, contractFile);
  contract.knownGaps = ['first-playable', 'wave-1a'].map(profile => ({ profile, type: 'role', key: 'ui.absent', issue: '#274' }));
  write(dir, contractFile, contract);
  assert.deepEqual(problems(dir), []);
  contract.knownGaps.push({ profile: 'wave-1a', type: 'role', key: 'ui.hint', issue: 'later' });
  write(dir, contractFile, contract);
  const found = problems(dir).join('\n');
  assert.match(found, /needs an issue reference/);
  assert.match(found, /known gap is resolved or no longer required; remove it .*wave-1a\|role\|ui\.hint/);
});

test('per-ID presentation tables must list every wave weapon and evolution', t => {
  const dir = sandbox(t);
  const table = 'unity/Assets/Resources/StageTable.json';
  fs.mkdirSync(path.dirname(path.join(dir, table)), { recursive: true });
  const runtime = json(dir, 'data/runtime/wave-1a.json').definition;
  const gear = Object.fromEntries(['core:iron_blade', 'core:ward_orbit', 'core:storm_fork', 'core:ember_wand'].map(id => [runtime.gear[id].designRef, 'normal']));
  write(dir, table, { gear, evolutions: Object.fromEntries(Object.keys(runtime.evolutions).map(id => [id, 'evolved'])) });
  const contract = json(dir, contractFile);
  contract.presentationTables = [{ file: table, profiles: ['wave-1a'], sections: [{ path: 'gear', key: 'designRef', kinds: ['weapon'] }, { path: 'evolutions', key: 'id', kinds: ['evolution'] }] }];
  write(dir, contractFile, contract);
  const found = problems(dir);
  assert.equal(found.length, 1, found.join('\n'));
  assert.match(found[0], /missing table .*StageTable\.json#gear:data\/system-design-v1\.json@designed-v1\.1#core:harvest_scythe in wave-1a/);
});

test('source parsers read the View rules they depend on', () => {
  assert.deepEqual(parseWaveForms('var form=gear.Kind==WaveAttackKind.Arc?"sector90":gear.Kind==WaveAttackKind.SeedFan?"projectile":"wave";'), { forms: { Arc: 'sector90', SeedFan: 'projectile' }, fallback: 'wave' });
  assert.deepEqual(parseEvolutionBase('if(kind==(evolution.Kind==WaveEvolutionKind.ShelteredPlot?WaveAttackKind.ConstructionSlam:WaveAttackKind.Arc) && x)'), { kind: 'ShelteredPlot', then: 'ConstructionSlam', otherwise: 'Arc' });
  const rig = parseEnemyRig('static readonly string[] Names = { "a", "b" };\nvar prefix = "enemy.rig." + Names[i] + ".";\nResolveRole(prefix + "idle");\nWaveEnemyKind.X => 0, WaveEnemyKind.Y => 1');
  assert.deepEqual(rig, { names: ['a', 'b'], prefix: 'enemy.rig.', separator: '.', poses: ['idle'], index: { X: 0, Y: 1 } });
  assert.throws(() => parseWaveForms('var form="wave";'), /could not be parsed/);
});
