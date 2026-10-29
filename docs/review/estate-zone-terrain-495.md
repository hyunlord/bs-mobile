# r8 zone terrain authoring groundwork (#495)

관문: 부분 — 승인 소스 64/64 SHA·PNG 크기 일치, Node 18/18 통과. 게임 렌더·밀도·카메라·Unity는 NOT_RUN.

2026-10-28. Base main `31f64a656ca528ce8a1617aaead0abd3946c81a9` (#485). Design: [#490 v0.4](https://github.com/hyunlord/bs-mobile/pull/490), related #489. This is authoring groundwork, not the completed zone terrain feature. No Core/data/Unity files, imported textures, save format, historical r3–r7 settings or original art change.

## Sources and explicit scope

[source-assets.json](../../tools/art-authoring/estate-zone-terrain/source-assets.json) pins the approved [estate-r8-terrain-488-20261028 Release](https://github.com/hyunlord/bs-mobile/releases/tag/estate-r8-terrain-488-20261028) to exact file/SHA/canvas/top-left pivot metadata. Its source lists are CLUSTERS-r1.json, PATCHES-final-r4.json, ACCENTS-final-r2.json and BRIDGES-final-r2.json. Historical approval-pending labels in those originals are not rewritten; current user approval authorizes this selection.

128 source references include alternative scales. Select **54 1x sprite PNGs + 10 flat-UV 512px floor PNGs = 64**. The 2x/1024 alternatives are not duplicate runtime imports. All 64 files were downloaded to this worktree's untracked artifacts and SHA256 plus PNG IHDR dimensions checked. No pixel editing, atlas packing, alpha-quality claim or GPU allocation occurred. Floor material projection remains distinct from isometric sprite projection. Runtime Unity pivot conversion will be `(x/width, 1-y/height)`; source coordinates remain unmodified here.

Approved river r2 registration/chunks are recorded in the dense source registry below; river r3 is explicitly not adopted. The existing product river is unchanged. The new source mapping is **not** an EstateArt role registration. References cannot be used by the product renderer yet.

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

Historical 2026-10-28 local Node suite16/16. CI source changes only authoring tools and this report; another owner may have uncommitted Unity camera work in the same worktree, which is explicitly excluded from this commit and its claims.

Small-prop variation is supported/tested by fixtures only: current contract's village/approach/meadow still preserve existing decoration, so this commit does not enable new small-prop placement in the product or authoring default layout.

## 2026-10-29 field r3 selection; river r2 retained

The latest user decision approves **field props only** from VARIETY-FIELDS-r3: six forms × three nominal sizes (S64×32 / M96×48 / L128×64), each bright/dark and1x/2x =72 source PNG alternatives. [field-source-assets.json](../../tools/art-authoring/estate-zone-terrain/field-source-assets.json) pins exact SHA256, canvas, floor and top-left pivot. All72 downloaded PNGs match SHA256 and IHDR sizes. Together with unchanged64 initial and120 dense sources, this is256 verified source alternatives, not256 runtime textures.

The field-slot selector returns only these r3 forms. The previous24 general props remain preserved for non-field slots; preservation is not permission to stack both sets in a field slot. The92 river assets remain byte-for-byte the previous dense-river-r2 selection. River r3 is **not adopted**, even though it is in the same Release and r3 preview images. No r3 scene composition or river metadata is selected or imported. The original metadata's approval-pending label is preserved as history; it does not negate the later field-only user approval.

Pivots are visually estimated floor centers, not physically measured vertices. Bright/dark silhouette differences are retained, not normalized. Field-between05 soil must be excluded from prop occupancy/density counts; nominal floor rectangles are not opaque occupancy evidence. These sources do not prove runtime density or overall r3 preview approval.

Reproduce the source registry after downloading VARIETY-FIELDS-r3.json, VARIETY-PIECES-r3.md, VARIETY-PROVENANCE-r3.json and the Release asset listing:

```sh
node tools/art-authoring/estate-zone-terrain/field-sources.mjs artifacts/estate-zone-terrain
node --test tools/art-authoring/estate-zone-terrain/plan.test.mjs
```

Light Node suite18/18 passes, including all18 form/size combinations in both states/resolutions, no general-prop fallback, invalid pivot/duplicate rejection and explicit river-r3 injection rejection. Dense registry and original64 registry remain unchanged. Unity/build/game application remain NOT_RUN; the six local camera files belonging to the other owner are not staged. No PR body update is made after this push.

## 2026-10-30 야간 구현 — Unity 미검증

사용자 야간 제한에 따라 Unity·Standalone·dotnet·성능 실행과 Mac 잠금은 사용하지 않았다. 아래는 코드/정적 증거이며 화면 승인이나 실제 밀도 통과가 아니다.

- r8만 건물 몸체 폭 기준 기본75px / 핀치 하한50px / 별도 전체 보기. 드래그 중심은 열린 구역 합집합+1.5셀로 제한한다. r7 이전 카메라는 유지한다.
- 승인 1x 128개를 공용 atlas 하나에 등록했다. 원본 SHA와 pivot을 유지하고 field r3의 6형태×3크기, 군집, fog, 승인 **dense-river-r2 등록 chunk24개**를 포함한다. 강 r3는 포함하지 않는다. 강은 승인 nativeOrigin(64×32 투영)에 놓고 기존 반복 강/강둑 조각을 대체하며 다리는 유지한다.
- r8 구역별 군집과 소품은 seed·구역ID·후보 좌표로 결정한다. 배치 후 건물/부지 그림의 투영 사각형과 길·논리 강 셀을 제외한다. 후보가 가려져도 다른 후보를 다시 채우지 않는다. 외곽은 world 위치에 고정된 어두운 숲과 fog를 깐다. 기존 마당/길/일꾼을 유지한다.
- `EstateZoneDensity.json` 수치는 **[가설]**이다. 화면당 군집4–8/소품10–18, 빈 땅≤40%, 연속빈땅≤20% 달성은 아직 측정하지 않았다. 모든 구역/배율별 캡처로 판단해야 하며 정적 개수로 대신하지 않는다.
- atlas 생성 0.83초. 2048×2436, 추가 예상메모리 Mac RGBA32 **19,955,712bytes(19.03MiB)**, iOS/Android ASTC4 각각 **4,988,928bytes(4.76MiB)**. 디바이스 측정 아님. 그리기 호출 증가분은 미측정이며 같은 atlas가 기존 건물과 Y 순서로 교차할 때 추가 묶음이 생길 수 있다.
- 변경 없음: Core, `data/`(역사 r3~r7 포함), replay/save codec. 실행 해시·저장 바이트 재검증은 낮 순서에 남는다.

정적 검사: Node 후보/정체성18개 + runtime atlas3개 통과, Unity 경계 검사 통과, `git diff --check` 통과. 새 EditMode 검사(75/50 카메라, 역사 설정 미적용, FNV, 공용 texture, pivot, 결정적 계획, 투영 예약)는 **작성만 / NOT_RUN**.

07:00 이후 순서: 가신 UI 다음 #496 잠금 획득 → 두 프로필 → Astra r2 동일 장면75/50 및 절반/전체복원 캡처 → 밀도/빈땅/연속빈땅/그리기호출 실제측정 → Core/replay/save 회귀. 강 chunk가 열린 경계를 걸칠 때 중심으로 밝음/어둠을 고르고 기존 어둠마스크를 덧씌우므로, 상태 경계의 색 이음새도 별도 확대 확인한다. 실패 두 번이면 중단한다. 초안 유지, 사용자 승인 전 병합하지 않는다.

후보예약은 건물/길에 가려진 후보도 유지해 이웃 후보를 재충원하지 않는다. 이 hide-only 계약 때문에 건물 밀집 화면에서 목표 군집/소품 수가 부족할 수 있으며 실제 캡처에서 측정해야 한다. 이를 명시한 EditMode 회귀검사는 작성했고 아직 실행하지 않았다.
