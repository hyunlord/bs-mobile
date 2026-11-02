# ADR0085 · 행군 병력 예약과 계정 명령 복구 장벽

상태: 구현 초안 · 2026-11-02 · [#641](https://github.com/hyunlord/bs-mobile/issues/641).
[GDD18](../design/gdd/18-slg-world-map-proposal.md), [ADR0078](0078-server-domain-command-transactions.md),
[ADR0084](0084-finite-gathering-return-ledger.md)의 서버 후속이다.
#590 병영 가지 위 독립 초안으로 작성하며 소유 가지에는 push하지 않는다.

## 결정

월드 행군은 별도 world_march_schema 1과 world_marches/world_march_events에 저장한다.
계정 schema 3과 지도 schema 1은 올리지 않는다. 발령 명령은 기존 commands에서
world-march-v1: payload hash로 멱등 응답을 공유한다. 같은 ID의 다른 payload는 거절한다.

잠금 순서는 **world 행 → 필요한 alliance 행 → account 행 UUID 오름차순 → 활동/행군 행**이다.
행군은 관계를 읽기만 하므로 alliance 행을 잠그지 않는다. 연맹 변경은 기존 alliance→account
순서를 유지하며 world 장벽 아래 실행한다. 이미 account 잠금을 잡은 채 world를 얻지 않는다.

공통 AccountStore에 신뢰된 ExecuteWorldAsync만 추가한다. 호출자가 PostgreSQL transaction을
소유하며 전체 분야 Advance 뒤 typed state 변경·Credits를 돌려준다. 같은 writer가 계정,
분야 이벤트, 지갑 delta, 보관함, outbox를 저장한다. 월드 호출자는 행군/활동 원장·receipt를
같은 transaction에서 확정한다. 공개 계정 명령에 임의 deploy/return/credit 조작을 추가하지 않는다.

TrainingHandler.Deploy는 중복 병종·알 수 없는 병종·0/음수·준비 병력 초과·행군 상한 초과를
거절하고 R→D만 한다. ReturnUnharmed는 이 단계의 비전투 회군 D→R만 한다. W/Q를 출정
병력으로 쓰지 않는다. 전투 N/K→S/A/L은 ADR0083 후속이며 무손실 귀환 함수로 대체하지 않는다.

발령 시 도시와 대상 세대, 고정 최단 경로, 병영 단일 행군 상한, 준비 병력, 영웅 보유/잠금,
행군 칸, 수령 대기 여부를 확인한다. 속도는 발령 시 완료 연구의 march.speedPercent만 합산하고
데이터 cap을 적용한다. 기본 영웅 ID와 병영 상한은 기존 콘텐츠 초안에서 읽으며 슬롯/간선 시간은
world-march-v1.json의 [가설] 값이다. 왕복 경로·속도·분야 상태/콘텐츠 hash snapshot을 저장한다.
공개 DTO에는 내부 snapshot을 내보내지 않는다.

## 시간과 복구

행군 deadline은 UTC 절대 시각이다. 복구는 (due_at, sequence, march_id) 순으로 원래 due 시각을
활동에 전달하고 사건별 transaction을 확정한다. 현재 UTC는 처리 cutoff이며 과거 arrival를 현재
시각으로 이동시키지 않는다. 시계가 뒤로 가도 world/account high-watermark는 낮추지 않는다.

일반 계정 조회/명령을 먼저 현재 UTC까지 Advance하면 오래된 행군 도착이 미래 훈련 결과를
볼 수 있다. WorldRecoveryGate는 due 사건을 먼저 복구한 뒤 world 잠금 아래 다시 due 여부를
확인하고, 없을 때 그 잠금을 계정 명령 끝까지 유지한다. 내부 월드 writer는 이 장벽을 재진입하지
않는다. 늦은 부착 holder는 AccountStore/활동/행군 사이 DI 순환을 없앤다. 저장된 행군 테이블이
있는데 처리기가 부착되지 않으면 ordinary gate는 fail-closed한다.

연맹 명령과 계정 삭제/복원/영구 삭제도 world 장벽을 통과한다. 이 기존 저장소들은 자기 transaction을
유지한다. 바깥 transaction은 world 잠금만 유지하고, 안쪽 transaction은 기존 alliance/account
정산을 한다.복구한 선행 사건은 이후 명령이 거절돼도 되돌리지 않는다. 신규 계정 생성은 기존
world 배정 transaction을 사용하므로 장벽 wrapper를 중첩하지 않는다.

출발 후 회군은 서버가 계산한 실제 경과 이동 시간(간선 도중 진행분 포함)만큼 돌아온다.
귀환 deadline 전에는 영웅·슬롯·D를 유지한다. 도착과 같은 시각의 회군 명령은 먼저 due 도착을
확정한 뒤 활동 취소 규칙을 적용한다. 대상 세대가 바뀌면 대상을 다시 찾지 않고 무보상 회군한다.

## 범위와 미완료 경계

이번 초안의 실제 활동 연결은 채집이다. 채집권·노드 잔량·완료/귀환 Credits는 ADR0084 처리기를
재사용한다. 정찰/보호막은 별도 서버 PR의 활동 adapter를 연결한다. PvE 계약·자동 전투·부상/전사
산출은 이 PR이 구현하지 않으며 없는 activity를 발령하면 거절한다. 대기 요청을 성공으로 위장하지 않는다.

노드 재생성은 대상 접근 시 이전 deadline까지 먼저 복구한다. 노드 전체를 현재 시각으로 진행한 뒤
과거 행군을 끼워 넣지 않는다. 별도 채집 standalone 복구는 march 테이블이 있을 때 금지한다.
행군과 node/이전 standalone 귀환은 world_gathering_sequence를 공유하며 dueAt/sequence/entityId 순으로
한 사건씩 고른다. 마지막 행군이 돌아간 뒤의 유휴 노드 재생성도 진행한다. 영구 삭제로 managed 행군이
사라져도 노드 완료/재생성을 처리하고 소유자 없는 운반물은 forfeited로 닫아 다른 계정에 주지 않는다.

## 검증

작성한 검사는 R/D 보존, 훈련 완료 경계, 행군 인원 경계, 중복 명령/다른 payload, 동시 영웅 경쟁,
간선 중간 회군, 정확한 귀환 deadline, 재시작 장기 중단 복구, 도착 실패 rollback, 세대 교체,
무료 개척전 기사 복제 차단, 계정 writer와 복구 경쟁, 실제 HTTP 훈련→채집→재시작→귀환을 다룬다.

현재 로컬 빌드·테스트는 공유 Mac CPU 잠금 대기 때문에 **NOT_RUN**이다. 정적 검사/원격 CI의
실제 결과는 PR 본문에 SHA별로 기록한다. Core/Unity/리플레이/골든/C05 코드는 변경하지 않는다.

## 주기 복구와 읽기

초기화가 완료되어 barrier가 붙은 뒤 `WorldRecoveryWorker`를 시작한다. `World:RecoveryPollMilliseconds`는 운영 지연만 조절하며 기본 1000ms, 허용 100~60000ms다. 게임 deadline은 콘텐츠와 서버 UTC로만 결정한다. 취소는 정상 종료하고 그 밖의 복구 오류는 critical 로그와 host 종료로 처리한다. AOI chunk 조회도 복구 후 world 잠금을 유지한 채 읽기 스냅샷을 얻는다. 테스트는 HTTP 조회 없이 원장 SQL로 background 도착 처리를 관찰한다.
