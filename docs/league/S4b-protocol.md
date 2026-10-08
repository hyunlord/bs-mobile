# S4b 사전 선언 (측정 전)

R1 관문 완료 후 #56에서 이 문서를 PR·CI로 고정한 다음 수치 보정을 시작한다. 기존 S4의 실패 원본은 보존한다.

- 실행 30Hz, 21600틱(720초), 사람 A/B/C, 카드 정책 weapon/land/building/people/mixed/random. 반복3회는 결정론 검사이며 통계에서는 첫 실행1회만 센다. 같은 seed를 공유하는 정책·사람 규칙·이동 비교는 대응 설계이며 독립 표본으로 합산하지 않는다.
- A: 모든 정책이 데이터의 같은 estate-relative circuit을 따르며, 이동은 정책·카드·공유RNG를 읽지 않는다. seed42–73의6×3×32=576서로 다른 사례(독립 seed32개, 사람×seed96대응묶음). 기존S4seed와 같아 모델간 비교가 가능하지만 완전히 새 seed라고 부르지 않는다.
- B: random 카드 정책 고정, movement harvest/circuit/evade × 사람A/B/C × 같은32seed =288서로 다른 사례(동일한 독립 seed32개, 사람×seed96대응묶음). A의관문결정에 B결과를 섞지 않는다.
- 보정: 별도1000–1031 seed의random/C만 사용한다. 전역 위협·XP 후보 최대6개를 순서대로 평가하고, 후보마다 모든 JSON값·변경이유·해시·32결과를 보관한다. 각 후보는 위협과 공유 XP곡선의 완전한 수치 묶음이다. 기준생존30–70%와 보정생존자레벨중앙값25–45를 동시에 만족하는 첫 완전한 후보를 고정한다. XP조정 후에도 같은32seed의생존율을 다시 확인한다. 최대횟수후범위를못맞춰도최종후보와FAIL을보존한다. A 평가를 본 뒤 값을 다시 조정하지 않는다.
- XP: Base+Linear*(level-1)+Quadratic*(level-1)^2, 모든계수data. 720초 생존한 사례의 각정책×사람그룹 최종레벨 중앙값25–45 목표. 생존자0인그룹은판정불가/FAIL. 전체종료레벨(조기사망포함)과생존자범위/중앙값을함께보고한다. XP 목표와(a-d)주관문은별도표기하지만 단계 전체 통과에는 둘 다 필요하다. XP 실패를 진단값으로만 취급하지 않는다.
- mixed: 제안카드중 현재고유소유카드수가가장적은weapon/land/building/people 범주우선, 기존 Category() 의미를 재사용하고 동률범주순서data에고정. 범주내선택은기존가중선택. random과비교한각사람×seed96쌍의정규화카드/선택시점원장동일쌍0을별도검사한다.
- 거리: 0틱/일정telemetry간격/실제종료틱의영주-영지중심유클리드거리, worldunit 명시. median/p95/분포와표본수. 사망뒤표본을만들지않는다.
- 익은밭대기: 처음ripe상태진입부터 harvest/destroyed/actualend까지틱별추적. 수확완료대기와 death/duration censor를분리. 검열값을완료대기처럼평균내지않으며관측나이의하한분포도별도표시한다.
- (a) 평가A random/C 32사례의생존율30–70% inclusive, n/32와Wilson95%구간.
- (b) A의사람×seed96조건에서공동1위를포함해항상1위인정책이없어야한다. 조건별순위는생존→도달틱→종료레벨→실제귀속전투피해의사전식. 완전동률만공동1위.
- (c) A의사람A/B/C각32seed묶음에서정책별생존율→평균도달틱→평균종료레벨→평균귀속피해의사전식순위. weapon/land/building/people각각최소한묶음1위/공동1위. 4정책/3묶음이므로통과에는공동1위가필요하며완전동률을임의오차로확장하지않는다. 해석민감성은needs-decision에기록하되보수적으로판정하며대기하지않는다.
- (d) 식량·자재stockpile은어떤순위필드에도없다. 랭킹필드화이트리스트와값만변형한회귀검사.
- a-d 또는 XP 또는 mixed 원장 분리 조건 중 하나라도 실패하면 단계 전체 관문 FAIL. 전체 PASS는 실험 유효성과 이 조건을 모두 만족할 때만 가능하다. 이동동일성/원장분리/프로파일검증/결정론/CSV재생성/원자료해시실패는실험유효성FAIL, 통계결론보류. 실패결과·원인가설·다음실험을남기고특정정책버프로관문을맞추지않는다.
- R3은선택A의기본ID및최소잔재고리만추가하고동일한최종R2프로토콜/seed로A와B를각1회재실행한다. 추가3결정론반복을독립표본으로세지않는다. R2결과를덮어쓰지않는다.

실행증거 계약: movement-samples.csv는 caseId,tick,lordX,lordY,estateX,estateY,distanceSquared,movementMode 필드와 0틱/종료틱을 포함한다. farm-wait-events.csv는 caseId,farmId,episode,ripeTick,endTick,endKind,observedWaitTicks,censored를 포함한다. A 실제좌표는 각사람×seed의6정책 공통생존틱구간에서 정확히같아야한다. 사망후좌표를만들지않는다. B 세이동도RNG를쓰지않는결정함수로고정한다. XP의 Base/Linear/Quadratic JSON계수와최종프로필해시는메타데이터CSV에기록한다.
