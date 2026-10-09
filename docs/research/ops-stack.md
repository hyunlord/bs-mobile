# 모바일 F2P 운영 스택 조사

관문: 부분 — 요구 대상의 공식 문서 조사 완료. 도입 결정·실제 SDK 호환/용량 검증은 미실시이며 SDK·서버를 설치하지 않았다.

- 추적: [P3 #116](https://github.com/hyunlord/bs-mobile/issues/116), [2단계 A #112](https://github.com/hyunlord/bs-mobile/issues/112).
- 확인일: **2026-10-09**. 아래 모든 외부 출처는 이날 확인했다. 가격은 USD 공개 표기이며 세금·환율·계약 할인·스토어 수수료를 포함한 견적이 아니다.
- `[공식]`은 공개 문서의 사실, `[권고]`는 이 프로젝트의 선택 제안, `[가설]`은 계산용 부하, `미확인`은 근거 또는 실측이 없는 항목이다. 가격 페이지가 달라지면 재산정한다.
- 고정 에디터는 `6000.6.4f1`. “Unity 지원”·“Unity 6 지원”·최소 버전 충족을 이 패치의 Android/iOS IL2CPP 검증으로 바꾸어 쓰지 않는다. 모든 후보의 이 프로젝트 실기 검증과 APK/IPA 증가량은 **미확인**이다.

## 결론: 승인 후 붙일 조합

**[권고 A] AdMob 중개 + Firebase Analytics/Remote Config/A/B/Crashlytics + Unity IAP + Firebase의 서버 검증/거래 원장.** 유료 유입을 시작할 때만 AppsFlyer를 추가 후보로 둔다. 초기에는 광고 네트워크를 최소화하고, 분석 이벤트를 장원·챕터·결산의 이탈 원인에 한정한다. Firebase와 광고의 통합 경로, 서버 검증에 필요한 저장·함수·분석의 연결을 활용하는 선택이다. 성능 우위나 최고 수익을 실측한 결론은 아니다. Firebase Remote Config를 무제한 무료로 전제하지 않는다.

**[대안 B] Unity LevelPlay + Unity Analytics/Remote Config/A/B + Unity IAP + UGS Cloud Code/Cloud Save/Economy, 크래시는 Crashlytics, 유료 유입은 Adjust.** Unity 문서·대시보드 중심 운영을 우선할 때의 대안이다. 단일 회사로 모두 통합되는 조합은 아니며 분석·경제·인증 데이터의 UGS 의존도가 높아진다. MAX는 광고 수익 비교 실험 후보로 남기되 중개 SDK를 둘 이상 동시에 주 중개자로 설치하지 않는다.

현재 P2 로컬 저장/결정론 Core에 어느 조합도 연결하지 않는다. 원격 설정은 다음 판 시작 전에 검증한 설정 스냅샷으로 고정하고 설정 버전·해시를 리플레이와 함께 보관하는 경계를 권고한다. 판 도중 원격 수치 변경은 역사 해시 재현을 깨뜨린다. 지급은 결산 재고 상한을 우회해서는 안 된다.

## 광고 중개

| 후보 | 작은 서비스 / 성장 시 비용 구조 | Unity·IL2CPP·충돌 검토 | 데이터 이전·종속·개인정보 |
|---|---|---|---|
| AppLovin MAX | [공식 상품 FAQ](https://applovin.com/en/monetization)는 퍼블리셔 중개 무료, 입찰 네트워크가 발생 광고 수익에 대한 수수료를 낸다고 설명한다. 개별 비율·정산 조건은 미확인. 성장 비용은 네트워크별 정산·운영 인력도 포함해 비교한다. | [공식 통합 문서](https://support.applovin.com/en/max/unity/overview/integration): Unity 2019.4 이상, Android Jetifier, iOS CocoaPods, 메인 스레드 API. 네이티브 네트워크 어댑터마다 버전 조합이 늘어난다. 6.6 패치 인증/실측 미확인. | 동의 값을 중개 파트너에 전달해야 한다는 공식 지침이 있다. 원시 노출 수익 export의 계약별 범위·보존 기간·종료 후 다운로드는 미확인. waterfall·ad unit·네트워크 설정과 수익 이력은 교체 비용이다. |
| Unity LevelPlay | [UGS 가격](https://unity.com/products/gaming-services/pricing)은 광고 monetization을 revenue share로 표기한다. LevelPlay 개별 계약 비율은 미확인. 성장 시 네트워크 조합·Ad Quality 등 추가 계약 확인 필요. | [공식 시작 문서](https://docs.unity.com/en-us/grow/levelplay/sdk/unity/get-started): Unity 패키지와 각 네트워크 SDK/어댑터를 별도 통합하고 integration test suite로 검증한다. 6000.6.4f1 + 선택 어댑터 전체 IL2CPP 검증은 미확인. | 플랫폼 설정 CSV/API 경로가 있으나 플레이어 원시 데이터의 완전한 이전을 보장하지 않는다. S2S 보상에는 사용자 식별자 설계가 필요하다. Unity 소유라는 이유로 ATT·국외 이전 책임이 없어지지 않는다. |
| AdMob 중개 | 정확한 중개 계약 비용·정산 공제율은 미확인. 광고 매출을 고정 eCPM으로 예측하지 않는다. 유료 UA 구매비와 광고를 보여 주는 수익은 별도 회계다. | [공식 Unity 통합](https://developers.google.com/admob/unity/quick-start): Google Mobile Ads Unity plugin과 네이티브 의존성, 초기화 필요. Firebase와 함께 EDM4U/Google 네이티브 라이브러리 버전 정합성 확인. 6.6 실측 미확인. | Google 계정·ad unit·중개 그룹 의존. 광고 보고서와 게임 이벤트는 독립 ID/통화/시간대 계약으로 조인하도록 권고. 타 중개로 옮길 때 과거 보고서 원본 export 범위는 계약 확인 필요. |

세 후보 모두 용량 증가를 “몇 MB”로 단정할 수 없다. ABI, stripping, 선택 네트워크 수, 네이티브 라이브러리와 리소스가 결과를 바꾼다. **[권고]** 승인 후 동일 Release 기준에서 SDK 없는 기준 빌드와 후보 하나의 압축 다운로드/설치 크기·기동 시간·메모리·프레임 p95를 비교한다. 광고 초기화/콜백은 Core 밖에서 처리하고, 보상 콜백 중복·취소·백그라운드 복귀를 검증한다.

## 분석·원격 설정·A/B·크래시

| 서비스 | 작은 서비스 / 성장 비용 | 이전·종속 및 기능 판단 |
|---|---|---|
| Firebase Analytics | [공식 가격](https://firebase.google.com/pricing): Analytics/A/B Testing/Crashlytics는 no-cost. BigQuery 저장·쿼리·내보내기는 별도 조건/비용. | [공식 BigQuery export](https://firebase.google.com/docs/projects/bigquery-export) 경로가 있다. 콘솔 통계만 정본으로 삼지 말고 이벤트 명세·export 스키마를 자체 유지. Google 프로젝트·GA 이벤트/사용자 속성·실험 연결에 종속된다. |
| Firebase Remote Config | [같은 가격표](https://firebase.google.com/pricing): 2026-09-01부터 프로젝트당 일 100,000 fetch까지 무료, 100,001~10,000,000 구간은 요청당 $0.000006, 이후 $0.000001. Spark는 무료 한도 내 사용. | 캐시/오프라인 기본값·fetch 빈도를 비용 계약에 포함. A/B 기능 자체가 무료여도 fetch와 warehouse가 무료라는 뜻은 아니다. 실험 할당·설정 JSON·승인 이력을 자체 보관하도록 권고. |
| GameAnalytics | [공식 가격](https://www.gameanalytics.com/pricing): 무료 상품과 유료 상품을 구분한다. PipelineIQ는 월 $499부터 표기되며 선택 상품·MAU·export 견적을 확인해야 한다. 성장 시 무료 대시보드만으로 원시 데이터 이전을 가정하지 않는다. | [공식 Data Export](https://docs.gameanalytics.com/products-and-features/pipeline-iq/data-export/overview-and-use-cases/)는 수집 이벤트를 외부 클라우드 저장소로 보내는 기능. SegmentIQ의 remote config/A/B 제공과 계약 권한 확인 필요. 게임 전용 이벤트 모델은 빠른 초기 분석에 유리하다는 판단이지만 가격/필드 제약은 교체 비용이다. |
| Unity Analytics | [공식 가격](https://unity.com/products/gaming-services/pricing): 월 50,000 MAU, MAU당 custom event 500개·query 0.05초의 무료 범위. 다음 구간 MAU당 $0.00360부터 단계별 요율. 이벤트/쿼리 초과와 Data Access 별도 계약은 미확인. | [공식 FAQ](https://docs.unity.com/en-us/analytics/faq): Data Access/Snowflake 경로, EU 저장과 다른 지역 복제 설명. “EU만 저장”으로 요약하면 안 된다. 이벤트 정의·audience·UGS 환경 ID가 이전 비용이다. |
| Unity Remote Config/A/B | [공식 가격](https://unity.com/products/gaming-services/pricing)은 무료 표기. 필요한 Analytics/Cloud Code 등 다른 서비스 비용은 따로 산정한다. | [공식 개요](https://docs.unity.com/en-us/remote-config)는 앱 업데이트 없이 설정을 조정하는 서비스. 환경별 설정·기본값·실험 bucket을 자체 manifest로 기록하고 판 시작 시 동결하도록 권고. |
| Crashlytics | Firebase 가격상 no-cost. 네이티브 크래시 재현·심볼 보관·보안 검토 인력 비용은 별도. | [Unity 공식 가이드](https://firebase.google.com/docs/crashlytics/unity/get-started)는 Android IL2CPP symbol upload를 명시한다. 실제 심볼 해석되는 강제 크래시를 출시 후보에서 증명해야 한다. stack/log에 사용자 입력·토큰을 보내지 않는다. |
| Unity Cloud Diagnostics | [공식 UGS 가격](https://unity.com/products/gaming-services/pricing)은 Personal/Pro/Enterprise에 포함된다고 표기한다. 현재 지원 기능·향후 대체 제품·보존/export 조건은 미확인. | 따라서 이번 조합의 유일한 크래시 수집기로 확정하지 않는다. Crashlytics와 중복 설치하면 비용보다 중복 로그/개인정보/심볼 운영이 먼저 늘어난다. |

**Unity 6.6 근거를 분리한다.** [IAP 5.4.4](https://docs.unity3d.com/6000.6/Documentation/Manual/com.unity.purchasing.html), [Analytics 6.3.0](https://docs.unity3d.com/6000.6/Documentation/Manual/com.unity.services.analytics.html), [Remote Config 4.2.5](https://docs.unity3d.com/6000.6/Documentation/Manual/com.unity.remote-config.html)는 공식 6000.6 문서에 released로 표기된다. 이는 후보 버전이지 설치 기록이 아니다. [Firebase Unity setup](https://firebase.google.com/docs/unity/setup)과 [GameAnalytics Unity SDK](https://docs.gameanalytics.com/event-tracking-and-integrations/sdks-and-collection-api/game-engine-sdks/unity/)는 통합 경로의 근거이며 정확한 에디터 패치/IL2CPP 공동 검증 근거는 아니다. 각 후보의 수 MB 추정치는 쓰지 않았다.

## 결제와 서버 검증

[Unity IAP 공식 검증 문서](https://docs.unity.com/en-us/iap/receipt-validation)는 로컬과 서버 검증을 구분하며, 새 Apple 구현의 서버 검증 입력으로 `OrderInfo.Apple.jwsRepresentation`을 안내한다. 오래된 StoreKit 1 receipt 예제를 그대로 채택하지 않는다. [IAP v5 이행 지침](https://docs.unity.com/en-us/iap/upgrade-to-iap-v5)은 외부 SDK/백엔드가 이전 receipt를 읽는 경우 조용히 구매 데이터를 놓칠 수 있음을 설명한다.

[권고] Unity IAP는 스토어 연결 어댑터로 한정한다. 클라이언트의 성공 알림만으로 영구 재화를 확정 지급하지 않는다. 서버가 스토어 구매를 검증하고 transaction/purchase token을 멱등 키로 원장에 기록한 뒤 지급한다. 재시도·중복 전달·복원·환불/취소·보류 거래·계정 변경·오프라인의 지급 상태를 명시해야 한다. Google은 [보안 지침](https://developer.android.com/google/play/billing/security)에서 안전한 백엔드 검증과 구매 상태 확인을 권고한다. 서버 자격 증명은 앱·Core·원격 설정에 넣지 않는다.

비용은 **IAP 패키지 계약 + 스토어 수수료 + 검증 함수/DB/네트워크 + 운영**으로 나눈다. 이 프로젝트의 판매 지역·사업자·스토어 프로그램이 정해지지 않아 유효 수수료율과 Unity IAP 상업 조건은 미확인이다. 특정 “15%/30%”를 일괄 예산에 넣지 않는다. 무료 패키지 여부가 지급 서버를 무료로 만들지 않는다. 후보 A는 Firebase 함수/DB, 후보 B는 UGS 서버 코드/저장을 사용하되 환불 알림·원장 보존의 실제 지원 범위를 도입 시 검증한다.

## 백엔드

| 후보 | 소규모 / 성장 비용 | Unity·IL2CPP·용량/충돌 | 소유·이전·종속 |
|---|---|---|---|
| UGS | [가격표](https://unity.com/products/gaming-services/pricing)의 서비스별 무료 한도 후 요청·저장·전송·실행 시간 과금. Analytics 무료와 Economy/Cloud Save 무료를 혼동하지 않는다. | Unity 서비스 패키지 간 Core/Authentication 버전 결합 확인. 정확한 선택 묶음의 6.6/IL2CPP·크기 미확인. | 서비스별 ID·경제 카탈로그·인증 계정 매핑 종속이 높음. 전체 플레이어 export/복구의 계약·도구 완결성은 미확인. |
| PlayFab | [공식 가격 안내](https://learn.microsoft.com/en-us/xbox/playfab/pricing/pricing-overview)는 Development Mode와 출시/과금 단계를 구분하며 최신 가격표로 연결한다. 본 프로젝트의 성장 견적·유효 단가 미확인. 개발 무료를 출시 무료로 가정하지 않는다. | [공식 Unity quickstart](https://learn.microsoft.com/en-us/xbox/playfab/sdks/unity3d/quickstart) 존재. JSON/HTTP·인증 라이브러리 및 stripping 확인 필요. 6.6 공동 실측 미확인. | 서버 권한·경제·계정 기능을 묶기 좋지만 PlayFab ID·API·CloudScript·데이터 모델의 이전 비용. 구매 검증/원장과 전체 계정 삭제/export의 실사용 검증 필요. |
| Firebase | [가격표](https://firebase.google.com/pricing): Spark와 사용량 기반 Blaze. DB reads/writes/storage/egress 및 함수 실행 등 각 비용 분리. 성장 시 이벤트를 DB 한 건씩 복제하면 write 비용이 부하에 비례한다는 설계상 위험. | Unity wrapper와 네이티브 Firebase/Google 라이브러리, EDM4U 정합성 확인. 실측 크기 미확인. | 일반 DB 모델은 자체 스키마를 쓰기 쉽지만 Auth UID·Security Rules·Functions·Firestore query에 종속. export가 계정 인증 상태의 완전 이행을 보장하지 않음. |
| Nakama 자체 호스팅 | [공식 저장소](https://github.com/heroiclabs/nakama): Apache-2 라이선스. 라이선스 비용과 호스팅 비용은 별개. 소규모에도 DB·백업·모니터링·보안 패치·당직 필요. 성장 시 HA/DB/트래픽/복구 인력 비용, 실제 견적 미확인. | [공식 Unity/.NET 문서](https://heroiclabs.com/docs/nakama/client-libraries/unity/) 존재. 클라이언트 serialization·AOT/stripping·TLS를 확인해야 함. 정확한 6.6 패치 실측 미확인. | DB/서버 운영을 직접 통제해 이전 자유는 높지만 Nakama 데이터 모델·서버 RPC·운영 역량에 종속. 자체 호스팅도 개인정보 의무가 사라지지 않음. |

[권고] 지금 필요한 것은 오프라인 장원이지 멀티플레이 서버가 아니다. 승인 후에도 지급 검증·계정 복구에 필요한 최소 서버 경계부터 시작한다. Core에서 벤더 SDK를 호출하지 않고, 정본 경제 ID/거래 ID/이벤트 명세를 자체 유지한다. 계정 삭제는 앱 로컬 파일 삭제와 공급자 삭제·백업 보존 종료를 별도 상태로 관리한다.

## 어트리뷰션

| 후보 | 소규모 / 성장 가격 | 통합·데이터 이전·종속 |
|---|---|---|
| AppsFlyer | [공식 가격](https://www.appsflyer.com/pricing/): Growth welcome package는 첫해 12,000 conversions, 이후 건당 $0.07 표기. Enterprise는 별도 견적. 유료 add-on 포함 여부와 계약상 conversion 정의를 확인해야 함. | [공식 Unity 설치](https://dev.appsflyer.com/hc/docs/installation)는 EDM4U 포함 경로를 설명. 6.6/IL2CPP 실측 미확인. raw export 권한·보존·매체 제한은 계약 확인 필요. ID/귀속 window/매체 연동 이전 시 연속성 단절 위험. |
| Adjust | [공식 가격](https://www.adjust.com/pricing/): Base는 첫해 월 1,500 conversions까지 무료, Core/Enterprise 별도 문의. 성장 단가 미확인. | [공식 Unity 통합](https://dev.adjust.com/en/sdk/unity/) 존재. 플랫폼 네이티브 SDK와 deferred deep link/생명주기 검증 필요. raw/aggregated 접근 상품 표기가 있어도 선택 계약의 export·보존 범위는 확인 필요. 6.6 실측 미확인. |
| Singular | [공식 가격](https://www.singular.net/pricing/): Free는 paid conversions 15,000, Growth는 $0.05/conversion 표기. 무료 수량의 기간/갱신 조건은 이 페이지에서 명확히 확인하지 못해 미확인. 자동 export는 optional add-on 표기. | [공식 Unity 가이드](https://support.singular.net/hc/en-us/articles/360037635452-Unity-SDK-Basic-Integration). API/S3/UI export 경로 표기가 있지만 계약 권한·매체 제한 확인 필요. 비용 집계 connector와 attribution 모델에 종속. 정확한 6.6/IL2CPP 실측 미확인. |

[권고] 유료 유입 캠페인 없이 세 SDK 중 하나를 먼저 붙이지 않는다. 광고 수익 분석과 광고 유입 귀속은 다른 문제다. ATT 거절 유저에 대한 개인 수준 귀속을 임의 fingerprint로 복원하지 않는다. 모델링된 귀속과 실제 관측치를 대시보드·원자료에서 분리한다.

## 비용을 계산하는 기준

아래는 **[가설]**, 공급자의 가격 약속이나 실제 유저 예측이 아니다. 작은 서비스 `DAU 1,000 / MAU 5,000`, 성장 `DAU 100,000 / MAU 300,000`, 하루 설정 fetch 2회, 월 30일을 비교 입력으로 둔다. DAU와 MAU는 대체 단위가 아니다.

| 항목 | 작은 서비스 | 성장 | 계산 범위/미확인 |
|---|---:|---:|---|
| Firebase RC fetch | 일 2,000, 공개 무료 구간 | 일 200,000: 초과 100,000 × $0.000006 × 30 = **$18/월** | 위 가설과 [공식 fetch 요율](https://firebase.google.com/pricing)만의 산술. 재시도·다중 앱 공유 프로젝트·기능별 호출은 별도 산정 |
| Firebase Analytics/Crashlytics | 공개 no-cost | 공개 no-cost | BigQuery·서버·네트워크는 제외 |
| Unity Analytics | 공개 무료 MAU 범위 내 | **MAU 구간 과금 발생** | [공식 가격](https://unity.com/products/gaming-services/pricing)의 단계 요율·event/query 한도와 실제 청구 방식 확인 후 견적. 단일 최고요율×전체MAU로 계산하지 않음 |
| MMP | 유료 전환량이 없으면 설치 보류 | 유료 conversion 수 × 계약 단가 + add-on | MAU를 conversion으로 대체하지 않음. 무료 프로모션 종료 후 견적 필요 |
| 검증 서버/저장/export | 거래·읽기·쓰기·GB·함수 실행 부하 미정 | 같은 단위로 계산, 지역·복제·보존 영향 | MAU만으로 월 총액 확정 불가 |

[권고] 광고 네트워크 추가와 export/원격 설정 주기를 비용 변경으로 취급한다. 월 예산·경보·중단 기준을 결정 이슈에서 확정한 뒤 구매/광고 지급을 막지 않는 별도 운영 제한을 설계한다. 비용 경보를 하드 상한으로 오해하지 않는다.

## 개인정보·스토어·한국 확률형 아이템

| 항목 | 확인한 공식 근거와 적용 경계 | 출시 전에 닫을 항목 |
|---|---|---|
| ATT | [Apple 공식 설명](https://developer.apple.com/app-store/user-privacy-and-data-use/): 다른 회사 앱/웹 데이터와 결합하는 tracking 및 광고 식별자 접근에는 ATT 조건 적용. ATT는 모든 분석에 일괄 요구되는 동의 창도, GDPR/한국법 동의의 대체물도 아니다. | 추적 목적/파트너/전송 필드 분류, 거절 시 기능, 동의 전 초기화·전송 여부 실측 |
| Apple SDK/개인정보 표기 | [Apple third-party SDK requirements](https://developer.apple.com/support/third-party-SDK-requirements/): 해당 목록 SDK 및 재포장 SDK의 manifest/signature 요건 검토. | 실제 archive에 포함된 전이 의존성까지 inventory와 manifest 확인. SDK가 제공한 파일만으로 앱 신고 정확성을 보장하지 않음 |
| Google Play Data safety | [공식 작성 지침](https://support.google.com/googleplay/android-developer/answer/10787469): 앱 및 SDK의 데이터 수집·공유를 개발자가 정확히 신고해야 함. | 광고 ID·앱 활동·구매·진단·기기 ID의 필수/선택·암호화·삭제 흐름을 최종 바이너리와 맞춤 |
| GDPR | [EDPB controller/processor](https://www.edpb.europa.eu/sme/learn-the-basics/data-controller-or-data-processor_en), [국제 이전](https://www.edpb.europa.eu/sme/be-compliant/international-data-transfers_en): 역할·처리 목적·위탁자 의무·국제 이전 근거를 따로 검토. | 국가/연령/법적 근거/보존·삭제/열람·이전 요청/하위처리자/DPA. 각 벤더가 모든 기능에서 단순 processor인지 미확인 |
| 한국 개인정보 보호법 | [국가법령정보센터 제28조의8](https://www.law.go.kr/LSW/lsLinkCommonInfo.do?chrClsCd=010202&lsJoLnkSeq=1034292881): 국외 제공·위탁·보관에 법정 요건 검토 필요. [현행법 안내](https://www.law.go.kr/법령/개인정보보호법)는 자동 본문 추출이 안 되어 다른 현행 조문 전체 대조는 미확인. | 사업자·처리방침·수집 근거·위탁/제3자 구분·해외 수신자/국가/항목/기간/거부 방법을 결정. “동의만 받으면 모두 적법”으로 단정하지 않음 |
| 한국 확률형 아이템 | [문체부 공식 해설서 배포](https://www.mcst.go.kr/site/s_notice/press/pressView.jsp?pSeq=20865)는 유료 확률형 아이템의 범위·표시사항·방법 판단 자료. [현행 게임산업법 안내](https://www.law.go.kr/법령/게임산업진흥에관한법률) 본문 자동 추출 불가. 최신 개정 전체·예외·개별 상품 적용은 미확인. | 이번 P2에는 유료 확률 상품 없음. 향후 유료 재화와 무료 재화 혼합, 조각 뽑기/합성/천장/확률 변경까지 상품 구조로 검토. 확률표·게임/홈페이지/광고 표기·변경 이력의 구체 의무는 출시 상품 확정 후 법무 확인 |

마지막 행은 “무작위가 있으니 무조건 유료 확률형” 또는 “직접 현금 결제가 아니니 제외”라는 판정을 피하기 위한 경계다. 현재 판 안 무작위 카드와 미래의 유료 확률 상품은 분리한다. 법률의 적용 범위·면제·제재 수치를 확인하지 않고 기재하지 않았다.

**데이터 소유의 구체 조건 [권고]:** 공급자 약관의 소유권 문구만으로 이전 가능하다고 판단하지 않는다. 원시 이벤트/거래/설정의 export 권한, 과거 기간, 포맷, 빈도, 다운로드 비용, 해지 후 유예, 삭제 증빙, ID 연결, 하위처리자, 해외 복제 위치를 계약 표로 받는다. vendor ID는 내부 콘텐츠 ID로 사용하지 않는다. 개인정보를 Core seed·결정론 해시에 넣지 않는다.

## 승인 전 미확인 목록과 도입 관문

1. **상품·국가·연령·개인정보 운영 책임자**가 미정이다. 법 적용/동의 설계·스토어 수수료를 확정할 수 없다.
2. **월 운영/UA 예산과 규모·보존 기간**, 계약별 export 조건이 미정이다. 총 월 비용을 보장하지 않는다.
3. **광고 보상·IAP 상품과 서버 원장**이 미정이다. 지급 중복·환불·계정 복원 정책부터 결정한다.
4. **SDK 정확 버전과 어댑터 조합**은 미정이다. 승인된 한 조합만 별도 가지에서 6000.6.4f1 Android/iOS IL2CPP build, stripping, 콜백·네트워크 거절·동의 거절·삭제·symbolication·크기를 검증한다. 현재 #98 Fold7 보류를 이 조사로 해제하지 않는다.
5. **서버 설치/운영 계약은 승인되지 않았다.** 본 문서는 도입 추천이며 설치 명령서가 아니다. 결정 질문은 [스택·후속 검증 #120](https://github.com/hyunlord/bs-mobile/issues/120), [출시·데이터·예산 #121](https://github.com/hyunlord/bs-mobile/issues/121), [보상·지급 원장 #122](https://github.com/hyunlord/bs-mobile/issues/122)에서 관리한다. 결정 대기 중에도 로컬 성장·저장·문서 작업은 진행한다.

모든 표의 판단은 위 확인일의 공식 자료와 명시된 미확인 범위 안에서만 유효하다. 저장소에 운영 SDK·자격 증명·서버 리소스를 추가하지 않았다.
