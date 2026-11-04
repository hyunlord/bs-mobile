# #685 직접 writer / 현재 정책 HTTP 부하 도구 초안

2026-11-04 기존 SDK 10.0.403으로 빌드하고 아래 첫 측정을 수행한 초안이다. 실행 SHA·통과·실패 범위는 마지막 절과 CSV를 따른다. 새 패키지는 없으며 Infrastructure의 기존 Npgsql을 사용한다.

## direct 모드 측정 범위

실제 `AccountStore.CreateAsync`로 새 계정을 만들고 반환된 토큰을 `AuthenticateAsync`로 확인한다. 준비 단계는 최대 `--seed-parallel`개로 제한하고 준비 시간은 본 측정과 분리한다. 토큰은 메모리에만 존재한다. 계정별 warmup 명령과 동일 commandId 재전송을 검증한다.

측정은 실제 `AccountStore.ExecuteAsync`에 `EstateCommand(...,"claim-production",null)`을 전달한다. 공통 transaction, 계정 행 잠금, 버전 검사, 상태/receipt/outbox 저장을 통과한다. HTTP, 로그인/서명 API, rate limiter, 소셜 공급자, 등록 도메인 handler, world/city/gathering 경로는 포함하지 않는다. HTTP 정책을 바꾸거나 우회하는 호스트를 만들지 않는다.

기본 계정의 반복 생산 받기는 빈 자원/no-op/규칙 거절일 수 있다. `Committed`는 새 명령 커밋 수이고 `Done`과 `Refused`는 별도 수치다. `Done=true`도 재화 증가를 뜻하지 않는다. 성공한 게임 작업, 전체 서버 대표 처리량, 사용자 수용량으로 보고하지 않는다. 후속 실제 도메인 작업·연맹 경합·월드 처리는 별도 시나리오와 정합성 기준이 필요하다.

`--concurrency 1000` 또는 `10000`은 각각 서로 다른 계정 worker 1000/10000개를 장벽에서 출발시킨다는 뜻이다. worker마다 하나의 명령/검증 호출을 진행하며 완료되면 다음 명령을 발행한다. 계정 총수만 준비하고 적은 worker로 실행하는 방식이 아니다. Npgsql 풀은 별도 `--pool`로 제한하므로 DB에서 동시에 실행되는 쿼리 수와는 다르다. `peakInFlight`가 목표에 미달하거나 일부 worker가 측정 replay까지 수행하지 못하면 실패로 보고한다. 한 순간의 peak만으로 지속된 목표 DB 동시성을 주장하지 않는다. 시간별 inFlight와 연결/active 표본을 반드시 함께 제시한다.

각 worker의 첫 측정 명령은 동일 ID로 한 번 재전송하여 응답 바이트 일치를 검증한다. replay는 새 명령 처리량에서 제외되지만 실행 시간에는 포함된다. 종료 후 모든 계정의 DB 버전이 worker가 관측한 마지막 버전인지 확인한다. 오류 때 전체 측정을 취소하며 자동 재시도하지 않는다.

## 안전 경계와 실행 준비

- localhost/127.0.0.1/::1, 포트 **55441**만 허용한다. `--database`와 연결 문자열 Database가 정확히 일치하고 DB 이름은 `sow_load_` 접두사여야 한다.
- 사용자가 지정한 전용 DB가 이미 존재해야 한다. 도구는 DB를 생성하거나 삭제하지 않는다. 매 실행 임의 고유 schema를 만들고 보존한다. 기존 다른 schema를 초기화하지 않는다.
- Compose 프로젝트는 **codex-account**를 사용한다. 다른 세션 DB/컨테이너를 사용하거나 종료하지 않는다.
- `$MV/agent-busy.lock`을 noclobber로 획득한 소유 세션만 낮에 실행한다. 23:00~07:00에는 빌드·Docker·실제 부하 실행 금지, 22:30까지 정리한다. 도구 자체는 공유 잠금 소유권을 대신 관리하지 않는다.
- 출력 폴더 파일은 CreateNew로 열어 기존 증거 덮어쓰기를 거부한다. 파일·DB 자동 삭제는 없다. schema 이름은 manifest에 기록한다.
- 연결 문자열은 `SOW_LOAD_POSTGRES` 환경변수로만 전달한다. 셸 명령 인자, 로그, 저장소에 비밀번호를 넣지 않는다. 예외는 타입과 SQLSTATE만 출력한다.
- 전용 DB의 다른 측정 작업이 없어야 `pg_stat_database.deadlocks` 증분을 이번 실행에 귀속시킬 수 있다.

