# M3a-7 낮 두 프로필 기록 (2026-10-26)

## Unity tests
commit: b4e9740ae0a544d52f5f06ef1c44ba3d4d5d8f8e
first-playable EditMode: passed 826, failed 0, skipped 25
first-playable PlayMode: passed 41, failed 0, skipped 42
wave-1a EditMode: passed 836, failed 0, skipped 15
wave-1a PlayMode: passed 70, failed 0, skipped 13
skipped in every profile: 2 unexplained, 1 listed

## 판정

- 네 suite assertion 실패 0. 하네스 종료1: 기존 공통 skip 2건은 예외를 추가하지 않고 그대로 보고한다. 전체 관문 통과로 부르지 않는다.
- 일반 캡처 0건, 청록 검출 0건. 의도적 청록 음성대조는 별도다. 셰이더 대기 후 캡처한 실제 프레임이며 최초 요청 프레임과 같다고 주장하지 않는다.
- M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes: 24개 실제 fixture sidecar가 없는 조건부 Ignore. MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures: 표준 두 프로필에 opt-in melee catalog가 없는 조건부 Ignore. 플랫폼 제한이 아니다.
- 로컬 기록: artifacts/unity-tests-b4e9740-day. 공유 Mac 잠금 하에 실행, PR #386 초안 유지.
