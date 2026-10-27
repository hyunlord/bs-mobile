# First-playable approved enemy walks (#429)

2026-10-27 · v4 초안 · 관문: 부분 — 5종 두 프로필·추가 시험·종료 해시 통과(4749000). 실제 비교 영상 확보·그리기 호출 회귀 미해결·최종 CI 대기. e0c2c06 기록은 3종 당시 역사 기록으로 보존.

| Content / role suffix | 승인 Release | frames × frameMs | pivot | worldSize |
|---|---|---|---|---|
| core:stone_breaker / walk | m3-walk-next-review-423-20261027, r3 | 16 × 45 | .5, .12109375 | .559006211093322 |
| core:rope_catcher / walk | m3-rope-ember-walk-review-425-20261027, r1 | 16 × 40 | .5, .12109375 | .5031055899839898 |
| core:ember_sapper / walk | same, r1 | 16 × 40 | .4609375, .12109375 | .5217391303537672 |

사용자가 승인한 기존 프로젝트 그림을 Unity SpriteSkin으로 구운 Release다. 기존 generated-actors 출처/상업 이용 계약을 유지하며 CC0로 재분류하지 않는다. 후보 manifest의 과거 pending 문구보다 사용자 승인 지시가 우선한다. Ember 심지 깜박임·공격 모션은 범위 밖이다.

## Packing and verification

- 원본 PNG는 ignored `artifacts/429/{stone,rope-ember}`에 보존. 재현 다운로드 경로·원본 byte SHA256·RGBA SHA256은 `tools/art-authoring/enemy-rigs/first-playable-approved-walks.json`에 기록.
- `node tools/art-authoring/enemy-rigs/import-first-playable-walks.mjs`로 생성. 기존 PNG decode/encode 함수를 재사용한다. 4096×256 원본마다 256×256 셀 16개를 그대로 복사하여 2080×1560 공통 atlas에 넣는다. 2px 투명 gutter, 축소/재색칠/inset 없음. Stone ATLAS-CONTRACT의 낡은 inset4 계약은 사용하지 않는다.
- `--check`: 원본 다운로드 없이 committed atlas에서 원본 RGBA를 역조립하여 3종 SHA 일치, 48칸 전체 RGBA, gutter alpha0, 크기/피벗/frameMs/role 계약 확인. 통과.
- `node tools/first-playable-art.mjs`: 18 atlases, 372 roles, 343 bindings, required329, registry18 통과.
- `node --test tools/first-playable-art.test.mjs`: 16/16 통과. Unity boundaries assemblies9 통과.
- 기본 a15abfc 대비 역할 변경은 위 walk3개뿐, binding/license 그대로. wave1a manifest 그대로, fallow-enemy-rigs LFS 원본 SHA256 `5a69aaf13a67ea6885234be74e4d984e00b7b77323f113024b15e05ebebf8bde` 일치. 처음 git show byte 비교는 LFS pointer와 실파일을 비교해 실패했고 oid 비교로 바로잡았다.

## Actual runtime path and pending gates

`ArtCatalog.Load` → first-playable이면 `Resources/FirstPlayableArt` → `ArtCatalog.Resolve("enemy", DefinitionId, state)`. `WorldRenderer`는 위치가 바뀌고 hit/death가 아니면 walk를 선택한다. first-playable에서는 wave가 null이므로 `EnemyRigAtlas.TrySample`를 거치지 않는다. `Draw`가 `floor(time*1000/frameMs)%16`으로 UV를 선택한다. 기존 idle/hit/death 역할과 wave 걷기는 변경하지 않는다. 이동거리 기반 stride를 새로 발명하지 않는다.

first-playable은 ActorMotion.SampleEnemy에 Pursuer 종류를 넘기며 이 분기는 offset=0/scale=1/rotation=0이다. 전체 sprite 흔들림을 추가하지 않고 좌우 facing만 유지한다. 승인 그림의 상태 전환 크기(특히 stone의 역사 idle 대비 약6px 기준점 차이)와 1배 before/after 영상은 Unity에서 확인해야 한다. 셀 몸 크기와 투명 여백을 자르지 않았으므로 승인 worldSize를 그대로 사용한다.

`FirstPlayableApprovedWalkTests`는 실제 Resource registry와 catalog binding으로 3종16칸·시간·pivot·worldSize·idle/hit/death 별도atlas를 검사한다. 세 테스트는 두 프로필 EditMode에서 통과했다. Core/data/save 규칙은 변경하지 않았으며, 아래 frozen replay 실행을 별도로 기록한다.

