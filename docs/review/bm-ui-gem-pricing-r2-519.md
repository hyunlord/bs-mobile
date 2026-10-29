# BM 재화·상품 시안 r2

r1을 보존하고 상단 보석 잔액과 상품 가격 체계를 구분한 390×844 오프라인 그림6장이다. 게임 코드·데이터·광고/결제 SDK는 변경하지 않았다.

- [6화면 흐름판](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-six-screen-flow-r2-1170x1880.png)
- [상점](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-shop-r2-390x844.png) / [가속](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-accelerate-r2-390x844.png) / [부족](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-insufficient-r2-390x844.png)
- [무료 확인](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-free-confirm-r2-390x844.png) / [구매 확인](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-purchase-confirm-r2-390x844.png) / [무료 수령 후](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-claimed-r2-390x844.png)
- [기존 r1과 같은 Release](https://github.com/hyunlord/bs-mobile/releases/tag/bm-ui-517-20261029)

모든 상단은 곡물·목재·철재·파란 보석이다. 가속30/부족8/그 밖30을 표시하고 공사 중 목재150을 유지한다. 현금 `₩—`는 보석3팩과 해당 구매 확인에만 남겼다. 곡물/목재/철재100개는 보석5/5/8, 회복·보호 상품 대신 성장가속권1개·10분 단축·보석5를 표시한다.

수량·가격·10분 효과는 화면 검토 표본이며 확정 경제 규칙이 아니다. 부족8과 상점30은 독립 표본이며 실제 연결 시8을 유지해야 한다. 구매후130은 성공 시 예상 표본이지 지급 완료가 아니다. 현행 판매 정책 및 [#122](https://github.com/hyunlord/bs-mobile/issues/122) 보류를 유지한다.

무료/구매 모달 `[20,236,350,360]`은 r1 rawRGBA와 차이0이다. 모달 밖은 저장된 r2 상점의 dim 배경과 차이0이다. 나머지4화면도 선언 수정영역 밖 r1 rawRGBA 변경0이다. 시각 검토에서 제기된 이전 배경 후보와 경계8pixel 차이 기록은 로컬에 보존하고 최종 조립본만 공개한다. 흐름판은 r1 외부 헤더를 그대로 두고 native6패널을 축소 없이 복사했다.

독립 시각/기술 검사에서 새본문14px 이상, hit48논리px 이상·활성중첩0·측정글자폭넘침0, 잔액 및 가격표본 일치를 확인했다. 공개 r2는7PNG·JSON1·MD3으로 별도 이름을 사용하며 기존 r1 자산12개를 덮어쓰지 않는다. 신규 ticket 원판과 개별crop, 실패 후보 및 검사자료는 `output/bm-ui-r2-20261029/`에 보존한다. 실제 결제·가속·지급·실기 터치·게임 실행은 NOT_RUN이고 최종 그림 승인은 사용자 담당이다.
