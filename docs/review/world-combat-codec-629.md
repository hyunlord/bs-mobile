# ADR0083 PR-A 착수 기록 (#629)

- 날짜: 2026-11-02
- 상태: **부분 — 기준선 absent 16/16·Core 902/902·crowd 26/26·Unity 보충 2/2 통과. PR-A 제품 코드 작성·push, 후보 테스트 검증 진행 중.**
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

이 선행 관문을 실행해 PASS하기 전 format9 제품 코드를 추가하지 않는다. 아래 낮 실행 기록이 최초 야간 NOT_RUN 상태를 대체한다. 별도 state byte baseline은 아직 수집하지 않았다.

## 분할과 가정

- PR-A: codec/schema/recovery 경계. PR-B: 시뮬레이션의 실제 사망/이동 사건과 목표·상비 슬롯·공통 효과·결과 연결. PR-C: 별도 army-auto-v1 정수 동시 라운드와 손계산 fixture. A만으로 플레이 가능이나 월드 전투 완료를 주장하지 않는다.
- inputGraceMilliseconds 및 chunk 상한은 opt-in 데이터로 고정하되 UTC 수신/만료/경합은 서버 소유다. Core는 UTC/계정/DB를 읽지 않는다.
- 자동 전투 계수는 미확정이며 구현 예제의 숫자를 출시 밸런스로 주장하지 않는다.
- CI의 unity-test-record는 unity/ 변경이 없으면 기록 검사를 생략한다. 이것은 의뢰의 Unity 호환 관문을 면제하지 않는다.
- 각 PR의 C05 요청에는 정확한 head/merge-base 및 'Core 산술·codec, View 무변경'을 남긴다. 지금은 제품 후보가 없어 측정 요청 전이다.
- 사용자 승인, C05 3:3, CI 및 앞 세션 병합 창 종료 댓글을 확인하기 전 병합/자동병합 예약하지 않는다.

## 현재 검증과 다음 실행

야간 23:00~07:00에는 빌드·dotnet test·Unity를 실행하지 않는다. 07:00 이후 첫 작업은 자기 잠금과 측정 앱 종료 확인 → absent 불변 16사례 → Core 전체 테스트 → 고정 커밋의 자기 새 worktree에서 Unity 두 프로필(EditMode/PlayMode, crowd-411 포함) 순서다. 기존 SDK 10.0.403은 읽기 전용으로 사용한다. #609는 실패 0이어도 skip 2건으로 스크립트 exit 1이었으므로 이를 무조건 PASS 선례로 쓰지 않고 이번 실행의 exit code·실패·skip 사유를 그대로 기록한다. 기준선 관문 통과 후 제품 구현에 착수한다. baseline 기대값은 새 구현에 맞춰 재생성하지 않는다. 같은 실패 두 번 또는 역사 해시 변경 필요 시 중단한다.

## 2026-11-02 낮 기준선 진행

