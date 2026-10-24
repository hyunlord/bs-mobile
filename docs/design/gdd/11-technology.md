# 11 기술·ADR 색인

- 버전: v1.4 · 날짜: 2026-10-24
- 바뀐 점: ADR0071(계정 영지 Core 상태와 저장 버전 6, #361)을 색인에 넣고 파일 수를 73개로 고쳤다. 이전 v1.3(2026-10-24): 무료 2D Animation의 승인·설치와 비교 시제품 상태를 적고(제품 영웅은 구운 시트), ADR 색인 수를 72개 파일로 고치고(v1.2까지의 "57개"는 고치지 않고 남은 옛 숫자) 0069·0070을 넣었다([#357](https://github.com/hyunlord/bs-mobile/issues/357) G08·G09). 이전 v1.2(2026-10-21): ADR 색인에 빠져 있던 0057–0064·0066–0068을 넣었다(무기 묶음 머지 뒤, #340). 이전 v1.1(2026-10-12): 살아 있는 설계 계약과 화면 전용 손맛·영웅 모션 비교 ADR을 연결한다.
- 상태: 정본 — 구현 경계는 확정, 성능·실기·미선택 제작 방식은 별도 관문.
- 근거: [AGENTS](../../../AGENTS.md), [시작 main](https://github.com/hyunlord/bs-mobile/commit/9387b7669bdc3d01790246509ad8d66f212af0ad), [기존 시스템 설계](../08_system-design-v1.md).

## 구현 경계

| 영역 | 정본과 계약 |
|---|---|
| 규칙 | [Core 프로젝트](../../../core/src/SowSiege.Core/SowSiege.Core.csproj): 순수 BCL, C#9, net8.0/netstandard2.1. 시각·소리·시스템 시각·Unity 의존 없음 |
| 입력·재생 | 같은 seed·콘텐츠·규칙 버전·입력 명령이면 같은 해시. 실제 DLL 두 타깃 검증을 Unity/IL2CPP 실기 통과라고 부르지 않음 |
| 화면 | [프로젝트 버전](../../../unity/ProjectSettings/ProjectVersion.txt) Unity6000.6.4f1, URP2D. Core 읽기 전용 스냅샷의 보간·예측·표현. 아트 교체로 규칙 변경 금지 |
| 데이터 | 실행 데이터와 designed-v1 별도. 공통 ID·designRef·primitives/params/bespoke 계약. 설계 레코드 존재를 실행 지원이라고 세지 않음 |
| 성능 | 풀·용량 사전 준비·변경된 HUD 값만 갱신. 매 프레임 할당 회피는 원칙, 실제 0할당/무끊김 주장은 계측으로만 증명 |
| 통합 | 이슈→PR→필수CI→병합. docs-only 판정기 완화 없음. #236의 운영 파일 변경은 문서 전용 PR과 분리 |
| 보안 | 광고·결제·분석 SDK 없음. 공개 저장소 DGX 러너 없음. 서명 키·비밀은 Git·보고에 남기지 않음 |

M1 손맛은 화면 반응만 바꾸고 Core 해시를 보존한다. 일반 적 처치의 전체 히트스톱 금지와 보스 전용 짧은 연출의 구체 처리는 손맛 문서에서 확정하고, 렌더 정지가 입력·Core 시간을 바꾸지 않는지 검증한다. 무료 `com.unity.2d.animation` 16.0.1은 [ADR0065](../../adr/0065-view-only-impact-and-hero-motion-comparison.md)로 영웅 한 명의 비교 시험에 승인됐고 [패키지 목록](../../../unity/Packages/manifest.json)에 설치돼 있다. SpriteSkin 실시간 뼈대 시제품([HeroLiveRig](../../../unity/Assets/Game/View/HeroLiveRig.cs))은 비교를 명시한 검토 실행에서만 쓴다. 현재 제품 영웅은 구운 옆모습 기사 시트다(#334·#337). 실시간 뼈대를 제품 영웅 방식으로 채택한 것이 아니며, 패키지의 빌드 크기·메모리·프레임 비용은 별도 실측 관문이다. v1.1의 "실시간 뼈대 도입이나 새 패키지는 아직 승인된 기술 결정이 아니다"는 당시 기록이다.

## 현행 결정과 역사 문구가 다른 곳

| 기록 | 현재 읽는 방법 |
|---|---|
| ADR0007/0008의 ZIP·전달 증빙 | ADR0016과 후속 보고 규칙이 우선. 새 증거 ZIP/CRC/영수증 금지. 승인된 Mac `.app` 배포 ZIP 예외만 유지 |
| ADR0018의 모듈 미설치·Unity 미착수 | 2026-10-08 검사 기록이다. 현재 설치·관문 상태를 그 문단에서 추정하지 않음 |
| ADR0026의 Fold 기기 | Fold7(SM-F966N)로 정정된 이력과 #98 보류가 현행. 다시 연결하지 않음 |
| ADR0039의 로스터 승인 대기 | 이후 ADR0041·#143 승인 범위와 wave-1a 실행 계약을 함께 읽음. 모든 설계 콘텐츠 출시 승인은 아님 |
| ADR0046의 Mac 성능 종료 | #169 당시 판정. 이후 #223의 최대≤50ms 실패를 덮지 않음 |
| ADR0048의 초기 화면 주사율·실시간 녹화 | 같은 문서의 후속 고른60fps·오프라인 렌더 결정, ADR0055의 지연 회복 틱 계약을 적용 |
| ADR0050의 A 기본·실험 모두OFF | 사용자 B 선택 및 [RunPreferences](../../../unity/Assets/Game/App/RunPreferences.cs)의 설정 버전 이관: 빠른 걸음·피격 표현 ON, 접촉·체력100 OFF. 사용자가 이후 저장한 선택은 보존 |
| ADR0051의 끝없는 들판 검증 중 | 사용자 544cc3d 검토에서 카메라 막힘0·귀환 수용. #223 끊김과 별개 |
| ADR0052~0054의 UI 검증 대기 | PR235 그림 승인·CI 병합은 후속 증거. BM 경제 수치나 실 결제 승인을 뜻하지 않음 |

ADR의 당시 상태·실패를 소급해서 성공으로 바꾸지 않는다. 아래 목록은 `docs/adr/`의 **73개 파일 전부**(번호 0001–0071, `0013`은 세 파일)의 탐색 색인이며 일괄 “채택됨” 표시가 아니다. 상태와 대체 범위는 각 ADR 본문 및 위 후속 결정을 따른다. `0013`은 정책 문서와 의존성 PR별 기록 두 개가 공존하므로 링크를 파일명까지 구별한다.

## ADR 전체 색인

### 제품·규칙·콘텐츠 모델

- [ADR 0001 — 모바일 Unity·F2P를 제품 경계로 고정](../../adr/0001-unity-f2p.md)
- [ADR 0002 — 순수 C# 코어와 실행 어댑터 분리](../../adr/0002-pure-csharp-core.md)
- [ADR 0003 — 콘텐츠와 조정 수치를 데이터로 분리](../../adr/0003-data-driven-content.md)
- [ADR 0004 — 기본 영웅·영지 각 하나, 모델은 복수 전제](../../adr/0004-one-default-extensible-model.md)
- [ADR 0005 — 판타지 표현과 일관된 인과](../../adr/0005-fantasy-consistent-causality.md)
- [ADR 0009: 데이터로 주입하는 월드와 통제된 헤드리스 실험](../../adr/0009-headless-world-and-controlled-experiments.md)
- [ADR 0010: 후보 풀과 실행 집합을 명시적으로 분리한다](../../adr/0010-candidate-pool-and-explicit-runtime-profiles.md)
- [ADR 0011: 실행 가능한 후보 효과와 재생 가능한 리그 원자료](../../adr/0011-executable-prototypes-and-auditable-league.md)
- [ADR 0014: 결과를 보기 전에 리그 판정 규칙을 고정한다](../../adr/0014-controlled-league-experiment.md)
- [ADR 0015: 처치 잔재를 위치에 보존해 나중에 심은 작물과 연결한다](../../adr/0015-local-remains-loop.md)
- [ADR 0019: 사람의 피해와 가로채기를 분리하고 무기 저생존을 진단한다](../../adr/0019-people-ablation-diagnostics.md)
- [ADR 0022: 무기 성장의 타격 전달을 데이터로 분리한다](../../adr/0022-weapon-level-tables-and-facing-sectors.md)
- [ADR0023 — 생산 데이터 정본과 역사 실험의 경계](../../adr/0023-production-data-and-experiment-boundary.md)
- [ADR 0029: 첫 플레이 가능판의 실행 계약과 역사 분리](../../adr/0029-first-playable-contract-and-historical-isolation.md)
- [ADR0034: 판 밖 상태와 한 판 규칙을 분리한 상한 있는 성장](../../adr/0034-bounded-meta-progression.md)
- [ADR0036: 설계 메타데이터와 실행 계약의 구분](../../adr/0036-content-metadata-without-runtime-redesign.md)
- [ADR0039: 실행 계약을 보존하는 개정별 설계 카탈로그](../../adr/0039-versioned-design-catalog.md)
- [ADR0041: 승인된 설계를 공통 동작 단위로 검증하고 별도 실행으로 연결](../../adr/0041-approved-design-primitives.md)
- [ADR 0042: wave-1a의 실행 경계와 동작 증거](../../adr/0042-wave1a-runtime-isolation.md)
- [ADR 0043: 기록된 한 판을 보존하는 공통 동작 조립](../../adr/0043-composed-wave-runtime.md)

### 도구·의존성·CI·증거

- [ADR 0006: 단일 코어 트리와 검증 도구](../../adr/0006-layout-and-tooling.md)
- [ADR 0007: 커밋별 지표를 소스와 분리해 영구 보존](../../adr/0007-durable-evidence.md)
- [ADR 0008: 커밋별 검사와 주석 규칙의 빈틈 닫기](../../adr/0008-complete-commit-evidence.md)
- [ADR 0012: 검증 원본을 보존하면서 작업 트리를 작게 유지한다](../../adr/0012-external-evidence-and-tree-budget.md)
- [ADR 0013: 주간 의존성 그룹과 검증 후 자동 병합](../../adr/0013-dependency-policy.md)
- [ADR 0013 appendix: dependency PR #239](../../adr/0013-dependency-pr-239.md)
- [ADR 0013 appendix: dependency PR #51](../../adr/0013-dependency-pr-51.md)
- [ADR 0016: 전달 비용을 줄이고 변경한 관문을 별도 판정한다](../../adr/0016-concise-evidence-and-retrospective-gates.md)
- [ADR 0017: 같은 규칙을 .NET 8과 Unity API 계약에서 검증한다](../../adr/0017-core-target-contracts.md)
- [ADR 0018: 설치된 Unity 6.6과 URP 2D를 다음 단계 기준으로 고정](../../adr/0018-unity-66-urp-baseline.md)
- [ADR 0020: Core 호환성 이후 테스트 SDK major를 독립 검증](../../adr/0020-test-sdk-18.md)
- [ADR 0021: SDK 검증 이후 VSTest adapter major를 독립 검증](../../adr/0021-xunit-adapter-4.md)
- [ADR0024 — Unity와 Core·정본 데이터의 생성 연결](../../adr/0024-generated-core-and-canonical-unity-bridge.md)
- [ADR 0027: 플레이어 소스 식별과 프레임 지표 분리](../../adr/0027-player-build-identity-and-frame-metrics.md)
- [0030 — 역할 기반 원본 아트 검증과 CI의 LFS 체크아웃](../../adr/0030-authored-art-validation-and-lfs-ci.md)
- [0032 — 실기 성능 진단과 관문 측정의 분리](../../adr/0032-device-performance-evidence.md)
- [ADR0033: 폰 연결과 독립된 맥 플레이판과 에디터 영상](../../adr/0033-mac-playable-and-editor-capture.md)
- [ADR0035: 권한 요청 대신 앱 내부의 명시적 Mac 자동 실행 증거](../../adr/0035-opt-in-native-mac-capture.md)
- [ADR0037: Publish a read-only Lattice content map](../../adr/0037-lattice-map.md)
- [ADR0038: 문서 전용 PR의 검사 범위](../../adr/0038-docs-only-ci.md)
- [ADR0040: 설계 카탈로그와 런타임 지도의 분리](../../adr/0040-layered-lattice-catalog.md)
- [ADR 0071: 계정 영지 Core 상태와 저장 버전 6](../../adr/0071-account-estate-state-and-save-version-six.md)
- [ADR 0070: quality job 제한 30분과 npm ci 3분 제한·1회 재시도](../../adr/0070-quality-job-time-limits.md)

### 입력·화면·성능·저장

- [ADR0025 — 수동 조작도 Core 명령과 이식 가능한 재생으로 기록](../../adr/0025-interactive-core-and-portable-replay.md)
- [ADR0026 — 세로 폴드 배치와 맥북 로컬 Unity 관문](../../adr/0026-portrait-fold-layout-and-local-unity-ci.md)
- [ADR 0028: Universal 2D 인스턴스 도형 표시](../../adr/0028-urp2d-instanced-graybox-rendering.md)
- [0031 — 첫 플레이 UI 상태와 로컬 표현 설정](../../adr/0031-first-playable-ui-state-and-local-preferences.md)
- [ADR 0044: 원본 입력을 보존하는 네이티브 고밀도 회귀 검사](../../adr/0044-fixed-native-wave-benchmark.md)
- [ADR 0045: 프레임 상한 대기와 실제 끊김의 판정을 분리](../../adr/0045-separate-pacing-and-stutter.md)
- [ADR 0046: 비재현 환경 지연을 기록하고 Mac 성능 작업을 종료](../../adr/0046-close-mac-performance-with-reproducible-stutter.md)
- [ADR 0047: 돌본 땅의 색은 게임 규칙이 아닌 성장 상태의 표현으로 둔다](../../adr/0047-state-driven-fallow-kingdom.md)
- [ADR 0048: 결정론 틱과 연속 표시·실시간 녹화를 분리한다](../../adr/0048-continuous-presentation-and-live-capture.md)
- [ADR 0049: 밀집 적의 몸을 결정론적으로 분리한다](../../adr/0049-deterministic-wave-enemy-separation.md)
- [ADR0050: 기본 규칙을 보존하는 이동·피격 실험](../../adr/0050-opt-in-movement-and-hit-feel.md)
- [ADR0051: 이전 재생을 보존하는 끝없는 휴경 들판](../../adr/0051-versioned-endless-fallow-field.md)
- [ADR0052: 세로 화면 흐름을 규칙과 분리한 표시 모드로 둔다](../../adr/0052-portrait-navigation-and-screen-contract.md)
- [ADR0053: 받기 원장과 wave 계정 기록을 전투 규칙에서 분리한다](../../adr/0053-explicit-meta-claims-and-wave-accounting.md)
- [ADR0054: 선택 보상을 재생 명령과 원자적 계정 지급으로 나눈다](../../adr/0054-opt-in-host-rewards-and-local-bm-slots.md)
- [ADR 0055: 정상 속도의 지연 회복 틱을 여러 표시 프레임에 나눈다](../../adr/0055-defer-recovery-ticks-between-rendered-frames.md)
- [ADR 0056: 구현 변경과 같은 PR에서 살아 있는 설계를 갱신한다](../../adr/0056-living-design-contract.md)
- [ADR 0057: 병합과 업로드가 끝난 로컬 산출물을 지운다](../../adr/0057-local-artifact-hygiene.md)
- [ADR 0058: wave 진화 해금과 카드 제시를 데이터로 정한다](../../adr/0058-data-driven-evolution-unlocks-and-card-offers.md)
- [ADR 0059: 검토 프로필의 근접 무기만 가까운 적을 향한다](../../adr/0059-opt-in-melee-aim-review.md)
- [ADR 0060: 프레임 영웅의 표시 계약과 해시를 보존하는 진단 도구](../../adr/0060-frame-hero-and-observational-profiling.md)
- [ADR 0061: 검토 합성 장판이 펄스마다 같은 적을 다시 칠 수 있게 하는 선택 옵션](../../adr/0061-review-patch-rehit-each-pulse.md)
- [ADR 0062: 검토 장면 수로 활 — 직선으로 날아가 적 사이를 튕기는 물화살](../../adr/0062-review-canal-bow-ricochet.md)
- [ADR 0063: 검토 장면 곡창 투석끈 — 자루가 떨어진 자리로 적을 모은 뒤 폭격](../../adr/0063-review-granary-sling-lure-bomb.md)
- [ADR 0064: 어둠 속 영지 — 영구 밝힘 구역과 잠든 건물의 Core 계약](../../adr/0064-dark-estate-awakening-buildings.md)
- [ADR 0065: 타격 시점과 영웅 제작 방식을 규칙 변경 없이 비교한다](../../adr/0065-view-only-impact-and-hero-motion-comparison.md) (처음 번호 0057, main과 겹쳐 다시 매김)
- [ADR 0066: 실제 피해 귀속과 소리 포함 기사 검토를 규칙 변경 없이 관측한다](../../adr/0066-observational-damage-and-audible-knight-review.md) (처음 번호 0058, main과 겹쳐 다시 매김)
- [ADR 0067: ArchitectureGuard는 저장소 루트 artifacts/의 생성 사본에서 콘텐츠 ID를 모으지 않는다](../../adr/0067-architecture-guard-ignores-generated-artifacts.md)
- [ADR 0068: unity/를 바꾸는 PR은 맥 Unity 전체 실행 기록이 있어야 병합한다](../../adr/0068-mac-unity-test-record-before-merge.md)
- [ADR 0069: opt-in 실험 데이터는 데이터 트리 해시 밖에 두고, 봇 실행도 프로필로 어둠 속 영지를 켠다](../../adr/0069-opt-in-experiment-data-outside-the-data-hash.md)
