// Reuse the historical offline compositor without modifying its B inputs or output.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const output = path.resolve(root, process.argv[2] || 'artifacts/m1-254/hero-a-authoring');
let code = fs.readFileSync(path.join(root, 'tools/art-authoring/hero-b/bake.mjs'), 'utf8');
code = code.replace("from '../../wave1a-art.mjs'", `from '${pathToFileURL(path.join(root, 'tools/wave1a-art.mjs')).href}'`)
  .replace("const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');", `const root = ${JSON.stringify(root)};`)
  .replace("unity/Assets/Game/Editor/Authoring/HeroB/hero-b-cutout-original.png", 'unity/Assets/Game/View/Resources/HeroKnight/parts.png')
  .replace("process.argv[2] || 'artifacts/grounded-motion/hero-new'", JSON.stringify(output))
  .replace("process.argv[3] || 'tools/art-authoring/hero-b/rig.json'", "'tools/art-authoring/hero-a/rig.json'");
const result = spawnSync(process.execPath, ['--input-type=module', '-e', code], { cwd: root, stdio: 'inherit' });
if (result.status !== 0) process.exit(result.status ?? 1);

// Publish only A bindings and calibrated joints; never replace historical B assets.
const authored = JSON.parse(fs.readFileSync(path.join(output, 'baked-frames.json')));
const rig = JSON.parse(fs.readFileSync(path.join(root, 'tools/art-authoring/hero-a/rig.json')));
const runtime = JSON.parse(fs.readFileSync(path.join(root, 'unity/Assets/Game/View/Resources/HeroMotion/motion-runtime.json')));
runtime.partsResource = 'HeroKnight/parts';
runtime.swordResource = 'HeroMotion/sword';
runtime.sprites = rig.sprites;
runtime.gaitFrames = authored.frames.slice(0, 13).map(frame => ({ id: frame.id, joints: runtime.jointNames.map(name => ({ x: frame.joints[name].x, y: frame.joints[name].y, angleRadians: frame.joints[name].angle })) }));
runtime.farSoles = authored.frames.slice(0, 12).map(frame => frame.soles.far);
runtime.nearSoles = authored.frames.slice(0, 12).map(frame => frame.soles.near);
for (const frame of runtime.frames) for (const name of ['farHip', 'farKnee', 'nearHip', 'nearKnee']) {
  const index = runtime.jointNames.indexOf(name);
  frame.joints[index] = { ...runtime.gaitFrames[12].joints[index] };
}
fs.writeFileSync(path.join(root, 'unity/Assets/Game/View/Resources/HeroKnight/motion-runtime.json'), JSON.stringify(runtime, null, 2) + '\n');
