# 가신 전투 구현 계약 v1

- 날짜: 2026-10-29
- 상태: 구현 검증 중, PR #528 초안. 사용자 병합 승인 대기.
- 정본 설계: [GDD 13 승인 r2](https://github.com/hyunlord/bs-mobile/blob/25b49d0293b8f4b26755ec0e6db8f8d9836b20fb/docs/design/gdd/13-vassal-combat-proposal.md).
- 변경 요약: 승인된 6개 능력의 wave opt-in, 출정 snapshot 고정, format8·정수 나머지 복구를 구현한다. wire 계약과 근거는 [ADR0075](../adr/0075-vassal-run-snapshot-and-replay-format-eight.md), 실행 결과는 [검증 기록](../review/vassal-run-527.md).

## 사용자 결정 반영

FP01은 역사 회귀만 검사하며 Growth·Allies·완료 XP를 추가하지 않는다. 효과 비교는 C05 고정 명령 파생 실행과 정상 wave 입력을 구분한다. C05의 무적·고정 종료는 정상 생존 근거가 아니다.

이동은 기존 입력 정규화·아이템·Feel 나머지 처리를 완료한 축별 이동 크기에 가신 배율을 마지막으로 한 번 내림 곱한다. 음수 방향은 크기를 내림한 후 부호를 복원한다. 새 이동 나머지는 만들지 않는다. 작은 정수 이동량의 증가가 사라지는 것은 이 보수안의 알려진 한계다.

## 화면과 적용 범위

개발 빌드 또는 Editor의 wave 설정에서 명시적으로 켠 출정만 가신 snapshot을 만든다. 기본값은 꺼짐이고 역사 first-playable·벤치마크에 자동으로 추가하지 않는다. 화면의 편성 수치는 다음 출정의 raw → capped / cap이며, 이미 진행 중인 판은 시작 snapshot을 유지한다. 적용이 꺼진 상태의 기존 미적용 안내는 유지한다.

성능·최종 사용자 화면 판정과 병합은 이 문서의 구현 기록으로 대신하지 않는다. 결제·광고 SDK, 판매/출처 원장, 추가 영웅은 범위 밖이다.
