# r8 구역 지형 #496 — 반복 실패 중단 (2026-10-30)

관문: 실패 — 보정 후 12/12 화면의 빈 땅 69.57–76.60%(목표 ≤40%), 연속 빈 땅 34.19–62.39%(목표 ≤20%). 같은 밀도 관문 두 번 실패하여 추가 구현·Unity 실행을 중단한다. PR은 초안, 사용자 승인 전 병합 금지.

검사 대상: `ad05935d0b32333c61759845602cc0ebf2dcea33`. 아래 과거 기록의 NOT_RUN은 당시 상태이며 이 절이 현재 판정이다. 원판정·실패 로그·PNG는 삭제하거나 덮어쓰지 않았다.

## 화면과 수치

선정 기준은 임의로 좋은 화면을 고르지 않고 사전 지정된 세 구역의 **전체 복원 / 75px** 한 장씩이다. Release 비교 PNG의 열 순서는 **승인 Astra → 최초 측정 → 보정 후**이며 실제 게임 화면은 확대·이동·재구성 없이 원래 390×844 그대로다. Astra는 기존 비교와 같은 (0,100,390,844) crop이다.

- `start-village-reference-baseline-corrective.png`
- `river-fallow-riverside-reference-baseline-corrective.png`
- `forest-hill-reference-baseline-corrective.png`

[12개 정본 측정 CSV](estate-zone-terrain-495-density.csv). 출처는 로컬 `artifacts/day-20261030/terrain-density-corrective-r2/comparison.csv`, 원판정은 같은 폴더 `VERDICT.md`다.

| 항목 | 최초 측정 | 보정 후 | 판정 |
|---|---:|---:|---|
| 빈 마른 땅 비율 | 73.48–82.61% | 69.57–76.60% | 12/12 실패, 목표 ≤40% |
| 최대 4방향 연결 빈 땅 | 38.65–68.68% | 34.19–62.39% | 12/12 실패, 목표 ≤20% |
| 동일 장면 빈 땅 개선 | — | 2.76–7.19%p | 개선은 통과가 아님 |
| 전체 마을 75px 빈 땅 / 연속 빈 땅 | 76.38 / 39.88% | 71.70 / 37.35% | 실패 |
| 전체 마을 75px 관측 개수 | — | 전체 prop Graphics 64, zone-cluster 17, zone-prop 1, primary-building 6 | 서로 다른 분류, 합쳐 목표와 비교하지 않음 |
| 실제 그리기 호출 / 전후 변화 | NOT_MEASURED | NOT_MEASURED | Graphic 개수는 draw call이 아님 |
| 추가 atlas 예상 메모리 Mac / iOS·Android 각 | 19.03 / 4.76 MiB | 19.03 / 4.76 MiB | RGBA32 / ASTC4, 실기 측정 아님 |

측정은 실제 alpha와 복구한 reference water mask를 사용한 dry-land ROI다. 모든 12개 결과는 MEASURED이며 UNKNOWN을 0으로 치환하지 않았다. UI inset, 카메라 보정, 완성 건물 상태가 Astra와 다르고, 절반 강 구역 중심은 clamp 때문에 (41.5,34), 전체/reference는 (45,34)다. 물 가장자리 CG.high 대 bilinear 차이는 남는 측정 한계다.

## 확인된 원인과 다음 제안 — 구현하지 않음

- 이전 코드는 중앙 preserve-existing 구역의 기존 filler를 제거하고 큰 forest 후보의 투명 캔버스까지 예약했다. 보정은 중앙 장식을 보존하고 낮은 fallow 군집을 추가했으며, 전체 군집 후보 간격을 4→2.5셀, 확률을 0.7→1로 바꾸고 실제 alpha bounds로 예약했다. 그림 배율·원본 crop은 유지했다.
- 그래도 독립된 작은 나무·폐허 군집을 뿌리는 방식은 승인 그림의 이어진 숲·바위·정원·울타리 마당 면적을 만들지 못했다. 마을 상단과 좌하단에 넓은 풀 공백이 남는다. 후보 수 증가는 실제 점유 면적을 충분히 늘리지 못했으며 군집 4–8 / 소품 10–18은 가설로 유지한다.
- 건물·부지·마당·길·강의 제외 영역과 hide-only 예약은 유지된다. 움직인 건물 때문에 이웃 후보가 새로 채워지지 않는다. 이 안전 계약을 풀거나 그림 크기를 키워 밀도 숫자만 맞추지 않았다.
- 제안: Astra 승인 구역 구성에서 이어진 낮은 지면/정원/울타리 군집을 구역 단위 묶음으로 재사용하는 구조를 먼저 설계·검토한다. 현재 점 소품 후보 증가와 구별하고, 문·부지·길 제외 영역과 actual alpha coverage를 함께 제시한 뒤 결정한다. 목표값 변경이나 추가 자산 제작은 이번에 하지 않는다.
- 강의 이전 수직 이음새는 transparent atlas gutter와 맞아 1px edge extrusion으로 보정했다. 128개 crop RGBA는 그대로다. 보정 캡처 1배에서 직선 이음새는 보이지 않지만 독립 사용자 승인은 아니다. canvas 중심 대신 실제 alpha가 닿는 구역 ID로 강 상태를 선택했다. 상단 강을 가리는 나무는 후속 배치 검토 항목이며 추가 수정하지 않았다.

## 검증 범위와 남은 관문

| 소스 | 실행 | 결과 / 한계 |
|---|---|---|
| `892929dc` 과거 전체 | first-playable EditMode / PlayMode | 925 pass, 25 skip, 0 fail / 55 pass, 51 skip, 0 fail |
| `892929dc` 과거 전체 | wave-1a EditMode / PlayMode | 935 pass, 15 skip, 0 fail / 93 pass, 13 skip, 0 fail |
| `ad05935d` 현재 | filtered capture | 2/2 pass, 밀도 자체는 위와 같이 실패 |
| 현재 소스 전체 두 프로필 | 전체 재실행 | NOT_RUN: 이전 전체 기록을 최종 소스 검증으로 재사용하지 않음 |
| 현재 replay / save hash | 최종 실행 재검증 | NOT_RUN: Core·data·codec 변경 없음은 코드 범위 사실일 뿐 최종 실행 해시 증거 아님 |

과거 skip에는 프로필 조건부 건너뜀과 두 입력 부재가 포함된다: `M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes`의 showcase fixture sidecar, opt-in melee aim catalog export. 이를 0 skip이라고 쓰거나 새 예외로 숨기지 않는다. 최초 실패·캡처 exporter 보정과 이번 밀도 실패 기록 모두 보존한다.

재생성: 로컬 원본 PNG와 `.density.json`에 `node tools/measure-estate-render-density.mjs <capture>.density.json --reference-water-mask artifacts/estate-zone-terrain/reference/dense-river-r2-water-occupancy-1x.png --water-origin=-256,448 --water-size=1152,1152 --grid-cell=64,32 --zone-config data/opt-in/account-estate/m3a-prototype-r8.json`. 정본 CSV의 카메라 문자열과 commit을 함께 확인한다. 이번 중단 보고는 문서만 변경한다.

---

# 역사 기록 — 아래 문장들은 각 작성 당시 범위와 결과

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
