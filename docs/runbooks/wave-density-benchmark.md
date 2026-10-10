# 고정 고밀도 Mac 재생 벤치마크 (#169)

사용자 결정에 따라 #169는 자연 플레이의 후반400+ 도달을 기다리는 대신, 기존 C05 입력을 **Release 네이티브에서 실제 렌더링하며 재생**해 전후 비용을 비교한다. 이 성능 관문은 wave-1b 시작 조건에서 제외한다. 1b는 사용자 직접 플레이 소감 이후에 시작하며, #126 수치·스폰·경제와 Fold7 #98 보류는 유지한다. 화면 판독 관문의 일반 녹화 요건은 별개로 유지한다.

## 고정 자료와 출처

현재 측정·판정 정본은 [`benchmarks/wave-c05/contract-v2.json`](../../benchmarks/wave-c05/contract-v2.json)과 같은 디렉터리의 `run.ssreplay`다. 원래 [`contract.json`](../../benchmarks/wave-c05/contract.json)과 v1 분석기는 당시 판정을 재생성하는 역사 계약으로 보존한다. [ADR0045](../adr/0045-separate-pacing-and-stutter.md)는 상한60fps에서 p95≤16.7ms를 요구하던 기준만 대체하며, 이전 최적화·해시 검증의 유효성을 철회하지 않는다. 이 fixture는 성공 전체 실행 묶음이 아니라, 기존 중단 실행에서 사전 선정한 고밀도 성능 회귀 표본이다. 원본과 역사 판정을 수정하지 않는다.

| 항목 | 고정값 |
| --- | --- |
| 원본 | C05 / `ddd453935bfb48e6851b20ee0e0dbf87` |
| 원본 소스 | `5118e2c2a06f6449be672a77a64b61c9a349d69b` |
| 재생 SHA-256 | `f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5` |
| dataHash | `942CFEA33A25F90CBD5053D711C9BBFF1279DBE481950F2081C765B6093C3FDC` |
| 입력 | seed30001, `meta:grain`, 원본14403명령 |
| 끝 | 14400틱, Quit |
| 종결 해시 | `B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8` |

원본 앱의 `StreamingAssets/data`와 보존된 `historical-537/data`의 공통255개 파일은 일치한다. 후자에는 검사용 더미4개가 추가된다. 벤치마크는 fixture 계약의 역사 dataHash와 일치하는 카탈로그를 사용한다. 현재 실행 데이터의 해시로 재생 헤더를 바꾸거나 입력을 재생성하지 않는다.

C05 실제 실행은15389틱 이후 중단됐지만, 원본 파일은 마지막 원자적 스냅샷인14400틱까지 문법상 닫힌 Quit 재생이다. 내부 checkpoint 레코드는0개이며 종결 해시가 존재한다. 원본 CSV의 마지막15391틱 행은 부분 쓰기다. 해당 꼬리나14400틱 이후를 벤치마크에 포함하지 않는다. 오래된 `partial-disposition.json`의 “no closed replay” 문구는 실제 바이너리와 다르므로, 전체 정상 완주와14400틱 스냅샷의 유효성을 구분한다.

## 측정 계약

동일한 하네스·fixture·뷰포트1600×900에서 **상한 없음(targetFrameRate=-1, vSync=0)**과 **실제 상한 설정(targetFrameRate=60, vSync=0)**을 구분해 각각3회 실행한다. 상한 없는 결과는 Mac 회귀 추세이며, 상한 있는 결과는 끊김 관문이다. 회귀 비교에서는 코드 이외의 Mac 모델·OS·Unity·백엔드·품질·해상도·GC 모드·측정 설정을 동일하게 유지한다. 설정이 달라지면 새3회 기준을 만들며 기존 기준과 직접 비교하지 않는다. 앱 소스 SHA, 실제 데이터 해시, fixture 해시, Mac 모델·OS·Unity·백엔드·Metal·vSync·실제 해상도와 빌드의 `development:false`를 함께 기록한다.

0–7200틱 입력을 빠르게 재생해 준비한 뒤,7200틱 상태에서120렌더 프레임을 준비 구간으로 제외한다. 이후는 정상1배속30Hz 시뮬레이션과 실제 화면 렌더링이다. 준비 구간을 실제 플레이나 성능 표본으로 보고하지 않는다. 측정 틱 구간은 새 측정 전에 원본 C05 CSV만 보고 선정했다.

| 고정 측정 틱 구간 | 원본에서 관찰한 적 범위 | 대응 밀도 묶음 |
| --- | --- | --- |
| 7200–9900 | 403–582 | 400–599 |
| 10200–12900 | 605–782 | 600–799 |
| 13200–14400 | 800–876 | 800+ |

묶음은 실제 그 프레임에 렌더링한 스냅샷의 적 수로 정한다. 예상 구간 이름으로 적 수를 대신하지 않는다. 고정 구간의 모든 렌더 프레임을 기록하고 시뮬레이션이 진행하지 않은 렌더 프레임과 느린 프레임도 보존한다. 포커스 상실·일시정지·중단·계약 불일치는 실행의 유효성 문제로 명시한다. 측정이 나빠졌다는 이유로 구간·seed·해상도·프레임 제한을 바꾸지 않는다.

