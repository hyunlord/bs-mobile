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

## 병합 리뷰 후속 (2026-10-26)

최신 main 문서까지 반영한 `0ea1437`의 quality/secrets는 통과했으나, 미해결 리뷰 두 건 때문에 정상 병합 정책이 차단했다. 관리자 우회는 하지 않았다.

- GDD 05를 v1.10으로 갱신하여 새 r6 8×8·현행 선택 창/건물/빛 표현과 역사 r5를 구분한다. r7은 #390의 별도 후속이다.
- 일반 `EstateTerrainImage`가 늘어난 그림의 원본 가로세로 비율로 지도 좌표를 복원하던 결함을 실제 RectTransform 비율로 수정한다. Core·설정·저장 변경 없음.
- 비대칭 피벗·높이 비율 .5/.8·배율 1/2의 실제 uGUI 메시를 검사하는 회귀 두 건: 수정 전 좌표 오차 2.12132/0.84853셀로 실패, 수정 후 2/2 통과. 최초 테스트 작성의 누락 namespace 컴파일 오류는 수정했고 해당 로그도 보존한다. 경계 검사 통과. 전체 두 프로필은 수정 커밋에서 다시 기록한다.
- 로컬 원자료: `artifacts/m3a7-review-threads-20261026/`.
