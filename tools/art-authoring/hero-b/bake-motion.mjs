// Authoring-only comparison: both runtime methods consume these same solved joints.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const output = path.resolve(root, process.argv[2] || 'artifacts/m1-246/hero-motion-b');
const rig = JSON.parse(fs.readFileSync(path.join(root, 'tools/art-authoring/hero-b/rig.json')));
const contract = JSON.parse(fs.readFileSync(path.join(root, 'tools/art-authoring/hero-b/motion-clips.json')));
const idle = rig.poses.find(pose => pose.id === 'idle');
assert.ok(idle);
const sample = (clip, time) => {
  let upper = clip.keys.findIndex(key => key.time >= time);
  if (upper <= 0) return { ...clip.keys[0].rotations };
  const a = clip.keys[upper - 1], b = clip.keys[upper];
  const t = (time - a.time) / (b.time - a.time);
  const rotations = {};
  for (const bone of rig.bones) rotations[bone.id] = (a.rotations[bone.id] || 0) * (1-t) + (b.rotations[bone.id] || 0) * t;
  return rotations;
};
const poses = [], clips = [];
for (const clip of contract.clips) {
  assert.equal(clip.keys[0].time, 0);
  assert.equal(clip.keys.at(-1).time, 1);
  clips.push({ id: clip.id, durationSeconds: clip.durationSeconds, firstFrame: poses.length, frameCount: clip.frameCount });
  for (let i = 0; i < clip.frameCount; i++) poses.push({ id: `${clip.id}-${i}`, feet: idle.feet, rotations: sample(clip, i / (clip.frameCount - 1)) });
}
fs.mkdirSync(output, { recursive: true });
const rigPath = path.join(output, 'motion-rig.json');
fs.writeFileSync(rigPath, JSON.stringify({ ...rig, poses }, null, 2) + '\n');
const result = spawnSync(process.execPath, ['tools/art-authoring/hero-b/bake.mjs', output, rigPath], { cwd: root, stdio: 'inherit' });
assert.equal(result.status, 0, 'motion bake must succeed');
const metadata = JSON.parse(fs.readFileSync(path.join(output, 'baked-frames.json')));
const jointNames = ['root', ...rig.bones.map(bone => bone.id)];
const frame = value => ({ id: value.id, joints: jointNames.map(name => ({ x: value.joints[name].x, y: value.joints[name].y, angleRadians: value.joints[name].angle })) });
const gaitDirectory = path.join(output, 'gait');
const gaitResult = spawnSync(process.execPath, ['tools/art-authoring/hero-b/bake.mjs', gaitDirectory], { cwd: root, stdio: 'inherit' });
assert.equal(gaitResult.status, 0, 'unchanged gait bake must succeed');
const gait = JSON.parse(fs.readFileSync(path.join(gaitDirectory, 'baked-frames.json')));
const runtime = { schemaVersion: 1, coordinateOrigin: 'top-left', canvasSize: 512, groundY: 420, root: rig.root, bones: rig.bones, sprites: rig.sprites, jointNames, clips, frames: metadata.frames.map(frame), gaitFrames: gait.frames.slice(0, 13).map(frame) };
fs.writeFileSync(path.join(output, 'motion-runtime.json'), JSON.stringify(runtime, null, 2) + '\n');
fs.writeFileSync(path.join(output, 'motion-bake-contract.json'), JSON.stringify({ schemaVersion: 1, clips, lowerBody: 'unchanged baseline gait atlas', upperBody: 'fallow-hero-b-layers.png, all four quadrants; use back/front only', columns: 4, rows: 4, sampleMode: 'nearest normalized action sample; live interpolates the same solved samples', sourceContract: 'tools/art-authoring/hero-b/motion-clips.json' }, null, 2) + '\n');
console.log(JSON.stringify({ actionFrames: poses.length, bones: jointNames.length, layersPngBytes: fs.statSync(path.join(output, 'fallow-hero-b-layers.png')).size, runtimeDataBytes: fs.statSync(path.join(output, 'motion-runtime.json')).size, output }, null, 2));
