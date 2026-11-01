# 가신 전투 구현 검증 (#527)

- 날짜: 2026-10-29
- 상태: 초안 검토. 사용자 병합 승인 대기, 성능은 별도 Claude Code 대기열.
- PR: [#528](https://github.com/hyunlord/bs-mobile/pull/528).
- 설계 정본: #525 r2 (`25b49d0293b8f4b26755ec0e6db8f8d9836b20fb`).
- 실행 소스: `0fbdb2e7dec771f1ff90cc63580a4403275594f0`. 이후 문서/수치 기록만 변경.

## 구현과 범위

6개 능력, 출정 고정 snapshot, 정수 permille와 나머지·아군 최대 HP 복구, format8/vassal-run-v1을 구현했다. 블록이 없으면 역사 format1~7 bytes/실행 경로를 유지한다. 계정 저장 형식6은 바꾸지 않는다. 실제 명령으로 나머지를 만든 후 저장/복구/계속 실행하는 회귀도 포함한다.

이동은 기존 계산을 완료한 축별 크기에 마지막으로 floor 곱 1회 후 방향을 복원한다. 기존 Feel 나머지 단위는 유지하며 작은 이동량에서 보너스가 양자화되어 사라지는 한계가 있다. [ADR0075](../adr/0075-vassal-run-snapshot-and-replay-format-eight.md)에 근거를 기록했다.

FP01은 역사 회귀만이며 Growth·Allies·완료 XP를 추가하지 않는다. 효과 비교는 정상 wave와 C05 고정 명령 파생본으로 구별한다. 개발 빌드/Editor wave 명시적 opt-in에서만 적용하고 기본값은 off다. 꺼진 상태의 미적용 안내는 유지한다. 이미 시작한 판에는 이후 편성 변경이 반영되지 않는다.

## 검증

| 검사 | 결과 | 범위 |
|---|---|---|
| Core 전체 | 872/872, skip 0 | net8.0 |
| 가신 집중 | 82/82, skip 0 | snapshot 격리, cap, 나머지 복구, 역사 음성 대조 |
| netstandard2.1 | 빌드 경고/오류 0 | 실기/IL2CPP 아님 |
| 변경 파일 형식 | exit 0, IDE0011 0 | CompatSim 참조 경고는 #533, 최종 판단 CI quality |
| C07 역사 | 양 타깃 동일, 27000틱 | `97152EB9E83B4158DB8714AAA76CE73435595B3CC3A62E918918E87480A219ED` |
| A03 역사 | 양 타깃 동일, 27000틱 | `2E394387DDE118A2F84E76419FC381474C4F071BCAF06D5E0C8A694EF8CE5FE6` |
| C05 역사 | 양 타깃 동일, 14400틱 | `B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8` |
| FP01 역사 | 양 타깃 동일, 27000틱 | `69E7182A9FB4EBF154E85551F2A2C6696F8EFBD30A87EC3C1046DF593361CDB5` |
| crowd-411 실제 입력 | 11530틱 동일 | `59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619` |

역사 양 타깃 검사는 .NET host에서 실제 net8.0/netstandard2.1 DLL을 사용했다. crowd-411은 ADR0058에 따라 기록 당시 header data hash와 현재 catalog를 사용했다. replay SHA256 `4fe0f97f23d20b487730be03ae76f5d7551ec88b787d43566f0cde41cd5a079c`; 원본 bytes 변경 없음.

| Unity 프로필 | EditMode 통과/실패/skip | PlayMode 통과/실패/skip |
|---|---|---|
| first-playable | 911 / 0 / 25 | 55 / 0 / 52 |
| wave-1a | 921 / 0 / 15 | 94 / 0 / 13 |

기본 4 XML은 두 입력 부재로 skip 집계 exit 1이었다. 기존 도구로 frozen combat sidecar와 opt-in melee catalog를 생성한 뒤 해당 테스트를 각각 1/1 통과시켰다(새 예외 없음). 여섯 XML 합산 `skipped in every profile: 0 unexplained, 1 listed`. listed는 선택적 recorded-input export다. 실행 뒤 추적 파일 변경0. 기본 실행의 exit1도 보존한다.

## 실제 화면

[선정 표본 4장 Release](https://github.com/hyunlord/bs-mobile/releases/tag/vassal-run-527-20261029).

390×844·1080×2520의 opt-in 상단/하단에서 초상화, 다음 출정 고정 안내, 여섯 합산→적용/상한 표시를 확인했다. 네 캡처 cyanPixels=0, compiling 전/후 false. 초기 계정 공격10‰/나머지0 표본이며 최대 편성의 cap 도달 문구와 실기 판정은 미확인이다. 별도의 기존 비활성 긴 화면은 제목 가림이 있으나 이번 opt-in 하단에서는 제목과 출전 칸 표시가 유지된다.

초상화6crop 밖 RGBA 변경0, atlas크기/import동일. [메모리 증가 기록](../metrics/texture-memory-deltas/527.json): Mac/iOS/Android 모두 추정 증가0bytes. 장치 측정은 아니다.

## 재생성

```sh
dotnet test core/tests/SowSiege.Tests/SowSiege.Tests.csproj --configuration Release
node tools/verify-wave-history-parity.mjs artifacts/vassal-history-new
dotnet core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll interactive-replay data benchmarks/first-playable-fp01/run.ssreplay first-playable
dotnet core/src/SowSiege.CompatSim/bin/Release/net8.0/SowSiege.CompatSim.dll interactive-replay data benchmarks/first-playable-fp01/run.ssreplay first-playable
# C05 frozen data export는 wave-density-benchmark 런북의 기존 경로 사용
dotnet core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll vassal-balance data artifacts/vassal-balance-new benchmarks/wave-c05/run.ssreplay artifacts/benchmarks/c05/data
```

## 보존한 실패·중단

- 최초 집중검사45통과/2실패: 새 catalog격리 fixture입력 오류. 실제 World 멤버 대신 없는 `WaveGroupView.Members`를 쓴 비교 도구 compile실패도 수정했다. 이후50/50·전체857/857, 보강 후82/82·872/872.
- format8 all-false Feel writer/reader 불일치를 정적검토에서 발견해 writer정규화와 회귀검사 추가.
- 17:15 두 차례 형식 검사에 CompatSim 참조 로딩과 IDE0011이 함께 남아 중단·잠금 해제. 사용자 재개 승인 뒤 누락 중괄호를 직접 추가하고 변경파일 `--include` exit0을 확인했다. 규칙 비활성/예외 추가 없음. 참조 로딩은 [환경 이슈 #533](https://github.com/hyunlord/bs-mobile/issues/533).
- 최종 이동 변경 전 정상wave128회 결과는 최종 비교에 쓰지 않는다. 최종 산술로 전부 재실행했다.

## 최종 CI 중단

- [CI 40846618660](https://github.com/hyunlord/bs-mobile/actions/runs/40846618660): quality FAIL, secrets PASS. ArchitectureGuard AG003 32건. Core 단위/형식 단계 전에 중단되어 CI의 tests TRX 업로드도 입력 파일 없음으로 실패했다.
- PR 본문 갱신으로 이미 예약됐던 동일 SHA의 [CI 40846916123](https://github.com/hyunlord/bs-mobile/actions/runs/40846916123)도 같은 quality 실패 후 취소 처리. 수정 없는 중복 실행이며 두 기록을 모두 보존한다. 추가 실행·구현을 중단한다.
- IDE0011 재발이 아니다. `VassalRunContract`의 능력 B/S/R/cap, 슬롯/레벨/승급 검증식은 게임플레이 데이터 중복이므로 알고리즘 예외에 넣을 수 없다. format8 판별자·codec 할당 제한과 구분해야 한다.
- 다음 수정 방향(미구현): 승인 opt-in/MetaCatalog 정본을 불변 출정 규칙으로 주입하고 Core에서 상한 근거·재계산 검증을 유지한다. 이 근거를 replay에도 고정해야 하므로 단순 형식 수정이 아니라 snapshot/wire 검증 계약 구체화가 필요하다. Guard 비활성·광범위 예외·host 검증으로만 이동하는 우회는 하지 않았다.
- 두 프로필/역사 해시의 통과를 전체 관문 통과로 표현하지 않는다. #528 초안 유지, #496 미착수.

## 최종 이동 산술 기준 효과 비교

동일 이동·카드 선택 정책, seed 30000~30031. 최대는 3칸 계약을 지키는 서로 다른 최대 레벨/승급 편성 3개다. 6능력 동시 최대가 아니다. XP 차이 때문에 카드 명령열은 달라질 수 있다. 시간은 시뮬레이션 tick/30Hz이며 CPU·프레임 성능이 아니다.

| 편성 | 완료/거부 | 생존 | 평균 전투초 | 없음 대비 평균초 | 평균 레벨 | 평균 처치 |
|---|---|---|---:|---:|---:|---:|
| none | 32/0 | 13/32 | 682.01 | 0.00 | 23.19 | 890.44 |
| attack-health-movement | 32/0 | 25/32 | 824.20 | 142.19 | 28.06 | 1186.59 |
| growth-experience-attack | 32/0 | 19/32 | 718.21 | 36.20 | 25.09 | 953.41 |
| allies-health-attack | 32/0 | 25/32 | 824.20 | 142.19 | 28.00 | 1186.81 |

C05 원본은 frozen catalog로 먼저 검증했다. 아래는 같은 명령의 파생 실행이며 무적 스트레스 구간이다. 거부된 접두부를 생존·완료 결과로 취급하지 않는다.

| 편성 | 상태 | 도달 tick | 첫 거부 sequence | 사유 |
|---|---|---:|---:|---|
| none | COMPLETED | 14400 | — | running |
| attack-health-movement | COMPLETED | 14400 | — | running |
| growth-experience-attack | REJECTED | 840 | 841 | ArgumentException: Card is not offered. |
| allies-health-attack | COMPLETED | 14400 | — | running |

정본 scalar CSV·snapshot/seed/DLL provenance는 [balance.csv](../metrics/vassal-run-527/balance.csv)와 [provenance.json](../metrics/vassal-run-527/provenance.json)에 둔다. 정상 생존은 이 자동 정책 표본의 결과이며 사람 밸런스 판정은 아니다. 성능 NOT_RUN, Claude Code 대기열 별도.

## 종료와 다음 순서

18:20 KST 정상128행+C05파생4행 기록 완료, 자신의 Mac 잠금 해제. 비교 CLI는 C05 카드 거부1건 때문에 exit1이며 재실행하지 않았다.

사용자 후속 결정: Core 관문 통과 뒤 승인 [가신 UI](https://github.com/hyunlord/bs-mobile/releases/tag/vassal-effects-ui-531-20261029)를 적용하고 그 다음 #496으로 간다. 새 UI·#496은 미착수다. 현재 실패를 통과로 바꾸지 않고, 이 기록은 추가 CI 반복을 피하기 위해 로컬 문서 커밋/PR 코멘트/Release scalar 자료로 보존한다.

## 2026-10-29 사용자 승인 재개: 수치 정본 분리

구현 commit `62558a324919c0cb3369badee357b517b1c369b2`. 이전 AG003 실패 기록은 위에 그대로 보존한다.

- B/S/R·능력 cap·슬롯/레벨/승급 한계: opt-in JSON이 정본. 원본 파일 SHA256을 snapshot ContentHash에 사용한다. Core 파일 I/O 없음.
- 검토된 형식 상수: 기존 algorithm-constants.json에 format8 번호와 codec 할당/해시 길이 상수10개만 등록. 기존 PlayerInput.Scale·ReplayCodec.MaximumCommands 재사용. AG003 코드·literal 허용 범위 변경0.
- 독립 검토에서 누락 numeric 필드의0대입과 int 배율 덧셈을 확인해 필수 필드 검사 및 Int64 덧셈으로 보완했다. 최종 독립 검토 차단 사항0.
- 집중78/78, 전체Core885/885(실패0·skip0), netstandard2.1 Release 경고0·오류0. 로컬 ArchitectureGuard 위반0.
- crowd-411 실제 원본11530틱 종료`59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619` 유지. 입력sha·recorded/currentdatahash는 별도 raw JSON에 보존. 역사 headerhash를 사용하는 ADR0058 재실행이며 일반strict검증을 우회한 새 정책이 아니다.
- 변경파일 dotnet format exit0, CompatSim 참조 환경 경고는 기존 #533에 해당. CI quality를 최종 형식 판정으로 사용한다.

### 이번 최초 CI 실패 (보존)

[40851230909](https://github.com/hyunlord/bs-mobile/actions/runs/40851230909): qualityFAIL/secretsPASS. 새 caps 필드가 기존 opt-in JSON schema에 누락돼 `additionalProperties` 오류가 기본 validator 검사를 함께 실패시켰다. AG003 재발이 아니다. Schema에 required caps 구조를 추가하고, 기존 효과 상한 요구·additionalProperties=false는 유지한다. 잘못된 필드를 허용해 통과시키지 않는다.

### 재개 후 Unity 실행

| 프로필 | EditMode 통과/실패/skip | PlayMode 통과/실패/skip |
|---|---|---|
| first-playable | 911/0/25 | 55/0/52 |
| wave-1a | 921/0/15 | 94/0/13 |

실행 소스는 `62558a324919c0cb3369badee357b517b1c369b2`. 기본 도구의 누락 입력 2건으로 exit1인 원기록은 보존한다. 기존 showcase-fixtures·melee-aim-fixture/prepare-unity로 해당 입력을 생성하고 두 검사를 각각 1/1 통과시켰다. 여섯 XML 합산: `0 unexplained, 1 listed`. 예외 추가0, 실행 후 추적 파일 변경0. listed는 선택적 recorded-input export다.

## 데이터 정본 전환 후 중간 상태 포함 효과 비교

동일 이동·카드 선택 정책, seed 30000~30031. 최대는 3칸 계약을 지키는 서로 다른 최대 레벨/승급 편성 3개다. 6능력 동시 최대가 아니다. 중간 상태는 Lv10·승급2 공격/체력/이동이며 편성 해금 여력만 같은 합성 계정을 사용한다. XP 차이 때문에 카드 명령열은 달라질 수 있다. 시간은 시뮬레이션 tick/30Hz이며 CPU·프레임 성능이 아니다.

| 편성 | 완료/거부 | 생존 | 평균 전투초 | 없음 대비 평균초 | 평균 레벨 | 평균 처치 |
|---|---|---|---:|---:|---:|---:|
| none | 32/0 | 13/32 | 682.01 | 0.00 | 23.19 | 890.44 |
| attack-health-movement | 32/0 | 25/32 | 824.20 | 142.19 | 28.06 | 1186.59 |
| growth-experience-attack | 32/0 | 19/32 | 718.21 | 36.20 | 25.09 | 953.41 |
| allies-health-attack | 32/0 | 25/32 | 824.20 | 142.19 | 28.00 | 1186.81 |
| middle-attack-health-movement | 32/0 | 20/32 | 783.49 | 101.48 | 28.03 | 1113.19 |

C05 원본은 frozen catalog로 먼저 검증했다. 아래는 같은 명령의 파생 실행이며 무적 스트레스 구간이다. 거부된 접두부를 생존·완료 결과로 취급하지 않는다.

| 편성 | 상태 | 도달 tick | 첫 거부 sequence | 사유 |
|---|---|---:|---:|---|
| none | COMPLETED | 14400 | — | running |
| attack-health-movement | COMPLETED | 14400 | — | running |
| growth-experience-attack | REJECTED | 840 | 841 | ArgumentException: Card is not offered. |
| allies-health-attack | COMPLETED | 14400 | — | running |
| middle-attack-health-movement | COMPLETED | 14400 | — | running |

정본 scalar CSV·snapshot/seed/DLL provenance는 [balance.csv](../metrics/vassal-run-527/data-contract/balance.csv)와 [provenance.json](../metrics/vassal-run-527/data-contract/provenance.json)에 둔다. 정상 생존은 이 자동 정책 표본의 결과이며 사람 밸런스 판정은 아니다. 성능 NOT_RUN, Claude Code 대기열 별도.

중간 상태 포함 비교 실행은 정상160/160 완료, C05파생4완료/1거부로 exit1이다. XP 카드 선택 거부는 기존 고정 명령과 달라진 진행 상태의 불일치이며 실패 접두부를 보존했다. 수치 조정 없음. 두 PlayMode 로그의 CYAN_CAPTURE_FAILURE는 각각 guard-self-test-negative-control-True(의도적1픽셀) 한 건뿐이다. 실제 캡처 청록 실패0.
