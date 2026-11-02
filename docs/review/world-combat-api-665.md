관문: 부분 — PR-D 구현 초안. 야간 코드 작성이며 실행 검증·Unity·C05는 NOT_RUN.
기준: `09e77eb2f20e5a4a7e47d6d161042f3acac0f519` (#657·#659·#660·#609 묶음).
연결: [#665](https://github.com/hyunlord/bs-mobile/issues/665), [서버 소비자 #664](https://github.com/hyunlord/bs-mobile/issues/664), [ADR0083](../adr/0083-opt-in-world-combat-contract.md).
기존 묶음의 검증 수치를 이 변경의 검증으로 승계하지 않는다.

# 월드 전투 서버 소비 API v1 (2026-11-03)

## 호출 순서와 권한

1. 서버가 소유권·대상 세대·모드·출정 N·규칙/콘텐츠·효과를 확정하고 `InteractiveOptions`를 만든다.
2. `WorldCombatApi.CreateContract(catalog, options)`가 format9 직접 전투 헤더를 깊게 고정하고 실제 catalog와 실행 가능성을 검사한다. 서버 계약 hash를 생성하거나 계정 소유권을 승인하지 않는다. 반환 헤더를 서버 계약 revision과 함께 보관한다.
3. `AcceptPrefix(catalog, actualDataHash, issuedHeader, acceptedRecovery, chunk)`가 발급 헤더 전체를 대조하고 승인 recovery를 재실행한 뒤 신규 chunk를 적용한다. 최초 호출은 recovery가 null이다. 명령 수와 canonical `ReplayCodec.WriteCommand` bytes 합계가 발급 `MaximumChunkCommands/MaximumChunkBytes` 이하여야 한다. HTTP/JSON 요청 크기 제한은 서버가 별도로 적용한다.
4. 반환 `WorldInputFrontier`의 `Recovery`, `NextSequence`, `Tick`, `StateHash`, `AppendedCommands`, 선택적 `Result`를 같은 run revision 아래 원자적으로 저장한다. 실패한 호출은 기존 recovery 배열을 수정하지 않는다. 빈 chunk는 신규 승인으로 받지 않는다.
5. 서버가 grace/highwater로 누락 틱을 확정한 뒤 `AdvanceWithoutInput(..., targetTick)`을 호출한다. Core는 UTC를 받지 않는다. 대상은 현재 틱 이상·발급 limit 이하이며 범위 밖은 거절한다. 입력 없는 Advance 및 카드 대기 시 ordinal 최소 ID ChooseCard를 생성하고 반환 prefix에 포함한다. 최초 terminal에서 멈춘다.
6. 최종 제출은 `VerifyResult(catalog, actualDataHash, issuedHeader, acceptedRecovery, replay)`로 헤더 전체·승인 prefix와 재실행 결과를 검증한다. 제출 prefix가 승인 명령을 줄이거나 교체하면 거절한다. 승인 terminal이 있으면 동일 결과만 허용한다. quit는 기존 world-end 경로만 사용하며 recovery에 넣어 재개하지 않는다.

`acceptedRecovery`는 **서버가 이미 승인·저장한 bytes**만 전달한다. 해시·checksum은 서명이 아니다. 클라이언트가 새로 제출한 recovery로 서버 승인 prefix를 교체하거나 null로 초기화하면 안 된다. 동시 수신의 revision 비교, 중복 요청 응답, 최초 terminal의 원자 정산, 취소/귀환 경합은 서버 책임이다. Core의 재실행 가능성은 입력의 실제 수신 시각이나 사람 조작을 증명하지 않는다.

`inputGraceMilliseconds`는 발급 데이터에 고정된 값을 서버가 읽는다. 마지막 유효 신규 수신 + grace **이하**에서는 미확정 입력을 허용하고 초과 후 확정 틱의 늦은 입력을 거절한다. 중복·무효 요청과 재시작으로 마감을 늘리지 않는다. Core는 확정된 sequence/tick만 검사하므로 이 시간 경계의 서버 테스트를 대신하지 않는다.

## 결과와 직렬화

| 값 | 의미 |
| --- | --- |
| ContractHash / Mode | 발급 계약의 opaque hash 및 DirectPve/AutoPve/CityPvp 구별 |
| RulesHash / ContentHash | 발급 규칙과 콘텐츠 provenance |
| Terminal / TickOrRound | 최초 종료 원인 및 직접 tick 또는 자동 round |
| Objectives | ID별 진행·완료. 자동 결과는 직접 hold 행동 증거가 아님 |
| Troops | ID ordinal 순 N/K. K는 판 안 전투 불능이며 영구 전사 수가 아님 |
| StateHash | 해당 실행의 최종 상태 hash |

직접 결과는 `ReplayCodec.EncodeWorldEnd(issuedHeader, end)` 및 `DecodeWorldEnd(issuedHeader, bytes)`를 사용한다. 기존 `world-end-v1` tag/schema/길이/framing을 그대로 사용하고 N·목표·provenance와 trailing bytes를 검사한다. **Decode는 결과 주장 검사이며 실행 증명은 아니다.** 서버는 `VerifyResult` 또는 서버 승인 prefix의 재실행 결과만 정산한다. codec 오류는 기존 `ArgumentException`(encode), `InvalidDataException`(decode) 계약이며 서버에서는 `invalid-result`로 매핑한다.

자동 전투는 기존 공개 `ArmyAuto.Resolve` 및 `ArmyAuto.EncodeResult` 계약을 사용한다. 내부 `ArmyAutoEncoding`을 서버에서 호출하지 않는다. 직접 헤더 도우미에 AutoPve/CityPvp를 넣지 않는다. 자동 계수·상성·round 상한·무수비 정책은 입력 데이터에서 오고 난수를 추가하지 않는다. 서버 귀환의 병상·부상·영구 전사·R/D/W/Q·보상은 이 API에 없다.

## 안정 오류 코드

`WorldCombatApiException.Code`를 분기한다. 예외 메시지를 파싱하지 않는다.

| Code | 의미 / 서버 대응 |
| --- | --- |
| invalid-contract | 직접 format9/지원 옵션/catalog 실행 계약 불일치. 발급 거절 |
| contract-mismatch | 발급 헤더와 recovery/제출 헤더 bytes 불일치 |
| data-mismatch | 실제 소비 data hash와 발급 data hash 불일치 |
| invalid-prefix | 빈 chunk, 저장 recovery 또는 전체 prefix 재실행/상태 검증 실패 |
| invalid-command | 지원하지 않는 명령, 카드/실행 상태 등 명령 검증 실패 |
| chunk-limit | 발급 명령 수/직렬화 byte 상한 위반 |
| frontier-conflict | 승인 sequence/tick과 신규 명령 불일치. 확정 틱 수정 금지 |
| battle-terminal | 최초 terminal 이후 새 명령 제출 |
| invalid-frontier | 무입력 목표 틱이 현재 틱 미만 또는 발급 limit 초과 |
| command-limit | 전체 replay 프로토콜 명령 상한 초과 |
| invalid-result | 제출 최종 결과 재실행 불일치 |

검증 예산·규칙/콘텐츠 가용성 문제로 결과를 확정하지 못하면 서버 상태 `recovery-pending`을 유지한다. 위 오류를 성공/K=0/무료 귀환으로 변환하지 않는다. Core에는 `recovery-pending` terminal을 추가하지 않는다.

## 가정과 검증 계획

- `OrderVersion=1`의 무입력 기본 카드는 StringComparer.Ordinal 최소 안정 ID다. 기본 선택도 명령으로 저장하므로 검증기가 선택을 추측하지 않는다. 과거 wire/state codec은 변경하지 않는다.
- 새 서버 진입점은 HostRewards 옵션과 host/review 명령을 거절한다. 기존 저수준 API의 역사 허용 범위는 바꾸지 않는다.
- 불변 승인 prefix를 매번 재실행한다. 최적화 캐시·부분 상태 직접 restore는 추가하지 않는다. 서버는 DB 잠금 밖 실행·동시 검증 슬롯·전체 시간/크기 예산을 유지한다.
- 낮 검증: 신규 API 집중 검사, absent16+추가 state/가신14, Core 전체, 실제 두 DLL parity, Unity 두 프로필 네 묶음+보충2+crowd-411 26, 별도 C05 3:3. 모두 이 PR의 정확한 head로 기록한다.
- 초기 인수 당시 #660 C05 결과 댓글 없음. #659/#660 리뷰 봇은 사용량 제한 실패이며 실질 검토 완료로 간주하지 않는다. 서버 #664의 ④ PR이 생기면 발급 헤더 대조·grace/확정 frontier·최초 terminal·정산 경계를 PR 댓글로 리뷰한다.
