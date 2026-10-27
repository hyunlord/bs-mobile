관문: 부분 — cdea663 두 프로필 실패0·보충 검사2/2·미설명 skip0·실제 캡처 청록0. 실제 전후 화면을 게시했으며 파손 목수집 문장은 이펙트 가림 한계가 있다. 초안·사용자 시각 판정 대기.

관문: 부분 — 승인 문장15종 통합·픽셀·해시는 통과, wave-1a PlayMode 청록 감시1건 실패로 중단.
변경: 기존8아틀라스의15crop 및 방어 방패의 대체 문장만 교체. measured_volley·manifest·Core·data는 보존.
결정: 초안 유지. 추가 Unity·native 캡처 실행 없이 실패 관측을 보고한다.

# 승인 그림과 계약

추적 #409. 원본 Release는 sprout-emblem-replacement-402-20261026, sprout-emblem-batch2-405-20261026, sprout-emblem-batch3-405-20261026.
`tools/art-authoring/sprout-emblems/approved.json`은15PNG의SHA·role·crop·기준점을 고정한다. 15/15 RGBA 동일,8아틀라스의 crop밖 변경0. 두 manifest 바이트 동일. measured_volley RGBA SHA256 `0f0cc3069a2d7a4324754f2925187dea9090088e870e3a5e5fdaaba150b7111c` 유지.

텍스처 수·크기·role·피벗·재생 시간은 그대로이며 새 그리기 층이나 인스턴스가 없다. 묶음 변화 없음은 구조적 판정이고 런타임 계측은 아직 없다. C05는 요청대로 실행하지 않았다.

# 실행 검증

코드/Unity 검증 커밋 `11c891dd236be82f06486ac3d15e6dfb07ea9e14`.

| 프로필 | EditMode 통과/실패/skip | PlayMode 통과/실패/skip |
|---|---|---|
| first-playable |828/0/25|52/0/42|
| wave-1a |838/0/15|80/1/13|

공통 미설명 skip2건은 기존과 같은 M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes(선택 sidecar 부재), MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures(opt-in catalog 부재). 예외를 추가하지 않았다. runner exit1이며 전체 Unity 통과로 표현하지 않는다.

crowd411 .NET 재생은 tick11530, 종료해시 `59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619`로 기존과 동일. 입력SHA `4fe0f97f23d20b487730be03ae76f5d7551ec88b787d43566f0cde41cd5a079c`, 기록data `51DA41A20DF37E4960AFB157AC7AA83D20C9AB1FC7E6B5E6D85643BC9C438B74`, 현재data `6AC1ACFC4B2E80DD510EE0A41D0039BC05932A5FEF1776CC1B2A4BF2E928C36D`. ADR0058 역사 provenance 정책 유지, 입력 수정 없음.

# 청록 감시 실패와 중단

2026-10-26 18:30:52 KST, EstateMapFlowTests.ASavedEstateBuildsClaimsAndSpecializesOnTheMap의 estate-flow-build:
- cyanPixels71959, frame7655, domainLoadedUtc09:30:44.0572330Z, captureOrdinal2.
- 대기 시작 anythingCompiling=True; 0.039초 뒤 false 확인. 캡처 직전/직후는 모두 false.
- precedingDomainReloadObserved=false.
- 실제 실패 PNG도 순청록 지면을 보여 준다. 원인은 미확정이며, 문장 교체가 원인이라고 추정하지 않는다.
- 앞선 사용자 지시의 anythingCompiling=false인데 청록이면 멈춤 조건에 따라 추가 수정·재실행하지 않는다. 감시 테스트·기준은 그대로 유지한다.

원자료: `artifacts/sprout-405/unity-two-profiles-t2FeFoxA/tests/`, `artifacts/portrait-234/editor-meta/estate-flow-build-390x844.png`와 `.cyan.json`.

이전4fd3931 실행은 캡처 fixture 프레임 경계 수정 전 리더가 중단했다. wrapper가 fixture를 먼저 복구해 잔여 EditMode child의 seed30002/30003/30004가 파일 부재로 실패했다. 이 중단 기록은 보존하며 passing evidence로 쓰지 않는다. 새 실행에서는 해당3건 모두 통과했다. 이후 중단 시 자식 Unity가 끝나기 전 fixture 경로를 옮기지 않는다.

# 아직 확인하지 않은 화면

실제 돌격선두 깃발·방어 궤도 방패·가신·목수집3상태·카드/진화 목록 전후는 미확인이다. 다른 작업의 worldsim_probe CPU가100%를 넘고 blocker 프로세스가 있어 공유Mac 녹화 조건을 충족하지 않았다. 다른 프로세스를 종료하지 않았다.

harvest-flag는 현재 게임 그리기 consumer가 없다. 승인 crop만 교체하고 명시적 역할 목록 fixture로 확인할 예정이며 게임 연결을 추가하지 않는다. 선택 캡처 SPROUT_EMBLEM_CAPTURE는 이번 두 프로필 실행에서0으로 유지했다. 누락 role 방패 fallback도 실제 캡처 미확인이다.

