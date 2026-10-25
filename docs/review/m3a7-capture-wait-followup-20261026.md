# M3a7 capture wait follow-up — 2026-10-26

Status: implementation prepared; Unity/runtime verification NOT RUN. The prior 01:00 user deadline remains; this follow-up began at00:49 and cannot fit two profiles plus native construction before that deadline.

## Evidence and limited fix

AstraBReview first-playable capture at2026-10-25T15:18:27.5151700Z reported329160 cyan pixels, anythingCompilingBefore=true and anythingCompilingAfter=true, precedingDomainReloadObserved=false. This is the first observed concurrence supporting the asynchronous shader hypothesis, not a demonstrated causal fix.

The PlayMode capture helper now yields while Editor ShaderUtil.anythingCompiling is true, with a10-second real-time limit. Timeout logs capture label, compilation state, frame and preceding reload state then fails; it does not capture/accept an unready frame. Screenshot callers use the helper immediately before their existing before/after probe. Existing cyan detection and failure PNG preservation remain. Editor API access is under UNITY_EDITOR; runtime game code is unchanged. Raw synchronous ReadPixels surveillance remains unchanged.

Waiting changes the actual capture frame; the recorded frameBefore/frameAfter remain authoritative, rather than the requested entry-frame label. A clean capture after a wait must not be described as proof of an un-delayed first frame.

## Two unexplained cross-profile skips

| Test | Source condition | Classification | Required input; no exception added |
|---|---|---|---|
| ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes | M1CombatReviewTests.cs:77: generated directory absent => Assert.Ignore | Runtime conditional Ignore, not platform exclusion | M1_COMBAT_FIXTURES or .omo/evidence/m1-256-combat-candidate4; showcase-fixtures24 case sidecars plus prepared frozen C05 catalog |
| MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures | MeleeAimReplayParityTests.cs:15–16: CatalogRunOptions.MeleeAim(current catalog)==null => Assert.Ignore | Runtime conditional Ignore, not platform exclusion | opt-in melee-aim catalog export and seed30001 replay fixture |

The standard first-playable/wave-1a execution does not supply those opt-in inputs. Historical failed harness records remain failed; this explanation does not turn them into a passing run.

## Verification

- node tools/verify-unity-boundaries.mjs: PASS,9 assemblies.
- git diff --check: PASS.
- No Unity, Standalone, build or native retry in this follow-up. Two-profile record, timeout runtime behavior and visual acceptance remain pending.
