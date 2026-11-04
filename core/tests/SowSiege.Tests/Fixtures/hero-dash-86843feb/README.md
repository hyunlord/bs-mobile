# Prior Dash schema2 byte fixtures

Frozen from `86843febe1510e62868104ee35c7fd8bc5512866` (#689) on 2026-11-04,
before applying the taunt implementation. Do not regenerate with a later writer.

- net8 Core SHA-256: `f53e73eeacfaf97f6a7ef50e8e9e989a7fc2d1505c6ce002ef65281339ae6e92`.
- netstandard2.1 Core SHA-256: `e3ea13b304a2a55e5e9522c8eddb1dfe1ffbb0dbc6edf4addfdd63190d42fdf5`.
- Both actual assemblies produced identical observations for six scenarios,
  16 ticks each, with recovery after every prefix. Hosts were compiled against
  their respective actual Core DLL, sharing the original fixture sources.
- Inputs: `HeroDashRuntimeTests.Fixture` for axis, diagonal (1000,333), release
  (zero input from tick3), body (enemyX80), hit (enemyX80,Y10,damage7,range11),
  and wall (centerX+70..71, centerY-20..+20). Other cases use (1000,0) input.
- Headers are the issued contracts; recovery is after tick3; complete state
  bytes are after ticks1,3,16, using `CaptureHistoricalState`.
- `HeroDashPriorBytesTests` reconstructs each scenario and compares full bytes.
  These files do not establish Unity or C05 performance.
