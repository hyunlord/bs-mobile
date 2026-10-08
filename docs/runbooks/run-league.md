# 봇 리그 실행·해석

먼저 `./tools/check.sh`로 Release 빌드와 짧은 리그를 검증한다. 저장소 루트에서 다음 명령으로 재현한다.

```sh
./tools/league.sh smoke
./tools/league.sh stage
./tools/league.sh long
```

| 모드 | 정책 × seed × 반복 | 시뮬레이션 기간 | 용도 |
|---|---|---|---|
| smoke | 6 × 3 × 3 | 최대 900틱, 기본 30Hz에서 30초 | PR 결정론·실제 게임 경로 검사 |
| stage | 6 × 3 × 3 | JSON의 전체 기간, 기본 21600틱·12분 | S2 전체 기간 증거 |
| long | 6 × 128 × 3 | JSON의 전체 기간 | 별도 장기 리그; 실행 비용 확인 후 사용 |

시즌 길이는 짧은 smoke에서도 바꾸지 않는다. 900틱은 12분 게임을 빠르게 압축한 결과가 아니라 앞부분만 실행한 결과다. 사망하면 기간 상한보다 일찍 끝날 수 있다. 모든 실행이 12분 생존했다는 뜻으로 전체 기간 모드를 해석하지 않는다. 같은 seed·설정·정책의 세 실행은 결정론 검사이며 독립 표본 세 개가 아니다. `summary.json`에 독립 정책/seed 사례 수와 총 실행 횟수를 따로 남긴다. 사람 규칙은 JSON 기본값을 사용하며 S4 A/B/C 교차 리그를 대체하지 않는다.

단일 실행과 부하 fixture 재현:

```sh
dotnet run --configuration Release --project core/src/SowSiege.Sim -- --data data --seed 42 --policy mixed --scenario normal --iterations 3 --output artifacts/run.json --metrics artifacts/metrics.json
dotnet run --configuration Release --project core/src/SowSiege.Sim -- --data data --seed 42 --policy mixed --scenario load --iterations 3 --output artifacts/load.json --metrics artifacts/load.metrics.json
```

`artifacts/league-<mode>/`에 정책/seed별 원본 결과·측정 JSON, `outcomes.csv`, `summary.json`을 남긴다. CSV는 각 세 반복의 첫 번째 결정론 결과만 기록한다. `artifacts/metrics.json`은 HTML·장기 축적의 호환 입력이다. 틱 p95는 mixed/첫 seed 기준 실행의 값이며 리그 전체를 합친 백분위가 아니다. 정책별 평균·생존율·성장·레벨·경험치 출처는 실제 게임 결과에서 집계한다. `policyDamageCv`는 정책 평균 피해의 변동계수인 S2 진단값이고, 평균 피해가 0이면 정의되지 않아 `null`이다. `balanceDispersion:null`과 `gameplayBalanceClaim:false`를 유지하며 S4 밸런스 통과를 주장하지 않는다. S0 과거 기록의 synthetic scaffold 수치와 S2 실제 게임 값을 섞지 않는다.

## 실제 부하 측정

```sh
dotnet run --configuration Release --project core/bench/SowSiege.Bench -- --filter '*LoadedTick*' --job short
```

BenchmarkDotNet fixture는 JSON의 **적 1000·농지 300·건물 20·사람 60** 실제 엔티티를 요구한다. 매 측정 iteration에 독립 fixture를 만들고 측정 밖에서 120틱 워밍업한다. 각 워밍업 틱 및 측정 틱 전후 `Snapshot`의 실제 활성 개수를 검사하며 하나라도 다르면 실패한다. `IterationSetup`이 있는 BenchmarkDotNet iteration은 한 번의 invocation으로 실행된다. 측정은 단일 게임 틱이며, Core 부하 모드의 틱 내부 엔티티 보충과 전후 개수 검사 비용도 포함한다. 이 보수적인 비용을 정상 게임의 순수 연산 시간이라고 표현하지 않는다. BenchmarkDotNet 통계와 Sim의 직접 per-tick p95는 별개 증거이며 서로 대체하지 않는다. `--job short`는 fixture 실행 확인용으로 표본 수가 작고 짧은 iteration 경고·넓은 신뢰구간이 발생할 수 있다. 평균을 p95로 보고하거나 이 실행 하나로 안정적인 성능 관문 통과를 주장하지 않는다.

