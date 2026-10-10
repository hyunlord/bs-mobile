import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import Ajv from 'ajv/dist/2020.js';
const programSchema = JSON.parse(readFileSync(new URL('../data/schema/wave-runtime.schema.json', import.meta.url))).properties.definition.properties.programs;
const validatePrograms = new Ajv({ allErrors: true, strict: true }).compile(programSchema);

const handlers = Object.freeze({
  "core:iron_blade": "Arc",
  "core:ward_orbit": "Orbit",
  "core:storm_fork": "Chain",
  "core:ember_wand": "Homing",
  "core:harvest_scythe": "HarvestArc",
  "core:seed_bag": "SeedFan",
  "core:rain_ladle": "WaterFan",
  "core:carpenter_hammer": "ConstructionSlam",
  "core:muster_horn": "MusterWave",
  "core:bitter_seed_dust": "SeedDetour",
  "core:clay_water_bead": "CarryWater",
  "core:crop_guard_signet": "HarvestGuard",
  "core:joiner_square": "FrontOrbit",
  "core:meadow_buckle": "RaiderAim",
  "core:levy_bread_wrap": "FieldMeal",
  "core:gathering_loop": "PickupRadius",
  "core:wayfarer_boots": "MoveSpeed",
  "core:sowing_sworddance": "PlantingArc",
  "core:warded_masonry": "RepairOrbit",
  "core:sheltered_sowing": "ShelteredPlot",
  "core:raider": "Pursuer",
  "core:seed_mite": "SeedThief",
  "core:crop_grazer": "RipeGrazer",
  "core:ram_runner": "Charger",
  "core:shield_raider": "Shield",
  "core:wine_wasp": "Ranged",
  "core:flood_tusk": "FloodBoss"
});

function stable(value) {
  if (Array.isArray(value)) return value.map(stable);
  return value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, stable(value[key])])) : value;
}
export function waveSemanticSignature(record) {
  return createHash('sha256').update(JSON.stringify(stable({ primitives: record.primitives, params: record.params, bespoke: record.bespoke }))).digest('hex');
}
export function validateWaveContent(profile, runtime, design) {
  assert.ok(runtime && design, 'Wave runtime and approved design catalog required');
  assert.equal(profile.waveRuntimeFile, 'runtime/wave-1a.json', 'Unsafe wave runtime path');
  for (const extension of ['runtime', 'firstPlayable', 'runtimeOverrides', 'experiment', 'gameplay', 'weaponCombat', 'tuningFile']) assert.equal(profile[extension], undefined, `Wave cannot mix ${extension}`);
  assert.equal(runtime.contractVersion, 1);
  assert.equal(runtime.designRevision, 'designed-v1.1');
  assert.equal(design.revision, runtime.designRevision);
  assert.equal(design.approvalStatus, 'approved');
  const wave = design.implementationWaves.find(entry => entry.id === 'wave-1a');
  assert.deepEqual(runtime.bindings.map(b => b.id).sort(), [...wave.contentIds].sort(), 'Wave binding scope must match approved 28');
  const records = new Map(design.content.map(record => [record.id, record]));
  for (const binding of runtime.bindings) {
    assert.deepEqual(binding.designRef, { catalog: 'data/system-design-v1.json', revision: design.revision, id: binding.id }, 'Invalid designRef');
    assert.equal(binding.kind, records.get(binding.id)?.kind, 'designRef kind mismatch');
    assert.equal(binding.semanticSha256, waveSemanticSignature(records.get(binding.id)), 'Primitive/parameter mapping drift');
  }
  const definition = runtime.definition;
  assert.ok(validatePrograms(definition.programs), 'Unsupported executable primitive parameters: ' + JSON.stringify(validatePrograms.errors));
  assert.deepEqual(Object.keys(definition.programs).sort(), runtime.bindings.map(b => b.id).sort(), 'Primitive program selection differs');
  for (const [group, kind] of [['weapons', 'weapon'], ['tools', 'tool'], ['enemies', 'enemy']]) {
    assert.deepEqual([...profile.selection[group]].sort(), runtime.bindings.filter(b => b.kind === kind).map(b => b.id).sort(), `Wave selection ${group} mismatch`);
  }
  for (const ids of Object.values(profile.testSelection)) assert.deepEqual(ids, [], 'Wave test selection must be empty');
  for (const [group, kinds] of [['gear', ['weapon', 'tool']], ['items', ['item']], ['evolutions', ['evolution']], ['enemies', ['enemy']]]) {
    assert.deepEqual(Object.keys(definition[group]).sort(), runtime.bindings.filter(b => kinds.includes(b.kind)).map(b => b.id).sort(), `Wave definition ${group} mismatch`);
    for (const [id, record] of Object.entries(definition[group])) {
      assert.equal(record.id, id);
      assert.equal(record.kind, handlers[id], "Unsupported typed primitive handler mapping");
      assert.equal(record.designRef, `data/system-design-v1.json@${design.revision}#${id}`);
      if (group === 'evolutions') assert.deepEqual(record.inputIds, records.get(id).inputIds);
      if (group === 'items') assert.deepEqual([...record.equipmentIds].sort(), [...records.get(id).linkedWeaponIds, ...records.get(id).linkedToolIds].filter(key => definition.gear[key]).sort());
    }
  }
  assert.equal(definition.chapterId, 'meta:chapter_1');
  assert.equal(definition.chapterDesignRef, `data/system-design-v1.json@${design.revision}#meta:chapter_1`);
  assert.equal(definition.bossId, 'core:flood_tusk');
  assert.deepEqual(definition.materialTargets, { 'meta:grain': 'core:seed_bag', 'meta:timber': 'core:carpenter_hammer', 'meta:charter': 'core:muster_horn' });
  assert.deepEqual(Object.keys(runtime.enemyStats).sort(), Object.keys(definition.enemies).sort());
  assert.deepEqual(runtime.provenance.map(p => p.path).sort(), ['definition', 'enemyStats', 'tuning']);
}
