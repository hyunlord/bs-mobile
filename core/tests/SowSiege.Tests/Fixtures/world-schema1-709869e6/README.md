# format9 schema1 byte fixtures

Frozen on 2026-11-04 from the preserved Release assemblies built for
`709869e654df948bec2a1a158a190244cc9ec6ce` (#669), before compiling the
hero-active candidate. These files are historical expectations, not candidate
encoder output. Do not regenerate them from the current implementation.

- Source Core DLL SHA-256: `2f2c8154906bda1c7fc31c0e2909e7df2110650fde84fc5d9b8e763ddd162cc2`.
- Source test DLL SHA-256: `266b9b1d6662e43c783be25e5ba9a3641919394538d049fb3794aa89b28b3198`.
- Host: .NET SDK 10.0.403, original net8.0 assemblies, runtime roll-forward.
- Eight headers: the original `WorldRecoveryCodecTests.Document(vassals)`
  fixture, estate absent/present (`1`, `test:loadout`, 64 `C` hash characters),
  melee aim absent/present, vassals absent/present.
- Nine state/runtime/recovery files: original `WaveEstateTests.Wave()` catalog
  and `WorldCombatRuntimeTests.Options(catalog, 3, 10)`, with zero-input Advance
  commands at ticks `[0, count)` for counts 0, 2, and 3. State bytes use the
  original `CaptureHistoricalState` helper; runtime bytes come from decoding
  the original recovery document.
- Terminal replay and end: the same three-command session, original header,
  command encoding and `GetWorldCombatResult()` at tick 3.

The temporary generator referenced only preserved baseline assemblies, used
reflection to invoke those original fixture methods, and wrote each binary
with `FileMode.CreateNew`. Its source and execution logs are retained in local
`.omo/evidence/dash-20261104/baseline/`; that local evidence is not a repository
regeneration dependency. `WorldSchemaOneCompatibilityTests` independently
reconstructs these inputs using the candidate and compares complete bytes.
