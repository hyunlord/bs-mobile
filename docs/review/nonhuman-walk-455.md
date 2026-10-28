# Nonhuman walks — #455

Status: runtime verification passed on 2026-10-28; draft PR, C05 and user video acceptance pending. No performance claim from offline rendering.

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
- EditMode contracts require 16/28,16/40,16/20 actual EnemyRigAtlas samples, same texture/pivots/world size as idle, while human eight-frame behavior remains protected. Passed in both profiles.
- Core/data/replay files unchanged; actual terminal replay hash and full profiles verified below.
- Reimport: download the three `*-16frames.png` files into `artifacts/nonhuman455/source`, then run the importer without `--check`. Source hashes are pinned in `nonhuman-approved.json`.

## Executed runtime evidence

Tested candidate source `0acf04caf16a7c3d601ab4c7d73cc23604ad48ae`; baseline `188f75e2598e60de8e8aa8d985d140a6eabfbdb5` is main `59d4080` plus the byte-identical test-only exporter. No production renderer change. Both worktrees remained tracked-clean after execution. Shared Mac lock was released after tests and encoding.

Seed30000 original wave replay SHA-256 `060a94a1c9a88f108550dda42c2236dcb6e1cf2791a486a5f6d48ace40423a24`. Selected ticks2101–3000 cover the first wasp spawn at2250; 900 Core ticks produce1800 actual interpolated GPU frames, fixed60fps/30s. Original terminal tick3000, Quit, checkpoint1/1, terminal hash **866612B830A6313E9E1EB1EC883C59FE07B188A14CF4FC5F8FE6CA601E2AC2CF**. All1800 pairs match tick, input, state hash, interpolation, camera, instances and actual enemy identities/positions. The initial2701–3600 selection exceeded the replay terminal and failed before any image; that raw failure is preserved. The window was corrected once before capture; source was unchanged.

| WorldRenderer Flush submissions | Before | After | Delta |
|---|---:|---:|---:|
| Mean | 11.5517 | 11.5517 | 0% |
| p95 count | 16 | 16 | 0% |
| Maximum count | 25 | 25 | 0% |

These are renderer Flush submissions, not total Unity Profiler draw calls, milliseconds or C05 performance. The increase-at-most20% submission comparison passes; C05 remains Claude Code night-queue work at the final PR SHA.

| Actual species | Frames present | Frames with center inside viewport |
|---|---:|---:|
| seed_mite | 1800 | 1800 |
| crop_grazer | 1800 | 1800 |
| wine_wasp | 1500 | 780 |

Center visibility does not prove unoccluded pixels or every selected animation cell. Independent visual samples1300/1320/1340 and1700/1704/1708 confirm actual three-species rendering and changed leg/wing phases without an observed alpha, UV or ordering regression. Delivery sample1500 covers both seed and grazer; sample1740 covers wasp. This is not user acceptance or exhaustive16-cell visual certification.

Review video uses actual720×1560 captures resized to390×844 panels, side by side with a56px header:780×900,60fps,1800frames,30.000s, no temporal interpolation or audio. Missing ffmpeg drawtext was an artifact-only failure, replaced by a PNG header without rerendering.

## Unity tests

commit: 0acf04caf16a7c3d601ab4c7d73cc23604ad48ae
first-playable EditMode: passed 868, failed 0, skipped 25
first-playable PlayMode: passed 55, failed 0, skipped 45
wave-1a EditMode: passed 878, failed 0, skipped 15
wave-1a PlayMode: passed 87, failed 0, skipped 13
skipped in every profile: 0 unexplained, 1 listed

The stock runner exit1 and original record with2 unexplained inputs are preserved. Existing fixture-sidecar and melee-catalog generators were then run, followed by the actual two skipped tests: combat1/0/0/0 and melee1/0/0/0 (passed/failed/skipped/inconclusive). Combined records therefore have0 unexplained and1 listed optional sequence-export skip, without widening exceptions. Canonical first-playable profile was restored and tracked files stayed clean. Candidate cyan monitor2066 events:2064 real captures had0 cyan; the two1-pixel events are deliberate guard negative controls, not render failures.

## Reproduction and delivery

Raw local evidence remains in `artifacts/nonhuman455/{capture-after-r1,full-profiles}`, baseline worktree `artifacts/nonhuman455/capture-before-r1`; analyzer result `artifacts/nonhuman455/pair-validation.json`. Capture uses `WAVE_QA_OFFLINE_60FPS=1` and `WaveWorldTests.RecordedWaveRangeExportsActualTickFrames` with the same replay and2101–3000 interval. Default30Hz export remains unchanged.

Selected video and two unique samples: [nonhuman-walk-455-20261028](https://github.com/hyunlord/bs-mobile/releases/tag/nonhuman-walk-455-20261028). Full successful raw frame bundles are not uploaded. No change to #410; no merge authorization implied.