Unity 준비·빌드·실행은 직렬화한다. 측정 중 다른 Unity, Recorder, 프로파일러, 빌드, 테스트, 영상 인코딩이나 큰 검색을 실행하지 않는다. 앱은 전경을 유지한다. 잠자기 방지 사용 여부도 양쪽에서 동일하게 기록한다. Development 프로파일러 실행은 원인 조사용이며 Release 관문 수치와 분리한다.

## 빌드와 실행

저장소 루트에서 새 출력 디렉터리를 사용한다. 일반 실행에는 벤치마크 옵션을 켜지 않는다. Release가 기본이며 `UNITY_MAC_DEVELOPMENT=0`으로 명시할 수 있다. 아래 예의 `release-build`는 깨끗하게 커밋된 소스로 빌드한다.

```sh
UNITY_WAVE_BENCHMARK=1 UNITY_CONTENT_PROFILE=wave-1a UNITY_MAC_DEVELOPMENT=0 \
  UNITY_MAC_OUTPUT="$PWD/artifacts/wave-density/release-build" \
  bash tools/build-mac.sh

open -n "$PWD/artifacts/wave-density/release-build/Sow and Siege.app" --args \
  --wave-benchmark "$PWD/benchmarks/wave-c05/run.ssreplay" \
  --wave-benchmark-mode uncapped \
  --wave-benchmark-output "$PWD/artifacts/wave-density/uncapped/run-1" \
  -screen-width 1600 -screen-height 900
```

각 모드의 `run-1`~`run-3`을 별도 프로세스에서 순서대로 실행한다. 상한 측정은 같은 앱에 `--wave-benchmark-mode capped`와 `capped/run-N` 새 출력 경로를 사용한다. 이미 존재하는 결과를 덮어쓰지 않는다. 모드를 생략하면 이전 하네스와 같은 capped지만 현재 증거 명령에는 명시한다. 종료·실제 뷰포트·종결 해시·포커스와 `benchmarkMode`, `profilerEnabled:false`, `gcMode`를 확인한다. Development와 프로파일러가 켜진 결과는 판정 분석기가 거부한다.

각 모드의 세 반복이 모두 끝난 뒤 분석한다. 첫 상한 없는3회는 기준이며 합격 판정이 아니다. 이후 웨이브는 보존한 기준3회 디렉터리를 `--reference`로 지정한다.

```sh
node tools/summarize-wave-benchmark-v2.mjs uncapped artifacts/wave-density/uncapped
node tools/summarize-wave-benchmark-v2.mjs uncapped artifacts/wave-density/next-uncapped \
  --reference artifacts/wave-density/uncapped
node tools/summarize-wave-benchmark-v2.mjs capped artifacts/wave-density/capped
```

원래 v1 분석 명령 `node tools/summarize-wave-benchmark.mjs before after`는 기존 p9516.7ms 판정을 그대로 재생성한다. 새 기준으로 과거 capped 실행을 다시 계산할 때만 `node tools/summarize-wave-benchmark-v2.mjs capped HISTORICAL_ROOT --historical-capped`를 쓴다. 이 명시적 예외는 새 모드·프로파일러·GC 메타데이터가 모두 없던 역사 기록만 허용하고 미기록으로 표시한다. 누락 값을 프로파일러 꺼짐이나 특정 GC 설정으로 만들어 내지 않는다. 새 실행은 예외 없이 메타데이터를 기록한다.

## 현재 판정과 비용 분해

#200 이후 Core/View를 바꾸는 PR은 위3회 기준 비교에 아래 명령을 사용한다. 20% 초과 회귀는 기본 실패이며, 실제 측정과 추적 이슈를 포함한 명시적 사유가 있을 때만 `--reason`을 붙여 `explained-regression`으로 기록한다. 이를 성능 통과라고 부르지 않는다. 누락·설정 불일치·중단은 사유로 면제할 수 없다. 역사 보고의 warning 판정은 보존한다.

```sh
node tools/check-wave-regression.mjs artifacts/wave-density/next-uncapped artifacts/wave-density/uncapped
# 회귀 사유를 명시한 경우에만:
node tools/check-wave-regression.mjs artifacts/wave-density/next-uncapped artifacts/wave-density/uncapped --reason docs/review/MEASURED-REGRESSION-REASON.md
```

- 비용 추세: 상한 없는3회에서 각 밀도400–599/600–799/800+의 반복별 p95와3회 통합 p95를 기록한다. 같은 기기·설정의 기준 대비 **통합 p95 증가가20%를 초과**하면 경고한다. 정확히20%는 경고가 아니다. Mac 절대 p95 합격선은 없으며 실제 기기 성능 합격선은 보류 중인 Fold7 #98에서 다룬다.
- 끊김: 상한60fps의 **각 반복·각 밀도**에서 **33ms 초과 프레임 비율≤0.1%**, **100ms 초과 프레임0개**를 모두 만족해야 한다. 정확히33ms/100ms는 각각 초과에 포함하지 않는다. 좋은 반복이나 통합 비율로 나쁜 반복을 숨기지 않는다.
- 두 모드 모두 필요한 반복·밀도·종결 해시가 없으면 미완료다. 0명령·따라잡기·느린 프레임도 모두 남긴다. 유효하지 않은 포커스 이탈 실행을 필터링으로 되살리지 않는다.

