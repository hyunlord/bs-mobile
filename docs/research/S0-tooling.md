# S0 도구 조사와 ADR 입력

관문 결과: 문서 조사 완료. Unity 실행·라이선스 활성화·플랫폼 빌드 성공은 아직 이 조사로 입증되지 않았다.

확인일: 2026-10-08 (Asia/Seoul). 공식 공급자 문서와 도구 원저장소를 확인했다. 아래 결정 권고는 조사자의 판단이며, 저장소의 채택 ADR 및 실제 검증 결과와 구분한다.

## 확인된 사실

| 항목 | 공식 자료에서 확인한 상태 | S0 적용 판단 |
| --- | --- | --- |
| Unity LTS | 현재 공식 지원 페이지의 LTS는 **Unity 6.3 LTS**이며 2027년 12월까지 지원한다. Unity 6.0 LTS 지원은 2026년 10월까지다. | 신규 프로젝트 기준 계열은 6.3 LTS. 정확한 패치와 changeset은 설치 가능한 릴리스 목록 및 실제 설치 결과를 확인한 뒤 `ProjectVersion.txt`에 고정한다. 이 문서는 특정 패치가 최신이라고 주장하지 않는다. |
| Unity CLI | 공식 소개 문서는 experimental로 표시한다. 릴리스 노트의 최신 항목은 **2026-10-07 / 1.0.0-beta.13**이다. | 자동화 후보이나 stable/GA로 부르지 않는다. 사용할 바이너리 버전·설치 경로·명령 출력을 기록하고, 기본 batchmode 경로를 유지한다. |
| Unity Pipeline | `com.unity.pipeline`은 실행 중인 Editor에 로컬 API를 제공하는 experimental 패키지다. 공식 설치 안내의 전제는 Unity 6.0 이상과 Unity CLI다. | Editor 연결 및 명령 실행을 별도 smoke test로 입증한다. 패키지의 현재 정확한 버전은 이 조사에서 확정하지 않았다. |
| Unity MCP | 폐기된 것은 **`com.unity.ai.assistant`의 Editor 내장 MCP 서버**다. CLI의 `unity mcp`는 지원되고, 제3자 MCP 패키지는 이 폐기 대상이 아니다. | “Unity MCP 전체 폐기”로 기록하지 않는다. CLI 직접 명령을 우선 검토하되, MCP가 필요하면 CLI MCP를 사용한다. |
| GameCI | 공식 GitHub 안내는 `unity-builder@v4`, `unity-test-runner@v4`를 제시하며 activation 페이지는 v4를 current로 표시한다. | v4 계열을 검토하되 Actions 사용 시 검증한 commit SHA로 고정한다. 문서 major 확인은 해당 SHA 실행 성공을 뜻하지 않는다. |
| Personal 라이선스 | GameCI 안내는 로컬 Hub 활성화 후 `.ulf`와 Unity 계정 자격증명을 GitHub secrets에 넣는 흐름을 설명한다. Builder 문서도 일회성 수동 활성화를 요구한다. | 무자격 CI가 자동으로 Unity를 사용할 수 있다고 가정하지 않는다. 필요한 secrets가 없으면 Unity 검증을 BLOCKED로 기록하고 독립적인 정적·core 검사를 계속한다. |
| Linux ARM64 | Unity 6.3 Editor 요구사항의 Linux CPU는 **x64/SSE2**다. 같은 문서의 Linux server player 표에는 ARM64가 있지만 Editor 지원과 다른 항목이다. | Linux ARM64/DGX를 Unity Editor 기본 실행기로 삼지 않는다. Unity는 지원되는 macOS 또는 Linux x64 경로, DGX는 순수 core/data 검증 등으로 역할을 나눈다. |
| Graft C# | 원저장소 README에서 C#은 **broad tier**: generic tree-sitter 기반 심볼·이름 해석 호출 간선. C#은 문서의 compiler-grade LSP 언어 목록에 없다. | 탐색 보조로 사용한다. C# 정확한 타입 해석·전체 호출 증명·아키텍처 관문의 대체재로 쓰지 않는다. |

