# Approved people walks (#436)

관문: 부분 — 두 프로필의 EditMode/PlayMode 실패 0, 실제 캡처 감시 청록 0. 실제 전후 영상·선택 프레임 관찰 및 사용자 화면 판정은 아직 완료하지 않았다.

작성 2026-10-27. 테스트 대상 `9a43e70341d20ab21970f8948d5d51c0275bcf2c`; 원판 비교 기준 `7e5ca6d0ca2010389ac45bea012e529907f3fd03`. 이 기록은 게임 적용 승인이나 PR 병합 승인이 아니다. CI 최종 결과는 별도 확인 후 기록한다.

## 범위와 원본

- 사용자 승인 원본: [m3-people-walk-review-433-20261027](https://github.com/hyunlord/bs-mobile/releases/tag/m3-people-walk-review-433-20261027)의 가신·주민·귀환 주민 r1, 각 16칸. 당시 manifest의 pending 문자열은 보존하고 이후 사용자 승인을 근거로 삼는다.
- 변경 역할: 두 프로필의 `person.vassal.move`, `person.peasant.move`, `person.returning.move`. Core·저장·게임플레이 데이터는 변경하지 않는다. 기존 활동별 서기·작업 그림은 보존한다.
- 공통 actors atlas를 1254×1254에서 2048×2048로 확장한다. 기존 1,572,516픽셀과 859개 비대상 역할 기록은 보존한다. 새 48칸은 원본을 재표본화하지 않은 union crop이며 2px 투명 여백을 둔다. legacy SpriteImporter 54개 rect의 bottom-origin y만 794 이동한다.
- 소스 이미지 SHA-256, 칸별 RGBA SHA-256, crop·pivot·worldSize 및 생성 명령은 `tools/art-authoring/people-walk/packed-frames.json`과 같은 폴더 README에 기록했다. 그림의 승인 원판과 게임용 crop을 구분한다.

| 역할 | union crop | 원본 한 주기 보폭(px, 계산값) | 한 주기 Unity 거리(계산값) |
|---|---:|---:|---:|
|가신|159×205|59.9856971161|0.1178872638|
|주민|186×204|61.0607158179|0.1233332830|
|귀환 주민|187×224|61.0607158179|0.1233332830|

귀환 주민의 짐 때문에 그림 높이가 달라도 주민과 다리의 픽셀 배율은 같다. 보폭은 승인 `PeopleGait.cs`와 rig-source alpha를 Node double로 재계산한 값이며 실제 Unity float 측정값으로 부르지 않는다. `frameMs=40`은 원본 저작 메타데이터다. 게임은 기존 거리 기반 재생과 2.5cycles/s 상한을 유지하므로 모든 속도에서 40ms나 미끄러짐 0을 약속하지 않는다. `cyclePixels` 없는 기존 병사와 합성 단일 프레임 fallback 검사를 유지한다.

텍스처 수 추가는 0이다. 무압축 RGBA 원본 할당 증가 추정은 **10,487,152바이트(약 10MiB)**이며 실제 GPU/native 메모리 증가를 측정한 값은 아니다. 이 추정만으로 성능 통과를 선언하지 않는다.

## 두 프로필 실행

공유 Mac 자기 잠금으로 2026-10-27 **15:52:58–16:05:50 KST** 실행했다. 종료 로그의 자기 잠금 해제와 추적 파일 변경 없음 확인. 원자료는 해당 작업트리 `artifacts/day-20261027/pr436-people-tests/`에 보존한다.

| 프로필 | 검사 | 통과 | 실패 | skip |
|---|---|---:|---:|---:|
|first-playable|EditMode|865|0|25|
|first-playable|PlayMode|54|0|45|
|wave-1a|EditMode|875|0|15|
|wave-1a|PlayMode|86|0|13|
|opt-in 실제 전투 sidecar|EditMode 추가 실행|1|0|0|
|opt-in melee aim 카탈로그|EditMode 추가 실행|1|0|0|

원래 `tools/unity-tests.sh` 실행은 두 환경 입력 부재 skip 때문에 **exit 1**이었다. 네 suite 자체의 실패는 0이다. 이 실패 기록을 지우거나 성공으로 덮지 않았다. 기존 생성 도구를 실행한 뒤 다음 두 검사를 실제로 실행하여 각각 통과했다.

- `M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes`: `showcase-fixtures`로 실제 sidecar 생성 후 실행.
- `Tests.EditMode.MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures`: `melee-aim-fixture`와 기존 opt-in catalog export 후 실행.

여섯 XML의 기존 skip 검사 재집계는 **0 unexplained / 1 listed**다. 남은 한 건은 기존 선택 실행 `Tests.PlayMode.WaveWorldTests.RecordedWaveRangeExportsActualTickFrames`이며 새 예외를 추가하지 않았다. 최초 네 XML의 `2 unexplained / 1 listed`도 `record.md`와 `skipped-everywhere.txt`에 보존한다.

## 청록 감시

`artifacts/playmode-capture-monitor/events.jsonl`에서 위 실행 시간에 해당하는 관찰 266건을 구분했다. 합성 guard self-test 4건을 제외하면 **실제 캡처 관찰 262건, 청록 픽셀 합계 0, 청록 발생 관찰 0**이다.

| 실행 도메인(UTC) | 실제 관찰 | 청록 픽셀 |
|---|---:|---:|
|2026-10-27T06:59:18.7415560Z (first-playable)|84|0|
|2026-10-27T07:03:12.8246160Z (wave-1a)|178|0|

합성 4건 중 의도적으로 청록 1픽셀을 만든 negative-control 2건은 감시가 실패를 검출하는 시험이다. 이를 실제 게임 청록 재발로 세거나 실제 0건에 섞지 않았다. 캡처 관찰 건수는 고유 이미지 수나 사용자 플레이 시간과 같지 않다.

## 리플레이·실제 선택 증거의 구분

두 프로필 seed 30000~30004의 .NET 기록을 Editor Mono에서 재검증한 기존 parity 테스트는 모두 통과했다. first-playable은 각 27000틱, wave-1a는 각 3000틱의 제한 입력 재생이다. 장시간 C05·실기·성능 측정의 대체 증거가 아니다.

| seed30000 | 종료 틱 | 상태 해시 |
|---|---:|---|
|first-playable|27000|`44049CB58B1A2824FE5EA35D27814F29BDEBC3D9D5F3394FBEA4CD9F7FB33D1C`|
|wave-1a|3000|`866612B830A6313E9E1EB1EC883C59FE07B188A14CF4FC5F8FE6CA601E2AC2CF`|

두 카탈로그의 세 역할이 서로 다른 16칸·원본 배율·stride·정지/역방향 처리·귀환 주민 전환을 유지한다는 EditMode 검사는 통과했다. `ActualPeopleObservationDoesNotChangeCore`도 두 프로필에서 통과했지만 기본 20틱 검사는 observer의 실제 ID와 View 처리 전후 해시를 확인하는 계약이다. 세 역할의 실제 이동 중 16칸 전체가 화면에 보였다는 증거로 확대하지 않는다.

**wave-1a의 자연 출현 제한:** `core/src/SowSiege.Core/Simulation.cs`는 `WaveRuntime`이 있을 때 `estate.Initialize()`를 실행하지 않는다. 따라서 일반 wave 판의 estate `People`은 없고, wave 작업 집단의 사람은 별도 militia/guard 경로다. 세 역할의 카탈로그 준비와 자연 게임 등장 검증을 분리한다. 이 PR에서 임의 사람을 만들거나 Core 규칙을 바꿔 wave 등장 증거를 만들지 않는다.

실제 FP 전후 비교는 같은 기록 명령과 seed, 카메라, 틱으로 오프라인 60fps 렌더한 뒤 제출한다. `WorldRenderer`의 실제 `DrawActor` 뒤에서 선택 role/frame/위치를 관찰한다. 관찰자의 중심점 viewport 포함은 다른 물체에 가리지 않았다는 증명이 아니므로 영상 확인이 필요하다. 전체 종료 해시·checkpoint, 전후 매 프레임 해시·카메라·사람 위치 일치와 역할별 이동 중 선택 coverage를 따로 기록할 예정이다. 이 문서 작성 시 해당 영상과 최종 사용자 화면 판정은 **미완료**다.
