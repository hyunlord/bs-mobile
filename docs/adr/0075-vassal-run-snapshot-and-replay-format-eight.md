# ADR0075: 고정 가신 snapshot과 replay format8

- 상태: 제안 — format8·독립 블록·능력 상한은 사용자 승인, 아래 wire 및 재실행 복구는 구현 구체화
- 날짜: 2026-10-29
- 연결: [#527](https://github.com/hyunlord/bs-mobile/issues/527), [승인 설계 r2](https://github.com/hyunlord/bs-mobile/blob/25b49d0293b8f4b26755ec0e6db8f8d9836b20fb/docs/design/gdd/13-vassal-combat-proposal.md), [ADR0053](0053-explicit-meta-claims-and-wave-accounting.md), [ADR0069](0069-opt-in-experiment-data-outside-the-data-hash.md), [ADR0071](0071-account-estate-state-and-save-version-six.md)

## 맥락

wave는 계정 가신을 전투에 투영하지 않는다. 역사 first-playable의 `MetaRunAdapter.ProjectCatalog`는 가신 외에도 챕터·적·지도·카드를 변경하므로 새 경로에 재사용하지 않는다. 이 ADR은 ADR0053의 wave 메타 미투영 경계에 **검증된 출정 가신 snapshot만** 예외로 추가한다. 계정 저장 format6, 영지 효과, 역사 리플레이의 의미는 변경하지 않는다.

근거 코드는 `5a685320`이다. `PortableStateCodec`은 해시용 단방향 writer이며 world decoder가 아니다. `MetaReplayContext.Restore`는 계정 복원이고, `ReplayRunner.Verify`는 처음부터 입력을 재실행하는 검증기다. 새 나머지의 복구를 기존 world decoder가 있는 것처럼 설명하지 않는다.

2026-10-29 사용자 추가 결정: FP01은 역사 해시·실행 회귀만 검사한다. 새 가신 효과 비교는 C05 파생 입력과 무적 없는 정상 wave 입력에서 한다. FP01의 legacy 농지·사람·XP를 wave 능력에 대응시키지 않는다.

## 결정

### 1. 명시적 opt-in과 출정 고정

`InteractiveOptions` 끝에 nullable 가신 snapshot을 추가한다. null은 기존 코드 경로이며 enabled 빈 편성은 별도 on 상태다. 가신 옵션은 WaveRuntime이 있는 판에만 허용한다. 데이터 및 스키마는 `data/opt-in/` 안에 두고 기본 데이터 트리·카탈로그에 섞지 않는다.

출정 전 계정의 해금, 유일 ID, 슬롯, 레벨·승급을 검증한다. 잘못된 편성을 자동 절삭하지 않는다. 슬롯은 `min(3,1+floor(병영 효과레벨/4))`, 레벨 상한은 `min(20,5+2×대장간 효과레벨)`, 승급 상한은 5다. ID를 ordinal 정렬하고 ID/level/rank, 유효 슬롯·레벨 상한과 그 규칙 정본근거 hash, 규칙 버전·content SHA256, 여섯 효과의 raw/capped 값, 산술/나머지 버전을 복사해 고정한다. 정의와 재계산 결과가 다르면 거부한다.

판은 시작 시 검증한 정의 사본만 소유하며 live 계정·지갑·편성·catalog를 재조회하지 않는다. 판 중 강화·공사 완료·편성 변경은 다음 출정에만 반영한다. 무료/유료 출처에 따른 계수 차등·전용 자격 hash·원장은 만들지 않는다. 간접 성장 허용은 결제·광고 SDK 구현 승인이 아니다.

### 2. 정수 효과와 적용 순서

모든 신규 산술은 checked Int64 중간값과 정수 나눗셈을 사용한다. 음수·오버플로를 clamp로 숨기지 않는다. 능력별 합산 후 cap을 적용한다.

| 능력 | B/S/R permille [가설] | 이 버전 강제 cap | 적용 지점 |
|---|---|---:|---|
| Attack | 10/3/6 | 100 | 영주 최종 양수 피해 후보, 적 HP clamp 직전 |
| Health | 20/5/10 | 150 | 가신 제외 최대 HP에 한 번, 회복·부활 상한도 동일 |
| Movement | 5/1/3 | 50 | 가신 제외 영주 이동속도에 한 번 |
| Growth | 10/3/5 | 100 | 기존 모든 gate 통과 후 grain 진행량만 |
| Allies | 15/4/8 | 150 | WaveGroup 생성 최대 HP와 최종 피해 각각 한 번 |
| Experience | 5/2/4 | 80 | 적 처치 및 완료 보상 생성 때 한 번 |

`raw=B+S×(level−1)+R×rank`, `bonus=min(cap,sum(raw))`다. 피해·HP는 기존 산술이 끝난 값에 `floor(base×(1000+bonus)/1000)`을 적용한다. 양수 피해만 최소 1이며 기존 0/무효 피해를 새로 만들지 않는다. 이미 가신 배율이 적용된 값을 다시 증폭하지 않는다. 독립 건물 고정 피해·환경 피해·legacy 사람은 제외한다.

**이동 산술 보수안(2026-10-29 사용자 결정):** 기존 아이템·입력 정규화·Feel 13/10 및 그 나머지 계산을 먼저 그대로 수행한다. 그 결과의 축별 이동 크기에 마지막으로 `floor(abs(delta)×(1000+bonus)/1000)`을 한 번 적용하고 원래 부호를 복원한 뒤 기존 지도 경계로 제한한다. 좌우 방향 편향을 만들지 않도록 음수 좌표값 자체를 floor하지 않는다. Feel 나머지의 분모·단위는 바꾸지 않고 가신 이동용 새 나머지도 만들지 않는다. 작은 이동량에서는 가신 효과가 내림으로 사라질 수 있으며 이는 보수안의 알려진 양자화 한계다. 합성 분수 누적이나 기존 속도 사전 증폭은 기존 계산 순서·나머지 계약을 바꾸므로 채택하지 않았다. 가신 블록이 없으면 이 후처리도 실행하지 않는다.

Growth/XP는 `numerator=base×(1000+bonus)+remainder`, 지급량 `numerator/1000`, 새 나머지 `numerator%1000`이다. Growth는 살아 있는 grain entity ID별로 보존하고 수확·적 소비 등 해당 주기 종료 때 폐기한다. 다음 ID에 넘기지 않는다. 현재 wave의 단일 성숙 구간을 legacy 다단계 성장으로 확장하지 않는다. XP는 고정 lord와 enemy/growth/irrigation/shipment/mission 범주별로 분리한다. 중복 완료 거부 이후에만 나머지를 진행시키고, Collect는 저장된 보상만 지급한다. 기존 HarvestExperience 집계는 유지한다.

WaveGroup 최대 HP는 생성 시 한 번 계산해 별도 opt-in 상태에 ID와 기준 버전으로 보존한다. idle 재사용·귀환·훈련에 재증폭·무료 회복이 없고 식량 회복은 그 최대값까지다. 영주 사망·부활·레벨업·시즌 전환은 살아 있는 개체/XP 나머지를 초기화하지 않는다.

### 3. replay format8 wire 계약

가신 snapshot이 있을 때만 format8을 고른다. 없을 때 버전 선택과 format1~7 직렬화는 원래대로다. 기존 format7 estate schema1/2를 확장하거나 가신 정보를 estate 블록에 넣지 않는다.

format8은 기존 공통 헤더 다음에 기존 선택 기능(Feel/Field/HostRewards/Estate/Melee)의 존재와 해당 payload를 명시하고, 마지막에 길이 제한된 독립 가신 블록을 둔다. 기존 기능의 버전·상호 조합 검증을 유지하며 format8을 이유로 review 전용 조합을 넓히지 않는다. Host 명령은 format8 번호만으로 허용하지 않고 해당 옵션 존재를 확인한다.

블록은 `vassal-run-v1`, schema 1, uint32 flags, 규칙 ID/버전·content SHA256·산술/나머지 버전, 상한 근거, 정렬된 roster, 고정 순서의 여섯 raw/capped 효과를 기록한다. bit0=enabled는 필수이며 **bit1~31은 모두 0**이다. 문자열·배열은 길이 상한을 검사하고, 미지 tag/schema/flag·중복/비정규 순서·범위 밖 값·누락·재계산 불일치·hash 불일치·남는 바이트는 거부한다.

format8을 모르는 reader는 unknown version으로 실패한다. snapshot을 버리고 format7로 낮춰 재생하지 않는다. enabled 빈 roster도 블록과 버전이 남는다. 계정 MetaSaveCodec format6은 변경하지 않는다.

### 4. 상태 hash와 저장·복구

기존 options/world/group writer에 무조건 null/false/maxHP 필드를 추가하지 않는다. 가신이 있을 때만 기존 상태 뒤에 버전 tag와 확장 payload를 추가한다. payload는 snapshot 정본 hash, entity ID ordinal 순서의 grain 나머지, 고정 범주 순서의 XP 나머지, group ID 순서의 최대 HP·기준 버전이다. 나머지는 0..999이며 죽은/없는 농지의 고아 키를 거부한다. enabled 빈 편성과 absent의 hash는 구별한다.

**복구는 검증된 명령 재실행 방식**으로 구현한다. 일반 world 역직렬화기는 추가하지 않는다. 저장 단위에는 원본 format8 header/snapshot bytes, 고정 catalog/data identity, 시작부터의 명령 prefix, frontier sequence/tick/전체 state hash, canonical 가신 확장 bytes를 모두 넣는다. 저장 시 prefix와 현재 session의 일치를 확인한다.

복구 시 원본 정의와 snapshot으로 session을 만들고 명령 prefix를 재실행한 뒤 frontier와 가신 확장 bytes를 완전히 대조한다. 일치한 **계속 실행 가능한 session**을 반환한다. 불일치한 나머지를 저장값으로 덮어써 통과시키지 않는다. 누락/중복 명령, sequence/tick 불일치, 불가능한 entity/group 참조, 버전·hash 불일치는 실패다. 진행 중 저장을 가짜 terminal replay로 표현하지 않는다. live 계정 재계산은 금지다.

이는 저장된 나머지·미수집 보상·적용 maxHP를 똑같이 재현하는 복구이지 직접 world decode가 아니다. 복구 시간은 입력 길이에 비례한다. hash는 손상·불일치 검출이며 인증된 부정행위 방지가 아니다.

## 결과·비용

- ADR0053의 opt-in 예외 외 기존 규칙은 유지한다. 새 소환체·계정 이관·가격·SDK는 없다.
- 새 on 경로의 종료 hash는 새 fixture로 관리한다. 역사 기대값을 다시 고정하지 않는다.
- 여섯 효과를 동시에 켠 최대 편성은 불법이다. 비교마다 최대 3명과 실제 snapshot을 남긴다.
- FP01 무적 결과와 C05 고정 Quit를 정상 생존율로 해석하지 않는다. 정상 wave A/B는 별도 seed/입력 표본이며 성능은 Claude Code 대기열의 실제 실행만 인정한다.

## 거절한 대안

- format7 schema3 또는 estate 필드 재해석: 역사 계약을 변경한다.
- wave에 ProjectCatalog 재사용: 챕터·카드 등 승인 밖 규칙까지 투영한다.
- 새 필드 기본값을 모든 state bytes에 추가: absent 역사 hash를 변경한다.
- 전체 world decoder 추가: 기존 구조에 없고 이번 가신 범위를 넘긴다.
- 확장블록만 저장하고 live 계정으로 재시작: 원래 판·나머지를 복구하지 못한다.
- FP01에 wave 여섯 효과를 억지 대응: 사용자 확인 후 역사 회귀만 수행하기로 결정했다.

## 검증 계획과 현재 상태

Core 구현 및 집중 검사는 진행했으며 실제 실행 범위·미완료 항목은 [검증 기록](../review/vassal-run-527.md)에 구분한다. 다음은 최종 관문이며 Unity·성능을 Core 검사로 대체하지 않는다.

1. format1~7 원본 bytes·실행 경로 음성 대조, 기존 golden·crowd-411·C05·FP01 종료 hash 불변.
2. 슬롯/레벨/승급/중복·empty enabled·계수 cap·버전/hash/unknown bits/trailing bytes 거부.
3. 기존 배율 뒤 단 한 번 적용, 건물/환경/legacy 제외, 작은 값 누적·중복 보상·농지 수명·아군 재사용/식량 회복.
4. live 계정/정의 변경에도 snapshot 불변. 나머지/maxHP 변조가 hash를 바꿈. 저장→복구→다음 입력이 uninterrupted 실행과 일치.
5. estate off/on × 가신 off/on 조합, 두 Core 타깃 및 Unity 실행은 실제 수행 범위만 보고.
6. 효과 검증 뒤 UI 미적용 안내를 제거하고 실제 raw/capped 값과 상한 도달을 표시. 포트레이트 승인 자산 사용.
