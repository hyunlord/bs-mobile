import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// #483: approved proposal fields are projected, never copied as unknown runtime members.
const read = path => JSON.parse(readFileSync(new URL(`../${path}`, import.meta.url), 'utf8'));
const proposal = read('docs/design/m3-estate-r8/geometry-proposal.json');
const r7 = read('data/opt-in/account-estate/m3a-prototype-r7.json');
const r8 = read('data/opt-in/account-estate/m3a-prototype-r8.json');
const layout = read('unity/Assets/Game/App/Resources/EstateR8Layout.json');
const zones = proposal.zones.map(({ id, x, y, width, height, openAtStart, unlock }) => ({
  id, x, y, width, height, openAtStart, ...(unlock ? { unlock } : {}),
}));
assert.deepEqual(r8, { ...r7, version: 'm3a-prototype-r8', zones });
assert.equal(layout.version, r8.version);
assert.deepEqual(layout.riverCells, proposal.river.cells.map(([x, y]) => ({ x, y })));
assert.deepEqual(layout.bridges, proposal.river.bridges.map(b => ({
  id: b.id, x: b.x, y: b.y, width: b.width, height: b.height, zone: b.hostZones[0],
})));
console.log('r8 exact proposal, full r7 non-zone equality, river cells and bridge anchors: PASS');
