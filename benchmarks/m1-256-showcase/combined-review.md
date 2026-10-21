# Eight-source combined review fixture

This isolated stress catalog combines the eight candidate9 evolved gear records. It is not a production build or a balance recommendation. The existing 24 single-source cases and normal weapon-slot limits are unchanged.

## Contract and coverage

- Config: `data/review/m1-weapon-combined-case.json`; schema: `data/schema/m1-weapon-combined-case.schema.json`.
- API: `ReviewShowcaseCatalog.CreateCombined(baseline, WaveCombinedShowcaseCase)`; sidecar `m1-combat-combined-v1` binds catalog, input replay and review parameters.
- Seven weapon slots plus one tool slot are review hypotheses. The starting pair is extended by six recorded `GrantLevel` / actually offered `ChooseCard` pairs; state is never injected directly. No invulnerability command.
- Raider and player health 1,000,000, enemy damage 1, weak crop-grazer health 100 with first spawn tick 850, global cap 1,000 are explicit finite stress parameters. They do not modify production data or Core algorithms.
- Warmup ticks 0–899; measured event window `[900,1500)`, corresponding snapshots `(900,1500]`, 600 ticks/20 seconds at 30Hz. Square walking input matches the single-source harness.
- **Global alive count is not viewport count.** Native presentation must report visible density separately.
- All eight sources have real activation-counter deltas and positive damage events. Weak-enemy deaths exercise real death bursts and XP events; rendering/audio still require native execution.

## Results

`combined-review.csv` preserves both candidates. V2 recycle radius 4,000 forced 307,936 relocations in 600 ticks with body width 475 and 920–1,000 global enemies. This is pathological review geometry, not a normal gameplay performance result. Its 155 real deaths and two-target byte equality are retained. The extra latest-DLL-SHA replay recheck was interrupted before the #223 night window; that recheck is not claimed complete.

V3 changes **only combined arena recycle radius to 20,000**; spawn radius 2,600, all eight gear records and every other stress parameter remain identical. Global enemies 909–1,000; 167 real deaths; **zero relocations** in the window. Every weapon activates 20 times; the summon tool 75 times. `combined-activations.csv` records positive hit events separately from activations; group hits retain their real group source association.

V3 actual `net8.0` and `netstandard2.1` Core DLLs were framework/location/SHA validated, each generated and internally replay-verified the fixture, and each independently verified its replay again. Replay, sidecar, event CSV and density CSV bytes match. `combined-targets.csv` records actual DLL hashes. These .NET-host checks do not establish Unity, device, visual approval or native frame-time performance.

## Reproduction

From the repository root, use frozen C05 data (`artifacts/benchmarks/c05/data`, raw hash `942CFEA33A25F90CBD5053D711C9BBFF1279DBE481950F2081C765B6093C3FDC`). Build Sim, build Core netstandard2.1, then CompatSim. For each host DLL:

```sh
dotnet <host.dll> --assembly-metadata <output>/assembly.json
dotnet <host.dll> combined-showcase-fixture artifacts/benchmarks/c05/data data/review/m1-weapon-combined-case.json <output>
dotnet <host.dll> combined-showcase-replay artifacts/benchmarks/c05/data data/review/m1-weapon-combined-case.json <output>/combined-evolved.ssreplay
```

Validate metadata with `validateBinding` / `validatePair` in `tools/verify-target-parity.mjs`, using each actual built Core DLL SHA and expected framework. Compare all four generated files byte-for-byte. From the sidecar take `minimumEnemies`, `maximumEnemies`, `activationCounts`, `stateHash` and `replaySha256`; count `enemy-killed` and `enemy-relocated` event rows with 900≤tick<1500. Positive hits are `hit`, `group-hit` or `trained-group-hit` with amount > 0 and source equal to gear ID or a prefixed group source ending with `:<gear ID>`.

Local immutable evidence: `.omo/evidence/m1-256-combined-v2/` (preserved failure), `.omo/evidence/m1-256-combined-v3/` (final input, actual-DLL manifest and verification). Full successful raw files remain local; the small canonical CSVs are sufficient to retain the comparison.
