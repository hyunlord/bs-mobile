관문: 부분 — S0 실행 3/4·의도 위반 PR 5/5 차단, S1~S3·S5·R1 통과. S4 96/96 우위, R2·R3 각각 6/7로 밸런스 실패를 보존한다.
변경: 개발 기반·헤드리스 Core·콘텐츠 후보·리그·BM 조사와 R1 저장소/증거/의존성 정리를 완료했다.
보존: 원본 증거 1,399파일과 R1 실행 증거 14파일의 다운로드·추출·해시 검증을 기록했다.
선택: 기본 새싹 변경·개척 기사 1/1을 데이터로 반영하고 잔재→비옥도→작물 최소 고리와 더미 실행을 검증했다.
남은 결정: 열린 needs-decision #46·#57·#61·#65은 아래와 같으며 R2·R3 작업을 멈추게 하지 않는다.
미착수: Unity 실행·기기 검증 0건. 에디터/SDK 설치·프로젝트 생성·비밀값 등록은 하지 않았다.
전달: 최종 CI·병합 커밋·ZIP 파일 수·크기·SHA-256·무결성 판정은 별도 전달 영수증으로 제공한다.

# Phase0 종료 보고

기준일 2026-10-08. 종료 정리는 [#64](https://github.com/hyunlord/bs-mobile/issues/64), Unity 버전·기기·라이선스 계획 결정은 [#65](https://github.com/hyunlord/bs-mobile/issues/65)에서 추적한다. 아래 완료 판정은 현재 단계 보고서와 증거 원장을 읽어 옮긴 기록이다. 완료된 R2·R3 평가를 구분하며 보정 표본이나 예상 수치로 최종 결과를 대신하지 않는다. 변경 범위는 `bs-mobile`이며 Charter & Kin은 작업 대상이 아니다.

## 단계별 관문과 근거

| 단계 | 판정 | 확인한 근거·범위 |
|---|---|---|
| S0 개발 기반 | 당시 PARTIAL | 실행 관문 3/4, 의도 위반 PR 5/5 차단·미병합 종료. Projects 권한 제한은 당시 기록 그대로 보존한다. R1에서는 추가 권한 없이 라벨·마일스톤 사용을 결정하고 #2를 종료했다. [S0 보고](../review/S0-report.md), [R1 보고](../review/R1-report.md) |
| S1 문서 검토 | PASS | 심각 3·중간 7·사소 2건, 심각 3/3건에 근거와 실패 가능한 인과 기록. 게임·경제의 실행 통과와 다르다. [S1 검토](../review/S1-review.md) |
| S2 헤드리스 Core | PASS | 실행 관문 5/5. 기본·더미 × A/B/C의 6조건 × 3반복 해시 일치·4계절 완주. 지정 부하 2,700표본 p95 1.40675ms는 당시 환경의 Core 측정이다. [S2 보고](../review/S2-report.md) |
| S3 콘텐츠 풀 | PASS | 7/7, 정본 219개·반시너지 11쌍·순환 연결 130개. 전체 후보의 게임 효과나 재미가 모두 실행 검증됐다는 뜻은 아니다. [S3 보고](../review/S3-report.md) |
| S4 원본 리그 | 밸런스 FAIL / 실행 PASS | 서로 다른 입력 576사례·공유 seed 블록 32개·각 3반복. 정책·사람 규칙 간 대응 자료이며 576개 IID 표본이 아니다. 무기형이 96/96 조건에서 엄격한 레벨 우위, 모든 사례가 완주해 생존·시간은 순위를 구분하지 못했다. 25/25 효과 관측과 CSV 재현은 별도 통과다. 원본 FAIL을 수정하지 않는다. [S4 인계](../review/S4-report.md), [원본 보고](../league/S4-report.md) |
| S5 시장·BM | PASS | 7종 조사, 원본 02의 10개 본절·§2.1 대응. 출처 55개·참조 109곳 검증 당시 기록이다. 비교 가능한 경쟁작 리텐션은 미확인이고 앱 설치·결제·광고 시청은 하지 않았다. [S5 보고](../review/S5-report.md), [조사](../research/S5-bm.md) |
| R1 저장소 정리 | PASS | 측정 tracked 트리 267,845,523→2,836,181bytes. 원본 1,399파일 검증, 초과 PR 1/1 차단, 기존 의존성 PR 9/9 정리, 그룹 PR 1개 실제 자동 병합. 후속 보고·매니페스트 추가 전 측정이며 최종 트리 크기로 오인하지 않는다. [R1 보고](../review/R1-report.md), [관문 원장](../evidence/R1-gates.csv) |
| R2 S4b 통제 실험 | **FAIL(c), 6/7 PASS** | A 576·B 288사례, 각 3반복. 기준 random/C 18/32(56.25%), XP 18/18 중앙값 30–43, mixed 동일 0/96쌍. 사람형만 사람 A/B/C 세 묶음 1위. [R2 보고](../review/R2-report.md), [PR60](https://github.com/hyunlord/bs-mobile/pull/60), [최종 CI37724092248](https://github.com/hyunlord/bs-mobile/actions/runs/37724092248). |
| R3 선택 A안 | **기술 PASS / 밸런스 FAIL(c), 6/7 PASS** | .NET 138/138·콘텐츠 123/123·잔재 보고 5/5. 기본·더미 21,600틱×3회 검증과 전체 A 576·B 288사례 재실행 완료. 기준 random/C 16/32, 사람 A는 mixed·B/C는 people이 1위. [R3 보고](R3-report.md), [PR63](https://github.com/hyunlord/bs-mobile/pull/63). |

## 승인된 방향과 남은 결정

R1 보고에 #32·#42 승인 기록과 종료가 명시돼 있다. 따라서 과거 S3/S5 보고의 ‘사용자 미선택’ 문장은 당시 기록이며 현재 대기 사유로 재사용하지 않는다. 기본 영웅·영지는 새싹 변경·개척 기사 각 하나, 외형 무스탯과 Core의 데이터/ID 독립 경계를 유지한다. BM 세부 승인 내용은 원래 이슈의 승인 기록을 따르며 이 보고서가 새 가격·확률·경제식을 승인하지 않는다.

#40은 승인한 통제 실험 실행으로 종료했고 #52도 실제 자동 병합 검증으로 종료했다. 현재 미결정 항목처럼 다시 나열하지 않는다.

다음 목록은 종료 보고 작성 시 GitHub의 열린 `needs-decision` 이슈 조회 결과다. 최종 전달 직전에 상태를 다시 확인한다.

| 이슈 | 남은 판단 | 진행 원칙 |
|---|---|---|
| [#61 사람형 우위 후속 진단](https://github.com/hyunlord/bs-mobile/issues/61) | 관문에 맞춘 보정 없이 사람형 생존 우위를 설명할 통제 실험 | R2 FAIL을 유지하며 미검증 인과 가설과 실제 관측을 분리한다. |
| [#65 Unity 착수 계획](https://github.com/hyunlord/bs-mobile/issues/65) | 에디터 고정 후보·대상 기기·라이선스/CI 방식 | 계획 결정만 기록한다. Unity 설치·프로젝트 생성·1단계 구현을 승인한 것으로 해석하지 않는다. |
| [#46 메이저 의존성 통합 검토](https://github.com/hyunlord/bs-mobile/issues/46) | 메이저·분류 불명 제안의 호환성 선택 | 자동 병합하지 않는다. R2·R3와 별도 운영한다. |
| [#57 네 정책·세 사람 묶음 공동 1위](https://github.com/hyunlord/bs-mobile/issues/57) | 관문(c)의 해석 민감성 | 사전 선언된 엄격한 공동 1위를 적용한다. 결과를 본 뒤 오차 허용이나 관문을 바꾸지 않는다. |

## 증거 보관과 재현

- [원본 Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-evidence-original-20261008): 원본 1,399파일, [파일 해시](../evidence/phase0-source-manifest.csv), [자산 해시](../evidence/phase0-assets-sha256.csv). 다운로드·빈 경로 추출·크기·전체 SHA-256·정확한 파일 집합을 검증했다.
- [R1 실행 Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-r1-runtime-20261008): 실행·실패·복구 증거 14파일. [파일 해시](../evidence/R1-source-manifest.csv), [자산 해시](../evidence/R1-assets-sha256.csv).
- Release 자산은 덮어쓰지 않는 운영 규칙이다. R1 보고의 GitHub `isImmutable:false`를 숨기거나 플랫폼 불변 보장으로 표현하지 않는다. 과거 Git 이력은 재작성하지 않았다.
- R1 이후 Node 플롯은 원본 CSV에서 SVG·PNG를 생성하며 복사한 CSV만으로 24파일 바이트 일치를 검증했다. 역사적 S4 그래프와 보고서는 바꾸지 않았다.
- [R2 Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-r2-s4b-20261008): 새 다운로드·깨끗한 추출의 2,226파일·4,144,928,329bytes 정확 집합·크기·SHA-256 검증 PASS. 압축 자산 SHA-256 `d9714b3d3a87f30ac4f55d6cf7b53a32bd4574a0b37fc517798c6c7b999f200e`. [자산 해시](../evidence/R2/R2-assets-sha256.csv), [파일 해시](../evidence/R2/R2-source-manifest.csv).
- R2 단계 ZIP `/Users/rexxa/Downloads/bs-mobile-R2-20261008.zip`: 521파일·1,144,973bytes, SHA-256 `3d09f8240e8191d7ab2d7520da7cb469af17b34a53c45eabe5a520dbf931320e`. CRC·깨끗한 추출 해시·정확 파일 집합 PASS. 포장 커밋 `70ba5f5196591ab07f866ae8b2f1dd124efe2386`, 평가 소스 `bcaa00f763ec6950707635569befb558e8de1731`. 이 수치는 `bs-mobile-phase0-close-delivery-20261008.json` 기록에서 확인했다.
- [R3 Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-r3-sprout-march-20261008), [파일 해시](../evidence/R3/R3-source-manifest.csv), [자산 해시](../evidence/R3/R3-assets-sha256.csv). 실제 보관 검증 상태는 [R3 보고의 증거 절](R3-report.md#증거와-후속-인계)을 따른다.

## R3 기술 증거 — 전체 리그 판정과 분리

**프로필 인계 경계:** 동결 R2/R3 밸런스 평가는 `s4b-02`와 연결된 catalog/tuning을 사용한다(XP 계수 30/15/5, 영주 체력 1800 등). `s2-baseline`·`s4-stage-one`은 이전 tuning을 사용하는 기술/역사 실행 경로다. 아래 수동 기본 CLI의 레벨 80은 R3 밸런스 결과가 아니며 생존자 중앙값 25–45 관문과 혼합하지 않는다. Unity는 승인된 측정 프로필·catalog를 명시적으로 선택해야 하며 루트 `tuning.json`이나 현재 CLI 기본값이 S4b 수치라는 전제를 두지 않는다.

현재 증거 소스는 `d9653353f0c17fcea756910d9b1db28d400680b6`다. [R3 증거 매니페스트](../evidence/R3/R3-source-manifest.csv)에 연결한 `technical/full-default-dummy-verification.json`의 값은 다음과 같다. .NET 138 통과와 콘텐츠 123 통과는 인접 `bs-r3-all-final.log`·`bs-r3-fullcheck.log`에서 확인했다. 잔재 보고 테스트 5/5도 technical 전체 검사 로그의 실제 통과 다섯 항목에서 확인했다. 보존식·반복 차이·계측 부재·출력 provenance·동일한 오류 원장의 재현을 검사했으며 [소스 CI37725210436](https://github.com/hyunlord/bs-mobile/actions/runs/37725210436)도 PASS다. 외부 파일 매니페스트로 로그의 크기·해시를 확인할 수 있다.

| 수동 실행 | 기본 | 더미 |
|---|---:|---:|
| 틱 / 결정론 반복 | 21,600 / 3 | 21,600 / 3 |
| 수용 잔재 Created | 2,292 | 586 |
| 흡수 / 만료 / 남음 | 698 / 1,447 / 147 | 27 / 557 / 2 |
| 상한 폐기 Dropped | 0 | 1,369 |
| 비옥도 전달 / 소비 | 698 / 605 | 27 / 17 |
| 실제 성장 보너스 | 605 | 17 |
| 비옥도 사용 주기 수확 | 185 | 8 |
| 전체 수확 / 수확 XP | 1,223 / 18,345 | 367 / 5,505 |

두 실행 모두 Created=Absorbed+Expired+Active가 맞는다. 기본 ID는 `core:frontier_knight`·`core:sprout_march`, 더미는 `test:scout`·`test:moor`다. 같은 실행의 세 해시는 각각 `0898937DD92829A467B38B1C3AD8151C5999CD184CABB7FD03DB2FE2CF84DBA6`, `7EAC79C8F13127BEB789800D237D8C1DD0A3D548A6F19BD4B09FDEBBE105F6AB`로 동일하다. 전체 수확량을 잔재의 인과 기여로 귀속하지 않는다. 이는 메커니즘 수동 CLI 증거다. 별도 전체 S4b A/B 평가에서는 관문(c)가 실패했고 Release·ZIP 검증은 별도 증거를 따른다.

전체 R3 A/B 잔재 원장은 각각 생성 2,462,055/895,859개의 보존식을 확인했다. 실제 비옥도 소비는 1,534,709/594,476, 비옥도 사용 주기 수확은 367,619/117,848이다. A/B 각각 보고 출력 12파일의 CSV 전용 바이트 재현이 통과했다. 잔재 보고 자체는 원본 JSON도 확인하는 별도 검증이며 CSV-only라고 부르지 않는다. 세부 결과와 R2 대응 해석은 [R3 보고](R3-report.md)를 따른다.

## Unity 인계와 한계

[Unity 착수 체크리스트](../runbooks/Unity-phase1-checklist.md)을 함께 제공한다. 첫 줄은 ‘관문: 부분 — 0건의 Unity 실행·기기 검증’이며 처음 6줄에 상태·변경·결정·근거·한계를 담았다. Unity 6.3 LTS `6000.3.25f1`은 공식 페이지로 확인한 고정 후보이며 절대 최신 주장이나 설치 완료가 아니다. 현재 .NET 8 Core와 Unity의 .NET Standard 2.1/AOT 호환성은 선행 검증 관문이다.

첫 회색 상자는 선택 A안 1/1, 한 손 이동·자동 발동·레벨업 카드·처치 잔재에서 수확으로 이어지는 실제 고리다. 직접 건설·수동 백성 배분·광고/결제 SDK 설정으로 범위를 넓히지 않는다. 계정·라이선스·서명 비밀값은 필요한 시점에 소유자가 처리한다. 실제 기기의 조작·가독성·프레임·발열과 사람이 이해하는 첫 도구·재미는 헤드리스 결과로 증명되지 않는다. 이번 종료 작업에서는 Unity를 시작하지 않는다.

## 전달 영수증과 재개 지점

최종 포장 후 실제 CI·병합 소스·ZIP 경로/파일 수/크기/SHA-256·CRC·깨끗한 추출·정확 파일 집합은 별도 전달 영수증으로 제공한다. 보고서 자체를 포함하는 ZIP 해시를 보고서 내부에 넣는 자기 참조는 만들지 않는다. R1·R2 단계 ZIP은 최종 Phase0 ZIP을 대신하지 않는다.

Phase0의 구현·측정 작업과 제품 밸런스 통과를 구분한다. R2/R3의 (c) 실패는 숨기지 않고 #57·#61에서 후속 결정과 통제 실험으로 이어 간다. Unity는 #65의 계획 결정 및 체크리스트를 인계한 상태이며 실행 0건이다. 사용자가 시작을 지시하기 전 1단계 설치·프로젝트 생성·운영 SDK 작업을 하지 않는다.
