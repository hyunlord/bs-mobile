# #426 승인 영지 그림 적용

버전: v0.1 · 2026-10-27 · 상태: 구현 브랜치, Unity/사용자 화면 검증 대기.

## 범위와 출처

공사 407 일반1·2/일반3r2/밭1–3, 소품4·일꾼2, 강둑416 밝음r1/어둠r2, 나무416 밝음r1/어둠r2, 운반423 SE/SW16칸. [정확한 출처/앵커](../licenses/m3-estate-art.md), `tools/estate-art.json`에 파일별 지정. 새 의존성·Core·프로필·저장 변경 없음. 모든 수치 조정은 View 시제품이다.

- 공사: 진행률 33/66%에서만 정적 그림 계획을 새로 만든다. 완료 시 기존 완공 그림 사용. stage1/2/3의 바닥 기준점은 동일 (68,124) /136×160. 4종 소품 중 ID로 고른 두 조각을 부지 바깥 앞 가장자리에 놓는다.
- 운반: 완성된 곡창과 공사 부지의 기존 길 그래프를 따라 왕복 한 명. 둘이 같거나 경로가 없으면 정지 운반 자세. 별도 컴포넌트가 캐시한 32스프라이트를 50ms마다 바꾸고 위치만 보간한다. 전체 계획/페이지를 프레임마다 재생성하지 않는다. 발 깊이 순서가 바뀔 때만 hierarchy를 조정한다.
- 강둑: 기존 둥근 돌이 `riverbank-patch`/`river-bend-patch`에 구워져 있었으므로 View shader의 물색 선택으로 물만 남기고 승인 강둑 조각을 얹는다. 원판 PNG는 변경하지 않는다. 밝음/어둠은 열린 상태로 선택, 어둠 r2는 별도 위층에 원본 밝기로 그려 중복 어둠 색조를 피한다.
- 나무: 복원 땅의 군집 후보와 닫힌 숲 후보를 승인된 침엽/활엽/덤불 2변형씩으로 바꾼다. 결정적 배치 seed/일반 소품 규칙은 유지한다.

## 정적 검증

| 항목 | 결과 |
|---|---|
| Node source/anchor/revision 계약 | 2/2 통과 |
| atlas 정합 검사 | 152 sprites / 30 building stages 통과 |
| Unity 경계 검사 | assemblies=9 통과 |
| git diff --check | 통과 |
| 기존 건물 body/shadow·terrain PNG | SHA-256 동일 |
| decor atlas | 동일 1장, 2048×2328 (기존 사용높이738 → 2328), importer 최대4096 이내 |
| EditMode 신규 | 진행률 경계7사례, 셀/앵커, 공사/경로/소품, 왕복, 캐시 단계 전환, 물/강둑 계약 작성. 실행 대기 |
| PlayMode | 기존 공사 화면 기대 그림을 승인 field-stage1로 갱신. 실행 대기 |

`node --test tools/test-estate-approved-art.mjs`, `node tools/estate-art.mjs --check`, `node tools/verify-unity-boundaries.mjs`, `git diff --check`.

## 실제 확인이 필요한 것

- 두 프로필 EditMode/PlayMode와 리플레이 해시. 정적 변경 경계는 실제 해시 결과를 대신하지 않는다.
- 첫 접속/공사1·2·3/완공 vs 키아트 C, 3배 확대. field/일반을 각각 확인.
- 강둑의 물색 마스크가 희미한 물결을 지우거나 구운 돌 잔재를 남기는지, 독립 강둑이 물과 접하는지. 아직 화면 통과 아님.
- 어둠 강둑/땅 RGB·Y′ ROI, 실제 묶음 전후. atlas 수 불변은 묶음 수 불변 증거가 아니다.
- 운반 주민의 길 접점/건물 가림/애니메이션. 승인된 SE/SW만 있어 위로 걷는 구간에서도 아래쪽 자세가 남는다. 새 방향을 생성하거나 기존 시트를 변형하지 않는다.

공유 Mac의 Unity 실행·잠금은 부모 세션에서 직렬로 수행한다. 이 구현 작업은 Unity/Standalone/빌드/야간 측정을 실행하지 않았다. PR은 초안, 사용자 승인 전 병합 없음.

## 실행 진입점 (부모 세션 전달)

- PlayMode `EstateMapFlowTests.FreshR7EstateBuildsItsGranaryBeforeTheFieldAndCompletesWithoutAccountWrites`: 첫 진입, 전체, 선행조건, 공사1/완공. 테스트 안의 기존 `Skip60`는 Editor 개발 시계 증거이며 native 정상 시간 증거가 아니다.
- 개발 macOS `--estate-entry-probe --estate-entry-output <비어 있는 자기 소유 절대 경로>`: 첫 진입0/1/30, 경작지 거부 사유, 정상 곡창60초→경작지30초. 이번 변경은 진행률을 관측만 하여 stage2/3에서 추가 캡처한다. 곡창은 시작 약20/40초, 경작지는 약10/20초에 도달한다. 실제 시점은 `construction.csv` elapsedSeconds에 남긴다. 기존180초 deadline 유지.
- stage 관측은 게임 규칙이나 저장을 바꾸지 않는다. 새로운 자원 지급·공사 시간 수정·native 가속 없음.
