관문: 통과 — 트리 267,845,523→2,836,181bytes, 원본 1,399파일 검증·초과 PR 1/1차단·기존 의존성 PR 9/9정리·그룹 1개 자동 병합으로 R1 관문을 충족했다.
변경: 원자료를 Release로 이전하고 5MB 예산, Node 도구, 주간 그룹 갱신을 적용했다.
크기: tracked 트리 267,845,523 → 2,836,181 bytes, 1,785 → 443파일(측정 커밋 아래 명시).
증거: 원본 1,399파일과 R1 실행 증거 14파일을 새로 다운로드·추출해 전체 해시와 파일 집합 검증.
CI: 실제 PR49가 5,000,001bytes로 차단됐고, 그룹 PR51은 전체 CI 후 github-actions가 자동 병합했다.
결정: #32·#42 승인 기록 및 종료, Projects 없이 라벨·마일스톤 사용(#2 종료).
남은 결정: 메이저·분류 불명 의존성 검토 #46. R2·R3 진행을 기다리게 하지 않는다.
한계: 과거 Git 이력·원래 S4 FAIL 유지. Unity·휴대전화 검증은 하지 않았다.

# R1 저장소 정리 결과

## 트리와 원본 보존

| 측정 대상 | 커밋 | 파일 수 | bytes |
|---|---|---:|---:|
| R1 전 | b3f4830bb8c441343f61362e2b39e474f5709290 | 1,785 | 267,845,523 |
| R1 구현·의존성 반영 후 | 5446322322955d10ea534d4b011b1459072f09f0 | 443 | 2,836,181 |

같은 `git ls-tree -r -l` 기준으로 약 98.9% 감소했다. `.git`, 무시된 빌드 출력, 파일시스템 블록 크기는 제외했다. 이 종료 보고와 최종 소규모 매니페스트의 후속 추가량은 위 측정에 포함하지 않으며, 최종 ZIP의 파일별 크기는 ZIP 내 MANIFEST에 있다. 히스토리 재작성은 하지 않았으므로 과거 blob과 clone 전송량까지 줄었다고 주장하지 않는다.

[원본 Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-evidence-original-20261008)는 기준 커밋의 evidence·league 전체 1,399파일 /265,931,147bytes를 보존한다. 재다운로드한 압축 자산과 파일별 매니페스트를 검증하고 빈 경로에 추출해 전체 SHA-256·크기·정확한 파일 집합을 확인한 뒤, 1,363개 대량 파일 /265,292,205bytes를 현재 트리에서 제거했다. 원래 S4 실패 보고서와 남긴 집계 CSV는 바이트 동일하다.

[원본 검증 상세](../evidence/R1-archive-verification.md), [원본 파일 해시](../evidence/phase0-source-manifest.csv), [자산 해시](../evidence/phase0-assets-sha256.csv). 과거 보고서 링크는 고정 커밋 또는 Release 복원 경로를 가리킨다. Release는 덮어쓰지 않는 운영 규칙이며 GitHub `isImmutable:false`인 플랫폼 불변 보장은 아니다.

## CI와 도구

[ADR0012](../adr/0012-external-evidence-and-tree-budget.md): 신규 파일의 실제 git blob과 수정 파일의 양의 증가분을 합해 5,000,000bytes 초과 시 실패한다. 삭제량으로 상쇄할 수 없고 이동·복사는 새 경로 전체를 센다. docs의 압축 파일·raw 디렉터리·evidence JSON, Python, symlink/submodule 및 해석 불가 ref를 차단한다. 임의로 바꾼 이름의 모든 파일 의미를 자동 판별한다는 주장은 하지 않는다.

