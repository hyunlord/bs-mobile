// Exact approved crop replacement for #475. Actor/fuse integration is a separate validated attachment step.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { encodeRgbaPng } from '../../estate-art.mjs';
import { replaceCells } from '../sprout-emblems/replace.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
export const crops = [
  ['iron-edge-candidate-r1.png', 'attack.core.iron_blade.sector90'],
  ['harvest-edge-candidate-r1.png', 'attack.core.harvest_scythe.sector180'],
  ['ember-candidate-r4-final.png', 'attack.core.ember_wand.projectile'],
  ['ward-candidate-r4-final.png', 'attack.core.ward_orbit.orbit'],
  ['canal-edge-candidate-r1.png', 'attack.core.canal_bow.piercing'],
  ['spore-combined-effective-alpha-r4-final.png', 'attack.core.spore_fan.field'],
];
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
export function install(source, baseline) {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'unity/Assets/Art/first-playable-manifest.json')));
  const original = fs.readFileSync(baseline), before = decodeRgbaPng(original);
  const cells = crops.map(([png, id]) => {
    const role = manifest.roles.find(r => r.id === id);
    assert.equal(role.atlas, 'fx'); assert.equal(role.frames.length, 1);
    const frame = role.frames[0], bytes = fs.readFileSync(path.join(source, png));
    const image = decodeRgbaPng(bytes);
    return { png, role: id, sha256: sha(bytes), rgbaSha256: sha(image.pixels), rect: [frame.x, frame.y, frame.width, frame.height], image };
  });
  const after = replaceCells(before, cells), bytes = encodeRgbaPng(after);
  assert.deepEqual(decodeRgbaPng(bytes).pixels, after.pixels);
  const target = path.join(root, 'unity/Assets/Art/fx.png');
  assert([sha(original), sha(bytes)].includes(sha(fs.readFileSync(target))), 'atlas changed outside this installation');
  fs.writeFileSync(target, bytes);
  const report = { release: 'm3-combat-fx-audit-456-20261028', sourceAtlasSha256: sha(original), outputAtlasSha256: sha(bytes), outsideChanged: after.outsideChanged,
    outsideRgbaSha256: sha(Buffer.from(before.pixels.map((v, i) => cells.some(({rect:[x,y,w,h]}) => Math.floor(i / 4) % before.width >= x && Math.floor(i / 4) % before.width < x+w && Math.floor(i / 4 / before.width) >= y && Math.floor(i / 4 / before.width) < y+h) ? 0 : v))),
    changedPixels: after.changedPixels, assets: cells.map(({ image, ...record }) => record) };
  fs.writeFileSync(path.join(root, 'tools/art-authoring/combat-fx/installed.json'), JSON.stringify(report, null, 2) + '\n');
  return report;
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  assert.equal(process.argv.length, 4, 'usage: install.mjs RELEASE_DIRECTORY BASELINE_FX_PNG');
  console.log(JSON.stringify(install(path.resolve(process.argv[2]), path.resolve(process.argv[3])), null, 2));
}
