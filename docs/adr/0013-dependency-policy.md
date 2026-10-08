# ADR 0013: 주간 의존성 그룹과 검증 후 자동 병합

관문: 정책 구현·로컬 회귀 검사 완료, 실제 GitHub 실행 증거는 R1 종료 보고에서 별도 확인한다.
변경: 주간 생태계별 그룹, 마이너·패치 전체 CI 후 병합, 메이저·미분류는 중앙 이슈.
사용자 결정: 메이저 호환성·이전안은 #46, 일상 갱신 추적은 #45. 새 비밀값은 요구하지 않는다.

- 상태: 채택
- 연결: R1 #44, 운영 기반 #47, 지속 추적 #45, 메이저 결정 #46
- 공식 문서 확인: 2026-10-08

## 결정

NuGet·npm·GitHub Actions는 매주 월요일 한국 시각 03:00에 확인한다. 생태계마다 마이너·패치 그룹과 메이저 그룹을 나눈다. 그룹은 PR 수를 줄이는 단위이며, 메이저 결정은 생태계 전체를 합쳐 **이슈 #46 하나**에 모은다. 확인할 수 없는 SHA 갱신도 같은 이슈에 미분류 검토로 기록하고 자동 병합하지 않는다. 보안 갱신은 GitHub의 별도 발생 주기를 지연하지 않으며 동일 분류·검증 정책을 적용한다.

공식 `dependabot/fetch-metadata`의 서명 검사와 작성자 검사를 유지한다. 각 그룹의 **모든 구성원**이 확인된 마이너·패치일 때만 자동 대상이다. 하나라도 메이저·미분류이거나 유지보수자 변경 표시가 있으면 자동 대상에서 제외한다. 0.x 마이너도 사용자 지시에 따라 대상이지만 호환성을 보장하지 않으므로 전체 CI를 생략하지 않는다.

#45는 병합으로 닫지 않는 지속 추적 이슈다. 자동 대상 PR별 작업 이슈를 만들고 그 번호만 `Closes #N`으로 연결한다. 기존 본문의 이슈 종료 지시문은 참조로 바꾼다. 본문에 관문·검증·화면 제목과 미실행 상태를 기록하고, 같은 PR에 `0013-dependency-pr-N.md` 결정 부록을 추가한다. 이 파일들은 ADR 0013의 실행별 부록이며 다른 ADR 번호를 소비하지 않는다. 이슈·제목·본문·구조 변경 ADR 규칙에 Dependabot 예외를 만들지 않는다.

## 권한과 CI 경계

`Dependency policy`는 `pull_request_target`의 **기본 브랜치 코드만** 실행한다. 같은 저장소의 열린 Dependabot PR, main 대상, 서명된 커밋을 확인한다. 메타데이터가 읽는 첫 Dependabot 커밋 하나와 자동 생성한 단일 ADR 파일 커밋만 허용한다. 이벤트의 head SHA, 현재 PR head, 마지막 커밋 SHA를 묶어서 새 메이저 갱신에 오래된 마이너 판정을 재사용하지 못하게 한다. 외부 PR 코드·패키지 설치·테스트는 쓰기 토큰 작업에서 실행하지 않는다.

ADR은 `createCommitOnBranch`의 `expectedHeadOid`로 원자적으로 추가한다. 같은 본문·부록을 다시 실행해도 중복하지 않는다. 예상과 다른 기존 부록이나 동시 변경은 실패로 멈춘다. 자동화가 만든 쓰기 이벤트의 일반 CI 실행을 가정하지 않고, 신뢰하는 main의 `check.yml`을 PR 번호와 정확한 SHA로 명시 호출한다. 기본 `GITHUB_TOKEN`을 쓰므로 별도 PAT나 App 비밀값을 추가하지 않는다.

dispatch resolver는 현재 PR을 다시 확인한다. 읽기 전용 quality·secrets 작업은 해당 SHA를 checkout한다. quality는 셸에서 합성 PR 이벤트 경로를 넘겨 이슈·ADR·크기 예산까지 정상 PR과 같은 관문을 실행한다. 예약된 `GITHUB_*` 변수를 `GITHUB_ENV`로 덮어쓰지 않는다. secrets는 기존 gitleaks의 workflow_dispatch 전체 검사를 사용한다.

main에서 dispatch한 Actions 체크는 main SHA에 붙으므로, checkout 없는 별도 쓰기 작업이 **실제 읽기 전용 작업 결론**을 검사한 dependency SHA의 `quality`·`secrets` 상태로 보고한다. 실패·취소·건너뜀은 성공으로 바꾸지 않는다. PR의 head가 바뀌면 결과를 게시하지 않는다. 기존 자동 병합 예약은 재분류 전에 해제한다. 메타데이터가 바뀌거나 실패하면 이전 승인이 남지 않는다. 준비 단계와 dispatch resolver 모두 현재 main이 PR head의 조상인지 확인하며 오래된 브랜치는 재기반 전까지 거부한다. 자동화는 검증 전에 두 상태를 pending으로 설정하고 `gh pr merge --auto --squash --match-head-commit`을 요청한다. 보호 규칙의 최신 브랜치·필수 검사·대화 해결을 유지하며 관리자 우회 병합은 쓰지 않는다. 여러 의존성 dispatch가 서로 취소하지 않도록 concurrency에 PR 번호와 SHA를 넣는다.