재현할 명령 예시(전용 DB·소유 잠금·낮 시간 선행):

```sh
# SOW_LOAD_POSTGRES는 비밀 저장 경로에서 별도로 주입한다.
# 아래 dotnet은 기존 읽기 전용 10.0.403 SDK 경로로 실행한다.
dotnet run --project server/tools/SowSiege.Server.Load -c Release -- \
  --mode direct --database sow_load_account_685 --concurrency 1000 --seconds 60 \
  --pool 64 --seed-parallel 8 --content-root /path/to/bs-mobile \
  --output /path/to/isolated-output/run-1000 --revision EXACT_SHA
```

10000 측정은 동일 조건에서 concurrency와 새 출력 경로만 바꾼다. 준비 포함 전체 30분, 개별 측정 호출 30초, DB command 30초 제한이다. 준비 실패/시간 초과는 보존하고 10000 동시성 달성으로 보고하지 않는다. CPU/메모리 여유가 부족하면 새 실행을 시작하지 않는다.

## 출력 해석

- `manifest.json`: 준비 시작, SHA(호출자가 제공), 콘텐츠 hash, runtime, CPU 수, schema, 설정. summary가 없으면 완료 관문 미충족이다.
- `samples.jsonl`: 250ms 목표 간격의 pg_stat_activity 표본, 연결/active/Lock wait/blocked 수, 최장 transaction 나이, 프로세스 CPU 누적초·working set·managed bytes, 현재 inFlight/누적 커밋. 실제 간격은 UTC에서 계산한다. 관측 쿼리는 별도 연결 1개를 쓴다.
- `summary.json`: 준비/측정+drain 시간, 새 명령 처리량, done/refused/replay/error, DB deadlock 증분, 지연 histogram p50/p95/p99 **bucket 상한**. 정밀 percentile이 아니다. 명령 latency에는 connection pool 대기부터 응답까지 포함되며 replay latency는 포함하지 않는다.
- 표본 잠금 대기 인원/비율을 정확한 누적 lock wait milliseconds나 개별 쿼리 대기시간으로 주장하지 않는다. 풀 대기를 DB 행 잠금 대기로 해석하지 않는다.
- CPU는 직접 Store와 부하발생기가 같은 프로세스인 합계다. DB CPU/메모리는 별도 소유권 허용 관측이 필요하며 이 도구가 측정했다고 보고하지 않는다.
- 출력 `CORRECTNESS_PASS_CAPACITY_UNRATED`는 정합성/참여 검사만 통과했다는 뜻이다. 사전 합의된 TPS/p95 목표가 없으므로 용량 합격선을 만들지 않는다.

## HTTP 증거 분리

`--mode http`는 별도로 실행 중인 실제 Host의 `/v1/estate/commands`에 계정별 한 요청씩 보낸다. 현재 prototype 60회/분, guest/auth 정책을 변경하지 않는다. 429는 정책 포화이며 정합성 실패가 아니다. 401, 5xx, 기타 비정상 응답, 전송 오류, 최종 버전 불일치는 실패다. HTTP 수락량과 direct-store 수치를 합산하지 않는다. 기존 smoke.mjs는 기능 확인용으로만 사용한다.

Host 소유자가 먼저 전용 DB 안의 새 `load_*` schema를 만들고 실제 Host를 다음 조건으로 실행한다. 도구는 Host를 spawn/kill하거나 schema를 만들지 않는다.

- `ConnectionStrings:Postgres`에는 localhost:55441의 지정 `sow_load_*` DB, 정확한 `Search Path=load_*`, 고유 `Application Name=sow-load-host-*`를 넣는다. 부하 도구의 SOW_LOAD_POSTGRES도 같은 DB/Search Path여야 한다.
- Host의 정상 Development/prototype 설정, 콘텐츠 root, Domains seed key, auth pseudonym key, Redis 등 기존 실행 선행 조건을 유지한다. `Logging:LogLevel:Default=Warning`을 실행 설정에 기록해 요청별 정보 로그 폭주를 막되 오류는 보존한다. HTTP limiter/인증 정책은 바꾸지 않는다.
- 다른 트래픽·측정이 없는 격리 Host를 사용한다. URI는 명시적 포트가 있는 loopback HTTP origin만 허용하고 redirect/proxy/cookie 사용을 차단한다. 도구는 OS 권한·ulimit·네트워크 설정을 변경하지 않는다.

