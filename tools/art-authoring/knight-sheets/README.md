# Knight sprite sheets

Offline packing of Astra's knight game sheets into `unity/Assets/Art/hero-knight.png`.

```sh
gh release download knight-game-sheets-r4-20261016 -D /tmp/knight-approved
gh release download knight-game-sheets-r4-additions-review-20261016 -D /tmp/knight-additions
node tools/art-authoring/knight-sheets/pack.mjs /tmp/knight-approved /tmp/knight-additions
```

- Every delivered 320x240 cell is copied unchanged, one clip per row (walk, slash, aim, cast, blow, throw, moving keys, idle,
  hit). The delivered decoded RGBA SHA-256 of each 240x200 source is re-checked from the packed atlas.
- The same roles are written to both art manifests (`hero.core.frontier_knight.*`), so no profile lacks a hero role.
- `packed-frames.json` records the delivery manifests, each cell's rectangle and `frameTicks60`, and the atlas hash. The
  runtime uses the exact 60 Hz tick timings (`HeroSheetSpec`); the manifest `frameMs` is only rounded for reading.
- Idle and hit are review candidates used temporarily: repack a newer delivery with the same clip names to replace them.
- After repacking, register the atlas GUID in `unity/Assets/Resources/FirstPlayableArt.asset` and `Wave1aArt.asset` if the
  atlas is new, then run `node tools/first-playable-art.mjs "$PWD"` and `node tools/wave1a-art.mjs "$PWD"`.
