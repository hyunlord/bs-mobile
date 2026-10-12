# ADR 0056: 구현 변경과 같은 PR에서 살아 있는 설계를 갱신한다

- 상태: 승인 — #236의 문서 운영 요청을 적용, 문서 내용의 미정 결정을 대신하지 않음
- 날짜: 2026-10-12
- 관련: [#236](https://github.com/hyunlord/bs-mobile/issues/236), [GDD 색인](../design/gdd/00-index.md)

## 맥락

화면·규칙 구현과 설계의 간격을 줄이되 원본 의뢰서와 역사 판정을 보존해야 한다. 설계 13개 문서만 담은 PR과 운영 계약·CI 변경 PR을 분리한다. 기존 docs-only 판정은 모든 경로가 `docs/`일 때만 성립하며 이 결정으로 느슨하게 하지 않는다.

## 결정

구현이 설계와 달라지면 같은 PR에서 해당 GDD의 버전·날짜·변경 요약·상태를 갱신한다. PR 양식에 `설계 문서 영향: 없음 / 있음(문서 이름)`을 기록한다. 없음에는 이유를 남기고, 원본은 역사 링크로 보존한다. AGENTS.md가 정본이고 CLAUDE.md는 해당 계약으로 안내한다.

`tools/design-impact.mjs`는 `tools/ci-scope.mjs`의 `changedPaths`를 그대로 사용한다. 다음 경로 중 하나라도 바뀌고 `docs/design/` 경로가 하나도 바뀌지 않았으면 workflow 경고를 **한 번만** 출력한다.

- `data/` 전체(스키마 포함)
- `core/src/SowSiege.Core/` 전체(규칙 경계의 보수적 대리 지표)
- `unity/Assets/Game/App/`, `View/`, `Input/` 전체(화면·흐름·입력 경계의 보수적 대리 지표)

이름 변경의 이전·이후 경로와 삭제 경로도 포함한다. 문서 삭제도 변경으로 센다. 따라서 알림이 없다는 사실은 설계 반영의 적정성을 보증하지 않으며 리뷰가 필요하다. 테스트·CLI·Editor/Debug 전용 파일은 이 화면 경계에 포함하지 않는다. 새 화면 경로를 도입하면 이 목록도 검토한다.

PR은 정확한 base/head SHA를 사용한다. 의존성 수동 dispatch는 기존 검증 단계가 만든 `DEPENDENCY_EVENT_PATH`의 PR 문맥을 우선한다. push는 before/after SHA로 같은 변경 경로 함수를 사용한다. 변경 범위를 결정할 수 없는 일반 dispatch·불완전 이벤트·없어진 Git 이력·초기 push는 ‘확인 불가, 수동 점검’ 경고 하나를 남긴다. 성공으로 추정하지 않는다.

알림은 비차단이며 기존 quality/secrets·PR 정책·full/docs 판정은 그대로다. 실제 정책이나 테스트 실패를 `continue-on-error`로 숨기지 않는다. 새 helper의 집중 테스트는 full gate 직전에 실행한다.

## 거절한 대안

- 모든 PR에 설계 변경 강제: 문서와 무관한 수정까지 의미 없는 편집을 유도한다.
- 경고로 CI 실패: 사용자가 요청한 비차단 경계를 넘는다.
- 계약 파일을 docs-only에 포함: 기존 CI 경로 정책을 약화한다.
- 구현 diff로 설계의 정확성 자동 판정: 경로 검사는 의미·재미·사용자 승인을 증명하지 못한다.

## 검증

`node --test tools/test-design-impact.mjs tools/test-ci-scope.mjs`: 9개 통과. 제품/무관/설계 동반 변경, rename·delete, PR·push·의존성 dispatch 문맥, 확인 불가 경고 한 번, strict docs-only와 실제 Git rename 검사를 포함한다. 게임·콘텐츠·의존성은 변경하지 않는다. 전체 검사는 이 운영 PR의 CI에서 실행하며 로컬에서 중복 실행하지 않는다.

번호는 2026-10-12 원격 main의 0055 다음 0056으로 확인했다. 병합 직전 원격 main에서 충돌 여부를 다시 확인한다. #126·#98·#224·1b와 #223의 미해결 성능 관문은 유지한다.
