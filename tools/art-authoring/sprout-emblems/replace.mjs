// Offline approved crop installation, #405. No resampling, role changes or gameplay edits.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { encodeRgbaPng } from '../../estate-art.mjs';

const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, '../../..');

export function replaceCells(before, cells) {
  const pixels = Buffer.from(before.pixels), touched = new Uint8Array(before.width * before.height);
  for (const { rect: [x, y, width, height], image } of cells) {
    assert([x, y, width, height].every(Number.isSafeInteger));
    assert(x >= 0 && y >= 0 && width > 0 && height > 0 && x + width <= before.width && y + height <= before.height);
    assert.deepEqual([image.width, image.height, image.pixels.length], [width, height, width * height * 4]);
    for (let row = 0; row < height; row++) {
      const start = (y + row) * before.width + x;
      for (let column = 0; column < width; column++) assert.equal(touched[start + column], 0, 'overlapping crop');
      touched.fill(1, start, start + width);
      image.pixels.copy(pixels, start * 4, row * width * 4, (row + 1) * width * 4);
    }
  }
  let outsideChanged = 0, changedPixels = 0;
  for (let p = 0; p < touched.length; p++) {
    if (pixels.readUInt32BE(p * 4) === before.pixels.readUInt32BE(p * 4)) continue;
    changedPixels++;
    if (!touched[p]) outsideChanged++;
  }
  assert.equal(outsideChanged, 0);
  return { width: before.width, height: before.height, pixels, outsideChanged, changedPixels };
}

function install(source, baseline, check) {
  const spec = JSON.parse(fs.readFileSync(path.join(directory, 'approved.json')));
  const installed = check ? JSON.parse(fs.readFileSync(path.join(directory, 'installed.json'))) : null;
  assert.equal(spec.assets.length, 15);
  const manifests = new Map(), groups = new Map();
  for (const asset of spec.assets) {
    for (const location of asset.locations) {
      if (!manifests.has(location.manifest)) {
        const bytes = fs.readFileSync(path.join(root, 'unity/Assets/Art', location.manifest));
        if (check) assert.equal(sha(bytes), installed.manifests.find(m => m.name === location.manifest).sha256, 'manifest changed');
        manifests.set(location.manifest, { bytes, roles: JSON.parse(bytes).roles });
      }
      const role = manifests.get(location.manifest).roles.find(r => r.id === location.role);
      assert(role, location.role);
      assert.equal(role.atlas + '.png', asset.atlas);
      assert.deepEqual(role.frames, location.frames);
      assert.deepEqual(role.pivot, location.pivot);
    }
    const bytes = fs.readFileSync(path.join(source, asset.png));
    assert.equal(sha(bytes), asset.pngSha256, asset.png);
    if (!groups.has(asset.atlas)) groups.set(asset.atlas, []);
    groups.get(asset.atlas).push({ ...asset, image: decodeRgbaPng(bytes) });
  }
  const outputs = [], rows = [];
  for (const [atlas, cells] of groups) {
    const beforeBytes = fs.readFileSync(path.join(baseline, atlas));
    for (const cell of cells) assert.equal(sha(beforeBytes), cell.sourceAtlasSha256, atlas + ' baseline');
    const before = decodeRgbaPng(beforeBytes), after = replaceCells(before, cells);
    const bytes = encodeRgbaPng(after), target = path.join(root, 'unity/Assets/Art', atlas);
    assert.deepEqual(decodeRgbaPng(bytes).pixels, after.pixels, 'PNG round trip');
    if (check) {
      assert.deepEqual(decodeRgbaPng(fs.readFileSync(target)).pixels, after.pixels, atlas);
      assert.equal(sha(bytes), installed.atlases.find(a => a.atlas === atlas).outputSha256, atlas + ' regeneration');
    }
    else assert.equal(sha(fs.readFileSync(target)), sha(beforeBytes), atlas + ' changed since baseline');
    outputs.push({ target, bytes });
    rows.push({ atlas, roles: cells.map(c => c.role), sourceSha256: sha(beforeBytes), outputSha256: sha(bytes),
      changedPixels: after.changedPixels, outsideChanged: after.outsideChanged });
  }
  for (const [name, { bytes }] of manifests) assert.deepEqual(fs.readFileSync(path.join(root, 'unity/Assets/Art', name)), bytes);
  if (!check) {
    for (const output of outputs) fs.writeFileSync(output.target, output.bytes);
    fs.writeFileSync(path.join(directory, 'installed.json'), JSON.stringify({ issue: 405, assets: spec.assets.length,
      manifests: [...manifests].map(([name, { bytes }]) => ({ name, sha256: sha(bytes) })), atlases: rows }, null, 2) + '\n');
  }
  console.log(JSON.stringify({ mode: check ? 'check' : 'install', assets: spec.assets.length, atlases: rows }, null, 2));
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const [mode, source, baseline] = process.argv.slice(2);
  assert(['--install', '--check'].includes(mode) && source && baseline,
    'Usage: node tools/art-authoring/sprout-emblems/replace.mjs --install|--check SOURCE_DIRECTORY ORIGINAL_ATLAS_DIRECTORY');
  install(path.resolve(source), path.resolve(baseline), mode === '--check');
}
