# C05 회귀를 공개한 검토판 전달 사유

관련: [#209](https://github.com/hyunlord/bs-mobile/issues/209), [#210](https://github.com/hyunlord/bs-mobile/issues/210). 이 문서는 AGENTS.md의 '20% 초과 회귀는 실패 또는 PR의 명시적 사유' 중 후자이며, 사용자 관문을 바꾸거나 기준을 재설정하지 않는다.

C05 무상한3회에서 측정 커밋2e00d7d는 기준eed0139 대비400–599/600–799/800+ 밀도별 통합 p95가34.26%/43.95%/37.47% 증가했다. 원래 사유 없는 회귀 명령은 exit1로 실패했다. 정본 [CSV](../metrics/grounded-motion-c05.csv)와 기존 기준을 보존한다.

두 비교 버전에는 #197·#200·#209·#210의 화면·움직임 변경이 누적돼 있다. 이것은 변경 범위의 설명이지 증가 원인의 입증이 아니다. 개별 기여나 장치 상태의 영향을 분리 측정하지 않았다. 승인된 표현을 되돌리거나 스폰량·체력·해상도를 줄이지 않고 이번 검토용 앱·영상을 전달하는 결정의 사유를 명시한다. 결과 이름은 `explained-regression`이며 `pass`나 개선으로 바꾸지 않는다. 약한 빌드411마리의 별도16.249ms 목표 충족은 C05 회귀를 상쇄하지 않는다.

무상한 첫 회400–599 구간의 최대830.8411ms도 보존한다. 재현·원인을 분류하지 않았으므로 환경 지연이나 끊김 통과로 판정하지 않는다. 이 자료는60fps capped 끊김 검사나 Fold7 성능 수락을 대신하지 않는다. #169 원인 조사를 다시 열지 않으며 사용자 부드러움 판정·#98·#126·1b 보류를 유지한다.

재생성: `node tools/check-wave-regression.mjs artifacts/grounded-motion/c05-final-uncapped /Users/rexxa/bs-mobile-stutter/artifacts/stutter/uncapped --reason docs/review/grounded-motion-c05-reason.md`. 원자료 디렉토리는 로컬 표본이며 CSV의 커밋·source/data/replay 해시·설정으로 식별한다. 모든 성공 실행 원자료 묶음이나 CSV에서 재생성할 그래프를 Release에 추가하지 않는다.
