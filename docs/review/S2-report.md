관문: 통과 — S2 실행 관문 5/5, 기본·더미 × A/B/C 6조건 × 3회 동일 해시·4계절 완주, 부하 2,700표본 p95 1.40675ms ≤ 5ms.

# S2 — 결정론 헤드리스 코어

검증일 2026-10-08. 이슈 [#33](https://github.com/hyunlord/bs-mobile/issues/33), PR [#35](https://github.com/hyunlord/bs-mobile/pull/35). 실제 측정 소스 커밋은 `f0a111e7e21bcb5576f05662bf4d32d23c6a68e9`이며 최종 CLI 원자료에서 dirty=false, 소스·Core/Sim 어셈블리·콘텐츠 SHA를 보존했다. 이후 증거·보고 추가는 게임 소스를 바꾸지 않는다.

## 구현과 관문

| 관문 | 실제 증거 |
|---|---|
| 같은 seed·정책·설정 3회 동일 | seed42·mixed, 기본/더미 × A/B/C 전체 18실행. 6조건마다 3해시 일치 |
| 실제 부하 p95 | 매 틱 전후 적1,000·농지300·건물20·사람 entity60; CSV2,700행 전수 검사. p95 1.40675ms |
| 깨끗한 checkout 검사 | [GitHub CI](https://github.com/hyunlord/bs-mobile/actions/runs/37685923896) quality·secrets 통과. Release 테스트31/31, 스키마 회귀29/29, 아키텍처 위반0, 전체 형식 검사 통과 |
| 도구 양면 집계 | 씨앗·망치·나팔 각각 실제 피해와 성장 산출 양수. 직접 성장 피해·기본 아군 피해도 별도 기록 |
| 더미 확장과 Core 경계 | test:scout/test:moor가 다른 시작 도구·성장 배율로 4계절 완주. Core 숫자·콘텐츠 ID·엔진 적합성 검사 통과 |

순수 Core는 경계 지도, 공간 해시, 자동 이동·형태별 공격, 시간+번영 출현, 영주/씨앗/익은 밭/건물 목표, 4단계 작물·수확·재심기, 터·폐허·재건, 가신·백성·부대를 구현한다. 사람 A는 군대 유지, B는 노동 강화, C는 징집 최소 시간과 실제 귀환 이후 작업 복귀를 사용한다. 부대는 여러 구성원을 하나의 시뮬레이션 개체로 묶는다. 식량은 저장·새 인구 수용을 제한하고 점수로 합산하지 않는다.

레벨에는 설계 상한을 두지 않았으며 경험치로 카드3장과 데이터 희귀도를 굴린다. 슬롯이 찬 경우 보유 장비를 계속 강화한다. 수동 선택 API의 리롤·금지·고정 예산과 대기 상태도 결정론 해시에 포함한다. 기본 봇은 자동 선택한다. 정책별 인위적 피해·성장 배율은 게임에 적용하지 않는다.

## 전체 판 원자료

모든 조건이 30Hz × 720초 = 21,600틱, 네 계절을 실행했다. 반복 실행은 결정론 검증이며 독립 표본 3개로 세지 않는다. [전체 조건 요약](../evidence/S2/full/summary.json)과 같은 폴더의 results/metrics JSON에서 메타 중립·광고 없음·정확한 설정과 결과를 확인할 수 있다.

| 조건 | 틱 | 최종 레벨 | 동일한 결과 해시 |
|---|---:|---:|---|
| default-A | 21600 | 58 | 5ECF650A1CC17E4F43318FA93F1CCBE0AB1A9502F026B2EAF2E0BF15B6403DB2 |
| dummy-A | 21600 | 54 | 49CD876A2AC043114D0DFF856362CF7E1389892F17CBFB5C6E80D3EC4C2CFECA |
| default-B | 21600 | 63 | A8E97DE9C3A4DE366D209E01C94366746D21495BFF6D16580833DE518D5B6806 |
| dummy-B | 21600 | 54 | 123CF41134F5D1D3B6925AB970A30A984B85CA8079FB2043D8CAFC1C246B0C76 |
| default-C | 21600 | 60 | 87D5616F91E7863AE9BD8AB57A23E6318035EB4ACA4F5A0470214CDBFC474C4A |
| dummy-C | 21600 | 54 | A16A9F0CEE46D1DC5D43EC450D030302730AE29AC4D7654662E742FA2A2F0358 |

### 도구 양면: 기본 C 조건의 첫 실행

| 도구 | 발동 피해 | 성장 산출 | 성장물 직접 피해 | 발동 수 |
|---|---:|---:|---:|---:|
| core:carpenter_hammer | 67 | 85 | 22633 | 466 |
| core:muster_horn | 41924 | 603 | 3678 | 702 |
| core:seed_bag | 4034 | 19 | 0 | 720 |

성장 산출 단위는 심은 농지·수리/건설·소집/작업 강화 등으로 서로 다르다. 이를 피해로 환산하지 않는다. 씨앗의 성장물 직접 피해가0이어도 수확 경험치·식량이 실제 발생한다. 경험치로 강화한 무기 피해를 다시 도구 피해로 넣지 않는다. 이 실행의 무기 피해 27072, 기본 아군 피해 10, 총피해 99418는 별도 원장이다. 원자료 [default-C.results.json](../evidence/S2/full/default-C.results.json).

## 부하와 측정 한계

Apple M4 Max, macOS26.4.1/Darwin25.4.0, .NET8.0.31 Arm64, SDK8.0.425. 300틱 워밍업을 제외하고 900틱 × 3회 = 2,700개 Simulation.Tick 시간을 Stopwatch로 측정했다. p95는 정렬한 원본에서 nearest-rank 95백분위다. 초기화·스냅샷 조회는 측정 밖, 틱 끝의 fixture 보충·정리 비용은 안이다. [원본 CSV](../evidence/S2/load/ticks.csv), [측정 메타](../evidence/S2/load/metrics.json), [전수 재계산](../evidence/S2/load/verification.json).

사람60은 실제 시뮬레이션 entity 수다. 부대 구성원 총수는60~116명으로 별도 보고한다. 부대 복귀로 개체 수가 늘면 fixture가 초과분을 정리한다. 정상 게임은 이 부하 복구·영주 체력 복원을 사용하지 않는다. 5ms는 헤드리스 임시 목표이며 모바일 렌더링·발열·터치 성능을 증명하지 않는다.

BenchmarkDotNet ShortRun의 별도 단일 틱 측정은 평균898.5μs, 표준편차57.55μs, 할당486.29KB/operation이었다. 이는120틱 워밍업 뒤 특정 상태를 측정한 평균이며 위900틱 구간 p95와 같은 통계가 아니다. 표본3개의 짧은 실행으로 안정적인 장기 성능을 주장하지 않는다. [BDN 원본](../evidence/S2/benchmark/GameplayBenchmarks-report-github.md).

## 재현

```sh
./tools/check.sh
node tools/verify-s2.mjs artifacts/s2-gate
# Release 빌드 후, PATH에 dotnet 추가
dotnet core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll --seed 42 --policy mixed --people-rule C --scenario load --duration-ticks 900 --warmup-ticks 300 --iterations 3 --output artifacts/s2-load/results.json --metrics artifacts/s2-load/metrics.json --timings artifacts/s2-load/ticks.csv
node tools/verify-s2-load.mjs artifacts/s2-load
```

숫자 재검산만 하려면 `node tools/verify-s2-load.mjs docs/evidence/S2/load`를 실행한다. 운영체제·부하 상태에 따라 새 측정 시간은 달라질 수 있다.

## 수정 기록과 남은 범위

[독립 재현 기록](../evidence/S2/independent-review.md)은 넉백 공간 인덱스 누락, B 성장 허수 집계, 사망 원인 덮어쓰기, 부대 귀환 부하 초과, 귀환 체력 생성의 재현·수정 결과를 담는다. [통합 검토](../evidence/S2/integration-review.md)는 부대 표현·사람 손실·아군 피해 귀속·지표 provenance 분리를 설명한다. 전체 검사의 서식 오류도 교정 후 재실행했다.

현재 수치는 가설이며 S4 밸런스 검증 전이다. 이 seed의 완주가 모든 seed·정책의 생존 또는 재미를 증명하지 않는다. 기본 영웅·영지 정체와 상품 선택은 [#32](https://github.com/hyunlord/bs-mobile/issues/32)에서 미정이다. 지도 이벤트·메타·상품·그래픽은 이번 S2 범위에 없다. S3 콘텐츠 풀 및 S4의 특허장·물품·진화 연결은 후속 작업이다. 무제한 레벨은 별도 설계 cap이 없다는 뜻이며 정수 표현 범위를 무한 수학 정수로 주장하지 않는다.

원격 리뷰의 식량 비용 경계 오류도 수정했다. 인구 구성원 수에 FoodPerPerson을 곱하고 long으로 중간 계산한다. 추가 회귀2건이 실패를 재현한 뒤 전체31건이 통과했다. 기본값1에서는 이전6조건 해시가 모두 그대로이며, 최종 소스 f0a111e로 전체 판·부하·BDN을 다시 실행했다. 수정 전 공개한 측정 원자료는 pre-food-fix-61bb726.tar.gz에 보존했다.