공통 atlas 1장 추가(18←17), raw RGBA 약12.38MiB. batching/프레임 성능 미측정. 추가 텍스처가 Y 정렬 중 묶음 수에 미치는 영향은 parent runtime 기록 대상. 승인 전 병합하지 않는다.

## Unity tests
commit: e0c2c06a0a11e81096c3db6000fd600fa45184ae
first-playable EditMode: passed 860, failed 0, skipped 25
first-playable PlayMode: passed 53, failed 0, skipped 45
wave-1a EditMode: passed 870, failed 0, skipped 15
wave-1a PlayMode: passed 85, failed 0, skipped 13

추가 EditMode 실행(같은 tested commit): combat 1/1, melee 1/1, 각각 failed0/skipped0. 원래 네 XML만의 skip 집계는 unexplained2/listed1로 stock runner exit1이었다. 누락된 opt-in fixture를 정상 생성한 후 실제 두 테스트를 실행했고, 여섯 XML 합산 결과는 아래와 같다. 원래 suite 숫자를 더하거나 예외를 새로 만들지 않았다.

skipped in every profile: 0 unexplained, 1 listed

- combat: M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes (생성 sidecar 준비 후 통과)
- melee: Tests.EditMode.MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures (opt-in catalog/replay 준비 후 통과)
- 기존 listed skip: Tests.PlayMode.WaveWorldTests.RecordedWaveRangeExportsActualTickFrames — Optional recorded-input sequence export.
- 근거: artifacts/day-20261027/pr429-tests/{record.md,combat.xml,melee.xml,combined-skip-record.txt,combined-skips.txt}. 전체 runner 종료 및 자기 잠금 해제 2026-10-27 12:02:45 KST, exit0.

청록: 이 실행 시간대에 남은 실제 capture sidecar 188개 모두 cyanPixels=0. 로그의 CYAN_CAPTURE_FAILURE 2건은 각 프로필에서 guard-self-test-negative-control-True가 의도적으로 1픽셀을 주입하고 Assert.Throws로 검출을 확인한 자체 시험이다. 실제 화면 실패에서 제외하며 숨기지 않는다. 같은 self-test 경로는 덮어쓰기되므로 보존 sidecar는 True/False 각1개지만 로그 실패는 프로필당1개다.

리플레이: 기존 frozen 입력 artifacts/day-20261027/replay-proof/first-playable-30000.ssreplay를 interactive-replay로 재검증했다. 입력 SHA256 61aa591ea8f05dc1ec7b30968652df654aa423d824b786ede6145915fdc183ba, Seed30000/Tick27000/EndKind0, StateHash 69E7182A9FB4EBF154E85551F2A2C6696F8EFBD30A87EC3C1046DF593361CDB5. 기록된 checkpoint/end hash 검증 성공이며, 별도 옛 fixture의 44049CB5…와 입력이 다르므로 서로 비교하지 않는다. 무적 correctness fixture이며 정상 플레이 성능 증거가 아니다.

실제 전/후 비교 영상·적별 가시 프레임·묶음 수·CI 최종 결과는 대기다. 위 검증을 사용자 영상 승인으로 확대하지 않는다.

## 사용자 승인 추가: bell/whistle r1 (#432)

Release `m3-bell-whistle-walk-review-428-20261027`의 과거 pending 문구는 최신 사용자 승인으로 대체된다. bell_thief와 whistle_hunter 각16×40ms, 셀256×256, 바닥(128,225), pivot(.5,.12109375). worldSize는 각각 .5124223602484472 / .4145962732919255이다. 승인된 손–종/휘파람–창 연결 그림은 그대로이며 가려진 입술/뒤 손목은 확인 불가 한계로 유지한다.

공통 atlas를2080×1560에서2080×2600으로 아래쪽 확장했다. 기존48칸과 모든 기존gutter RGBA가 byte동일하다. 위쪽48칸 rect/pivot/time 계약도 동일하고 새32칸만 추가했다. 원본PNG는 artifacts/429/bell-whistle에 보존. atlas 개수18은 그대로, rawRGBA 약20.63MiB(이전12.38MiB에서+8.25MiB), Unity maxTextureSize4096·비압축·NPOT그대로 설정은 유지한다. rawPNG파일과 UV정규화값은 높이 변경으로 달라지지만 기존셀 RGBA/픽셀rect는 불변이다.