측정 환경·commit SHA·콘텐츠 버전/해시·seed·정책·사람 규칙·반복 수·워밍업·부하 개수 검증을 원자료와 함께 보관한다. 시간축 HTML은 커밋별 측정 JSON을 소비한다. 다른 기계·빌드·설정의 측정치를 동일 성능 변화로 단정하지 않는다. 코어 틱 측정은 I/O·CLI·렌더링·Unity 비용을 제외하며 모바일 실기기 성능을 증명하지 않는다. 장기 리그의 seed 수를 시간 절약 목적으로 몰래 줄이지 않는다. 시간 제한을 넘으면 미완료로 기록하고 실행 비용과 후속 계획을 남긴다.

## S4 해석 범위

S4에서는 정책×A/B/C×seed별 원본 CSV에서 보고 수치를 재생성한다. 생존율·도달 시간·경험치 출처·시간별 도구/무기 피해·체류율·사망 원인을 비교한다. 모든 조건에서 한 정책이 1위면 실패라고 쓰고 원인 가설 세 개를 낸다. 식량·자재 보유량 자체를 점수로 보상하지 않는다.

DGX는 S4 대량 리그에만 별도 폴더·낮은 우선순위로 사용하며 다른 프로젝트 관문을 방해하면 즉시 중단한다. 공개 저장소의 Actions 자체 호스팅 러너로 연결하지 않는다.

## 첫 플레이 가능판 M1

`first-playable`은 역사 production/S4와 별도인 27000틱 프로필이다. smoke는 seed9300의 900틱 18사례만 검사하므로 밸런스 관문 통과를 뜻하지 않는다. full은 사전 선언한 seed40000–40031 × 사람규칙A/B/C × 정책6의 576사례를 세 번씩 실행한다. 저장소가 깨끗한 커밋 상태여야 하며 실행기는 Release DLL을 다시 빌드하고 소스·데이터·DLL 식별자가 끝까지 같은지 검사한다. 실행 중 다른 빌드나 소스 변경을 하지 않는다.

```sh
node tools/first-playable-league.mjs smoke artifacts/first-playable-smoke 4
node tools/first-playable-league.mjs full artifacts/first-playable-candidate-01 4
node tools/first-playable-report.mjs artifacts/first-playable-candidate-01 artifacts/first-playable-report-01 full
node tools/verify-first-playable-target-parity.mjs artifacts/first-playable-parity 4
```

출력 경로는 매번 새로 지정한다. 판정 실패도 완료된 CSV·packet을 보존하며 종료 코드가 실패로 반환된다. 실행 자체가 중단된 경우 `.incomplete-*`의 `failure.json`에 완료·누락 사례가 남는다. 실패 후보를 덮어쓰거나 성공 후보로 바꿔 이름 붙이지 않는다.

`runs.csv`와 `provenance.csv`가 관문 재생성 입력이다. 기존(b)의 순위 의미와 c′의 같은 묶음 random 대비 전문 정책 생존비0.6–1.5를 유지하고, 혼합 생존수가 random 이상인 묶음이 최소2개인지 추가 검사한다. random 생존0은 평가 불가다. `weapon-sources.csv`는 과잉 요청 피해와 완료된 발동 그룹의 허공 비율을 구별한다. `runtime-effects.csv`, `evolutions.csv`, `coverage.csv`는 실제 효과·진화 시점·동작 관찰을 기록하며 단위가 다른 효과량을 합쳐 성능 점수로 쓰지 않는다.

두 타깃 검사는 서로 다른 net8.0/netstandard2.1 DLL에서 5seed의 27000틱 입력을 각각 세 번 재생하는 정확성 검사다. 무적 fixture이므로 밸런스·Unity·실기 플레이 검증이 아니다. 전체 원자료는 로컬에 보존하고, 공개 원자료는 [사전 선정 표본](../review/phase1b-first-playable.md)만 사용한다. 로컬 CSV 용량을 이유로 관측 행을 삭제하지 않는다.

