# Sow & Siege (가제)

모바일 판타지 액션에서 도구가 공격하고 땅·건물·사람을 키우는 게임. 기본 영웅 1·영지 전통 1로 시작하되 규칙은 데이터로 확장한다.

현재 **S3 콘텐츠 후보 풀**을 구축한다. S2의 30Hz 월드에서 이동·전투·농지·건물·사람과 카드 선택을 실행한다. 전체 판은 임시 설정 12분이며 CI 연기 시험은 앞 900틱만 실행한다. S0의 과거 지표와 실제 게임 지표는 분리하며, S4 밸런스·모바일 화면·재미를 검증했다고 주장하지 않는다. Unity 프로젝트는 다음 개발 단계에서 생성한다.

.NET 8 SDK, Node.js 22 이상(npm), Python 3, Git, Bash를 준비한 깨끗한 클론의 루트에서:

```sh
./tools/check.sh
```

에이전트는 [AGENTS.md](AGENTS.md)부터 읽는다. 설계 원본은 [docs/design](docs/design), 결정 기록은 [docs/adr](docs/adr), 반복 절차는 [docs/runbooks](docs/runbooks)에 있다. S0 이후 모든 변경은 이슈 → 짧은 가지 → PR → 필수 CI → 자동 병합 순서다.

전체 판의 기본·더미 × 사람 규칙 A/B/C 결정론 관문은 Release 빌드 후 `node tools/verify-s2.mjs`로 실행한다. 실행 원자료와 단계 보고는 `docs/evidence/`, `docs/review/`에 보존한다.

[S3 후보 표](docs/content/S3-pool.md)는 `node tools/content-report.mjs`로 정본 데이터에서 재생성한다. CI는 전체 후보를 검사하지만 시뮬레이션은 `data/profiles/s2-baseline.json`의 명시적 집합만 실행한다. 후보의 고유 효과와 피해 계수는 구현·밸런스 승인을 뜻하지 않는다. [기본 영웅·영지 세 안](docs/content/S3-base-proposals.md)의 최종 선택은 사용자 결정으로 남긴다.
