# #386 approved night verification — 2026-10-26

Status: FAILED at unexplained cross-profile skips; individual suites have zero failures.

## Unity tests
commit: ad6bc52a4bbe0ae93b95447d3d0ceba2a62fe65b
first-playable EditMode: passed 826, failed 0, skipped 25
first-playable PlayMode: passed 41, failed 0, skipped 42
wave-1a EditMode: passed 836, failed 0, skipped 15
wave-1a PlayMode: passed 70, failed 0, skipped 13
skipped in every profile: 2 unexplained, 1 listed

## Remaining blockers

UNEXPLAINED M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes :: Generated actual fixture sidecars unavailable; run showcase-fixtures first.
UNEXPLAINED Tests.EditMode.MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures :: Requires the opt-in melee aim catalog export.
listed Tests.PlayMode.WaveWorldTests.RecordedWaveRangeExportsActualTickFrames :: Optional recorded-input sequence export.

The normal two-profile harness does not export the opt-in melee aim catalog or prepare the frozen showcase fixture set. No skip exceptions or rules were changed. Public screenshot capture boundary is preserved. Runtime suites executed under the owned shared-Mac lock with the user-approved exception ending 01:00.