첫 정식 결과는 [M1 candidate-01](../league/first-playable-m1/README.md)에 보존한다. 저장소의 작은 정본 CSV에서 관문을 다시 계산할 수 있으며, 전체 생존이라는 상한 효과를 난이도·재미의 통과로 확대하지 않는다.

## S4 실행과 원자료 재생성

S4는 별도 프로필 `s4-stage-one`을 명시한다. 기존 `smoke`·`stage`·`long` 모드는 S2 계약을 유지한다. Release 빌드 후 저장소 루트에서 실행한다.

```sh
node tools/league.mjs s4-smoke --profile s4-stage-one --workers 2
node tools/league.mjs s4-stage --profile s4-stage-one --workers 2
node tools/s4-report.mjs artifacts/league-s4-stage docs/league
```

S4 smoke는 정책 6 × 사람 규칙 3 × seed 3의 54사례를 각각 세 번 실행한다. 900틱 앞부분 검사다. S4 stage는 seed 42~73의 32개를 사용하여 576사례, 총 1728실행을 수행한다. 각 사례의 첫 반복만 통계 표본이다. `--workers`는 1~4 범위에서 정하고 측정 환경과 함께 기록한다. 병렬 수는 표본 수나 게임 설정을 바꾸지 않는다.

실행 결과의 원본 JSON과 SHA-256을 보존하고, 다음 일곱 CSV를 통계의 정본으로 사용한다.

- `runs.csv`: 사례별 종료·최종 빌드·최종 집계와 JSON 출처 해시.
- `timeline.csv`: 실제 초기·주기·종료 표본의 누적 피해와 경험치.
- `cards.csv`: 제안과 선택. 물품은 카드가 아니다.
- `effects.csv`: 선택된 효과의 실제 적용 횟수·변화량. 단위가 다른 효과끼리 합산하지 않는다.
- `loot.csv`: 실제 전리품 획득·스택 변화·식량 비용.
- `determinism.csv`: 세 반복 각각의 해시와 종료 조건.
- `metadata.csv`: 프로필·콘텐츠·소스·실행 조건·통계 규칙.

보고 생성기는 이 CSV만 읽는다. 새 빈 디렉터리에 일곱 CSV만 복사하여 같은 명령으로 다시 생성한 뒤 보고서·파생 CSV·SVG 바이트를 비교한다. 누락 사례, 종료 표본 불일치, 잘못된 반복 해시나 CSV 문법 오류는 결과를 게시하기 전에 해결한다. 검사 편의를 위해 원본을 조용히 수정하지 않는다.

그래프는 `node tools/s4-plot.mjs OUTPUT_DIRECTORY`로 Node 표준 라이브러리만 사용해 SVG·PNG를 생성한다. `--no-plots`는 계산·표·보고서만 재생성할 때 사용한다. 로컬 최종 검증은 그래프도 생성하고 눈으로 확인한다. 그래프의 `nObserved`와 `nAlive`는 시점별 모집단을 설명한다. 사망한 사례의 마지막 값을 뒤로 채우거나 시점 사이 값을 보간하지 않는다.

피해 교차는 무기와 도구의 누적 피해가 모두 양수인 실제 표본에서 도구가 무기 이상인 첫 시점이다. 관측 간격보다 정확한 교차 시각으로 해석하지 않는다. 교차가 없는 사례는 0초가 아니라 미관측이다. 아군 피해는 별도이며 경험치나 식량을 피해로 바꾸지 않는다.

지배 판정의 정렬 순서는 생존 여부 → 도달 틱 → 레벨 → 귀속된 전투 피해다. 식량·자재·영지 체류율은 순위에 넣지 않는다. 모든 A/B/C×seed 조건에서 공동 1위를 포함해 항상 1위인 정책이 있으면 실패이며, 모든 정책이 같은 점수인 경우도 예외가 아니다. 측정된 실패는 유효한 결과다. 원인 가설과 후속 이슈를 남기고 수치를 덮어쓰지 않는다.
