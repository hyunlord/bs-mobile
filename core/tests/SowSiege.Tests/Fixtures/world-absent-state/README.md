# Pre-world state bytes

These four fixtures were captured on 2026-11-02 in an isolated worktree fixed at
`b66e85ff3e682b51229c147b81296d848b351e4b`, before any world product implementation.
The new test-only capture helper was copied into that worktree temporarily. The
Core source remained unchanged. Capture passed under the shared Mac lock using
SDK 10.0.403, the net8.0 test target and major runtime roll-forward.

`WorldCombatAbsentStateCompatibilityTests.HistoricalSession` defines the inputs:
plain and estate/melee/populated-vassal options, each at tick 0 and tick 10.
`CaptureHistoricalState` writes the historical version-1 root field sequence,
then verifies the complete bytes' SHA-256 against the session's actual state
hash. The regular tests only read these files and compare every byte.

Do not regenerate these fixtures to accept a new codec or runtime behavior.
World-absent bytes must remain unchanged under ADR0083.
