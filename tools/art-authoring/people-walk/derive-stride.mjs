// Read-only source calculation of approved PeopleGait.cs using PNG alpha and JavaScript double precision.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const dir = path.resolve(process.argv[2] || 'artifacts/people433-source');
const expected = {
  vassal: '1b26a4331dd8019ccba2bf9de967d49a7347a021eebb9258903c21e508fd57f3',
  peasant: 'cd3ac70f400aa708df33c07fed5a390f1cab5ac545e90bc7d2b8f969aad269b3',
};
const results = [];
for (const kind of ['vassal', 'peasant']) {
  const stem = `person-${kind}-r1`;
  const bytes = fs.readFileSync(path.join(dir, `${stem}-rig-source.png`));
  const sha256 = createHash('sha256').update(bytes).digest('hex');
  assert.equal(sha256, expected[kind]);
  const image = decodeRgbaPng(bytes);
  const config = JSON.parse(fs.readFileSync(path.join(dir, `${stem}-rig-config.json`), 'utf8'));
  const contour = [];
  let maxX = 0, toe;
  // PNG rows are top-down. Unity GetPixel(x, source.height-1-y) addresses exactly PNG row y.
  // Unity alpha <.5 rejects integer alpha0..127; 128..255 are included. Coordinates use pixel centers.
  for (let x = config.shinRect.x; x < config.shinRect.x + config.shinRect.width; x++) {
    for (let y = config.shinRect.y + config.shinRect.height - 1; y >= Math.ceil(config.ankleSource.y + config.footBlendWidth); y--) {
      if (image.pixels[(y * image.width + x) * 4 + 3] < 128) continue;
      const point = { x: x + .5, y: y + .5 };
      contour.push(point);
      if (x > maxX) { maxX = x; toe = point; }
      break;
    }
  }
  assert.ok(contour.length && toe);
  const offset = p => ({ x: (p.x - config.ankleSource.x) * config.shinScale, y: (config.ankleSource.y - p.y) * config.shinScale });
  const rotate = (p, angle) => {
    const a = angle * Math.PI / 180;
    return { x: p.x * Math.cos(a) - p.y * Math.sin(a), y: p.x * Math.sin(a) + p.y * Math.cos(a) };
  };
  const lowest = angle => {
    let result, min = Infinity;
    for (const p of contour) { const v = rotate(offset(p), angle); if (v.y < min) { min = v.y; result = v; } }
    return result;
  };
  const integral = new Float64Array(2001);
  for (let i = 1001; i <= 2000; i++) integral[i] = integral[i - 1] + lowest((i - 1000 - .5) / 20).y * (Math.PI / 180) / 20;
  for (let i = 999; i >= 0; i--) integral[i] = integral[i + 1] - lowest((i - 1000 + .5) / 20).y * (Math.PI / 180) / 20;
  const Integral = angle => {
    const z = angle * 20 + 1000, i = Math.max(0, Math.min(1999, Math.trunc(z)));
    return integral[i] + (integral[i + 1] - integral[i]) * (z - i);
  };
  const deltaIntegral = Integral(-25) - Integral(8);
  const deltaToeX = rotate(offset(toe), -25).x - rotate(offset(toe), 8).x;
  const strideSourcePx = 2 * (config.targetGap + deltaIntegral + deltaToeX);
  const candidate = JSON.parse(fs.readFileSync(path.join(dir, `${stem}-manifest-candidate.json`), 'utf8'));
  const inherited = 102.3603456;
  results.push({ kind, sourceSha256: sha256, imageSize: [image.width, image.height], contourPoints: contour.length, toeSource: toe,
    targetGap: config.targetGap, deltaIntegral, deltaToeX, strideSourcePx,
    inheritedStrideSourcePx: inherited, differenceSourcePx: strideSourcePx - inherited,
    inheritedVsAuthoredRatio: inherited / strideSourcePx,
    authoredCycleWorldUnits: strideSourcePx * candidate.worldSize.y / 256,
    inheritedCycleWorldUnits: inherited * candidate.worldSize.y / 256,
    note: 'Source-derived double precision approximation, not actual Unity float execution or runtime measurement' });
}
console.log(JSON.stringify({ formula: '2*(TargetGap+Integral(-25)-Integral(8)+RotatedToe(-25).x-RotatedToe(8).x)', results,
  returning: 'Same leg source/config/gait as peasant; SHARED-RIG confirms motion.csv byte equality; use peasant derivation' }, null, 2));
