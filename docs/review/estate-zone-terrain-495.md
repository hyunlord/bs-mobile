# r8 zone terrain authoring groundwork (#495)

관문: 부분 — 승인 소스 64/64 SHA·PNG 크기 일치, Node 16/16 통과. 게임 렌더·밀도·카메라·Unity는 NOT_RUN.

2026-10-28. Base main `31f64a656ca528ce8a1617aaead0abd3946c81a9` (#485). Design: [#490 v0.3](https://github.com/hyunlord/bs-mobile/pull/490), related #489. This is authoring groundwork, not the completed zone terrain feature. No Core/data/Unity files, imported textures, save format, historical r3–r7 settings or original art change.

## Sources and explicit scope

[source-assets.json](../../tools/art-authoring/estate-zone-terrain/source-assets.json) pins the approved [estate-r8-terrain-488-20261028 Release](https://github.com/hyunlord/bs-mobile/releases/tag/estate-r8-terrain-488-20261028) to exact file/SHA/canvas/top-left pivot metadata. Its source lists are CLUSTERS-r1.json, PATCHES-final-r4.json, ACCENTS-final-r2.json and BRIDGES-final-r2.json. Historical approval-pending labels in those originals are not rewritten; current user approval authorizes this selection.

128 source references include alternative scales. Select **54 1x sprite PNGs + 10 flat-UV 512px floor PNGs = 64**. The 2x/1024 alternatives are not duplicate runtime imports. All 64 files were downloaded to this worktree's untracked artifacts and SHA256 plus PNG IHDR dimensions checked. No pixel editing, atlas packing, alpha-quality claim or GPU allocation occurred. Floor material projection remains distinct from isometric sprite projection. Runtime Unity pivot conversion will be `(x/width, 1-y/height)`; source coordinates remain unmodified here.

River r5 registration/chunks are deliberately outside this first slice: the existing river is unchanged. The new source mapping is **not** an EstateArt role registration. References cannot be used by the product renderer yet.

## Executable candidate contract

[contract.json](../../tools/art-authoring/estate-zone-terrain/contract.json) maps the exact 13 r8 zones. Village, approach and meadow preserve existing decoration; new wood, fields, hill, fallow, ruins and riverside candidates refer to approved paired sources. Missing or duplicate zone/theme/source identities fail validation rather than silently falling back.

[plan.mjs](../../tools/art-authoring/estate-zone-terrain/plan.mjs) implements AstraDecor's UTF-16 FNV-1a arithmetic, map-cell anchored candidates, stable ordinal tie-breaking, per-zone limits chosen before mutable exclusions (hide-only, no refill), full rectangular footprint containment and supplied reservation exclusion. Open state changes only the selected bright/dark file. Camera, zoom and list order never enter the seed. Historical profiles return no candidates. The emitted JSON is explicitly `OFFLINE_CANDIDATES_NOT_RUNTIME_OR_DENSITY_PROOF`.

The future Unity adapter must consume emitted stable candidates, not silently introduce a second divergent sampler. It must re-evaluate current building, yard, door, curved road, plaza, river, bridge and **projected alpha/display** exclusions when state changes. Today's rectangular candidate exclusions are not that full check. Unspecified shore/terrain reservations are labelled hypotheses; metadata floor dimensions are not proven physical collision geometry. Moving buildings must hide conflicting candidates without changing Core rules.

There is **no positional jitter or adjacent-variant avoidance yet**. These remain integration work with the projected-art exclusion stage. Do not claim final art density or repeat-pattern quality from candidate counts. Candidates are not automatically lit just because a saved zone opened: the future adapter must retain fallow/sage/restored progression.

## Camera decision history

At the first groundwork commit, building-width candidates were75px and50px with null default. The later user decision (2026-10-28 21:50) fixes **default75px / minimum50px**; current authoring contract and validation reflect it. Existing product camera remains unchanged in this scoped commit. Integration must use one product setting and compare the same density targets at both widths without reseeding placement. The actual Unity home BodyWidth105.75 and Astra preview native98.7 are different references: product camera must calibrate from actual displayed artwork, not copy the preview scale.

Opened-zone union drag bounds plus 1–2 cells, all-zoom continuous dark forest/fog and zero uncovered black areas are still NOT_IMPLEMENTED in this groundwork. No current camera behavior is claimed to satisfy them.

## Reproduce

```sh
node --test tools/art-authoring/estate-zone-terrain/plan.test.mjs
node tools/art-authoring/estate-zone-terrain/plan.mjs \
  tools/art-authoring/estate-zone-terrain/contract.json \
  tools/art-authoring/estate-zone-terrain/source-assets.json \
  data/opt-in/account-estate/m3a-prototype-r8.json
```

The tests are included in `tools/check.sh`: identity/geometry/pair failure cases, CLI unknown-argument rejection, exact hash vectors, reversed inputs, zoom independence, open-order invariance, old-profile routing, footprint/reservation exclusion, stale SHA and unsafe source path rejection. Initial test-first failure was the missing implementation module; subsequent tests pass. No repeated product/runtime failure occurred.

## Remaining full-feature gate

1. Register approved assets with source identity and texture-memory delta records; preserve original atlas/role cells.
2. Add the r8-only Unity consumer and projected-art/current-state exclusions, art repeat avoidance and stable jitter.
3. Add floor UV, river registration, camera preview controls/bounds and continuous forest/fog coverage.
4. Capture normal progression 7/13 and 13/13 at both 75/50 previews; measure true-alpha empty area, maximum component, visible objects, door occlusion and draw batches.
5. Run both profiles with cyan monitoring and historical hashes/saves; preserve any failed visuals and stop on the same failure twice.

No Unity lock was taken for this slice. No Unity/build/native run, screenshot, density pass, performance pass or user visual acceptance is implied. Draft only; no merge without user approval.

## First CI failure preserved

The first secrets run40688311728 flagged 12 public `shore-accent-01..04` art IDs because the JSON field was named `key`. No credentials were involved. The current field is `variantId`; only the 12 exact historical47e2f2af fingerprints are listed in the existing `.gitleaksignore`, with no current-path or scanner-rule exception. The failed run remains visible.

## Approved dense source follow-up (21:50 decision)

[dense-source-assets.json](../../tools/art-authoring/estate-zone-terrain/dense-source-assets.json) adds120 exact approved identities: props24, river92, fog4. All120 PNG SHA256 and IHDR sizes were checked against downloaded files; originals stay in untracked artifacts, with no runtime import. Both scales remain provenance alternatives (60 at1x), not120 textures to load. Select registered chunks OR validated contextual modules at matching sockets, never overlay both. Chunks use explicit top-left anchor(0,0) plus global nativeOrigin; modules preserve floorAnchorNative; props preserve their source pivots. Fog is exterior overlay, not ground or opaque-tree coverage.

The registry pins SHA256 of the six requested metadata sources, preserves NATIVE-CAMERA-r1 as **offline art camera**, and identifies DENSE-SCENES-r2's2110 placements as art composition, not validated runtime placement. Historical metadata approval-pending text remains source history; latest user approval authorizes this selection. River exact asset SHA comes from GitHub Release asset digest and is checked against actual downloaded PNG, not inferred from a filename.

Optional small-prop spacing choices use stable per-row/per-cell hashes; optional scaleRange is constrained to(0,1], keeps the full conservative reservation rectangle, and cannot enlarge collision bounds. Defaults retain prior spacing and scale1. Tests exercise varying small props, reversed spacing input, camera independence, retained boundary/overlap checks and hide-only reservation changes. Projected-art exclusions remain the future View consumer's responsibility. No density result is inferred.

Rebuild source registry after downloading the six metadata files plus Release asset JSON:

```sh
node tools/art-authoring/estate-zone-terrain/dense-sources.mjs artifacts/estate-zone-terrain
```

Final local Node suite16/16. CI source changes only authoring tools and this report; another owner may have uncommitted Unity camera work in the same worktree, which is explicitly excluded from this commit and its claims.

Small-prop variation is supported/tested by fixtures only: current contract's village/approach/meadow still preserve existing decoration, so this commit does not enable new small-prop placement in the product or authoring default layout.