정적 검사: 5종80칸 sourceRGBA 재구성 SHA 모두일치, 기존48칸+gutter byte동일, 이전HEAD대비 역할변경은 bell/whistle walk2개만, binding불변. first-playable-art registry18/roles372/bindings343 통과, Node16/16, boundaries9, diffcheck통과. 테스트 case2개를 추가했으며 새전체 Unity·실제영상·리플레이 검증은 아직 실행하지 않았다. 기존 3종 기록을 재사용해5종통과로 주장하지 않는다.

## Five-species runtime verification (2026-10-27)

정확한 tested commit: 47490004986a112a5fd10be1d07e05d3b45179cc. 5종 승인 원본 적용 및 오프라인 captureFramerate 테스트 helper 포함. 아래 결과는 위 e0c2c06의 3종 역사 실행과 별개다.

| Profile | Suite | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| first-playable | EditMode | 862 | 0 | 25 |
| first-playable | PlayMode | 53 | 0 | 45 |
| wave-1a | EditMode | 872 | 0 | 15 |
| wave-1a | PlayMode | 85 | 0 | 13 |
| supplemental | combat EditMode | 1 | 0 | 0 |
| supplemental | melee EditMode | 1 | 0 | 0 |

네 기본 XML은 skipped-in-every-profile unexplained2/listed1이므로 stock 기록을 그대로 보존한다. 정식 생성 fixture로 combat/melee 두 테스트 실행 후 6 XML 합산 unexplained0/listed1. 기존 listed는 Tests.PlayMode.WaveWorldTests.RecordedWaveRangeExportsActualTickFrames(선택적 영상 export). 예외 추가나 suite 합산 수치 변조 없음. 전체 runner exit0/14:12:16 KST는 parent 실행 기록이며 이 검토는 XML·리플레이·sidecar readback이다.

청록: 실행 시간대 UTC05:01–05:13에 보존된 sidecar190개 중 실제 캡처188개 전부 cyan0. 자체검사2파일 제외. 두 프로필 로그의 CYAN_CAPTURE_FAILURE 각1건은 guard-self-test-negative-control-True가 의도적으로 주입한 1픽셀 검출 시험이며 실제 화면 실패가 아니다. 공유 자체검사 파일은 마지막 프로필 결과로 덮어써졌으므로 파일 수와 로그 수가 다르다. cyan-audit.json에 분리 기록.

리플레이 frozen61aa591ea8f05dc1ec7b30968652df654aa423d824b786ede6145915fdc183ba 재검증: Seed30000/Tick27000/EndKind0, StateHash69E7182A9FB4EBF154E85551F2A2C6696F8EFBD30A87EC3C1046DF593361CDB5. 이 fixture의 저장 checkpoint0; 종료 해시 검증이며 정상플레이/성능 증거 아님.

근거: artifacts/day-20261027/pr432-five-tests/{record.md,combat.xml,melee.xml,combined-skip-record.txt,combined-skips.txt,replay-hash.txt,replay-input.sha256,cyan-audit.json}. 검토 시 tracked git status clean.

남음: 실제 30초 기존/신규1배 영상, 5종 각각 실제 다리 가시성·전환 판독, 최종 CI quality/secrets 결과. CPU성능/C05 통과 주장 없음.

## Actual five-species offline comparison (#432)

관문: 부분 — 기술·실제 프레임 증거 확보, **그리기 호출 회귀 미해결**, 사용자 영상 판정 대기. 같은30초 drawCalls는 before14–17(평균15.4667) → after43–94(평균66.8789). +332.4% 평균 호출 수 증가는 숨기지 않으며 성능 통과로 보고하지 않는다.

