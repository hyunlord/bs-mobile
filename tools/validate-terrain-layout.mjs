import { createHash } from 'node:crypto';
import { exclusionReasons, rectangle, spritePolygon } from './art/terrain-layout-geometry.mjs';

// Called only after the closed JSON schema accepts the representation.
export function validateTerrainLayout(layout, runtime) {
  const errors = [];
  // Approved GEOMETRY.json: 100 river cells and all three bridge rectangles.
  // Changing the layout requires an explicit newly reviewed geometry version.
  const approvedWaterReservationsSha256 = 'dfca3986bf2f5af592a9b30c6e091e9986e2f3fe3d7a088a3368a2e9e02b82ff';
  const expect = (condition, message) => { if (!condition) errors.push(message); };
  const unique = (rows, field, label) => expect(new Set(rows.map(r => r[field])).size === rows.length, `duplicate ${label}`);
  const waterReservations = layout.reservations.filter(r => ['river', 'bridge'].includes(r.type)).sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
  expect(createHash('sha256').update(JSON.stringify(waterReservations)).digest('hex') === approvedWaterReservationsSha256, 'approved river/bridge reservations differ');
  unique(layout.assets, 'id', 'asset ID'); unique(layout.zones, 'id', 'zone ID'); unique(layout.reservations, 'id', 'reservation ID');
  const all = [...layout.instances, ...layout.reviewOnly.buildings, ...layout.reviewOnly.excludedInstances];
  unique(all, 'id', 'instance ID'); unique(all, 'drawOrder', 'draw order');
  expect(all.length === layout.provenance.sourcePlacementCount, 'source placement count differs from partition');
  expect(all.every(p => p.drawOrder < all.length), 'draw order outside source sequence');
  const assets = new Map(layout.assets.map(a => [a.id, a])), zones = new Map(layout.zones.map(z => [z.id, z]));
  const ref = (id, field) => expect(assets.has(id), `unresolved asset ${field}: ${id}`);
  const seen = new Set(), active = new Set();
  function composition(id) {
    if (active.has(id)) { errors.push(`composition cycle: ${id}`); return; }
    if (seen.has(id)) return;
    seen.add(id); active.add(id);
    const asset = assets.get(id);
    if (asset?.composition) {
      expect(JSON.stringify(asset.size) === JSON.stringify(asset.composition.size), `composition size mismatch ${id}`);
      for (const part of asset.composition.parts) {
        ref(part.assetId, 'composition');
        const child = assets.get(part.assetId);
        if (child) expect(part.topLeft.every((n, i) => n + child.size[i] <= asset.size[i]), `composition part out of canvas ${part.assetId}`);
        composition(part.assetId);
      }
    }
    active.delete(id);
  }
  for (const asset of layout.assets) {
    expect(asset.anchor.every((n, i) => n >= 0 && n <= asset.size[i]), `asset anchor outside canvas ${asset.id}`);
    expect(Boolean(asset.source.cropRect) === Boolean(asset.source.containerSha256), `atlas crop needs container SHA ${asset.id}`);
    if (asset.source.cropRect) expect(asset.source.cropRect[2] === asset.size[0] && asset.source.cropRect[3] === asset.size[1], `atlas crop size mismatch ${asset.id}`);
    composition(asset.id);
  }
  ref(layout.background.assetId, 'background');
  for (const zone of layout.zones) { ref(zone.groundAssetId, 'ground'); expect(zone.rect[2] > 0 && zone.rect[3] > 0, `empty zone ${zone.id}`); }
  expect(layout.zones.reduce((sum, z) => sum + z.rect[2] * z.rect[3], 0) === 1664, 'r8 total area must be 1664');
  for (const item of all) {
    ref(item.assetId, item.id);
    expect(item.zoneId === 'exterior' || zones.has(item.zoneId), `unknown zone ${item.id}`);
    const zone = zones.get(item.zoneId);
    if (zone) expect(item.grid[0] >= zone.rect[0] && item.grid[0] < zone.rect[0] + zone.rect[2] && item.grid[1] >= zone.rect[1] && item.grid[1] < zone.rect[1] + zone.rect[3], `anchor outside assigned zone ${item.id}`);
    if (['prop', 'cluster', 'building-prop'].includes(item.category)) {
      expect(item.footprint.every(n => n > 0), `empty decorative footprint ${item.id}`);
      const asset = assets.get(item.assetId);
      if (asset) expect(item.footprint.every((n, i) => Math.abs(n - asset.nominalFootprint[i] * item.scale[0]) < 1e-9), `nominal footprint mismatch ${item.id}`);
      expect(Math.abs(item.scale[0] - item.scale[1]) < 1e-9, `nonuniform decorative scale ${item.id}`);
    }
  }
  for (const item of layout.instances) {
    expect(item.category !== 'building', `runtime building duplicate ${item.id}`);
    expect(exclusionReasons(item, layout.reservations).length === 0, `reserved footprint collision ${item.id}`);
  }
  for (const item of layout.reviewOnly.buildings) expect(item.category === 'building', `non-building review reference ${item.id}`);
  for (const item of layout.reviewOnly.excludedInstances) expect(JSON.stringify(item.exclusionReasons) === JSON.stringify(exclusionReasons(item, layout.reservations)), `incorrect exclusion reasons ${item.id}`);
  for (const item of all.filter(p => p.category === 'path')) {
    const asset = assets.get(item.assetId), reservation = layout.reservations.find(r => r.id === `path:${item.id}`);
    expect(Boolean(reservation), `missing path reservation ${item.id}`);
    if (asset && reservation) expect(JSON.stringify(reservation.polygon) === JSON.stringify(spritePolygon(item, asset)), `path polygon mismatch ${item.id}`);
  }
  if (!runtime) errors.push('current opt-in r8 geometry missing');
  else {
    expect(JSON.stringify(layout.zones.map(z => [z.id, ...z.rect])) === JSON.stringify(runtime.zones.map(z => [z.id, z.x, z.y, z.width, z.height])), 'current r8 zone geometry differs');
    for (const building of runtime.buildings) {
      const reservation = layout.reservations.find(r => r.id === `slot:${building.kind}`);
      expect(Boolean(reservation), `missing building/Lv0 reservation ${building.kind}`);
      if (reservation) expect(JSON.stringify(reservation.polygon) === JSON.stringify(rectangle([building.x, building.y, building.width, building.height])), `slot polygon mismatch ${building.kind}`);
    }
  }
  return errors;
}
