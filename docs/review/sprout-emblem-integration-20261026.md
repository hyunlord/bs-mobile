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