- 기준 코드: `b66e85ff3e682b51229c147b81296d848b351e4b` (원격 push 확인).
- 14:34 첫 확인 시 `hero #633 full Unity record (pid 72113)` 잠금과 살아 있는 소유 프로세스를 확인하고 대기했다.
- 14:45:32 자기 잠금 획득 후 첫 absent 시도는 신규 테스트 중첩 foreach의 IDE0011 두 건으로 빌드 실패했다. 테스트 실행 전 실패이며 기존 코드/정책은 수정하지 않았다. 중괄호 수정 후 다시 실행했다.
- absent: **16 passed / 0 failed / 0 skipped**. Core 전체: **902 passed / 0 failed / 0 skipped**, 테스트 출력 기간 55초. SDK 10.0.403의 CLI를 직접 호출했고 실제 대상은 net8.0이다. 런타임 major roll-forward 설정을 사용했으며 Unity 또는 netstandard2.1 실행 증거가 아니다.
- 명령: `dotnet <SDK10.0.403>/dotnet.dll test core/tests/SowSiege.Tests/SowSiege.Tests.csproj --configuration Release`에 absent는 `--filter FullyQualifiedName~WorldCombatAbsentCompatibilityTests`, 전체는 `--no-build`를 적용했다. 각각 TRX 기록을 남겼다.
- 자기 원자료: `artifacts/world-combat/absent-attempt1-ide0011.log`, `absent.log`, `core-all.log`, `baseline-results/absent.trx`, `baseline-results/core-all.trx` (로컬, 업로드하지 않음).
- Unity: 새 자기 worktree를 위 SHA에 고정했다. 14:48 실행 요청 시 `tabs #635 full Unity record (pid 25993)`가 잠금을 보유해 대기 중이다. **NOT_RUN**이며 기준선 전체 PASS나 제품 구현 착수로 보고하지 않는다.
- 오늘 목표: 기준선 통과 후 PR-A 구현, 22:00까지 검증된 PR/Unity 기록과 측정 준비 댓글. PR-B는 A 위 별도 가지에서 이어간다. 준비되지 않은 SHA를 측정 준비 완료로 표시하지 않는다.
- 최신 병합 순서: Codex 클라이언트 → Codex 서버-계정 → Codex 월드맵 → Codex Core. 월드맵의 "병합 창 종료" 댓글 및 기존 승인·CI·C05 조건을 유지한다. Core는 다른 세션 PostgreSQL 포트를 사용하지 않는다.

## Unity 기준선 실행 결과 (15:29 갱신)

실행 SHA는 `b66e85ff3e682b51229c147b81296d848b351e4b`다. 자기 잠금 아래 `tools/unity-tests.sh`를 실행했다. 최초 준비 시도는 로컬 SDK wrapper가 Sim DLL을 CLI 명령으로 잘못 전달해 Editor 실행 전에 실패했다. wrapper의 DLL 직접 실행 분기만 수정하고 재실행했다. 저장소 검사나 Unity 코드는 변경하지 않았다.

| 프로필 | suite | passed | failed | skipped |
| --- | --- | --- | --- | --- |
| first-playable | EditMode | 942 | 0 | 25 |
| first-playable | PlayMode | 55 | 0 | 54 |
| wave-1a | EditMode | 952 | 0 | 15 |
| wave-1a | PlayMode | 96 | 0 | 13 |

`CrowdReplayBenchmarkTests`는 각 프로필 13/13, 합계 26/26 통과했고 skip은 0이다. 네 suite의 실패는 0이지만 모든 프로필에서 skip된 검사 중 2건이 예외 목록에 없어 전체 스크립트는 **exit 1**이다. #609의 동일 skip 기록을 면제로 사용하지 않는다.

남은 검사는 `M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes`와 `MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures`다. 필요한 opt-in fixture를 자기 고정 worktree에서 생성하고 해당 검사들을 추가 실행해 전체 skip 검사에 합산할 예정이다. 예외 목록을 늘리거나 검사를 끄지 않는다. 15:27 현재 `account-ui #638 native/server/Unity (pid 19216)` 잠금 때문에 보충 실행 대기 중이다.

원자료는 자기 고정 worktree의 `artifacts/unity-tests/record.md`와 XML·로그, 주 worktree의 `artifacts/world-combat/unity-baseline.log`다. 준비 실패 원자료도 별도로 보존했다. 이 기록은 baseline SHA 전용이며 향후 제품 구현 head의 Unity 기록을 대신하지 않는다.

## 16:20 구현 및 보충 검증 갱신

사용자가 핵심 기준선 통과를 확인하고, 보충 fixture 준비 누락을 제품 구현 차단 조건으로 삼지 말라고 지시했다. 위의 선행 전체 PASS 대기 계획은 이 지시로 대체되었다. PR-A 제품 코드는 `5cac71ac`부터 원격에 보존했으며, 종료 계약 자체 모순 검사와 데이터/Core 계약 검사를 후속 커밋으로 추가했다. [ADR0083 §12](../adr/0083-opt-in-world-combat-contract.md)에 실제 wire 및 PR-A/PR-B 경계를 명시했다. 실제 월드 실행·정산 완료로 보고하지 않는다.

