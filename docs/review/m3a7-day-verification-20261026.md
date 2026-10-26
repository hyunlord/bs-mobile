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
- 일반 캡처 251건, 청록 검출 0건. 의도적 청록 음성대조는 별도다. 셰이더 대기 후 캡처한 실제 프레임이며 최초 요청 프레임과 같다고 주장하지 않는다.
- M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes: 24개 실제 fixture sidecar가 없는 조건부 Ignore. MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures: 표준 두 프로필에 opt-in melee catalog가 없는 조건부 Ignore. 플랫폼 제한이 아니다.
- 로컬 기록: artifacts/unity-tests-b4e9740-day. 공유 Mac 잠금 하에 실행, PR #386 초안 유지.

집계 정정: 성공 캡처는 Editor.log가 아니라 `artifacts/playmode-capture-monitor/events.jsonl`에만 기록된다. 이번 실행 UTC 2026-10-26T01 시간대 행을 집계했다.

## 승인 후 main 동기화 (16:26)

사용자 승인 HEAD3485e95를 정상 squash하려면 최신 main이 필요해 강제 push 없이 merge했다. 기능 수정 없이 main의 #382 변경을 포함한다. 기존 공통 skip2건은 그대로 보고하며 예외를 추가하지 않는다.

## Unity tests
commit: 99419b6d533b3beb77e68ade72d5123e229a85c7
first-playable EditMode: passed 826, failed 0, skipped 25
first-playable PlayMode: passed 50, failed 0, skipped 42
wave-1a EditMode: passed 836, failed 0, skipped 15
wave-1a PlayMode: passed 79, failed 0, skipped 13
skipped in every profile: 2 unexplained, 1 listed
