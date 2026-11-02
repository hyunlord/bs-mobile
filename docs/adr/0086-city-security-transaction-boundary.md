# ADR0086 · 도시 보호와 적대 관계의 트랜잭션 경계

- 상태: 구현 초안, 런타임 검증 대기
- 날짜: 2026-11-02
- 추적: [#642](https://github.com/hyunlord/bs-mobile/issues/642)
- 근거: [GDD18 §6](../design/gdd/18-slg-world-map-proposal.md), [ADR0077](0077-authoritative-slg-server.md), [ADR0078](0078-server-domain-command-transactions.md), [ADR0080](0080-release-login.md)

## 결정

보호 상태는 공유 계정 writer의 지갑을 직접 변경하지 않는 별도 city_security_schema v1 저장소에 둔다. 계정 생성 시 같은 transaction으로 신규 보호를 기록한다. public 보호 명령은 commandId 영수증으로 중복 지급을 방지한다. 보호가 켜진 상태의 추가 활성화·적대 행군 중 활성화·귀환 후 제한시간 이전 활성화는 거절한다. 현재 숫자는 opt-in city-security-v1.json의 가설이며 서버 코드에 기본값을 넣지 않는다.

잠금 순서는 필요 시 월드 사건 잠금 → 관련 accounts UUID 오름차순 → 보호/적대 행군이다. 관계 검사에서 alliance 행을 나중에 잠그지 않는다. 기존 연맹 변경은 alliance → account 잠금 아래에서 membership을 변경하므로 account 잠금 보유 중 관계 조회가 안정된다. 발령과 도착에 같은 관계 검사를 수행한다.

보호 활성화/조회는 WorldRecoveryGate를 통과한다. 행군 소유자는 먼저 기한이 지난 사건을 처리한 뒤 월드 잠금을 잡고 누락된 due 사건이 없는지 재검사하며, 그 잠금을 사용자 작업의 commit까지 유지해야 한다. 복구 함수 호출과 사용자 transaction을 분리하지 않는다. 행군 표가 이미 존재하는 서버에서 복구 소유자가 미부착이면 fail-closed 한다. 행군 이전 서버는 자체 transaction으로 동작한다.

정찰 snapshot은 내부 서버 도착 훅에서만 발급한다. 호출자는 세대/경로/R→D/영웅/슬롯 자격을 먼저 검사하고, 도착에서 대상 account domain을 승인 시각까지 진행해야 한다. 응답은 ready 병력, 승인 자원의 약탈 가능 상한, 관측시각으로 제한한다. 실제 전투·수비 예약·약탈 원장·행군은 별도 PR이다.

## 삭제 및 재시작

신규 보호 기준은 계정 생성 때 한 번 저장한다. 이전 서버 계정은 최초 생성시각 원장이 없으므로 신규 보호를 소급 지급하지 않고 최초 보안 접근부터 무료 주기만 시작한다. 기존 도시 배치 시각을 계정 생성 시각으로 오인하지 않는다.

상태에 policy hash와 UTC highwater를 보관하며 해시 변경은 명시적 migration 전 거절한다. 명령과 도착 영수증을 재시작 뒤 재사용한다. 영구 삭제 때 자기 상태/명령/사건/적대 행군은 cascade 삭제하고 타인의 행군 대상 FK만 NULL로 바꾼다. 대상 ID가 제거되어도 살아 있는 공격자 귀환 제한을 정산할 수 있다.

## 검증과 잔여 경계

정확한 종료시각, 거절 시 보호 유지, rollback, 재접속/재시작, 시각 역행, 동시 활성화, 역방향 공격 잠금, 도착 시 관계 변경, 대상 삭제와 귀환, 정찰 whitelist를 테스트한다. 로컬 runtime은 공유 Mac 잠금 대기이며 NOT_RUN이다. 행군의 dueAt 복구 및 target account advance 전체는 행군 통합 테스트에서 별도로 검증한다.