## 21:19 KST: test-session guard prerequisite stopped (#415)

Merged the isolated #415 guard without rewriting history (a5e7b83 includes 0543c60). Added optional actual CardPool screenshots for `core:heirloom_service` and `core:seed_crown` (af8203c), rather than labeling the icon-gallery fixture as a gameplay/UI list.

The prerequisite's full attempt had 236 actual cyan-free observations, but two wave-1a map interaction tests hit `Movement surface` instead of their target in graphical Editor. Graphical Editor also reserialized EditorSettings.asset. #415 preserves both failures and the invalid runner record. Under the repeated-failure rule, **no new #411 Unity run or native comparison was started**. The prior #411 failed Unity record is historical and does not validate this new head. Keep this PR draft; no merger authorization.

15 approved crop checks and both importer tests passed again; pixels outside approved cells and measured_volley remain unchanged. Core/data are unchanged. Missing actual before/after surfaces remain missing, not replaced by catalog fixtures. C05 remains unnecessary for pixel-only replacements unless actual batching changes are observed.

## 2026-10-27: batchmode 재검증

검증 HEAD `d0455d9c3e65d510e07a0464ab4a23c092575afa`, 10:30:16–10:43:37 KST. 자기 잠금 해제 완료. 추적 파일 변경0.

| 프로필 | EditMode 통과/실패/skip | PlayMode 통과/실패/skip |
|---|---|---|
| first-playable |828/0/25|55/0/42|
| wave-1a |838/0/15|84/0/13|

기존 도구로 frozen C05 combat sidecar와 m1-showcase-review melee catalog를 생성한 뒤 두 검사를 각각 실행: 1/0/0씩 통과. 네 전체 XML+두 추가 XML을 기존 skip 검사기로 합산하면 미설명0/기존 목록1. 예외 목록 변경0. stock runner의 exit1은 입력 없는 두 전체 실행 skip이며 추가 실행으로 실제 검사했다.

이번 실행 시각의 보존된 실제 캡처 sidecar154개에서 cyanPixels>0은0개. 감시 자체의 고의 청록 negative-control은 실제 캡처에서 제외한다.

앞선 같은 날 실행은 wave CardPool 캡처에서 first-playable 전용 heirloom_service/seed_crown을 찾다가 1건 실패했다. 실제 wave UI가 제공하는 crop_guard_signet으로 테스트 대상을 바로잡았으며 production catalog/Core/View는 수정하지 않았다. 실패 원자료 pr411-tests와 수정 후 pr411-retry-tests를 모두 보존한다.

현재 roles-15 전후와 house-states-vassal은 표시 상태를 직접 주는 fixture이며 실제 플레이로 부르지 않는다. card-pool-crop_guard_signet은 실제 wave 카드 목록 UI다. harvest-flag는 runtime consumer가 없어 역할 목록으로만 확인하며 새 연결은 하지 않는다. 실제 전투 전후 녹화는 입력 idle20분·외부CPU 조건 충족 후 별도 수행한다.

## 2026-10-27: 실제 입력 캡처 도우미 포함 최종 검증

검증 HEAD `6b383f644e8e078f5866822120adf8987f9757ad`, 10:57:32–11:10:09 KST. 두 프로필 및 추가 입력 검사 결과는 위와 동일: first-playable 828/55, wave-1a 838/84 통과, 실패0. combat/melee 추가 검사 각각1/0/0, 합산 미설명skip0/기존목록1. 예외 추가0, 추적파일 변경0, 자기 잠금 해제.

캡처 감시 events.jsonl의 이 실행 261건 중 guard 자체 시험4건을 제외한 실제 캡처 관측257건은 청록0. 실제 플레이 성공을 의미하는 수치가 아니라 해당 테스트 화면의 청록 감시 결과다.

선택 실행 전용 first-playable 캡처 도우미는 실제 기록 명령을 InteractiveSession에 적용하며 체크포인트/종료 해시를 확인한다. 각 프레임의 실제 적 ID와 viewport 안 중심 ID, 그리기 묶음을 sidecar에 기록한다. 기본 테스트에서는 비활성이다. 실제 전후 영상 실행은 외부 CPU와 사용자 입력 대기 조건 때문에 아직 미실행이다.

원자료: `artifacts/day-20261027/pr411-final-tests`, `pr411-final-run.log`.

## 2026-10-27 실제 결산 UI 캡처 보조 경로와 재검증

실제 완료된 입력 리플레이의 보유 특허장·활성 진화를 `UiRunPanels.Summary`로 표시하고 실제 ScrollRect를 이동해 아이콘을 캡처하는 opt-in `FP_QA_SUMMARY=1` 경로를 추가했다. 임의 보유 목록·게임 규칙 변경은 없다. 캡처 크기390×844·획득ID·활성 진화·완료 상태·종료 해시 불변을 검사한다. 선택 경로 자체의 캡처 실행은 공유 Mac 유휴/부하 관문 때문에 아직 하지 않았다. 기본 프로필 실행 결과가 그 선택 경로의 실행 증거는 아니다.

