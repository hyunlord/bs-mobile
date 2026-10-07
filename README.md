# Sow & Siege (가제)

모바일 판타지 액션에서 도구가 공격하고 땅·건물·사람을 키우는 게임. 기본 영웅 1·영지 전통 1로 시작하되 규칙은 데이터로 확장한다.

**Phase0 S0–S5** 산출물을 정리했다. [단계 결과와 남은 결정](docs/review/S5-report.md), [시장·BM 근거](docs/research/S5-bm.md)를 먼저 읽는다. S0 Projects 보드는 권한 제한으로 미완료다. **S4 헤드리스 봇 리그** 실행을 마쳤다. 576사례의 결정론·CSV 재현은 통과했으나 무기형이 모든 조건에서 우세해 밸런스 관문은 실패했다. [결과와 한계](docs/review/S4-report.md)를 보존했다. S2의 30Hz 월드에서 이동·전투·농지·건물·사람과 카드 선택을 실행하고, S4 프로필은 선택된 후보의 특허장·물품·진화를 연결한다. 전체 판은 임시 설정 12분이며 CI 연기 시험은 앞 900틱만 실행한다. S0의 과거 지표와 실제 게임 지표는 분리한다. 측정된 리그 결과와 모바일 화면·사람의 재미 검증을 구분하며 Unity 프로젝트는 다음 개발 단계에서 생성한다.

.NET 8 SDK, Node.js 22 이상(npm), Python 3, Git, Bash를 준비한 깨끗한 클론의 루트에서:

```sh
./tools/check.sh
```

에이전트는 [AGENTS.md](AGENTS.md)부터 읽는다. 설계 원본은 [docs/design](docs/design), 결정 기록은 [docs/adr](docs/adr), 반복 절차는 [docs/runbooks](docs/runbooks)에 있다. S0 이후 모든 변경은 이슈 → 짧은 가지 → PR → 필수 CI → 자동 병합 순서다.

전체 판의 기본·더미 × 사람 규칙 A/B/C 결정론 관문은 Release 빌드 후 `node tools/verify-s2.mjs`로 실행한다. 실행 원자료와 단계 보고는 `docs/evidence/`, `docs/review/`에 보존한다.

[S3 후보 표](docs/content/S3-pool.md)는 `node tools/content-report.mjs`로 정본 데이터에서 재생성한다. CI는 전체 후보를 검사하지만 시뮬레이션은 명시한 프로필의 집합만 실행한다. 기본 `s2-baseline`은 기존 동작을 유지하고, `s4-stage-one`은 후보 중 정확히 선택한 시제품 효과를 실행한다. 후보 문장 전체의 구현이나 밸런스 승인을 뜻하지 않는다. [기본 영웅·영지 세 안](docs/content/S3-base-proposals.md)의 최종 선택은 사용자 결정으로 남긴다.

[리그 런북](docs/runbooks/run-league.md)에 S4의 A/B/C × 정책 × seed 실행, 원본 CSV, 보고서·그래프 재생성 절차가 있다. 그래프 생성에는 설치된 Python Matplotlib가 필요하며 `./tools/check.sh`의 계산·문서 검증은 그래프 의존성 없이 실행한다.
