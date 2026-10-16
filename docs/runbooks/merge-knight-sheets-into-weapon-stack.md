# 기사 시트(#290)와 #266 무기 스택 합치기

관련: #268, #290(기사 전신 스프라이트 시트, main 기준), #255 ← #257 ← #261 ← #267 ← #285 ← #286 ← #287 ← #288 ← #289 ← #291 ← #294(#266 무기 스택).

2026-10-17에 검토 장면 영상(무기 고정 영웅 동작)을 찍으려고, 스택 꼭대기 `feat/266-ricochet-min-hop`(`a6037b5`)에 `feat/268-knight-sprite-sheets`(`d86a9e8`)를 합친 임시 브랜치 `review/266-268-composite`(병합 커밋 `bae68cc`)를 만들었다. 이 브랜치는 PR로 올리지 않는다. 아래 충돌과 해결은 실제로 스택을 main에 합칠 때 그대로 쓴다.

당시 스택과 main의 공통 조상은 `bc4e14b`(#245)였다. main은 4커밋(#250, #260, #275, #277) 앞섰고, 스택은 199커밋 앞섰다.

## 충돌 파일과 해결

| 파일 | 원인 | 해결 |
|---|---|---|
| `unity/Assets/Game/View/WorldHeroRig.cs` | 스택: 라이브 기사 리그(`PrepareHeroKnight`, 2bf5295)·움직임 비교·제작·컨셉 검토 경로와, #267 임시 대체(`heroRigRolesMissing`, `TODO #268`). main(#290): `heroRigActive` 한 줄 판정 | `HeroVisual`은 컨셉 검토 그림을 먼저 돌려준다. 그다음 `if(!heroRigActive){heroGait.Reset();heroLiveRig?.Hide();return Resolve("hero",heroId,state);}`이다. **#267 임시 대체를 지우고** `hero.preview-b.*`를 `ResolveRole`로 바로 찾는다(컷아웃은 명시 요청일 때만 오고, 그 role은 wave-1a에만 있다). `HeroRigRolesMissing` 속성도 지운다. `DrawHeroRig`는 제작 검토·컨셉 검토 확인 뒤 `if(!heroRigActive){heroLiveRig?.Hide();return false;}` |
| `unity/Assets/Game/View/HeroSheet.cs`(자동 병합 뒤 수정) | main의 `SelectHeroRig`는 B 리그 설정만 안다 | 스택의 검토 모드를 명시 요청으로 본다. `explicitReview = HeroRigExplicit \|\| HeroMotionPreviewMode != 0 \|\| productionHero != null \|\| heroConceptVisual != null`, `heroSheetOn = !explicitReview && EnsureHeroSheet(heroId)`, `heroRigActive = !heroSheetOn && (heroKnightEnabled \|\| HeroRigPreviewEnabled \|\| HeroMotionPreviewMode != 0)`. `DrawHeroSheet`·`HeroFigureSize`는 `heroSheetOn`으로 판단한다. `DrawHeroSheet`에 `float? flashOverride`를 더해 스택의 피격 밝기를 받는다. 테스트용 `HeroSheetDrawsHero` 속성을 둔다 |
| `unity/Assets/Game/View/WorldRenderer.cs`(카메라) | 스택: 비교 모드에서 앵커 중앙. main: 영웅 크기를 몸 크기로 | 둘 다: `HeroFigureSize(...)` + `FallowActive && HeroMotionPreviewMode == 0` 앵커 |
| `unity/Assets/Game/View/WorldRenderer.cs`(영웅 그리기) | 스택: `heroHitFeedback`의 2프레임 밝힘·밀림(`heroFlash`, `heroDrawn`), `PlaceBowstringFlashes`. main: 시트 경로·붉은 외곽선 | 스택 블록을 유지한다. 그림자·밀림 폭은 `HeroFigureSize(...)`로 바꾸고, `DrawHeroRig(...)&&!DrawHeroSheet(snapshot.HeroId,heroDrawn,...,HitFeedbackEnabled,current.TickRate,heroFlash*.3f)`로 시트를 끼운다. 전신 붉은 칠은 쓰지 않는다(사용자 결정 2026-10-17) |
| `unity/Assets/Game/View/WorldWave.cs` | 스택: 뿔나팔 병사·제작 공격·번개/검 지연 피격 깜빡임. main: `OfferHeroAction` | 스택 줄 전부 + `if(value.Kind=="attack"\|\|value.Kind=="hit"\|\|value.Kind=="review-impact-tell"\|\|value.Kind=="review-impact"\|\|value.Kind=="chain-link")OfferHeroAction(value,value.Tick);` |
| `unity/Assets/Game/App/RunCoordinator.cs` | 스택: `ConfigureHeroMotionComparison`·타격감 비교 | 스택 블록 + `world.HeroRigExplicit=explicitHeroRig;` |
| `unity/Assets/Game/App/RunCoordinator.WaveBenchmark.cs` | 스택: 검토 장면 `activeHitFeedback`, `PresentWorld` 앞의 카메라·소리·쇼케이스 | 스택 줄 + `explicitHeroRig=activeHeroRig;`, `PresentWorld` 바로 앞에 `world.HeroLocomotionPerTick = (float)(Session?.LocomotionSpeedPerTick ?? 0);` |
| `unity/Assets/Art/wave1a-manifest.json` | 양쪽이 역할·아틀라스를 추가 | **스택 쪽을 받은 뒤** `node tools/art-authoring/knight-sheets/pack.mjs <승인 Release 폴더> <추가분 Release 폴더>`로 영웅 role을 다시 등록한다(같은 픽셀·해시가 나오는지 확인). 손으로 합치지 않는다 |
| `unity/Assets/Resources/Wave1aArt.asset` | atlas 목록 끝 | 스택의 atlas 두 줄 뒤에 `hero-knight`(guid `fdfeddd402e34c31baf3bdb2985b54d0`)을 둔다. 매니페스트 atlas 순서와 같아야 한다 |
| `docs/design/gdd/06-feedback.md` | 양쪽이 절 추가 | 스택 절 뒤에 main의 "영웅 전신 스프라이트 시트" 절을 둔다(둘 다 유지) |
| `unity/Assets/Game/App/RunCoordinator.Smoothness.cs`(충돌 없음, 병합 뒤 수정) | main은 라이브 캡처가 아니면 `world.HeroSheetPoseSampled = null`로 둔다. 스택의 M1 검토(`M1CombatReview.ObserveFeel`)가 먼저 붙인 자세 기록이 그 뒤에 지워져 `m1-hero-poses.csv`가 비었다(임시 병합 `0e05039`) | `if (AutoplayCapture.Active != null) world.HeroSheetPoseSampled = ObserveHeroPose;` — 라이브 캡처일 때만 덮어쓴다(임시 병합 `3d8a39f`) |
| `unity/Assets/Game/Tests/PlayMode/WeaponFeelWorldTests.cs`(자동 병합 뒤 수정) | #267 임시 대체 테스트가 `[hero-art] ... missing ... #268` 경고와 `HeroRigRolesMissing`을 기대한다 | `FirstPlayableKnightDrawsTheSheetsAndTheWorld`로 바꾼다. 경고를 기대하지 않고(`LogAssert.NoUnexpectedReceived`), `world.HeroSheetDrawsHero`가 참인지 본다 |

## 병합 뒤 남는 문제

- **ADR 번호 충돌**: main의 `0057-local-artifact-hygiene`, `0058-data-driven-evolution-unlocks-and-card-offers`와 스택의 `0057-view-only-impact-and-hero-motion-comparison`, `0058-observational-damage-and-audible-knight-review`가 겹친다. 스택 쪽 0057~0063을 main 다음 번호(0059~)로 다시 매기고, 문서·PR 안의 참조를 함께 고친다.
- **view-coverage(#275)**: 임시 병합에서 3건이 실패했다.
  1. 스택이 바꾼 `WorldRenderer.cs`의 적 리그 줄(`if(ArtCatalog.ProfileName=="wave-1a")enemyRigAtlas=...`)을 인용한 증거
  2. 스택의 `WorldHeroConceptReview.cs`가 `hero.preview-b.idle`을 문자열로 쓰는데, first-playable에 그 role이 없다. 컨셉 검토는 wave-1a 전용이므로 그 파일을 wave-1a 범위로 둔다.
  3. `tools/fixtures/view-coverage.expected.json` 갱신(`node tools/view-coverage.mjs --write`)
  - `tools/view-coverage.json`의 #268 범위 증거는 병합 뒤 `WorldHeroRig.cs` 줄(`heroLiveRig?.Hide()` 포함)과 `HeroSheet.cs`의 `SelectHeroRig` 본문으로 갱신한다.
- **아트 레지스트리 직렬화**: 손으로 고친 `FirstPlayableArt.asset`은 Unity가 빌드 설정 단계에서 `audioManifest: {fileID: 0}`을 더해 다시 저장한다. 그러면 작업 트리가 더러워져 Mac 빌드가 "Generated Unity bridge is stale"로 멈춘다. Unity 직렬화 형태로 커밋한다(#290의 `f821cd0`).

## 확인

- 임시 병합(`bae68cc`): EditMode `WeaponPresentationPilotTests|M1CombatArtBindingTests|HeroSheetTests|ArtContractTests` 42/42. 아트 검사: first-playable 17 atlas, wave-1a 33 atlas, 레지스트리 일치.
- 실제 병합 때는 위 view-coverage·ADR 번호를 고친 뒤 `tools/check.sh`, `tools/check-unity.sh`(EditMode·PlayMode는 준비 프로필에 묶인 테스트가 있어 first-playable·wave-1a 두 번 준비해 확인)를 돌린다. #268 수용 관문(first-playable Mac 앱 예외 0건·월드 그려짐·PlayMode 7건)도 다시 확인한다.
