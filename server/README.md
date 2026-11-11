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

### 웹 삭제 요청 전용 API (#668)

연결 계정용 웹 클라이언트는 앱 설치·앱 bearer 없이 다음 순서로 삭제를 요청한다.

1. `GET /v1/account/deletion`으로 경로·유예·미연결 게스트 안내를 읽는다.
2. `POST /v1/account/deletion/challenge`에 `{provider, clientId}`를 보낸다. 서버가 등록된 공급자/클라이언트만 허용하고 목적을 `delete`로 고정한다.
3. 반환된 nonce를 사용하는 새 Apple/Google 인증을 진행한다. 공급자 토큰의 nonce·서명·issuer·audience·만료는 기존 로그인 2단계 검증을 따른다.
4. `POST /v1/account/deletion/requests`에 `{challengeId, nonce, identityToken, confirm:"delete"}`를 보낸다. Apple은 같은 인증의 `authorizationCode`도 필요하다. 성공 응답은 `{purgeAt}`이며 세션·새 토큰·계정 ID를 반환하지 않는다.

명시적 확인이 없으면 400이고 proof는 소비하지 않는다. 만료·재사용·다른 목적·미연결 신원의 proof는 거부한다. `accountId`, `email`, `purpose`, `publicKey`, `signature` 등 정의되지 않은 입력은 400이다. bearer만으로 웹 삭제를 요청할 수 없고, bearer가 있어도 삭제 대상은 검증된 기존 소셜 연결로만 결정한다. 요청/응답과 공급자 proof를 브라우저 로그·분석 이벤트에 남기지 않는다. challenge/삭제 응답은 `Cache-Control: no-store`다.

challenge 실패는 기존 IP 제한을 따르고, 기존 프로세스 요청 제한도 유지한다. 잘못된 proof의 재시도는 새 challenge부터 시작한다. 기존 `/v1/auth/social/*`는 호환 경로로 유지한다. 웹 삭제도 앱과 같은 삭제 예약·토큰 폐기·연맹 탈퇴·Apple 폐기 큐를 사용하며, 성공 응답은 Apple 폐기 완료가 아니다.

이 API는 JSON 서버 계약이며 공개 웹 화면이 아니다. Development 가드와 미연결 게스트의 앱 내 삭제 안내를 유지한다. 실제 브라우저 OAuth 화면·반환 URI·스토어 앱/개발자 표시·삭제/보존 정책 안내·공개 URL/Play Console 등록 및 실제 공급자 키 확인은 휴대폰 빌드/배포 단계에 남는다. 실제 식별자나 비밀 값을 PR에 기록하지 않는다.

### 계정 삭제 작업 관측 (#684)

`GET /v1/admin/account-purge`는 기본 비활성이며 계정 식별자 없이 대기·기한 경과·재시도 건수와 누적 결과만 반환한다. Host 환경에 `Auth__PurgeAdmin__Enabled=true`와 별도 32바이트 hex `Auth__PurgeAdmin__Secret`을 주입하면 활성화된다. 요청은 `X-Purge-Admin-Key` 헤더로 같은 secret을 전달하며 일반 사용자 bearer는 관리자 권한이 아니다. secret을 URL·로그·저장소에 넣지 않는다. 응답은 `Cache-Control: no-store`다. 외부 활성화 전에 TLS와 프록시 배포 구성을 확인한다. Compose `.env`에 넣는 것만으로 Host에 전달되지는 않는다.

`Auth__PurgeIntervalMinutes`(기본 60), `Auth__PurgeRetryMinutes`(기본 60, 0 초과·1440 이하), `Auth__PurgeCycleCandidates`(기본 1000, 1 이상)는 운영 설정이다. 주기 간 커서를 보존하고 큐 끝에서만 초기화한다. 프로세스 재시작 시 커서는 처음부터 시작한다. 계정별 실패가 뒤 계정 처리나 별도 신고 만료 정리를 막지 않도록 분리한다.

`duePending`은 재시도 대기를 포함한 삭제 기한 경과 건수다. `retryWaiting`·`retryReady`는 그 부분집합이다. `failed`는 커밋된 실패 기록 수, `retryFinished`는 실제 시도 시작 때 이전 실패가 있었던 요청이 다시 성공 또는 실패로 끝나 기록된 수, `recovered`는 그중 성공한 계정 수다. 최초 실패·취소·skip은 `retryFinished`에서 제외한다. DB 장애로 기록 자체가 실패한 시도는 집계되지 않는다. 계정별 `attempts`는 실패 횟수이며 복구·영구삭제 시 해당 행과 UUID를 제거한다. 통계 조회는 한 SQL snapshot을 사용한다.
