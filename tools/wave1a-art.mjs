import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { inflateSync } from 'node:zlib';
import { validateManifest } from './first-playable-art.mjs';

const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const check = (value, message) => { if (!value) throw new Error(message); };
export const waveRoles = ['sprout-path', 'grain-seed', 'grain-young', 'grain-ripe', 'workshop-frame', 'workshop-complete', 'workshop-ruin', 'pool-full', 'pool-empty', 'roof-intact', 'roof-broken', 'xp-grain', 'xp-water', 'xp-timber', 'xp-mission', 'bitter-dust', 'water-carry', 'ration', 'shield-fragment', 'grain-fragment', 'charge-tell', 'water-lane', 'wet', 'stopped', 'timber-bundle', 'timber-source'];
export const enemyPhases = {
  'core:raider': ['windup'],
  'core:crop_grazer': ['feeding'],
  'core:ram_runner': ['windup', 'charge', 'recover'],
  'core:shield_raider': ['guard', 'turn'],
  'core:wine_wasp': ['windup', 'shoot', 'retreat'],
  'core:flood_tusk': ['water-windup', 'water-surge', 'charge-windup', 'charge', 'stuck', 'recover'],
};
export function requiredBindings(root) {
  const design = read(path.join(root, 'data/system-design-v1.json'));
  const wave = design.implementationWaves.find(value => value.id === 'wave-1a');
  check(wave?.contentIds.length === 28, 'wave-1a must retain its approved 28-record scope');
  const result = wave.contentIds.map(contentId => ({ kind: 'icon', contentId, state: 'default' }));
  const add = (kind, contentId, states) => { for (const state of states) result.push({ kind, contentId, state }); };
  for (const contentId of ['core:raider', 'core:seed_mite', 'core:crop_grazer', 'core:ram_runner', 'core:shield_raider', 'core:wine_wasp', 'core:flood_tusk']) add('enemy', contentId, ['idle', 'walk', 'hit', 'death', ...(enemyPhases[contentId] ?? [])]);
  for (const contentId of [...waveRoles, 'wood-brace']) add('wave', contentId, ['default']);
  for (const [contentId, state] of [['iron_blade', 'sector90'], ['ward_orbit', 'orbit'], ['storm_fork', 'chain'], ['ember_wand', 'projectile'], ['harvest_scythe', 'sector90'], ['seed_bag', 'projectile'], ['rain_ladle', 'wave'], ['carpenter_hammer', 'melee'], ['muster_horn', 'wave'], ['sowing_sworddance', 'sector90'], ['sheltered_sowing', 'melee'], ['wine_wasp', 'projectile']]) add('attack', `core:${contentId}`, [state]);
  return result;
}
const guid = file => {
  const match = fs.readFileSync(file, 'utf8').match(/^guid:\s*([0-9a-f]{32})$/m);
  check(match, `missing Unity GUID: ${file}`);
  return match[1];
};
export function validateRegistry(root, manifest) {
  const file = path.join(root, 'unity/Assets/Resources/Wave1aArt.asset');
  const yaml = fs.readFileSync(file, 'utf8');
  const manifestGuid = guid(path.join(root, 'unity/Assets/Art/wave1a-manifest.json.meta'));
  check(new RegExp(`manifest: \\{[^}]*guid: ${manifestGuid}[,}]`).test(yaml), 'Wave1aArt registry must reference the wave-1a manifest');
  const audioGuid = guid(path.join(root, 'unity/Assets/Art/wave1a-audio.json.meta'));
  check(new RegExp(`audioManifest: \\{[^}]*guid: ${audioGuid}[,}]`).test(yaml), 'Wave1aArt registry must reference wave-1a audio');
  const section = yaml.split(/^  atlases:\s*$/m)[1];
  check(section !== undefined, 'Wave1aArt registry missing atlas list');
  const actual = [...section.split(/^  [A-Za-z]/m)[0].matchAll(/guid:\s*([0-9a-f]{32})/g)].map(match => match[1]);
  const expected = manifest.atlases.map(atlas => guid(path.join(root, 'unity', atlas.path + '.meta')));
  check(actual.length === expected.length, 'Wave1aArt registry atlas count differs');
  expected.forEach((value, index) => check(value === actual[index], `Wave1aArt registry atlas ${index} differs`));
  return { registryAtlases: actual.length };
}
// These generated atlases retain faint alpha residue (measured maximum 9/255).
// The complete outside padding ring permits alpha <=16; this is not strict zero padding.
// Legacy atlas import/validation remains strict zero. Keep ArtPreparation's matching rule aligned.
export const waveAtlasPaddingAlphaMaximum = 16;
export const waveAtlasIds = ['wave1a-actors', 'wave1a-growth', 'wave1a-fx', 'wave1a-icons', 'wave1a-arcs'];
export function decodeRgbaPng(png) {
  check(png.length >= 33 && png.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])), 'invalid PNG signature');
  const width = png.readUInt32BE(16), height = png.readUInt32BE(20);
  check(width > 0 && height > 0 && width <= 8192 && height <= 8192, 'unsupported PNG dimensions');
  check(png[24] === 8 && png[25] === 6 && png[26] === 0 && png[27] === 0 && png[28] === 0, 'expected noninterlaced RGBA8 PNG');
  const chunks = [];
  for (let offset = 8; offset < png.length;) {
    check(offset + 12 <= png.length, 'truncated PNG chunk');
    const length = png.readUInt32BE(offset);
    check(offset + length + 12 <= png.length, 'truncated PNG payload');
    if (png.toString('ascii', offset + 4, offset + 8) === 'IDAT') chunks.push(png.subarray(offset + 8, offset + 8 + length));
    offset += length + 12;
  }
  const stride = width * 4;
  const filtered = inflateSync(Buffer.concat(chunks), { maxOutputLength: (stride + 1) * height });
  check(filtered.length === (stride + 1) * height, 'PNG scanline length differs');
  const pixels = Buffer.alloc(stride * height);
  const paeth = (left, above, upperLeft) => {
    const prediction = left + above - upperLeft;
    const dl = Math.abs(prediction - left), da = Math.abs(prediction - above), du = Math.abs(prediction - upperLeft);
    return dl <= da && dl <= du ? left : da <= du ? above : upperLeft;
  };
  for (let y = 0; y < height; y++) {
    const filter = filtered[y * (stride + 1)];
    check(filter <= 4, 'invalid PNG scanline filter');
    for (let x = 0; x < stride; x++) {
      const index = y * stride + x;
      const left = x >= 4 ? pixels[index - 4] : 0;
      const above = y > 0 ? pixels[index - stride] : 0;
      const upperLeft = y > 0 && x >= 4 ? pixels[index - stride - 4] : 0;
      const prediction = filter === 0 ? 0 : filter === 1 ? left : filter === 2 ? above : filter === 3 ? Math.floor((left + above) / 2) : paeth(left, above, upperLeft);
      pixels[index] = (filtered[y * (stride + 1) + x + 1] + prediction) & 255;
    }
  }
  return { width, height, pixels };
}
export function validateAlphaPadding(manifest, decoded) {
  let checkedFrames = 0, maximumRingAlpha = 0;
  for (const atlas of manifest.atlases.filter(value => waveAtlasIds.includes(value.id))) {
    const image = decoded.get(atlas.id);
    check(image && image.width === atlas.width && image.height === atlas.height, 'missing or mismatched decoded atlas: ' + atlas.id);
    for (const role of manifest.roles.filter(value => value.atlas === atlas.id)) for (const frame of role.frames) {
      const right = frame.x + frame.width, bottom = frame.y + frame.height;
      check(frame.x >= atlas.padding && frame.y >= atlas.padding && right + atlas.padding <= image.width && bottom + atlas.padding <= image.height, 'alpha ring outside atlas: ' + role.id);
      let visible = false;
      for (let y = frame.y - atlas.padding; y < bottom + atlas.padding; y++) for (let x = frame.x - atlas.padding; x < right + atlas.padding; x++) {
        const alpha = image.pixels[(y * image.width + x) * 4 + 3];
        const inside = x >= frame.x && y >= frame.y && x < right && y < bottom;
        if (inside) visible ||= alpha > waveAtlasPaddingAlphaMaximum;
        else {
          maximumRingAlpha = Math.max(maximumRingAlpha, alpha);
          check(alpha <= waveAtlasPaddingAlphaMaximum, 'visible alpha in outside padding ring: ' + role.id + ' at ' + x + ',' + y + ' alpha=' + alpha);
        }
      }
      check(visible, 'empty visible frame: ' + role.id);
      checkedFrames++;
    }
  }
  return { alphaCheckedFrames: checkedFrames, maximumRingAlpha, paddingAlphaMaximum: waveAtlasPaddingAlphaMaximum };
}
export function validateArcGeometry(manifest, decoded) {
  const image = decoded.get('wave1a-arcs');
  check(image, 'missing decoded arc atlas');
  const roles = manifest.roles.filter(role => role.atlas === 'wave1a-arcs');
  check(roles.length === 2, 'expected two authored arc roles');
  let maximumAngleDegrees = 0, maximumVisibleRadius = 0;
  for (const role of roles) for (const frame of role.frames) {
    for (let y = frame.y; y < frame.y + frame.height; y++) for (let x = frame.x; x < frame.x + frame.width; x++) {
      if (image.pixels[(y * image.width + x) * 4 + 3] <= waveAtlasPaddingAlphaMaximum) continue;
      const dx = ((x + 0.5 - frame.x) / frame.width - role.pivot.x) * role.worldSize.x;
      const dy = (1 - (y + 0.5 - frame.y) / frame.height - role.pivot.y) * role.worldSize.y;
      check(dx >= 0 && Math.abs(dy) <= dx + 1e-9, 'arc visible pixel outside forward 90-degree sector: ' + role.id);
      const radius = Math.hypot(dx, dy);
      check(radius <= 1 + 1e-9, 'arc visible pixel outside unit collision radius: ' + role.id);
      maximumAngleDegrees = Math.max(maximumAngleDegrees, Math.abs(Math.atan2(dy, dx)) * 180 / Math.PI);
      maximumVisibleRadius = Math.max(maximumVisibleRadius, radius);
    }
  }
  return { arcRoles: roles.length, maximumAngleDegrees, maximumVisibleRadius };
}
export function validateRepository(root, { registry = true } = {}) {
  const manifest = read(path.join(root, 'unity/Assets/Art/wave1a-manifest.json'));
  const result = validateManifest(manifest, requiredBindings(root));
  const decoded = new Map();
  for (const id of waveAtlasIds) check(manifest.atlases.some(atlas => atlas.id === id), 'missing wave atlas: ' + id);
  for (const atlas of manifest.atlases) {
    const png = fs.readFileSync(path.join(root, 'unity', atlas.path));
    check(png.length >= 33 && png.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])), `atlas ${atlas.id}: invalid PNG`);
    check(png.readUInt32BE(16) === atlas.width && png.readUInt32BE(20) === atlas.height, `atlas ${atlas.id}: dimensions differ`);
    check(png[25] === 6 || png[25] === 4 || (png[25] === 3 && png.includes(Buffer.from('tRNS'))), `atlas ${atlas.id}: missing alpha`);
    if (waveAtlasIds.includes(atlas.id)) decoded.set(atlas.id, decodeRgbaPng(png));
  }
  return { ...result, ...validateAlphaPadding(manifest, decoded), ...validateArcGeometry(manifest, decoded), ...(registry ? validateRegistry(root, manifest) : { registry: 'not-checked' }) };
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.resolve(process.argv[2] && !process.argv[2].startsWith('--') ? process.argv[2] : path.join(path.dirname(fileURLToPath(import.meta.url)), '..'));
  console.log(JSON.stringify(validateRepository(root, { registry: !process.argv.includes('--source-only') })));
}