준비는 AccountStore.CreateAsync로 legacy session을 발급한다. 이 fixture에는 **world city/CitySecurity 생성 레코드가 없다**. 현재 영지 명령 경로는 해당 레코드를 요구하지 않으며, 실제 Host의 등록된 domain handler가 요청을 처리한다. 이는 HTTP 계정 생성·로그인 부하, 완전한 신규 플레이어·월드 시나리오의 증거가 아니다. 준비 토큰은 메모리에만 둔다.

첫 계정의 HTTP 명령 한 개를 수락받고, 해당 account/command/version이 지정 schema에 실제 저장되었는지 확인한다. 따라서 잘못된 DB/schema의 Host로 측정이 진행되지 않는다. 이 요청과 seed 시간은 준비 단계다. 첫 요청이 429면 `PREFLIGHT_INCOMPLETE`와 exit 2로 종료하며 limiter를 재설정하거나 자동 재시도하지 않는다. 401은 오류로 종료한다.

```sh
dotnet run --project server/tools/SowSiege.Server.Load -c Release -- \
  --mode http --server-uri http://127.0.0.1:5085/ \
  --schema load_http685_1000 --server-application sow-load-host-http685 \
  --database sow_load_account_685 --concurrency 1000 --seconds 60 \
  --pool 64 --seed-parallel 8 --content-root /path/to/bs-mobile \
  --output /path/to/isolated-output/http-1000 --revision EXACT_SHA
```

