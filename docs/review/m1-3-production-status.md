[![3방식 걷기·비례 표본](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-combat-908e3e3/walk-12frames.png)](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-combat-908e3e3/three-methods-1x.mp4)

관문: **부분** — 3방식 비교 영상은 게시했지만 사용자 선택은 대기다. 3D의 각진 얼굴·갑옷과 단순한 머리카락은 품질 한계로 남는다.
revision7 모션3안과 실제 전투8종·24단계 촬영은 완료했다. 15개 자산의 게시·원격 SHA 일치를 확인했으며 사용자 선택은 대기이며 실제8종 결합 네이티브 성능은 실패했다.
M1-2 실패, PR255·257 초안과 보류 항목을 유지한다.

| 항목 | 현재 확인 | 판정의 한계 |
|---|---|---|
| 최신 모션3안 | `908e3e3`·revision7, 같은 입력30초60fps·실제 소리·1배/2배·시트 조립 완료 | 걷기·베기 관찰; 피격·쓰러짐은 이 입력에 없어 N/A. 게시·원격 SHA 확인 완료 |
| 비례 | 원화53/441=0.12018, 원화±10% 우선 | 대략1:7을 실측으로 대체하지 않음; 숫자는 미술 승인 아님 |
| 3D | revision7 후면 얼굴 UV 수정,64클립·2,000장 동결·atlas 가장자리 검사 통과 | 각진 미술 스타일 미승인; revision6 실패 보존 |
| 2D | AI 생성 독립 자세, 일부 방향 반전 | 수작업 원화 아님; 검 쥔 손도 반전, 미제작 동작 존재 |
| 실제 무기8종·24단계 | 7종 `2ef07ca`, 창은 발 위치 수정 `908e3e3`; 촬영 완료 | 8종 영상 모두 게시·원격 SHA 확인; 사전 구성 가설·무기별 크기/판독 한계 유지 |
| 최신 결합 스트레스 | `2dfad7e` Editor45 통과; combined-v3 실제 두 DLL 검증·집중7검사 | retry2 성능 실패; 초기 혼합 인자/예외 이력 보존 |
| CI | 최신 `2dfad7e` 실행38331619723 통과 | CI 통과와 결합 성능 실패를 별도로 판정; 이전 실패 보존 |
| 800+ 네이티브 비용 | 아래4표본 모두 >25ms 0건 | 각1회·약30초·60fps 상한, 원인 개선율이나 #223 종료 증거 아님 |

candidate9의 `3799374`는 검토 데이터 Lv1 적 상한24만 적용했다. 실제 두 Core DLL의 C05+24사례 일치는 역사 검사로 보존한다. 첫 이미지·영상은 `908e3e3`·revision7이며 [최신 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m1-3-combat-908e3e3)의15개 자산을 게시·원격 SHA까지 확인했다. [이전 중간 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m1-3-methods-ed83d5f)의 revision6 자료는 역사 기록으로 보존한다.

별도 **저작 시연**은7동작×8방향·21초·무음으로 표기했다. 실제 판의 사건을 발생시킨 영상이 아니므로 정상 플레이 피격·쓰러짐 증거를 대신하지 않는다. 실제 판 비교 provenance는 `artifacts/m1-256/heroes-revision7-908e3e3/assembled/production-review-provenance.json`이다.

combined-v3는 실제8개 공격원 모두 양수 피해 사건을 기록했고 두 대상의 재생 결과 `C5050C0EE8DBF8837865CFBE83E7C59113109C0439BDE89B8BA9EB851CE5510F`가 같다. 정본은 [집계](../../benchmarks/m1-256-showcase/combined-review.csv)·[발동/피해](../../benchmarks/m1-256-showcase/combined-activations.csv)·[실제 DLL](../../benchmarks/m1-256-showcase/combined-targets.csv)·[조건과 재생성](../../benchmarks/m1-256-showcase/combined-review.md)이다. 결합 네이티브 retry2는 혼합 CLI 인자를 바로잡아 실행을 마쳤다. 초기 원인은 혼합 인자였고 `SaveKnightReview` 예외가 이를 가렸다. 실제 캡처 앱은 `2dfad7e`이며 후속 오류 처리 수정은 포함하지 않는다. 982프레임의 p95 33.3361ms·최대53.7928ms·>25ms 20.9775967%로 성능 관문은 실패다.

## revision7 최신 네이티브 표본 · 2dfad7e

| 방식 | 장 p95(ms) | 최대(ms) | >25ms |
|---|---:|---:|---:|
| 관절 기준선 | 17.1222 | 22.6251 | 0건 |
| 3D revision7 | 17.0450 | 22.9780 | 0건 |
| 2D | 17.5042 | 22.9120 | 0건 |

세 방식 네이티브 표본은 완료했다. [최신 정본CSV](../metrics/m1-256/native-cost-revision7.csv)와 실행 출처를 연결하며 미검증 메모리·추가 비율을 추정하지 않는다. 아래 revision6 실적과 별도 실행이다. 세 표본의 차이를 제작 방식의 원인 개선율로 해석하지 않으며 실제8종 결합 결과도 아니다.

## 실제8종 결합 네이티브 · 실패

| 실제 앱 | 프레임 수 | 장 p95(ms) | 최대(ms) | >25ms 비율 |
|---|---:|---:|---:|---:|
| 2dfad7e / corrected CLI retry2 | 982 | 33.3361 | 53.7928 | 20.9775967% |