p95는 nearest-rank로 계산한다. 서로 다른 구간의 p95를 빼거나 중첩 CPU 표본을 더해서 “남은 비용”을 만들지 않는다. 화면 구성 CPU는 GPU 렌더 시간과 다르다. GC 또는 GPU 카운터를 얻지 못하면 `unavailable`로 명시하며0으로 바꾸지 않는다. 프레임 전체 지연, 실제 시뮬레이션 처리, 화면 구성, 측정 가능한 GC를 별도로 제시하고 나머지는 미분류로 남긴다.

Core 최적화는 후보 수집과 조회·할당 비용만 바꾼다. 거리→Id 동률 정렬, 호출·이벤트 순서, 기존27000틱 재생의 두 실제 Core 대상 해시와 결정론 테스트를 보존한다. 스폰·무기·도구·성장 수치를 조정해 성능을 맞추지 않는다. 이 fixture와 계약은 이후 웨이브의 회귀 검사로 유지한다. 새 콘텐츠의 대표 부하를 더할 때에는 별도 fixture와 사전 선정 근거를 추가하고 C05를 보존한다.

보고는 판정과10줄 요약, 커밋·PR·CI 링크, 전후 표로 남긴다. 정본 CSV와 재생성 명령을 유지하며 CSV에서 재생성 가능한 그래프·JSON 보고서·전체 성공 원자료를 Release에 중복 업로드하지 않는다. 실패와 사전 선정 표본만 기존 보고 규칙에 따라 남긴다.

## Development 원인 조사와 중단 조건

Release 관문과 별도로 `UNITY_MAC_DEVELOPMENT=1`로 기존 `Game.Editor.FoundationBuild.MacDiagnostic` 경로를 사용한다. 같은 C05를 capped 모드에서 재생하고 Development 전용 `--wave-benchmark-profile` 옵션을 추가한다. Release에서 이 옵션은 거부된다. 8900·11100·13500틱부터 잡는 제한된 세 프로파일 구간은 각 밀도를 살피는 넓은 탐색 구간이다. 과거369ms·167ms 멈칫이 같은 틱에 다시 발생한다는 보장은 없으며, 다시 관측하지 못한 과거 지연의 원인을 확정하지 않는다.

```sh
UNITY_WAVE_BENCHMARK=1 UNITY_CONTENT_PROFILE=wave-1a UNITY_MAC_DEVELOPMENT=1 \
  UNITY_MAC_OUTPUT="$PWD/artifacts/wave-density/development-build" \
  bash tools/build-mac.sh

open -n "$PWD/artifacts/wave-density/development-build/Sow and Siege.app" --args \
  --wave-benchmark "$PWD/benchmarks/wave-c05/run.ssreplay" \
  --wave-benchmark-mode capped --wave-benchmark-profile \
  --wave-benchmark-output "$PWD/artifacts/wave-density/profile-run-1" \
  -screen-width 1600 -screen-height 900

UNITY_PROFILER_TRACE="$PWD/artifacts/wave-density/profile-run-1/diagnostic-SELECTED.raw" \
UNITY_PROFILER_EXPORT="$PWD/artifacts/wave-density/profile-export-1" \
  /Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath "$PWD/unity" \
  -executeMethod Game.Editor.ProfilerTraceExport.Run \
  -logFile "$PWD/artifacts/wave-density/profile-export-1.log"
```

`diagnostic-SELECTED.raw`는 실제 생성된 파일로 바꾸며 같은 이름의 메타데이터 JSON을 함께 보존한다. 프로파일 수집 종료 후 앱이 닫힌 다음 Editor 추출을 실행한다. GC.Alloc 이벤트 수·메타데이터가 제공한 할당 바이트와 GC 정지 시간을 구별한다. 경고·버퍼 부족·불완전 프레임 상관은 조사 자료로만 남기며 누락을0으로 채우지 않는다.

원인 확정에는 느린 **동일 프레임**의 틱·적 수·Unity 프레임과 지배적인 표본 마커를 연결한다. GC 정지, 셰이더 첫 컴파일, 에셋 로드, 격자 재구축 후보를 실제 마커로 비교하고 화면 구성 증가도 같은 방식으로 조사한다. 중첩 마커 inclusive 시간을 합산하거나 다른 프레임의 p95를 빼서 원인을 만들지 않는다. 명확한 원인이 있으면 좁게 수정하고 **프로파일러를 끈 깨끗한 Release**에서 capped3회를 다시 측정해 새 끊김 관문을 통과할 때 #169를 닫는다. 원인이 불명확하면 후보와 증거 한계를 보고하고 추가 최적화를 중단하며 #169는 열어 둔다. 조사 자체는 새 런타임 검증 완료나 원인 발견의 증거가 아니다.
