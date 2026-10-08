# ADR 0021: SDK 검증 이후 VSTest adapter major를 독립 검증

- 상태: 제안 — 보호된 PR의 CI 통과·병합 시 채택
- 날짜: 2026-10-08
- 이슈: [#76](https://github.com/hyunlord/bs-mobile/issues/76), 사용자 승인 [#46](https://github.com/hyunlord/bs-mobile/issues/46)

## 결정

Test SDK 18.10.1의 독립 검증·PR #75 병합 이후 xunit.runner.visualstudio만 2.8.2에서 4.0.0으로 갱신한다. Microsoft.NET.Test.Sdk 18.10.1과 xunit 2.9.3은 유지한다. 이는 VSTest adapter 갱신이며 xunit.v3 프레임워크 전환이 아니다.

공식 패키지 README는 .NET 8 이상 및 xUnit 1.9.2 이후 테스트 실행을 지원한다고 명시한다. 4.0.0 release note는 종료 시 메시지 순서 문제의 수정을 포함한다. 대상 지원은 실제 테스트 발견·실행·실패 종료 검증과 구분한다. [공식 패키지](https://www.nuget.org/packages/xunit.runner.visualstudio/4.0.0), [공식 release](https://xunit.net/releases/visualstudio/4.0.0)

## 검증과 경계

직전 main ea6e5a7에서 adapter 2.8.2로 Release 빌드·165개 테스트 통과를 확인했다. 변경 후 restore/build,165개 발견·실행, 전체 tools/check.sh와 실제 CI를 검증한다. 동일 패키지 조합의 임시 테스트에서 의도된 assertion 실패가 종료 코드에 반영되는지 확인하고 probe는 커밋하지 않는다. 실제 명령·결과와 CI 링크는 PR에 기록한다.

Core·콘텐츠·실험 수치·Unity 프로젝트는 변경하지 않는다. .NET 호스트 테스트가 Unity/IL2CPP·기기 검증을 뜻하지 않는다. primary checkout의 소스·빌드 산출물은 건드리지 않는다.

## 대안과 복구

xunit.v3 전환이나 다른 패키지 major 동시 갱신은 원인 분리와 승인 범위를 위해 제외한다. 회귀 시 adapter 버전만 되돌린다.
