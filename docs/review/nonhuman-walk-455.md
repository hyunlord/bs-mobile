# Nonhuman walks — #455

Status: implementation prepared; Unity, runtime video, measured draw calls and replay execution pending. No game acceptance or C05 performance claim.

Approved input: [m3-nonhuman-walk16-449-20261027](https://github.com/hyunlord/bs-mobile/releases/tag/m3-nonhuman-walk16-449-20261027), user approval 2026-10-28. seed_mite r1, crop_grazer r2, wine_wasp r2. Historical release approval-pending prose remains history.

| Role | Frames | frameMs | Cycle |
|---|---:|---:|---:|
| seed_mite | 16 | 28 | 448ms (old 440ms, +1.818%) |
| crop_grazer | 16 | 40 | 640ms, unchanged |
| wine_wasp | 16 | 20 | 320ms, unchanged |

## Isolation and exact pixels

Base main `59d4080`, independent of #410. The delivered shared atlas r2 contains #410 human walks; copying that whole file would wrongly import another PR. Instead, copy only the three approved 2048×512 sheets without resampling to their delivered x=2048, y=0/1024/2048 slots. Keep original main's 2048×2304 pixels byte identical. The unused #410 area remains transparent, and raider/shield/ram roles retain their original eight frames.

The atlas identity remains `fallow-enemy-rigs`, 3072×3072, four-pixel borders, existing pivots/world sizes/tweens. The delivered transparent tail at rows3072–3839 is omitted: the last full cell ends at3072, while the last nontransparent pixel is y2998. No crop coordinates change. The normal loader recalculates normalized UVs. No live skeleton, layer, material, shader or renderer change. First-playable art is unchanged. All other wave manifest roles, bindings and atlas records are hash protected.

RGBA8/no-mipmap arithmetic is **18→36MiB, +18MiB** against actual main, not the release's #410-based 30→45MiB. This is not measured GPU/process memory. Importer already permits 4096 and NPOT without resize/compression.

## Verification

- `node tools/art-authoring/enemy-rigs/import-nonhuman-walks.mjs --check`: 48 complete RGBA cells, 4px alpha border, full main region and unaffected manifest contracts.
- `node tools/verify-unity-boundaries.mjs`: passed.
- EditMode contracts require 16/28,16/40,16/20 actual EnemyRigAtlas samples, same texture/pivots/world size as idle, while human eight-frame behavior remains protected. Not run yet.
- Core/data/replay files unchanged; actual terminal replay hash and full profiles remain pending.
- Reimport: download the three `*-16frames.png` files into `artifacts/nonhuman455/source`, then run the importer without `--check`. Source hashes are pinned in `nonhuman-approved.json`.

## Runtime handoff

Use an identical original recorded wave replay, same 30-second interval and camera, `Time.captureFramerate=60` offline rendering. Do not duplicate 30Hz screenshots to claim 60Hz rendering. Verify all checkpoints and original terminal hash; count actual renderer draw calls before/after and report visibility limits. Select interval with all three actual species visible, not fabricated entities. Full profiles and capture require parent-owned serial Unity slot and shared Mac lock. C05 runs only in Claude Code's night queue, at the final PR source SHA. No change to #410.
