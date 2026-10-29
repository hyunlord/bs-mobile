# 가신 전투 구현 검증 (#527)

- 날짜: 2026-10-29
- 상태: 부분 — 사용자 재개 승인 후 IDE0011 수정·변경 파일 형식 검사 통과. Unity·최종 CI 대기.
- PR: [#528](https://github.com/hyunlord/bs-mobile/pull/528), 초안 유지.
- 설계 정본: #525 r2 (`25b49d0293b8f4b26755ec0e6db8f8d9836b20fb`).

## 사용자 확인에 따른 비교 범위

FP01에는 wave의 Growth·Allies·완료 XP 시스템을 추가하지 않는다. FP01은 원래 입력·종료 해시 회귀만 검사한다. 새 효과 비교는 C05의 명령을 그대로 사용하는 파생 실행과 별도의 정상 wave 입력으로 한다. C05는 무적 스트레스 입력이므로 생존 밸런스 증거가 아니다.

## 실행 기록

아래는 구현 중 로컬 작업본의 기록이다. 이후 추가 테스트·코드의 최종 검증과 CI를 대신하지 않는다.

| 검사 | 결과 | 범위 |
|---|---|---|
| 가신 집중 테스트 최초 | 45 통과 / 2 실패 | 새 catalog 격리 테스트의 입력 구성 오류. 원자료 보존 |
| 수정 후 재빌드 | 컴파일 실패 | 새 비교 도구가 없는 `WaveGroupView.Members`를 참조. 실제 처치 카운터로 수정 |
| 가신 집중 테스트 재검증 | 50/50, skip 0 | 수정된 두 fixture 및 비교 정책 검사 포함 |
| 전체 Core 테스트 | 857/857, skip 0 | net8.0, 아래 후속 보강 전 실행 |
| C07 역사 입력 | 양 타깃 동일 | 27000틱, `97152EB9E83B4158DB8714AAA76CE73435595B3CC3A62E918918E87480A219ED` |
| A03 역사 입력 | 양 타깃 동일 | 27000틱, `2E394387DDE118A2F84E76419FC381474C4F071BCAF06D5E0C8A694EF8CE5FE6` |
| C05 역사 입력 | 양 타깃 동일 | 14400틱, `B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8` |
| FP01 역사 입력 | 양 타깃 동일 | 27000틱, `69E7182A9FB4EBF154E85551F2A2C6696F8EFBD30A87EC3C1046DF593361CDB5` |

양 타깃은 .NET host에서 실제 net8.0 / netstandard2.1 Core DLL을 사용했다. Unity Mono·IL2CPP·기기 실행 또는 성능 증거가 아니다.

재생성 명령:

```sh
dotnet test core/tests/SowSiege.Tests/SowSiege.Tests.csproj --configuration Release
node tools/verify-wave-history-parity.mjs artifacts/vassal-history-new
dotnet core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll interactive-replay data benchmarks/first-playable-fp01/run.ssreplay first-playable
dotnet core/src/SowSiege.CompatSim/bin/Release/net8.0/SowSiege.CompatSim.dll interactive-replay data benchmarks/first-playable-fp01/run.ssreplay first-playable
```

## 후속 보강 및 미완료

- 실제 명령으로 생성한 grain/XP 나머지·group 최대 HP를 저장·복구한 후 계속 실행하는 검사 추가. 집중 82/82 및 전체 872/872에서 통과.
- 실제 opt-in 세션 생성 후 원본 catalog 변경·반환 snapshot 변경 시도에 대한 격리 검사 추가. 집중 82/82 및 전체 872/872에서 통과.
- 정적 검토에서 수동 format8 header의 all-false Feel writer/reader 불일치 발견. writer 정규화와 회귀 검사 추가. 집중 82/82 및 전체 872/872에서 통과.
- 이동은 사용자 지정 보수안: 기존 이동 계산 후 축별 이동 크기에 가신 배율을 마지막으로 floor 1회 적용. ADR0075에 기존 나머지 계약 유지와 양자화 한계를 기록했다.
- 정상 wave 128회 실행은 완료했으나 최종 이동 산술 변경 전 결과이므로 최종 비교로 쓰지 않는다. C05 파생 효과 비교·성능 측정은 NOT_RUN이다.
- UI 안내 제거·초상화 통합·Unity 두 프로필·crowd-411 명시 검증·최종 CI는 아직 완료하지 않았다.

## 17:15 중단 기록

- 집중 검사: 82/82, skip 0 (`artifacts/vassal-527/tests/vassal-final-second.trx`).
- 전체 Core: 872/872, skip 0 (`core-all-final.trx`). netstandard2.1 빌드 오류·경고 0.
- 형식 검사 최초와 재실행 모두 CompatSim 참조 로딩 실패 및 새 Sim/테스트 코드 IDE0011(중괄호 누락)이 남았다. whitespace 수정과 solution restore만으로 해결되지 않았다. `format.log` / `format-final.log` 보존. 두 번 같은 실패 중단 규칙을 적용하여 추가 수정·Unity 실행을 중단했다.
- Unity 실행 전 prepare/export는 앞 단계 실패로 실행되지 않았다. 최신 구현 CI는 미실행이며, ADR 커밋의 CI 성공과 구별한다. 구현은 로컬 작업본에 보존했다.
- 17:15 공유 Mac 잠금 해제 확인. #528 초안 유지, 병합하지 않는다. 후속 순서는 #528 완료 → #496 → 전투 UI → 영지 UI 그대로다.

## 사용자 승인 후 재개

- 새 Sim·테스트 코드의 누락 중괄호를 직접 추가했다. 규칙·예외 변경 없음.
- `dotnet format --no-restore --verify-no-changes --include <변경 Core/Sim/테스트 .cs>`: exit 0, IDE0011 없음. CompatSim 참조 로딩 경고는 환경 이슈 [#533](https://github.com/hyunlord/bs-mobile/issues/533)로 분리했으며 최종 판정은 CI quality다.
- 최종 이동 산술 적용본으로 crowd-411 실제 입력을 실행했다: 11530틱, `59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619` 동일. ADR0058에 따라 기록 당시 data hash를 보존하고 현재 catalog로 실행했다. 원본 replay bytes 변경 없음.
