# Six articulated enemy rigs (#209)

## Approved walk replacement (#399, 2026-10-26)

The runtime atlas is now **2048×3328**. Its first 2048×2304 RGBA pixels remain exactly the historical atlas; the final four rows append the approved shield raider and ram runner walks, sixteen 256px cells each. Only their `.walk` roles change. Their original pivot/world size remain unchanged, `frameMs:40`, `tween:none`, no distance-cycle field: each cycle still lasts 640ms. Tell, idle and the other four species retain their original pixels and metadata. One shared texture retains the existing batch contract; the importer does not repaint, rescale or add outlines.

Approved input provenance, PNG SHA-256 and original part SHA-256 are pinned in `approved-walks.json`. Source Release: [enemy-walk-shield-ram-399-20261026](https://github.com/hyunlord/bs-mobile/releases/tag/enemy-walk-shield-ram-399-20261026). The Release's original candidate/PENDING label predates user approval and is preserved as history. The ram runner is a humanoid charger, not a boar.

```sh
gh release download enemy-walk-shield-ram-399-20261026 --repo hyunlord/bs-mobile \
  --pattern shield-raider-16.png --pattern ram-runner-16.png --dir artifacts/enemy-integration-prep
node tools/art-authoring/enemy-rigs/import-approved-walks.mjs --source artifacts/enemy-integration-prep
node tools/art-authoring/enemy-rigs/import-approved-walks.mjs --check
```

The importer is idempotent and checks the historical pixel hash before writing. It accepts the historical 2304px-high atlas or the already integrated 3328px atlas. After any historical `bake.mjs` regeneration/install below, **run this approved importer last**; never install the old fragment as the finished runtime manifest. The original baker is a preserved historical generator and does not recreate the approved SpriteSkin animation. If its historical pixels differ, the importer stops for review instead of silently dropping newer artwork. `--check` detects a reverted eight-frame runtime install. Native visual comparison, Unity suites, replay verification and the night C05 gate remain separate from this pixel/metadata validation.

## Historical authoring pipeline (preserved)

Generated separated-part drawings preserve the existing ink/gouache identities. Exact prompts, reference images, generation session, source file IDs and SHA256 are in `generation-provenance.json`. The actual images are 1254×1254, despite the requested 2048 size. Sources live in `unity/Assets/Game/Editor/Authoring/EnemyRigs/` and never execute in the player. No package/dependency was added.

From repository root:

```sh
node tools/art-authoring/enemy-rigs/author.mjs
node tools/art-authoring/enemy-rigs/bake.mjs
node tools/art-authoring/enemy-rigs/verify.mjs
```

`author.mjs` is the hand-authored anatomical rectangle/anchor hierarchy and produces `rigs.json`. `bake.mjs` defaults to `artifacts/grounded-motion/enemy-new/`; an optional first argument selects another output directory. After verification, copy its `fallow-enemy-rigs.png` to `unity/Assets/Art/`. Parent integration merges `manifest-fragment.json` into the shared wave manifest, which this lane does not modify.

Atlas: 2048×2304, 8 columns, 256px cells, 4px transparent inset. Six consecutive groups of eleven cells: walk0–7, tell0–1, idle. Species order: seed_mite, crop_grazer, raider, shield_raider, ram_runner, wine_wasp. Runtime role IDs: `enemy.rig.<species>.walk`, `.tell`, `.idle`. Roles contain8/2/1 frames, `tween:none`, a common species pivot/size, and authored frame duration. Runtime uses existing single-quad SpriteBatch UV selection, not live bone transforms. All species share one texture and a baked one-source-pixel ink alpha outline, eliminating duplicated enemy edge sprites. World sizes scale the idle opaque silhouette to the existing art height, while state changes keep the same pivot/size.

Six mite legs alternate tripods; four boar legs alternate diagonal pairs. Raider/shield/charger hips and knees solve two-link chains toward low foot arcs (10 authored pixels; charger16), with counter-moving arms and separate cape joints. The charger keeps a low torso/head and its two preparation keys lower the head farther. Wasp wings rotate and foreshorten independently; abdomen curls in its two preparation keys. The root never bobs, rotates or squashes. No generated image is merely rotated as an entire character.

Each inspected part rectangle selects its largest connected alpha component, with a one-pixel antialias fringe. This removes neighboring parts intruding into a rectangle without repainting or modifying original source pixels. Baking uses premultiplied bilinear sampling, 2× downsampling and a dark one-pixel alpha expansion. Dark charger sources contain no bright red area in any baked frame; active danger color is a runtime responsibility.

The verifier checks6 species,48 unique walking keys,12 distinct tell keys,6 idle keys, common pivots, fixed roots, actual transparent borders, plausible occupied areas, ink outlines and dark charger pixels. `baked-frames.json` records joint transforms/source hashes/pose bounds. `contact-sheet.png` is a regenerable authoring preview and is not a native gameplay/performance claim. Parent must still verify real-sized native walking/tells and crowd cost. Enemy source parts are generated art, not public-domain assets. Source art remains unchanged; runtime/Core data and historical gameplay hashes are outside this authoring lane.
