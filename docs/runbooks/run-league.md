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