실제 [위반 PR49](https://github.com/hyunlord/bs-mobile/pull/49)는 신규 파일 5,000,001bytes가 한도를 1byte 초과해 [CI37716260011](https://github.com/hyunlord/bs-mobile/actions/runs/37716260011)의 quality에서 `RB001`로 실패했다. secrets는 통과했다. 병합 없이 닫고 브랜치를 제거했다. 정상 기반 [PR48](https://github.com/hyunlord/bs-mobile/pull/48)은 [CI37715959947](https://github.com/hyunlord/bs-mobile/actions/runs/37715959947)를 통과했다.

그래프와 패키징을 Python에서 Node로 이전했다. 원본 576사례 CSV로 현행 SVG·PNG를 실제 생성·확인한 후, 복사된 CSV만으로 재생성한 24파일이 바이트 동일했다. 역사적 그래프를 덮어쓰지 않았다. 패키징은 tracked 파일만 담고 CRC·깨끗한 추출·정확한 파일 집합·SHA-256을 검증한다. 기반 구현 커밋의 실제 439파일 패키지도 이 검사를 통과했다. 별도 `verify-evidence.mjs`는 공개 매니페스트의 재검증 명령을 제공한다.

## 의존성 운영과 실제 복구

[ADR0013](../adr/0013-dependency-policy.md)에 따라 매주 생태계별 마이너·패치 그룹을 제안하고, 엄격한 이슈·ADR·PR 형식·quality·secrets 검사를 유지한다. 신뢰하는 main의 조정 작업만 쓰기 권한을 가지며 PR 코드 실행은 읽기 권한 작업으로 분리한다. 결과는 실제 검사한 head에만 연결하고, 오래된 base 또는 예상하지 못한 head는 실패한다.

[기존 9개 처리 원장](../evidence/R1-dependency-disposition.csv): #14~#18·#23·#26은 메이저 또는 분류 불명 제안으로 [#46](https://github.com/hyunlord/bs-mobile/issues/46)에 모아 병합 없이 닫았다. #22·#24는 Dependabot이 주간 그룹 [PR51](https://github.com/hyunlord/bs-mobile/pull/51)로 대체하고 닫았다. 새 메이저 그룹 PR50도 자동화가 같은 #46에 기록한 뒤 병합 없이 종료했다. 지속 추적 #45는 열린 상태로 유지한다.

첫 실제 준비 실행은 부록 커밋 직후 PR head 확인에서 실패했다. 당시 조회값이 로그에 없어 첫 실패 원인의 조회 지연은 추론으로 남긴다. 복구는 이전 PR head이면서 실제 branch ref가 예상 새 OID일 때만 최대4회 관측하며, 다른 값은 즉시 차단한다. 커밋 제목 생성·검증 불일치도 수정했고, 기존 서명된 단일 파일 부록은 정확한 내용 일치 시에만 재사용한다. 실패 회귀4개를 먼저 확인한 뒤 수정했으며 [PR54 CI](https://github.com/hyunlord/bs-mobile/actions/runs/37717049393)가 통과했다.

새 [준비 실행37717482161](https://github.com/hyunlord/bs-mobile/actions/runs/37717482161)에서는 **실제로** 이전 PR head `7a225dd8…`와 새 branch ref `cd7bd5db…`가 동시에 관측됐다. 재확인 후 수렴했고 github-actions가 조건부 병합을 설정했다. [자동 CI37717506157](https://github.com/hyunlord/bs-mobile/actions/runs/37717506157)는 정확한 `cd7bd5db946aa573c94828fd2d5e9fdf66337548`의 전체 quality·secrets를 통과시켰다. PR51은 2026-10-08T02:24:25Z에 **github-actions가 자동 병합**해 main `5446322322955d10ea534d4b011b1459072f09f0`이 됐다. BenchmarkDotNet 0.15.8, Test.Sdk 17.14.1, xunit 2.9.3만 반영됐으며 제안된 메이저는 반영하지 않았다.

## 검증 범위와 재현

로컬 전체 검사: 빌드 경고·오류0, .NET95테스트, S2 smoke18사례×3반복, S4 smoke54독립사례×3반복 통과. 예산24·의존성20·공개 증거 검증3개 및 CSV·콘텐츠·도구 검사를 포함했다. 새 패키지 버전도 실제 자동 CI에서 검증했다. 짧은 smoke는 전체 균형 통과나 Unity 성능을 뜻하지 않는다.

[R1 실행 Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-r1-runtime-20261008)는 실패·복구·CI·자동 병합 로그 14파일 /818,506bytes를 보관한다. 압축 SHA-256 `4a8043eae7f726383ee991a0fb66d18aa4d360b914482e0856bd4ca0e2cd5a19`. 다시 다운로드한 payload2개와 빈 경로에 추출한 전체14파일 검증을 통과했다. [파일 해시](../evidence/R1-source-manifest.csv), [자산 해시](../evidence/R1-assets-sha256.csv), [관문 원장](../evidence/R1-gates.csv). 해시 목록 자체는 재귀 자기 해시 대상이 아니다.

보고서는 첫10줄 이내 변경·관문·결정을 요약한다. 역사적 원본 보고서는 원문·실패 결론을 보존하며 현행 재현 안내를 분리한다. S4b는 다음 R2에서 사전 선언 후 실행하고, Phase1 Unity 작업은 시작하지 않는다.
