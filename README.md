# Sow & Siege (가제)

모바일 판타지 액션에서 도구가 공격하고 땅·건물·사람을 키우는 게임. 기본 영웅 1·영지 전통 1로 시작하되 규칙은 데이터로 확장한다.

**1단계 A 착수:** [의뢰서](docs/design/04_ASTRA_GOAL_bs-mobile_phase1a.md)에 따라 데이터 정리를 Unity 구현보다 먼저 진행한다. [승인된 무기 평가](docs/review/weapon-holdout-79.md)는 무기 하한·기존(b)·c′를 모두 통과했다. [U0 환경 확인](docs/review/phase1a-u0.md)과 [생산 데이터 진입점](docs/runbooks/production-data.md)을 먼저 읽는다. Unity 실기 검증과 사람의 재미 판정은 별도 관문이며 1단계 B는 시작하지 않는다.

Phase0의 [S5](docs/review/S5-report.md)·[기존 S4 실패](docs/review/S4-report.md)·R2/R3·진단 원자료는 당시 기록으로 보존한다. Projects 보드는 사용자 결정 #2에 따라 사용하지 않고 라벨·마일스톤·이슈/PR로 관리한다. 전체 판은 현재12분/30Hz이며 CI 스모크900틱을 한 판·모바일 성능 검증으로 부르지 않는다.

.NET 8 SDK, Node.js 22 이상(npm), Git, Bash를 준비한 깨끗한 클론의 루트에서:

```sh
./tools/check.sh
```

에이전트는 [AGENTS.md](AGENTS.md)부터 읽는다. 설계 원본은 [docs/design](docs/design), 결정 기록은 [docs/adr](docs/adr), 반복 절차는 [docs/runbooks](docs/runbooks)에 있다. S0 이후 모든 변경은 이슈 → 짧은 가지 → PR → 필수 CI → 자동 병합 순서다.

전체 판의 기본·더미 × 사람 규칙 A/B/C 결정론 관문은 Release 빌드 후 `node tools/verify-s2.mjs`로 실행한다. 실행 원자료와 단계 보고는 `docs/evidence/`, `docs/review/`에 보존한다.

[S3 후보 표](docs/content/S3-pool.md)는 `node tools/content-report.mjs`로 정본 데이터에서 재생성한다. CI는 전체 후보를 검사하지만 시뮬레이션은 명시한 프로필의 집합만 실행한다. 기본 `s2-baseline`은 기존 동작을 유지하고, `s4-stage-one`은 후보 중 정확히 선택한 시제품 효과를 실행한다. 후보 문장 전체의 구현이나 밸런스 승인을 뜻하지 않는다. [기본 영웅·영지 세 안](docs/content/S3-base-proposals.md)의 최종 선택은 사용자 결정으로 남긴다.

[리그 런북](docs/runbooks/run-league.md)에 S4의 A/B/C × 정책 × seed 실행, 원본 CSV, 보고서·그래프 재생성 절차가 있다. 그래프는 Node 표준 라이브러리만으로 SVG와 PNG를 생성한다. 추가 그래프 의존성이 없다.
