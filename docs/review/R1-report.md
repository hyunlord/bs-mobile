관문: 부분 — 원본 보관·도구 이전 검증 완료, 실제 크기 위반 PR 차단 완료, 의존성 자동 병합 검증 대기.
변경: 원자료를 Release로 옮기고 5MB 예산, Node 도구, 주간 의존성 정책을 추가했다.
증거: 원본 1,399파일 다운로드·SHA-256·정확한 파일 집합 검증 통과.
크기: 원본 tracked 트리 267,845,523 bytes; 제거한 원본 blob 265,292,205 bytes. 기반 커밋 8aaf94e 트리 2,820,696 bytes(439파일), 약98.9% 감소.
결정: #32·#42 권고 승인, Projects 대신 라벨·마일스톤(#2), 메이저 의존성은 #46.
한계: Git 이력은 그대로라 과거 blob과 clone 전송량 감소를 주장하지 않는다. Unity 미착수.

# R1 저장소 정리

## 원본 보존

[증거 보관 안내](../evidence/README.md), [전체 검증](../evidence/R1-archive-verification.md), [파일별 SHA-256](../evidence/phase0-source-manifest.csv), [Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-evidence-original-20261008).

원본은 main `b3f4830bb8c441343f61362e2b39e474f5709290`에서 만들었다. 압축 자산 SHA-256은 `ee04370bc0c5a560238a1223efb18eaadd1ba0e2cbc61fc7f2e0dbc4b2465881`이다. 새 다운로드 위치에서 1,399개 파일 / 265,931,147 bytes를 확인한 후 1,363개 파일을 현재 트리에서 제거했다. 기준 트리 전체는 1,785개 파일 / 267,845,523 bytes다. 원본 S4 실패와 보고서·집계 CSV는 유지했다.

## 정책과 도구

[ADR0012](../adr/0012-external-evidence-and-tree-budget.md)의 5,000,000바이트 예산은 신규 파일과 기존 파일의 양의 증가분을 실제 git blob으로 계산한다. 삭제로 상쇄할 수 없고 이름 변경·복사는 새 경로 전체를 센다. 원시 JSON·압축파일·raw 디렉터리·Python의 재유입을 차단한다. 정확한 경계, 거짓 manifest 이름, ref 오류, PR head와 checkout 차이, 파일 형식 우회 등을 테스트했다.

Python 그래프·패키징 도구를 Node로 옮겼다. SVG와 PNG를 같은 장면에서 생성하고 PNG의 CRC·결정론·CSV 좌표를 확인했다. 원본 576사례 CSV로 현행 보고서를 생성한 후 복사된 CSV만으로 다시 생성하여 24개 파일 전부가 바이트 동일했다. 과거 그래프를 재생성해 역사적 결과를 덮어쓰지 않았다. 패키징은 tracked 파일만 담고 CRC, 빈 경로 추출, 정확한 파일 집합과 SHA-256을 검사한다.

## 의존성 정리 진행

주간 그룹 업데이트를 사용한다. 패치·마이너는 엄격한 PR 형식·이슈·ADR·CI를 통과한 정확한 head만 자동 병합한다. 메이저와 분류 불명 변경은 [#46](https://github.com/hyunlord/bs-mobile/issues/46)에 모았다. 기존 #14~#18, #23, #26은 병합 없이 닫고 브랜치를 제거했다. #22(마이너), #24(패치)는 Dependabot이 2026-10-08T02:08:44Z/02:08:48Z에 병합 없이 닫고 주간 그룹 #51로 대체했다. #51은 BenchmarkDotNet 0.15.8, Test.Sdk 17.14.1, xunit 2.9.3의 마이너·패치만 포함한다. 실제 자동 병합은 후속 검증 중이다. 지속 추적 이슈는 [#45](https://github.com/hyunlord/bs-mobile/issues/45)다.

## 검증 상태

현 로컬 전체 검사: 빌드 경고·오류 0, .NET 95테스트 통과, S2 smoke 18사례×3반복, S4 smoke 54독립사례×3반복 통과. 이는 전체 리그의 균형 통과를 뜻하지 않는다. 신규 의존성 자동화 통합 후 전체 검사를 다시 실행하고 GitHub CI와 실제 초과 PR 결과를 기록한다.

R1 최종 관문은 상위 [#44](https://github.com/hyunlord/bs-mobile/issues/44), 기반 구현은 [#47](https://github.com/hyunlord/bs-mobile/issues/47)에서 추적한다. 기반 PR 하나의 병합만으로 R1 완료를 주장하지 않는다.

## 실제 CI 관문

기반 [PR48](https://github.com/hyunlord/bs-mobile/pull/48)은 [CI37715959947](https://github.com/hyunlord/bs-mobile/actions/runs/37715959947)의 quality·secrets 통과 후 main `00ae6af0f6ec4ee326a0d969c566b56b8a73c170`으로 병합됐다. 실제 [위반 PR49](https://github.com/hyunlord/bs-mobile/pull/49)는 신규 파일 5,000,001bytes가 예산을 1byte 초과하여 [CI37716260011](https://github.com/hyunlord/bs-mobile/actions/runs/37716260011)에서 RB001로 실패했고 병합 없이 닫았다. 비밀값 검사는 통과했다.

`tools/verify-evidence.mjs`로 다운로드 원본 전체1,399파일과 자산payload2개를 다시 검증했다. 도구는 source모드의 정확한 파일 집합, 경로 이탈·symlink·중복·누락·크기·SHA256 오류를 차단한다. 자산 안내문과 해시 목록 자체는 별도 bytecompare 항목이며 payload manifest에 자기 해시를 넣었다고 주장하지 않는다.
