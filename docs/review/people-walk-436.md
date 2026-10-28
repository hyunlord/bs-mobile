# Approved people walks (#436)

관문: 부분 — 두 프로필의 EditMode/PlayMode 실패 0, 실제 캡처 감시 청록 0. 실제 전후 영상·16칸 선택 확인 완료. 사용자 화면 판정·병합은 대기한다.

작성 2026-10-27. 테스트 대상 `9a43e70341d20ab21970f8948d5d51c0275bcf2c`; 원판 비교 기준 `7e5ca6d0ca2010389ac45bea012e529907f3fd03`. 이 기록은 게임 적용 승인이나 PR 병합 승인이 아니다. 기록 커밋75a070c의 required CI [40465436750](https://github.com/hyunlord/bs-mobile/actions/runs/40465436750)는 quality·secrets 모두 최종 통과했다. 후속 문서 커밋 CI는 PR에 최종 결과를 별도로 남긴다.

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

## 실제 오프라인 전후 영상

[비교 Release](https://github.com/hyunlord/bs-mobile/releases/tag/people-walk-game-review-436-20261027). Time.captureFramerate=60, 디버그 명령이 포함된 기존 FP seed30000 입력을 그대로 사용했다. 정상 사람 플레이·성능 측정 증거가 아니다. 3쌍 모두 15 checkpoint와 전체 종료 해시가 위 FP 값과 같다. 각 프레임 명령 순번·Core 해시·카메라·실제 사람 위치가 전후 동일하다.

|장면|틱|프레임/길이|이동 중 viewport 관찰|평균 draw calls 전→후|p95 전→후|
|---|---|---|---|---|---|
|기본 카메라 초반|180–1079|1800 /30초|가신1268건,16칸 전부|12.209→12.209|14→14|
|기본 카메라 후반|23400–24299|1800 /30초|귀환 주민185건,16칸 전부|14.407→14.911|15→15|
|주민 검토 카메라|430–780|702 /11.7초|주민1400건,16칸 전부|12.195→12.195|14→14|

기본 카메라에서 이동 주민은 화면 밖이므로 별도 **REVIEW CAMERA**로 (6.74,6)을 바라보았다. 로컬 진단 before d6e0cf0 / after35a19ed의 Editor 전용 카메라 변경은 게임 PR에 포함하지 않는다. 기본 카메라 before c400899 / after9a43e70이며, 양쪽 모두 같은 캡처 관찰자를 쓴다. 영상 인코딩 결과는 60/1fps, 각1800/702프레임으로 확인했다.

실제 PNG에서 가신·귀환 주민·주민을 확인했다. 주민 검토 frame100은 익은 곡물이 하체 일부를 가리며 같은 좌표 주민이 겹친다. 중심점 viewport 관찰은 완전 비가림이나 사용자 승인으로 확대하지 않는다. 후반 draw calls 평균 +0.503은 그대로 기록하며 텍스처 수 불변을 묶음 수 불변이라고 표현하지 않는다.

검증 스크립트와 원자료는 자기 작업트리 artifacts/day-20261027/people436/의 analyze-replays.mjs, replay-comparison.json, peasant-analysis.mjs, peasant-analysis.json 및 videos-r1·peasant-video-r1에 보존했다. 원본 성공 캡처 전체는 업로드하지 않고 선정 비교 영상3개와 표본 정지화면만 Release에 올린다.

## #439 메모리 축소와 승인 main 반영 · 2026-10-27

관문: 부분 — 실행 대상 `9fbad22b648def4a022c628e756735bc36dfa94d`의 두 프로필 및 추가 입력 검사 실패 0. 아래가 최신 검증이며, 위 2048-square 배치·이전 실행 결과는 역사 기록으로 보존한다. CI 최종 결과와 사용자 판정은 PR에서 별도로 기록한다.

#411 squash `a44bf7ae6a5811de057c8f12410ce4a5d8fb0d3f`를 일반 merge하고 그 커밋의 actors를 재생성 기준으로 삼았다. 원본 PNG SHA256은 `060b8316485f3d8e36b1bdee0a9d10e1040a6ac7cc35e49e863b67a9b52bae42`다. 새싹 문장이 들어간 1254×1254 전체 RGBA, 859개 비대상 역할, legacy importer 54개 crop을 보존한다. 승인된 새 48칸은 RGBA·union crop·pivot·worldSize·frameMs·cyclePixels 및 layout이 이전 후보와 동일하다. 바뀐 것은 빈 atlas 면적과 bottom-origin y 보정(794→686)이다.

| 메모리 계산 항목 | 이전 후보 | 현재 후보 |
|---|---:|---:|
| atlas 크기 | 2048×2048 | 1920×1940 |
| 무압축 RGBA 할당 | 16,777,216 bytes | 14,899,200 bytes |
| 1254-square 원본 대비 증가 | 10,487,152 bytes (10.00 MiB) | 8,609,136 bytes (8.21 MiB) |
| 이전 후보 대비 절감 | — | 1,878,016 bytes (1.79 MiB) |
| 텍스처 수 증가 | 0 | 0 |

이는 mipmap 없는 소스 할당 계산이며 실제 GPU/native 메모리 측정값이 아니다. `ArtPreparation`의 Uncompressed·NPOT=None 계약은 유지한다. `pack.mjs` / `check.mjs`, sprout replacement 단위 검사 2개, diff 검사가 통과했다. 기존 #411 `installed.json`은 당시 전체 파일 기록으로 보존하며 이번 문장 보존은 기준 atlas 영역 전체 RGBA 동일성으로 검사한다.

### 같은 커밋의 실제 Unity 결과

원자료: 자기 작업트리 `artifacts/day-20261027/pr439-memory-main-tests/`. `record.md`의 source commit과 여섯 XML을 직접 대조했다.

| 프로필 | 검사 | 통과 | 실패 | skip |
|---|---|---:|---:|---:|
|first-playable|EditMode|865|0|25|
|first-playable|PlayMode|56|0|45|
|wave-1a|EditMode|875|0|15|
|wave-1a|PlayMode|88|0|13|
|opt-in frozen combat sidecar|추가 EditMode|1|0|0|
|opt-in melee aim catalog|추가 EditMode|1|0|0|

기본 네 suite 기록의 `2 unexplained / 1 listed`를 그대로 남겼다. 기존 생성 도구로 입력을 준비한 추가 두 XML을 합친 결과는 **0 unexplained / 1 listed**다. 남은 listed는 기존 optional recorded-input sequence export 한 건이며 예외를 추가하지 않았다.

두 fixture 생성 로그는 이전 `pr436-people-tests`의 같은 파일과 **바이트 동일**하다. FP seed30000–30004의 27000틱 기록 및 wave seed30000–30004의 3000틱 기록을 각각 Mono parity 테스트가 다시 통과했다. seed30000 종료 해시는 위 표의 FP `44049CB5…7FB33D1C`, wave `866612B8…E2AC2CF`와 같다. C05나 실기 성능을 증명하지 않는다.

### 남은 성능·화면 판정

- 최종 제출 SHA를 명시해 **Claude Code 야간 대기열에 wave-1a C05를 요청**한다. 이 변경 작업에서는 직접 성능 측정을 하지 않는다.
- first-playable 성능은 **FP01 / #443 장면 계약과 main 기준선 3회**가 먼저 필요하다. C05는 wave-1a 전용이므로 first-playable 성능 통과로 확대하지 않는다.
- 승인 원본 Release `m3-people-walk-review-433-20261027`와 기존 비교 Release `people-walk-game-review-436-20261027`는 수정하지 않는다. RGBA 보존으로 그림 승인·외형 비교를 재사용하되, 과거 영상의 draw calls나 실행 시각을 이번 커밋 측정값이라고 부르지 않는다.
- 이번 실행의 청록 관찰 집계·실측 메모리·CI 최종 결과는 이 문서에서 새 수치를 주장하지 않는다. 담당자의 원자료 확인 후 PR에 기록한다.

## 최신 main 통합 후 재검증 · 2026-10-28

관문: 부분 — 실제 실행 source `92323f70bbef5b35b75221fad955dff83b11af42`에서 두 프로필과 supplemental 모두 실패 0. #446 FP01 코드를 포함한 main `2c7987c`를 merge한 뒤의 새 기록이다. 전날 이 source의 CI quality 실패는 이전 Unity 기록을 사용한 정책 실패로 보존하며, 새 기록 커밋의 최종 CI는 PR에 기록한다.

공유 Mac 자기 잠금 **09:00:16–09:09:11 KST**, batchmode 순차 실행. 종료 시 first-playable 복구, 추적 파일 변경 없음, 자기 잠금 해제를 확인했다. 원자료: `artifacts/day-20261028/pr439-current-main-tests/`; 실행 로그: `artifacts/day-20261028/people439/run-profiles.log`.

| 프로필 | 검사 | 통과 | 실패 | skip |
|---|---|---:|---:|---:|
|first-playable|EditMode|873|0|25|
|first-playable|PlayMode|56|0|45|
|wave-1a|EditMode|883|0|15|
|wave-1a|PlayMode|88|0|13|
|frozen combat sidecar|추가 EditMode|1|0|0|
|opt-in melee aim catalog|추가 EditMode|1|0|0|

기본 runner exit1과 `2 unexplained / 1 listed`는 보존했다. 기존 생성 도구로 두 입력을 준비해 해당 테스트를 실제 실행한 여섯 XML 합산은 **0 unexplained / 1 listed**다. 예외를 추가하지 않았다. 양쪽 프로필 fixture 생성 로그는 전날 `pr439-memory-main-tests`와 바이트 동일하다.

이번 시간대 capture monitor 266건 중 합성 guard self-test 4건을 제외한 실제 관찰 **262건의 cyanPixels 합계 0**이다. 합성 negative control 두 건의 각 1픽셀은 의도된 검출 시험이며 실제 청록 재발이 아니다.

그림·atlas·실행 코드는 이번 기록 작업에서 바꾸지 않았다. 메모리 추정은 +8.21MiB, 이전 후보 대비 −1.79MiB 그대로다. 새 제출 SHA로 Claude Code 대기열의 C05·FP01 비교를 요청하되 여기서는 성능 측정을 실행하지 않는다. FP01 main 기준선 3회가 선행해야 하며, 두 프로필 기능 검사를 성능 판정으로 확대하지 않는다. PR은 초안·사용자 병합 승인 대기를 유지한다.
