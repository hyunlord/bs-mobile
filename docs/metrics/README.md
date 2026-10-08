# 측정 이력

`node tools/metrics.mjs artifacts/metrics.json docs/metrics`는 검증한 원본과 정규화한 레코드를 `history/<commit>-<content-digest>.json`에 보관하고 전체 이력을 `index.html`로 렌더링한다. `METRICS_SHA`로 CI가 검증한 **40자리 commit SHA**를 지정할 수 있다. 입력 원본은 레코드의 `source`에 남는다. 같은 입력은 중복 추가하지 않으며 같은 커밋의 다른 실행은 각각 보존한다.

필수 필드: `schemaVersion:1`, ISO `timestamp`, 전체 `commit`, 문자열 `stage`/`model`/`scope`(또는 `measurementScope`), 객체 `runtime`/`config`, 음수가 아닌 정수 `seed`, 유한한 음이 아닌 `tickP95Ms`, `balanceDispersion`(유한한 음이 아닌 수 또는 `null`). S0의 balance 값은 반드시 `null`이다. 런타임·모델·단계·설정·측정 범위가 다르면 별도 시계열을 그린다. 입력 오류나 손상된 기존 기록은 exit 1로 실패시킨다.

HTML은 외부 자산과 자바스크립트 없이 열린다. 원본 링크까지 보려면 `history/` 폴더를 HTML과 함께 보관한다. 표가 모든 정확한 값을 제공하며 차트는 순서와 범위를 요약한다. S0 타이밍은 합성 scaffold 측정이며 게임 밸런스나 모바일 실기기 성능의 증거가 아니다. 밸런스 결과 분산은 게임 모델이 구현된 뒤 실제 seed league 출력에서 추가한다.

`node tools/metrics.mjs --selftest`는 유효성 검사, HTML escaping, 빈 이력 표현을 점검한다. CI의 장기 저장 위치 및 보존 정책은 운영 런북을 따른다. 이 디렉터리의 스냅샷은 단계별 PR로 갱신하며 CI artifact 만료를 영구 보존으로 취급하지 않는다.

## 1단계 A 기기 축

`schemaVersion:2`는 Unity 프레임 간격 전용이다. `tickP95Ms`와 `balanceDispersion`는 `null`이며, `frameP95Ms`·`frameMaxMs`, `sampleCount`, `lateComplete`, `device`의 `model`·`os`·`screen`·`posture`를 기록한다. 기존 공통 commit/seed/runtime/config/measurementScope도 필수다. 기기·화면·접힘 상태·측정 범위가 다르면 별도 시계열이고 Core 틱과 같은 그래프에 연결하지 않는다.

표본이 없으면 두 프레임 값은 `null`, 완전한 마지막 계절을 관측하지 못했으면 `lateComplete:false`다. 불완전한 표본의 p95가 낮아도 전체 관문 통과를 뜻하지 않는다. 개발 배속·정지·출현량·무적 설정과 실제 마지막 계절 범위를 측정 출처에 유지한다.

기기 CSV의 집계 입력은 [기기 실행 런북](../runbooks/device-play.md)의 명령으로 생성한다. 같은 도구에 전달하되 신규 기기 보고는 로컬 출력 폴더를 사용한다. CSV에서 재생성 가능한 HTML·집계 JSON을 Release에 다시 올리지 않는다. 필요한 작은 정본 측정 CSV와 실패/선정 표본만 보관한다. 역사 schema1 기록은 그대로 읽으며 과거 원자료를 새 schema로 다시 쓰지 않는다.
