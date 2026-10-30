# M3a r7 승인 부지·큰길 낮 검증 (2026-10-26)

관문: 부분. Unity 네 suite assertion 실패 0, 배치 예산 통과, native 건설은 부모 건물 미완공으로 거부. 화면 최종 승인은 별도다. #386·#390 초안 유지.

## Unity tests
commit: df080e1ee541085bc9fe88432de764329707a173
first-playable EditMode: passed 846, failed 0, skipped 25
first-playable PlayMode: passed 41, failed 0, skipped 44
wave-1a EditMode: passed 856, failed 0, skipped 15
wave-1a PlayMode: passed 72, failed 0, skipped 13
skipped in every profile: 2 unexplained, 1 listed

- 하네스 종료1은 기존 공통 조건부 skip 2건 때문이다. 동결 M1 실제 fixture sidecar 부재, opt-in nearest-enemy catalog 부재이며 예외를 추가하지 않았다. CI의 통과와 로컬 전체 하네스 통과를 구별한다.
- 최종 두 PlayMode 실행 시간 범위로 집계한 일반 캡처255건, 청록0. 의도적 negative control 제외. 셰이더 대기/시간초과/전후 상태 감시는 유지한다. 원인 확정이 아니라 현재 관측이다.
- 실패 보존: 5661d58 configure에서 NUnit 컬렉션 `Does.Not.Contain` 오버로드 컴파일 오류 1회, ce4e73d wave PlayMode에서 임시 조각4개를 기대한 낡은 assertion 1회. 각각 수정. b2fe6e9 재실행은 남은 옛 층 assertion을 정적 발견해 configure 단계에서 자체 중단, 테스트 결과로 세지 않는다. 최종 df080e1 네 suite 실패0.

## 그림·길 계약

승인 Release `m3-lv0-sites-review-20261026`의 6종×밝음/어둠과 길 straight/bend/fork512를 기존 terrain atlas에 넣었다. 부지272×192, 바닥128×64, pivot(0.5,0.375), 표시폭136. 15개 새 원본의 RGBA와 SHA256, 밝음/어둠 알파 일치 검사2/2, 기존 아틀라스 검사6/6. 기존 15개 terrain 그림도 RGBA 불변이다.

부지 ID 순서로 이웃이 쓰지 않은 그림을 배정한다. 완공된 ID도 배정을 유지해 곡창 완공 후 경작지 그림이 바뀌지 않는다(회귀 검사). 빛의 둥근 거리로 lit/dark를 고른다. 장식 off에서도 필수 부지는 남는다.

r7 길은 집 앞 광장을 지나는 한 큰길과 문으로 가는 짧은 가지다. 시작 상태41칸·40간선, 연결10문, 고리0, 부지 침범0. 서쪽은 경작지를 피해 1칸 우회한다. 동쪽은 다리 **방향**까지이며 닫힌 땅의 실제 다리까지 이어진 길을 주장하지 않는다. r3~r6의 기존 길 셀은 보존한다. 풀 바닥은 Astra r4 전까지 기존 그림을 유지한다.

## 묶음 기여도

| 설정 | 장식 on 기본+지형지물=전체 | off 기본+지형지물=전체 | Lv0 추가분 on/off |
|---|---|---|---|
| r3 | 12+2=14 | 10+2=12 | 2/1 |
| r4 | 12+2=14 | 10+2=12 | 2/1 |
| r5 | 12+2=14 | 10+2=12 | 2/1 |
| r6 | 12+2=14 | 9+2=11 | 2/1 |
| r7 | 10+2=12 | 8+2=10 | 0/0 |

이 수치는 UI Graphic의 연속 material/texture 구간 구조 계수이며 GPU drawcall 측정과 동일시하지 않는다. 실제 라벨과 `DecorEnabled` 일치도 검사한다. `CheckBatchBudget`가 기록한 CSV와 각 Graphic 순서로 재현한다.

