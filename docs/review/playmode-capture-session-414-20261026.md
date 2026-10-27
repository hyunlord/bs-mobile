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


## Authorized batchmode correction (2026-10-26, code-only evening)

The user superseded the graphical/EndOfFrame approach: both profiles and both test platforms return to batchmode. The earlier proposal to restore serialized EditorSettings bytes is withdrawn; no file-copy restoration is added.

- Session setup/teardown now saves, disables and restores the nonpersistent `ShaderUtil.allowAsyncCompilation` switch. The older EstateMapFlowTests probe also uses this switch, including its log/CSV value; it no longer writes EditorSettings during cleanup.
- API availability was verified without launching Unity in installed 6000.6.4f1: `Contents/Resources/Scripting/Managed/UnityEngine/UnityEditor.CoreModule.xml` declares `P:UnityEditor.ShaderUtil.allowAsyncCompilation` (lines 55687–55690); the matching DLL contains both getter and setter.
- After the existing 10-second compilation wait, the guard yields null twice. This is an update wait, not an end-of-frame/render fence. Cyan counting, pre/post compiler status and immediate assertion failure remain unchanged.
- Restoration regression cases now exercise the runtime switch and assert that the persisted EditorSettings value is unchanged. The frame-wait test uses two update frames, not WaitForEndOfFrame.
- Static verification: shell syntax, Unity boundaries (9 assemblies), whitespace and unchanged tracked ProjectSettings all pass. No Unity, Standalone, build or full profile test was run for this revision. Runtime settings-file invariance remains to be checked after tomorrow's run.
- Next authorized execution is after 07:00: #415 two profiles, then #411. Historical failed XML/results above are preserved and are not evidence for the corrected revision. Both PRs remain draft.

## Daytime batchmode verification (2026-10-27, 9877ccc)

Owned run: `artifacts/day-20261027/pr415-tests`, 09:04:54–09:13:28 KST; own lock released at 09:13:28. First-playable EditMode 828/0/25, PlayMode 52/1/42; wave-1a EditMode 838/0/15, PlayMode 82/0/13 (pass/fail/skip). Tracked files, including ProjectSettings, remained unchanged. The prior wave-1a map pointer failures did not recur. Existing two unlisted cross-profile skips remain; no exceptions added.

The first-playable failure is `UiCaptureTests.PortraitAndSquareRetainRunAndCaptureRealUi:76`: after completed-run Retry, two fixed update yields did not replace the session. The log immediately after summary capture records `Run.unity` loading in 140.505ms (82.313ms thread wait); `BeginRun` assigns Session only after asynchronous scene loading. This supports a timing race, not a cyan/pointer failure. Intentional save-denied exceptions belong to separate passing failure-recovery tests. The test now uses the same bounded session-change wait already used for its death-retry path, with an immediate error check; game code is unchanged. Revalidation is required before claiming this correction passes.

The two skipped tests require extra execution environments, not exemptions: frozen C05 combat sidecars plus `UNITY_WAVE_BENCHMARK=1`, and the `m1-showcase-review` opt-in melee profile. The stock two-profile runner does not prepare those prerequisites. This limitation is preserved rather than treating the skips as a pass.

## Revalidation and stop (2026-10-27, 1a0f540)

Owned run: `artifacts/day-20261027/pr415-retry-tests`, 09:14:24–09:22:48 KST. Own lock released 09:22:48; no Unity process remained. No tracked settings or other tracked files were changed by Unity.

| Profile | EditMode pass/fail/skip | PlayMode pass/fail/skip |
|---|---:|---:|
| first-playable | 828 / 0 / 25 | 53 / 0 / 42 |
| wave-1a | 838 / 0 / 15 | 82 / 0 / 13 |

The completed-run restart regression now passes. Actual captures: **251, cyan 0**; two separate positive cyan negative-controls correctly detected one pixel each. The initial run had 250 actual captures, cyan 0. Both actual sessions used the nonpersistent async-compilation switch and restoration; persisted EditorSettings bytes are unchanged.

**Overall runner FAIL / stopped:** `skipped in every profile: 2 unexplained, 1 listed`, exit 1, in both daytime runs. No skip exception or assertion was weakened. The missing frozen-combat fixtures and opt-in melee profile remain the reasons documented above. Four green XML suites are not a passing full runner. The repeated failure rule stops further Unity work; #411 verification is blocked by its prerequisite. #410 was not recorded because external worldsim_probe/StorageManagement load exceeded the permitted CPU limit. Estate integration remains read-only preparation, and optional #422 was not started. All PRs remain drafts; no game/Core/save/data code changed.

Evidence source commit: `1a0f540b896ea9b1541d9d96482b19f27415d27c`. The exact runner block is in the PR body; raw logs/XML remain at the owned paths above. Historical graphical failures are retained, not replaced by these newer measurements.
