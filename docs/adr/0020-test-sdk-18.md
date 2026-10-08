# ADR 0020: Core 호환성 이후 테스트 SDK major를 독립 검증

- 상태: 제안 — 보호된 PR의 CI 통과·병합 시 채택
- 날짜: 2026-10-08
- 이슈: [#73](https://github.com/hyunlord/bs-mobile/issues/73), 사용자 승인 묶음 [#46](https://github.com/hyunlord/bs-mobile/issues/46)

## 결정과 경계

Core 호환성 #67의 병합 이후 Microsoft.NET.Test.Sdk만 17.14.1에서 18.10.1로 갱신한다. xunit 2.9.3과 xunit.runner.visualstudio 2.8.2는 유지한다. adapter major는 이 변경의 CI·병합 이후 별도 PR로 검증한다. Core·콘텐츠·Unity 프로젝트·실험 수치는 변경하지 않는다.

18.10.1 공식 패키지는 net8.0 지원 대상을 포함한다. 해당 release note는 published output의 coverage 파일 격리 수정을 기록한다. 패키지 지원 표기는 실제 테스트 발견·실행·실패 전달 검증을 대체하지 않는다. [공식 패키지](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.10.1), [공식 release](https://github.com/microsoft/vstest/releases/tag/v18.10.1)

## 검증

기존 패키지의 Release 빌드와 147개 테스트 통과를 기준으로 삼는다. 변경 후 restore/build, 147개 테스트 발견·실행, 전체 tools/check.sh 및 실제 CI를 검증한다. 격리된 임시 실패 테스트가 비정상 종료를 내는지 확인하고 그 probe는 커밋하지 않는다. 실제 명령·결과·CI 링크는 PR에 기록한다. Unity/IL2CPP·기기 실행을 검증했다고 해석하지 않는다.

## 대안과 되돌리기

SDK와 adapter를 함께 올리는 안은 실패 원인 분리를 어렵게 하므로 사용하지 않는다. xunit.v3 전환은 이번 승인 범위가 아니다. 회귀 시 이 한 패키지 버전 변경을 되돌린다.

## 2026-10-08 검증 완료 기록

초기 기준은 147개 테스트였다. #74 병합본 b067c3a 통합 후 최종 검사는 **165개 발견·165개 통과**, 두 Core 타깃 간 15사례 × 3반복 × 2타깃 = **90회** 동등성 실행과 전체 tools/check.sh 통과다. [최종 CI 37738544737](https://github.com/hyunlord/bs-mobile/actions/runs/37738544737) 통과 후 [PR #75](https://github.com/hyunlord/bs-mobile/pull/75)는 `ea6e5a7e6c8e40aeea725e541f32388520bf4eb6`으로 병합되어 이 결정을 채택했다. 초기 147개와 최종 165개를 같은 시점의 수치로 혼용하지 않는다.
