# Local authoritative server

이 서버는 ADR0077 stage②의 개발용 계정·영지 시제품이다. 정식 인증·배포는 포함하지
않는다. 실행 환경·비밀 설정은 기존 [서버 런북](../docs/runbooks/server-stage2.md)을
따른다. 비밀 값과 bearer를 로그에 남기지 않는다.

## 도메인 추가 인계 (#582)

공통 계약은 [ADR0078](../docs/adr/0078-server-domain-command-transactions.md)과
`src/SowSiege.Server.Application/DomainHandlers.cs`에 있다. 새 도메인은 별도 파일에서
`IDomainHandler`를 구현하고 Host의 `builder.Build()` 전에 아래 한 줄로 등록한다
(`TrainingHandler`는 후속 구현의 예시 이름이며 이 PR에는 없다).

```csharp
builder.Services.AddSingleton<IDomainHandler, TrainingHandler>();
```

- handler는 별도 writer·DB 연결·시계·RNG를 만들지 않는다. `DomainContext`의 계정,
  도메인 JSON과 서버 UTC highwater를 사용하고, 지갑 변경은 복사한 `MetaState`로 반환한다.
- `Advance`는 도메인 이름 ordinal 순서로 먼저 정산한다. 그 뒤 선택한 `Execute` 또는
  기존 영지 명령을 실행한다. 명령이 있으면 전체 도메인의 `AfterCommand`를 한 번
  호출하고 마지막에 순수 `View`로 최종 상태를 표시한다. `AfterCommand` 기본값은
  no-op이며 다른 도메인 지출 후 보류 환불 채우기처럼 즉시 정산할 일만 override한다.
  시간 진행·완료 처리는 이 hook에서 반복하지 않는다.
- `Execute`의 작업 ID는 `context.CommandId`에서 유도한다. payload는 도메인별 typed
  계약으로 알 수 없는 필드를 거부한다. `StateVersion`·`ContentHash`는 저장값과 일치해야 한다.
- 모든 상태·자원 delta·이벤트·receipt·outbox는 공통 계정 행 잠금과 같은 transaction에
  저장된다. 중복 commandId는 이전 응답 그대로이고 시간도 다시 정산하지 않는다.

새 API는 `GET /v1/account`, `POST /v1/account/commands`이며 명령 필드는
`commandId`, `expectedVersion`, `domain`, `operation`, `payload`이다. 응답은
`done`, `reason`, `estate`, `domains`, `resources: [{id, amount}]`를 제공한다.
기존 `/v1/estate` 응답·명령·receipt hash는 유지하지만 등록된 도메인의 시간 정산도
같은 transaction에서 수행한다.

서버 DB는 시작 시 advisory transaction lock 아래 schema 1→2로 확장한다. 기존
Core v6 payload와 receipt를 재작성하지 않으며 이관 실패는 rollback한다. 미지원 schema,
저장된 도메인의 handler 누락, state version/content hash 불일치는 fail-closed한다.
handler를 제거해 기존 상태를 무시하는 배포는 허용하지 않는다.

훈련·연구·영웅의 시간/비용/완료 정책 및 환불 상한 초과 정책은 공통층에 없다.
생산 보관 상한의 논리 시간과 작업 종료의 UTC highwater는 다르므로 작업 완료 시각은
`context.UtcMilliseconds`로 판정한다. 대기 작업의 시작은 재접속 시각이 아니라
선행 작업의 실제 완료 시각에서 이어야 한다.

## 검증 기록

소스 `9fe6392f`: SDK 10.0.403, 별도 PostgreSQL/Redis, 서버 테스트 20/20 통과,
실패 0·skip 0. `artifacts/server-582/test-final.log`와 TRX가 로컬 실행 증거다.
후속 도메인 구현과 실제 Unity 연결은 이 결과에 포함하지 않는다.
