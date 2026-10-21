# Hero B articulated authoring (#207, #209)

The user selected B on #209. Current refinement uses twelve distance-driven walk keys and three action keys, independently layered legs, and continuous contact correction. See [CONTACT-CONTRACT.md](CONTACT-CONTRACT.md) for the authoring/runtime contract. Bake and verify with:

```sh
node tools/art-authoring/hero-b/bake.mjs
node tools/art-authoring/hero-b/verify.mjs
```

Output is staged under `artifacts/grounded-motion/hero-new`; these commands no longer overwrite Unity atlases. An optional output-directory argument controls staging. The parent integrates the manifest and runtime assets only after native baseline capture. The preview and authored contact checks do not constitute native visual acceptance or measured zero slip. The earlier `baked-frames.json` in this source directory is the historical eleven-frame record until the final new record is installed.

## Historical #207 candidate

This is an **opt-in visual comparison candidate**, not a replacement approved by the user.
The runtime draws one baked atlas quad. This source hierarchy, joint solve and Node process never execute in a player.

Bake from repository root with `node tools/art-authoring/hero-b/bake.mjs`, or use Editor menu **Sow and Siege → Art → Bake articulated hero B preview**. Node is an existing repository prerequisite; no package is installed. If Unity's environment lacks Node on PATH, set `HERO_B_NODE` to the existing executable. The Editor wrapper has not been launched in this authoring lane; the parent serializes Unity verification.

`rig.json` holds authored source rectangles/anchors, parented joints, draw order and eleven keyposes. Shoulder rotation carries the elbow; elbow carries the sword. Cape upper/lower have distinct parented articulation. Legs solve two-link hip/knee joints toward authored stance/swing feet; root/torso translation remains fixed, without whole-body bob, scaling or squash. Eight walk keys alternate planted and lifting feet. Idle, attack and hit are independent keys. Open-hand alternatives in the source are preserved but unused by this sword-holding candidate.

The atlas is 2048×1536, eleven 512×512 frames, four columns. Walk indices0–7, idle8, attack9, hit10. `baked-frames.json` is the generated geometry/joint record. Rectangles use top-left image coordinates. All roles use bottom-left pivot `(0.546875, 0.1796875)` (pixel ground anchor280,420), four transparent perimeter pixels. Frames use a common canvas so switching does not recenter the character. Parent integration chooses worldSize from the approximately330px opaque hero height, **not** full512px frame height; verify against the actual A hero before claiming equal scale.

## Provenance and limitations

Original is copied unchanged to `unity/Assets/Game/Editor/Authoring/HeroB/hero-b-cutout-original.png`; its SHA256 is in `baked-frames.json`. Tool: builtin `image_gen.imagegen`, date2026-10-11, transparent_background=true, reference `unity/Assets/Art/fallow-actors.png`, generation output `exec-f664aa46-51b2-4473-a8fa-5bbef813e588.png` in generation session `01a117ea-b0bb-74e0-a7d2-f880a17826b1`. This is generated art, **not a CC0 claim**.

The original complete tool prompt was unavailable after conversation compaction. The following is explicitly a **reconstructed generation brief**, not a verbatim prompt:

> Only the top-row hero: cobalt cape, ivory quilted tunic, brown hair. Transparent2048×2048 requested; exact4×4 cells512. Row1 head/neck, torso/hips with split skirt and no appendages, upper cape mantle/clasp, lower cape tail/emblem. Row2 near upper arm, near forearm/closed hand, far upper arm, far forearm/closed hand. Row3 near thigh/knee, near shin/boot, far thigh/knee, far shin/boot. Row4 upright full sword, spare open near hand, spare open far hand, belt tab. Genuine separated transparent parts, no labels/grid/shadow/background; coherent proportions and lighting, drawn overlap at joints; same three-quarter overhead right-facing ink/gouache character.

