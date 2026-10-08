# ADR0025 — 수동 조작도 Core 명령과 이식 가능한 재생으로 기록

상태: 채택 — 구현·회귀 관문은 연결 PR에서 검사
날짜: 2026-10-08
연결: [#86](https://github.com/hyunlord/bs-mobile/issues/86), [#83](https://github.com/hyunlord/bs-mobile/issues/83)

## 경계

역사 Simulation/봇 실행과 기존 JSON 상태 해시는 유지한다. 새 InteractiveSession이 양자화된 이동, 카드 선택·리롤·금지·고정, 조준 방식과 개발 명령을 받아 Core에서 적용한다. 명령에는 적용 전 tick과 연속 sequence가 있다. 값·순서·현재 상태를 먼저 검증하고 잘못된 명령은 상태를 바꾸지 않는다. 카드 대기 중 세계 tick은 진행하지 않는다.

이동 속도·정규화·지도 경계, 가까운 적 조준과 동률의 ID 순서, 무적·출현량·레벨 부여는 Core가 소유한다. 배속은 Unity의 tick 스케줄링이며 피해나 성장 계산을 바꾸지 않는다. 최초 조준 기본값은 이동 방향으로 유지하고 [#85](https://github.com/hyunlord/bs-mobile/issues/85)의 사용자 플레이 결정을 기다린다.

View에는 내부 Simulation이나 mutable catalog 대신 복사된 읽기 전용 값과 Core 사건을 준다. 공격이 빗나간 경우도 사건을 남긴다. 투사체와 경험치 표시 효과는 이미 계산된 타격/경험치를 보여 주며 Unity에 추가 충돌·피해·수집 판정을 만들지 않는다. 관찰이 RNG를 소비하거나 사건을 지우지 않는다.

## 난수와 상태

수동 경로의 RNG는 .NET8 seeded Random과 호환되는 명시적 상태 구현이다. 새 난수열로 교체하는 대신 .NET의 기존 seeded 호환 알고리즘을 고정한다. 배열·인덱스·draw count를 상태 해시에 포함하며, 역사 경로의 System.Random은 바꾸지 않는다. 0/1 범위도 draw를 소비하는 규칙과 경계 seed를 비교한다.

출처는 MIT인 dotnet/runtime 커밋 `5535e31a712343a63f5d7d796cd874e563e5ac14`의 [Net5CompatPrng](https://github.com/dotnet/runtime/blob/5535e31a712343a63f5d7d796cd874e563e5ac14/src/libraries/System.Private.CoreLib/src/System/Random.Net5CompatImpl.cs#L241-L355)다. [seeded 생성자 분기](https://github.com/dotnet/runtime/blob/5535e31a712343a63f5d7d796cd874e563e5ac14/src/libraries/System.Private.CoreLib/src/System/Random.cs#L25-L41)와 [라이선스](https://github.com/dotnet/runtime/blob/5535e31a712343a63f5d7d796cd874e563e5ac14/LICENSE.TXT)를 함께 고정하고 구현에 출처/라이선스를 유지한다.

새 상태 codec은 버전을 붙인 명시적 BCL 바이트 기록이다. seed·정본 데이터·옵션·명령 순서뿐 아니라 미래에 영향을 주는 엔티티/장비/카드/런타임/유해/실험/무기 상태와 RNG를 포함한다. 리스트 순서는 보존하고 사전 값은 ordinal/숫자 키 순서로 기록한다. 콘텐츠 사전에는 열거 순서도 별도로 기록한다. 생성 당시 catalog 식별자와 현재 catalog를 모두 묶어, 외부에서 레코드를 교체해도 초기 캐시의 차이가 해시에서 사라지지 않게 한다. 런타임 reflection, 프로세스별 해시, 시스템 시각을 쓰지 않는다. 역사 해시와 새 수동 해시를 같은 계약이라고 주장하지 않는다.

## 재생과 관문

한 기록 파일에 버전·seed·설정·데이터 식별자·입력 명령·checkpoint·종료 tick/이유/해시가 들어간다. 잘못된 버전·순서·데이터·잘림·뒤에 붙은 바이트는 거부한다. 정상 종료와 명시적 중도 종료를 구분한다. 파일 입출력은 호스트/앱이 맡고 Core는 바이트와 명령을 처리한다.

5개 correctness seed(30000~30004)의 한 해 기록을 같은 바이트로 .NET/Unity Mono/Android IL2CPP에서 비교한다. 이 기록은 밸런스 리그가 아니다. 실기에서 꺼낸 실제 입력 파일의 CLI 재생도 별도로 검증한다. 알고리즘 출처 확인과 두 .NET 타깃 일치만으로 실제 Mono/IL2CPP 관문을 통과했다고 말하지 않는다.
