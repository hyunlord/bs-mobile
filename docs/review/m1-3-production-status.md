[![3방식 걷기·비례 표본](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-methods-ed83d5f/walk-ratio-12frames.png)](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-methods-ed83d5f/three-methods-1x.mp4)

관문: **부분** — 3방식 비교 영상은 게시했지만 사용자 선택은 대기다. 3D의 각진 얼굴·갑옷과 단순한 머리카락은 품질 한계로 남는다.
무기 candidate4는 실제 처치가 화면 밖에서 일어나 시각 관문에 실패했다. candidate5 전용 검토장을 준비 중이며 완료로 세지 않는다.
M1-2 실패, PR255·257 초안과 보류 항목을 유지한다.

| 항목 | 현재 확인 | 판정의 한계 |
|---|---|---|
| 모션3안 | `ed83d5f`, 30초·고정60fps 비교 1배/2배와 비례 표기 12장 시트 게시 | 오프라인 렌더; 실시간 성능·사용자 시각 승인 아님 |
| 비례 | 원화53/441=0.12018, 원화±10% 우선 | 대략1:7을 실측으로 대체하지 않음; 숫자는 미술 승인 아님 |
| 3D | 프로젝트 메시·생성 표면·CC0 기성 모션 | 각진 형태·단순 망토; 원화 품질에 도달했다고 주장하지 않음 |
| 2D | AI 생성 독립 자세, 일부 방향 반전 | 수작업 원화 아님; 검 쥔 손도 반전, 미제작 동작 존재 |
| 무기24사례 candidate4 | 실제 Core 피해·처치·해시 자료 존재 | 화면 밖 처치로 영상 목적 실패; candidate5 준비 중 |
| 최신 소스 검사 | `3a94a09` 관련 Unity 집중32개 통과 | 이전76·1·56검사와 합산하지 않음; 전체 CI 통과 아님 |
| CI | 실행38256971906 형식 검사 실패, 수정 중 | 실패 보존; 수정 후 실행 결과 대기 |
| 800+ 네이티브 비용 | 아래4표본 모두 >25ms 0건 | 각1회·약30초·60fps 상한, 원인 개선율이나 #223 종료 증거 아님 |

## 캡처 없는800+ 네이티브 표본

깨끗한 Release `3a94a09`에서 같은 C05·카메라1.4배·기존60fps 설정으로 각1회 약30초를 측정했다. 전경 유지로 인정된 프레임만 집계한다. 모든 표본의 적 수 중앙값846·p95/최대876이며 C05 최종 해시 `B056B090…DE8AEE8`가 일치한다.

| 표본 | 프레임 수 | 장 p95(ms) | 최대(ms) | >25ms 건수/비율 | Unity 할당 p95(MiB), 표본 수 |
|---|---:|---:|---:|---:|---:|
| 관절 기준선 | 1789 | 17.5392 | 23.5031 | 0 / 0% | 212.18 (29) |
| 3D 구움 | 1792 | 17.5751 | 23.0714 | 0 / 0% | 175.12 (30) |
| 2D 그림 | 1796 | 16.8530 | 22.9024 | 0 / 0% | 174.87 (30) |
| 8종 View 효과 동시 | 1796 | 17.5673 | 22.3528 | 0 / 0% | 211.50 (30) |

Unity 할당 바이트는 프로세스 RSS·GPU 메모리가 아니다. `productionTextureBytes=0` 기록은 **미계측**으로 취급하며 텍스처 비용0을 뜻하지 않는다. 표본별 관리 힙 p95·메모리 표본 수·전체 소스/입력/최종 해시는 [정본 CSV](../metrics/m1-256/native-cost.csv)에 남겼다. 캡처를 끈 네이티브 실측이지만 1회·60fps 페이싱으로 방식 간 원인 개선율이나 장기 회귀 해소를 판정하지 않는다. 수집 불가 스키닝 마커도0으로 대체하지 않는다.

**8종 동시는 현재 쇼케이스의 View 전용 오버레이 효과 부하만** 더한 것이다. 24사례의 실제 Core 전투 모듈을 동시에 실행한 결과가 아니며, 그 조합의 비용 관문은 미검증이다. 전경 관측 로그에는 Orca·게임 및 일부 조회 불가가 기록됐다. 포함/제외는 외부 앱 이름 관측 시각이 아닌 네이티브 전경 판정에 따른다. 네 표본에서 긴 장이 없었다는 결과를 무기 영상의 시각 실패나 사용자 승인으로 바꾸지 않는다.

`benchmarks/m1-256-showcase/evolved-activation-kills.csv`의 발동별 처치는 궤도·포자 등의 **발동 수명 전체 누적**일 수 있다. 한 프레임의 동시 처치나 화면 안 처치 수가 아니다. 미귀속 소환 타격도 별도이며, 이 숫자로 “수십 마리를 쓸어가는 장면”을 통과시키지 않는다. Lv1/max/evolved는 사전 구성한 검토 가설이고 정상 성장으로 획득한 진화가 아니다. 적 피해1 등 검토장 수치는 production 변경이나 #126 확정이 아니다.

제작시간의 실측/추정과 라이선스·실패 경계는 [제작 비교 정본](../design/m1-3-production-comparison.md), 실제 사건 집계는 [정본 CSV 설명](../../benchmarks/m1-256-showcase/README.md)을 따른다. #223 야간 일정과 #126·#98·1b·#224 보류, SDK 미도입은 유지한다. 새 영상의 사용자 승인 전 시각 통과를 선언하지 않는다.

[영상1배](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-methods-ed83d5f/three-methods-1x.mp4) · [영상2배](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-methods-ed83d5f/three-methods-2x.mp4) · [베기 시트](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-methods-ed83d5f/slash-light-ratio-12frames.png) · [Release](https://github.com/hyunlord/bs-mobile/releases/tag/m1-3-methods-ed83d5f)

[소스3a94a09](https://github.com/hyunlord/bs-mobile/commit/3a94a09) · [PR257 초안](https://github.com/hyunlord/bs-mobile/pull/257) · [CI 형식 검사 실패](https://github.com/hyunlord/bs-mobile/actions/runs/38256971906)
