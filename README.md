# Sow & Siege (가제)

모바일 판타지 액션에서 도구가 공격하고 땅·건물·사람을 키우는 게임. 기본 영웅 1·영지 전통 1로 시작하되 규칙은 데이터로 확장한다.

현재 **S2 헤드리스 코어**를 개발한다. 30Hz 월드에서 이동·전투·농지·건물·사람과 카드 선택을 실행한다. 전체 판은 임시 설정 12분이며 CI 연기 시험은 앞 900틱만 실행한다. S0의 과거 지표와 실제 게임 지표는 분리하며, S4 밸런스·모바일 화면·재미를 검증했다고 주장하지 않는다. Unity 프로젝트는 다음 개발 단계에서 생성한다.

.NET 8 SDK, Node.js 22 이상(npm), Python 3, Git, Bash를 준비한 깨끗한 클론의 루트에서:

```sh
./tools/check.sh
```

에이전트는 [AGENTS.md](AGENTS.md)부터 읽는다. 설계 원본은 [docs/design](docs/design), 결정 기록은 [docs/adr](docs/adr), 반복 절차는 [docs/runbooks](docs/runbooks)에 있다. S0 이후 모든 변경은 이슈 → 짧은 가지 → PR → 필수 CI → 자동 병합 순서다.

전체 판의 기본·더미 × 사람 규칙 A/B/C 결정론 관문은 Release 빌드 후 `node tools/verify-s2.mjs`로 실행한다. 실행 원자료와 단계 보고는 `docs/evidence/`, `docs/review/`에 보존한다.
