# Knight sprite sheets

Offline packing of Astra's knight game sheets into `unity/Assets/Art/hero-knight.png`.

```sh
gh release download knight-game-sheets-r4-20261016 -D /tmp/knight-approved
gh release download knight-game-sheets-r4-additions-review-20261016 -D /tmp/knight-additions
node tools/art-authoring/knight-sheets/pack.mjs /tmp/knight-approved /tmp/knight-additions
```

- Every delivered 320x240 cell is copied unchanged, one clip per row (walk, slash, aim, cast, blow, throw, moving keys, idle,
  hit r5). The delivered decoded RGBA SHA-256 of each 240x200 source is re-checked from the packed atlas.
- The same roles are written to both art manifests (`hero.core.frontier_knight.*`), so no profile lacks a hero role.
- `packed-frames.json` records the delivery manifests, each cell's rectangle and `frameTicks60`, and the atlas hash. The
  runtime reads each clip's frame count and `frameMs` from the manifest role (`HeroSheetTimings`; `frameMs` is the first
  frame's `frameTicks60` rounded to ms and converted back to ticks), so a clip may change its frame count or timing.
  A clip uses one frame time; only the hit's last frame may differ (it holds while the hit lasts). `HeroSheetTests` checks
  the manifests against this record.
- The atlas has one row per clip and as many columns as the longest clip.
- Idle comes from the additions Release (approved 2026-10-17); hit is the approved r5 in the handoff Release (r4 hit rejected).
  A newer delivery under the same roles replaces them by repacking.
- After repacking, register the atlas GUID in `unity/Assets/Resources/FirstPlayableArt.asset` and `Wave1aArt.asset` if the
  atlas is new, then run `node tools/first-playable-art.mjs "$PWD"` and `node tools/wave1a-art.mjs "$PWD"`.
