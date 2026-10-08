# 0030 — 역할 기반 원본 아트 검증과 CI의 LFS 체크아웃

- 상태: 승인 — 1단계 B 의뢰서의 M2 구현 범위
- 날짜: 2026-10-09
- 연결: [#96](https://github.com/hyunlord/bs-mobile/issues/96), [PR #106](https://github.com/hyunlord/bs-mobile/pull/106)
- 관련: ADR0024 정본 Unity 연결, ADR0028 URP2D 인스턴싱, ADR0029 첫 플레이 프로필

## 맥락

첫 플레이 프로필은 도형 월드를 원본 스프라이트로 대체한다. 규칙·수치는 Core와 정본 데이터에 남기고 그림을 교체할 수 있어야 한다. PNG는 Git LFS에 보관한다. 최초 원격 M2 검사에서 실제 PNG 대신 LFS 포인터를 읽어 아트 계약 검사가 실패했다.

## 결정

1. 표현 전용 manifest의 역할 ID와 콘텐츠·상태 binding으로 atlas의 rect, pivot, 크기와 트윈을 연결한다. 게임 수치는 포함하지 않는다. 누락·중복·범위 초과·원본 alpha 여백·출처 누락은 실패하며 도형 대체물을 쓰지 않는다.
2. 준비 단계에서 manifest를 검증하고 Unity registry/import 설정을 만든다. 런타임은 원본 PNG를 수정하지 않고 atlas UV와 인스턴싱으로 표현한다. ADR0028의 URP2D 카메라 호환 경로와 511개 묶음은 유지한다.
3. Unity 생성 연결은 명시적인 `first-playable` 프로필을 사용한다. 역사 프로필·Core 결정론 계약은 보존한다.
4. 전체 검사를 실행하는 PR 및 야간 workflow의 checkout에 `lfs: true`를 설정한다. 실제 PNG 바이트를 검사하며 포인터를 성공으로 인정하거나 CI에서 아트 검사를 생략하지 않는다. 기존 읽기 전용 권한과 `persist-credentials: false`를 유지한다.

## 결과·비용

CI는 원본 이미지 다운로드 비용을 추가로 부담한다. 그림의 선언적 계약은 자동 검사하지만 시각 품질은 실제 GPU 캡처로 별도 판정한다. Editor 월드 검증은 UI·정상 기기 플레이·성능 관문을 대체하지 않는다.

## 거절한 대안

- CI에서 PNG 검사를 건너뛰기: 실제 손상·누락을 놓친다.
- LFS 포인터의 크기·해시만 확인하기: atlas rect와 원본 alpha 조건을 증명하지 못한다.
- Core에 그림 경로나 영웅·영지 ID를 고정하기: 규칙과 표현의 교체 경계를 훼손한다.

## 검증

포인터만 체크아웃한 격리 worktree에서 `atlas actors: invalid PNG`를 재현했다. 같은 worktree에 `git lfs pull`을 실행한 뒤 11 atlas·345 역할·329/329 binding 검사가 통과했다. 저장소 정책 테스트155개, 전체 로컬 검사, Unity EditMode35개·PlayMode12개가 통과했다. 원격 CI 통과 여부는 PR의 실제 결과로 확인한다.
