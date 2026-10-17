# M1-4 번개 진화 검토 후보 1

상태: 검토용 수치 가설, 사용자 영상 승인 전. #258의 무기 규모 검토이며 #126 출시 밸런스 결정이 아니다.

## 변경 범위

`data/review/m1-weapon-combat-cases.json`의 `core:storm_fork / evolved`만 `cooldownTicks: 30 → 60`으로 바꾼다. 기존 출현 주기 2틱·연쇄 대상 상한32·피해140·거리6000과 걷기 입력을 그대로 둔다. 이전에는 발동 사이에 적15마리가 추가돼 한 번에 수십 마리를 처치하는 규모를 관찰하기 어려웠다. 새 후보는 발동 사이에 최대30마리가 모일 시간을 주는 **시연 조건 가설**이다. 공격 빈도 감소가 정상 플레이에서 더 좋다는 주장은 아니다.

나머지23개 사례, production 데이터, Core 알고리즘은 바꾸지 않는다. 이전 candidate9 원자료는 보존한다. 8종 동시 성능 측정용 `m1-weapon-combined-case.json`과 combined-v3 리플레이는 독립 고정 입력으로 유지하며 재생성하지 않는다.

## 실제 Core 사건 결과

seed30001, 동일 사각 걷기, ADR0059 `MeleeAimOptions`가 켜진 v6 입력, 600틱 중300–599틱 관찰이다. 이는 미리 구성한 진화 상태이며 정상 획득 과정을 증명하지 않는다.

| 사례 | 같은 틱의 실제 처치 최댓값 | 처치 틱별 수 | 관찰 구간 전체 처치 |
|---|---:|---|---:|
| candidate9 번개 evolved | 15 | 30틱마다15 | 150 |
| M1-258 candidate1 번개 evolved | 30 | 300·360·420·480·540틱 각각30 | 150 |

`enemy-killed` 사건을 틱별로 집계했다. 구간 전체150처치를 동시 처치로 표시하지 않는다. 기록된 최대 적 스냅샷은29인데, 공격 틱에 추가된 한 마리까지 같은 틱에서 처치된 뒤 스냅샷이 찍히기 때문이다. **화면 안에 보이는 처치 수·공격의 화면 점유율·손맛은 아직 미검증**이며 네이티브 영상에서 따로 확인한다.

## 검증과 입력

- 실제 net8.0 / netstandard2.1 Core DLL의 TFM·MVID·SHA 바인딩을 확인했다.
- 새24개 사례를 각 DLL로 생성하고 내부 리플레이 검증했다. replay·case JSON·events CSV가 두 대상에서 모두 동일하다.
- 변경하지 않은23개 사례의 replay·case JSON·events CSV는 candidate9와 바이트 단위 동일하다.
- 콘텐츠 스키마 검사 통과. combined 데이터는 HEAD와 바이트 동일하며 고정 combined 리플레이를 건드리지 않았다.
- 번개 새 최종 상태 해시: `69F8F290769AD27C22BF4C7238436F454B91C5DDEAD781309963062B57E4B1A0`.
- 번개 새 replay SHA256: `4c38fa05d5bd50ae3e10e7ad8c91f14420ae1c47db81d0a1d414a8dfb26e0b0b`.

로컬 네이티브 입력: `.omo/evidence/m1-258-combat-candidate1/net8/core-storm_fork-evolved.ssreplay`와 같은 접두사의 `.case.json`. 전체 결과·출처 바인딩은 해당 후보 폴더의 `parity.json`에 있다. Unity 실행·성능 측정은 이 작업에서 수행하지 않았다.

새 디렉터리로 재생성하는 명령:

```sh
dotnet run --project core/src/SowSiege.Sim -c Release -- showcase-fixtures \
  artifacts/benchmarks/c05/data data/review/m1-weapon-combat-cases.json \
  artifacts/m1-258-combat-candidate1-fresh
```

촬영은 이 실제 사건을 사용한다. 화면에 적이나 피해를 추가해 동시 처치 규모를 부풀리지 않는다.
