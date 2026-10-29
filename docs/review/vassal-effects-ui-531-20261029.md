# 가신 효과 UI 구현 후 시안 r1 / HUD r2

관문: 부분 — 오프라인 기술 PASS·시각 PASS94. r1 가신탭·출정확인은 사용자 승인, HUD r2는 사용자 검토 대기이며 게임 적용·Unity·실기·성능은 NOT_RUN이다.

이슈: https://github.com/hyunlord/bs-mobile/issues/531

Release: https://github.com/hyunlord/bs-mobile/releases/tag/vassal-effects-ui-531-20261029

승인 GDD13의 가신 효과를 가신탭·출정확인·전투HUD 세 화면으로 표현한다. 승인된 새싹 초상6인과 양피지·파란 버튼·전투HUD r2 컴포넌트를 Swift/AppKit/CoreGraphics로 재사용했다. 기존 UI PNG의 글자를 덧칠해 교체하지 않고 텍스트·반복행·9slice·초상을 source-native 렌더로 구성한다. 게임 코드와 데이터는 변경하지 않는다.

테스트계정은 Lv20/승급5·병영 효과8·대장간 효과8이다. 고랑 방패수 공격9.7%, 황금밭 호위 최대체력15%, 수확 서기 판 안 경험치6.3%만 편성했다. 미편성3인의 보유 능력과 이번 적용0%를 구분한다. 수치는 가설/예시이며 실제 현재 제품의 효과라고 주장하지 않는다. 황금밭의 원계산16.5%는 상한15%로 제한되며 해당 능력에만 '상한 도달 — 더 강화해도 전투 효과 없음'을 표시한다.

출정확인은 snapshot 예정 미리보기다. 출정 시 확정하고 진행 중 강화·편성 변경은 다음 출정부터 적용한다. absent/꺼짐과 enabled/빈편성0효과를 서로 다른 상태로 명세하며 현재 예시는 켜짐3인이다. 현재 게임의 가신 미적용 안내는 수정하지 않았다.

검증 결과: 3PNG390×844, 글자 누락·폭/높이 넘침0, 새 본문14px 이상. 활성 hit11/2/1 모두48×48 이상이며 중첩0이다. HUD y0..<84 예약영역 안에 상태를 표시하고 기존 pause48·13칸을 보존한다. 새 터치영역·추가 월드 가림0, y84 이후 원본과 rawRGBA차이0이다. HP2070은 표본 기본1800×1.15의 산술 예시다. HUD13칸은 승인2아이콘 반복 용량표본이며 유효한 게임 장비 편성은 아니다.

원본6초상 SHA와 기존 그림을 보존했다. 전달은 native3+설명포함3장보드 PNG4, 계약JSON1, README/QA/DESIGN3으로 정확8파일이다. 후보와 소스/검증은 보존하며 신규 ZIP을 만들지 않는다. 버튼 크기는 오프라인 명세이며 실제 기기 터치 검증을 대신하지 않는다.

이 문서PR은 시안 검토를 위해 열어 두며 임의 병합하지 않는다.

## HUD r2 후속

사용자는 r1 가신탭과 출정확인을 승인했다. 기존 r1 파일8개는 보존하고 HUD r2만 추가한다. 후반 UI 부하 표본은 현행 카탈로그의 서로 다른 무기7·도구7이다. 실제 장비 상한 변경을 뜻하지 않는다. 초반은 각2개, 후반은 각2개와 비터치 +5 표시로 접고 전체 내역은 기존 일시정지에서 확인하는 설계다.

원본 그림 alpha≥32 가시 경계를 기준으로 비율을 유지해 긴 변28px로 표시한다. alpha≥128 경계의 긴 변은26~28px이며 불투명 픽셀28px라는 주장은 아니다. 레벨1/12는 그림 옆에 유지한다. 예약84px/844=9.9526%, pause48×48 및 HP/XP·가신 상태를 보존한다. 새 터치는 없으며 후반 우측 여백16px다.

Root 실제크기 시각검토 c3 PASS94. c1 레벨 누락 REVISE86과 c2 밀집 REVISE88은 보존했다. native2장390×844의 y84 이후 월드는 r1 대비 rawRGBA 차이0, 글리프 누락·텍스트 넘침0이다. 전달은 native2·나란히판·28px 접사4PNG와 계약JSON·README·QA로 새7파일이다. 게임 코드·데이터 변경 없음, Unity·실기·성능 NOT_RUN. HUD r2 사용자 승인 전 PR은 미병합으로 유지한다.

- [초반4 HUD](https://github.com/hyunlord/bs-mobile/releases/download/vassal-effects-ui-531-20261029/vassal-hud-early4-r2-390x844.png)
- [후반14 HUD](https://github.com/hyunlord/bs-mobile/releases/download/vassal-effects-ui-531-20261029/vassal-hud-late14-r2-390x844.png)
- [나란히 비교](https://github.com/hyunlord/bs-mobile/releases/download/vassal-effects-ui-531-20261029/vassal-hud-early-late-r2-780x900.png)