모션3방식의 >25ms0건과 다른 부하다. 최대50ms·>25ms 비율 관문을 만족하지 않는다. 실제 네이티브 기록은 `all8Activated=true`·`replayVerified=true`, 두 DLL과 같은 `C5050…CE5510F` 해시, 8개 공격원 모두 양수 피해를 확인했다. 처치167·처치 경험치835·성장 보상0이다. 전체 적909–1,000과 화면 안 적143–163은 다른 지표이며 화면 안800+ 표본은0이다. [비용 분해CSV](../metrics/m1-256/combined-cost-breakdown-revision7.csv)의 Core p95 17.2974ms·최대36.1806ms, 화면 구성 p95 4.6484ms·스냅샷 p95 0.2562ms는 Core가 큰 비용 후보임을 보여 주지만 근본 원인을 확정하지 않는다. 이 결과로 #223을 닫거나 사용자 시각 승인을 대신하지 않는다.

## revision6 역사 성능 표본 · revision7 측정 아님

3D revision6을 포함한 깨끗한 Release `3a94a09`에서 같은 C05·카메라1.4배·기존60fps 설정으로 각1회 약30초를 측정했다. 전경 유지로 인정된 프레임만 집계한다. 모든 표본의 적 수 중앙값846·p95/최대876이며 C05 최종 해시 `B056B090…DE8AEE8`가 일치한다.

| 표본 | 프레임 수 | 장 p95(ms) | 최대(ms) | >25ms 건수/비율 | Unity 할당 p95(MiB), 표본 수 |
|---|---:|---:|---:|---:|---:|
| 관절 기준선 | 1789 | 17.5392 | 23.5031 | 0 / 0% | 212.18 (29) |
| 3D 구움 | 1792 | 17.5751 | 23.0714 | 0 / 0% | 175.12 (30) |
| 2D 그림 | 1796 | 16.8530 | 22.9024 | 0 / 0% | 174.87 (30) |
| 8종 View 효과 동시 | 1796 | 17.5673 | 22.3528 | 0 / 0% | 211.50 (30) |

Unity 할당 바이트는 프로세스 RSS·GPU 메모리가 아니다. `productionTextureBytes=0` 기록은 **미계측**으로 취급하며 텍스처 비용0을 뜻하지 않는다. 표본별 관리 힙 p95·메모리 표본 수·전체 소스/입력/최종 해시는 [정본 CSV](../metrics/m1-256/native-cost.csv)에 남겼다. 캡처를 끈 네이티브 실측이지만 1회·60fps 페이싱으로 방식 간 원인 개선율이나 장기 회귀 해소를 판정하지 않는다. 수집 불가 스키닝 마커도0으로 대체하지 않는다.

**8종 동시는 현재 쇼케이스의 View 전용 오버레이 효과 부하만** 더한 것이다. 24사례의 실제 Core 전투 모듈을 동시에 실행한 결과가 아니며, 후속 실제 결합 retry2는 위와 같이 성능 실패했으며 이 역사 View 표본과 혼합하지 않는다. 별도 [combined-v3](../../benchmarks/m1-256-showcase/combined-review.md)는 실제8개 공격원을 결합해 두 DLL의 재생·사건·밀도 바이트 일치까지 검증했으며 이 View 표본과 구분한다. 전경 관측 로그에는 Orca·게임 및 일부 조회 불가가 기록됐다. 포함/제외는 외부 앱 이름 관측 시각이 아닌 네이티브 전경 판정에 따른다. 네 표본에서 긴 장이 없었다는 결과를 무기 영상의 시각 실패나 사용자 승인으로 바꾸지 않는다.

`benchmarks/m1-256-showcase/evolved-activation-kills.csv`의 발동별 처치는 궤도·포자 등의 **발동 수명 전체 누적**일 수 있다. 한 프레임의 동시 처치나 화면 안 처치 수가 아니다. 미귀속 소환 타격도 별도이며, 이 숫자로 “수십 마리를 쓸어가는 장면”을 통과시키지 않는다. Lv1/max/evolved는 사전 구성한 검토 가설이고 정상 성장으로 획득한 진화가 아니다. 적 피해1 등 검토장 수치는 production 변경이나 #126 확정이 아니다.

제작시간의 실측/추정과 라이선스·실패 경계는 [제작 비교 정본](../design/m1-3-production-comparison.md), 실제 사건 집계는 [정본 CSV 설명](../../benchmarks/m1-256-showcase/README.md)을 따른다. [#223 야간 기록](https://github.com/hyunlord/bs-mobile/issues/223#issuecomment-6136065887)은 진단 재생2회의 해시 일치를 확인했으나 기록기 overflow가 있어 Release ABBA는 미완료다. 또한 #126·#98·1b·#224 보류와 SDK 미도입은 유지한다. 새 영상의 사용자 승인 전 시각 통과를 선언하지 않는다.

[영상1배](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-combat-908e3e3/three-methods-1x.mp4) · [영상2배](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-combat-908e3e3/three-methods-2x.mp4) · [베기 시트](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-combat-908e3e3/slash-light-12frames.png) · [최신 Release·8종 영상](https://github.com/hyunlord/bs-mobile/releases/tag/m1-3-combat-908e3e3) · [무음 저작 시연](https://github.com/hyunlord/bs-mobile/releases/download/m1-3-combat-908e3e3/seven-motions-eight-directions-authoring-preview.mp4)

[CI 통과 소스2ef07ca](https://github.com/hyunlord/bs-mobile/commit/2ef07ca) · [후속 검토 데이터3799374](https://github.com/hyunlord/bs-mobile/commit/3799374) · [PR257 초안](https://github.com/hyunlord/bs-mobile/pull/257) · [CI 통과](https://github.com/hyunlord/bs-mobile/actions/runs/38260157868) · [역사 CI 실패](https://github.com/hyunlord/bs-mobile/actions/runs/38256971906)

[최신2dfad7e CI 통과](https://github.com/hyunlord/bs-mobile/actions/runs/38331619723)