초기 configure는 `PlayModeWindow`의 `UnityEditor` namespace 누락으로 실패했다. 한 번 수정한 `f755feba328a22efe7499ce9ba65737fa2801173`에서 11:41:04–11:51:27 소유 잠금 아래 두 프로필을 다시 실행했다. 최초 실패 `pr411-summary-tests`와 성공 `pr411-summary-retry-tests`를 모두 artifacts/day-20261027에 보존했다.

| 프로필 | EditMode 통과/실패/skip | PlayMode 통과/실패/skip |
|---|---:|---:|
| first-playable | 828 / 0 / 25 | 55 / 0 / 42 |
| wave-1a | 838 / 0 / 15 | 84 / 0 / 13 |

기존 도구로 생성한 combat sidecar와 opt-in melee 입력의 보충 실행은 각각1/0/0, 합산 공통 skip0 unexplained/1 listed. 새로운 skip 예외를 추가하지 않았다. 실행 뒤 추적 파일 변경0. 실제 입력 결산·combat 전후 비교 영상은 아직 대기이며 직접 만든 상태 화면으로 대신하지 않는다.

## 2026-10-27 14:00: 오프라인 캡처 후 정확한 HEAD 재검증

검증 소스 `cdea6630e158953739e97ab3045ea5b1d41ef610`, 13:49:46–14:00:12 KST. 두 프로필 batchmode 및 기존 도구로 생성한 combat/melee 보충 입력 검사 후 wrapper exit0, 자기 잠금 해제. 실행 뒤 추적 파일 변경0. 이 후속 기록 커밋은 문서만 바꾸며 새로운 Unity 실행으로 표현하지 않는다.

| 프로필 | EditMode 통과/실패/skip | PlayMode 통과/실패/skip |
|---|---:|---:|
| first-playable |828/0/25|55/0/42|
| wave-1a |838/0/15|84/0/13|

보충 combat/melee 각각1/0/0. 전체4 XML의 기존 skip2건으로 stock runner exit1을 보존하며, 보충2 XML을 합친 기존 검사기는 미설명0/기존목록1이다. 예외 목록 추가0. 실제 감시 events.jsonl261건 중 guard 자체 시험4건(의도적 cyan1픽셀2건 포함)을 분리한 실제 캡처257건은 cyanPixels0. 원자료 `artifacts/day-20261027/pr411-offline-tests`, `pr411-offline-tests.console.log`, `pr411-offline-guard-events.json`.

### 실제 화면 증거

[선택 전후 PNG와 provenance README Release](https://github.com/hyunlord/bs-mobile/releases/tag/sprout-actual-captures-411-20261027). 전체 raw/성공 중복 자료는 로컬 보존한다.

- native 돌격 깃발·방어 궤도 방패: before b7cc3e2 / after feb8ba0, 각600프레임. 동일 frame150/tick375/alpha0.5에서 보이는 실제 게임 렌더러. 모든 index/tick/alpha 및 원본 입력/종료 해시 동일. 명시적 isolated combat hypothesis fixture, 오프라인60fps 무음이며 정상 획득 진행·성능 증거가 아니다.
- 실제 first-playable 가신 방패와 완주 UiRunPanels.Summary의 heirloom_service/seed_crown: before51fb5aa / aftercdea663. 실제 입력을 실행하며 SetInvulnerable·GrantLevel debug 입력 포함을 표시했다. 각 입력 종료 해시 동일. 필터4회 통과, 청록 감시547+4+547+4=1102건0. 해당 필터에는 negative control 실행 없음.
- 목수집 ID11(core:carpenter_hammer,5500/6000) 폐허·파손·완성: 실제 기존 입력 접두부 뒤 ordinary Advance 이동을 추가한 **새 derived review 입력**, 원본 재생과 구별. 카메라/좌표로 hedge/windmill과 구별. 이전 stale-camera ruin 실패는 보존하고 증거로 쓰지 않았다. 폐허 tick4724·파손270·완성351 비교와 각 새 종료 해시는 Release CARPENTER-README에 있다. 파손 문장은 파란 효과가 덮어 세부 판독 제한을 남긴다.
- harvest-flag는 실제 runtime consumer 없음: 역할 목록만, 게임 연결은 추가하지 않는다. 누락 role fallback의 정상 게임 발동 화면은 별도 미확인이다.

사용자의 오프라인60fps 승인에 따라 CPU/입력 idle 관문을 적용하지 않았으며, Unity/native 상호배제와 자기 잠금은 유지했다. 실시간 성능/C05 근거로 전용하지 않는다. 기존 역사 실패·중단 기록은 위에 그대로 보존한다. PR은 초안 유지, 병합·사용자 시각 판정은 별도다.
