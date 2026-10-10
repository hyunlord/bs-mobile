import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync } from 'node:fs';
import { validateWaveContent } from './wave1a-content.mjs';
const read = relative => JSON.parse(readFileSync(new URL('../data/' + relative, import.meta.url)));
function fixture() { return [read('profiles/wave-1a.json'), read('runtime/wave-1a.json'), read('system-design-v1.json')]; }
test('approved wave scope resolves separately from historical content', () => assert.doesNotThrow(() => validateWaveContent(...fixture())));
for (const mutation of ['reference-path', 'reference-revision', 'reference-kind', 'primitive', 'scope', 'legacy-mix', 'item-link', 'evolution-input', 'target-material', 'handler']) {
  test(`wave rejects ${mutation}`, () => {
    const [profile, runtime, design] = fixture();
    switch (mutation) {
      case 'handler': runtime.definition.gear['core:iron_blade'].kind = 'Homing'; break;
      case 'reference-path': runtime.bindings[0].designRef.catalog = '../system-design-v1.json'; break;
      case 'reference-revision': runtime.bindings[0].designRef.revision = 'designed-v1'; break;
      case 'reference-kind': runtime.bindings[0].kind = 'tool'; break;
      case 'primitive': design.content.find(r => r.id === runtime.bindings[0].id).params['unit:attack-shape'].aim = 'nearest-enemy'; break;
      case 'scope': runtime.bindings.pop(); break;
      case 'legacy-mix': profile.tuningFile = 'first-playable-tuning.json'; break;
      case 'item-link': runtime.definition.items['core:meadow_buckle'].equipmentIds = ['core:seed_bag']; break;
      case 'evolution-input': runtime.definition.evolutions['core:sowing_sworddance'].inputIds.reverse(); break;
      case 'target-material': runtime.definition.materialTargets['meta:iron'] = 'core:carpenter_hammer'; break;
    }
    assert.throws(() => validateWaveContent(profile, runtime, design));
  });
}

for (const mutation of ['missing-program','unknown-unit','ignored-parameter']) {
  test(`wave rejects executable ${mutation}`, () => {
    const args = fixture(); const programs = args[1].definition.programs;
    if (mutation === 'missing-program') delete programs['core:iron_blade'];
    if (mutation === 'unknown-unit') programs['core:iron_blade'].params['unit:unknown'] = {};
    if (mutation === 'ignored-parameter') programs['core:iron_blade'].params['unit:attack-shape'].silentFlag = 'true';
    assert.throws(() => validateWaveContent(...args));
  });
}
