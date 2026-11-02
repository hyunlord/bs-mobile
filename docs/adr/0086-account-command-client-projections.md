# ADR0086 · 계정 명령 클라이언트의 명시적 JSON 투영과 재시도 저널

- 상태: 제안 · 구현 후보, 런타임 검증 대기
- 날짜: 2026-11-02
- 연결: [#638](https://github.com/hyunlord/bs-mobile/issues/638)

## 맥락

서버 모집의 영웅 ID/조각, 연구의 자원별 비용은 동적 JSON 객체다. 기존 JsonUtility 필드 DTO로 읽으면 동적 키를 잃는다. Unity 경계 검사는 host/reflection serializer를 금지하고 새 패키지 추가는 승인 범위 밖이다. 앱 종료 뒤에도 미확정 소비 요청의 같은 commandId와 본문을 재사용해야 한다.

## 결정

BCL JSON reader의 제한된 길이·깊이·문자 quota를 적용한 뒤 명시적 필드 매핑으로 투영한다. 반사 기반 DTO 활성화와 Core/Sim 참조를 추가하지 않는다. 필수 값 누락·중복 키·잘못된 타입·정수 범위 초과는 거절한다. 불명확한 값은 0으로 그리지 않는다. 기존 training JsonUtility 경로는 보존한다.

전송 전에 endpoint와 안정된 accountId에 묶인 단방향 digest 파일명으로 명령을 저장한다. 임시 파일 flush 후 원자 교체하고 토큰은 쓰지 않는다. 확정 응답/명시적 거절만 저널을 빈 기록으로 바꾼다. 실패/만료에는 원본 명령을 보존한다. 공통 pending은 모집·연구·훈련·연맹 사이의 새 명령 생성을 막는다. 연맹의 별도 응답 reader가 아직 등록되지 않았으면 재전송을 막고 기록을 보존한다.

## 결과·비용과 거절한 대안

명시 매핑은 서버 필드 추가에 맞춘 유지보수가 필요하다. `JsonUtility`만 사용하는 대안은 동적 키 유실 때문에, 반사 serializer는 기존 경계와 stripping 검증 부담 때문에, 패키지 추가는 승인 범위 때문에 채택하지 않는다. 검사를 수정하거나 문자열만 우회하지 않는다. JSON reader 자체의 Mono/IL2CPP 지원은 실제 대상 실행으로 따로 검증한다.

저널 tombstone은 런타임 파일 삭제를 피하며 계정당 작은 파일이 남는다. 개발용 prototype 연결은 원래 accountId를 반환하지 않아 token digest로 구분한다. 토큰이 교체되는 실제 인증 연결은 안정된 accountId 진입점을 써야 한다. 후속 통합에서 로그인 controller의 read-only 계정/토큰을 연결하며 비밀 저장 구현은 별도 인증 담당이다.

## 검증

정적 Unity 경계 검사 PASS. 동적 키·0조각 소유·누락·중복·타입·overflow·재시작 동일 본문·계정 분리·tombstone 테스트를 추가했다. Unity 실행, 실제 서버 화면, 전체 프로필 검사, C05는 아직 NOT_RUN이며 문서나 정적 검사를 실행 증거로 대체하지 않는다.
