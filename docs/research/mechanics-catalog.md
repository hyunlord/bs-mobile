관문: 통과 — 문헌 조사 기준; 지정11개 게임, 107개 근거 행, 공격15형태. 현행 빌드 재현·재미 검증은 별도다.
범위: 공격 형태·성장·진화/합성·물품 문법·적·보스·판 구조·판 밖 성장.
구분: 출처로 확인한 동작, 재미를 만드는 이유에 대한 해석, 우리 게임 적용 제안을 분리한다.
제외: 수치·매출 조사, 밸런스 보정, SDK·Lattice·게임 메커니즘 구현.

# 메커니즘 레퍼런스 카탈로그

- 의뢰서: [Phase2B v2](../design/09_ASTRA_GOAL_bs-mobile_phase2b-system-design_v2.md), [D2 #134](https://github.com/hyunlord/bs-mobile/issues/134).
- 선행: D1 [PR #136](https://github.com/hyunlord/bs-mobile/pull/136), 병합 `3f402f398311dfaf202bc2fc85df02b36a444d3d`.
- 확인일: **2026-10-10 (한국 시간)**. 각 행의 출처를 이날 열어 확인했다. 발행일·게임 버전과 확인일은 다르다.
- `[공식]` 개발자·퍼블리셔 자료, `[커뮤니티]` 위키·사용자 가이드·토론, 매체/편집 가이드, 개발자 글의 제삼자 미러를 출처 원장에서 구분한다. 출시 후 바뀔 수 있는 설명이며 이 문서는 모든 최신 버전의 실기 검증을 뜻하지 않는다.
- **재미 이유는 분석 가설**, **우리 게임 적용은 제안**이다. 원작에서 실제 사용하는 동작과 우리 설계 아이디어를 한 문장으로 합치지 않는다. 이번 작업에서 각 게임을 직접 플레이하거나 재미를 검증하지 않았다.
- 물품의 대가는 명시된 부작용·소모와 선택/위치/기회의 제약을 구분한다. 출처에 대가가 없으면 원작의 페널티를 만들어내지 않는다. 우리 게임에 제안하는 대가는 별도 해석이다.

## 읽는 법

각 행은 구체적인 입력/조건과 결과를 가진 메커니즘 단위다. 같은 효과의 피해량·체력·가격 차이는 별도 메커니즘으로 세지 않는다. `R-게임-번호`는 D3가 출처 있는 발상을 추적할 수 있는 식별자이며 게임 콘텐츠 ID나 구현 승인 번호가 아니다.

우리 게임의 적용 판단은 다음 질문으로 좁힌다. 도구가 공격으로 드러나는가, 지도에 남은 성장물이 후속 선택을 바꾸는가, 플레이어가 이동과 카드 선택으로 인과를 읽을 수 있는가, 적의 대응이 그 인과를 시험하는가. 답이 단순 수치 상승뿐이면 메커니즘 채택 근거로 부족하다.

## 공격 형태 색인

총 **15형태**를 구별한다. 같은 형태의 게임별 예시는 중복 집계하지 않는다. 하나의 무기가 여러 단계를 가질 수 있으며, 낙하와 지면 잔류는 각각 발동 경로와 지속 판정이다. 표적 자동 선택만으로 형태 수를 늘리지 않는다.

| 형태 | 동작의 구별 | 근거 행 |
|---|---|---|
| 부채꼴 근접 | 앞쪽 호를 휘두름 | [R-HC-01](#r-hc-01) |
| 직선 찌르기 | 좁은 전방을 찌름 | [R-BR-02](#r-br-02) |
| 관통 직선 | 진행선의 여러 적을 통과 | [R-VS-01](#r-vs-01), [R-VS-06](#r-vs-06) |
| 부메랑 | 발사 뒤 돌아오는 귀환 경로 | [R-VS-03](#r-vs-03) |
| 궤도 | 중심 주위를 공전하며 접촉 | [R-VS-04](#r-vs-04) |
| 장판 | 지면에 남아 지속 작용 | [R-HC-04](#r-hc-04) |
| 연쇄 | 적중 뒤 다른 적으로 연결 | [R-BR-02](#r-br-02) |
| 낙하 투하 | 하늘에서 지면으로 도달한 뒤 잔류; 별도 충돌 피해 미확인 | [R-VS-05](#r-vs-05) |
| 추적 탄 | 발사체가 적을 찾아 이동 | [R-20-01](#r-20-01) |
| 반사 | 진행 중 튕겨 경로 변경 | [R-VS-06](#r-vs-06) |
| 소환 포탑 | 설치된 별도 공격 주체 | [R-BR-01](#r-br-01) |
| 덫 설치 | 이동 후 남겨 미래 접근에 대비 | [R-K2-03](#r-k2-03) |
| 오라 | 본체 주변 지속 피해 | [R-VS-07](#r-vs-07) |
| 후방 벽 | 뒤쪽 면을 생성해 타격 | [R-HC-03](#r-hc-03) |
| 방향 화염 파동 | 발동 순간 방향으로 파동 발사 | [R-HT-02](#r-ht-02) |

렌즈 통과 변환·자동 표적·슬롯 반환은 중요한 메커니즘이지만 위 형태 수에 더하지 않는다.

## 게임별 근거

## Vampire Survivors

| ID | 분류 | 확인 사실 | 재미 해석 — 추론 | 전이 제안 — 가설 | 주장별 출처 |
|---|---|---|---|---|---|
| <a id="r-vs-01"></a>R-VS-01 | 공격 형태 | Whip은 수평으로 공격하며 적을 관통한다. | 줄을 맞추는 이동이 공격 효율을 바꾼다. | 수확 도구의 직선 작업면과 공격면을 일치시키는 후보. | [S-VS-W](https://vampire.survivors.wiki/w/Weapons) |
| <a id="r-vs-02"></a>R-VS-02 | 공격 형태 | Knife는 바라보는 방향으로 빠르게 발사한다. | 방향 선택이 화력 집중과 도주 사이의 선택이 된다. | 전방 발사 도구는 이동 방향과 바라보는 방향을 구분해 시험. | [S-VS-W](https://vampire.survivors.wiki/w/Weapons) |
| <a id="r-vs-03"></a>R-VS-03 | 공격 형태 | Cross는 가까운 적을 겨냥하고 부메랑처럼 돌아온다. | 왕복 경로를 이용하면 이동 자체가 추가 적중 계획이 된다. | 왕복 도구의 복귀선이 성장 지점을 다시 통과하는 후보. | [S-VS-W](https://vampire.survivors.wiki/w/Weapons) |
| <a id="r-vs-04"></a>R-VS-04 | 공격 형태 | King Bible은 캐릭터 주변을 공전한다. | 접근 거리와 공전 위치를 맞추는 공간 판단이 생긴다. | 도구의 공전 공격과 인접 땅 작업을 같은 궤도로 표현하는 후보. | [S-VS-W](https://vampire.survivors.wiki/w/Weapons) |
| <a id="r-vs-05"></a>R-VS-05 | 공격 형태 | Santa Water는 하늘에서 성수병을 떨어뜨린다. 병은 지면에 닿아 깨져 피해를 주는 물웅덩이와 푸른 불꽃을 남긴다. 별도 충돌 피해는 미확인. | 낙하 위치를 보고 다음 공격 구역에 맞춰 이동한다. | 낙하 신호·지면 도달·잔류 작업 영역을 구분하는 도구 후보. | [S-VS-SW](https://vampire.survivors.wiki/w/Santa_Water), 확인 2026-10-10 |
| <a id="r-vs-06"></a>R-VS-06 | 공격 형태 | Runetracer는 적을 관통하고 튕기는 투사체다. | 발사 후 경로가 다시 전장을 가르며 예상 밖의 연속 적중을 만든다. | 지도 구조와 반사 경로의 관계를 검토하되 장애물 규칙은 별도 결정. | [S-VS-W](https://vampire.survivors.wiki/w/Weapons) |
| <a id="r-vs-07"></a>R-VS-07 | 공격 형태 | Garlic은 근처 적에게 피해를 준다. | 근접 위험을 감수하는 경계가 명확하다. | 영웅 주변 작업 반경을 공격 오라와 시각적으로 연결하는 후보. | [S-VS-W](https://vampire.survivors.wiki/w/Weapons) |
| <a id="r-vs-08"></a>R-VS-08 | 성장·레벨업 | 보유 무기의 레벨업은 무기별 능력을 높이며 최대 레벨 무기는 일반 선택지에서 빠진다. | 한 무기 완성 후 선택 공간이 바뀐다. | 무기별 성장표와 완성 상태를 카드 선택에서 읽히게 한다. | [S-VS-W](https://vampire.survivors.wiki/w/Weapons) |
| <a id="r-vs-09"></a>R-VS-09 | 진화·합성 | 일반적인 진화는 최대 레벨 기본 무기·대응 패시브·보물상자가 필요하며 예외도 있다. | 성장 투자와 보상 획득 시점이 연결된다. | 무기×도구 진화를 화면상의 성장 완료 사건과 연결하는 후보. | [S-VS-W](https://vampire.survivors.wiki/w/Weapons) |
| <a id="r-vs-10"></a>R-VS-10 | 조건부 효과·대가 | Pentagram은 주기적으로 화면을 지우며 전리품도 지울 수 있다. 레벨·Luck에 따라 전리품 보존 가능성이 달라진다. | 즉시 생존과 회수할 보상 사이에 긴장이 생긴다. | 강력한 정리 효과의 대가는 숨기지 않고 사전 표시한다. 전리품 삭제를 채택한다는 뜻은 아니다. | [S-VS-P](https://vampire.survivors.wiki/w/Pentagram) |
| <a id="r-vs-11"></a>R-VS-11 | 적 유형 | Twin Snakes는 움직이지 않고 투사체를 발사한다. | 적을 끌고 다니기만 해서는 해결되지 않는 위험 지점을 만든다. | 성장 거점에 접근하는 경로를 바꾸는 고정 사격 적 후보. | [S-VS-E](https://vampire.survivors.wiki/w/Enemies) |
| <a id="r-vs-12"></a>R-VS-12 | 보스 | Sketamari는 다른 적을 흡수하고 그들의 체력을 더한다. | 주변 적 정리가 보스 난이도에 직접 영향을 준다. | 번영이 만든 전장 상태와 보스 강화의 인과를 명료하게 연결하는 후보. | [S-VS-E](https://vampire.survivors.wiki/w/Enemies) |
| <a id="r-vs-13"></a>R-VS-13 | 런 구조·메타 성장 | Adventures는 제한된 무기군과 챕터 목표를 가진 별도 진행이며, 본편의 기존 해금을 지우지 않는다. | 같은 재료에 제한을 걸어 익숙한 조합을 새롭게 보게 한다. | 역사 프로필을 보존하면서 별도 도전 규칙을 구성하는 참고. | [S-VS-A](https://poncle.games/adventures-faq) |

## Brotato

| ID | 분류 | 확인 사실 | 재미 해석 — 추론 | 전이 제안 — 가설 | 주장별 출처 |
|---|---|---|---|---|---|
| <a id="r-br-01"></a>R-BR-01 | 공격 형태·성장 | Wrench는 휩쓸기 근접 무기이며 등급별 포탑을 만든다. 무기 분류상 Sweep은 주위의 넓은 곡선을 공격한다. | 직접 공격과 남겨 놓은 지원물이 동시에 성장한다. | 도구의 공격 면과 지도에 남는 건물 면을 연결하는 직접적인 구조 참고. | [S-BR-W](https://brotato.wiki.spellsandguns.com/Wrench), [S-BR-A](https://brotato.wiki.spellsandguns.com/Weapons) |
| <a id="r-br-02"></a>R-BR-02 | 공격 형태·성장 | Lightning Shiv는 찌르기 적중에서 다른 적을 향한 번개를 만들고, 상위 등급 번개는 연쇄한다. | 짧은 사거리 적중이 군중 전체로 보상된다. | 사람·건물의 발동도 최초 적중과 후속 연쇄를 구분해 읽히게 하는 후보. | [S-BR-L](https://brotato.wiki.spellsandguns.com/Lightning_Shiv) |
| <a id="r-br-03"></a>R-BR-03 | 성장·합성 | 같은 무기·같은 등급 둘을 합쳐 더 높은 등급 무기를 만든다. 두 원본은 합성에 쓰인다. | 중복 획득이 완성 경로가 된다. | 중복 카드 처리의 참고. 기존 레벨 표를 합성 방식으로 바꾼다는 결정은 아니다. | [S-BR-S](https://brotato.wiki.spellsandguns.com/Shop) |
| <a id="r-br-04"></a>R-BR-04 | 아이템 발동→효과→대가 | Baby with a Beard는 적 사망에서 탄환을 만들며 사거리 감소가 붙는다. | 처치 연쇄의 이익과 교전 거리 손실을 한 물품에서 비교한다. | 물품의 발동·이익·부작용을 카드와 결산에서 같은 용어로 보여 준다. | [S-BR-B](https://brotato.wiki.spellsandguns.com/Baby_with_a_Beard) |
| <a id="r-br-05"></a>R-BR-05 | 아이템 발동→효과 | Hunting Trophy는 치명타 처치에서 확률적으로 자재를 준다. 그 자재는 경험치가 아니다. | 전투 방식과 런 내 구매력을 연결한다. | 처치 보상을 식량·자재·경험치와 혼동하지 않게 구별한다. 별도 전투 대가는 출처 미확인. | [S-BR-H](https://brotato.wiki.spellsandguns.com/Hunting_Trophy) |
| <a id="r-br-06"></a>R-BR-06 | 적 유형 | Spitter는 가까워지면 달아나며 사격한다. Slasher Egg는 제거하지 않으면 부화한다. | 추격 과제와 시간 제한 과제가 같은 전장에 공존한다. | 지도 성장물과 구별되는 위협 표식을 먼저 설계하는 후보. | [S-BR-E](https://brotato.wiki.spellsandguns.com/Enemies) |
| <a id="r-br-07"></a>R-BR-07 | 보스 | Invoker는 플레이어 주변 투사체 구역을 만들고 단계에 따라 범위와 움직이는 고리 패턴을 바꾼다. | 피해량보다 피해야 할 공간 규칙을 학습한다. | 보스 단계마다 안전 영역의 규칙을 변화시키는 참고. | [S-BR-E](https://brotato.wiki.spellsandguns.com/Enemies) |
| <a id="r-br-08"></a>R-BR-08 | 런 구조 | 웨이브 사이 상점에서 물품을 사고 선택지를 잠그거나 자재를 내고 다시 뽑는다. | 즉시 구매와 다음 웨이브 투자 사이의 선택이 생긴다. | 전투 중 배치 UI는 도입하지 않고 레벨업·결산 선택의 정보 구조만 참고한다. | [S-BR-S](https://brotato.wiki.spellsandguns.com/Shop) |
| <a id="r-br-09"></a>R-BR-09 | 메타 성장 | Crazy로 승리하면 Hunting Trophy가 해금된다. | 특정 플레이 방식의 성공이 미래 선택지를 늘린다. | 도전 보상을 무조건 능력치 증가 외의 선택지 해제로 검토한다. | [S-BR-P](https://brotato.wiki.spellsandguns.com/Achievements) |

## Megabonk

| ID | 분류 | 확인 사실 | 재미 해석 — 추론 | 전이 제안 — 가설 | 주장별 출처 |
|---|---|---|---|---|---|
| <a id="r-mb-01"></a>R-MB-01 | 공격 형태 | Spooky Update는 Dragon's Breath가 플레이어 회전 대신 적을 자동 추적하도록 변경했다고 설명한다. | 공격 방향 관리 부담을 줄이고 이동 판단을 앞세운다. | 자동 표적 도구와 방향성 도구의 조작 부담을 비교하는 참고. 원뿔 판정은 이 출처로 확인하지 않았다. | [S-MB-N, Spooky Update](https://steamcommunity.com/app/3405340/announcements/) |
| <a id="r-mb-02"></a>R-MB-02 | 성장·레벨업 | 적을 처치해 경험치를 모으면 무작위 희귀도의 업그레이드 선택을 받는다. | 같은 시작에서도 제시되는 성장 경로가 달라진다. | 성장 축의 다양성과 선택 편향을 기록하는 후보. 희귀도 수치는 제안하지 않는다. | [S-MB-S](https://store.steampowered.com/app/3405340/Megabonk/) |
| <a id="r-mb-03"></a>R-MB-03 | 아이템 발동→효과→조건 | Christmas Patch는 Golden Shield가 피해를 받을 때 골드를 주며 일부 자해 상호작용을 조정했다고 설명한다. | 맞을 위험을 보상 기회로 다시 해석한다. | 피격 보상은 생존 손실이라는 발동 조건을 명시하고 안전한 무한 발동 여부를 따로 검사한다. 별도 추가 비용은 확인 안 됨. | [S-MB-N, Christmas Patch](https://steamcommunity.com/app/3405340/announcements/) |
| <a id="r-mb-04"></a>R-MB-04 | 적 유형 | Patch #2는 선인장 투사체의 방어·회피 상호작용과 폭발 거미를 다룬다. | 원거리 회피와 근접 폭발 거리 관리가 다른 행동을 요구한다. | 적 위협의 공격 형태별 판독성과 대응 수단을 기록한다. | [S-MB-N, Patch #2](https://steamcommunity.com/app/3405340/announcements/) |
| <a id="r-mb-05"></a>R-MB-05 | 보스 | Patch #2의 최종 보스는 단계마다 무기를 돌려주며, 파일런 구역은 보스를 회복시키고 파란 구체는 빙결시킨다. | 빌드 회복과 전장 장치 대응이 단계 진행을 체감하게 한다. | 지도 성장물과 보스 장치의 소유·효과를 혼동하지 않도록 설계하는 참고. 무기 박탈 도입 결정은 아니다. | [S-MB-N, Patch #2](https://steamcommunity.com/app/3405340/announcements/) |
| <a id="r-mb-06"></a>R-MB-06 | 런 구조·메타 성장 | 무작위 생성 지도에서 생존하며 퀘스트를 통해 캐릭터·무기·물품을 해금한다. | 탐색과 반복 목표가 다른 빌드 시도를 유도한다. | 지도 위 성장의 반복성과 도전 해금의 연결을 검토한다. 추가 영웅 구현은 제안 범위 밖이다. | [S-MB-S](https://store.steampowered.com/app/3405340/Megabonk/) |
| <a id="r-mb-07"></a>R-MB-07 | 지도 상호작용 | H A T S 패치는 항아리·화병에서 Luck을 얻는 탐색 유인을 설명한다. | 이동 중 작은 목표가 전투 외 경로 결정을 만든다. | 공격으로 발동하는 작업이 지도 이동 목적을 만드는지 검토한다. 지도 영속 성장과 동일한 시스템이라는 뜻은 아니다. | [S-MB-N, H A T S](https://steamcommunity.com/app/3405340/announcements/) |

## Survivor.io

| ID | 분류 | 확인 사실 | 재미 해석 — 추론 | 전이 제안 — 가설 | 주장별 출처 |
|---|---|---|---|---|---|
| <a id="r-si-01"></a>R-SI-01 | 공격 형태 | Kunai는 가까운 적을 자동 겨냥한다. Shotgun·Sword·Bat는 움직이는 방향을 따른다. | 무기 선택이 손의 부담과 위치 잡기를 바꾼다. | 방향성 공격은 별도 바라보기 계약을 분명히 한다. 원문의 이동 방향을 부채꼴 각도 근거로 쓰지 않는다. | [S-SI-A](https://apps.apple.com/us/iphone/story/id1641743438) |
| <a id="r-si-02"></a>R-SI-02 | 공격 형태 | Guardian은 주위를 회전하고 Forcefield는 주변 방어 영역을 만든다. | 바깥 교전과 안쪽 방어의 역할이 구별된다. | 아군·도구의 영역 표시에 공격과 방어 역할 차이를 드러내는 후보. | [S-SI-A](https://apps.apple.com/us/iphone/story/id1641743438) |
| <a id="r-si-03"></a>R-SI-03 | 진화·합성 | 두 종류 Drone은 Destroyer로 합쳐지며 무기 기술 슬롯 하나가 비워진다. | 합성 보상이 강해짐뿐 아니라 다음 선택의 여유로 나타난다. | 무기×도구 진화의 슬롯 처리와 UI를 별도 계약으로 명시한다. | [S-SI-A](https://apps.apple.com/us/iphone/story/id1641743438) |
| <a id="r-si-04"></a>R-SI-04 | 진화·공격 형태 | Durian은 Caltrops로, Molotov는 주변에 푸른 불꽃을 두르는 Fuel Barrels로 진화한다. | 진화 전후 화면의 공격 범위 변화가 성장을 보인다. | 진화는 숫자 상승만 아니라 작업·공격 형태 변화를 후보로 둔다. | [S-SI-A](https://apps.apple.com/us/iphone/story/id1641743438) |
| <a id="r-si-05"></a>R-SI-05 | 물품 발동→효과 | 폭탄과 자석은 화면 밖에서도 남는다. 나중에 폭탄으로 적을 정리하고 자석으로 경험치를 회수할 수 있다. | 보상 위치가 미래의 복귀 경로가 된다. | 지도 위 완성물의 회수 시점을 선택하게 하는 후보. 기회비용 해석은 추론이며 자원 비용은 확인 안 됨. | [S-SI-A](https://apps.apple.com/us/iphone/story/id1641743438) |
| <a id="r-si-06"></a>R-SI-06 | 메타 성장 | Trials의 골드 DNA로 Rogue 재능을 해금하면 런 중 선택지를 새로 뽑을 수 있다. | 판 밖 성장이 다음 판 선택 통제력을 늘린다. | 메타가 전투 수치 외 선택권에 미치는 영향도 별도로 검토한다. | [S-SI-A](https://apps.apple.com/us/iphone/story/id1641743438) |
| <a id="r-si-07"></a>R-SI-07 | 적·보스 대응 | 가이드는 다수의 일반 적과 Mega Devourer 보스에 다른 무기 선택을 권한다. Lightning Emitter의 보스 표적화를 설명한다. | 군중 처리와 우선 표적 제거 사이의 역할 차이가 드러난다. | 아군·무기별 표적 규칙과 과잉 타격 낭비를 함께 관찰한다. 보스 세부 공격 패턴은 미확인. | [S-SI-A](https://apps.apple.com/us/iphone/story/id1641743438) |
| <a id="r-si-08"></a>R-SI-08 | 런 구조 | 가이드는 개방된 넓은 레벨에서 군중 처리에 Molotov·Drone을 활용하는 예를 든다. | 같은 공격도 지도 공간에 따라 가치가 달라진다. | 지면 성장물이 이동 공간과 공격 효율에 주는 영향을 비교한다. 장별 상세 규칙은 미확인. | [S-SI-A](https://apps.apple.com/us/iphone/story/id1641743438) |


## HoloCure

버전 경계: HC-W/HC-G는 제목에 명시된 **0.6 시대** 자료이며 현행 전체와 동일하다고 보지 않는다. HC-P는 2023년 당시 관찰이다. 공식 HC-N의 0.7 변경 기록이 구자료와 충돌하면 공식 변경을 우선한다. 예를 들어 Fan Beam의 수평 전용 제약은 0.7에서 제거되었으므로 현행 제약으로 인용하지 않는다. 공식 스토어는 무기·아이템 빌드와 팬 무리를 확인하나 상세 판정 규칙은 설명하지 않는다.

| ID | 범주 | 확인된 메커니즘(출처 시점) | 재미 해석(추론) | 이식 제안 | 직접 출처·확인일 |
|---|---|---|---|---|---|
| <a id="r-hc-01"></a>R-HC-01 | 공격 형태 | Scythe Swing은 전방 반원 호를 휘두른다. | 앞을 정하는 이동이 군중 제어를 결정한다. | 낫 도구가 바라보는 호를 베고 같은 자리에 수확 흔적을 남긴다. | [HC-W: Weapons 0.6](https://www.gamezebo.com/walkthroughs/holocure-weapons/) · 2026-10-10 |
| <a id="r-hc-02"></a>R-HC-02 | 공격 형태 | Tarot Cards는 돌아오는 부메랑형 카드다. | 발사 때와 귀환 때 서로 다른 위치 판단이 생긴다. | 회수 도구가 성장물 사이를 오가며 귀환선으로 공격한다. | [HC-W](https://www.gamezebo.com/walkthroughs/holocure-weapons/) · 2026-10-10 |
| <a id="r-hc-03"></a>R-HC-03 | 공격 형태 | Cutting Board는 뒤쪽 적을 때리는 벽을 만든다. | 도주 방향의 뒤를 보호하는 명확한 용도가 생긴다. | 방책 도구가 후방 타격 뒤 지도에 방호 흔적을 남긴다. | [HC-W](https://www.gamezebo.com/walkthroughs/holocure-weapons/) · 2026-10-10 |
| <a id="r-hc-04"></a>R-HC-04 | 공격 형태 | Elite Lava Bucket은 지면에 피해 영역을 남긴다. | 지나간 위치가 다음 적의 이동 비용이 된다. | 살포 도구의 공격 영역이 밭 성장과 겹치되 적 경고를 가리지 않는다. | [HC-W](https://www.gamezebo.com/walkthroughs/holocure-weapons/) · 2026-10-10 |
| <a id="r-hc-05"></a>R-HC-05 | 성장·레벨업 | Trident Thrust는 전방 찌르기에서 V자 추가 찌르기, 갈래 찌르기로 변한다. | 레벨업이 타격 모양과 유효 위치를 바꾼다. | 성장표에 피해량 외 발사 갈래·판정 방향을 기록한다. | [HC-W](https://www.gamezebo.com/walkthroughs/holocure-weapons/) · 2026-10-10 |
| <a id="r-hc-06"></a>R-HC-06 | 성장·레벨업 | Paint Brush의 최종 단계는 적 도색과 지면 페인트 피해를 추가한다. | 무기 성장 결과가 화면의 흔적으로 읽힌다. | 붓·갈퀴류의 성장 결과를 지도 흔적과 공격 기능의 동시 변화로 설계한다. | [HC-W](https://www.gamezebo.com/walkthroughs/holocure-weapons/) · 2026-10-10 |
| <a id="r-hc-07"></a>R-HC-07 | 진화·융합 | 최대 성장한 지정 무기 둘을 Golden Anvil에서 Collab으로 합치며 슬롯이 비워진다. | 장기 조합을 준비한 뒤 새 선택 여지를 얻는다. | 무기×도구 결합이 전투·지도 양쪽 행동을 바꾸고 조합 비용을 명시하게 한다. | [HC-G: General Guide 0.6](https://steamcommunity.com/sharedfiles/filedetails/?id=3021610470) · 2026-10-10 |
| <a id="r-hc-08"></a>R-HC-08 | 진화·융합 | Jingisukan은 Elite Cooking과 Uber Sheep의 Super Collab이며, 생성 영역 안에 서면 회복한다. | 공격 공간과 회복 공간을 함께 확보하려는 움직임이 생긴다. | 공격으로 남긴 성장물 근처에서만 회복을 돌려받는 결합 후보를 검토한다. | [HC-A: 100% Achievements](https://steamcommunity.com/sharedfiles/filedetails/?id=3023862734) · 2026-10-10 |
| <a id="r-hc-09"></a>R-HC-09 | 아이템: 발동→효과→대가 | Halu 획득 후 처치→추가 코인; 동시에 팬의 출현과 강도가 증가한다. | 더 얻기 위해 더 위험한 판을 자발적으로 고른다. | 수확 보상을 늘리되 성장물에 대한 적의 관심도 바뀌는 특허장 후보. 숫자 조정은 하지 않는다. | [HC-G](https://steamcommunity.com/sharedfiles/filedetails/?id=3021610470) · 2026-10-10 |
| <a id="r-hc-10"></a>R-HC-10 | 아이템: 발동→효과→대가 | Stolen Piggy Bank는 이동→코인 획득; 이동 성능 증가와 함께 획득 범위가 줄어든다. | 달리기와 가까이 돌아와 줍기 사이에 충돌이 생긴다. | 도구 흔적을 길게 남기는 이익과 수확 복귀 동선의 부담을 엮는다. | [HC-G](https://steamcommunity.com/sharedfiles/filedetails/?id=3021610470) · 2026-10-10 |
| <a id="r-hc-11"></a>R-HC-11 | 적 | 공식 설명의 적 무리는 정신 지배된 팬이며, 처치·구제와 무기·아이템 수집 빌드를 결합한다. **일반 적별 행동은 이 공식 페이지로 미확인**. | 적 집단을 세계관 역할로 묶으면 전투 목적이 읽힌다. | 땅·건물·사람을 노리는 역할을 명명하되, 이 자료를 세부 AI 근거로 삼지 않는다. | [HC-S: 공식 Steam](https://store.steampowered.com/app/2420510/HoloCure__Save_the_Fans/) · 2026-10-10 |
| <a id="r-hc-12"></a>R-HC-12 | 보스 패턴 | Smol Ame는 공중으로 뛰고 지면을 찍으며 그림자가 착지 위치를 알린다. | 피해 전에 피할 정보를 읽고 응답한다. | 성장물 파괴형 보스의 착지 예고를 지면 흔적과 별도 우선순위로 표시한다. | [HC-P: Rice Digital](https://ricedigital.co.uk/holocure-major-update/) · 2026-10-10 |
| <a id="r-hc-13"></a>R-HC-13 | 런 구조 | 당시 Stage Mode는 제한 시간 뒤 보스를 쓰러뜨려 마무리한다. | 생존 후에도 빌드의 결론을 시험하는 종착점이 있다. | 챕터마다 축적한 지도 빌드를 시험하는 최종 패턴을 둔다. | [HC-P](https://ricedigital.co.uk/holocure-major-update/) · 2026-10-10 |
| <a id="r-hc-14"></a>R-HC-14 | 메타 성장 | Fandom은 캐릭터별 목표 달성과 보상을 제공한다. | 같은 캐릭터를 익힌 과정이 다음 목표로 이어진다. | 추가 영웅 없이 가신·도구의 행동 숙련 목표로 적용 가능성을 검토한다. | [HC-P](https://ricedigital.co.uk/holocure-major-update/) · 2026-10-10 |

## 20 Minutes Till Dawn

버전 경계: 20-K는 **v0.6.1(2022)** 역사 목록, 20-L은 2022년 모바일 조작을 설명하는 가이드다. PC·모바일의 조작을 동일시하지 않는다. 20-S 공식 현행 스토어로 방향 조준·직접 발사·런과 Souls/Runes를 확인했다. 20-N의 2024-10-28 `Blessings & Curses`는 공개 베타 예정 발표로, 최신 출시 완료 증거로 사용하지 않는다. 아래 역사 메커니즘의 최신 계수·존속은 재현하지 않았다.

| ID | 범주 | 확인된 메커니즘(출처 시점) | 재미 해석(추론) | 이식 제안 | 직접 출처·확인일 |
|---|---|---|---|---|---|
| <a id="r-20-01"></a>R-20-01 | 공격 형태 | Light Weaponry는 적을 추적하는 Magic Dagger를 소환한다. | 이동·조준과 독립적으로 위험 표적에 접근한다. | 일꾼이 남긴 표식을 추적하는 공격 도구 후보. 추적 대상 규칙을 명시한다. | [20-K: All Characters, Items and Synergies v0.6.1](https://kosgames.com/20-minutes-till-dawn-all-characters-items-and-synergies-v0-6-1-24468/) · 2026-10-10 |
| <a id="r-20-02"></a>R-20-02 | 공격 형태 | Heavy Weaponry는 플레이어 주위를 도는 Magic Scythe다. | 안전 거리와 타격 궤도가 서로 맞물린다. | 성장물 주변으로 순찰 궤도를 옮기는 도구 후보. 이동식과 고정식 궤도를 구별한다. | [20-K](https://kosgames.com/20-minutes-till-dawn-all-characters-items-and-synergies-v0-6-1-24468/) · 2026-10-10 |
| <a id="r-20-03"></a>R-20-03 | 공격 형태 | Magic Lens를 통과한 탄이 강화되고 Refraction은 통과 탄에 추가 도탄을 준다. | 공격이 지나가는 길 자체가 빌드 자원이 된다. | 완성된 건물을 통과하는 공격의 궤도·효과를 바꾸는 지도 시너지 후보. | [20-K](https://kosgames.com/20-minutes-till-dawn-all-characters-items-and-synergies-v0-6-1-24468/) · 2026-10-10 |
| <a id="r-20-04"></a>R-20-04 | 성장·레벨업 | Dragon Egg를 고르면 후속 Aged/Trained Dragon 후보가 열리고 그중 하나가 Dragon Bond로 이어진다. | 즉시 전력과 나중 보상을 위한 투자 순서가 생긴다. | 사람 성장의 전직 경로를 선택 이력으로 열고 레벨업 창에서 후속을 보여 준다. | [20-L: Beginner's Guide](https://www.levelwinner.com/20-minutes-till-dawn-beginners-guide-tips-tricks-strategies-to-survive-till-dawn-and-unlock-everything/) · 2026-10-10 |
| <a id="r-20-05"></a>R-20-05 | 진화·융합 | Death Rounds는 Reaper Rounds와 Light Bullets를 모두 갖춘 뒤 선택 후보가 된다. | 서로 다른 성장 가지를 연결하는 계획을 세운다. | 도구×무기 소유만이 아니라 양쪽 성장 행동을 충족하는 진화 조건 후보. | [20-L](https://www.levelwinner.com/20-minutes-till-dawn-beginners-guide-tips-tricks-strategies-to-survive-till-dawn-and-unlock-everything/) · 2026-10-10 |
| <a id="r-20-06"></a>R-20-06 | 아이템/강화: 발동→효과→대가 | Focal Point 획득→렌즈 효과 강화; 대신 렌즈가 작아진다. | 더 강한 보상을 받으려면 탄의 통과 경로를 더 정확히 맞춰야 한다. | 건물 통과 공격에 효율과 통과 폭의 명시적 상충을 부여하는 후보. | [20-K](https://kosgames.com/20-minutes-till-dawn-all-characters-items-and-synergies-v0-6-1-24468/) · 2026-10-10 |
| <a id="r-20-07"></a>R-20-07 | 아이템/강화: 발동→효과→대가 | 보스 보상의 Tome of Speed 선택→이동 성능 증가; 최대 체력 감소. | 회피 능력과 실수 허용량 중 무엇을 믿을지 고른다. | 특허장의 이득·대가가 서로 다른 행동 능력을 바꾸도록 설계한다. | [20-L](https://www.levelwinner.com/20-minutes-till-dawn-beginners-guide-tips-tricks-strategies-to-survive-till-dawn-and-unlock-everything/) · 2026-10-10 |
| <a id="r-20-08"></a>R-20-08 | 적 | 가이드의 흰색 폭발형 일반 적은 죽을 때 근처 다른 괴물에도 피해를 준다. 고유명은 해당 자료에 미기재. | 위험한 적을 처치 순서와 군중 정리의 도구로 활용한다. | 군집 사이에 유인해 터뜨릴 수 있는 적 후보. 아군 성장물 피해 여부는 별도 설계 결정이다. | [20-L](https://www.levelwinner.com/20-minutes-till-dawn-beginners-guide-tips-tricks-strategies-to-survive-till-dawn-and-unlock-everything/) · 2026-10-10 |
| <a id="r-20-09"></a>R-20-09 | 보스 패턴 | 돌진 보스는 준비 신호 때 경로가 정해져 옆으로 피할 수 있다. 주변 잡몹이 회피선을 막는다. | 예고 이해와 탈출로 확보를 동시에 시험한다. | 직선 돌진 보스와 길을 막는 적을 엮되, 지면 예고와 탈출 통로를 남긴다. | [20-B: Steam 플레이어 토론, 2022-09-09](https://steamcommunity.com/app/1966900/discussions/0/3374907062173114938/) · 2026-10-10 |
| <a id="r-20-10"></a>R-20-10 | 런 구조 | 공식 소개는 제한 시간 생존, 경험치 선택, 방향 조준과 직접 발사를 설명한다. | 유한 목표와 조준 책임이 매 판의 긴장을 만든다. | 시간 목표와 레벨업 결정을 참고하되 직접 사격 조작은 한 손 자동 전투 원칙에 그대로 이식하지 않는다. | [20-S: 공식 Steam](https://store.steampowered.com/app/1966900/20_Minutes_Till_Dawn/) · 2026-10-10 |
| <a id="r-20-11"></a>R-20-11 | 메타 성장 | 획득 Souls로 Runes를 강화하고 캐릭터·무기를 해금하는 진행이 런 사이에 유지된다. | 실패해도 다음 실험의 선택 폭이 남는다. | 재료별 장원·가신 해금 역할을 분리하되 전투 수치 상향안은 보류한다. | [20-S](https://store.steampowered.com/app/1966900/20_Minutes_Till_Dawn/) · 2026-10-10 |
| <a id="r-20-12"></a>R-20-12 | 보스 패턴(발표·출시 여부 미확인) | 2024 베타 예정안은 보스 고유 부하를 두고 보스전 중 일반 적 출현을 멈추도록 제안했다. 출시 확인 아님. | 보스의 의도와 전용 부하의 관계를 더 읽기 쉽다. | 우리 보스 설계에서도 전용 부하와 일반 웨이브 혼잡을 별도 검토한다. | [20-N: Blessings & Curses 공식 발표](https://steamcommunity.com/app/1966900/allnews/) · 2026-10-10 |

## Halls of Torment

버전 경계: HT-P/HT-G/HT-B/HT-E는 **2023년 얼리 액세스** 관찰이다. HT-I는 2024-11-05 커뮤니티 설명으로 태그 버그 가능성까지 기록되어 있다. HT-S 공식 스토어는 큰 구조만 증명한다. 공식 HT-N은 2026-10-05 **experimental**, 정식 반영 예정일 2026-10-22로 명시하므로 해당 변경을 현재 stable로 취급하지 않는다. 과거 보스의 위치·단계 수를 현재 챕터 전체의 사실로 확대하지 않는다.

| ID | 범주 | 확인된 메커니즘(출처 시점) | 재미 해석(추론) | 이식 제안 | 직접 출처·확인일 |
|---|---|---|---|---|---|
| <a id="r-ht-01"></a>R-HT-01 | 공격 형태 | Arcane Splinters는 세로로 벌어지는 투사체 패턴이다. | 상하 통로를 열지만 다른 방향은 비워 두는 개성이 생긴다. | 밭고랑 방향으로 뻗는 공격 도구 후보. 방향의 빈틈도 화면에 남긴다. | [HT-P: Ability Tier List, 형태 기술만](https://progameguides.com/halls-of-torment/halls-of-torment-ability-tier-list-best-abilities-for-each-character/) · 2026-10-10 |
| <a id="r-ht-02"></a>R-HT-02 | 공격 형태 | Dragon's Breath는 발동 순간 바라보는 방향으로 화염 파동을 보낸다. | 발동 타이밍 직전의 방향 선택이 중요하다. | 풀무·등불 도구를 전방 부채꼴 발동과 건물 활성화의 결합 후보로 본다. | [HT-P](https://progameguides.com/halls-of-torment/halls-of-torment-ability-tier-list-best-abilities-for-each-character/) · 2026-10-10 |
| <a id="r-ht-03"></a>R-HT-03 | 공격 형태 | Meteor Strike는 무작위 방향으로 호 형태로 발사된 투사체가 충돌 시 폭발한다. Scattered Debris는 작은 운석을 퍼뜨린다. | 최초 타격과 후속 분산 영역을 서로 다른 층으로 읽는다. | 투척 도구의 파편과 파종 흔적을 연결하되 원형 피해만 늘리지 않는다. | [HT-P](https://progameguides.com/halls-of-torment/halls-of-torment-ability-tier-list-best-abilities-for-each-character/) · 2026-10-10 |
| <a id="r-ht-04"></a>R-HT-04 | 공격 형태 | Astronomer's Orbs는 주위를 공전하며 경로에 닿는 적을 때린다. | 플레이어 위치와 궤도 반경이 실질적인 사거리다. | 호위 사람이 회전하는 공격과 건물 주위 순찰로 역할을 달리하는 후보. | [HT-P](https://progameguides.com/halls-of-torment/halls-of-torment-ability-tier-list-best-abilities-for-each-character/) · 2026-10-10 |
| <a id="r-ht-05"></a>R-HT-05 | 성장·레벨업 | Ring Blades 숙련은 공격 빈도·부채꼴 폭·도달 범위를 바꾸는 항목을 포함한다. | 성장은 피해량 외 적을 만나는 기하를 바꾼다. | 무기별 성장표에서 범위·각도·타격 수의 의미를 구분한다. | [HT-U: Ability Upgrades Guide](https://www.gamezebo.com/walkthroughs/halls-of-torment-ability-upgrades/) · 2026-10-10 |
| <a id="r-ht-06"></a>R-HT-06 | 진화·융합 | 당시 능력 성장·관련 과제는 Electrifying Strike, Phantom Rift, Electrified Orbs 같은 능력별 Upgrade를 연다. 무기 둘 융합이라는 증거는 아님. | 익힌 능력의 다른 기능을 단계적으로 탐색한다. | 융합과 단일 도구 전직을 서로 다른 계약으로 구분한다. | [HT-G: Quest/Achievement Guide 2023-07-03](https://steamcommunity.com/sharedfiles/filedetails/?id=2992231234) · 2026-10-10 |
| <a id="r-ht-07"></a>R-HT-07 | 아이템: 발동→효과→대가 | Thunder Crown은 Spark 상태 적에 물리/마법 타격→연쇄 번개를 유발한다고 설명된다. 명시적 소모 대가는 미확인; 선행 상태·타격 유형이 제약. | 상태를 만드는 수단과 그 상태를 조건으로 활용하는 타격의 조합을 찾는다. | 젖은 땅·표식·작업 중 상태를 도구 공격의 연쇄 조건으로 쓰는 후보. | [HT-I: Damage Type Modifier Upgrades, 커뮤니티](https://steamcommunity.com/app/2218750/discussions/0/4625853420295715171/) · 2026-10-10 |
| <a id="r-ht-08"></a>R-HT-08 | 적 | Forgotten Viaduct의 유령 벽이 안쪽으로 다가오며 이동 공간을 줄인다는 복수 이용자 기록. 죽일 수 있는지와 끝 시점은 서로 충돌하므로 미확인. | 적 집단이 맵의 가용 공간을 바꾼다. | 건물 파괴만 하는 적 외에 작업 구역을 압축하는 진형 적 후보. | [HT-E: Forgotten Viaduct 토론, 2023-06-20](https://steamcommunity.com/app/2218750/discussions/0/5514142829700960132/) · 2026-10-10 |
| <a id="r-ht-09"></a>R-HT-09 | 보스 패턴 | Lord of Pain은 기승/하차 국면이 있고 돌진 전 지면 원, 원형 해골탄, 이후 방향성 탄과 지면 손을 쓴다. | 같은 공간에서 피해야 하는 축이 국면마다 달라진다. | 보스 국면 전환이 성장물 보호·귀환 경로의 판단을 바꾸도록 설계한다. | [HT-B: Lord of Pain breakdown, Gamepressure](https://www.gamepressure.com/newsroom/halls-of-torment-how-to-beat-lord-of-pain-first-final-boss-explai/zc5acc) · 2026-10-10 |
| <a id="r-ht-10"></a>R-HT-10 | 런 구조 | 공식 소개는 웨이브를 버틴 뒤 Lord와 맞서는 제한 시간 런과 지하 공간 탐색을 설명한다. | 버티기 외 탐색 동선의 목적이 생긴다. | 공격·수확·유적 탐색의 동선이 겹치거나 갈라지는 챕터 장치를 검토한다. | [HT-S: 공식 Steam](https://store.steampowered.com/app/2218750/Halls_of_Torment/) · 2026-10-10 |
| <a id="r-ht-11"></a>R-HT-11 | 메타 성장 | 찾은 아이템을 지상으로 보내 다음 런 준비에 쓸 수 있으며 과제 기반 메타 진행이 있다. | 현재 전리품이 이후 런의 선택 자산이 된다. | 무장 장비 시스템을 추가하지 않고, 지도에서 회수한 재료가 특정 장원 기능을 여는 연결만 참고한다. | [HT-S](https://store.steampowered.com/app/2218750/Halls_of_Torment/) · 2026-10-10 |
| <a id="r-ht-12"></a>R-HT-12 | 메타 성장·동선 | 2023 가이드의 Wellkeeper 구출 이후 우물을 통해 아이템을 회수하고 지상에서 구입해 시작 장비로 쓴다. | 전투 중 회수 지점 방문이 장기 진행으로 이어진다. | 살아남은 일꾼이 회수한 재료와 잃어버린 재료를 결산에서 구분하는 후보. | [HT-G](https://steamcommunity.com/sharedfiles/filedetails/?id=2992231234) · 2026-10-10 |



## Atomicrops

| ID | 범주 | 확인한 원작 메커니즘 | 재미 이유 [추론] | Sow & Siege 이식 [제안] | 출처·범위 |
|---|---|---|---|---|---|
| <a id="r-ac-01"></a>R-AC-01 | 공격 형태 | Flamethrower는 적과 작물을 태운다. | 방향 선택이 자기 농장을 위험하게 만든다. | 화염 도구의 사격선과 경작지 보호를 연결. | [A1], Thyme Flies 당시 |
| <a id="r-ac-02"></a>R-AC-02 | 공격 형태 | Biodegrader는 광역 폭발물을 발사한다. | 무리와 단독 적의 처리감이 다르다. | 투척 도구의 착탄 범위에 성장 흔적 연결. | [A1], Thyme Flies 당시 |
| <a id="r-ac-03"></a>R-AC-03 | 성장 | 처치→비료→수확 품질→무기·장비 구매로 이어진다. | 전투와 농사가 서로 재료가 된다. | 적 처치 흔적을 도구 성장의 입력으로 사용. | [A1], 공식 소개 |
| <a id="r-ac-04"></a>R-AC-04 | 물품 트리거→효과→비용 | Hotwire: 트랙터 사용→재사용 대기 제거, 사용 시 피해 위험. | 반복 발동과 안전 사이 선택. | 도구 재발동 물품에 눈에 보이는 대가. | [A1], 확률 수치 제외 |
| <a id="r-ac-05"></a>R-AC-05 | 물품 트리거→효과→비용 | Early to Rose: 장미 수확→낮 연장. 별도 대가 미명시. | 성장 행동이 활동 시간을 돌려준다. | 수확이 다음 공격 준비로 환원되는 물품. | [A1], 대가 임의 보충 금지 |
| <a id="r-ac-06"></a>R-AC-06 | 성장·동료 | 동물은 농사 자동화, 배우자는 농사와 전투를 돕는다. | 아군이 경제와 전투에 함께 보인다. | 사람 도구의 노동·전투 전환을 화면으로 표현. | [A1], 공식 소개 |
| <a id="r-ac-07"></a>R-AC-07 | 런 구조 | 낮 수확·하루 끝 구매·밤 농장 방어·계절 보스가 이어진다. | 준비한 자산이 다음 위협의 표적이다. | 성장 구간 뒤 기존 흔적을 시험하는 위협. | [A2], 현행 상점 설명 |
| <a id="r-ac-08"></a>R-AC-08 | 메타 성장 | 매 런 보상으로 영구 개선과 해금을 얻는다. | 실패한 판도 다음 선택지를 남긴다. | 런 결과별 해금 이유를 장원에 표시. | [A2], 비용·속도 미검증 |

## Thronefall

| ID | 범주 | 확인한 원작 메커니즘 | 재미 이유 [추론] | Sow & Siege 이식 [제안] | 출처·범위 |
|---|---|---|---|---|---|
| <a id="r-tf-01"></a>R-TF-01 | 런 구조 | 낮 건설, 밤 방어; 적을 모두 제거해야 아침이 온다. | 준비 결과가 전투로 판정된다. | 전투 후 남은 성장물이 다음 선택의 증거. | [T1], 현행 상점 설명 |
| <a id="r-tf-02"></a>R-TF-02 | 성장 | 경제 건물이 생존하면 아침에 금을 준다. | 지켜 낸 장소가 보상 원인이다. | 남은 건물의 작동 이력을 보상과 연결. | [T1] |
| <a id="r-tf-03"></a>R-TF-03 | 성장 | Castle Center·Blacksmith·Royal Forge에서 업그레이드를 연구한다. | 기능을 가진 건물이 빌드 방향을 드러낸다. | 건물 도구마다 서로 다른 성장 반응. | [T1] |
| <a id="r-tf-04"></a>R-TF-04 | 공격 형태 | 군주는 무기를 선택하고, 병영·궁병 시설은 기사·석궁병·불화살병 등을 제공한다. | 본체와 부대의 공격 역할이 구분된다. | 사람 도구를 무기와 다른 사격·방어 역할로 구분. | [T1], 세부 판정 미검증 |
| <a id="r-tf-05"></a>R-TF-05 | 적 유형 | 비행 마법사·말벌과 투석기 등 서로 다른 적이 등장한다. | 수비 범위가 한 방향으로 고정되지 않는다. | 공성·비행 역할을 이름과 실루엣으로 구분. | [T1], 구체 약점 미검증 |
| <a id="r-tf-06"></a>R-TF-06 | 메타 성장 | 해금한 무기·특전을 출전에 선택하고 선택적 Mutator를 적용한다. | 다음 판의 규칙 선택이 늘어난다. | 가신·특허장을 출전 전략의 선택지로 제시. | [T1], 영구 능력치 성장으로 확대하지 않음 |
| <a id="r-tf-07"></a>R-TF-07 | 보스 패턴 | Shadow In The Water는 소환으로 자신도 피해를 받고, 직접 타격은 남은 소환량을 줄인다. | 보스 공격과 잡몹 정리의 우선순위 교환. | 소환 능력과 노출된 약점을 연결한 보스. | [T2], 커뮤니티 문서, 패치 미명시 |
| <a id="r-tf-08"></a>R-TF-08 | 보스·공간 | 같은 보스가 호수 주변 소환 위치를 바꾸며 등장 때 어업 항구를 파괴한다. | 익숙한 경제 공간이 새로운 전선이 된다. | 보스 단계별 성장물 위협 위치 변화. | [T2], 순서·수치 인용 제외 |

## Kingdom Two Crowns

| ID | 범주 | 확인한 원작 메커니즘 | 재미 이유 [추론] | Sow & Siege 이식 [제안] | 출처·범위 |
|---|---|---|---|---|---|
| <a id="r-k2-01"></a>R-K2-01 | 성장 | 농장·주민·벽·탑을 늘리고 확장으로 새 병종과 기술에 접근한다. | 영토가 다음 기능의 조건이다. | 도구가 남긴 성장물에 기능 변화 연결. | [K1], 본편 소개 |
| <a id="r-k2-02"></a>R-K2-02 | 런 구조·적 | 밤에 Greed를 방어하고 병사를 보내 근원을 공격한다. | 방어 성공이 반격의 준비가 된다. | 수비와 위협원 제거를 다른 목표로 표현. | [K1], 본편 소개 |
| <a id="r-k2-03"></a>R-K2-03 | 공격 형태 | Dead Lands의 거대 딱정벌레 탈것은 함정을 놓는다. | 이동이 미래 전투 장소를 만든다. | 이동 경로에 남는 도구 공격. | [K1], Dead Lands 한정 |
| <a id="r-k2-04"></a>R-K2-04 | 공격 형태 | Dead Lands의 언데드 탈것은 Greed 진행을 막는 장벽을 소환한다. | 공격력 외에 진로를 바꾸는 힘. | 자동 생성 성장물이 방어선으로 작동. | [K1], Dead Lands 한정 |
| <a id="r-k2-05"></a>R-K2-05 | 성장·런 구조 | Call of Olympus는 사원·퀘스트 섬 도전과 유물 보상으로 Olympus 길을 연다. | 목표 완료가 이동 경로를 해금한다. | 챕터 장치를 단순 수치 관문과 분리. | [K2], 확장팩 한정 |
| <a id="r-k2-06"></a>R-K2-06 | 공격 형태 | 유물 활은 방향 공격, 망치는 병사 능력을 강화한다. | 개인 공격과 아군 지원이 다른 선택. | 무기와 사람 도구의 역할 차이 유지. | [K2], 발동 비용 미명시 |
| <a id="r-k2-07"></a>R-K2-07 | 보스 구조(세부 패턴 미확인) | Serpent와 다단계 보스전을 치른다. | 단계 전환이 긴 전투를 구획한다. | 단계별로 다른 성장물을 시험하는 보스. | [K2], 세부 공격은 미검증 |
| <a id="r-k2-08"></a>R-K2-08 | 성장·런 구조 | 함대의 발리스타가 전투를 돕고 승리 후 군대를 바다 건너 운송한다. | 전투 자산이 다음 이동 수단이다. | 건물 성장의 전투 기능과 귀환 기능 연결. | [K2], 확장팩 한정 |

## Dome Keeper

| ID | 범주 | 확인한 원작 메커니즘 | 재미 이유 [추론] | Sow & Siege 이식 [제안] | 출처·범위 |
|---|---|---|---|---|---|
| <a id="r-dk-01"></a>R-DK-01 | 런 구조 | 공격 사이 채굴하고 자원을 운반해 돔을 강화한 뒤 방어한다. | 더 캐기와 제때 귀환의 갈림길. | 도구 수확과 귀환 경로에 긴장 부여. | [D1], 현행 상점 설명 |
| <a id="r-dk-02"></a>R-DK-02 | 성장 | 자원을 방어·드릴·이동 업그레이드에 쓴다. | 생산 투자가 다음 방어와 경쟁한다. | 성장 선택의 결과를 서로 다른 기능으로 표시. | [D1] |
| <a id="r-dk-03"></a>R-DK-03 | 적 유형 | 지상·공중 적의 이동과 공격 방식이 다르다. | 하나의 조준법만으로 충분하지 않다. | 적 역할별 위협 방향을 읽히게 설계. | [D1], 세부 판정 미검증 |
| <a id="r-dk-04"></a>R-DK-04 | 성장·공격 형태 | Droneyard는 자원을 운반하고 전투 드론도 제공한다. | 노동과 전투가 같은 시설로 연결된다. | 사람 도구의 작업·전투 전환. | [D2], v4.0 |
| <a id="r-dk-05"></a>R-DK-05 | 진화 유사 변형 | Supplement는 가젯 사용법을 바꾸며, 순간이동 자원 운송을 택하면 무기 Supplement를 미룬다. | 강한 변화에 선택 비용이 있다. | 진화의 기능 변화와 포기 항목 표시. | [D2], 합성 레시피 아님 |
| <a id="r-dk-06"></a>R-DK-06 | 물품 트리거→효과→비용 | Deafening Blast: 박격포 발사→근처 적 기절. Supplement 선택 비용. | 원거리 무기에 근접 대응이 붙는다. | 공격 발동을 다른 방어 작용에 연결. | [D2], 선택 비용은 일반 규칙 |
| <a id="r-dk-07"></a>R-DK-07 | 적·대응 | Tormentor는 변신 뒤 약해지지만 곧 강한 레이저를 쏜다. | 기다림과 집중 공격의 타이밍. | 예고→약점 노출→위험 발동 적. | [D2], 보스로 재분류하지 않음 |
| <a id="r-dk-08"></a>R-DK-08 | 메타 성장 | Guild Assignments 배지로 가젯 재추첨 등 영구 보상을 얻는다. | 도전 결과가 선택권을 남긴다. | 자동 장원 성장의 선택권 해금. | [D2], v4.0 |

## 공간·영지 자료의 출처와 버전 한계

모든 URL은 본문을 실제 열어 읽었다. Steam 사용자 리뷰·태그는 근거로 쓰지 않았다. 각 요약은 사실 열의 근거 위치를 찾기 위한 짧은 의역이다.

- **[A1] [Atomicrops — Thyme Flies Update / FARM, MARRY, KILL](https://www.atomicrops.com/)** — 공식 개발사 사이트. 확인 2026-10-10. 게시일·현재 패치 미명시, 과거 업데이트와 소개가 혼재. New Content의 무기·Hotwire·Early to Rose, 하단 FARM/KILL/BEFRIEND가 근거. 요약: 무기가 농장에도 작용하고 농사·전투·동료를 연결한다. 현행 확률·효율은 주장하지 않는다.
- **[A2] [Atomicrops on Steam](https://store.steampowered.com/app/757320/Atomicrops/)** — 개발사/배급사 공식 상점 소개. 확인 2026-10-10, 빌드 번호 없음. About This Game: 하루·계절 흐름과 런 보상으로 영구 개선·해금.
- **[T1] [Thronefall on Steam](https://store.steampowered.com/app/2239150/Thronefall/)** — 공식 상점 소개. 확인 2026-10-10, 현행 패치 번호 없음. About This Game: 낮/밤, 살아남은 경제 건물의 수입, 연구시설, 병종·적, 출전 무기·특전. 요약: 건설 결과를 야간 방어가 시험한다.
- **[T2] [Shadow — Thronefall wiki](https://throne-fall.github.io/game-content/enemies/shadow.html)** — 커뮤니티 위키, 공식 자료 아님. 확인 2026-10-10. 패치·수정일 미명시이므로 패턴 참고만. General/Attack Pattern: 자해 소환과 호수 주변 전선 전환. 현행 버전 재현은 미검증.
- **[K1] [Kingdom Two Crowns on Steam](https://store.steampowered.com/app/701160/Kingdom_Two_Crowns/)** — 공식 상점 소개. 확인 2026-10-10, 패치 미명시. About This Game의 본편 건설·전투, Dead Lands 항목의 함정/장벽 탈것. 요약: 확장과 방어, 이동 중 전장 변화. 기본 본편과 무료 테마 기능을 구별한다.
- **[K2] [Kingdom Two Crowns: Call of Olympus on Steam](https://store.steampowered.com/app/2736340/Kingdom_Two_Crowns_Call_of_Olympus/)** — 공식 확장팩 소개. 확인 2026-10-10, 출시 2024-10-08, 현행 패치 미명시. Temple and Quest Islands / Face Your Fears / Assemble Your Fleet / Powerful Artifacts가 근거. 요약: 섬 과제·유물·다단계 보스·함대의 전투/운송 기능. 본편 기본 규칙으로 일반화하지 않는다.
- **[D1] [Dome Keeper on Steam](https://store.steampowered.com/app/1637320/Dome_Keeper/)** — 공식 상점 소개. 확인 2026-10-10, 패치 미명시. About This Game: 채굴/운반/방어 반복, 업그레이드 선택, 지상·공중 공격. 요약: 공간 탐사 시간을 방어 준비와 교환한다.
- **[D2] [A Keeper's Duty — Out Now!](https://steamdb.info/patchnotes/15150010/)** — 개발자 패치노트의 **SteamDB 미러**. 발표 2024-07-25, v4.0/build15150010, 확인 2026-10-10. New Gadgets and supplements / Weapon supplements / New world and 5 monsters / Guild Assignments가 근거. 요약: 물류와 전투의 결합, 선택형 변형, 변신 적, 도전 보상. [공식 원문 링크](https://steamcommunity.com/games/1637320/announcements/detail/4396159337027279925)는 열었으나 텍스트 추출이 되지 않아 미러 본문을 사용했다. 최신 패치 동작으로 단정하지 않는다.

## 공간·영지 자료의 확인 한계

- Atomicrops Corpse-a-Copia 수확 피해·거대 작물 합성은 검색 결과에 있었지만 Fandom 본문 열기가 실패했다. 확정 행에 넣지 않았다. 계절 보스 존재만 A2에서 확인했다.
- Kingdom Two Crowns Breeder의 세부 패턴 역시 해당 위키 본문 접근이 실패하여 제외했다.
- 진화/합성은 모든 게임에 강제로 대응시키지 않았다. Dome Keeper의 Supplement는 **선택형 변형 사례**이고 무기×도구 합성 규칙이 아니다. 나머지 게임의 레시피 존재/부재는 이 조사로 확정하지 않는다.
- Kingdom의 캠페인 내 기술 확장을 런 사이 영구 메타 성장으로 부르지 않는다. 별도 메타 보존 규칙은 이번 열린 출처에서 확정하지 않았다.
- 기계적으로 채택할 행 목록이나 신규 콘텐츠 로스터는 제안하지 않았다. 이식 열은 D3 검토 재료이지 승인된 설계가 아니다.

## 설계로 옮길 때의 경계

- 원작의 고유 명칭·캐릭터·시각 자산은 가져오지 않는다. 공간 관계, 발동 조건, 상태 전이와 대가만 추상화한다.
- 자동 공격 게임의 조작을 참고하더라도 수동 조준/재장전/배치가 필수인 메커니즘을 그대로 이식하지 않는다. 우리 게임은 이동과 레벨업 선택이 중심이다.
- 도시/기지 방어 게임의 직접 건설은 지도 위의 성장·복구·위협 선택을 이해하는 참고다. 이것이 전투 중 건설 UI를 도입하는 승인은 아니다.
- 영구 성장 사례를 모든 능력의 퍼센트 보정으로 축약하지 않는다. 해금하는 행동·편성·출처/소비 경로와 판 안 역할을 먼저 구분한다.
- 카탈로그는 선택지다. D3 로스터와 사용자 승인 이후에만 구현 순서와 실행 검증을 확정한다.

[A1]: https://www.atomicrops.com/ "Atomicrops — Thyme Flies Update / FARM, MARRY, KILL"

[A2]: https://store.steampowered.com/app/757320/Atomicrops/ "Atomicrops on Steam"

[T1]: https://store.steampowered.com/app/2239150/Thronefall/ "Thronefall on Steam"

[T2]: https://throne-fall.github.io/game-content/enemies/shadow.html "Shadow — Thronefall wiki"

[K1]: https://store.steampowered.com/app/701160/Kingdom_Two_Crowns/ "Kingdom Two Crowns on Steam"

[K2]: https://store.steampowered.com/app/2736340/Kingdom_Two_Crowns_Call_of_Olympus/ "Kingdom Two Crowns: Call of Olympus on Steam"

[D1]: https://store.steampowered.com/app/1637320/Dome_Keeper/ "Dome Keeper on Steam"

[D2]: https://steamdb.info/patchnotes/15150010/ "A Keeper's Duty — Out Now!"

## 출처 원장 및 짧은 근거 메모

아래 모두 **실제 페이지 본문을 열어 확인**, 접근일 **2026-10-10 KST**. 근거는 번역·요약이며 긴 직접 인용이 아니다. 행과 부록을 합친 출처별 사용량을 작게 유지했다.

| ID | 정확한 페이지 제목·URL | 성격·버전 주의 | 짧은 본문 근거 요약 |
|---|---|---|---|
| S-VS-W | [Weapons - Vampire Survivors Wiki](https://vampire.survivors.wiki/w/Weapons) | 커뮤니티 위키. 특정 출시 패치 고정 아님. 일반 웹 도구 접근 실패 후 브라우징 엔진으로 실제 HTML 본문 열람. | 무기별 설명 표에 수평·전방·왕복·공전·장판·관통/반사·주변 피해. 일반 성장·진화 절차와 예외 설명. |
| S-VS-P | [Pentagram - Vampire Survivors Wiki](https://vampire.survivors.wiki/w/Pentagram) | 커뮤니티 위키. 패치 미고정, 본문 직접 열람. | 화면 제거 효과와 픽업 삭제 가능성, Luck/레벨과 보존 관계. |
| S-VS-E | [Enemies - Vampire Survivors Wiki](https://vampire.survivors.wiki/w/Enemies) | 커뮤니티 위키. 패치 미고정, 본문 직접 열람. | Twin Snakes의 고정 사격, Sketamari의 적 흡수. |
| S-VS-A | [poncle \| Adventures FAQ](https://poncle.games/adventures-faq) | 개발자 FAQ. 출시 당시와 후속 정보 혼재; 현재 콘텐츠 개수·Ascension 수치 제외. | 독립 모험 진행, 챕터 목표, 제한 무기, 기존 본편 해금 보존. |
| S-BR-A | [Weapons - Brotato Wiki](https://brotato.wiki.spellsandguns.com/Weapons) | 커뮤니티 위키 oldid=8469. 페이지는 패치 1.1.6.3을 OUTDATED로 표시. 실제 HTML 본문 직접 열람. | Thrust는 직선, Sweep은 주변의 넓은 곡선이라는 분류 정의. |
| S-BR-W | [Wrench - Brotato Wiki](https://brotato.wiki.spellsandguns.com/Wrench) | 커뮤니티 위키; 열람 revision oldid=4504. 현재 실행 검증 아님. | 근접 sweep와 등급별 포탑. |
| S-BR-L | [Lightning Shiv - Brotato Wiki](https://brotato.wiki.spellsandguns.com/Lightning_Shiv) | 커뮤니티 위키; oldid=6931. 툴팁과 연쇄 수치 불일치 언급 있어 수치 제외. | thrust 적중에서 번개, 상위 등급 연쇄. |
| S-BR-S | [Shop - Brotato Wiki](https://brotato.wiki.spellsandguns.com/Shop) | 커뮤니티 위키; oldid=8034. 패치·플랫폼 차이 미검증. | 동일 등급 동일 무기의 결합, 잠금, 재추첨, 웨이브 사이 구매. |
| S-BR-B | [Baby with a Beard - Brotato Wiki](https://brotato.wiki.spellsandguns.com/Baby_with_a_Beard) | 커뮤니티 위키; oldid=6531. | 적 사망 탄환과 사거리 페널티. |
| S-BR-H | [Hunting Trophy - Brotato Wiki](https://brotato.wiki.spellsandguns.com/Hunting_Trophy) | 커뮤니티 위키; oldid=7804. | 치명타 처치 확률 자재 보상, 경험치와 구분. |
| S-BR-E | [Enemies - Brotato Wiki](https://brotato.wiki.spellsandguns.com/Enemies) | 커뮤니티 위키. 지역·DLC·버전별 적 출현은 확정하지 않음. | Spitter 행동, 알 부화, Invoker 단계별 투사체 구역. |
| S-BR-P | [Progress - Brotato Wiki](https://brotato.wiki.spellsandguns.com/Achievements) | 커뮤니티 위키; URL은 Achievements, 표시 제목은 Progress. oldid=8581. | Crazy 승리와 Hunting Trophy 해금 대응표. |
| S-MB-S | [Megabonk on Steam](https://store.steampowered.com/app/3405340/Megabonk/) | 개발자 제공 스토어 설명. 현재 개별 패치와 수치 확인용 아님. | 무작위 지도·경험치·희귀도 업그레이드·퀘스트 해금. |
| S-MB-N | [Steam Community :: Megabonk](https://steamcommunity.com/app/3405340/announcements/) | 개발자 공지 집계. 아래 정확한 글 제목·날짜로 찾는다. **후속 패치에서도 동일하다는 주장은 하지 않음.** | 패치의 변경 설명을 읽었으며 현재 실행 확인과 구별한다. |
| S-SI-A | [How to Survive Survivor!.io - App Store](https://apps.apple.com/us/iphone/story/id1641743438) | Apple 편집 가이드; 개발자 기술 문서 아님. 패치 번호·현재 장비군 미명시. | 무기 방향/표적, 방어 기술, Drone 합체, 진화, 저장된 픽업, Trials/Rogue 조언. |

### S-MB-N의 인용 대상 글

- **Megabonk Spooky Update** — 2025-12-14. Dragon's Breath 자동 표적 변경. R-MB-01.
- **Megabonk Patch #2** — 2025-09-28. 본문에 포함된 이전 v1.0.4 설명과 신규 변경을 함께 확인. 최종 보스 단계·파일런·빙결 구체 및 적 관련 변경. R-MB-04/05. 서로 다른 하위 버전 설명을 한 패치의 신규 기능으로 합치지 않는다.
- **Christmas Patch** — 2025-12-25, v1.0.49. Golden Shield 피격 보상과 자해 상호작용 조정. R-MB-03.
- **H A T S** — 2026-01-25, v1.0.64; 같은 게시물에 후속 수정 설명도 포함. 항아리·화병의 Luck과 이동 유인. R-MB-07.

## 제외·누락과 후속 검증 경계

| 항목 | 상태 | 처리 |
|---|---|---|
| Megabonk 진화·합성 | 실제 열린 공식 자료에서 구체적 조합 확인 못 함 | **시스템 부재라고 쓰지 않는다.** D3 근거에서 제외. |
| Megabonk Blood Magic의 체력 비용 등 | 팬 사이트간 설명 충돌, 다른 위키 접근 실패 | 비용·성장 사실 모두 제외. |
| Survivor.io Mega Devourer 공격 패턴 | 열린 가이드에 세부 패턴 근거 부족 | 이름·무기 대응 조언만 사용; 패턴 설계 근거에서 제외. |
| 근접 부채꼴 각도·판정 | Sweep의 넓은 곡선·수평 공격은 확인했으나 각도/반경 미확인 | 구체적 부채꼴 수치는 자체 설계·검증 과제. |
| 합성 슬롯·비용 일반화 | Survivor.io Drone의 슬롯 반환, Brotato 중복 결합만 확인 | 모든 게임의 진화가 슬롯을 돌려준다고 일반화하지 않음. |
| 명시되지 않은 아이템 대가 | Hunting Trophy 등의 별도 페널티 미확인 | 추가 비용을 발명하지 않음. 획득 비용과 발동 비용을 구별. |
| 실제 재미·현재 최신 패치 일치 | 문헌 조사만 수행 | 재미 해석은 추론, 현재 빌드 행동은 미실행. |


## HoloCure·20 Minutes Till Dawn·Halls of Torment 출처와 한계

아래는 모두 본문을 실제 열어 읽은 페이지다. 표에 이미 요약한 사실을 반복 복제하지 않고 확인 위치·제약을 적는다. 웹 조회는 문서 증거이며 최신 실행 파일을 검증한 것이 아니다.

| 출처 ID | 제목·유형 | 확인 근거 위치 / 주의 |
|---|---|---|
| HC-S | [HoloCure - Save the Fans! 공식 Steam](https://store.steampowered.com/app/2420510/HoloCure__Save_the_Fans/) | About This Game. 상세 일반 적 AI 없음. |
| HC-N | [HoloCure 공식 Steam News](https://steamcommunity.com/app/2420510/allnews/) | `Update 0.7 Released!`의 WEAPONS/FAN BEAM에 수평 전용 제거. 0.6 무기 설명을 현행으로 일반화하지 않는 근거. 개별 announcement URL은 도구가 이미지 한 줄만 반환해 본문이 열린 뉴스 목록을 사용. |
| HC-W | [HoloCure Weapons – Updated For 0.6! / Gamezebo](https://www.gamezebo.com/walkthroughs/holocure-weapons/) | 이름별 Level 목록. 0.6 역사 자료; 계수 및 현재 전체 무기 수를 가져오지 않음. |
| HC-G | [General Guide to HoloCure 0.6 / Steam 사용자 가이드](https://steamcommunity.com/sharedfiles/filedetails/?id=3021610470) | Gameplay Mechanics, Anvils, Items. Super Hammer 드롭 조건 상세는 다른 자료와 서술 차이가 있어 채택하지 않음. |
| HC-A | [HoloCure 100% Achievements / Steam 사용자 가이드](https://steamcommunity.com/sharedfiles/filedetails/?id=3023862734) | Super Collabs의 Jingisukan 설명. 0.7 관련 항목도 섞인 편집 문서라 빌드 번호 미고정. |
| HC-P | [HoloCure's major update… / Rice Digital](https://ricedigital.co.uk/holocure-major-update/) | 보스/스티커/Fandom 단락. 2023년 관찰; 기자의 작품 우열 평가는 사용하지 않음. |
| 20-S | [20 Minutes Till Dawn 공식 Steam](https://store.steampowered.com/app/1966900/20_Minutes_Till_Dawn/) | About This Game, Key Features. PC 방향 조준 명시. |
| 20-K | [All Characters, Items and Synergies v0.6.1 / KosGames](https://kosgames.com/20-minutes-till-dawn-all-characters-items-and-synergies-v0-6-1-24468/) | Summons/Light Weaponry/Magic Lens. 과거 버전 목록으로만 사용. |
| 20-L | [Beginner's Guide / Level Winner](https://www.levelwinner.com/20-minutes-till-dawn-beginners-guide-tips-tricks-strategies-to-survive-till-dawn-and-unlock-everything/) | Talents, Eliminate Bosses. 모바일 UI·자동 조준 기술은 PC 공식 제어와 다르므로 일반화 제외. |
| 20-B | [second boss… / Steam 사용자 토론](https://steamcommunity.com/app/1966900/discussions/0/3374907062173114938/) | 2022-09-09 댓글의 준비 음향과 확정 경로. 낮은 권위의 플레이어 증언; 최신 동작 재현 아님. |
| 20-N | [Blessings & Curses - Coming to Beta Branch Soon / 공식 Steam News](https://steamcommunity.com/app/1966900/allnews/) | 2024-10-28 Enemy overhaul. 출시·베타 실행 결과와 구별. |
| HT-S | [Halls of Torment 공식 Steam](https://store.steampowered.com/app/2218750/Halls_of_Torment/) | About/Features: 웨이브, Lord, 회수, 과제 기반 진행. |
| HT-N | [HoT Experimental Update 2026-10-05 / 공식 Steam Announcements](https://steamcommunity.com/app/2218750/announcements/) | 첫 공지에 experimental과 10월22일 정식 반영 예정 명시. 수치 변경은 수집·적용하지 않음. |
| HT-P | [Ability Tier List / Pro Game Guides](https://progameguides.com/halls-of-torment/halls-of-torment-ability-tier-list-best-abilities-for-each-character/) | 능력별 기하·동작 문장만 사용; 등급·성능 순위 배제. 2023년 자료. |
| HT-U | [Ability Upgrades Guide / Gamezebo](https://www.gamezebo.com/walkthroughs/halls-of-torment-ability-upgrades/) | Ring Blades 숙련 항목. 2023년 자료. |
| HT-G | [Quest/Achievement Guide + Gear Breakdown (7/3/2023) / Steam 사용자 가이드](https://steamcommunity.com/sharedfiles/filedetails/?id=2992231234) | Ability Expert 및 Wellkeeper 항목. 현재 층 배치·총량 주장에 쓰지 않음. |
| HT-I | [Damage Type Modifier Upgrades / Steam 사용자 토론](https://steamcommunity.com/app/2218750/discussions/0/4625853420295715171/) | Thunder Crown 문장; 댓글에 태그 체계 버그와 툴팁 불일치 경고가 있어 실행 계약의 권위로 사용 불가. |
| HT-E | [HELP ME WITH FORGOTTEN VIADUCT / Steam 사용자 토론](https://steamcommunity.com/app/2218750/discussions/0/5514142829700960132/) | 벽 압박은 여러 기록에서 공통; 파괴 가능성은 상충해 미확인으로 보존. |
| HT-B | [How to Beat Lord of Pain / Gamepressure](https://www.gamepressure.com/newsroom/halls-of-torment-how-to-beat-lord-of-pain-first-final-boss-explai/zc5acc) | Phase1/2에 예고·탄·손 패턴. 당시 얼리 액세스 관찰. |

출처 분류: 공식 개발사/스토어는 HC-S·HC-N·20-S·20-N·HT-S·HT-N이다. 매체 편집 가이드는 HC-W·HC-P·20-L·HT-P·HT-U·HT-B이며 공식 자료나 커뮤니티 위키로 분류하지 않는다. KosGames(20-K)는 제3자 가이드 모음으로 원작성·검수 권위가 불명확하다. HC-G·HC-A·20-B·HT-G·HT-I·HT-E는 Steam 사용자 가이드/토론이며 Steam 호스팅 자체가 개발사 검증을 뜻하지 않는다. 이 세 게임(HoloCure·20 Minutes Till Dawn·Halls of Torment)에서는 채택된 커뮤니티 위키 본문이 없다. 앞 절의 Vampire Survivors·Brotato 위키 근거와 구별한다.

접근 실패: holocure.wiki.gg, 20minutestilldawn.wiki.gg는 직접 열기 403; hot.fandom.com은 402 또는 접근 실패. ultimate-browsing 스킬의 Tier1 실행도 의존 모듈 부재로 본문을 얻지 못했다. 이들의 검색 발췌는 본문 증거로 채택하지 않았다. 특정 위키를 열지 못했다는 사실을 메커니즘 부재로 해석하지 않는다.

커버리지: HoloCure 14행, 20 Minutes Till Dawn 12행, Halls of Torment 12행, 총38행. 각 게임에서 8범주를 다루되 HoloCure의 일반 적별 AI는 미확인으로 한계를 명시했다. 새로운 시스템·수치·코드 구현 없음.


- **S-VS-SW** [Santa Water — Vampire Survivors Wiki](https://vampire.survivors.wiki/w/Santa_Water): 커뮤니티 위키 oldid94755, 확인 2026-10-10. 실제 HTML 본문에서 병의 하늘→지면→잔류 피해 순서를 확인. 현재 빌드 재현이나 충돌 순간 별도 피해의 증거는 아니다.

## 조사 범위와 빈칸

아래는 문헌 근거의 범위이며 모든 게임의 모든 범주를 완전히 조사했다는 뜻이 아니다. 미확인은 시스템이 없다는 뜻이 아니다. 번호는 해당 게임의 `R-접두어-번호` 행을 가리킨다.

| 게임(접두어) | 공격 | 성장 | 진화/합성 | 물품 문법 | 적 행동 | 보스 패턴 | 판 구조 | 판 밖 성장 |
|---|---|---|---|---|---|---|---|---|
| Vampire Survivors(VS) | 01–07 | 08 | 09 | 10 무기 효과 대용 | 11 | 12 | 13 모험 | 13 별도 진행 |
| Brotato(BR) | 01–02 | 01–03 | 03 등급 결합 | 04–05 | 06 | 07 | 08 | 09 |
| Megabonk(MB) | 01 표적 변경 | 02 | 미확인 | 03 | 04 패치 설명 | 05 패치 설명 | 06–07 | 06 |
| Survivor.io(SI) | 01–02 | 04 진화 사례 | 03–04 | 05 픽업 | 07 역할만 | 미확인 | 08 지도 예시 | 06 |
| HoloCure(HC) | 01–04 | 05–06 | 07–08 | 09–10 | 11 세계관만 | 12 | 13 | 14 |
| 20 Minutes Till Dawn(20) | 01–03 | 04 | 05 선행 강화 | 06–07 | 08 | 09; 12 발표안 | 10 | 11 |
| Halls of Torment(HT) | 01–04 | 05 | 06 능력 변형 | 07 낮은 확신 | 08 충돌 설명 보존 | 09 | 10 | 11–12 |
| Atomicrops(AC) | 01–02 | 03,06 | 미확인 | 04–05 | 미확인 | 07 존재만 | 07 | 08 |
| Thronefall(TF) | 04 부대 역할 | 02–03 | 미확인 | 미확인 | 05 역할만 | 07–08 | 01 | 06 |
| Kingdom Two Crowns(K2) | 03–04,06 | 01,05,08 | 미확인 | 미확인 | 02 개요 | 07 구조만 | 02,05,08 | 미확인 |
| Dome Keeper(DK) | 04 | 02,04 | 05 선택형 변형 | 06 | 03,07 | 미확인 | 01 | 08 |

D3는 실제 근거가 있는 범주를 조합한다. 빈칸을 원작 사실로 보충하지 않으며, 독자 설계는 제안으로 기록한다. 특히 진화 없는 게임이라고 단정하거나, 적의 명칭·존재만으로 행동 원형을 확인했다고 세지 않는다. R-20-07 같은 수치 상충 사례는 문법 비교용이지 D3의 비수치 물품 수량을 채우는 근거가 아니다.