이전 80a558b의 r7 off 기본10은 집 몸체 앞뒤로 정렬되는 임시 Lv0 standing 조각이 원인 후보였다. 당시 Graphic별 trace가 없어 +2 전체의 귀속은 미확정이다. 이번 실행에서는 승인 부지가 `Estate ground / estate-terrain` 묶음에 합쳐졌고, 제외 재계산에서도 Lv0 추가분0과 off 기본8을 확인했다. 새로운 아틀라스 수는0. r7 off 기본8의 순서: DefaultUI/white → ground/terrain → tint/decor(veiled) → DefaultUI/white(lamps) → DefaultUI/night → tint/building-shadows → tint/buildings → tint/decor(paddock). 지형지물 terrain/decor가 각각1구간을 더한다. on의 +2는 yard-ground/decor와 standing roadside/decor다.

r7 실제 예산은 base on17/off9 및 landmark≤4를 만족한다. Lv0는 이번 구성에서 추가0. r6 예산은 수정하지 않았다. 역사 r3~r5 값은 보존만 한다.

## Core·저장·리플레이

- Core targeted AccountEstateR7MapTests/WaveEstateEffectTests 25/25. r3~r6 설정 SHA256 고정과 r7 설정으로 역사 저장을 다시 쓰지 않는 검사, r6 경제 동등성 통과. Core 규칙/저장 버전6 변경 없음.
- 두 Unity 프로필의 역사 재생 검사 통과. 기본 data 트리 변경 없음(opt-in r7 제외).
- 별도 crowd411 입력: Release `movement-feel-2cabae7`의 `rules-off-six-minute-failure-8c94ce2.ssreplay`, SHA256 `4fe0f97f23d20b487730be03ae76f5d7551ec88b787d43566f0cde41cd5a079c`. seed30001, tick11530 종료 `59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619` 일치.
- 위 crowd는 ADR0058 역사 provenance 경로로 현재 catalog + 녹화 헤더 data hash를 사용했다. 녹화 data hash `51DA41A20DF37E4960AFB157AC7AA83D20C9AB1FC7E6B5E6D85643BC9C438B74`, 현재 `6AC1ACFC4B2E80DD510EE0A41D0039BC05932A5FEF1776CC1B2A4BF2E928C36D`는 다르다. 일반 CLI의 data-hash 일치로 표현하지 않는다. 입력 파일/종료 기대값을 변경하지 않았다.

## Native 1회

wave-1a ARM64 Mono Development 빌드 성공(554821095bytes). 격리된 새 계정, 자원 지급 없음. 첫 진입0/1/30은 각각 실제 player frame271/272/301, 834×1806, foreground=true, cyanPixels=0. Editor 셰이더 상태는 Standalone에서 NA다.

경작지 UI 건설 1회는 `config=m3a-prototype-r7 building=8 reason=parent-required`로 거부돼 앱 종료2. 부모 곡창이 Lv0인 fresh 상태라 규칙대로 거부됐다. 실패를 성공으로 바꾸지 않고 추가 재시도하지 않았다. Editor 건설/완공 화면은 곡창부터 정상 순서로 지은 뒤 테스트 전용 시간 진행을 사용한 증거이며, native 건설 성공 영상이 아니다.

## 화면 판정과 증거

[Release 비교4장·가장자리 확대·native 첫진입 및 거부 로그](https://github.com/hyunlord/bs-mobile/releases/tag/m3a-r7-approved-sites-day-20261026).

독립 검토2건: 낮은 부지/길 패치 alpha/가장자리 기초는 개선. 큰길의 시각 위계가 약하고 마당 테두리와 섞임. 기존 밭의 크림색 받침, 공사 표지 가독성, 반복 강굽이는 남은 한계다. 도로 구조 검사 통과를 사용자 화면 승인으로 대신하지 않는다. 키아트 C 비교는 구성·화풍 참고이며 픽셀 동일 복제 판정이 아니다.

공유 Mac 자기 잠금으로 순차 실행. Unity·앱 종료 후 자기 잠금을 해제했다. $MV와 야간 프로세스는 잠금 파일 외 쓰지 않았다. 원자료는 자기 worktree `artifacts/unity-tests-approved-sites-final2`, `artifacts/m3a-r7-native-day`, `artifacts/m3a-r7-day-review`. C05는 직접 돌리지 않았다.
