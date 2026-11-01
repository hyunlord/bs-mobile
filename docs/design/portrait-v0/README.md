# Sow & Siege 화면 설계 v0 — 구현 기준 패키지

> 2026-10-31: `boards/`와 `source/a/`의 이미지 67장은 저장소 LFS 대역폭 절감을 위해 [Release portrait-v0-design-20261031](https://github.com/hyunlord/bs-mobile/releases/tag/portrait-v0-design-20261031)로 옮겼다(#601 후속). 같은 파일 이름으로 받으며, 원래 경로와 sha256은 Release 설명 표에 있다. git 역사에는 그대로 남아 있다. HTML 원본을 그림과 함께 보려면 Release 파일을 `boards/`·`source/a/`에 내려받는다:
> `gh release download portrait-v0-design-20261031 -D <폴더>`

이 패키지가 화면 구현의 기준이다. 서면 규격은 이 그림을 설명하는 글일 뿐이고, 둘이 다르면 이 그림을 따른다.

- boards/ : 각 화면 설계 그림. 390×844pt 기준, 2배 해상도(780×1688). 01-Main은 흐름 지도, 15-BM은 BM 규칙, 16-Logic은 행동 규칙이다.
- source/ : 같은 화면의 HTML 원본. 브라우저로 열면 그대로 보인다(폰트: Gowun Batang 제목, Noto Sans KR 본문).
  위치·크기·색·글자 크기는 모두 이 파일의 style 값(pt 단위)을 따른다.
- source/a/ : 화면에 쓴 이미지. 저장소 아트에서 가져온 것이다. 같은 스프라이트가 저장소 아틀라스에 있으면 그것을 쓰고, 없으면 이 파일을 가져와 쓴다.

## 9-slice 값 (CSS border-image → Unity Sprite Border)
| 이미지 | 자르기(px) | 그릴 때 테두리 두께(pt) | 쓰는 곳 |
|---|---|---|---|
| ui-panel.png | 사방 44 | 12–18 | 패널, 결산 표, 카드형 칸 |
| ui-btn-blue.png | 좌우 52 | 좌우 24–30 | 받기, 유료 줄 열기, 챕터 배너, 광고 일어나기 |
| ui-btn-red.png | 좌우 52 | 좌우 28–30 | 출정, 출정 시작, 영지로 (화면당 주 버튼 1개) |
| ui-btn-cream.png | 좌우 46 | 좌우 16–18 | 보조 버튼, 광고 2배, 다시 출정, 건물 이름표 |
| ui-slot.png | 사방 48 | 5–10 | 장비 칸, 패스 보상 칸 |
| ui-card-common/rare/epic.png | 사방 40 | 10–14 | 레벨업 카드(도구/무기/특허장), 상점 외형 |
| ui-xp-rail / ui-xp-fill / ui-hp-fill.png | 좌우 36 | 좌우 6–10 | 체력·경험치·패스 진행 바 |
| ui-bar-wood.png | 좌우 44 | 좌우 18 | 영지 자원 바 |
| ui-hint.png | 좌우 60 | 좌우 30 | 판 안 짧은 힌트 |
