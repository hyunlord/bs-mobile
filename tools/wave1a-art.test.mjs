import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { validateManifest } from './first-playable-art.mjs';
import { requiredBindings, validateRegistry, validateRepository, validateAlphaPadding, validateArcGeometry, decodeRgbaPng } from './wave1a-art.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const manifest = () => JSON.parse(fs.readFileSync(path.join(root, 'unity/Assets/Art/wave1a-manifest.json'), 'utf8'));

test('wave art source covers all approved records and causal completion states', () => {
  const result = validateRepository(root, { registry: false });
  assert.ok(result.required > 100);
  assert.equal(result.registry, 'not-checked');
});
test('legacy art cannot substitute for a missing water completion or boss warning', () => {
  for (const [kind, contentId, state] of [['wave', 'xp-water', 'default'], ['enemy', 'core:flood_tusk', 'charge-windup']]) {
    const value = manifest();
    value.bindings = value.bindings.filter(binding => !(binding.kind === kind && binding.contentId === contentId && binding.state === state));
    assert.throws(() => validateManifest(value, requiredBindings(root)), /missing required binding/);
  }
});
test('registry rejects legacy manifest, absent wave audio and shuffled atlas references', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'wave-art-test-'));
  try {
    const art = path.join(dir, 'unity/Assets/Art');
    const resources = path.join(dir, 'unity/Assets/Resources');
    fs.mkdirSync(art, { recursive: true }); fs.mkdirSync(resources, { recursive: true });
    const ids = ['1', '2', '3', '4'].map(value => value.repeat(32));
    for (const [name, id] of [['wave1a-manifest.json', ids[0]], ['wave1a-audio.json', ids[1]], ['a.png', ids[2]], ['b.png', ids[3]]]) fs.writeFileSync(path.join(art, name + '.meta'), `fileFormatVersion: 2\nguid: ${id}\n`);
    const value = { atlases: [{ path: 'Assets/Art/a.png' }, { path: 'Assets/Art/b.png' }] };
    const yaml = `  manifest: {fileID: 4900000, guid: ${ids[0]}, type: 3}\n  audioManifest: {fileID: 4900000, guid: ${ids[1]}, type: 3}\n  atlases:\n  - {fileID: 2800000, guid: ${ids[2]}, type: 3}\n  - {fileID: 2800000, guid: ${ids[3]}, type: 3}\n`;
    const file = path.join(resources, 'Wave1aArt.asset');
    fs.writeFileSync(file, yaml); assert.equal(validateRegistry(dir, value).registryAtlases, 2);
    fs.writeFileSync(file, yaml.replace(ids[0], '0'.repeat(32))); assert.throws(() => validateRegistry(dir, value), /wave-1a manifest/);
    fs.writeFileSync(file, yaml.replace(ids[1], '0'.repeat(32))); assert.throws(() => validateRegistry(dir, value), /wave-1a audio/);
    fs.writeFileSync(file, yaml.replace(ids[2], ids[3])); assert.throws(() => validateRegistry(dir, value), /atlas 0 differs/);
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
});

test('new atlas padding checks the entire outside ring including corners', () => {
  const value = { atlases: [{ id: 'wave1a-fx', width: 8, height: 8, padding: 2 }], roles: [{ id: 'test', atlas: 'wave1a-fx', frames: [{ x: 2, y: 2, width: 4, height: 4 }] }] };
  const pixels = Buffer.alloc(8 * 8 * 4);
  const image = new Map([['wave1a-fx', { width: 8, height: 8, pixels }]]);
  assert.throws(() => validateAlphaPadding(value, image), /empty visible frame/);
  pixels[(3 * 8 + 3) * 4 + 3] = 255;
  pixels[3] = 16;
  assert.equal(validateAlphaPadding(value, image).maximumRingAlpha, 16);
  pixels[3] = 17;
  assert.throws(() => validateAlphaPadding(value, image), /outside padding ring/);
  pixels[3] = 0;
  pixels[(7 * 8 + 4) * 4 + 3] = 255;
  assert.throws(() => validateAlphaPadding(value, image), /outside padding ring/);
});

test('arc pivots and scales keep authored visible pixels inside actual 90-degree collision', () => {
  const image = decodeRgbaPng(fs.readFileSync(path.join(root, 'unity/Assets/Art/wave1a-arcs.png')));
  const decoded = new Map([['wave1a-arcs', image]]);
  const value = manifest();
  const result = validateArcGeometry(value, decoded);
  assert.ok(result.maximumAngleDegrees < 45);
  assert.ok(result.maximumVisibleRadius <= 1);
  const wrongPivot = structuredClone(value);
  wrongPivot.roles.find(role => role.atlas === 'wave1a-arcs').pivot.x = 0.5;
  assert.throws(() => validateArcGeometry(wrongPivot, decoded), /outside forward 90-degree sector/);
  const wrongScale = structuredClone(value);
  const role = wrongScale.roles.find(role => role.atlas === 'wave1a-arcs');
  role.worldSize.x *= 2; role.worldSize.y *= 2;
  assert.throws(() => validateArcGeometry(wrongScale, decoded), /outside unit collision radius/);
});
