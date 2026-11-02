# SLG 콘텐츠 JSON 초안 r1

상태: **사용자 검토 대기, 모든 수치는 [가설]**. 추적 [#571](https://github.com/hyunlord/bs-mobile/issues/571). 게임에 적용하지 않는 데이터 초안이며 신규 그림·Core·Unity·SDK 변경은 없다.

## 납품과 경계

[초안 팩](../../data/opt-in/slg/content-draft-v1.json)은 병종3종·영웅3명의 능력/스킬·연구1단계10노드·월드 몬스터/거점 합계5종이다. [설명](../../data/opt-in/slg/README.md)에 단위·근거·실행하지 않는 경계를 기록한다. 기존 GDD15의2병종/GDD17의9노드는 기준을 보존하고, 추가1병종/1연구는 최신 사용자 요청에 따른 신규 가설로 구별한다.

팩은 별도 `slg-content-draft` 종류이며 `runtimeEnabled:false`다. 스키마도 [opt-in 경로](../../data/opt-in/schema/slg-content-draft.schema.json)에 둔다. 기존 `data/schema`·프로필·골든·해시 함수·런타임 로더를 고치지 않는다. `ContentLoader.Hash`는 opt-in JSON을 제외하고 Unity export도 opt-in 전체를 제외한다. 일반 콘텐츠 검증은 opt-in도 검사하므로 정확한 초안 경로와 전용 스키마만 검증기에 등록한다. 알 수 없는 파일을 일괄 무시하는 예외를 만들지 않는다.

## 게임 설계와의 연결

가신·영웅·연구는 같은 효과를 합산해 기존 cap으로 한 번 제한한다. 진행 중 전투/행군/작업에는 새 성장값을 소급하지 않는다. 영웅 스킬은 한 손 이동 중심의 자동 발동 초안이며 신규 수동 전투 버튼을 요구하지 않는다. 높은 희귀도 자체에 더 큰 최종 능력치 상한을 주지 않는다.

검증된 판의 목표 성공은 특정 연구 탐사 증명·유한한 개인r8구역 개방 자격·예약된 월드 목표 세대의 상태에 연결된다. 자격을 실제 무료 개방이나 연구 즉시완료로 바꾸지 않는다. 물질 보상은 출정/생존 병력 적재 상한과 귀환 정산을 따르고, 세계 변화·자격과 운반 자재 지급 시점을 구분한다. 기본 기사만의 무료 개인 기초 개척 경로는 새 병종/영웅/연구로 잠그지 않는다.

## 미검증

이 팩의 스킬/전투 수치·훈련/연구 비용·세계 목표는 실행 게임 규칙이 아니다. 신규 SLG 실제 플레이·승률·경제·서버 원자성·RNG·모바일 성능·Unity 표현은 **NOT_RUN**이다. 검증기 통과와 기존해시 보존을 밸런스 승인이나 게임 구현 완료로 표현하지 않는다. 기존format7/8·골든을 신규초안으로 교체하지 않는다.

## 정적 검증과 역사 경계 확인 (2026-10-30)

- `node --test tools/test-slg-content-draft.mjs`: 29/29 통과. 개수·닫힌 스키마·가설 표기·runtime 비활성·중복 ID/JSON 키·참조·연구 순환·단위/범위/상한·보상 적재 상한과 미등록 opt-in 경로 거부를 검사했다.
- `npm test`: 277/277 통과. 신규 검사를 기존 테스트 진입점에서 함께 실행한다.
- `npm run validate`: 256레코드·24스키마, 오류0. `git diff --check` 통과.
- 로컬 `./tools/check.sh` 전체 중복 실행은 현행 이슈 처리 런북 §6에 따라 하지 않는다. PR CI 전체 관문은 별도 확인한다.
- 실제 C# Sim의 기존 `wave-1a`, seed42, 1tick, iterations1, warmup0 fixture를 추가 전후 비교했다. 공유 Mac 잠금을 잡고 기존 Debug DLL을 실행했으며 게임 규칙을 변경하거나 새 SLG 팩을 로드하지 않았다. 실행 명령: `dotnet core/src/SowSiege.Sim/bin/Debug/net8.0/SowSiege.Sim.dll --data data --profile wave-1a --seed 42 --iterations 1 --duration-ticks 1 --warmup-ticks 0 --output artifacts/slg-after-571/result.json`. 추가 전에는 `--data artifacts/slg-baseline-571/data`를 사용했다.
- 실제 `runMetadata.contentSha256`: 전후 동일 `6AC1ACFC4B2E80DD510EE0A41D0039BC05932A5FEF1776CC1B2A4BF2E928C36D`.
- 기존 1tick 상태 해시: 전후 동일 `26FD7DBCC8D727C2A13E66C9402535D7B2ED395BEB93BD4792A50B20B4DE97E8`.

1tick 검사는 콘텐츠 해시 제외 경계와 기존 fixture의 비변경만 증명한다. 전체 플레이·모바일 성능·신규 SLG 밸런스나 서버 실행 증거가 아니다. 원자료는 작업 폴더의 `artifacts/slg-baseline-571/result.json` 및 `artifacts/slg-after-571/result.json`이며 별도 성공 원자료 묶음을 업로드하지 않는다.
