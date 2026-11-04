# Prior Taunt runtime-v2 raw fixtures

Frozen from `6ad16d4e099b3911fb9d4444aa228edbbe3d2cd3` (#698) on 2026-11-04,
before applying the zone implementation. Do not regenerate with a later candidate writer.

- Actual net8 Core SHA-256: `c3a9103be74c8c1e293da4f67f48fd0e58a4c84ca6f586cec0e1c3bad1dab10e`.
- Actual netstandard2.1 Core SHA-256: `1001fa3076be58301b19c586ffdfc02898346703aa4d03ccc4b04541f37efbb0b`.
- Copied from `.omo/evidence/taunt-20261104/parity-net8/taunt-*.bin` after the two actual DLL runs.
- Five taunt scenarios each provide issued header, recovery after tick3, complete state bytes after ticks1/3/16 (25 files).
- Scenarios reconstruct `HeroTauntRuntimeTests.Fixture`: all set first definition duration5; overlap has two definitions; movement inputs (1000,0) at tick3 only; damage cohort attack7/range100/cooldown1. Other input is (0,0).
- `taunt-basic` and `taunt-expiry` intentionally share the same recorded command/config sequence; retain both original scenario names without inventing additional coverage.
- `HeroTauntPriorBytesTests` reconstructs each scenario and compares complete bytes. These fixtures do not establish Unity, C05, or gameplay balance.
