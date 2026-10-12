# M1-3 실제 공격 시제품의 Core 타깃 비교

검토 전용이다. `target-parity.csv`는 동일 입력으로 실제 net8.0 / netstandard2.1 Core DLL을 실행한 25개 표본의 작은 정본 표다. 원본 C05 14400틱과 검토 8종×3단계 600틱 전부이며 선택 누락은 없다. 두 DLL의 최종 상태 해시, 24개 생성 재생의 전체 바이트, 실제 사건 CSV가 같다. 기존 `tools/verify-target-parity.mjs`의 `validateBinding`/`validatePair`로 로드 경로·TFM·MVID·DLL SHA가 서로 다른 실제 타깃임을 확인했다. DLL 지문과 입력 JSON 지문은 `target-assemblies.json`에 있다. 당시 작업 트리는 dirty였다. 따라서 sourceHead만으로 빌드를 재현했다고 주장하지 않는다.

`data/review/m1-weapon-combat-cases.json`은 모든 수치를 hypothesis로 표시한다. 같은 원본 C05 카탈로그를 순수 Core factory로 변환한다. 적 피해1은 검토장에서만 사용하며 실제 피해를 받고 무적 명령은 없다. `evolved`는 시작 시 구성한 진화 제안 상태이며 판 중 획득한 진화가 아니다. 과거 stationary candidate2/3의 저레벨 고리·포자는 후반10초 동안 실제44회 타격·0처치였다. 현재 candidate8은 전 단계 동일한 30틱 우/상/좌/하 걷기 입력과 검토장 적 속도20을 사용한다. 화살비는 잠근 적 위치에 12틱 뒤부터 순서대로 떨어지는 실제 지연 충돌이며, 종전 외향 유도탄이 아니다. `showcase-results.csv`는 candidate8의 실제 타격·처치 정본이다. 모든24개가600틱을 완료하며, 번개·창의 Lv1은 처치0을 유지한다. 처치0을 실패 사건으로 숨기거나 가짜 죽음을 추가하지 않는다.

재생성 절차(저장소 루트):

```sh
node tools/prepare-wave-benchmark.mjs --data-only
dotnet build core/src/SowSiege.Core -c Debug -f netstandard2.1
dotnet build core/src/SowSiege.Sim -c Debug
dotnet build core/src/SowSiege.CompatSim -c Debug
dotnet core/src/SowSiege.Sim/bin/Debug/net8.0/SowSiege.Sim.dll showcase-fixtures artifacts/benchmarks/c05/data data/review/m1-weapon-combat-cases.json artifacts/showcase-parity/net8
dotnet core/src/SowSiege.CompatSim/bin/Debug/net8.0/SowSiege.CompatSim.dll showcase-fixtures artifacts/benchmarks/c05/data data/review/m1-weapon-combat-cases.json artifacts/showcase-parity/standard21
```

각 CLI는 생성한 재생을 다시 검증한다. 두 출력의 `.ssreplay`, `.events.csv`, `summary.csv`를 바이트 단위로 비교한다. 두 host에 `interactive-replay artifacts/benchmarks/c05/data benchmarks/wave-c05/run.ssreplay wave-1a`를 실행하면 C05 해시 `B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8`이어야 한다. `--assembly-metadata <output.json>`으로 각 실제 Core binding도 확인한다.

이 결과는 Unity Editor·Mac 실행·IL2CPP·화면 시인성·프레임 비용을 증명하지 않는다. 전체 성공 재생·CSV·생성 그래프를 별도 배포하지 않고 정본 데이터와 명령으로 재생성한다.

`evolved-activation-kills.csv`는 candidate4의 raw event tick300~599만 센다. 처치는 같은 틱·적 ID의 마지막 양수 hit 사건에 연결한다. 즉시 공격은 source+틱, 궤도는 겹치지 않는 발동 생성 틱, 지연 충돌은 tell의 subjectId→impact의 subjectId→예약 생성 틱으로 묶는다. 여러 궤도 조각이나 연속 낙하는 한 번 발동한 수명 전체이며 한 프레임 동시 처치라고 부르지 않는다. 소환 아군의 작업 타격은 공격 발동 ID가 없으므로 별도 미귀속 수로 남긴다. 화면 안의 처치 수가 아니며, 이 표만으로 화면 절반 또는 화면에서 수십 처치가 보였다고 판정하지 않는다. `attackActivationsEmittedAtTick`은 그 틱에 새로 발동한 수로, 앞선 틱에 예약된 충돌·궤도가 처치했을 때0일 수 있다.

현재 정본은 candidate8이다. field-v2와 기존 분리v1의 폭475를 주입한다. 기본 생성1200/회수3000, 고리·포자 evolved만 생성2600/회수4000이다. 고리·포자 궤도600/700/900, evolved 조각반경400은 세로 화면 안 접근을 관찰하기 위한 가설이다. 대각 궤도 중심거리 ceil(√2×900)=1273에 조각반경400을 더한 최외곽은1673이다. 생성반경2600과의 여유927은 영주24+적대각28.3/틱의 최대 접근에서도 약17틱이다. `first-hit-latency.csv`는 각 적의 enemy-spawn부터 최초 양수 hit까지 실제 사건 틱 차이를 검증한다. 기본판·역사field-v1은 변경하지 않는다. 화면 절반·수십 처치 및 판독성은 실제 영상 판정이며 수치로 대신 통과 처리하지 않는다.

실패 후보는 로컬 원자료로 보존한다. candidate4는 먼 생성과 큰 궤도 때문에 화면 밖 처치가 많았다. candidate5는 반경1200 생성과 evolved 궤도1600/조각1200 때문에 출생 즉시 처치되어 post-tick 살아있는 적 최대치0이었다. candidate6의 생성3000도 대각 중심거리2263+조각1200=3463의 외곽 안이어서 최초 타격 지연0틱이었다. 당시 조각반경500이라는 계산은 잘못이었으며 해소 판정도 철회한다. candidate7의 생성4500/회수6000은 화면 밖 처치 위험으로 선택하지 않았다. 이력은 최종 데이터의 status에 누적하지 않는다.