16:15:21 자기 잠금을 획득해 [#590](https://github.com/hyunlord/bs-mobile/pull/590) 및 [인수인계 #632](https://github.com/hyunlord/bs-mobile/issues/632) 절차를 실행했다.

- 기준선 SHA `b66e85ff3e682b51229c147b81296d848b351e4b`의 combat supplement: **1 passed / 0 failed / 0 skipped**, wave-1a + `UNITY_WAVE_BENCHMARK=1`.
- 같은 SHA의 melee supplement: **1 passed / 0 failed / 0 skipped**, m1-showcase-review.
- 여섯 XML을 함께 검사: **0 unexplained, 1 listed**. first-playable로 복원했으며 추적 파일 변경 0을 확인했다.
- 고정된 위 worktree에 테스트 전용 캡처 파일만 임시 복사해 전체 state fixture 4개를 생성했다. **capture 1 passed / 0 failed / 0 skipped**. 생성기는 제거 목록으로 제거했고 파일은 `core/tests/SowSiege.Tests/Fixtures/world-absent-state`에 원본 bytes 그대로 저장했다. plain/estate+melee+vassals 각각 tick0/tick10, 생성 과정에서 bytes SHA와 실제 session hash를 대조했다.
- 후보 Core net8.0 빌드는 성공했으나 새 테스트에서 CS1513(중괄호 누락), 다음 시도에서 CS8752(params의 target-typed new)가 차례로 발견되었다. 각각 수정했으며 `world-focused-attempt1-cs1513.log`, `world-focused-attempt2-cs8752.log`를 보존했다. 수정 후보의 집중/전체 Core·스키마·ArchitectureGuard·netstandard·format 검사는 다음 잠금에 이어 실행한다. 아직 후보 PASS가 아니다.

PR-B는 [#656](https://github.com/hyunlord/bs-mobile/issues/656)과 별도 가지 `feat/656-world-combat-runtime`에서 PR-A 위에 쌓아 작성한다. 빌드·테스트를 병렬 실행하지 않는다. C05 요청/측정 준비 완료 댓글은 실제 검증된 후보 SHA에만 남긴다.

정리: 자기 worktree의 성공 테스트 생성 PNG 288개를 명시 목록으로 제거해 275,798,452 bytes를 회수했다. XML·로그·CSV·실패 기록과 현재 검증에 쓰는 단일 Unity Library는 보존했다.

## 16:45 PR 및 첫 CI 기록

[초안 PR #657](https://github.com/hyunlord/bs-mobile/pull/657)을 열었다. 후보 `a1ca66b9ded6d9d43f5501f4bb2acc5aeba3ab48`의 [CI 41407246530](https://github.com/hyunlord/bs-mobile/actions/runs/41407246530)은 Core 두 대상 빌드(경고/오류 0), ArchitectureGuard 위반 0을 기록했다. Core 전체는 **1039 passed / 1 failed / 0 skipped, 총 1040**이다. 실패는 `ExplicitStateWriterCoversEveryPersistedMember`의 새 `InteractiveOptions.WorldCombat` 직렬화 누락이었다. 월드 옵션이 있을 때만 canonical payload를 붙이는 분기와 직접 회귀 검사를 추가했다. 기존 absent 바이트를 바꾸거나 검사를 면제하지 않았다. 수정 뒤 실행은 아직 대기다.

스키마 정적 검토에서는 정규식 `$`가 마지막 줄바꿈 직전에도 일치하는 JS/Core 입력 차이를 수정하고 LF/CR/CRLF/Unicode separator × hash/identifier/target 15사례를 추가했다. PR-A는 아직 C05 측정 준비 완료가 아니다. 현재 Unity 잠금은 login #639 소유다.
