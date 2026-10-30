# SLG 화면 흐름 시안 r1

상태: 사용자 승인 — 2026-10-30. 추적 [#561](https://github.com/hyunlord/bs-mobile/issues/561). 승인된 병력·영웅·연구·월드 GDD15–18과 연맹 제안 [#562](https://github.com/hyunlord/bs-mobile/pull/562)을 390×844 오프라인 화면으로 표현한다. 게임 코드·데이터·SDK 변경 없음. 신규 수치는 [가설], 화면 숫자는 [예시]다. 사용자 승인에 따라 최신 main과 최종 CI를 확인한 뒤 이 기록 PR을 squash 병합한다.

## 범위와 읽는 순서

- 병영 훈련: 준비/출전/부상/예약을 포함한 총 인원, 병종·비용·시간·큐.
- 영웅: 모집 → 확률 공개 → 확인 → 결과. 보통 확률과 천장 직전은 별도 상태다.
- 연구: 경제·군사·영지의 세 노드씩, 선행 조건·탐사 증명·현재 효과/상한.
- 월드: 첫 화면 → 행군 편성·출발 → 대상 도착·직접 참전/자동 전투/회군 → 귀환 확정.
- 연맹: 미가입 목록 → 공개 가입 또는 신청 승인 → 도움·회원·채팅. 신청/권한·신고/차단 세부 상태는 연맹 GDD와 화면 계약을 함께 읽는다.
- 서버 결과 불명확: 기존 요청 결과 조회를 제공하고 새 지출을 잠근다.

이 화면들은 상태별 설계 표본이며 전체가 한 계정의 연속 저장 시뮬레이션은 아니다. 모집 흐름의 보석 300→100 사용→200, 연속 서사 미획득 12→13은 일관된 예시다. 천장 직전19회는 별도 분기다. 월드 배경은 승인된 영지 컨셉의 시각 참조이며 실제 서버 타일 배치나 게임 캡처가 아니다.

## 승인 UI 계승

승인된 양피지 패널·어두운 바·파란 주 버튼·광택 자원 아이콘을 재사용한다. 기존 원화/화면은 보존하며 새 인물 일러스트를 제작하지 않았다. 새 영웅은 GDD의 이름·등급으로 표시하고 기존 가신 초상을 영웅으로 재분류하지 않는다. 기본 본문16px·보조14px, 조작 영역48×48 이상을 기준으로 한다.

## 확률과 정산

상시 단일 풀의 보통 모집은 일반/농로 길잡이70%, 희귀/강가 수호자25%, 서사/폐허 탐구자5% [가설]다. c=19의 다음 모집은 각각0/0/100%다. 등급별·영웅별 실제 다음 확률, 현재 카운트, 천장까지 남은 횟수를 함께 표시한다. 기본 기사와 가신은 풀 밖이며 서사 중복도 카운터를 초기화한다. 미보유 영웅 확정을 뜻하지 않는다.

행군 시간 예시는 (64,64)→(68,61)의 통행 가능한4방향7간선·보너스0·간선당10초로01:10이다. 실제 경로/장애물 관측 증거가 아니다. 귀환 예시는 출전20=생존16+입원3+전사1, 목재40/철재20 지급이다. 세계 변화·탐사 증명은 전투 종료에서 확정되고 귀환에서 재지급하지 않는다. 출발의 병상 여유는 예약 보장이 아니다.

## 전달과 검증

최종 전달 링크와 정적 검토 결과는 완성 파일에 맞춰 기록한다. 렌더링·문서 검사는 게임 적용, 서버 동시성·모집 RNG, 실제 휴대폰 터치·판독·성능 검증을 대신하지 않는다. 이 항목들은 NOT_RUN이다.

## 최종 파일과 검토 결과

- [Release](https://github.com/hyunlord/bs-mobile/releases/tag/slg-ui-flow-561-20261030)
- [핵심 흐름 8장](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/slg-main-flow-r1-1560x1856.png)
- [보조 상태 9장](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/slg-state-board-r1-1560x2752.png)
- [보통 확률 실제 크기](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/slg-03-rates-normal-r1-390x844.png), [천장 상태 실제 크기](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/slg-04-rates-pity-r1-390x844.png)
- [흐름·예외 상태 명세](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/FLOW-r1.md), [디자인 계약](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/DESIGN-r1.md), [정적 QA](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/QA-r1.md)

최종17개 PNG 모두390×844다. 렌더러의 텍스트445개 누락 glyph·폭/높이 넘침0, 터치156개48×48미만·화면 밖·중첩0을 확인했다. 독립 계약 검토와 전17장 직접 시각 검토를 통과했다. 초기 행군 시간00:35 오류는01:10으로 수정했고 이전 후보는 보존했다. 마지막 확인 중 화면의 설명도 플레이어 문장으로 다듬었다. 이 판정은 사용자 최종 화면 승인을 대신하지 않는다.

재생성 원본은 작업 출력의 `export/renderer.swift`, `export/boards.swift`이며 기존 승인된 PNG를 읽어 AppKit으로 렌더한다. 게임 코드가 아니다. 새 그림을 생성하거나 atlas를 덮어쓰지 않았다. 모든 기존 출력과 실패 후보를 보존하고 신규 ZIP은 만들지 않았다. Release에는 실제 크기17장·비교판2장·FLOW/DESIGN/QA/layout 기록4개를 전달한다.

## r2 · 월드 크기 정합 (사용자 승인 — 2026-10-30)

[#566](https://github.com/hyunlord/bs-mobile/pull/566)은 [GDD18 §2.1](../design/gdd/18-slg-world-map-proposal.md#21-첫-출시-크기-정합-r2-가설)과 [ADR0077 §4](../adr/0077-authoritative-slg-server.md)에 맞춰 06 월드 화면의 **128×128 → 512×512 [가설]** 표기만 정정한다. r1 승인 기록과 모든 원본은 보존한다. 등록 계정 10,000개와 동시 접속 1,000명은 별도 측정 목표이며, 도시 표식 1타일 기준 총면적 점유 61.04% → 3.81%는 실제 배치·성능 검증이 아니다. 예시 좌표와 01:10 행군 시간은 유효하므로 유지한다.

- [월드 r2 · 390×844](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/slg-06-world-r2-390x844.png), [핵심 흐름 r2 비교판](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/slg-main-flow-r2-1560x1856.png)
- [FLOW r2](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/FLOW-r2.md), [정적 동일성 검증 r2](https://github.com/hyunlord/bs-mobile/releases/download/slg-ui-flow-561-20261030/verification-r2.json)

r2는 텍스트 사각형 밖 RGBA 변경0·나머지 7패널 변경0, 텍스트21개 넘침/누락0·터치11개 오류0을 확인했다. 화면 규격과 512×512 표기를 직접 시각 확인했다. 이는 문구·정지 시안 검증이며 게임 적용·실제 지도·서버 부하·실기 관문은 **NOT_RUN**이다.