[30초 영상·선정된5장 Release](https://github.com/hyunlord/bs-mobile/releases/tag/fp-five-enemy-walks-432-20261027). BEFORE왼쪽390×844 / AFTER오른쪽390×844, 합계780×844,60fps,1800frames,30s. 무음 world-only 실제 기록 debug-input이며 정상 플레이·성능 측정이 아니다. seed30000, input61aa591e…,tick19891–20790. baseline ff453e83918e68db5db63974b01c161e6399823e / candidate47490004986a112a5fd10be1d07e05d3b45179cc. 전후 모든1800장의 tick/stateHash/appliedCommands/alpha/camera 동일, 원본390×844 PNG 헤더 확인. 별도 capture PlayMode는 각1/1, 실패0·skip0, CYAN_CAPTURE_FAILURE 로그0.

| 종류 | 실제 표본 frame / tick / ID | 화면에서 확인한 것 | 한계 |
|---|---|---|---|
| stone_breaker | 940 / 20361 / 24631 | 상단 왼쪽에서 두 장화 자세 변화 | 근처 동종과 일부 겹침 |
| rope_catcher | 1670 / 20726 / 25874 | 상단 왼쪽 밧줄 인물의 다리 교대 | 옆 피해 숫자 일부 |
| ember_sapper | 50 / 19916 / 24515 | 하단 중앙 불씨 인물의 다리 자세 변화 | 후속+4/+8프레임 피격 숫자·효과, 그 구간을 순수 걷기 증거로 쓰지 않음 |
| bell_thief | 1010 / 20396 / 24957 | 상단 오른쪽 종 인물의 장화 교대 | 표본 주변은 몸·다리 읽힘 |
| whistle_hunter | 550 / 20166 / 24947 | 하단 중앙 작은 창 인물의 다리 벌림/모임 | 작은 크기에서 세부는 제한 |

선정 프레임과 ±4/±8 실제 캡처를 직접 보았다. 확대 내부 검토물은 운동 위상 확인 보조이며 배포5장은 리사이즈 없는1배 전체화면 전후다. centreFrames는767/661/879/981/804로 전후동일하지만 가림·걸음 증명과 구별한다. 다섯 종 동시노출을 주장하지 않는다.

원인 후보: EnemyDrawQueue.cs:49는 Y painter order로 정렬하고 :54는 연속된 같은 Texture만 합친다. first-playable 신규walk는 별도atlas, 기존 적·idle/hit/death는 역사atlas라 Y순 A/B/A가 여러묶음이 될 수 있다. 이는 코드상 가능한 기전이며 해당실행의 texture-run계측으로 인과를 확정한 것은 아니다. 정렬/아틀라스 재포장 변경은 범위 밖이라 하지 않았다.

작은 정본 [초별 호출표](first-playable-walks-429-drawcalls.csv)는 실제 sidecar1800쌍의60frame 구간 min/mean/max 집계다. 전체PNG/raw sidecar는 로컬보존, ZIP/중복전체업로드 없음. 원자료경로 artifacts/day-20261027/fp-five-offline-20261027-1414. 최종CI는 PR체크에서 확인하며 사용자승인 전 draft유지.

## 2026-10-27 통합 텍스처 재검증 — source 1602d79

관문: 호출 수 통과(+0%), 표본 시각 검사 통과. 사용자 영상 판정·최종 CI는 별도 확인한다. 앞 절의 15.47→66.88(+332.4%) 실패와 기존 CSV/Release는 역사 기록으로 보존한다.

전용 atlas를2080×2600에서2080×3200으로 확장해 역사 적 고유13crop(47역할)을 기존 신규80칸과 함께 패킹했다. FP 적52역할이 같은Texture2D를 쓰며 Y순서는 그대로다. 역사13crop은 각 원본 RGBA SHA-256 및 복사 결과의 바이트 비교로 보호한다. 신규80칸 RGBA, 모든 적의 pivot/worldSize/frameMs/셀 크기, 비적 역할·bindings, 공유actors.png/wave/Core/data는 보존했다. 전체actors 파일 해시는 고정하지 않아 무관한 승인 문장 변경을 허용한다.

비압축 RGBA32 산술 메모리는20.63→25.39MiB, **+4,992,000바이트(+4.76MiB)**이다. 실제 런타임 메모리 측정치가 아니다. `import-first-playable-walks.mjs --check`, 재import 바이트 동일성, Node 구문·diff 검사는 통과했다.

### 실제 Unity 실행 기록

정확한 tested source: `1602d79d4151340c2860f157057e6088558adce2`.

| 프로필 | Suite | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| first-playable | EditMode | 863 | 0 | 25 |
| first-playable | PlayMode | 53 | 0 | 45 |
| wave-1a | EditMode | 873 | 0 | 15 |
| wave-1a | PlayMode | 85 | 0 | 13 |
| supplemental | combat EditMode | 1 | 0 | 0 |
| supplemental | melee EditMode | 1 | 0 | 0 |
| after 오프라인 캡처 | PlayMode | 1 | 0 | 0 |

기본4 XML root는 `Skipped:Ignored`, failed0이다. 기본 기록의 공통 미설명skip2/listed1을 그대로 보존하며, 기존 생성 도구의 combat/melee 입력을 보충 실행한6 XML 합산에서는 미설명0/listed1이다. listed는 선택적 `WaveWorldTests.RecordedWaveRangeExportsActualTickFrames`; 예외 추가 없음. 근거는 `artifacts/day-20261027/pr432-single-texture-tests/{record.md,*Mode.xml,combat.xml,melee.xml,combined-skip-record.txt,combined-skips.txt}`이다. 최종 보충 XML은17:51:10 KST 종료, 새 캡처 XML은17:55:22 KST 종료다.

### 같은 30초 호출 수

| 렌더 소스 | 평균 호출 | p95 호출 | 최대 호출 | 기준 대비 |
|---|---:|---:|---:|---|
| baseline ff453e8 | 15.4667 | 17 | 17 | 기준 |
| 분리 atlas 4749000 | 66.8789 | 84 | 94 | +332.4%, 실패 보존 |
| 통합 atlas 1602d79 | 15.4667 | 17 | 17 | +0%, ≤20% 통과 |

이는 **그리기 호출 수**이며 프레임 시간 p95·성능 통과가 아니다. 기존before를 그대로 재사용하고after만 다시 찍었다. 두 실행의 tick19891–20790,1800프레임,입력SHA `61aa591ea8f05dc1ec7b30968652df654aa423d824b786ede6145915fdc183ba`,종료해시 `69E7182A9FB4EBF154E85551F2A2C6696F8EFBD30A87EC3C1046DF593361CDB5`가 일치한다. 프레임별 stateHash·tick·alpha·appliedCommands·카메라 위치/배율·submittedInstances·enemyIds/enemyKinds/viewport-centreIds·buildingStates도1800/1800 일치한다.

기존 EnemyDrawQueue는 Y순의 연속 동일Texture만 묶는다. 분리atlas에서 새walk와 역사idle/hit/death·다른적이 A/B/A로 갈렸으며, baseline→분리atlas의 queue/material/renderer 코드 변경은 없다. 같은 장면에서 단일texture로 묶은 결과 호출이 기준선으로 돌아와 이 원인 설명을 뒷받침한다. Y순서 변경으로 해결하지 않았다.

새 작은 정본은 [통합 후 초별 호출표](first-playable-walks-429-unified-drawcalls.csv)다. 60프레임 단위 min/mean/max를 새before/after sequence.json에서 계산했다. 재생성 입력은 `artifacts/day-20261027/fp-unified-after-1602d79/{before,after}/walk/sequence.json`이며 초i는 frames[60i..60i+59]다. 이전 실패 CSV는 변경하지 않았다. 새 검증·영상 원자료는 같은 폴더의 `pair-validation.json`, `fp-five-unified-before-after-1x.mp4`에 있다. [새 Release 게시 대상](https://github.com/hyunlord/bs-mobile/releases/tag/fp-five-unified-432-20261027)은 root 게시 후 확인한다.

### 시각 검토와 한계

실제PNG 전후0·50·550·741·828·851·960·1162의8쌍을 직접 보았다. stone_breaker(741/851/960), rope_catcher(1162), ember_sapper(50/1162), bell_thief(741/828/851), whistle_hunter(550)의 주요 실루엣을 확인했다. 새 사각 배경·명백한UV오류·겹침 역전·비대상 그림 교체는 발견하지 못했다. 550의 반투명 적도 전후 유지되며,0/50/741 하단 단색 지도 경계는before에도 있는 기존 표현이다. **PASS는 표본의 atlas 통합 회귀 검사에 한정**한다. 큰 물결/불꽃의 가림, 작은 적의 세부 판독 한계가 있다. 정지 표본으로 걷기 자연스러움·전체프레임 무결점·사용자 승인을 대신하지 않는다.

영상 ffprobe 직접 확인: 780×844(각390×844),60/1fps,1800프레임,30.000초. 고정seed30000,debug명령 SetInvulnerable/GrantLevel 포함,world-only,Time.captureFramerate60 오프라인 렌더다. 정상플레이·실시간 성능 증거가 아니다.
