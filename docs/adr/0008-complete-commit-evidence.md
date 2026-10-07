# ADR 0008: 커밋별 검사와 주석 규칙의 빈틈 닫기

상태: 채택. 날짜: 2026-10-08. 연결 이슈: #27.

## 맥락

같은 PR에 연속 push하면 기존 concurrency 키가 앞 커밋 CI를 취소했다. 또한 TODO 검사가 주석으로 시작하는 줄만 검사하여 코드 뒤 인라인 주석을 놓쳤다. 둘 다 요청된 장기 이력·협업 규칙에 빈틈이다.

## 결정

CI concurrency 키에 SHA를 포함하고 실행 중 취소를 끈다. 각 커밋의 실행 결과와 지표를 보존하며, 동일 ref/SHA 중복 이벤트만 같은 그룹을 사용한다. 주석 시작 기호를 문자열 밖에서 찾아 인라인·블록·셸 TODO에도 이슈 번호를 요구한다.

## 결과와 비용

모든 push를 끝까지 검사하므로 Actions 사용량은 증가한다. 실패 커밋은 실패 증거를 유지하고 성공한 측정만 지표 release에 보관한다. 주석 검사는 일반 문법의 문자열/주석을 구분하는 경량 검사이며 언어 전체 parser는 아니다.

## 거절한 대안

- 최신 PR 실행만 남기기: 중간 커밋별 측정 요구를 충족하지 못한다.
- 모든 TODO 문자열 금지: 검사 테스트 문자열과 사용자 데이터까지 오탐하므로 거절.
- 규칙을 문서로만 두기: 인라인 위반이 실제 통과하므로 거절.

## 검증

실제 임시 Git 저장소의 인라인 TODO를 검사기에 넣었을 때 이전 구현은 통과했다(회귀 테스트 1개 실패). 수정 후 standalone/inline/block/shell 위반과 번호 있는 정상 주석 6개가 모두 기대한 종료 코드로 동작했다. 최종 clean-clone/CI에서 전체 검사와 함께 재실행한다.

## 지표 보관 큐

GitHub의 기본 concurrency는 실행 중 취소를 꺼도 대기 항목을 교체한다. 지표 보관 그룹에는 `queue: max`를 사용해 최대 100개 대기 실행을 FIFO로 보존한다. 2026-10-08 확인한 [공식 문서](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency), [2026-05-07 변경 기록](https://github.blog/changelog/2026-05-07-github-actions-concurrency-groups-now-allow-larger-queues/)에 근거한다. 100개 초과 폭주나 실패 시 원본 CI run을 재실행하여 보관을 재시도하며 로그 없이 성공으로 처리하지 않는다. 동시 release index 덮어쓰기를 허용하는 대안은 이력 누락을 숨길 수 있어 거절했다.

릴리스 생성은 성공하고 최초 JSON 업로드가 실패한 중간 상태도 복구해야 한다. 자산 목록을 조회해 JSON이 0개일 때만 다운로드를 생략하며 네트워크·권한 실패를 무시하지 않는다.

원본 로그와 BenchmarkDotNet 보고에는 도구가 출력한 후행 공백이 있다. 증거를 변형하지 않기 위해 `.gitattributes`는 `docs/evidence/**`만 text 변환·공백 진단에서 제외한다. 제품 코드·스크립트의 형식 검사는 유지한다. 제공받은 design 문서도 바이트 보존을 위해 text 변환하지 않는다.
