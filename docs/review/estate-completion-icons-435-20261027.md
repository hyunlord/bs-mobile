# 영지 완성 건물·아이콘 화풍 검토 #435

관문: 부분 — 아트 제작·내부 검사 완료, 사용자 그림 승인과 아이콘 화풍 통일 결정 대기. 게임 코드·데이터·원본 atlas는 변경하지 않았다. 저장소 전체 로컬 검사는 아래 기존 Core 테스트에서 실패했으며 CI는 대기 중이다.

[검토 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-completion-icons-review-435-20261027) · [추적 #435](https://github.com/hyunlord/bs-mobile/issues/435)

## 먼저 볼 비교판

- [건물 밝음/어둠 10종](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/buildings-lit-dark-r1.png)
- [r7 원판과 완성 후보](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/buildings-before-after-r1.png)
- [제철소 공사3r2→완성 겹침](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/ironworks-construction-overlay-r1.png) · [보존한 집의 높이 차이](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/house-construction-overlay-r1.png)
- [아이콘 실제 UI 크기 비교](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/icon-ui-size-comparison-r1.png)
- [깃발](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/icon-rally-boundary-style-comparison-r1.png) · [검](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/icon-heirloom-service-style-comparison-r1.png) · [씨앗 왕관](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/icon-seed-crown-style-comparison-r1.png)

## 완성 그림 감사와 납품

[감사표](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/AUDIT-435.md)는 실제 r7 Native 캡처 소스 `8fcbcd7`을 기준으로 한다. 현재 main `7e5ca6d0ca2010389ac45bea012e529907f3fd03`의 manifest·매핑·r7 설정·planner 4파일과 바이트가 동일함을 확인했다. Release 태그 target과 실제 캡처 소스를 구별했다.

제철소는 대장간 Lv1을 재사용했고, 완성 건물 전10종의 독립 어둠 그림이 없었다. 기존 atlas 하단 앵커도 새 공사 그림의 바닥 중심과 같은 의미가 아니었다. 이에 집·곡창·대장간·기록소·병영·망루·우물사당·경작지·벌목장·제철소 **10종×밝음/어둠×1·2배=40 PNG**와 건물별 겹침 비교10장을 제공한다.

- 곡창·대장간·기록소·병영·망루·우물사당·벌목장·제철소 **8종을 공사3r2에서 이어지는 완성 후보로 제작**했다. 기록소·병영·우물사당의 문장은 씨앗1개·휜 줄기·높이가 다른 잎2장이다.
- 승인된 붉은 기와 집 `manor-s2`와 성숙한 밭 `field-s3` **2종 원색은 보존**하고 균일 축척·바닥 중심 정렬로 내보냈다. 원본 파일은 덮어쓰지 않았다.
- 1배 canvas136×160, 논리2×2 바닥128×64, 바닥 중심(68,124). 2배는 모두 정확히 두 배다. 새8종은 승인 공사3r2와 같은 source-floor 변환을 사용했다.
- 집은 보존 예외다. 2배 기준 상단이 공사3r2보다 **58px 낮다**. 같은 골조의 완성이나 구조 일치로 표현하지 않는다. 밭은 밭 전용 공사3과 비교한다.

이번 신규 후보는 **첫 완공/Lv1 범위**다. 기존에 있는 상위 레벨 그림을 전부 새로 만들지 않았다. 집·밭·벌목장·제철소는 r7 설정상 Lv1만 지원하므로 Lv2/3를 임의로 추가하지 않는다. 기존 기록소·병영의 상위 레벨 옛 문장은 감사에 남긴 후속 항목이며 이번 Lv1 후보로 모두 해결되었다고 주장하지 않는다.

## 아이콘 결정용 시안

승인된 `rally_boundary`, `heirloom_service`, `seed_crown`을 잉크·과슈로 바꾼 **후보 PNG3장과 비교판4장**이다. 기존 아이콘이나 게임 연결은 교체하지 않았다. [아이콘 검토 상세](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-completion-icons-review-435-20261027/ICON-STUDIES-435.md)에 원판 크기와 배율 정의를 기록했다.

각 아이콘 비교판의1배/3배는 원본 crop 픽셀 기준이다. 별도 UI 비교판은390×844 화면의 실제 카드46×46px·목록32×32px 박스에 맞춘 표시1배와 그3배 확대다. 역사 Editor fixture를 참고로 사용한 오프라인 조립이며 신규 게임 실행 캡처가 아니다. 검 메달의 잎 개수는32px에서 명확히 판독되지 않는 한계를 남겼다. 통일 여부는 사용자가 결정한다.

## 실행한 검사와 한계

Release70개 자산의 원격 SHA-256과 로컬 파일을 대조해 불일치0개를 확인했다.

- 건물40PNG의 크기·투명도·경계 검사와 밝음/어둠20쌍 확인: alpha>8 캔버스 경계 접촉0. 2배 bbox 각 변 차이는 새8종≤1px, 보존 집·밭≤8px.
- 어둠10종의 평균 RGB 각 채널과 가중 밝기가 실제r7 어둠땅3개 ROI보다 낮다. 가중 sRGB code-value Y′는32.72–43.49, 비교 땅은66.28 이상이다. 물리 휘도나 사용자 시각 승인으로 확대하지 않는다.
- 아이콘3종의 원판 crop·투명도·경계 검사 통과. bbox 각 변 차이 최대4px, 참조 중심 오차≤0.5px. Swift typecheck와 export/verify 검사를 실행했다.
- 건물 겹침8장·밝음/어둠10종 보드·아이콘 비교판4장을 직접 확인했다. 우물사당의 잎이3장인 첫 후보는 거절 기록으로 보존하고2장 수정본을 사용했다.
- 작업 전후 원본94파일 SHA-256 목록이 동일하다. 생성 색상 편집은 내부 붓질·알파가 미세하게 다르며 픽셀 동일한 색조 변환이 아니다.

`./tools/check.sh`는 변경하지 않은 Core의 `WaveEnemyTests.RangedApproachDoesNotAllocatePerEnemyAfterWarmup` (`WaveEnemyTests.cs:329`)에서 실패했다: expected800, actual8240. 이번 아트/문서 변경으로 테스트를 고치거나 기준을 완화하지 않았다. 따라서 아트 검사를 저장소 전체 관문 통과라고 표현하지 않으며 CI 결과는 별도 확인한다.

재현 스크립트·프롬프트·수동 바닥 중심·픽셀 검사는 작업 산출물과 Release 문서에 보존했다. 실제 게임 설치, 빛 전환, 게임 배율 시인성 및 실행 성능은 이번 아트 제작의 검증 범위가 아니다.
