관문 결과: S5 PASS — 7종 조사, 02의 10개 본절·§2.1 대응, 출처 55개·참조 109곳 검증. 비교 가능한 경쟁작 리텐션은 미확인으로 남겼다.

# S5 시장·BM 근거 인계

[이슈 #41](https://github.com/hyunlord/bs-mobile/issues/41)의 문서 작업이다. [S5-bm.md](../research/S5-bm.md)에 판 안/밖 구조, 통화, 상품의 힘/외형 구분, 광고, 에너지, 패스, 공개 지표, 개별 불만과 원본 02 절별 권고를 모았다. 모든 출처 확인일은 2026-10-08이다.

핵심 구분은 Brotato 무료/Premium/PC와 VS 일반/Arcade 판본, Megabonk 공식 모바일 미확인, Kingshot 스킨의 능력치다. 다운로드 구간·과거 추정 매출·현재 상품 목록·코호트 리텐션을 서로 대체하지 않는다. 가이드의 과거 관찰과 실기 확인을 구분한다. 상점 가격·확률·에너지 한도는 승인하거나 코드에 구현하지 않았다.

[정적 문서 검사](../evidence/S5/document-check.json), [출처 목록](../evidence/S5/source-ledger.json), [독립 출처 검토](../evidence/S5/source-review.md)를 보존했다. 원본 설계 문서 세 개의 최초 커밋 대비 diff는 비어 있다. 근거 없는 리텐션 부재 표현, 직접 에너지 근거 누락, Premium 해금 설명 과장을 고쳐 재검토했다.

[전체 로컬 검사](../evidence/S5/full-check.log)는 콘텐츠 68, PR 정책 6, CSV/리그/보고서 57, .NET 95, Guard 24개 및 실제 위반 0건, 빌드·형식·S2/S4 연기 시험 통과다. 변경은 조사·보고·증거 문서뿐이며 Core·데이터·스키마·SDK·의존성 변경은 없다. 연구 자체는 앱 설치·결제·광고 시청·모바일 실기 검증이 아니다.

## 원본 02에 대한 권고

힘의 축은 가신 중심을 우선 비교하고 장비 배율 추가는 보류한다. 영웅·영지 각 하나와 외형 무스탯을 유지한다. 광고 제거 권리, 패스의 무료/유료 보상·만료, 에너지 또는 보상 한도는 경제 시뮬레이션과 실제 사용자 관측을 거쳐 정한다. 원문의 외부 D7·광고 횟수 기준은 해당 코호트 근거 미확인이므로 통과선으로 쓰지 않는다. 선택은 [needs-decision #42](https://github.com/hyunlord/bs-mobile/issues/42)에 남겼다.

## 최종 감사 정정

기존 S3 보고서 두 곳의 Guard 자체 검사 수 35는 보존된 당시 `full-check.log`의 24/24와 달랐다. 현재 보고서는 24로 바로잡고 정정 기록을 덧붙였다. 원본 로그와 이미 전달한 S3 ZIP은 변경하지 않았다. S5 ZIP에는 정정된 보고서가 들어간다. 검사 실패를 숨긴 변경이 아니라 요약 숫자의 정정이다.

## 남은 관문과 결정

- S0 PARTIAL: GitHub Projects 보드 생성 권한 범위 부족 [#2](https://github.com/hyunlord/bs-mobile/issues/2). 의도 위반 PR 다섯 개는 모두 CI에서 차단했다.
- S1/S2/S3 PASS. 원본 검토·결정론 코어·콘텐츠 후보 관문의 결과이며 재미나 출시 승인과 다르다.
- S4 balance FAIL: 무기형이 96/96 조건에서 엄격한 레벨 우위. 576사례·1,728회 실행과 일곱 CSV 기반 재현은 통과. 후속 통제 실험 [#40](https://github.com/hyunlord/bs-mobile/issues/40).
- 기본 조합·경제 경계 [#32](https://github.com/hyunlord/bs-mobile/issues/32), 초기 BM 권리 [#42](https://github.com/hyunlord/bs-mobile/issues/42)는 사용자 결정 전이다. 기다리지 않고 이번 조사 범위를 완료했다.

Unity 회색 상자·사람 플레이·운영 SDK는 다음 개발 단계의 작업이다. Phase0의 문서와 헤드리스 증거를 실제 모바일 제품의 완성으로 표현하지 않는다.