Actual output is **1254×1254**, not requested2048. Some parts cross nominal cell boundaries, so explicit inspected part rectangles are used. The original contains thin red/yellow edge flecks around the sword and some skirt edges. A whole-sheet cleanup edit still retained blade defects and was rejected. A second sword-only imagegen edit supplied the clean724×2172 source `hero-b-sword-clean.png`; only the sword uses this source, while all body/cape parts remain original. Exact edit prompts and output provenance are in `generation-provenance.json`. Baking transforms/composites source pixels without repainting. The new rig is more upright than reference A. Small-scale contact-sheet inspection demonstrates assembly and differing limb/cape keys only; it does not prove in-game frame rate, natural movement feel, occlusion or user acceptance. Parent must inspect actual native A/B walking video.

Run `node tools/art-authoring/hero-b/verify.mjs` to verify11 frames, transparent8px perimeter, fixed root, distinct joint articulation and planted boot sole stability. Per-leg sole calibration keeps actual walk-frame bottom at420–421px; it never shifts the whole body. Manifest integration can inset every cell by4px to504×504, with pivot `(276/504,88/504)`, retaining transparent borders.

## M1-1 motion method comparison (#246, not a method selection)

`motion-clips.json` is the common authored rotation/time contract for baked (b) and live Unity 2D Animation (c). It uses the existing B source parts without repainting. Three upper-body clips return to the same idle pose: iron-blade swing (0.36s, 6 samples), evolved larger swing (0.48s, 6 samples), and hit (0.20s, 4 samples). These durations are presentation hypotheses, not weapon attack timing or Core rules. Existing distance-driven twelve-frame legs continue independently; hit does not move the root. Linear interpolation is declared explicitly. The live method interpolates the same solved samples; the baked method selects their nearest sample. That temporal distinction is intentional comparison evidence, not two different authored performances.

```sh
node tools/art-authoring/hero-b/bake-motion.mjs
node tools/art-authoring/hero-b/verify-motion.mjs
node tools/art-authoring/hero-b/verify.mjs artifacts/m1-246/hero-motion-b/gait
```

The bake stages outputs at `artifacts/m1-246/hero-motion-b`; it does not replace player assets. `motion-runtime.json` exports fifteen joints in `jointNames` order (root first), the exact original bone/sprite definitions, sixteen solved action samples and thirteen unchanged gait/idle samples. Joints use top-left authoring coordinates, radians and a 512px canvas. `clips` supplies first frame, frame count and duration. `motion-bake-contract.json` is the atlas integration contract. Action atlas is 2048×2048, arranged as four 1024×1024 layer quadrants, each holding sixteen 256px cells; select back/front from this sheet while legs come from the unchanged gait sheet. The generic legacy `manifest-*-fragment.json` files emitted by the underlying baker describe legacy roles and must **not** be used to register this action sheet.

The full pose atlas and preview are review intermediates; only the layered action atlas and shared runtime contract need player integration. Current layered PNG is 936,298 bytes, runtime JSON 62,408 bytes (regenerate to verify). A 2048² RGBA32 unmipped texture is 16 MiB uncompressed by arithmetic; actual imported texture format, resident memory and build size must be measured in the comparison player. PNG size is not a build-size result.

For one additional weapon family, the current authoring structure requires one source weapon/attachment definition when its shape differs, one approximately five-key normal upper clip, one approximately five-key evolved upper clip, a bake and three integration checks (walking attack, evolution, hit interruption). Those are work units, not measured hours. Method (c) can reuse the same keys without a new baked action texture; both still require artistic pose and small-screen validation. Hand-drawn frames are reserved for accents such as swing afterimages, not the whole hero. No package or enemy animation is changed by this authoring tool.

`verify-motion.mjs` checks all 240 exported joint samples against baked geometry, fixed root, unclipped eight-pixel perimeter, three distinct sword articulations per clip and return to idle. It does not establish visual naturalness, runtime performance, memory, input/hash parity or user acceptance; those belong to the same-input native comparison.
