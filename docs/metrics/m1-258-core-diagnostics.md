# M1-258 combined Core diagnostic measurements

Scope: .NET 8 Release on the development Mac, the existing combined-v3 input, ticks 900–1499 (600 advances). This is stage diagnosis, not native frame acceptance. Core source candidate is commit `99fb483`; production content and the replay were unchanged.

## Findings and historical measurements

[m1-258-core-diagnostics.csv](m1-258-core-diagnostics.csv) retains three measured runs per incremental candidate. Values are calculated from the original per-tick CSVs; p50/p95 use nearest rank (`ceil(p*N)-1`). The temporary host's original JSON summary used `floor(p*N)` and therefore selected the following sample at exact integer ranks; do not mix those percentile conventions.

- `baseline`: before this change. Separation averaged approximately 1.13–1.16ms, the largest measured Core stage. Work averaged 0.50–0.52ms. Attack allocated 63,630.92 bytes per advance.
- `nearest`: group target selection scans for the distance→ID minimum instead of sorting all candidates. Work averaged 0.34–0.35ms. Other sorted target consumers are unchanged.
- `cached`: also reuses per-source metric counter keys. Attack allocation fell to 28,971.75 bytes per advance.
- `hit-loop`: also removes the captured predicate in `Hit`. Attack allocation fell to 24,658.68 bytes per advance; Work allocation was 134.8 bytes per advance.

These are sequential diagnostic experiments, not an isolated ABBA performance gate. Concurrent build/environment outliers remain in the rows, including the nearest candidate's 20.95ms maximum. Later maxima do not establish a regression or improvement. Unity Mono timings, render costs and native maximum-frame acceptance are pending separate measurements.

Stages are **nested**: `Enemy` includes `Separation`, which includes `Residual`; fine `Collect`/`Clear` occur inside separation. `Movement` is inside Enemy. Do not add nested time or allocation columns together. Fine-stage instrumentation itself adds calls and clock overhead, and its timings must not be mixed with the normal build. Zero fine-stage counters in a normal build mean not instrumented, not zero work. The measured historical host allocated row storage between measured advances; the maintained host reserves this storage first, so new runs are a new observer configuration.

All candidate runs ended at `C5050C0EE8DBF8837865CFBE83E7C59113109C0439BDE89B8BA9EB851CE5510F`. Actual net8.0 and netstandard2.1 DLL binding checks also preserved historical C05 `B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8`. This is not Unity/IL2CPP verification.

## Reproduce using tracked source

No existing Sim/Bench host exposed `IRuntimeDiagnostics`; the small BCL-only [M1CombinedProfile](../../tools/M1CombinedProfile/Program.cs) uses that existing hook. It changes no Core state and refuses existing output filenames. Run from the repository root with .NET 8 on PATH. Choose a fresh output directory/stem each time:

```sh
node tools/prepare-wave-benchmark.mjs --data-only
# Generate the same deterministic review input only when the fixture is unavailable.
dotnet run --project core/src/SowSiege.Sim -c Release -- combined-showcase-fixture \
  artifacts/benchmarks/c05/data data/review/m1-weapon-combined-case.json artifacts/m1-258-new-fixture

dotnet run --project tools/M1CombinedProfile -c Release -- \
  artifacts/benchmarks/c05/data data/review/m1-weapon-combined-case.json \
  artifacts/m1-258-new-fixture/combined-evolved.ssreplay artifacts/m1-258-new-profile/run 3
```

The fixture-generation command writes its own outputs: use a new directory. The diagnostic host writes `<stem>-0.csv` etc and `<stem>-summary.json`; the latter binds replay/review/catalog hashes, loaded Core DLL SHA/MVID, OS/runtime and observer settings. It checks the consumed catalog hash and final state hash. Timing surrounds `Apply` only; file writing and summary work happen after a complete run. Snapshot/render time is not included.

For nested grid instrumentation use `-p:SowSiegeFineDiagnostics=true` before `--`, with a different output stem. Keep those diagnostic runs separate. The source diagnostic host runs net8.0; the two-DLL replay comparison is a distinct correctness check, not a timing result.

Original diagnostic raw logs are local under `.omo/evidence/m1-258-core-diagnostics/`: `baseline`, `nearest`, `cached`, `hit-loop` and `fine`. The compact CSV above is the tracked measurement record; regenerable plots and duplicate successful raw outputs are not Release assets. No new performance run was performed while packaging this tool.
