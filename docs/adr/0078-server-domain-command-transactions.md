# ADR0078 · 서버 도메인 명령과 확장 저장

상태: 구현 제안, 사용자 승인 공통 선행 범위(#582). 2026-10-30.
ADR0077 stage② 뒤 병영(GDD15), 영웅(GDD16), 연구(GDD17)가 하나의 계정 지갑을
사용한다. 각각 별도 writer를 만들면 동시 소비·재접속 완료·중복 요청이 분리된다.

## 결정

기존 `/v1/estate` 응답·명령 JSON·receipt payload hash와 Core v6 blob을 유지한다.
새 `/v1/account/commands`는 commandId, expectedVersion, domain, operation,
payload(object)를 받는다. 필수 필드 누락·알 수 없는 최상위 필드는 거부한다.
각 handler는 typed payload를 엄격히 파싱한다. 요청 ID는 모든 도메인과 기존 영지
사이에서 공유한다. 새 해시는 `account-v1:` 접두사를 붙여 legacy와 혼동하지 않는다.
동일 ID의 다른 요청은 409, 같은 요청은 이전 응답 그대로이며 시계도 다시 진행하지 않는다.
새 응답은 estate, domains와 Unity JSON용 resources 배열을 제공한다.

공통 AccountStore만 계정 행을 FOR UPDATE로 잠그고 단일 transaction을 쓴다.
Core 영지 정산 → Domain 이름 ordinal 순 전체 Advance → 선택 Execute → 계정·도메인
상태·지갑 delta·도메인 이벤트·receipt·outbox를 함께 commit한다. 기존 estate endpoint도
같은 Advance를 통과한다. handler는 I/O나 RNG를 호출하지 않고 원본 MetaState를
변경하지 않는다. 변경은 복사한 값으로 반환한다. CommandId를 작업 ID의 원천으로 쓴다.
순서는 재현 가능성을 위한 계약이지 연구·영웅 효과의 시간 구간 산술을 구현한 것이 아니다.
효과 변경 시각별 생산 분할은 해당 도메인 구현에서 별도로 검증해야 한다.

서버 DB만 schema 1→2로 이관한다. Core codec6·동결 콘텐츠·역사 리플레이는 그대로다.
기존 advisory transaction lock 아래 미래 schema를 먼저 거부한 뒤 도메인 JSON,
도메인별 version/content hash, 이벤트 원장, 계정 작업 UTC high-watermark 테이블을
추가한다. 이관 실패는 전체 rollback한다. 기존 accounts payload를 다시 encode하거나
빈 도메인을 모든 역사 계정에 미리 덮어쓰지 않는다. 명시적 account INSERT 컬럼을 쓴다.
저장된 domain의 handler가 없으면 상태를 버리지 않고 요청 전체를 409로 거부한다.
state version/content hash 불일치도 자동 해석하지 않는다. 도메인 이관은 별도 명시 승인이다.

작업 시간은 `max(server UTC ms, estate UTC highwater, job UTC highwater)`이다.
생산 보관 상한 때문에 버려지는 EstateClock 논리 초와 분리한다. 클라이언트 시각은
입력으로 받지 않는다. 시각 역행은 진행시키지 않으며 절대 종료 시각을 보존한다.
대기 작업은 재접속 시각이 아니라 선행 작업의 실제 종료 시각부터 계산해야 한다.
공통층은 훈련/연구 시간·완료·환불 정책을 알지 못한다.

## 제외와 검증

병력·영웅·연구 행동, 환불 지갑 초과 정책, 도움·유료 가속, 실계정 인증·배포 없음.
기존 영지 계약 회귀, v1→v2 payload 보존, 미래 schema/이관 rollback, 도메인 누락·해시
불일치, UTC 역행·생산 상한 분리, 동시 지출·receipt·late failure 원자성을 검사한다.
코드 작성 시점 로컬 무거운 검사는 공유 Mac 슬롯 대기이며 실행 결과는 PR에 기록한다.