## 1차 출처

모든 링크 확인일은 2026-10-08이다.

1. [Unity 6 release support](https://unity.com/releases/unity-6/support): LTS 계열과 지원 기간.
2. [Unity CLI 소개](https://docs.unity.com/en-us/unity-cli/unity-cli): experimental 표기 및 CLI 역할.
3. [Unity CLI release notes](https://docs.unity.com/en-us/unity-cli/release-notes): 1.0.0-beta.13, 2026-10-07 항목. 과거 버전의 인증·로그 관련 수정도 포함하므로 단순한 “beta 최신” 자동 갱신 대신 버전 고정 후 재검증한다.
4. [Meet the Unity CLI](https://unity.com/blog/meet-the-unity-cli): 2026-07-20 발표. Pipeline experimental 및 localhost/dev build 용도.
5. [Unity Pipeline package](https://docs.unity.com/en-us/unity-cli/unity-pipeline/unity-pipeline-package): Editor 6.0 이상 전제, 설치와 연결 확인 절차.
6. [Unity CLI as replacement for in-Editor MCP server](https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli): 폐기 범위, CLI MCP 지원 유지, 제3자 패키지 비영향.
7. [GameCI getting started](https://game.ci/docs/github/getting-started/), [Builder](https://game.ci/docs/github/builder/), [Activation](https://game.ci/docs/github/activation/): v4 예시와 라이선스 선행 조건. 실제 사용 계정의 자격·계약 적합성은 여기서 판정하지 않는다.
8. [Unity 6.3 system requirements](https://docs.unity3d.com/6000.3/Documentation/Manual/system-requirements.html): Editor와 server player 표를 구분해서 확인했다.
9. [Graft 원저장소 README](https://github.com/trailhq/Graft/blob/fe30ead39d5e6f0c921018d364da2bdbc9d4b3ad/README.md#supported-languages): 확인 시점 원격 HEAD를 `git ls-remote`로 고정했다. 로컬 `graft --version` 결과는 **0.21.1**이며 원격 HEAD와 동일 빌드라는 뜻은 아니다.

## ADR 권고

- **엔진 기준:** Unity 6.3 LTS 계열을 채택하고 정확한 patch/changeset, 설치 모듈, 최초 import/build 로그를 함께 고정한다. CLI/Pipeline은 선택적인 실험 자동화 표면으로 두고 기본 Editor batchmode 검증을 유지한다.
- **실행기 역할:** macOS Apple Silicon 또는 Linux x64에서 Editor 검증, Linux ARM64에서 엔진 독립 core/data 도구 검증을 수행한다. 특정 장비의 설치·접속 가능 여부는 런북의 실측 값으로 결정한다.
- **관문 구분:** S0 구조·의존·정책 CI와 Unity compile/EditMode/PlayMode/build를 별도 상태로 보고한다. 라이선스 누락으로 건너뛴 Unity job을 PASS로 계산하지 않는다.
- **탐색 보조:** Graft 결과는 소스 탐색과 영향 범위 후보에 사용한다. CI 규칙은 별도 결정적 검사와 컴파일 결과로 입증한다.
- **권한 필요 사항:** 라이선스 자격증명, 사용 가능한 CI 실행기, 디바이스 확보 등 사용자가 결정해야 하는 항목은 `needs-decision` 이슈에 기록한다. 계정 인증을 요구하는 경로를 임의로 우회하지 않고 다른 독립 작업을 계속한다.

## 미검증과 후속 증거

이 문서는 웹 조사 산출물이다. 실제 Unity Editor 설치, Personal 활성화, GameCI 컨테이너 실행, CLI/Pipeline 접속, Android/iOS 빌드, 실제 디바이스 성능은 수행하지 않았다. 해당 검증에는 명령, 버전, SHA, 종료 코드, 원본 로그, 수행 시각을 붙여야 한다. 의도적 위반 PR 5개의 GitHub CI 차단 여부도 별도 PR/run 증거로만 판정한다.