`Durable metrics`는 workflow_dispatch 완료를 추세에서 제외한다. dispatch의 main SHA를 dependency 실행 SHA로 잘못 기록하지 않기 위해서다. 해당 실행의 Actions 아티팩트는 남고, 일반 PR·main CI 추세는 유지한다.

## 기존 9개 PR의 분류 근거

2026-10-08 GitHub PR diff와 서명 커밋 메타데이터를 조회했다. 아래는 이전 버전 → 제안 버전의 분류이며, 실제 병합·폐기 상태는 R1 보고서와 #45/#46에 기록한다.

| PR | 실제 변경 | 분류 / 처리 원칙 |
| --- | --- | --- |
| #14 | gitleaks-action `dcedce43c6f43de0b836d1fe38946645c9c638dc` → `ff98106e4c7b2bc287b24eaf42907196329070c7`, check.yml 1곳 | 서명 메타데이터에 update-type 없음. 패치 추정 금지, #46 검토 |
| #15 | setup-node 4.4.0 → 7.0.0, check/metrics/nightly 3곳 | 메이저, #46 |
| #16 | setup-dotnet 4.3.1 → 6.0.0, check/nightly 2곳 | 메이저, #46 |
| #17 | upload-artifact 4.6.2 → 7.0.1, check/nightly 2곳 | 메이저, #46 |
| #18 | checkout 4.4.0 → 7.0.1, check 2곳·metrics/nightly 각 1곳 | 메이저, #46 |
| #22 | BenchmarkDotNet 0.14.0 → 0.15.8, Bench.csproj 1행 | 마이너, 전체 CI 통과 후 자동 병합; 0.x 위험 유지 |
| #23 | Microsoft.NET.Test.Sdk 17.11.1 → 18.10.1, Tests.csproj 1행 | 메이저, #46 |
| #24 | xunit 2.9.2 → 2.9.3, Tests.csproj 1행 | 패치, 전체 CI 통과 후 자동 병합 |
| #26 | xunit.runner.visualstudio 2.8.2 → 4.0.0, Tests.csproj 1행 | 메이저, #46 |

메이저·미분류 PR은 중앙 이슈에 버전·근거를 보존한 뒤 닫을 수 있다. 필요 시 호환성 검증을 포함한 새 이슈→PR로 다시 제안한다. 기존 마이너·패치는 버리지 않고 새 정책의 실제 통과·병합 증거로 처리한다.

## 검증과 한계

`node --test tools/test-dependency-policy.mjs`는 그룹의 한 메이저 차단, SHA 미분류, 외부/변조/미서명 PR 거부, 본문 이슈 종료 제한, ADR 원자성, head 변경 경합, CI dispatch SHA 결합과 쓰기 작업의 base checkout을 검사한다. API 테스트는 모의 응답이며 GitHub 서비스 실제 권한·서명·보호 규칙 동작을 증명하지 않는다. 실서비스 자동화 관문은 정책이 main에 들어간 뒤 실제 마이너·패치 PR에서 확인한다. 실패하면 성공으로 보고하지 않고 원인을 #45에 기록한다.

Dependabot가 주간 그룹을 생성하는 스케줄 실행 자체, 새 메이저의 호환성, 업스트림 코드의 안전성은 이 로컬 검사만으로 증명되지 않는다. ADR 추가 후 기본 브랜치가 앞서가면 최신 브랜치 보호 때문에 병합이 보류될 수 있으며, Dependabot 재생성/재기반 후 같은 관문을 다시 실행한다.

## 공식 출처와 기각한 대안

- [GitHub: Dependabot 자동화와 서명 메타데이터·자동 병합](https://docs.github.com/en/code-security/tutorials/secure-your-dependencies/automate-dependabot-with-actions): 공식 예시의 metadata SHA `d7267f607e9d3fb96fc2fbe83e0af444713e90b7`를 API에서 실재 확인하고 고정했다. 검증 우회 옵션은 false다.
- [GitHub: 그룹·update-types·주간 일정](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference): 메이저 그룹을 별도로 생성하고 자동화가 중앙 이슈로 모은다. Dependabot 설정 자체가 이슈를 생성한다고 주장하지 않는다.
- [GitHub: 토큰과 워크플로 트리거](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow): 쓰기 이벤트의 자동 재실행/승인을 가정하는 대신 명시 dispatch를 쓴다.
- [GitHub: pull_request_target 보안 경계](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request_target): 쓰기 토큰 작업에서 PR checkout·실행을 금지한다.
- [GitHub: 원자적 커밋 입력](https://docs.github.com/en/graphql/reference/input-objects#createcommitonbranchinput): expectedHeadOid를 통해 동시 변경을 확인한다.

기각: Dependabot를 PR 정책에서 면제하기, 알 수 없는 SHA를 패치로 취급하기, 메이저 자동 병합, CI 결과와 무관한 성공 상태 게시, 쓰기 토큰으로 PR 테스트 실행, 추가 PAT를 요구해 일상 갱신을 사람 승인에 의존하기.