HTTP/1.1을 명시하고 client MaxConnectionsPerServer를 worker 목표와 같게 둔다. `peakInFlightRequests`에는 client 연결 대기가 포함되므로 이것만으로 실제 접속 달성을 주장하지 않는다. BCL System.Net.Http의 ConnectionEstablished/ConnectionClosed 이벤트 ID를 집합으로 관리해 동시에 열린 연결 수를 별도 기록한다([Microsoft 설명](https://devblogs.microsoft.com/dotnet/dotnet-8-networking-improvements/)). 이벤트 미관측 시 actual TCP concurrency는 UNKNOWN이며 목표 미달은 `PARTIAL_CONCURRENCY_TARGET_UNMET`/exit 2다. 열린 idle keep-alive 연결도 포함되므로 수천 개 동시 명령 실행이나 지속된 접속 목표를 증명하지 않는다. 250ms 연결·pending 표본과 순간 peak를 함께 보고한다. 이벤트 계측의 실제 .NET 10 동작도 실행 관문이다.

429는 Retry-After delta/date를 존중하고, 없거나 100ms 미만이면 명시적인 client backoff 100ms를 사용한다. 측정 종료까지 Retry-After를 지킬 수 없으면 해당 worker가 종료한다. backoff는 InFlight에서 제외되며 지속 동시성 감소에 영향을 준다. 이 값은 부하발생기 설정이며 게임 수치나 서버 제한 변경이 아니다. 자원 부족/소켓 오류를 보존하며 OS 제한을 올려 숨기지 않는다.

수락된 각 worker의 첫 명령은 같은 ID로 재전송한다. 429가 나오면 같은 replay를 유지하고, 시간 내 수락되지 않은 replay는 `replayCoverageAccounts`/`allAccountReplayVerified=false`로 공개한다. 전체 계정 replay 관문 통과로 주장하지 않는다. 최종 DB 버전 검사는 모든 fixture 계정에 적용한다. HTTP 지연 histogram은 수락·429 등 모든 HTTP 응답을 포함한다. CPU/RSS는 **부하 client 프로세스**만이며 Host/DB CPU가 아니다. DB 표본은 지정 Host application_name을 필터링한다.

## 독립 연결 진단 모드와 오류 분류

HTTP 선택 인자 `--connection-mode shared|per-account`의 기본값은 기존 `shared`다. shared는 한 HttpClient/handler 풀을 공유하며 유휴 연결 재사용 때문에 worker 10000개가 물리 연결 10000개를 보장하지 않는다. `per-account`는 계정마다 HttpClient/handler를 하나씩 만들고 각 handler의 MaxConnectionsPerServer를 1로 설정한다. 여러 모바일 클라이언트의 독립 연결풀을 모사하는 별도 실험이며 실제 단말 10000대를 뜻하지 않는다. 모든 client 객체는 준비 시간에 만들고, preflight 계정을 제외한 연결은 측정 요청에서 열린다. 정상 종료·오류·준비 중 부분 생성 실패 모두 생성된 client/handler를 dispose한다. shared/per-account 모두 같은 HTTP/1.1, 30초 timeout, 서버 정책, Retry-After/100ms backoff를 유지한다.

`--server-revision FULL_40_HEX_SHA`를 주면 manifest와 summary에 서버 SHA를 별도로 기록한다. `--revision`은 부하 client SHA다. 서버 SHA는 운영자가 제공한 값이며 자동 Host 바이너리 검증이 아니다. 생략하면 serverRevision=null/UNKNOWN으로 남기고 client SHA와 같다고 가정하지 않는다. 이미 실행 중인 Host를 유지하면서 client만 바꾼 진단에서는 반드시 서로 다른 정확한 SHA를 기록한다.

```text
--mode http --connection-mode per-account --revision FULL_CLIENT_SHA --server-revision FULL_RUNNING_HOST_SHA
```

HTTP summary의 `errorCounts`는 고정 범주 및 BCL enum/type 이름과 횟수만 기록한다. Transport에는 HttpRequestError와 최대 8단계 inner exception에서 확인한 SocketErrorCode를 붙인다. Timeout, Cancellation, CancellationUnknownOrigin, Json, InvalidResponse, VersionMismatch, ReplayMismatch, FinalVersionMismatch를 구분한다. timeout 원인이 확인되지 않은 취소를 임의로 timeout으로 바꾸지 않는다. 예외 Message/StackTrace, URL, 요청·응답 본문, 계정 ID, 토큰, 키는 오류 분류에 쓰지 않는다. HTTP 401/5xx/기타 상태와 429는 기존 별도 계수를 유지한다.

첫 shared 10000 실험의 실패 증거는 보존한다. 낮은 연결 peak나 오류 하나만으로 FD 한도·서버 용량을 원인으로 확정하지 않는다. per-account는 연결 구조와 오류 관측을 명시적으로 바꾼 진단 실험이며 통과를 얻기 위한 같은 설정 재시도가 아니다. 동일 실패가 다시 발생하면 측정을 멈추고 관측된 오류 종류를 보고한다. 새 진단 코드의 빌드·실행 결과는 아래 첫 측정 절에 별도로 기록한다.

## 검증 상태와 남은 관문

1. 기존 SDK의 빌드 경고0·오류0, 기존 영지 검사5/5 PASS. 진단 client `2e72a3fc`도 로컬 빌드 경고0·오류0. 전체 서버 CI는 PR에서 별도로 확인한다.
2. 잘못된 포트/host/DB명/DB불일치/누락옵션/출력 충돌을 DB 변경 전 거부하는지 확인.
3. 허용된 작은 개발 fixture로 seed, receipt byte replay, final version, observer cancellation/error를 점검한 뒤 정식 1000→10000 실행. 현재 CLI는 정식 concurrency만 허용하므로 fixture 검사는 별도 테스트에서 구성한다.
4. Ctrl+C·pool exhaustion·observer 실패가 성공 요약으로 남지 않는지 확인. 실패 로그와 부분 표본을 보존한다.
5. 실제 실행 결과, 환경, SHA/콘텐츠 hash, 측정기의 포화 여부를 함께 기록한다. 같은 실패를 두 번 겪으면 재실행하지 않고 보고한다.
6. HTTP 모드의 잘못된 schema/URI/appname, preflight429/401, 수락+429+replay, Retry-After/backoff 제외, connection lifecycle 집계, 소켓 자원 부족, 제한시간 내 drain을 확인한다. 첫 실제 HTTP 결과는 아래에 있으며, 나열한 모든 오류 주입 시나리오를 검증한 것은 아니다.

HTTP 연결 계측 계약 확인(2026-11-04): [.NET 10 HttpTelemetry 원본](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Net.Http/src/System/Net/Http/HttpTelemetry.cs)의 `System.Net.Http`, `ConnectionEstablished`/`ConnectionClosed`, `connectionId` 이름을 확인했다. 이는 이벤트 계약 정적 확인이며 실제 실행에서의 이벤트 수신·연결 목표 달성 증거를 대신하지 않는다.

## 첫 측정 — 2026-11-04

정본 수치는 [CSV](measurements/2026-11-04.csv)에 보존한다. Mac16,5 / M4 Max 14-core / RAM36GB / macOS27.0.1(26A434), .NET SDK10.0.403/runtime10.0.14, PostgreSQL17-alpine(55441), Redis7-alpine(56385), 별도 localhost Host5085. 소유 잠금 아래 60초 입력 후 drain을 포함했다. DB pool64·seed 병렬8. HTTP는 Warning 로그·기존 전역60회/분·HTTP1.1이며 OS/서버 제한을 변경하지 않았다. 기기 식별자·키·토큰은 수치에 포함하지 않는다.

첫 네 실행의 client/Host source는 `a129d16c476e03cfd2c33b114c57dd62d9be7c55`다. HTTP 전에 같은 source의 Host와 도구를 다시 빌드했다. HTTP 전용 schema의 실제 명령/버전 변경을 preflight로 확인했으며 Redis도 이 프로젝트의 전용 컨테이너를 사용했다. 실제 Apple/Google·월드 배정·도메인 혼합은 측정하지 않았다.

- direct1천/1만: 새 영수증 처리 약2799/3106건/s, 오류0·deadlock0, 전체 계정 재전송/최종버전 일치. 실제 게임 효과 Done은 모두0이었다. 초기 계정의 빈 생산 수령 거절 경로 비용이며 자원 지급·정상 게임 명령 처리량으로 해석하지 않는다.
- shared HTTP1천: 연결1천 관측·오류0. 새 명령116건, 429 585752건. rate limit이 지배했고 재전송은3계정만 검증했다. 전 계정 재전송 검증으로 확대하지 않는다.
- shared HTTP1만: **FAIL**. 연결9860·peak pending9861, 오류1, 새 명령119건·429 3378648건. 최초 도구는 예외 종류를 남기지 않아 오류 원인은 UNKNOWN이다. 실패를 지우거나 1만 연결 통과로 바꾸지 않는다. Host 로그는 비어 있었으며 이것만으로 서버/클라이언트 원인을 확정하지 않는다.
- 250ms 표본에서 direct1만의 최대 Lock 대기1, 나머지는0이었다. blocked는 모두0·deadlock0. 관측되지 않은 짧은 대기나 다른 workload의 경합까지 없다고 주장하지 않는다. p95는 histogram 구간 상한이며 HTTP 지연은 대다수429를 포함한다.

첫 실패 뒤에는 비밀 없는 오류 분류와 `per-account` 연결풀을 별도 진단 모드로 추가했다. 공유 풀의 연결 재사용과 독립 클라이언트의 동시 접속을 구분하기 위한 변경이며, timeout/backoff/게임 규칙·서버 rate limit은 유지한다. 동일 실패가 두 번이면 측정을 중단하고 보고한다. 최초 실패의 상세 원인을 후속 결과만으로 역추정하지 않는다.

### 두 번째 1만 연결 진단과 중단

client `2e72a3fccee0384fd5b7f567a03a830dc674285c`, 기존 Host `a129d16c476e03cfd2c33b114c57dd62d9be7c55`에서 명시적 per-account 모드를 실행했다. Host는 재시작하거나 limiter를 초기화하지 않았다. 계정10000개 준비 후 실제 연결/peak pending은9409, 새 거절 명령59, 429는2804414, 오류591·deadlock0이었다. 오류 분류는 모두 `Transport / HttpRequestException / ConnectionError / ConnectionReset`이다. 연결 재설정의 발생 지점·근본 원인은 미확정이며 최초 shared 실패의 원인도 UNKNOWN으로 보존한다.

**FAIL**: 목표1만 연결에 미달했다. 두 번째 실패 후 추가 측정을 중단하고 소유 Host를 종료했다. timeout·rate limit·OS 설정을 완화하지 않았다. 이후 네트워크/Host 자원 원인 조사는 별도 관측 계획이 필요하며 현재 결과로 실서비스 용량을 주장하지 않는다. 실패 원자료와 표본은 로컬에 보존한다.

## 전체 서버 혼합 부하 후속 계획 — 실행 금지 / NOT_RUN

현재 상태: 첫 다섯 실행의 성공·실패 및 원자료를 보존한다. 두 번째 1만 per-account 진단은 연결 9409·ConnectionReset 591로 실패했다. 같은 실패 반복 중단 규칙에 따라 **추가 측정하지 않는다**. 최초 shared 오류의 원인은 UNKNOWN이며 후속 오류로 역추정하지 않는다. 아래는 원인 조사와 재개 결정 이후 사용할 계획이다.

### 선행 조건과 합법적 fixture

- #590 훈련, #610 연구의 영지 효과, #692/#693 후속 훈련 연구효과·연맹 도움의 최종 병합 SHA와 통합 테스트를 고정한다. 미병합 기능은 실행 행렬에서 제외하고 전체 서버 완료로 보고하지 않는다. client/Host/Core·콘텐츠·DB schema·설정 버전을 각각 기록한다.
- 신규 fixture는 실제 `POST /v1/auth/guest`로 기기 P-256 공개키를 등록하고, 반환 토큰으로 실제 API를 사용한다. Host의 정상 AccountStore 생성 경로에서 도시 배정·도시 보호 상태 생성까지 확인한다. 키·토큰은 비공개 환경에만 둔다. SQL 지갑 증액·도시 삽입·가짜 전투 보상 대신 정상 비용 지불·시간 경과·서버 검증 보상으로 준비한다. 준비 시간·명령은 측정에서 분리한다.
- **현재 준비 제약:** 단일 실제 IP의 guest 5회/시간을 유지하면 새 1000/10000계정 발급에 최소 약200/2000시간의 발급 창이 필요하다. 정책 우회용 IP 위장·limiter 초기화·guest 제한 상향을 하지 않는다. 이미 정당하게 발급된 격리 테스트 cohort가 없으면 이 단계는 BLOCKED다. 기존 직접 seed fixture를 실제 guest/도시 검증으로 바꾸어 부르지 않는다. 계정 준비 방식을 별도로 결정하기 전 혼합 부하를 시작하지 않는다.

### 단계와 시나리오

먼저 소수 계정으로 아래 정합성 시나리오를 실행한다. 이후 1000계정, 검토 후 10000계정을 별도 실험으로 진행한다. 모든 계정이 worker 하나를 가지며 각 계정의 명령은 직렬이다. 실제 공급자 자격증명 없는 Apple/Google은 fixture 검증으로만 표시한다.

| 시나리오 | 실제 경로와 핵심 관문 |
|---|---|
| 인증·영지 | 기기 challenge/signature 로그인·refresh, 생산 수령·건설; 세션 회전, 버전, 자원 원장 일치 |
| 연구·모집 | 충족된 연구 시작/완료, 정상 재화로 모집; 비용·결과·보상 단 한 번, 같은 commandId replay 불변 |
| 훈련·연구효과 | 활성/대기 등록·취소·완료, 연구 경계 전후 승격; 오프라인/단계 정산 동등, 예약 병력·환불 보관함 보존 |
| 연맹 분산/경합 | 정상 가입·도움·역할·탈퇴; 다수 연맹 분산군과 정원 내 한 연맹 집중군 분리. 도움/삭제 경쟁은 연맹→계정 UUID 오름차순 잠금, 정원·단일 leader·요청자 버전/outbox 검증 |
| 월드 | 실제 배정 도시의 행군·채집·정찰·보호막; 월드 담당과 현재 잠금 순서·도착/귀환 계약 확인. 병력·자원 보존, 도착·취소·재전송 중복 정산 금지 |

행렬별 비중은 실제 이용 근거가 없으므로 대표성 가설로 명시하고, 실행 전 명령 목록·seed·계정 역할을 고정한다. HTTP 전역60회/분과 인증 제한은 유지한다. 429·수락량·실제 효과 성공·규칙 거절을 분리한다. 직접 store 혼합 실험이 필요하면 별도 결과로만 기록하고 HTTP 수용량과 합산하지 않는다.

### 관측·시간·중단

250ms 연결/진행 요청/DB active·Lock·blocked 표본을 보존한다. 연결 peak 외에 전체 측정 구간의 목표 동시성 유지 비율·최저값·중앙값을 보고하며, client queue·idle 연결·backoff·실행 중 DB 쿼리를 구분한다. 잠금 표본은 정확한 대기시간이 아니다. client/Host/DB CPU·메모리를 별도로 관측한다.

실험당 입력60초와 drain을 구분하고 준비 포함30분 상한을 둔다. 낮 소유 잠금, 22:30 종료, 여유공간50GiB 조건을 유지한다. 오류·deadlock·원장 불일치·자원 부족은 중단/실패로 보존한다. 429는 정책 포화, 연결 목표 미달은 부분이며 임의 TPS/p95 합격선을 만들지 않는다. 원인 미확정 실패 재실행·timeout/OS 한도 완화는 금지한다. 첫 다섯 실행을 새 실험으로 대체하거나 삭제하지 않는다.
