# Enemy walk extension — 2026-10-27

관문: 부분 — #410 범위는 wave 약탈병·방패 약탈병·충차 돌격병 3종이다. first-playable stone breaker는 사용자 지시로 분리했다. 아래 정적 검증과 Unity·실제 영상·CI 판정은 구별한다.

## Integration

| Profile / role | Frames | frameMs | Source cell / runtime packing | Pivot | World size |
|---|---:|---:|---|---|---|
| wave / enemy.rig.raider.walk |16|26|256, inset4/248UV; 8×2 source appended y3328–3840|.5383064516,.1209677419|.6453105882 square|
| wave / enemy.rig.shield_raider.walk |16|40|256, inset4/248UV; 8×2 runtime y2304–2816|.5383064516,.1209677419|.7820694419 square|
| wave / enemy.rig.ram_runner.walk |16|40|256, inset4/248UV; 8×2 runtime y2816–3328|.5383064516,.1209677419|.66216 square|

Release sources: enemy-walk-audit-raider-r2-20261023 and enemy-walk-shield-ram-399-20261026. Approved bytes and reconstructed pixel hashes are pinned in approved-walks.json. Raider source SHA256: af97e336fabc88a6412f51b47b6602e13be4968b5cf0b127dd0f2647bf699126.

Shared wave atlas previous 2048×3328 →2048×3840; every prior RGBA pixel is unchanged. Wave atlas count34 is unchanged. First-playable stays at17 atlases and its stone/raider/shield/ram roles remain historical, including its non-humanoid ram identity. First-playable does not reference fallow-enemy-rigs. No runtime consumer, Core, data or capture helper changes in this scope split.

## Scope history

a910372 initially included approved stone breaker from m3-walk-next-review-423-20261027: full256 cells, 8×2 padded runtime2080×520, pivot(.5,.12109375), square size.559006211093322,45ms. Its approved source SHA256 is 74867a8cbd4034e041f4b487ffd411ef411306ae6ec9b5c94ac856a6460e16a2. That integration, source and earlier validations remain in git/artifacts as history; it is **not delivered in #410**. Its texture/registry entry is removed and historical first-playable stone role restored (actors, one239×233 frame,350ms,pivot(.5,.08),size(.430815,.42)). No artwork is regenerated.

## Verification

- Scope-regression test first failed: stone16 != historical1; passes after scope separation.
- importer --check: approved RGBA, historical/shared pixel preservation and padding.
- test-approved-extensions.mjs: wave timing/pivots/sizes and historical first-playable contract/max4096.
- first-playable-art.mjs / wave1a-art.mjs --check: manifest and registry validation.
- verify-unity-boundaries.mjs / view-coverage.mjs: assembly boundaries and role coverage.
- Exact diff check preserves shared atlas PNG/wave manifest and the capture helper; only the stone role/atlas changes in first-playable manifest. Bindings/licenses remain unchanged.
- EditMode regression updated but not executed in this bounded preparation lane. No Unity, build, Core test, replay run, native capture or performance measurement here.

Parent verification must record actual wave3 before/after at equal ticks/scale, Unity suites, replay hash and final CI. Inventory/centre-in-view metadata alone does not prove sprite visibility. C05 remains Claude Code night queue only. Historical mixed-scope first-playable captures are not #410 delivery evidence. Draft remains; no merge authorization implied.
