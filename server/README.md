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

## Apple·Google 계정 연결 (#634)

[ADR0080](../docs/adr/0080-release-login.md)의 2단계 서버 API다. 실제 공급자 등록,
Unity 로그인 화면과 웹 삭제 화면은 별도 배포가 필요하다. 등록된 provider/client ID만
허용하며 설정이 비어 있으면 소셜 인증은 닫힌다. 이메일로 계정을 합치지 않는다.

1. `POST /v1/auth/social/challenge`에 `provider` (`apple`/`google`), `clientId`,
   `purpose` (`link`/`login`/`delete`)를 보낸다. `link`에는 기존 bearer가 필요하고,
   `login`에는 새 기기의 P-256 SPKI 공개 키를 base64 `publicKey`로 보낸다.
2. 응답의 `nonce`를 공급자 인증 요청에 그대로 사용한다. ID token의 nonce도 같은
   값이어야 한다. 공급자 SDK가 nonce를 해시하는 경우 이 계약에 맞게 요청을 구성한다.
3. `POST /v1/auth/social/{purpose}`에 `challengeId`, `nonce`, `identityToken`을
   보낸다. Apple은 같은 인증의 `authorizationCode`도 필요하다. `link`에는 같은
   bearer를 붙인다. `login`은 UTF-8 `sow-social-login-v1\n{challengeId:D}\n{nonce}`를
   새 기기 키로 ECDSA/SHA-256 서명한 64바이트 P1363 값을 base64 `signature`로 보낸다.
   `delete`에는 명시적 `confirm: "delete"`가 필요하다. 사용한 challenge는 재사용할 수 없다.

로그인은 미리 연결한 계정만 복구한다. 삭제 유예 중 소셜 로그인은 거부하며 기존
기기 키 로그인만 삭제를 취소한다. `GET /v1/account/deletion`은 삭제 API 안내 JSON이다.
미연결 게스트는 앱 안의 `POST /v1/account/delete`를 사용한다.

삭제 응답은 로컬 접근 차단·영구 삭제 예약과 Apple 폐기 작업의 내구성 저장을 뜻한다.
Apple 폐기 성공을 뜻하지 않는다. 별도 worker가 기본 60초마다 처리하며 실패는
5분 뒤 재시도한다. 실패한 연결은 기존 Apple 권한을 폐기하지 않는다. 폐기 대기 중인
Apple 신원은 재연결을 거부한다. 계정 제거 후에도 계정 UUID 없는 암호화 폐기 작업은
성공할 때까지 남는다. 운영자는 폐기 대기 건수·최장 대기 시간을 확인해야 한다.

Host 환경의 `Auth__Social__Providers__N__Provider`, `ClientId`, 선택적 `RedirectUri`,
Google `AllowedPresenters__N`을 등록한다. Apple은 `Auth__Social__Apple__TeamId`,
`KeyId`, `PrivateKeyPem`과 별도 32바이트 hex `Auth__Social__CredentialKey`가 필요하다.
키를 잃거나 바꾸면 기존 암호문을 폐기할 수 없으므로 보존·재암호화 절차 없이 교체하지
않는다. 폐기 대기 작업이 있는 동안 암호화 키·Apple 서명 설정·해당 client ID를
제거하지 않는다. `.env.example`은 빈 항목만 제공하며 Compose가 자동 전달하지 않는다.

## 역사 검증 기록

소스 `9fe6392f`: SDK 10.0.403, 별도 PostgreSQL/Redis, 서버 테스트 20/20 통과,
실패 0·skip 0. `artifacts/server-582/test-final.log`와 TRX가 로컬 실행 증거다.
후속 도메인 구현과 실제 Unity 연결은 이 결과에 포함하지 않는다.
