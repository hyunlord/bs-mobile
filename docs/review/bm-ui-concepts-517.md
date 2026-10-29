# BM 그림 흐름 시안 r1

승인 영지 UI r3와 같은 양피지·어두운 바탕·파란 주버튼 및 광택 아이콘으로 만든 390×844 오프라인 화면 6개다. 사용자 결정을 위한 그림이며 게임 코드·데이터, 실제 판매 정책, 광고·결제 SDK는 변경하지 않았다.

- [6화면 흐름 비교판](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-six-screen-flow-r1-1170x1880.png)
- [가속 확인](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-accelerate-r1-390x844.png) / [보석 부족](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-insufficient-r1-390x844.png)
- [상점 첫 화면](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-shop-r1-390x844.png) / [무료 구성 확인](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-free-confirm-r1-390x844.png)
- [구매 확인](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-purchase-confirm-r1-390x844.png) / [무료 수령 후](https://github.com/hyunlord/bs-mobile/releases/download/bm-ui-517-20261029/bm-claimed-r1-390x844.png)
- [Release](https://github.com/hyunlord/bs-mobile/releases/tag/bm-ui-517-20261029)

상점은 보석3·자원3·소모품2 고정 상품과 일일 무료 보상을 한 화면에서 비교한다. 보석12/보유30·8, 상품 수량, 무료 곡물20·목재10·철재5는 시안 표본이며 확정 경제 규칙이 아니다. 현금 가격은 모두 `₩—`로 미정이고 확률형 상품·광고 요구는 없다. 가속 공식과 소모품의 실제 효과는 설계하지 않았다.

보석8 부족 화면과 보석30 상점은 독립 검토 표본이다. 실제 연결 시8을 유지해야 하며 상점 이동만으로30으로 바뀌지 않는다. 구매 확인의130은 성공 시 예상 예시이며 지급 완료 증거가 아니다. 무료 수령 후 자원은120/180/60→140/190/65, 보석30은 불변이며 수령 버튼은 비활성이다.

현행 자재·판 능력 판매 금지 정책과 이번 상품 그림 제안은 다르다. 사용자의 이번 요청은 그림 검토에 한정하며 정책·실제 판매 승인으로 확대하지 않는다. [#122](https://github.com/hyunlord/bs-mobile/issues/122) 보류를 유지한다.

기존 곡물·목재 PNG와 승인 UI bitmap은 보존했다. 신규 보석·철재·회복·보호4그림은 imagegen 시안 원판을 기계적으로 분리한 것이며 현행 판매 역할이라고 주장하지 않는다. 상점/수령후의 편집 영역 밖 rawRGBA는 승인 r3와 같고, 모달은 전체 배경을 의도적으로 어둡게 합성했다. 비교판 각 패널은 축소하지 않은390×844다.

내부 시각 PASS92 및 독립 기술검사 PASS: 본문14px 이상, 입력명세48×48논리px 이상·활성 중첩0·측정 글자폭 넘침0, 계산·출처 SHA 확인. 실기 dp/pt·터치, 실제 결제·가속·지급·중복 수령·일일 갱신·Unity 실행은 NOT_RUN이다. 최종 그림 승인은 사용자 담당이다. 로컬 검사·원판은 `output/bm-ui-concepts-20261029/`에 보존하며 Release에는 선택한 화면과 작은 명세·감사·QA 문서만 담는다.
