# M1-3 실제 공격 시제품의 Core 타깃 비교

검토 전용이다. `target-parity.csv`는 동일 입력으로 실제 net8.0 / netstandard2.1 Core DLL을 실행한 25개 표본의 작은 정본 표다. 원본 C05 14400틱과 검토 8종×3단계 600틱 전부이며 선택 누락은 없다. 두 DLL의 최종 상태 해시, 24개 생성 재생의 전체 바이트, 실제 사건 CSV가 같다. 기존 `tools/verify-target-parity.mjs`의 `validateBinding`/`validatePair`로 로드 경로·TFM·MVID·DLL SHA가 서로 다른 실제 타깃임을 확인했다. DLL 지문과 입력 JSON 지문은 `target-assemblies.json`에 있다. 당시 작업 트리는 dirty였다. 따라서 sourceHead만으로 빌드를 재현했다고 주장하지 않는다.

`data/review/m1-weapon-combat-cases.json`은 모든 수치를 hypothesis로 표시한다. 같은 원본 C05 카탈로그를 순수 Core factory로 변환한다. 적 피해1은 검토장에서만 사용하며 실제 피해를 받고 무적 명령은 없다. `evolved`는 시작 시 구성한 진화 제안 상태이며 판 중 획득한 진화가 아니다. 과거 stationary candidate2/3의 저레벨 고리·포자는 후반10초 동안 실제44회 타격·0처치였다. 현재 candidate4는 전 단계 동일한 90틱 우/상/좌/하 걷기 입력과 검토장 적 속도20을 사용한다. 화살비는 잠근 적 위치에 12틱 뒤부터 순서대로 떨어지는 실제 지연 충돌이며, 종전 외향 유도탄이 아니다. `showcase-results.csv`는 candidate4의 실제 타격·처치 정본이다. 모든24개가600틱을 완료하며, 검·번개·창·화살의 Lv1은 처치0을 유지한다. 처치0을 실패 사건으로 숨기거나 가짜 죽음을 추가하지 않는다.

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
