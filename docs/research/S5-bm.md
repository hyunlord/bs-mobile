관문: 통과 — S5 조사, 지정 게임 7종, 02의 본절 10개와 §2.1 대응. 공개 수치는 출처·확인일·범위를 붙였고 미확인 값은 채우지 않았다.

# S5 장르·BM 해부 조사

확인일은 **2026-10-08**이며 아래 모든 출처와 수치에 적용한다. 발행일·집계 기간은 별도로 적었다. [이슈 #41](https://github.com/hyunlord/bs-mobile/issues/41), [결정 #42](https://github.com/hyunlord/bs-mobile/issues/42). 원본 [02 BM 골격](../design/02_BM_경제_골격_v0.1.md)은 수정하지 않는다.

## 조사 기준과 결론

웹 공개 자료를 조사했다. 앱 설치·결제·광고 재생·계정별 오퍼·실기 플레이는 수행하지 않았다. **공식**은 게시자·스토어·지원 문서, **추정**은 분석업체 수치, **가이드/리뷰**는 해당 작성자의 관찰, **권고**는 이 프로젝트에 대한 판단이다. 미확인은 기능 부재나 매출이 없다는 뜻이 아니다. 스토어 상품명은 SKU 존재를 보여 주지만 구성·지급량·효력·자동 갱신을 모두 보증하지 않는다. 통화·상품 목록은 확인 가능한 대표 목록이며 기간 이벤트까지 포함한 완전한 카탈로그가 아니다.

- 생존 런과 메타 성장의 결합은 여러 방식이 있다. 무료 진입+자원 성장, 무료 진입+콘텐츠 DLC, 별도 유료 앱을 같은 수익 공식으로 묶지 않는다.
- 경쟁작의 스킨이나 광고 제거라는 이름만 보고 순수 외형·단순 편의로 판정하지 않는다. Kingshot은 스킨 능력치가 공식적으로 확인되는 반례다. bs-mobile의 외형 무스탯 [확정]은 유지한다.
- 공개 다운로드·매출은 규모의 일부를 설명한다. 특정 패스·에너지·광고 횟수가 성공을 만들었다는 인과 증거는 아니다. 정의·기간·지역·OS가 갖춰진 비교 가능한 경쟁작 리텐션은 확보하지 못했다.
- 기본 영웅·영지 각 하나로 시작하고, 힘의 축은 가신 중심으로 먼저 비교하는 안을 권고한다. 장비 배율을 추가하는 결정과 가격·확률·에너지는 승인하지 않았다. [결정 #42](https://github.com/hyunlord/bs-mobile/issues/42)에 남긴다.

## 게임별 구조

### Survivor.io (탕탕 특공대)

대상: Habby 일반 모바일, Android `com.dxx.firenow`, iOS `1528941310`. 공식 [Google Play][S-play]·[미국 App Store][S-apple].

| 항목 | 확인한 구조와 한계 |
| --- | --- |
| 판 안 | 한 손 이동, 다수 적, 로그라이트 스킬 조합과 스테이지 진행. 직접 플레이로 조작·후반 난도를 평가하지 않았다. [공식][S-play] |
| 판 밖 | Survivor·Synergy 레벨, 클랜·이벤트가 공식 업데이트에 명시된다. 가이드에서 장비 레벨·희귀도 합성, Tech Parts, 펫 레벨·진화를 확인했다. 전체 해금 순서는 미확인. [공식][S-play], [장비 가이드][S-guide], [Tech Parts][S-tech], [펫 가이드][S-pets] |
| 통화 | Gems SKU 공식 확인. 가이드 대표 목록: Coins, Energy, 장비 Designs, Supply Keys, Pet Cookie. 재화·입장 자원·강화 재료를 구분하며 현재 교환율·완전 목록은 미확인. [공식][S-apple], [장비][S-guide], [펫][S-pets] |
| 상품 / 힘·외형 | Gems, Chapter Pack, Month Card, Growth Fund, Daily Pack, Piggy Bank SKU. 자원·진척 연계 상품이므로 외형 전용 모델로 분류하지 않는다. 개별 지급량·스탯 기여·스킨 무스탯 여부는 미확인. [공식][S-apple] |
| 광고 | 에너지 카운터에서 보상형 광고를 쓰는 위치가 과거 가이드에 있다. 펫 가이드는 Pet Chest·Pet Peddler의 광고 획득도 설명한다. 부활과 모든 현행 위치·광고 제거 권리는 미확인. [가이드, 2023-01-27][S-energy], [펫 가이드][S-pets] |
| 에너지 / 패스 | 에너지 입장 제약은 과거 가이드 관측. 과거 Survivor Pass 가이드는 일일 과제 XP와 무료/유료 보상 트랙을, 펫 가이드는 별도 Pet Pass를 설명한다. 현행 시즌·보상량·만료/갱신 상세는 미확인. Month Card·Growth Fund와 같은 상품으로 세지 않는다. [과거 패스][S-pass], [펫][S-pets] |
| 불만 | H Steele의 Google Play 리뷰(2024-12-27)는 무기 성장 뒤 반복감을 언급하면서 광고 압박은 적다고 평가했다. 선택 노출된 개인 경험이며 전체 이용자 반응이나 현재 빌드의 사실 판정이 아니다. [리뷰][S-play] |

### Archero 2

대상: Habby, Android `com.xq.archeroii`, iOS `6502820653`. **Archero 전작**의 이용자 수·구독 약관을 이 게임에 옮기지 않는다.

| 항목 | 확인한 구조와 한계 |
| --- | --- |
| 판 안 | 스킬 희귀도·선택, 일반 스테이지와 카운트다운 생존, 보스·탑·골드 던전. [공식][A-apple] |
| 판 밖 | 장비 합성·교환, Rune/Artifact 계열, 캐릭터 스킨과 장비·룬 조정이 업데이트에 나온다. 가이드는 Characters·Talent Cards도 구분한다. 성장 배율·전체 해금 순서는 미확인. [공식][A-apple], [Play][A-play], [가이드][A-guide] |
| 통화 | 보석·상자 자원과 에너지는 App Store 이용자 보고. 에너지 선물 기능은 공식 업데이트에서도 확인. 가이드에는 Gold, Gems, Energy, Gear Scrolls, Guild Coins, Rune Ruins Shovels, 캐릭터 Shards가 명시된다. 재료와 통화를 구분하며 교환율·유료 지갑은 미확인. [공식/리뷰][A-apple], [가이드][A-guide] |
| 상품 / 힘·외형 | Gift Pack 계열 IAP, 랜덤 아이템·광고 표기. 스킨 존재는 공식 확인되나 외형 전용 여부는 미확인. 유료 장비 접근 속도·광고 제거 상품은 리뷰 주장으로만 분류한다. [공식/리뷰][A-apple], [Play][A-play] |
| 광고 | 과거 가이드의 위치는 에너지 보충·Sky Tower 추가 티켓·상점 무료 보너스·Quick Hunt 추가 수령이다. 현행 횟수·광고 제거 보상 대체 계약은 미확인. [가이드][A-guide], [리뷰][A-apple] |
| 에너지 / 패스 | 에너지 존재 확인. 복수 시즌 패스·이벤트 완주 부담은 MG21-(2025-03-21)의 경험이며 커뮤니티 계산기는 Free/Advanced/Premium Battle Pass와 별도 공급·광고 제거·월 카드를 구분한다. 공식 계약이 아니며 현행 보상표·만료 조건은 미확인. [공식/리뷰][A-apple], [계산기][A-pass] |
| 불만 | MG21-은 이벤트 완주 비용과 유료 장비 획득 속도 차이를, Bullfrog(Play, 2026-04-15)는 후반 진척의 결제·일일 접속 의존감을 지적했다. 개발자 개선 의향 답변은 실제 수정 증거가 아니다. [리뷰][A-apple], [Play][A-play] |

### Capybara Go!

대상: Habby, Android `com.habby.capybara`, iOS `6596787726`. [공식 App Store][C-apple]와 [Play][C-play].

| 항목 | 확인한 구조와 한계 |
| --- | --- |
| 판 안 | 텍스트 기반 로그라이트, 무작위 사건·선택·장비·동물 동료. 이동 조작형 액션과 동일한 런으로 분류하지 않는다. [공식][C-apple] |
| 판 밖 | 가이드는 Talents 기본 스탯, 장비 레벨·합성, Pets, Mounts, Artifacts를 구분한다. 현재 전체 모드·해금 순서 미확인. [가이드][C-guide], [Talents][C-talents] |
| 통화 | Gold/Gems/Energy, Power Stones, Pet Eggs/Food, Horseshoes, Spirit Stones, Keys 등. 모드 입장 Realm Keys와 교환 Origin Stones는 별도 재료다. 장착 gem을 결제 Gems와 같은 지갑으로 합치지 않는다. [가이드][C-guide], [던전][C-abyss] |
| 상품 / 힘·외형 | 공식 Package SKU만으로 구성 식별 불가. 가이드는 광고 제거·평생·월 카드, 자원·기회·속도 혜택을 설명하므로 순수 외형 상품으로 분류하지 않는다. 현재 지급량·스탯·무스탯 스킨 여부 미확인. [공식][C-apple], [상품 가이드][C-paid] |
| 광고 | 가이드 관측 위치: Hero’s Supply Crate, 펫 소환, 던전 추가 Realm Key, Goblin Miner 도구·상점 보상. 현행 횟수·노출·권리 미검증. [가이드][C-guide], [펫][C-tips], [던전][C-abyss], [상품][C-paid] |
| 에너지 / 패스 | 챕터 에너지와 자동 회복 구조는 [전용 가이드][C-energy] 근거. 활동별 Talent/Tower/Dungeon/Adventure Fund의 무료/유료 마일스톤 보상과 Battle Pass 관찰이 있다. 진척형 Fund를 시간제 시즌 패스·자동갱신으로 단정하지 않는다. 정확한 현행 트랙·판매 조건 미확인. [상품][C-paid], [외부 관찰][C-pass] |
| 불만 | A B(Play, 2024-11-27)는 진척 정체·일일 리셋 대기, Leslie P(2025-02-19)는 광고·결제 노출과 아레나 시간대 불편을 언급했다. 개발자 조정 의향은 실제 조치 증거가 아니다. [리뷰][C-play] |

### Vampire Survivors — 일반 모바일과 Apple Arcade 구분

대상: poncle 일반 iOS `6444525702`, Android `com.poncle.vampiresurvivors`. Vampire Survivors+는 별도 Apple Arcade 판이다.

| 항목 | 확인한 구조와 한계 |
| --- | --- |
| 판 안 | 생존 중 무기·업그레이드 선택과 콘텐츠 조합. DLC 콘텐츠를 기존 콘텐츠와 섞을 수 있다. [공식][V-play], [DLC FAQ][V-content] |
| 판 밖 / 통화 | 런에서 얻은 gold를 이후 강화에 사용하며 강화 환불은 무료라고 스토어가 설명한다. 현금 충전 gold SKU는 확인 목록에서 발견하지 못했다. 이것은 모든 지역·향후 오퍼의 부재 보장이 아니다. [공식][V-play] |
| 상품 / 힘·외형 | 일반 모바일 무료 진입, Moonspell·Foscari·Ode to Castlevania 등 DLC 판매. 캐릭터·무기·스테이지를 추가하므로 **플레이 콘텐츠 확장**이며 외형 전용이 아니다. 이를 곧바로 필승 판매라고 단정하지 않는다. [공식][V-apple], [DLC FAQ][V-content] |
| 광고 | 출시 보도에 선택형 부활·종료 gold 보너스. 현재 횟수·실기 노출은 미확인. 공식 지원 문서는 DLC 구매 후 보너스 광고 기능을 옵션에서 끌 수 있다고 안내하며, 광고 보상을 무시청 지급한다는 뜻으로 확대하지 않는다. [출시 보도][V-ads], [공식 지원][V-support] |
| 에너지 / 패스 | 일반판의 에너지·배틀패스·현금 전용 통화·외형 판매는 확인 자료로 미확인. VS+는 플랫폼 구독에 포함되고 광고가 없으며 일반판과 저장 체계가 다르다. 협업 DLC 제외를 포함한 권리 차이가 있어 모든 DLC 포함으로 쓰지 않는다. Arcade 구독은 게임 내 패스가 아니다. [공식 FAQ][V-arcade] |
| 불만 | Radityo Prakoso의 Play 후기(2023-06-12)는 DLC 구매 뒤 오프라인 로딩 문제를 호소한 후 편집에서 해결을 알렸다. 현재 미해결 결함으로 인용하지 않는다. 공식 FAQ의 최초 다운로드·업데이트와 오프라인 조건을 구매/복원 QA에 참고한다. [리뷰][V-play], [FAQ][V-offline] |

### Megabonk — 공식 확인 범위는 PC

대상: vedinad [공식 Steam 앱 `3405340`][M-steam]. Windows·SteamOS/Linux 지원을 확인했다. **공식 iOS/Android 출시는 미확인**이다. 동명 앱·팬 사이트·Steam Link를 공식 네이티브 모바일판 근거로 사용하지 않는다.

| 항목 | 확인한 구조와 한계 |
| --- | --- |
| 판 안 | 랜덤 맵, 적 처치 XP, 희귀도 업그레이드와 빌드 조합. [공식][M-steam] |
| 판 밖 / 통화 | 캐릭터·무기·아이템 해금. silver를 해금에 사용한다는 설명은 PC Gamer 필자의 플레이 관찰이며 공식 재화·교환율 명세는 미확인. [공식][M-steam], [플레이 기사][M-play] |
| 상품 / 힘·외형 | PC 본체와 soundtrack 상품 확인. 모바일 힘/외형 IAP 목록은 대상 앱이 확립되지 않아 적용하지 않는다. [공식][M-steam] |
| 광고 / 에너지 / 패스 | 공식 모바일판 미확인으로 모바일 항목은 적용 대상 미확립. PC의 보상 광고·에너지·배틀패스도 확인 자료에서는 미확인. |
| 불만 | Reddit 작성자 ggw1776은 RNG에 따른 런 양극화와 단계 전환 난도 급등을 호소했다. 다른 댓글에는 빌드 선택 탓이라는 반론도 있다. 이는 PC 이용자 의견이며 모바일 과금 불만이나 통계가 아니다. [토론][M-complaint] |

### Brotato — 무료 모바일 / Premium / PC를 분리

모바일 게시자 Erabit. 무료 Android `com.brotato.shooting.survivors.action.roguelike`·iOS `6445884925`, Premium Android `com.brotato.shooting.survivors.games.paid.android`·iOS `1668755109`. PC는 Blobfish의 [별도 Steam판][B-steam].

| 항목 | 무료 모바일 | Premium 모바일 |
| --- | --- | --- |
| 판 안 | 재료 수집→XP→웨이브 사이 상점→빌드. [공식][B-free-play] | 같은 웨이브·상점 구조를 공식 설명. 업데이트·버그·밸런스가 PC와 같다는 보장은 아니다. [공식][B-paid-apple] |
| 판 밖 | 버전 이력에 영웅 강화, Chip System, 일/주간 과제, Hero Fund가 등장. [공식][B-free-apple] | 캐릭터·아이템 선택 및 완료 도전/진척 항목 소개. 해금 조건과 무료판식 영구 수치 성장의 현행 존재·범위는 실기 미확인. [공식][B-paid-apple] |
| 통화 | 런 재료와 판매 SKU `spuds`. 추가 강화 재화의 정확한 소스/싱크는 미확인. [공식][B-free-apple] | 런 재료 확인. 현금 소모 통화는 미확인. [공식][B-paid-apple] |
| 상품 / 힘·외형 | `permanent_vip`, `month_vip`, `No ads`, spuds, Beast Master, DLC SKU. 유료 캐릭터·강화 구조가 있어 외형 전용 모델로 분류 불가. 상품별 스탯·권리는 미확인. [공식][B-free-apple] | 유료 본체+Abyssal Terrors DLC. 현재 IAP가 있으므로 “Premium은 IAP 없음”이라는 과거 요약을 쓰지 않는다. 콘텐츠 확장과 외형은 다르다. [공식][B-paid-apple] |
| 광고 | Play 광고/IAP 표기, iOS 이력에 보상형 광고 일일 제한·광고 제거 팩. 현행 위치별 보상·부활·상점 재굴림 조건은 미확인. [공식][B-free-play], [이력][B-free-apple] | Play에는 IAP/Play Pass 표시, Contains ads 표시 없음. 실기에서 광고 부재를 검증한 것은 아니다. [공식][B-paid-play] |
| 에너지 / 패스 | 에너지·배틀패스 미확인. 월 VIP가 자동 갱신인지 상품명만으로 판정하지 않는다. | 에너지·게임 내 패스 미확인. Play Pass는 플랫폼 구독이며 무료판 VIP와 다른 권리. |
| 불만 | Sid Kelly(Play, 2026-04-29)는 토템 강화의 과금 장벽을, Ben48261(iOS, 2024-04-11)는 난도 변동·결제 압박을 느꼈다고 썼다. [리뷰][B-free-play], [iOS][B-free-apple] | Nicholas Lance(Play, 2026-06-27)는 설명/효과 불일치와 PC 차이를 주장했다. 빌드 재현 전에는 사실 확정 불가. 무료판 과금 불만과 합치지 않는다. [리뷰][B-paid-play] |

### Kingshot — 영지 메타 참고, 생존 런 동형 모델 아님

게시자는 **Century Games**이며 Habby가 아니다. Android `com.run.tower.defense`, iOS `6739554056`. [공식 게임 소개][K-official].

| 항목 | 확인한 구조와 한계 |
| --- | --- |
| 판 안에 대응하는 활동 | 주민 구조·식량/주거 제공·병사 훈련·영웅·자원 확보와 방어. 동맹/PvP가 결합된 지속형 전략 구조다. bs-mobile처럼 독립 생존 판 뒤 정산하는 구조로 간주하지 않는다. [공식][K-official] |
| 판 밖 | 도시·기술·영웅·병력·동맹 성장. 전체 메타가 계속 유지되는 구조의 참고이며 군사 경쟁을 출시 PvP 권고로 가져오지 않는다. [공식][K-play] |
| 통화 | Gold, Gems, Skin Tokens, VIP Points와 공식 외부 상점 Kingdom Stars를 확인. Gold는 주민·Rebel Suppression 방치 보상, Skin Tokens는 일일 미션·이벤트 출처를 안내한다. Stars와 Gems를 같은 지갑으로 합치지 않는다. [Gold FAQ][K-gold], [토큰 FAQ][K-token], [VIP FAQ][K-vip], [상점 공지][K-stars] |
| 상품 / 힘·외형 | Weekly/Monthly Card, Ultra Value Monthly Card, Resource Pack, Normal Pack·Daily Worship·Daily Deal SKU 확인. 실제 구성/가격 대비 전투력 미측정. **아바타 프레임·행군·성 스킨의 능력치가 미장착 상태에서도 누적되고 상한이 있다.** 모든 스킨이 유료라는 뜻은 아니지만 외형 전용 사례로 사용할 수 없다. [기간 카드 FAQ][K-card], [App Store][K-apple], [스킨 FAQ][K-skin] |
| 광고 | 보상형/전면 광고의 실제 지점과 광고 제거 권리는 미확인. 스토어 개인정보 항목으로 노출을 추정하지 않는다. |
| 에너지 / 패스 | 공식 FAQ에 Energy 회복과 Captain 혜택, 별도 Stamina 비소모 활동 설명이 있다. 두 용어의 동일성·모든 활동의 입장 제한은 미확인. 시즌 패스 보상·트랙은 미확인. Transfer Pass는 왕국 이전 아이템이므로 배틀패스로 분류하지 않는다. [Energy FAQ][K-energy], [Stamina FAQ][K-stamina] |
| 불만 | Tyler Galbraith(Play, 2026-04-14)는 광고와 실제 경험의 차이·PvP 결제 압박을, Hector Cornejo(2025-05-12)는 타워디펜스 이미지와 길드 경쟁의 차이를 지적했다. 선택 노출 리뷰이며 불만 빈도나 이탈 원인 통계가 아니다. [리뷰][K-play] |

### 가이드의 시점 한계

Survivor 장비 가이드는 제목상 2023 범위, Survivor Pass는 2022-12-26/2023-05-10 표기의 과거 구조, 펫 가이드는 2023-04-27/2026-09-05 표기지만 초기 펫 설명을 포함한다. Archero 2 가이드는 2025-06-19 갱신, 패스 계산기는 2026-05-28 게시/2026-06-23 갱신이다. Capybara 초보 가이드는 2025-02-07 편집, 펫 가이드는 2025-02-23 게시이며 상품 가이드는 2025-01-23, 에너지 문서는 2025-07-27 편집이며 던전·패스 관찰 일부의 정확한 수정일은 미확인이다. **수정일만으로 현재 앱 전체 명세라고 인정하지 않는다.** 위 가이드의 이름·구조는 공개 관찰이며 2026-10-08 실기 재현 결과가 아니다.

## 공개 매출·다운로드·리텐션

**표의 확인일은 모두 2026-10-08.** Google Play 배지는 해당 Android 패키지의 누적 다운로드 **하한 구간**이다. 표시 언어·열람 국가가 국가별 집계 범위를 뜻하지 않는다. 정확한 집계 종료 시점·재설치 처리·고유 이용자 수는 이 페이지로 알 수 없다. iOS·PC를 합치거나 현재 정가를 곱해 매출로 만들지 않는다.

| 게임·판본 | 공개 다운로드 | 공개 매출 | 리텐션 |
| --- | --- | --- | --- |
| Survivor.io | Play **50M+** 누적 구간. [공식][S-play] | **2022-10-21** Mobilegamer.biz 보도: 출시 이후 IAP 개발사 수취액 **$75M 초과**, 다운로드 약 **37M**. AppMagic 추정, 중국 포함 App Store+Google Play, 스토어 수수료·광고 제외. 현행 매출 아님. [과거 보도][S-revenue] | D1/D7/D30 코호트·분모·지역·OS 미확인 |
| Archero 2 | Play **5M+** 누적 구간. 전작 이용자 숫자 제외. [공식][A-play] | **2025-02-06** PocketGamer.biz 보도: **2025-01-07 세계 출시 후 첫 30일 $32.8M**, 소프트런치 포함 누적 **$65.3M** gross player spending. AppMagic 추정, 세계 App Store+Google Play, 광고 제외. 두 값을 더하지 않음. [과거 보도][A-revenue] | 미확인 |
| Vampire Survivors 일반 | Play **5M+**. 별도로 Mobilegamer.biz **2023-03-14** 보도는 AppMagic의 출시 후 모바일 누적 **5.1M** 추정(Android **2.7M**, iOS **2.4M**)을 인용. 기사 집계의 스토어/제외 국가 전체 명세 미확인; 현재 값 아님. [공식][V-play], [과거 추정 보도][V-downloads] | 광고/DLC/플랫폼별 현행 매출 미확인 | 미확인 |
| Megabonk | 공식 모바일 대상 미확립. PC 판매·동접은 모바일 다운로드에 사용하지 않음. [공식 확인판][M-steam] | 모바일 매출 적용 대상 미확립; PC 매출도 여기서 확정하지 않음 | 모바일 미확인 |
| Brotato 무료 모바일 | Play **10M+** 해당 무료 앱 누적 구간. [공식][B-free-play] | 현행 광고/IAP 매출 미확인 | 미확인 |
| Brotato Premium 모바일 | Play **1M+** 해당 유료 앱 누적 구간. Play Pass 이용 비중 분리 불가. [공식][B-paid-play] | 본체/DLC 매출 미확인; 설치×정가 계산 금지 | 미확인 |
| Capybara Go! | Play **10M+** 누적 구간. [공식][C-play] | PocketGamer.biz **2025-02-12** 보도가 AppMagic 추정으로 **2025-02-10까지** 세계 누적 gross player spending 약 **$109M**을 인용. 광고 제외, 기사에서 플랫폼 전체 포함 범위 미확인. 순매출·현행 월매출 아님. [보도][C-revenue] | 미확인 |
| Kingshot | Play **50M+** 누적 구간. [공식][K-play] | Sensor Tower **2026-02** 공개 글: **2025-02 출시 후 10개월** 세계 매출 **$750M 초과**. 분석업체 보고/추정이며 해당 문단에 gross/net·세금/수수료·플랫폼/외부결제 포함 범위 미기재. 개발사 회계 매출 아님. [원 발행사][K-revenue] | 미확인 |

작은 국가·짧은 주간의 자동 생성 시장 글과 절대 기간이 없는 “Last Month” 대시보드는 세계 사업성 비교에서 제외했다. Survivor의 과거 보도에는 재인용된 리텐션 수치도 있지만 코호트·지역·OS 정의가 불충분해 비교표에서 채택하지 않았다. 공개 주장 부재라는 뜻은 아니다. 다운로드 변화·활성이용자 변화·리뷰·별점은 리텐션 대체값이 아니다. 표본이 알려지지 않은 현행 매출을 과거 기사에서 연장 추정하지 않았다.

## 02 각 절에 대한 수정 제안 — 원본 미변경

아래는 **권고·실험 가설**이며 채택된 가격표나 구현 계약이 아니다. 근거가 있는 사례와 프로젝트 선택을 분리한다. 기본 영웅·영지 각 하나, 외형 무스탯, 자율 성장, 출시 PvP 미도입 방향을 보존한다.

| 원본 절 | 제안 | 근거·확인할 것 |
| --- | --- | --- |
| §1 판매 원칙 | 상품마다 `힘 / 선택 폭 / 편의 / 외형`을 구분하고 영구·기간 권리 및 무료 대체 경로를 적는다. 스킨 무스탯은 유지. 확률 표기 데이터 요구는 원본대로 별도 계약으로 관리한다. | Kingshot 스킨 능력치 반례, VS의 콘텐츠 DLC, Brotato 판본 차이. 명칭만으로 힘이 없다고 분류하지 않는다. 경쟁작 사례가 국내 규정 준수 여부를 대신하지 않는다. |
| §2 수익 축 후보 | 가신 성장 축을 우선 후보로 하고 추가 장비 배율은 보류한다. 외형·패스·광고 제거는 무엇을 추가 지급하는지 먼저 명세한다. 외형만으로 수익 충분하다는 가정도 하지 않는다. | Habby류 성장 층 중첩과 리뷰의 진척 압박은 검증할 위험 신호다. 경쟁작 성장 층 수를 성공의 원인으로 복제하지 않는다. |
| §2.1 기본 영웅·영지 | 초기 영웅/영지 판매를 되살리지 않는다. 영지 테마는 역할 ID→그림 계약으로만 바꾸고 수치 무변경 검사를 유지한다. 후속 영웅/영지 판매 일정은 미결정으로 남긴다. | Kingshot의 성 스킨 모델과 우리 외형 계약은 다르다. 추가 콘텐츠 판매의 선택 폭 격차도 따로 평가한다. |
| §3 통화 | 기존 금화·보석·자재 후보에서 시작해 각 지갑의 출처/소비처/귀속·환불·만료 여부를 기록한다. 경쟁작 이벤트 재화를 그대로 추가하지 않는다. | Kingshot Stars/Gems, Brotato 무료 재화와 런 재료처럼 이름·판본·지갑을 혼합하면 경제가 틀어진다. 실제 공급/소비 곡선 전 숫자 미확정. |
| §4 판 밖 성장 | 자율 장원을 중심으로 해금 도전·연대기를 연결한다. 가신 성장과 영구 장원 효과가 같은 전투 배율을 중복 제공하는지 검사한다. 출시 PvP를 참고작 때문에 추가하지 않는다. | Kingshot 지속 전략과 독립 생존 런의 차이, VS 해금/강화 구조. 매일 접속 이유와 결제 없이는 진행 불가한 상태를 구분한다. |
| §5 주입 지점 | 시작 효과·가신·전역 배율 각각을 로그에 남기고 중립 메타/광고 미사용 기준선과 분리한다. 판 안 카드 선택이 메타에 압도되는지를 챕터별로 측정한다. | [S4](../league/S4-report.md)는 중립 메타에서도 정책 지배가 나왔다. 수익 구조를 얹기 전에 이동·빌드 교란을 통제해야 한다. 경쟁작 매출은 적정 배율 근거가 아니다. |
| §6 광고 지점 | 원본의 선택형 후보를 유지하되 위치마다 보상, 거절 결과, 실패 재시도, 광고 제거 구매자의 동일 보상 권리를 명시한다. 첫 실험은 종료 보상처럼 런을 끊지 않는 위치를 우선 검토한다. 전면광고는 초기 미사용 권고 유지. | VS 부활/결과 광고는 사례이지 최적 횟수 증거가 아니다. 광고 비활성화와 무시청 보상 지급은 다르다. 판 중 리롤은 흐름·밸런스 교란을 별도 검사한다. |
| §7 획득·소비 | 하루/주 단위 잔액과 도달 시간을 무과금·광고 선택·유료 권리 시나리오로 분리한다. 필수 재료가 막히면 대체 획득 행동이 있는지 확인한다. 런 점수는 음식/자재 축적량으로 바꾸지 않는다. | 원본 과제와 S4 점수 계약. 팩 가격이나 설치 수에서 적정 인플레이션·승급 비용을 역산하지 않는다. |
| §8 에너지 | 무제한 플레이와 보상 한도 안을 먼저 시제품에서 비교하되 승인된 한도·타이머는 아직 없다. 보상 종료 뒤에도 어떤 진척·기록이 남는지 명시한다. | Habby 에너지와 유료/Premium 사례는 서로 다르다. 리뷰 불만만으로 에너지가 항상 나쁘거나 없어야 성공한다고 결론 내리지 않는다. |
| §9 지표 | 아래 정의표처럼 분모·기간·국가·OS·유입원·광고 여부를 고정한다. 원문의 외부 기준 범위는 출처 미확인 가설로 격리하며 통과선으로 쓰지 않는다. | 동일 범위로 검증 가능한 경쟁작 리텐션을 확보하지 못했고 전장르 벤치마크는 해당 코호트와 다르다. 표본·관측 종료를 확보한 소프트런치에서 정한다. |
| §10 다음 단계 전 채울 것 | 경제 곡선, 챕터 벽, 힘 축 선택, 통화 목록, 패스 권리표를 각각 검증 가능한 산출물로 만든다. 패스는 무료/유료 트랙·진척 획득·시작/종료·중도 구매·누락 보상·만료·환불을 명세한다. | 현행 경쟁작 패스 상세 미확인을 가정으로 채우지 않는다. [결정 #42](https://github.com/hyunlord/bs-mobile/issues/42)와 기존 [#32](https://github.com/hyunlord/bs-mobile/issues/32)에 선택을 남기고 SDK 도입 전 별도 검토한다. |

### §9 외부 기준의 증거 상태

원문의 **D7 15–22%**, **보상형 광고 DAU당 4–7회**, 액션·전략의 IAP 우세를 보편 기준으로 입증하는 일치된 출처는 **미확인**이다. 위 숫자는 원본 문구를 식별하기 위한 인용이며 관측 시장 수치가 아니다. GameAnalytics의 [2025 보고서][GA-report]는 통합된 게임 표본의 장르·지역별 집계로, 우리 하이브리드 코호트의 목표를 직접 제공하지 않는다. [Unity 광고 배치 가이드][U-ads]는 노출/DAU·참여·빈도를 비교하도록 안내하지만 해당 횟수 범위를 보편 최적으로 보장하지 않는다. 원본은 그대로 두고 이 보고서에서 근거 수준을 낮춘다.

| 내부 측정 정의 제안 | 분모·구분 |
| --- | --- |
| D1/D7/D30 | 설치 코호트 중 해당 경과일에 돌아온 이용자. 달력일/경과시간·시간대·재설치·기기/계정 병합 규칙을 고정하고 관측일이 아직 안 된 코호트는 제외. rolling retention과 혼용 금지. [정의 참고][GA-metrics] |
| ARPDAU | 해당 날짜의 정의된 수익 / 그 날짜의 활성 이용자. IAP·IAA, gross/net·세금·수수료·통화를 분리. 누적 ARPU·유료 이용자 ARPPU와 혼용 금지. [정의 참고][AF-arpdau] |
| 결제 전환·ARPPU | 같은 기간의 결제 이용자/대상 이용자, 수익/결제 이용자를 각각 기록. 설치 코호트와 당일 활성 분모를 표에 명시. 0분모는 미측정. |
| 광고 | 제안→수락→로드→표시→완료→보상 지급을 구분. 완료/DAU와 노출/DAU, 참여자당 시청 수를 분리하고 로드 실패·보상 중복도 기록. [배치 지표 참고][U-ads] |
| 판·챕터·빌드 | 진입/승리/사망/중단/크래시를 구분하고 메타 스냅샷·광고 사용·사람 규칙·빌드와 연결. 플레이 시간과 일일 판 수를 보상 한도 도달 전후로 나눈다. |

이 표는 계측 제안이며 구현 완료 보고가 아니다. 운영 분석·광고·결제 SDK를 설치하지 않았다. 공개 자료만으로 UA 비용·ROAS·과금 성공을 예측하지 않는다.

## 출처 목록

모든 출처 확인일 **2026-10-08**. 아래 링크는 주장 가까이에서 참조한다. 동적 스토어 리뷰·상품·배지는 이후 바뀔 수 있다. 공식 기능 설명, 외부 추정, 개인 의견을 서로 대체하지 않았다.

[S-play]: https://play.google.com/store/apps/details?hl=en_US&id=com.dxx.firenow
[S-apple]: https://apps.apple.com/us/app/survivor-io/id1528941310
[S-energy]: https://gamesadda.in/gaming/survivor-io-get-energy/
[A-play]: https://play.google.com/store/apps/details?hl=en&id=com.xq.archeroii
[A-apple]: https://apps.apple.com/us/app/archero-2/id6502820653
[V-play]: https://play.google.com/store/apps/details?id=com.poncle.vampiresurvivors&hl=en_US
[V-apple]: https://apps.apple.com/us/app/vampire-survivors/id6444525702
[V-content]: https://poncle.games/ode-to-castlevania-faq
[V-support]: https://poncle.zendesk.com/hc/en-gb/articles/14803391394705-How-do-I-buy-DLC-on-mobile
[V-ads]: https://www.pcgamer.com/vampire-survivors-gets-surprise-mobile-release-and-its-free/
[V-arcade]: https://poncle.games/arcade-faq
[V-offline]: https://poncle.games/dlc-faq
[V-downloads]: https://mobilegamer.biz/vampire-survivors-mobile-hits-5m-downloads/
[M-steam]: https://store.steampowered.com/app/3405340/Megabonk/
[M-play]: https://www.pcgamer.com/games/roguelike/a-slot-machine-for-gags-disguised-as-a-vampire-survivors-like-is-taking-over-steam-and-im-just-as-addicted-to-pulling-the-lever-as-the-other-90k-people-playing-it/
[M-complaint]: https://www.reddit.com/r/MegabonkOfficial/comments/1ntrbu5/rng_and_balance_are_unreasonably_brutal/
[B-free-play]: https://play.google.com/store/apps/details?id=com.brotato.shooting.survivors.action.roguelike
[B-free-apple]: https://apps.apple.com/us/app/brotato/id6445884925
[B-paid-play]: https://play.google.com/store/apps/details?hl=en-US&id=com.brotato.shooting.survivors.games.paid.android
[B-paid-apple]: https://apps.apple.com/us/app/brotato-premium/id1668755109
[B-steam]: https://store.steampowered.com/app/1942280/Brotato/
[C-play]: https://play.google.com/store/apps/details?hl=en&id=com.habby.capybara
[C-revenue]: https://www.pocketgamer.biz/habbys-capybara-go-surpasses-100m-in-gross-player-spending/
[K-official]: https://www.centurygames.com/games/kingshot/
[K-play]: https://play.google.com/store/apps/details?hl=en-US&id=com.run.tower.defense
[K-apple]: https://apps.apple.com/us/app/kingshot/id6739554056
[K-gold]: https://centurygames.helpshift.com/hc/en/140-kingshot/faq/9012-how-to-obtain-more-gold/?l=en
[K-token]: https://centurygames.helpshift.com/hc/en/140-kingshot/faq/9126-how-to-acquire-skin-tokens-1783436152/?l=en
[K-vip]: https://centurygames.helpshift.com/hc/en/140-kingshot/faq/9240-what-s-included-in-prestige-benefits/?l=en
[K-stars]: https://www.centurygames.com/kingshot-kingdom-stars-pack/
[K-card]: https://centurygames.helpshift.com/hc/en/140-kingshot/faq/9013-how-to-use-the-ultra-value-monthly-card-voucher/?l=en
[K-skin]: https://centurygames.helpshift.com/hc/en/140-kingshot/faq/9018-i-have-multiple-avatar-frames-marching-castle-skins-how-do-the-stat-bonuses-add-up/?gv=frVenez&p=winpc
[K-energy]: https://centurygames.helpshift.com/hc/en/140-kingshot/faq/8527-how-to-acquire-energy/
[K-stamina]: https://centurygames.helpshift.com/hc/en/140-kingshot/faq/8769-what-rewards-are-granted-for-killing-dragonborn-escorts-does-this-spend-stamina/
[K-revenue]: https://sensortower.com/blog/sensor-tower-apac-awards-2025
[GA-report]: https://www.gameanalytics.com/reports/2025-mobile-gaming-benchmarks
[GA-metrics]: https://docs.gameanalytics.com/events-metrics-and-filtering/metrics/
[U-ads]: https://unity.com/blog/3-kpis-to-watch-when-a-b-testing-your-ad-placement-strategy
[AF-arpdau]: https://www.appsflyer.com/glossary/arpdau/

[S-guide]: https://www.talkandroid.com/24372-survivor-io-ultimate-game-guide-with-tips-2023/
[S-pets]: https://mturbogamer.com/2023/04/survivor-io-pets-guide-how-to-get-level-up-evolve-revive/
[S-pass]: https://mturbogamer.com/2022/12/survivor-io-survivor-pass-guide/
[S-revenue]: https://mobilegamer.biz/two-months-in-survivor-io-passes-75m-from-37m-downloads/
[A-guide]: https://progameguides.com/archero-2/archero-2-beginners-guide/
[A-pass]: https://archero2hub.com/en/calculator/gem-income
[A-revenue]: https://www.pocketgamer.biz/archero-2-makes-328m-in-first-30-days-from-player-spending/
[C-apple]: https://apps.apple.com/us/app/capybara-go/id6596787726
[C-guide]: https://capybara-go.game-vault.net/wiki/Guide:Ultimate_Beginners_Guide
[C-talents]: https://www.pocketgamer.com/capybara-go/guide/
[C-abyss]: https://capybara-go.game-vault.net/wiki/Guide:Abyssal_Realm_And_Dungeon_Guide
[C-paid]: https://capybara-go.game-vault.net/wiki/Guide:Real_Money_Spending_P2W_Guide
[C-pass]: https://appservatory.com/mechanics/season-pass/
[C-tips]: https://theriagames.com/guide/capybara-go-tips-and-tricks/

[S-tech]: https://gamesfuze.com/guides/survivor-io-tech-parts-guide/

[C-energy]: https://capybara-go.game-vault.net/wiki/Energy
