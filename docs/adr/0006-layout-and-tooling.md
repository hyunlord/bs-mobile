# ADR 0006: 단일 코어 트리와 검증 도구

상태: 채택 (2026-10-08), 연결 #1.

S0의 `core/` 경계와 S2의 `src/`, `tests/`는 `core/src/` 및 `core/tests/`로 합친다. 루트 솔루션에서 `dotnet test`가 가능해야 한다. 물리 소스 복제나 심볼릭 링크는 두지 않는다.

.NET 8 SDK, xUnit, BenchmarkDotNet은 의뢰서 지정이다. 요청된 JSON Schema 2020-12 검증에는 개발 전용 Ajv 8.20.0을 사용한다. 직접 부분 스키마 검증기를 만들면 unknown keyword/type/required 위반을 놓치므로 표준 엔진을 선택했다. 제품 Core에는 외부 패키지를 넣지 않는다. Node는 검사 도구에만 필요하다. lockfile과 Dependabot으로 버전을 관리한다.

현재 개발 기반의 리그·성능은 synthetic-scaffold이며 S2 게임·S4 밸런스 증거와 섞지 않는다. 지표에는 stage/model/commit/runtime를 보존한다.

Unity 기준은 6.3 LTS 계열, 정확한 patch는 1단계 설치 후 고정한다. CLI/Pipeline은 experimental, 기본 batchmode 대안 유지. Linux ARM64 Editor는 지원 표에 없으므로 DGX는 S4 순수 .NET 대량 리그에만 사용하며 공개 저장소 러너로 등록하지 않는다. Graft C#은 broad 지원 탐색 보조이며 적합성 검사를 대신하지 않는다. 출처와 확인일은 docs/research/S0-tooling.md.

관문: ./tools/check.sh, 실제 위반 PR 다섯 개, 깨끗한 클론, 독립 런북 PR. 프로젝트 보드 권한은 needs-decision으로 추적하며 미충족 관문은 부분으로 보고한다.
