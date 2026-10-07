# Sow & Siege (가제)

모바일 판타지 액션에서 도구가 공격하고 땅·건물·사람을 키우는 게임. 기본 영웅 1·영지 전통 1로 시작하되 규칙은 데이터로 확장한다.

현재는 **S0 개발 기반**이다. 연기 시험은 저장소·데이터·결정론 검사 배선을 확인하며 완성된 게임, S2 시뮬레이션 또는 S4 균형을 증명하지 않는다. Unity 프로젝트는 다음 개발 단계에서 생성한다.

.NET 8 SDK, Node.js 22 이상(npm), Python 3, Git, Bash를 준비한 깨끗한 클론의 루트에서:

```sh
./tools/check.sh
```

에이전트는 [AGENTS.md](AGENTS.md)부터 읽는다. 설계 원본은 [docs/design](docs/design), 결정 기록은 [docs/adr](docs/adr), 반복 절차는 [docs/runbooks](docs/runbooks)에 있다. S0 이후 모든 변경은 이슈 → 짧은 가지 → PR → 필수 CI → 자동 병합 순서다.
