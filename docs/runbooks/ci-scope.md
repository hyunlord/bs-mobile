# PR별 CI 검사 범위 확인

[ADR0038](../adr/0038-docs-only-ci.md)의 범위 선택 규칙을 실제 PR에서 확인한다. 구현은 [#140](https://github.com/hyunlord/bs-mobile/issues/140), 첫 문서 전용 증거는 [#144](https://github.com/hyunlord/bs-mobile/issues/144)에서 추적한다.

## 검사 범위

| 변경 | quality 내부 관문 | .NET·결정론 교차 검증 |
|---|---|---|
| PR 변경 경로가 모두 `docs/` 아래 | Document gate | 생략 |
| 코드·데이터·스키마·CI·기타 경로가 하나라도 포함 | Full gate | 실행 |
| 이름 변경의 원본 또는 목적지가 docs/ 밖 | Full gate | 실행 |
| 경로 비교 실패·빈 비교·알 수 없는 상태 | Full gate | 실행 |
| main push 또는 수동 실행 | Full gate | 실행 |

두 PR 경로 모두 필수 `quality`·`secrets` 결과를 게시한다. 문서 관문은 PR 정책·저장소 예산·변경 Markdown의 형식과 로컬 파일 링크를 확인한다. 외부 URL 가용성, 렌더러별 앵커, 변경되지 않은 문서에서 삭제된 파일로 향하는 역참조는 확인하지 않는다.

## 실제 실행 확인

1. PR의 Files changed에서 모든 경로가 `docs/` 아래인지 확인한다. 코드에서 문서로 옮긴 파일이 있으면 원본 경로도 확인한다.
2. PR의 CI 링크에서 `quality` job을 연다. `Select conservative validation scope` 로그가 `CI scope: docs`인지 확인한다.
3. `Document gate`가 성공하고 `.NET setup`, `Full gate`, 런타임 evidence 업로드가 skipped인지 확인한다. `quality`와 `secrets`는 모두 성공해야 한다.
4. 검증한 커밋 해시와 PR·CI 링크를 해당 이슈에 기록한다. 문서 검사를 통과했다는 결과를 게임 기능·성능·결정론 검증으로 표현하지 않는다.

로컬에서는 변경에 맞는 집중 검사와 diff 검토만 한다. CI와 같은 전체 런타임 검사는 중복 실행하지 않는다. 커밋 후 `node tools/check-docs.mjs`로 기본 `origin/main...HEAD` 문서 비교를 확인할 수 있다. 전체 관문을 수동으로 다시 실행할 필요가 있으면 새 실패나 미해결 우려처럼 구체적인 근거를 먼저 남긴다.
