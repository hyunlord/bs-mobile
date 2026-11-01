# ADR0083 PR-A 착수 기록 (#629)

- 날짜: 2026-11-02
- 상태: **부분 — 역사 호환 테스트 작성 단계. 빌드/테스트/제품 구현 NOT_RUN.**
- 기준: `f762eaf5242f777cacb07f7d82e8be32ee9a00c0` (확인 당시 main).
- 승인 계약: [PR #627](https://github.com/hyunlord/bs-mobile/pull/627)의 `bf59be264a0216b7cb52eb3aa7d332313dd22947`, ADR0083 승인 v2.
- 추적: [#629](https://github.com/hyunlord/bs-mobile/issues/629). server/ 및 unity/ 변경 없음.

## 읽은 계약과 구현

AGENTS.md, 공유 Mac 및 콘텐츠/이슈 런북, C05 런북, ADR0083 전체, ADR0075 두 문서, ADR0077 §13, ADR0079, GDD15, GDD18 §5와 worldEncounters를 확인했다. 의뢰의 `docs/design/wave-density-benchmark.md`는 없으며 정본은 [runbooks 경로](../runbooks/wave-density-benchmark.md)다.

ReplayCodec 및 가신 envelope, InteractiveOptions, VassalRunContract, VassalRecoveryCodec과 서버 RunVerifier를 읽었다. 서버의 현행 개인 개척전 생존 판정을 월드 목표 판정으로 재사용하지 않는다. 현행 Core 대상은 net8.0/netstandard2.1이고 서버 .NET 10과 구별한다.

## PR-A wire 설계 (구현 전)

모든 정수는 기존 BinaryWriter와 같은 little-endian이다. 문자열은 기존 헤더의 `u8 present (0 또는 1)`, 존재 시 `i32 UTF8 byte length`, UTF8 bytes를 사용한다. 가변 문자열 때문에 뒤 필드의 절대 offset은 고정하지 않는다.

| 순서 | 바이트 배치 |
| --- | --- |
| 공통 preamble | Text(magic), i32 format=9, i32 rules=1, i32 stateCodec=1, Text(RNG) |
| 기존 기본 입력 | i32 seed, Text(hero/estate/policy/peopleRule/scenario), u8 manualCards, Text(movement), i32 aim, Text(dataHash) |
| 새 envelope | i32 schema=1 |
| 선택 옵션 | Text(TargetMaterial), u8 Feel 존재 및 i32 flags, u8 Field 존재 및 i32 version, u8 HostRewards 존재 및 기존 host payload, u8 Estate 존재 및 version/loadout ID/hash, u8 MeleeAim 존재 및 i32 version |
| 가신 | u8 존재, 존재 시 i32 length 및 기존 VassalRunCodec bytes |
| 월드 | 필수 i32 length 및 canonical world-combat-v1 bytes |

선택 옵션 존재 표시는 각 payload 앞에 하나씩 둔다. format8 schema1 및 format7 schema1/2 writer를 확장하지 않고 새 partial codec으로 분기한다. disabled Feel은 기존 Normalize를 거친다. unknown flag/schema, 비정규 boolean/문자열, 중복/역순 ID, 음수/overflow, 잘림·과대 length·trailing bytes는 거부한다. count/length는 할당 전 남은 bytes와 프로토콜 상한을 함께 검사한다.

월드 종료는 별도 record tag(기존 1/2/3과 겹치지 않는 4), i32 payload length, Text(world-end-v1), i32 schema=1, 최종 sequence/tick, terminal, 목표 결과, ordinal N/K, state hash로 설계한다. legacy ReplayEndKind 의미를 바꾸지 않는다. 블록의 상세 제한과 모든 provenance 필드는 구현 PR의 ADR 구현 절에서 확정한다.

복구는 새 `SowSiege-world-recovery-v1` magic/version을 사용한다. 완전한 format9 header, catalog identity, 명령 prefix, frontier 및 world runtime bytes를 재실행과 대조한다. 가신 활성 시 가신 runtime도 대조한다. VassalRecoveryCodec은 format8만 받는 기존 조건을 유지한다.

## 선행 absent 관문

1. format1 기본, 2 target, 3 Feel, 4 Field, 5 HostRewards, 6 MeleeAim의 헤더와 command/checkpoint/end 포함 전체 bytes.
2. format7 estate schema1/2, format8 enabled-empty/실제 roster, estate/melee 유무 조합. 월드 absent에서 기존 형식 선택 불변.
3. 변경 전 SHA에서 초기/진행 후 state bytes 및 가신 payload를 별도 고정. production writer로 기대값을 즉석 생성하는 roundtrip만으로 고정 관문을 대체하지 않음.
4. 기존 C05/FP01/estate-v7 및 wave-history 전체 replay byte 비교, S2/wave golden, data tree hash의 opt-in 제외.
5. 2026-11-02 사용자 정정: crowd-411 별도 원본 전달은 필요 없다. [PR #609 Unity 실행 절차](https://github.com/hyunlord/bs-mobile/pull/609#issuecomment-6540285256)를 따라 커밋을 고정한 자기 새 worktree에서 잠금을 잡고 `tools/unity-tests.sh`로 first-playable/wave-1a EditMode·PlayMode를 실행한다. `CrowdReplayBenchmarkTests`가 두 프로필 모두 실제 실행되고 skip 없이 통과하며 고정 종결 해시 계약을 확인하는 것을 이번 crowd-411 기준선 관문으로 삼는다. 이를 별도 원본 전체 재생 또는 실시간 성능 측정이라고 보고하지 않는다.
6. 두 실제 Core DLL 비교와 Unity 두 프로필 EditMode/PlayMode를 각 실행 결과로 기록.

이 선행 관문을 실행해 PASS하기 전 format9 제품 코드를 추가하지 않는다. 현재 시간대 제약으로 테스트 실행 0건이며 state byte baseline도 아직 수집하지 않았다.

## 분할과 가정

- PR-A: codec/schema/recovery 경계. PR-B: 시뮬레이션의 실제 사망/이동 사건과 목표·상비 슬롯·공통 효과·결과 연결. PR-C: 별도 army-auto-v1 정수 동시 라운드와 손계산 fixture. A만으로 플레이 가능이나 월드 전투 완료를 주장하지 않는다.
- inputGraceMilliseconds 및 chunk 상한은 opt-in 데이터로 고정하되 UTC 수신/만료/경합은 서버 소유다. Core는 UTC/계정/DB를 읽지 않는다.
- 자동 전투 계수는 미확정이며 구현 예제의 숫자를 출시 밸런스로 주장하지 않는다.
- CI의 unity-test-record는 unity/ 변경이 없으면 기록 검사를 생략한다. 이것은 의뢰의 Unity 호환 관문을 면제하지 않는다.
- 각 PR의 C05 요청에는 정확한 head/merge-base 및 'Core 산술·codec, View 무변경'을 남긴다. 지금은 제품 후보가 없어 측정 요청 전이다.
- 사용자 승인, C05 3:3, CI 및 앞 세션 병합 창 종료 댓글을 확인하기 전 병합/자동병합 예약하지 않는다.

## 현재 검증과 다음 실행

야간 23:00~07:00에는 빌드·dotnet test·Unity를 실행하지 않는다. 07:00 이후 첫 작업은 자기 잠금과 측정 앱 종료 확인 → absent 불변 16사례 → Core 전체 테스트 → 고정 커밋의 자기 새 worktree에서 Unity 두 프로필(EditMode/PlayMode, crowd-411 포함) 순서다. 기존 SDK 10.0.403은 읽기 전용으로 사용한다. #609는 실패 0이어도 skip 2건으로 스크립트 exit 1이었으므로 이를 무조건 PASS 선례로 쓰지 않고 이번 실행의 exit code·실패·skip 사유를 그대로 기록한다. 기준선 관문 통과 후 제품 구현에 착수한다. baseline 기대값은 새 구현에 맞춰 재생성하지 않는다. 같은 실패 두 번 또는 역사 해시 변경 필요 시 중단한다.
