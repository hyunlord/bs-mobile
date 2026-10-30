import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import path from 'node:path';
import assert from 'node:assert/strict';
import { project, inverse, rectangle, spritePolygon, exclusionReasons } from './terrain-layout-geometry.mjs';

const [sourceRoot, outputFile = 'data/opt-in/estate/terrain-layout-r8.json', evidenceDirectory = 'artifacts/terrain-layout-572'] = process.argv.slice(2);
assert(sourceRoot, 'Usage: node tools/art/export-terrain-layout.mjs <approved-art-checkout-root> [output.json] [evidence-dir]');
const base = 'output/estate-r8-dense-20261028/';
const read = relative => readFileSync(path.join(sourceRoot, relative));
const json = relative => JSON.parse(read(relative));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const manifestFile = base + 'delivery/DENSE-SCENES-r2.json';
const original = json(manifestFile), geometry = json(base + 'reference/GEOMETRY.json');
const registry = json(base + 'export/source-registry-draft-r1.json').assets;
const runtimeFile = 'data/opt-in/account-estate/m3a-prototype-r8.json';
const runtimeBytes = readFileSync(runtimeFile), runtime = JSON.parse(runtimeBytes);
const shape = z => [z.id, z.x, z.y, z.width, z.height];
assert.deepEqual(geometry.zones.map(shape), runtime.zones.map(shape), 'Approved and current r8 zones differ');
assert.deepEqual(geometry.buildings.map(z => [z.kind, z.x, z.y, z.width, z.height]), runtime.buildings.map(z => [z.kind, z.x, z.y, z.width, z.height]), 'Approved and current slots differ');
const assets = [], byPath = new Map(), localSources = {};
function assetFor(file, publishedSource, expectedHash) {
  if (byPath.has(file)) return byPath.get(file);
  const bytes = read(file), sourceHash = sha(bytes);
  if (expectedHash) assert.equal(sourceHash, expectedHash, `Source changed: ${file}`);
  assert.equal(bytes.toString('ascii', 1, 4), 'PNG', file);
  const size = [bytes.readUInt32BE(16), bytes.readUInt32BE(20)];
  const entry = registry.find(a => a.path === file);
  const anchor = entry ? entry.nativePivot.map((n, i) => n / entry.nativeSize[i] * size[i]) : size.map(n => n / 2);
  const id = path.basename(file, '.png');
  assert(!assets.some(a => a.id === id), `Asset basename collision: ${id}`);
  const source = { sha256: sourceHash };
  if (publishedSource?.downloadURL) source.url = publishedSource.downloadURL;
  else if (publishedSource?.atlasURL) {
    source.url = publishedSource.atlasURL;
    source.containerSha256 = publishedSource.atlasSHA256;
    source.cropRect = publishedSource.cropRectTopLeft;
  }
  const floor = entry?.grid ?? (entry?.floorPixels ? [entry.floorPixels[0] / 64, entry.floorPixels[1] / 32] : entry ? [2, 2] : [0, 0]);
  const nominalFootprint = entry ? floor.map(n => n * size[0] / entry.nativeSize[0]) : [0, 0];
  const asset = { id, fileName: path.basename(file), size, anchor, nominalFootprint, source };
  assets.push(asset); byPath.set(file, asset); localSources[id] = path.resolve(sourceRoot, file);
  return asset;
}
const all = original.placements.map((p, drawOrder) => {
  const asset = assetFor(p.file, p.publishedSource, p.sha256);
  const scale = p.nativeSize.map((n, i) => n / asset.size[i]);
  const nativeAnchor = p.nativeTopLeft.map((n, i) => n + asset.anchor[i] * scale[i]);
  const grid = p.floorCenterGrid ?? inverse(nativeAnchor);
  assert(project(grid).every((n, i) => Math.abs(n - nativeAnchor[i]) < 1e-8), `Anchor mismatch ${p.instanceId}`);
  const zone = geometry.zones.find(z => grid[0] >= z.x && grid[0] < z.x + z.width && grid[1] >= z.y && grid[1] < z.y + z.height);
  const entry = registry.find(a => a.path === p.file);
  const relativeScale = entry ? p.nativeSize[0] / entry.nativeSize[0] : 1;
  const floor = entry?.grid ?? (entry?.floorPixels ? [entry.floorPixels[0] / 64, entry.floorPixels[1] / 32] : [2, 2]);
  const footprint = ['prop', 'cluster', 'building-prop'].includes(p.category) ? floor.map(n => n * relativeScale) : [0, 0];
  return { id: p.instanceId, assetId: asset.id, zoneId: zone?.id ?? 'exterior', category: p.category, grid, scale,
    sizeVariant: relativeScale === 1 ? 'original' : `scale-${relativeScale}`, flipX: false, rotationRadians: p.rotationRadians ?? 0,
    opacity: p.opacity ?? 1, drawOrder, layer: p.layer, footprint,
    phase: !zone ? 'exterior' : ['zone:start', 'zone:outskirts-north', 'zone:outskirts-east', 'zone:outskirts-south', 'zone:outskirts-west', 'zone:inner-village'].includes(zone.id) ? 'start-adjacent' : 'all13' };
});
const river = original.placements.find(p => p.category === 'river');
const riverAsset = byPath.get(river.file), riverPlacement = json(base + 'delivery/dense-river-r2-placement.json');
const chunks = riverPlacement.chunks ?? riverPlacement.assets;
assert(chunks, 'Need native river chunk metadata');
riverAsset.composition = { size: riverAsset.size, parts: river.dependencies.map(dep => {
  const asset = assetFor(dep.file, dep.publishedSource, dep.sha256);
  const chunk = chunks.find(c => c.file === path.basename(dep.file) || c.file1x === path.basename(dep.file));
  assert(chunk, `Chunk metadata missing: ${dep.file}`);
  const origin = chunk.nativeOrigin ?? chunk.origin;
  assert(origin, `Chunk origin missing: ${dep.file}`);
  return { assetId: asset.id, topLeft: origin.map((n, i) => n - river.nativeTopLeft[i]) };
}) };
const zones = original.groundZones.map(z => ({ id: z.id, rect: z.rect, groundAssetId: assetFor(z.file, z.publishedSource, z.sha256).id }));
const backgroundFile = 'output/estate-r8-terrain-20261028/export/release-ready-r2/terrain-forestfloor-dark-r3b-512.png';
const backgroundAsset = assetFor(backgroundFile, { downloadURL: 'https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/terrain-forestfloor-dark-r3b-512.png' });
const reservations = geometry.buildings.map(b => ({ id: `slot:${b.kind}`, type: 'building-or-lv0', polygon: rectangle([b.x, b.y, b.width, b.height]) }));
for (const p of all.filter(p => p.category === 'path')) reservations.push({ id: `path:${p.id}`, type: 'path', polygon: spritePolygon(p, assets.find(a => a.id === p.assetId)) });
for (const b of geometry.bridges) reservations.push({ id: `bridge:${b.id}`, type: 'bridge', polygon: rectangle([b.x, b.y, b.width, b.height]) });
for (const [x, y] of geometry.river.cells) reservations.push({ id: `river:${x},${y}`, type: 'river', polygon: rectangle([x, y, 1, 1]) });
const instances = [], excludedInstances = [], buildings = [];
for (const item of all) {
  if (item.category === 'building') { buildings.push(item); continue; }
  const reasons = exclusionReasons(item, reservations);
  if (reasons.length) excludedInstances.push({ ...item, exclusionReasons: reasons }); else instances.push(item);
}
const result = {
  id: 'presentation:terrain_layout_r8', kind: 'estate-terrain-layout', version: 1, runtimeEnabled: false,
  provenance: { approvedRelease: 'estate-r8-terrain-488-20261028', approvedManifest: 'DENSE-SCENES-r2.json', approvedManifestSha256: sha(read(manifestFile)), geometrySourceSha256: sha(read(base + 'reference/GEOMETRY.json')), runtimeGeometryFile: runtimeFile, runtimeGeometrySha256: sha(runtimeBytes), sourcePlacementCount: all.length },
  contract: { units: 'fractional-r8-grid; native isometric pixels', rendering: 'project(grid)-anchor*scale; rotate around scaled canvas centre; ascending drawOrder; ground first', collisions: 'Nominal ground footprint convex polygon against full building/Lv0 slots, rotated path canvas, bridge and river cells; canopy pixels may overhang. Props may overlap each other. Ground-decal textures have zero occupancy and may underlay reserved cells; they are not physical props.', runtime: 'Only instances are terrain draw data. reviewOnly is never drawn by the game loader. Existing game buildings own reserved slots.', approval: 'Approved original art; strict exclusion changes require new visual review; no runtime/gameplay claim.', texture: '512px native texture plane, repeating every 8 grid units; clip to zone rectangles' },
  projection: { tileWidth: 64, tileHeight: 32 }, assets, zones,
  cameras: original.cameras.map(c => ({ id: c.id, centerGrid: c.centerGrid, scale75: 75 / 98.7, scale50: 50 / 98.7 })),
  background: { assetId: backgroundAsset.id, canvasSize: [4000, 3200], canvasOffset: [2000, 300], tilePixels: 512 },
  reservations, instances, reviewOnly: { buildings, excludedInstances },
};
mkdirSync(path.dirname(outputFile), { recursive: true }); mkdirSync(evidenceDirectory, { recursive: true });
writeFileSync(outputFile, JSON.stringify(result, null, 2) + '\n');
writeFileSync(path.join(evidenceDirectory, 'local-asset-sources.json'), JSON.stringify(localSources, null, 2) + '\n');
console.log(JSON.stringify({ source: all.length, runtime: instances.length, excluded: excludedInstances.length, buildingReferences: buildings.length, assets: assets.length }));
