> **2026-10-13 사용자 최종 판정: 실패.** AI 생성 2D 프레임 방식은 중단한다. 프레임마다 비율·디테일 변화, 중간 그림 부족에 따른 자세 끊김, 피격 시 전신 빨강 약1초가 이유다. 아래 부분 판정은 당시 이력이며 사용자 판정을 대체하지 않는다. 결과물·원본·실패 영상은 보존하고 PR261은 초안을 유지한다. 후속은 [M1-5 #262](https://github.com/hyunlord/bs-mobile/issues/262)의 기성 리그 모델 선택부터 시작한다.

[초반 2배 비교 · 걷기 판정 부적합 표본](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/hero-walk-slash-2x.mp4) · [피격·쓰러짐 2배 비교](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/hero-hit-death-2x.mp4)

[철날 검 · 3단계 30초](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/core-iron_blade-three-stages.mp4) · [연쇄 번개](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/core-storm_fork-three-stages.mp4) · [뿔나팔](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/core-muster_horn-three-stages.mp4)

관문: **부분** — 선택된 2D 영웅으로 무기 8종·24단계를 촬영해 게시했다. 사용자 영상 승인은 대기다.
독립 8방향·5동작 40클립 232자세를 사용했으며 작은 채도 차이는 남는다. 3D 굽기는 재개하지 않았다.
성능 측정은 사용자 요청으로 보류 중이다. 이번 오프라인 영상·테스트를 성능 통과로 쓰지 않는다.

| 항목 | 확인한 결과 | 남은 한계 |
|---|---|---|
| 영웅 | 서기·걷기·베기·피격·쓰러짐, 독립 8방향 40클립 232자세 | 원화별 작은 팔레트 차이; 전체 자연스러움은 사용자 판정 |
| 무기 영상 | 실제 앱 `0bd300c`, 24/24 재생·소리·해시 검증, 8종 각각 30초·60fps 게시 | 검토용 사전 구성 Lv1/최대/진화; 정상 성장 획득 아님 |
| 집중 검사 | 81/81 통과 | 집중 검사와 네이티브 영상 검증은 다른 증거; 전체 CI 대체 아님 |
| 철날 검 진화 | 같은 틱에 뷰포트 안 중심점의 실제 처치 최대 32 | 픽셀 노출·몸 전체가 보였다는 판정 아님 |
| 연쇄 번개 진화 | 같은 기준 실제 처치 최대 30 | 같은 틱·화면 중심점 기준이며 시각 승인 아님 |
| 뿔나팔 | 넓은 파동과 실제 아군 공격, 같은 틱 최대 4처치 | 수십 동시 처치를 달성했다고 쓰지 않음; 픽셀 면적 비율 미산정 |
| 그림 메모리 | 비압축 RGBA 명목 114.876MiB | 런타임 상주량·RSS·GPU 실측 불가; 실제 비용으로 대체하지 않음 |
| 전후 영웅 비교·시트 | 30초 초반과 346장 피격·쓰러짐, 원본 프레임·PCM·종료 해시 일치 | 초반 입력의 매 틱 반전으로 자연스러운 걷기 판정 부적합; 실제 조준 시트는 별도 |
| 성능·원격 CI | 모든 네이티브 시간 측정 보류; bfc568a CI38364897770 진행 중 | 역사 실패와 새 후보를 섞지 않음 |


## 초반 비교의 입력 원인과 관문 한계

초반 C05 비교의 프레임540–549에서 보이는 S/N 전환은 원본 입력 틱266–278의 `(0,+1000)`/`(0,-1000)` 매 틱 반전과 연결됐다. 이를 표시 코드 결함으로 단정하지 않으며, 가리기 위한 View 방향 지연0.1초를 추가하지 않았다. **이 초반 비교는 일정한 걷기의 자연스러움 승인 증거로 부적합**하다. 실패 표본과 원본 입력은 보존한다. 예측·보간·카메라가 위치 변화의 크기에 기여하는 정도는 이번 조사에서 분리하지 않았다. 프레임·PCM·해시 일치는 이 시각 증거의 결함을 상쇄하지 않는다.

별도의 실제 무기 영상8종은 `square-walk-v2` 입력을 사용한다. 각각30초 영상과 연속 최근접 조준 시트를 게시했으며 초반 C05의 반전 입력과 혼동하지 않는다. 입력 종류가 다르므로 두 영상의 적 처치·방향 변화를 동일 입력 전후 효과로 비교하지 않는다. 남은 팔레트 차이와 사용자 시각 판정, 측정 보류는 그대로다.

실제 캡처 앱은 `0bd300cbde6d46aaf61d91a7417ad3fb77ca0169`, 사용한 아트 manifest SHA256은 `133469231867d9a227d4ea7c51ad4948c556c2e3474fa63d4903a0d7206e2888`이다. 정본 요약은 `artifacts/m1-258/final-weapons-v2-assembled/summary.json`이며 `performanceEligible=false`, `visualApproval=false`를 유지한다. 철날 검 조준 연속 표본은 실제 관측 범위의 증거이며8방향 모두의 승인으로 확대하지 않는다.

초기 머리 로드 null 실패는 보존했다. 수정된 `0bd300c`와 명시적 미측정 메타데이터로 재촬영한 결과만 위24회에 포함한다. 이후 `bfc568a`는 아트 프롬프트 ID6개의 gitleaks 오탐 대응이며 로컬 변경 범위 탐지0건·독립 검토와 원격 CI를 구분한다. 후속 문서·오탐 수정 커밋을 실제 영상 앱 커밋으로 바꿔 표기하지 않는다.

PR255·257·261은 초안이다. 사용자 영상 승인 전 병합 승인이나 시각 통과를 가정하지 않는다. #126·#98·#224·1b 보류와 SDK 미도입을 유지하며 #223을 이번 영상 결과로 닫지 않는다. [M1-4 계약](../design/m1-4-frame-hero-impact.md)의 불변 결합 기준선·최종 후보 분리와 성능3회 관문은 측정 재개 뒤 별도 검증한다.

[활](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/core-canal_bow-three-stages.mp4) · [창](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/core-ditch_pike-three-stages.mp4) · [곡창](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/core-granary_sling-three-stages.mp4) · [포자](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/core-spore_fan-three-stages.mp4) · [수호 고리](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/core-ward_orbit-three-stages.mp4)

[Release·아트 원본](https://github.com/hyunlord/bs-mobile/releases/tag/m1-4-frame-0bd300c) · [영상 앱0bd300c](https://github.com/hyunlord/bs-mobile/commit/0bd300c) · [PR261 초안](https://github.com/hyunlord/bs-mobile/pull/261)

[베기 연속12장](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/hero-slash-contiguous12.png) · [피격 연속12장](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/hero-hit-contiguous12.png) · [쓰러짐 연속12장](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/hero-death-contiguous12.png) · [실제 최근접 조준 연속12장](https://github.com/hyunlord/bs-mobile/releases/download/m1-4-frame-0bd300c/core-iron_blade-evolved-actual-aim-contiguous12.png)

초반 새 영웅의 실제 기록은 서기72장·걷기1058장·베기670장으로 8방향이 관측됐다. 피격 비교의 새 영웅은 걷기61·베기75·피격119·서기30·쓰러짐61장이다. 이것은 모든 방향의 모든 동작이 시각 승인을 받았다는 뜻이 아니다. 종료 후60장은 Core 틱을 고정한 쓰러짐 표현이며 프레임을 늘려 성능을 꾸미지 않는다.

[후속 bfc568a CI · 확인 시 진행 중](https://github.com/hyunlord/bs-mobile/actions/runs/38364897770)
