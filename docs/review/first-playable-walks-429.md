# First-playable approved enemy walks (#429)

2026-10-27 · v1 · 관문: 부분 — 정적 계약 통과, Unity/영상/리플레이 재생 미실행. 게임 규칙 변경 없음.

| Content / role suffix | 승인 Release | frames × frameMs | pivot | worldSize |
|---|---|---|---|---|
| core:stone_breaker / walk | m3-walk-next-review-423-20261027, r3 | 16 × 45 | .5, .12109375 | .559006211093322 |
| core:rope_catcher / walk | m3-rope-ember-walk-review-425-20261027, r1 | 16 × 40 | .5, .12109375 | .5031055899839898 |
| core:ember_sapper / walk | same, r1 | 16 × 40 | .4609375, .12109375 | .5217391303537672 |

사용자가 승인한 기존 프로젝트 그림을 Unity SpriteSkin으로 구운 Release다. 기존 generated-actors 출처/상업 이용 계약을 유지하며 CC0로 재분류하지 않는다. 후보 manifest의 과거 pending 문구보다 사용자 승인 지시가 우선한다. Ember 심지 깜박임·공격 모션은 범위 밖이다.

## Packing and verification

- 원본 PNG는 ignored `artifacts/429/{stone,rope-ember}`에 보존. 재현 다운로드 경로·원본 byte SHA256·RGBA SHA256은 `tools/art-authoring/enemy-rigs/first-playable-approved-walks.json`에 기록.
- `node tools/art-authoring/enemy-rigs/import-first-playable-walks.mjs`로 생성. 기존 PNG decode/encode 함수를 재사용한다. 4096×256 원본마다 256×256 셀 16개를 그대로 복사하여 2080×1560 공통 atlas에 넣는다. 2px 투명 gutter, 축소/재색칠/inset 없음. Stone ATLAS-CONTRACT의 낡은 inset4 계약은 사용하지 않는다.
- `--check`: 원본 다운로드 없이 committed atlas에서 원본 RGBA를 역조립하여 3종 SHA 일치, 48칸 전체 RGBA, gutter alpha0, 크기/피벗/frameMs/role 계약 확인. 통과.
- `node tools/first-playable-art.mjs`: 18 atlases, 372 roles, 343 bindings, required329, registry18 통과.
- `node --test tools/first-playable-art.test.mjs`: 16/16 통과. Unity boundaries assemblies9 통과.
- 기본 a15abfc 대비 역할 변경은 위 walk3개뿐, binding/license 그대로. wave1a manifest 그대로, fallow-enemy-rigs LFS 원본 SHA256 `5a69aaf13a67ea6885234be74e4d984e00b7b77323f113024b15e05ebebf8bde` 일치. 처음 git show byte 비교는 LFS pointer와 실파일을 비교해 실패했고 oid 비교로 바로잡았다.

## Actual runtime path and pending gates

`ArtCatalog.Load` → first-playable이면 `Resources/FirstPlayableArt` → `ArtCatalog.Resolve("enemy", DefinitionId, state)`. `WorldRenderer`는 위치가 바뀌고 hit/death가 아니면 walk를 선택한다. first-playable에서는 wave가 null이므로 `EnemyRigAtlas.TrySample`를 거치지 않는다. `Draw`가 `floor(time*1000/frameMs)%16`으로 UV를 선택한다. 기존 idle/hit/death 역할과 wave 걷기는 변경하지 않는다. 이동거리 기반 stride를 새로 발명하지 않는다.

first-playable은 ActorMotion.SampleEnemy에 Pursuer 종류를 넘기며 이 분기는 offset=0/scale=1/rotation=0이다. 전체 sprite 흔들림을 추가하지 않고 좌우 facing만 유지한다. 승인 그림의 상태 전환 크기(특히 stone의 역사 idle 대비 약6px 기준점 차이)와 1배 before/after 영상은 Unity에서 확인해야 한다. 셀 몸 크기와 투명 여백을 자르지 않았으므로 승인 worldSize를 그대로 사용한다.

`FirstPlayableApprovedWalkTests`는 실제 Resource registry와 catalog binding으로 3종16칸·시간·pivot·worldSize·idle/hit/death 별도atlas를 검사한다. 소스만 추가했고 Unity에서 실행하지 않았다. Core/data/save/runtime .cs 변경 없음은 소스 경계 증거이며 실제 리플레이 해시 재실행을 대신하지 않는다.

공통 atlas 1장 추가(18←17), raw RGBA 약12.38MiB. batching/프레임 성능 미측정. 추가 텍스처가 Y 정렬 중 묶음 수에 미치는 영향은 parent runtime 기록 대상. 승인 전 병합하지 않는다.
