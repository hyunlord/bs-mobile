# First-playable approved enemy walks (#429)

2026-10-27 · v2 · 관문: 부분 — 정적 계약·Unity 두 프로필+보충 검사·frozen 리플레이 검증 통과, 실제 비교 영상 대기. 게임 규칙 변경 없음.

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

`FirstPlayableApprovedWalkTests`는 실제 Resource registry와 catalog binding으로 3종16칸·시간·pivot·worldSize·idle/hit/death 별도atlas를 검사한다. 세 테스트는 두 프로필 EditMode에서 통과했다. Core/data/save 규칙은 변경하지 않았으며, 아래 frozen replay 실행을 별도로 기록한다.

공통 atlas 1장 추가(18←17), raw RGBA 약12.38MiB. batching/프레임 성능 미측정. 추가 텍스처가 Y 정렬 중 묶음 수에 미치는 영향은 parent runtime 기록 대상. 승인 전 병합하지 않는다.

## Unity tests
commit: e0c2c06a0a11e81096c3db6000fd600fa45184ae
first-playable EditMode: passed 860, failed 0, skipped 25
first-playable PlayMode: passed 53, failed 0, skipped 45
wave-1a EditMode: passed 870, failed 0, skipped 15
wave-1a PlayMode: passed 85, failed 0, skipped 13

추가 EditMode 실행(같은 tested commit): combat 1/1, melee 1/1, 각각 failed0/skipped0. 원래 네 XML만의 skip 집계는 unexplained2/listed1로 stock runner exit1이었다. 누락된 opt-in fixture를 정상 생성한 후 실제 두 테스트를 실행했고, 여섯 XML 합산 결과는 아래와 같다. 원래 suite 숫자를 더하거나 예외를 새로 만들지 않았다.

skipped in every profile: 0 unexplained, 1 listed

- combat: M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes (생성 sidecar 준비 후 통과)
- melee: Tests.EditMode.MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures (opt-in catalog/replay 준비 후 통과)
- 기존 listed skip: Tests.PlayMode.WaveWorldTests.RecordedWaveRangeExportsActualTickFrames — Optional recorded-input sequence export.
- 근거: artifacts/day-20261027/pr429-tests/{record.md,combat.xml,melee.xml,combined-skip-record.txt,combined-skips.txt}. 전체 runner 종료 및 자기 잠금 해제 2026-10-27 12:02:45 KST, exit0.

청록: 이 실행 시간대에 남은 실제 capture sidecar 188개 모두 cyanPixels=0. 로그의 CYAN_CAPTURE_FAILURE 2건은 각 프로필에서 guard-self-test-negative-control-True가 의도적으로 1픽셀을 주입하고 Assert.Throws로 검출을 확인한 자체 시험이다. 실제 화면 실패에서 제외하며 숨기지 않는다. 같은 self-test 경로는 덮어쓰기되므로 보존 sidecar는 True/False 각1개지만 로그 실패는 프로필당1개다.

리플레이: 기존 frozen 입력 artifacts/day-20261027/replay-proof/first-playable-30000.ssreplay를 interactive-replay로 재검증했다. 입력 SHA256 61aa591ea8f05dc1ec7b30968652df654aa423d824b786ede6145915fdc183ba, Seed30000/Tick27000/EndKind0, StateHash 69E7182A9FB4EBF154E85551F2A2C6696F8EFBD30A87EC3C1046DF593361CDB5. 기록된 checkpoint/end hash 검증 성공이며, 별도 옛 fixture의 44049CB5…와 입력이 다르므로 서로 비교하지 않는다. 무적 correctness fixture이며 정상 플레이 성능 증거가 아니다.

실제 전/후 비교 영상·적별 가시 프레임·묶음 수·CI 최종 결과는 대기다. 위 검증을 사용자 영상 승인으로 확대하지 않는다.
