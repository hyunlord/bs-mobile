import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const spec = JSON.parse(fs.readFileSync(new URL('./estate-art.json', import.meta.url)));
test('approved construction and carrying assets preserve their authored ground anchors', () => {
  const get = id => { const asset = spec.assets.find(a => a.id === id); assert.ok(asset, id); return asset; };
  assert.deepEqual(get('construction-building-3-lit').pivot, [.5, .225]);
  assert.equal(get('construction-building-3-lit').file, 'building-stage3-lit-r2-1x.png');
  assert.deepEqual(get('worker-carry-se-00').pivot, [122 / 256, 25 / 256]);
  assert.deepEqual(get('worker-carry-sw-15').pivot, [134 / 256, 25 / 256]);
  assert.equal(get('worker-carry-sw-15').displayWidth, 66);
  for (const direction of ['se', 'sw']) for (let i = 0; i < 16; i++)
    assert.equal(get(`worker-carry-${direction}-${String(i).padStart(2, '0')}`).maxSize, 256);
});
test('only approved dark bank and tree revisions enter the runtime atlas', () => {
  const relevant = spec.assets.filter(a => a.id.startsWith('bank-') || a.id.startsWith('estate-tree-'));
  assert.equal(relevant.length, 28);
  assert.ok(relevant.filter(a => a.id.endsWith('-dark')).every(a => a.file.includes('-dark-r2-')));
});
test('six emblem crops preserve historical outside pixels and alpha without requiring Git history', async () => {
  const { createHash } = await import('node:crypto');
  const { decodeRgbaPng } = await import('./wave1a-art.mjs');
  const dir = 'unity/Assets/Resources/EstateArt/';
  const image = decodeRgbaPng(fs.readFileSync(dir + 'estate-buildings.png'));
  const manifest = JSON.parse(fs.readFileSync(dir + 'estate-art.json'));
  const crops = manifest.buildings.replacements.filter(r => r.file.endsWith('-sprout-r3.png'));
  assert.equal(crops.length, 6);
  const baseline = spec.buildings.historicalPreservation;
  assert.equal(image.width, baseline.width);
  const outside = Buffer.from(image.pixels.subarray(0, baseline.width * baseline.height * 4));
  const alpha = Buffer.alloc(baseline.width * baseline.height);
  for (let y = 0; y < baseline.height; y++) for (let x = 0; x < baseline.width; x++) {
    const index = y * baseline.width + x;
    alpha[index] = outside[index * 4 + 3];
    if (crops.some(({ rect: [cx, cy, w, h] }) => x >= cx && x < cx + w && y >= cy && y < cy + h)) outside.fill(0, index * 4, index * 4 + 4);
  }
  assert.equal(createHash('sha256').update(outside).digest('hex'), baseline.outsideCropSha256);
  assert.equal(createHash('sha256').update(alpha).digest('hex'), baseline.alphaSha256);
  assert.equal(manifest.buildings.records.find(r => r.kind === 'ironworks').pivot[1], .125);
});
