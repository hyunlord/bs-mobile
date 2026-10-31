# 서버 stage② 로컬 계정·영지 시제품

상태: 구현 중, 실행 관문 미확인. 관련: ADR0077, #575. 2026-10-30.

## 범위와 권위

신규 계정 생성 → r8 영지 건설/생산 받기 → 재접속/서버 재시작만 검증한다.
월드맵, 전투 검증 worker, 기존 기기 저장 이관, Apple/Google, 결제는 포함하지 않는다.
`/prototype/accounts`는 Development + `Prototype:Enabled=true`일 때만 시작하는
로컬 시제품 API이다. Docker 포트는 127.0.0.1에만 공개한다. 인터넷 배포 금지.
토큰 원문은 발급 응답에만 제공하고 DB에는 SHA-256만 저장한다. 만료는 24시간.
이것은 정식 계정 복구/로그인 계약이 아니다. 세션은 PostgreSQL이 권위이며 Redis는
접속 presence 힌트만 가진다. Redis 소실로 재화나 인증이 사라지지 않는다.

클라이언트 명령은 ID·기대 버전·종류·건물 ID만 받는다. 재화·시각·완공 상태·저장
blob은 받지 않는다. 한 계정 행을 잠그고 Core의 반환 상태, 시계, 멱등 응답,
자원 변동 원장, outbox를 같은 트랜잭션에 기록한다. 같은 명령 재전송은 원래 응답을
반환하며 ID 재사용의 다른 payload/오래된 버전은 409이다. Core의 거부 결과도
시간 경과를 포함하므로 상태와 함께 커밋한다. HTTP 전송 실패 뒤 재전송해도
중복 지출하지 않는다. outbox 발송 worker는 다음 단계이며 현재는 내구성 기록이다.

DB UTC high-watermark와 논리 경과초·소수 밀리초를 저장한다. 서버 시각 후퇴 시
high-watermark를 내리지 않으며 초과 방치 시간은 기존 Core 상한으로 잘라 버린다.
초 단위 이하 재접속도 누적한다. 서버 OS 시각 신뢰/동기화 운영은 정식 배포 관문이다.
기존 기기 v6 파일은 받지 않고 내부 상태 직렬화에 codec v6만 재사용한다. DB schema
version은 별개이다. 콘텐츠 해시가 다르면 기존 계정을 자동 해석하지 않고 409로 막는다.

## 로컬 실행 (공유 Mac 슬롯 허용 후)

루트 SDK8은 변경하지 않는다. server/global.json만 SDK10.0.403을 고정한다.
PostgreSQL17/Redis7/.NET10 이미지는 최초 다운로드가 필요하다.

```sh
cp server/.env.example server/.env
# server/.env에 로컬 고유 암호를 넣는다. 파일은 gitignore 대상이다.
docker compose -f server/compose.yaml up -d --build
node server/tools/smoke.mjs
docker compose -f server/compose.yaml stop
```

smoke는 실제 새 계정·곡창 건설·중복 명령·host 재시작·Redis 재시작·62초 자연
경과 후 완공을 검사한다. 토큰은 프로세스 메모리에만 둔다. 재화 지급/시각 조작
API는 없다. 기존 승인 r8 데이터·경제값은 그대로다. `down -v`는 데이터를 지우므로
복구 검사에 쓰지 않는다. 실패하면 컨테이너와 volume을 보존하고 원인 로그만 남긴다.

## 검사

```sh
cd server
dotnet test tests/SowSiege.Server.Tests --filter 'Category!=Postgres'
# 별도 테스트 DB 연결을 환경변수로 제공한다. 암호를 로그에 출력하지 않는다.
dotnet test tests/SowSiege.Server.Tests --filter 'Category=Postgres'
```

`SERVER_TEST_POSTGRES`가 없으면 통합 검사는 실패한다(묵시적 skip 없음).
CI는 GitHub-hosted runner의 PostgreSQL service에서 검사한다. 공개 self-hosted
DGX runner를 사용하지 않는다. Core/Sim 공유 로더 추출은 역사 파서 검사를 그대로
통과해야 하며 기본 data·규칙·골든 해시 변경은 허용하지 않는다.

## 남은 제품 관문

로컬 신규 계정 시제품은 정식 인증/서버 운영 승인이 아니다. TLS, 실계정 소유권,
재해복구/PITR 복원, 인증 provider, 토큰 회전, schema migration, worker 전달 정책,
비용·부하 관문은 뒤 단계다. 이 PR은 기존 로컬 게임 저장과 실행 경로를 바꾸지 않는다.

## 구현 참고 원문 (2026-10-30 확인)

- [Npgsql 기본 사용](https://www.npgsql.org/doc/basic-usage.html): 공유 data source,
  위치 매개변수, 명시적 연결·트랜잭션을 사용한다.
- [ASP.NET Core 통합 검사](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0):
  WebApplicationFactory HTTP 검사는 실제 PostgreSQL과 함께 실행하며 Compose 프로세스
  재시작 증거를 대신하지 않는다.
- [.NET10 공식 배포 메타데이터](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json):
  SDK10.0.403 ARM64를 worktree 로컬에 설치하며 root global.json은 유지한다.

SDK 첫 실행에는 `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`도 설정한다. 이번 준비 중
첫 실행이 사용자 login keychain에 localhost 개발 인증서를 자동 생성한 부작용이
있었다(2026-10-30 14:32 KST). 신뢰 등록·삭제·변경은 하지 않았으며 후속 실행은
자동 생성을 끈다. 로컬 HTTP 시제품에 HTTPS 개발 인증서는 필요하지 않다.

공유 parser golden의 기준은 `e281a4fa:data/meta/progression.json`이다. 다음 정규화만
적용한 JSON을 `core/tests/SowSiege.Tests/Fixtures/meta-catalog-e281a4fa.json`에 고정했다:
materials/chapters/manorBuildings/vassals/challenges 각 record에서 kind, tags,
designStatus, concept, effect를 제거하고 manorBuildings의 manorEffect를 effect로
이름 변경한다. `git show e281a4fa:data/meta/progression.json`을 Node JSON.parse로
읽어 위 필드만 처리한 뒤 JSON.stringify(2칸)로 저장했다. C# parser나 wrapper의
실행 결과로 기대값을 만들지 않았다. 이 golden은 정본 catalog 변환의 독립 비교다.
설정 wrapper끼리의 비교는 경로 보존 확인에 불과하며 독립 회귀 증거가 아니다.
역사 r3~r7 설정 고정 해시와 저장 검사는 기존 AccountEstateR8MapTests 관문을 사용한다.
