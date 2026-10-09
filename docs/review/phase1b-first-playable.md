관문: 부분 — M1·M2·M3 병합 완료. 사용자 요청으로 폴드7 연결 작업은 보류하고 M4의 독립 검증을 진행한다. 최종 사용자 빌드는 아직 전달하지 않는다.

# 1단계 B: 첫 플레이 가능판

- 기준: main `9b126bd8cb5d4524d550bb180bd49e45b810e0e7`, [의뢰서](../design/06_ASTRA_GOAL_bs-mobile_phase1b-first-playable.md), [#94](https://github.com/hyunlord/bs-mobile/issues/94).
- 진행 순서: [M1 #95](https://github.com/hyunlord/bs-mobile/issues/95) → [M2 #96](https://github.com/hyunlord/bs-mobile/issues/96) → [M3 #97](https://github.com/hyunlord/bs-mobile/issues/97) → [M4 #98](https://github.com/hyunlord/bs-mobile/issues/98).
- 사용자 확인을 기다리지 않고 순서대로 진행한다. 이 문서는 완료 선언이나 사용자용 빌드 전달이 아니다.
- 기존 12분 production과 역사 결과를 유지하고 별도 first-playable 프로필에 15분 규칙을 적용한다. [ADR0029](../adr/0029-first-playable-contract-and-historical-isolation.md).
- 실제 플레이 화면에 도형 대체물·기본 UI 스타일이 남아 있으면 최종 전달하지 않는다.

| 마일스톤 | 충족해야 할 요구 | 실제 증거 / 상태 |
|---|---|---|
| M1 콘텐츠 | 무기10×12레벨, 도구8(3/3/2), 특허8, 물품30, 진화8(교차≥4), 일반적10+정예2+보스1, 지도사건3 | 정의 검사 및 현재 소스에 결합한 실행 관찰156개 통과 |
| M1 로직 | 도구의 발동·성장, 구별되는 공격 형태, 태그 효과, 진화 조건, 사람 목표 적, 건설/작물/백성 상태, 시작 가신1 | 재건 수정 후 전체343테스트 통과; 실제 수확 진화·특허 양축·수레 예산·폐허 문맥·단계 재건 우회 방지 회귀 포함 |
| M1 관문 | Core·두 타깃 결정론·확장성·구현 검사, 32×3×6 리그(b)/c′/혼합2묶음≥random | 재건 수정 후 `tools/check.sh` 통과. 역사96회 및 신규5seed×두DLL×3회=30회 해시 일치. candidate-01의 결함·당시 결과를 보존하고, 수정 소스7e4dd4a의 candidate-02도576조건·1728회 및(b)/c′/무기하한/혼합3묶음 통과. 모두32/32 생존이며 대응 게임플레이 해시 변화는 없어 수정 효과는 실제 재건 회귀로 별도 증명. [정본 CSV와 한계](../league/first-playable-m1/README.md) |
| M2 방향·그림 | 같은 장면 후보3장, 1안 채택, 아트 바이블, 역할→그림 계약, 전체 스프라이트/계절 지형 | 후보3장·1안 선택·11atlas/345roles/329bindings 및 실제 Editor 표본 검증. [PR106](https://github.com/hyunlord/bs-mobile/pull/106), commit2ad450ec, [CI37855429211](https://github.com/hyunlord/bs-mobile/actions/runs/37855429211), [선정 원자료](https://github.com/hyunlord/bs-mobile/releases/tag/phase1b-m2-world-v1) |
| M2 손맛 | 피격/넉백/처치/흡수/수확/레벨업/진화/흔들림/피해수치/보스/계절 전환 | 대표 실제 GPU 검사와 효과 라우팅 통과. 모든 자연 전투 연출·기기 판독성은 M4 체크리스트에서 확인 |
| M3 흐름·UI | 타이틀·한 번만 안내·판·결산·재시작·설정, 작은 HUD·장비 아이콘·카드 변화/진화 단서 | 타이틀/첫 안내/판/카드/설정/사망/결산/재시작, 설정 저장·동일 카드 복귀·리플레이 검증 완료. 세로/정사각18캡처와 독립 시각 검토2개 통과. [PR107](https://github.com/hyunlord/bs-mobile/pull/107), commit b82e5f2, [CI37861565231](https://github.com/hyunlord/bs-mobile/actions/runs/37861565231), [선정 표본](https://github.com/hyunlord/bs-mobile/releases/tag/phase1b-m3-ui-v1) |
| M3 소리 | 7종 효과음과 반복 배경음, 음량/진동, 출처·라이선스 | 원본 효과음7개+16초 음악, 실제 master7cue 출력/mute/2loop 관측 통과. [오디오 원자료와 한계](../licenses/assets.md) |
| M4 성능 | 실제 폴드7에서 지속 60초의 적500+ 1배속 창, p95≤16.7ms, 50ms 끊김 원인 계측 | 진단 raw 분석 완료, 최적화 후 실측은 사용자 보류. 2배속·프로파일러 진단을 관문 통과로 사용하지 않음 |
| M4 자체 QA | 정상 완주3판, 초/중/후/보스/결산 캡처, 30초 이상 실제 녹화2개, 양쪽 화면 비율 | 정상3판·기기 영상 미완료, 연결 작업 보류. Editor 양쪽 비율은 M3에서 확인; 물리적 힌지 조작은 #83 대기 유지 |
| 최종 전달 | 검증한 APK·해 볼 것 목록·커밋·PR·CI·선정 Release 표본 | 모든 관문 통과 후 |

## 표본 선정과 검증 한계

M1 리그 원자료 표본은 실행 전에 고정한다. 정상 표본은 seed40000의 혼합 정책 A/B/C 세 사례이며, 실패 후보는 실패 관문을 설명하는 가장 작은 seed의 해당 정책 사례를 중복 없이 최대6개 선택한다. 전체 실행 packet은 로컬에 보존한다. 소스·설정 식별자와 576사례 결과·재생성 명령은 정본 CSV로 남기고, 집계표·그래프를 Release에 중복 게시하지 않는다.

Release에는 최종 검증 APK, M2 아트 방향 후보3장, 실제 플레이의 각 구간을 대표하는 캡처와 서로 다른30초 이상 녹화2개를 올린다. 영상1은 첫 판의 이동·카드·수확 흐름, 영상2는 후반 적/영지/보스의 판독성과 손맛을 대상으로 선정한다. 성능 원자료는 최종500+ 지속 구간 한 실행과 재현이 필요한 실패 표본만 올린다. CSV에서 재생성 가능한 JSON·그래프·HTML은 업로드하지 않는다.

완주3판은 서로 다른 실제 기기 실행이며 저장 기록의 재생3회를 뜻하지 않는다. 테스트 자동 입력은 명시하고 사람의 재미 판단을 대신하지 않는다. 무적·가속 스트레스는 정상 완주의 대체 증거가 아니다. 순간적으로 적500을 넘겼다는 사실만으로500+ 성능 관문을 통과시키지 않는다. 물리적 접힌 상태와 펼친 상태의 관측이 없으면 화면 비율 검사로만 표기한다.

## M3 로컬 검증

- 동일 변경56파일을 복사한 격리 체크아웃에서 `tools/check.sh` exit0: Core349, 역사96회·FP30회 타깃 해시 일치, 콘텐츠·그림·음원·경계·형식·smoke 검사 통과.
- `tools/check-unity.sh` exit0: EditMode44/44, PlayMode19/19, 실제 Release 컴파일의 개발 코드 배제, ARM64 IL2CPP Android 빌드 통과.
- UI 실패1(테두리/대비/HUD/배너)과 실패2(square 카드 폭/버튼 줄바꿈)를 보존하고 재검토했다. 최종 캡처는 모두 런타임 UI이며 붙여 넣은 목업이 아니다.
- 화면 캡처는 무적/가속 등 진단 입력을 포함하므로 정상 완주·실기 성능의 증거가 아니다. M4까지 사용자용 APK를 전달하지 않는다.
- [ADR0031](../adr/0031-first-playable-ui-state-and-local-preferences.md), [실행 런북](../runbooks/phase1b-playtest.md).

## M4 독립 준비와 남은 실기 관문

[PR108](https://github.com/hyunlord/bs-mobile/pull/108)은 독립 준비 [#109](https://github.com/hyunlord/bs-mobile/issues/109)를 처리한다. 전체 M4 [#98](https://github.com/hyunlord/bs-mobile/issues/98)는 계속 열린다. 연결된 폴드7의 설치·입력·수집·재생·녹화는 사용자 재개 지시까지 보류하며, APK 생성이나 Editor 검사를 기기 관문으로 대신하지 않는다.

- 기존 진단 raw 두 개의 Editor 가져오기는 실제 통과했다(601/603 CPU 프레임). 첫 가져오기에서 이름 없는 샘플의 null 오류를 확인하고 수정했으며 원본과 실패를 보존했다. 모든 샘플을 남기고 중첩 시간은 합산하지 않는다.
- 첫 진단의 유일한50ms 초과 CPU 프레임은76.373ms로, `Gfx.WaitForPresentOnGfxThread`가66.416ms였다. 대기의 GPU·드라이버·스케줄링 원인은 미확인이다. 혼잡 진단의 단일 체크포인트는21.546ms였지만 해시/파일 쓰기 비용을 분리해 입증하지는 않았다.
- 혼잡 진단은2배속·프로파일러 켜짐이며 CPU p95는17.131ms였다. 1배속 관문과 직접 비교하지 않는다. 이후 사전 선언한 첫60초 시도는 카드 일시정지를 관측했고, 전체 원자료의 최종 분석은 연결 보류로 미완료다. 유리한 행을 골라 통과시키지 않는다.
- `RuntimeSystem`은 현재 trigger/operation/subject와 무관한 효과를 객체 생성·정렬 전에 제외한다. 캐시나 규칙 변경 없이 현재 스택·태그 조건·정렬·중첩 이벤트·기여 기록을 보존했다. 새 회귀4개를 포함한 Core353개가 통과했다.
- 같은 워밍업 뒤 .NET 합성 표본2000회에서 무관한 효과128개의 할당량은51,648,000→1,568,000바이트였다. 대표 게임 판이나 IL2CPP 성능 개선량은 아니며, 실기 측정은 남아 있다.
- 엄격한 연속60초 분석기·출처 결합·실패 보존·명시적 미확인 자세를 검증했다. 자세 `unknown`은 물리 접기를 추정하지 않으며500/16.7ms 기준을 완화하지 않는다. [ADR0032](../adr/0032-device-performance-evidence.md).

최적화 소스 `cbeea0ffd7316d9f4be4061ca2e39c44b6a8d2cc`에서 Unity EditMode45/45·PlayMode19/19와 실제 Release 컴파일의 개발 코드 배제가 통과했다. EditMode는 .NET이 기록한30000–30004 seed의27000틱 전체를 실제 Mono에서 재생한다. 저장된 과거 진단 실기 기록도 최적화한 CLI에서 틱9000·Quit 해시가 일치했다. 이 기록은 정상 완주가 아니다.

같은 소스의 전체 저장소 관문은 [CI37868284536](https://github.com/hyunlord/bs-mobile/actions/runs/37868284536)에서 통과했다. 로컬 최초 전체 검사는 Unity 준비와 동시 실행해 공유 DLL 경로가 사라지는 오류로 실패했으며 실패 로그를 보존했다. 두 검사는 순서대로 실행해야 한다.

Development APK60,073,016바이트와 Release APK43,895,396바이트가 ARM64 IL2CPP로 빌드됐다. Release 패키지는 debuggable 플래그·재생 fixture가 없고, 실제 Android 빌드의 stripped assembly에서 게임 개발 도구 타입이 제외되었다. 패키지 메타데이터의 커밋과 소스 해시는 검사 소스와 일치한다. 두 파일은 로컬 검증 산출물이며 사용자용 Release로 게시하지 않았다.

최종 Android5seed 비교·최적화 후500+ 실측·정상3판·실제 영상2개·사용자 APK 설치 확인은 아직 통과하지 않았다. #109의 병합은 준비 작업의 완료이며 #98의 통과 선언이 아니다.
