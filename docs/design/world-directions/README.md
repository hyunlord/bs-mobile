# 보이는 품질: 세계와 목표 화면

**A 휴경의 왕국 선택 확정 · [#194](https://github.com/hyunlord/bs-mobile/issues/194), 구현 [#197](https://github.com/hyunlord/bs-mobile/issues/197).** B·C와 여섯 목표 그림은 선택 당시 제안으로 보존한다. 원본 콘텐츠 ID와 규칙은 유지한다. 각각의 전투·타이틀 이미지는 built-in imagegen으로 만든 목표 화면이며 실제 플레이 캡처가 아니다.

| 후보 | 세계 요약 1쪽 | 첫눈에 보여 줄 게임의 얼굴 |
|---|---|---|
| A 휴경의 왕국 | [세계·수호자·열 지명](A-fallow-kingdom.md) | 내가 키운 곳에만 색이 돌아오는 잿빛 왕국 |
| B 변경의 둔전 | [세계·강적·열 지명](B-frontier-farms.md) | 마수의 숲을 마주한 밭, 등불과 울타리로 읽히는 집 |
| C 수확제의 장원 | [세계·손님·열 지명](C-harvest-manor.md) | 밭과 건물이 자라며 완성되는 밝은 수확제 |

## 목표 화면 여섯 장

각 행은 왼쪽 전투, 오른쪽 타이틀이다. [원본 PNG 6장](https://github.com/hyunlord/bs-mobile/releases/tag/world-directions-20261010)은 선택용 Release에 보존한다. 이미지를 런타임에 설치하지 않는다.

| A · 전투 | A · 타이틀 |
|---|---|
| ![A 전투](https://github.com/hyunlord/bs-mobile/releases/download/world-directions-20261010/A-battle.png) | ![A 타이틀](https://github.com/hyunlord/bs-mobile/releases/download/world-directions-20261010/A-title.png) |

| B · 전투 | B · 타이틀 |
|---|---|
| ![B 전투](https://github.com/hyunlord/bs-mobile/releases/download/world-directions-20261010/B-battle.png) | ![B 타이틀](https://github.com/hyunlord/bs-mobile/releases/download/world-directions-20261010/B-title.png) |

| C · 전투 | C · 타이틀 |
|---|---|
| ![C 전투](https://github.com/hyunlord/bs-mobile/releases/download/world-directions-20261010/C-battle.png) | ![C 타이틀](https://github.com/hyunlord/bs-mobile/releases/download/world-directions-20261010/C-title.png) |

## 선택 후의 경계

A가 선택되어 첫 1분 구현이 승인되었다. 1b 콘텐츠 확장·별도 성능 측정·추가 검증 프로젝트는 시작하지 않는다. #126·#98은 보류한다. 선택 당시 후보 문서 병합과 이번 사용자 선택은 별도 기록이다.

선택 후에는 고른 화면을 기준으로 **기존 1a의 첫 1분**을 다시 만든다. 콘텐츠 수를 늘리지 않는다. 땅·길·물·숲 가장자리·소품·랜드마크, 씨앗→싹→익음과 건설·수확 보상, 캐릭터·적의 실루엣·애니메이션, 타격·처치·수확·레벨업·진화·보스 등장 효과와 소리, 카드·HUD·타이틀을 함께 맞춘다. 판정은 사용자가 영상·스크린샷을 보고 **“게임 같다”**고 판단하는 것 하나다. 결정론·해시·성능은 CI 안전망에만 두며 별도 측정·보고 작업은 하지 않는다. 실기 성능은 Fold7 #98에서 다룬다.

## 제작 기록

- 기준: [잉크·과슈 아트 바이블](../07_first-playable-art-bible.md), `data/system-design-v1.json`의 보스 5종과 챕터 10곳.
- 방법: built-in imagegen, 신규 6장과 A 전투의 기존 무기·도구 아이콘 교정 1회. [전체 프롬프트](image-prompts.md).
- 보존: 로컬 `artifacts/world-directions/{A,B,C}-{battle,title}.png`; Release에는 최종 6장만 올린다. 신규 증거 ZIP·CRC·전달 영수증은 만들지 않는다.
- 제안 확인: 여섯 장 모두 세로 휴대폰 구도, 한글 타이틀, 전투의 영주·밭 성장 단계·건설·적·공격·HUD를 육안 확인했다. 독립 문서 검토에서 5개 보스 ID·10개 챕터 ID 및 보스 배정을 확인했다. 실제 게임 구현이나 플레이 품질 합격을 뜻하지 않는다.
- 기존 실행 데이터·프로필·Core·Unity와 원본 의뢰서는 변경하지 않는다. 이미지의 장소명·장비 별칭은 후보의 표시 이름이며 ID 변경이 아니다.
