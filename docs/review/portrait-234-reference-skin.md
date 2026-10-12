![화면4건 — 왼쪽 제공 시안 / 오른쪽 실제 Editor](https://github.com/hyunlord/bs-mobile/releases/download/portrait-v0-fa6c71a/four-fixes.png)

관문: 부분 — HUD 여백·부활 뒤 글자 숨김·일시정지 진화 설명·생산 상자 대비와 Gowun Batang을 반영했다. 사용자 시각 승인은 대기다.
깨끗한 `fa6c71a`의 Editor19/19(BM9 포함), CI Core664/664가 통과했다. Mac 빌드와 비교 시트를 먼저 전달한다.
성능은 사용자 요청으로2026-10-12 11:35 KST 보류했다. 완료된 B2 C05 최대841.7649ms로 기준 미달이며, 각3회도 미완료다. #223을 해결했다고 보고하지 않는다.

## 화면·배포

[Mac ARM64 앱](https://github.com/hyunlord/bs-mobile/releases/download/portrait-v0-fa6c71a/Sow-and-Siege-portrait-fa6c71a-mac.zip) · [13화면 및 긴 비율 비교](https://github.com/hyunlord/bs-mobile/releases/tag/portrait-v0-fa6c71a) · [추가 상태 표본](https://github.com/hyunlord/bs-mobile/releases/download/portrait-v0-fa6c71a/positive-states.png)

빌드·캡처 소스는 `fa6c71a03bd42810e13c66fb44f81d561ec1a53e`, sourceHash `2A31903EFFC0CECD00C6B2F5F5CEA3A3DE691BB6DF638A63010E2DBAEFF381B1`, sourceDirty=False다. 문서 후속 커밋을 앱 커밋으로 표시하지 않는다. ARM64 Mono Release 빌드와 개발 기능 제외 검사가 통과했다.

- HUD 메달·체력 바를 SafeRoot 안쪽8pt 이상으로 내렸다. 390×844와1080×2520 Editor 표본 및 HUD 기하 검사를 확인했다. 비영 노치·물리 기기 판정은 아니다.
- 부활 창에서 실제 피해/성장 글자 풀을 숨기고, 복귀 때 다시 표시한다. PlayMode 검사는 활성 글자4개 이상→창 중0→복귀4개 이상을 확인했다.
- 일시정지에는 세 진화 조건을 모두 표시하고 설정 내용을 스크롤 영역으로 옮겼다. 고정 계속·포기 동작은 유지했다.
- 생산 상자 받침을 어둡게 하고 금빛 글자의 대비를 높였다. 설계와 남은 색·굵기 차이는 사용자 비교로 판단한다.
- Gowun Batang Regular/Bold 고정 원본과 OFL을 번들했다. 본문은 기존 Noto, 제목·장식 버튼은 Gowun이다. 설정·뒤로·닫기는 원형 조작부로 다듬었다.
- rare 무기, Lv1→2, 활성 도전 받기, 실제 정산 로직의 가신 조각+2를 추가 캡처했다. 결산·부활은 명시적 시험 fixture이며 정상 플레이 완주·미정 경제 승인 근거가 아니다.

[요소별 정본](portrait-234-elements.json)은106개 중 **표시89·차이16·미구현1**과42PNG의 출처를 기록한다. 표시 확인은 픽셀 일치가 아니다. 미구현1개는 수령 데이터가 없는 패스 강조/받기 상태다. 남은 외형 차이에는 생산 상자 색·글자 굵기, 출정 선택색·보스/자원/잠금 아이콘, 가신 별/다음 효과, 동일한 상점 외형 그림과 일부 상태 표본이 있다. 가짜 보상으로 메우지 않는다. 제공 아트50개는 원본과 바이트가 같다.

## 검증

|항목|결과|경계|
|---|---|---|
|Editor 흐름|19/19, BM9 포함|사용자 시각·실기 승인 아님|
|CI|[38192436867](https://github.com/hyunlord/bs-mobile/actions/runs/38192436867) 통과, Core664/664|결정론 안전망; 성능 합격 아님|
|배포|Mac ARM64 Mono Release, 개발 기능 제외 검사 통과|일반 실행은 자동 측정/fixture 비활성|
|기능 범위|Core·data·schema 무변경|BM·저장·리플레이 규칙 유지|
|부분 재생|완료9실행 모두 고정 최종 해시 일치|A/B 각3회 미완료|

## #223 부분 측정과 보류

[부분 결과 및 출처](stutter-223-fa6c71a-partial.md) · [정본 CSV](../metrics/stutter-223-fa6c71a-partial.csv)

A1·B1·B2·A2의411/C05와 A3의411만 완료했다. 완료 실행은 캡처·Profiler 없이 모든 측정 행focused=1, interrupted=false다. B의focus/exclusion은0이고 A에는 새 이벤트 필드가 없다. B1/B2의411은 >25ms1.180%/3.499%, B2 C05 최대841.7649ms여서 기준 미달이다. 낮은 p95로 이 실패를 덮지 않는다. A도 반복 간 차이가 커 개선/원인 확정은 하지 않는다.

A3 C05 두 시도는 Orca·Google Chrome·카카오톡·OpenUsage 전경 전환으로 미완료다. 기존 A는 새 자동 재개 기능이 없으므로 실패 로그를 보존했다. B는 전경 이탈 구간만 제외하고 진행 위치를 보존하지만, B3은 사용자가 측정을 보류한 시점에 대기 중이었고 최종 결과가 없다. 11:35:25 KST 게임·관측기를 종료했다. 사용자 재개 지시 전 새 측정을 시작하지 않는다.

Development 같은 프로세스 반복의254.7951/1111.5987ms에서는 여러 렌더·메인 스레드 스코프가 함께 늘어 단일 원인이 분리되지 않았다. 후속 프레임의 진단 파일 조회19.756ms는 계측 자체의 이차 비용이며 최초 지연이나 Release841.8ms의 원인이라는 증거가 아니다. 용량 증가·GC·첫 사용만의 문제로 단정하거나 근거 없는 사전 할당을 추가하지 않았다. 과거 실패를 보존하며 #223은 열어 둔다.

재생성: `node tools/render-portrait-reference.mjs artifacts/portrait-234 artifacts/portrait-234/comparison-fa6c71a-final`. 원본 캡처의 commit/source/hash는 요소 JSON에 있다. 큰 성공 원자료나 새 증거 ZIP은 올리지 않는다. 이번 ZIP은 승인된 Mac 앱 배포용 하나다.

[PR235 초안](https://github.com/hyunlord/bs-mobile/pull/235) · [코드 fa6c71a](https://github.com/hyunlord/bs-mobile/commit/fa6c71a03bd42810e13c66fb44f81d561ec1a53e) · [CI](https://github.com/hyunlord/bs-mobile/actions/runs/38192436867)

#126·#98·#224·1b 보류, SDK 미도입을 유지한다. 사용자 승인 전 병합하지 않는다.

## 역사 기록 — 이전 소스의 판정이며 현재 상태로 대입하지 않음

아래 보고는 e3f5817 및 당시 진단·측정 준비 상태를 그대로 보존한다. 앞의 fa6c71a 후속 보고가 현재 상태이며, 과거 미해결/미검증을 삭제하거나 소스 식별자를 바꾸지 않는다.

![제공 시안과 실제13화면 비교](https://github.com/hyunlord/bs-mobile/releases/download/portrait-v0-8f446e5/overview.png)

관문: 부분 — 제공 시안의 장식·프레임·버튼을13화면에 적용했지만 원본과의 차이와 미확인 상태가 남아 사용자 시각 판정을 기다린다.
깨끗한 `8f446e5`의 Editor 검사17/17(그중 BM9)이 통과했고, 제공 아트50개는 원본과 바이트가 같다.
성능은 Release 반복 측정 준비·미완료 상태다. Development의 긴 지연은 반복됐으며 #223을 해결했다고 판정하지 않는다.

### 화면 반영과 차이

이번 비교의 원본은 [제공 패키지](../design/portrait-v0)이며, 이전 서면 대체 보드를 원본 시안으로 쓰지 않는다. 패키지 반입 [PR233](https://github.com/hyunlord/bs-mobile/pull/233)은 병합됐고 구현 [PR235](https://github.com/hyunlord/bs-mobile/pull/235)는 초안이다. 첫 이미지와 비교 시트는 [검토용 Release](https://github.com/hyunlord/bs-mobile/releases/tag/portrait-v0-8f446e5)에 게시한다. 비교 시트는 `artifacts/portrait-234/comparison/index.html`에서13화면의390×844·1080×2520을 확인한다.

검토한106요소는 **존재77·차이26·누락3**이다. `present`는 화면에 보인다는 뜻이며 픽셀 일치·기능 통과·사용자 승인이 아니다. `different`에는 확인된 외형 차이와 양성 상태 표본 부족이 함께 들어 있으므로 개별 사유로 구분한다. [요소별 정본](portrait-234-elements.json)은 검토자의 원본 JSON을 그대로 보존하며26기본 캡처와6추가 fixture의 해시·출처를 포함한다.

| 구분 | 남아 있는 차이·미확인 |
| --- | --- |
| HUD·일시정지 | 레벨/체력 위치가 원본보다 위에 있음. 일시정지 계절 문구가 없고 진화 설명은 스크롤 경계에 걸림 |
| 영지·출정 | 일부 원형 아이콘이 사각 글자 버튼임. 보석·보스·자물쇠·상한 자원 아이콘 및 챕터 선택 색에 차이. 생산 상자 글자 대비가 낮음 |
| 가신·도전·상점 | 가신 별/다음 레벨 효과 없음. 도전 카드가 커 스크롤 필요. 미수령/잠김 상태 표본 부족. 상점 두 외형 슬롯의 영주 그림이 같음 |
| 부활 | 뒤의 수확 글자가 제목/기록 사이에 남음. 현재 fixture는 광고 제거 보유의 바로 일어나기이며 광고 표시 상태가 아님 |
| 승인 예외·글꼴 | 기존 한국어 로고와 최소11pt를 유지. 미제공 Gowun Batang 제목 글꼴의 완전 일치는 미확인 |

누락3개는 서로 다른 이유다. 결산 조각 줄은 포기·0수확 표본에서 보이지 않으며 실제 지급 fixture는 미검증이다. 도전의 활성 받기 버튼도 현재 진행/받음 표본에는 없다. 패스의 받을 칸 강조·수령 상태는 현행 수령 데이터가 없어 미구현이며, 시안처럼 보이도록 보상을 만들지 않는다.

추가 진화·결산 보상 fixture6장은 기본13화면을 대체하지 않는다. 부활·결산 배율 fixture를 일반 실행 승인으로 해석하지 않는다. 긴 비율 캡처의 safePixels는 전체 화면이므로 비영 노치/홈 인디케이터 여백·실제 터치·실기 판정은 미검증이다.

### 검증 출처와 성능 경계

| 항목 | 현재 증거 | 판정 경계 |
| --- | --- | --- |
| 최종 UI | `8f446e5f46273a7f6001f6e09411e9bdd0c9f534`, sourceDirty=False | 캡처·Editor 검증 소스 |
| Editor 흐름 | `artifacts/portrait-234/final-8f446e5-playmode.xml`,17/17 통과(BM9 포함) | 정상 완주·사용자 미감·실기 증거 아님 |
| 아트·규칙 경계 | 제공 아트50개 바이트 동일, Core/data/schema 무변경 | UI가 규칙·경제를 바꾸지 않음 |
| 기존 CI | `0aa97e6` [CI38149253171](https://github.com/hyunlord/bs-mobile/actions/runs/38149253171) 통과 | 최종8f446e5 CI는 아직 게시 전; 기존 CI를 최종 통과로 옮겨 쓰지 않음 |
| Development 같은 프로세스 반복 | 두 번 모두 해시 일치·포커스 이탈 없음; 최대658.9129/649.4870ms, 해당 지연 GC 증가0·사건0 | 첫 등장만의 비용 가설은 지지되지 않음. OS/GPU 등 원인 확정 아님 |
| Release 반복 | 최종8f446e5 앱 빌드 완료. 첫 A411이 측정 시작 전 포커스를 잃어 중단됐고 미완료 실행은 보존. 반복 측정은 준비·미완료 | 새 반복 결과 없음; 통과·개선 주장 없음 |

Release의 기존A와 새UI B는 각 fixture·반복에서 최대50ms 및25ms 초과 비율≤0.2%를 확인하며 C05의800+도 따로 본다. 평균이나 이전 빌드의 단일 표본으로 실패를 덮지 않는다. Development 기록은 진단용이며 Release 합격을 대신하지 않는다. Part1의 실패와 [#223](https://github.com/hyunlord/bs-mobile/issues/223)은 보존한다.

#126 경제·#98 Fold7·#224 생산 부활 규칙·1b 확장 보류를 유지한다. 사용자 확인 전 PR235를 병합하지 않으며 전체 성공 원자료·재생성 가능한 중복 보고서를 새 증거 묶음으로 올리지 않는다.

### 2026-10-12 진단 후속 — 최초 지연은 미해결

기존7f Development 두 반복의 최대658.9129/649.4870ms와 GC 증가0·사건0은 그대로 보존한다. 같은 프로세스 반복에서도 발생해 첫 등장만의 비용이라는 가설은 지지되지 않는다. 각 반복에서 프레임 간격 상위3개를 사전에 정한 기준으로 고른 [실패6프레임 CSV](../metrics/stutter-223-diagnostic-samples.csv)는 뒤따른28/25틱 몰림도 포함한다. 스코프 합과 프레임 간격의 차이를 OS 원인으로 단정하지 않는다.

`29deb1b` 트리거 진단은 두 시도 모두 다른 프로젝트의 Godot가 전경이 되며 포커스를 잃었다. 이 중단 실행은 유효한 연속 전경 계측으로 채택하지 않는다. 최종 Release A/B 반복 역시 전경 유지 조율 전까지 미완료이며 새로운 합격 표는 없다.

이차 회복 몰림 수정 `ef86f6a`는 정상 속도에서 표시 프레임당 Advance1회로 제한하고 남은 누적 시간을 보존한다. [ADR0055](../adr/0055-defer-recovery-ticks-between-rendered-frames.md)에 입력 순서·해시 계약과 A/B pacing 차이를 명시했다. Node17개와 최종8f446e5 Unity pacing25/25가 통과했다. 실제 카드 제안·선택을 생성한 동일 명령 fixture에서 기존 배치와 새 배치의 최종 상태 해시가 일치한다. 트리거 진단26/26도 통과했다. 최초 수백ms 지연 수정·최종 성능 통과로 주장하지 않으며 #223은 열어 두고 PR235는 초안을 유지한다. 최종8f446e5의 Editor 흐름17/17(BM9 포함)과32개 화면 표본을 다시 수집했고 ARM64 Mono Release 빌드 및 개발 기능 제외 검사도 통과했다.
