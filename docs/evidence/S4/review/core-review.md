관문 결과: S4 Core·데이터 독립 검토 PASS — 발견한 실행 차단 결함은 수정 후 재현 검사 통과. 리그 전체 행렬·CSV 보고서 관문은 이 검토와 별도다.

# 독립 검토 범위

2026-10-08, `/Users/rexxa/bs-mobile` 작업 트리. 원본 01 §4/§9/§15와 S4 의뢰서, Core/data/CSV 작성 계약을 대조했다. 저장소 파일은 수정하지 않았다. 독립 콘솔 fixture는 `/tmp/bs-s4-audit/`에 만들었으며 실제 `s4-stage-one` 카탈로그를 Loader로 읽고 Core 내부 상태를 통제해 전후 변화를 관측했다. 이는 정상 리그 표본이나 플레이테스트가 아니다.

## 직접 실행 결과

명령: `/Users/rexxa/.dotnet/dotnet run --project /tmp/bs-s4-audit/Audit.csproj`. 출력은 `independent-mechanics-output.txt`에 대응한다.

| 검사 | 실제 결과 |
|---|---|
| 낫 수확→헌장 연쇄 | scythe harvest=1, 적 HP100→88, WeaponDamage12. 일반 수확 추가 후 HP76, 누적24 |
| 귀환 집결 공격 주기 | tick1 적HP992, tick2도992, 다음 공격 tick31, 설정 주기30. 매 tick 피해 없음 |
| 혼합 성장 | 실제 건물HP35, 수비병1, 일꾼5→4, ToolGrowthByTarget building1/people1 |
| 진화 입력·파종 | 입력 하나 소유 시 진화0, 두 입력 소유 시1, 실제 공격 경로 밭1 생성 |
| 공격 장비 태그 | meadow_buckle 조건 아래 근접 기본10→14, 원거리10 유지 |
| 북쪽 펄스·과잉 피해 | 북쪽 HP3 적에게 두 번 발동 후 HP0, 남쪽 HP100 유지. WeaponDamage3, GrowthDamage0 |
| 헌장 비용·실체 | 영지 안 실제 식량10→6, 건물HP10→22, 강화 일꾼3. 영지 밖에서는 식사 헌장 미발동 확인 |

`dotnet test core/tests/SowSiege.Tests/SowSiege.Tests.csproj -c Release --no-build --filter FullyQualifiedName~FullRunPreservesCommittedS2GoldenHash`: 6 PASS, 0 FAIL. 기본/더미 × A/B/C의 기존 21600 tick golden을 별도로 직접 실행했다. 원래 기대값을 수정하지 않았다. 원본 출력은 `independent-s2-goldens.txt`에 대응한다.

`node tools/validate-content.mjs`: valid=true, records226, schemas12, errors=[]. Core에는 영웅·영지 ID 또는 수치 예외를 추가하지 않았다. Runtime=null 격리는 실제 여섯 golden과 별도 RuntimeCatalog 구조로 확인한다.

## 발견 및 수정 확인

1. RuntimeSystem의 전역 dispatching 플래그가 모든 중첩 이벤트를 막았다. 실제 낫으로 밭을 수확하면 수확1이지만 헌장 피해0/적HP100, 일반 수확은 피해12/HP88이었다. 현재 `RuntimeSystem.Emit`은 실행 중인 trigger 집합으로 같은 이벤트의 재귀만 막는다. 공격→수확→헌장, 수리→보호 등 서로 다른 이벤트가 동작한다. 독립 fixture 재실행으로 위 표의 피해12를 확인했다. 중첩 비용은 적용 전에 예약하고 변화가 없으면 환불한다.
2. Validator/Loader는 tool-growth/weapon 펄스와 modifier FoodCost를 허용했지만, Core는 선택된 weapon-front 및 무료 modifier만 구현했다. 현재 이 미구현 구성을 거절하도록 양쪽이 좁혀졌다. 독립 변이에서 tool-growth, weapon, modifier FoodCost2가 모두 거절됨을 확인했다. 현재 선택된 정본 동작을 임의의 전역 버프로 대체하지 않았다.
3. 지속시간 기반 물품의 중복 효용과 북/남 방향 펄스는 초기 검토에서 확인 대상으로 전달했다. 현재 Core는 extend-duty/guard-return의 Amount×DurationTicks를 넓은 정수로 계산해 상한 처리하며, 전방 펄스는 2D 내적을 사용한다. 소유자의 회귀 테스트와 독립 북쪽 HP fixture가 있다.

## 읽기 검토와 범위 제한

슬롯5/4/4와 선택된 콘텐츠 수량4/4/4/12/2/6/1/1은 별개로 유지된다. 무기·도구·헌장만 카드 후보이며 아이템은 태그·거리·비용을 만족하는 실제 월드 획득에서 무제한 스택으로 누적된다. 모든 스택이 무조건 유효한 것은 아니며 공간·자원·수치 상한은 남는다. 슬롯 초과/기소유 강화와 반복 물품 획득은 작성자의 구체적 회귀 테스트를 읽었고, 이 표에 독립 실행했다고 중복 기재하지 않았다.

헌장의 전투/영지 양 축은 직접 상태변화와 기회비용으로 구성된다. 혼합 도구는 카운터만 바꾸지 않고 건물 수리와 백성 역할 전환을 수행한다. 진화는 모든 입력 소유를 검사하고 실제 파종/보호를 실행한다. 효과의 AppliedTotal은 HP·tick·밭·위치 등 서로 다른 단위이며 한 피해 점수로 합산할 수 없다. 스킨/확장 후보의 구현을 주장하지 않는다.

추가 독립 변이 중 hedge_drum의 people 가지 삭제는 처음 Validator에서 통과했다. 실제 C# S4ContentTests의 혼합 대상 배열 검사가 전체 CI에서 이를 막으므로 전체 CI 우회는 아니다. 담당자에게 Validator의 단계별 성장 구성 검사 보강을 전달했다. 이 항목의 후속 결과는 아래에 갱신한다.

이 검토는 CSV로부터 모든 보고 수치를 재생성하는지, 전체576 독립 케이스/1728 실행이 완전한지, 지배 정책 판정이 정직한지까지 대신하지 않는다. 그 관문은 runner/report 통합 증거로 별도 확인해야 한다. 재미, 폰 화면 판독성, 첫 도구 이해도는 headless 검증 밖이다.
