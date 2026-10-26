# PlayMode capture-session stop (#414 / #415)

2026-10-26. Status: **failed / draft**, no merge. Game/Core/save/replay code unchanged.

## Change and compatibility

`PlayModeCaptureSession` saves and disables Editor asynchronous shader compilation for the NUnit namespace session and restores the original setting during teardown. `WaitForShaders` retains the 10-second compiler timeout, then yields two `WaitForEndOfFrame` boundaries. Strict cyan monitoring is unchanged.

Installed Unity Test Framework `CoroutineRunner.cs` explicitly throws `UnityTest yielded WaitForEndOfFrame, which is not evoked in batchmode.` The runner therefore keeps EditMode in batchmode and runs PlayMode in graphical Editor. This is an execution-mode change, not evidence that graphical and batch interaction behavior are equivalent.

The first runner attempt stopped after a passing first-playable EditMode because macOS Bash 3 treats an empty array expansion as unset under `set -u`. Commit `0543c60` fixes the argument array. That first attempt did not execute PlayMode.

## Execution (0543c60)

Owned output: `artifacts/capture-session-414/unity-two-profiles-jS6f5N6A/tests`.

| Profile | EditMode pass/fail/skip | PlayMode pass/fail/skip |
|---|---:|---:|
| first-playable | 828 / 0 / 25 | 53 / 0 / 42 |
| wave-1a | 838 / 0 / 15 | 80 / **2** / 13 |

- Actual capture observations: **236**, cyan observations **0**. Two separate intentional 1-pixel negative controls still fail inside `Assert.Throws` as required; they are excluded from actual captures.
- Session domains: `2026-10-26T12:11:55.3746710Z`, `2026-10-26T12:15:41.1960920Z`.
- Both real sessions log `CAPTURE_SESSION_RESTORED asyncShaderCompilation=True` at teardown. The extra restoration lines come from the two setting-restoration unit cases.
- Render-boundary regression passed: from a starting end-of-frame, the guard advanced to two subsequent end-of-frame boundaries.
- Cyan absence in these sessions does **not** establish the original shader root cause or successful flow validation.

## Repeated interaction failure: stopped

Both failures occur in the same `EstateMapFlowTests` fixture after switching to graphical Editor. They were observed once each in the single complete two-profile attempt; no retry was performed after this pair.

1. `ASavedEstateBuildsClaimsAndSpecializesOnTheMap`, `EstateMapFlowTests.cs:465`:
   `A tap reaches the map itself at (195.00, 554.26): Movement surface`
   Expected `EstateMap.Viewport`; actual `Movement surface`.
2. `DevelopmentOptionSwitchesTheEstateTabToTheIsometricMapAndBack`, `EstateMapFlowTests.cs:535`:
   `EstateMap.Goal blocked at (54.00, 772.00) by Movement surface`
   Expected `EstateMap.Goal` Button; actual null.

This repeated pointer-routing failure activates the user's stop rule. It is not a cyan recurrence. No interaction assertion was weakened, no exception added, and no game input code changed. Root cause is unconfirmed; the evidence currently establishes a graphical-Editor execution-mode regression only.

Graphical Editor also reserialized `EditorSettings.asset` from schema 11 to 16 while restoring `m_AsyncShaderCompilation: 1` correctly. The stock runner rejected the dirty tracked file. The auto-upgraded copy is preserved in the owned run folder; after Editor exit the exact HEAD bytes were restored. The run is **not a valid passing Unity record**. A future authorized fix should restore the test's original EditorSettings bytes after Editor exit, while independently verifying the session setting restoration; that runner change has **not** been implemented after the stop.

Existing cross-profile skips remain unmodified:
- `M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes`: generated actual fixture sidecars unavailable.
- `MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures`: opt-in melee aim catalog export unavailable.

## Dependent work / shared Mac

- #411 includes the test-only fix through non-force merge, plus optional actual CardPool captures for heirloom-service and seed-crown. Its new two-profile run was **not started**, because the prerequisite failed.
- #410 native comparison was **not started**. External StorageManagement processes repeatedly exceeded the recording CPU limit; no external process was stopped. C05 remains with Claude Code's night queue.
- #390 remains draft/unmerged: this task has no separate approval to merge it.
- Own Unity work ended before 21:19 KST. Own lock was released at 21:18:5x and confirmed absent at 21:19; no Unity/native process remained. No other `$MV` path was written or deleted.
