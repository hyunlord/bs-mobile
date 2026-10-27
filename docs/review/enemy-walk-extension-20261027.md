# Enemy walk extension — 2026-10-27

관문: 부분 — 승인 원판 RGBA·crop·pivot·timing 및 정적 검증 통과. Unity 두 프로필·실제 영상·해시·묶음 계측·CI는 아직 실행하지 않았다.

## Integration

| Profile / role | Frames | frameMs | Source cell / runtime packing | Pivot | World size |
|---|---:|---:|---|---|---|
| wave / enemy.rig.raider.walk |16|26|256, inset4/248UV; 8×2 source appended y3328–3840|.5383064516,.1209677419|.6453105882 square|
| first-playable / enemy.core.stone_breaker.walk |16|45|full256; 8×2, 260 pitch,2px padding,2080×520 atlas|.5,.12109375|.5590062111 square|

Release raider-r2-spriteskin-16.png SHA256 af97e336fabc88a6412f51b47b6602e13be4968b5cf0b127dd0f2647bf699126; stone-breaker-r3-16.png SHA256 74867a8cbd4034e041f4b487ffd411ef411306ae6ec9b5c94ac856a6460e16a2. Source approval: enemy-walk-audit-raider-r2-20261023 and m3-walk-next-review-423-20261027. Their pending labels predate the latest approval.

Shared atlas previous 2048×3328 →2048×3840, every prior RGBA pixel unchanged. Stone adds one first-playable texture; combining all into the shared atlas would exceed4096 height. Runtime stone full cells reconstruct the exact approved source RGBA without cropping/resampling. First-playable atlas count17→18; wave34 unchanged. Draw-call change is **not measured**.

No runtime code change was needed. first-playable resolves core walk roles directly; its ActorMotion.SampleEnemy(Pursuer) is already identity. Wave uses EnemyRigAtlas and variable frame counts. First-playable raider/shield/ram, including historical non-humanoid ram identity, remain unchanged. Stone is not selected by wave data. All idle/hit/death roles remain unchanged, so the walk-to-idle appearance transition still needs real visual review.

## Verification

- Test-first Node contract failed before integration: raider8 !=16; passed afterward.
- importer --source / --check: approved file hashes, reconstructed RGBA, historical atlas preservation, padding passed.
- test-approved-extensions.mjs: timing/pivots/sizes/profile isolation/max4096 passed.
- first-playable-art.mjs:18atlases/372roles/343bindings, registry passed.
- wave1a-art.mjs:34atlases/493roles/415bindings, registry passed.
- verify-unity-boundaries.mjs:9assemblies passed; view-coverage.mjs both profiles missing0.
- Independently compared manifests to prior HEAD: all roles except the two authorized walks, bindings and licenses unchanged.
- Unity EditMode tests updated but **not executed**. No Unity, build, Core tests, hash run, native capture or performance measurement in this preparation lane.

Parent runs actual first-playable stone scene and wave raider/shield/ram before/after at equal ticks/scale, Unity suites, replay hash check, and reports final CI. C05 remains Claude Code night queue only. No merge authorization implied.
