# S0 deterministic foundation

This is a **synthetic S0 CI scaffold**, not the S2 game simulation. Damage/growth
are input-sensitive counters for validating data loading, IDs, policy selection,
reproducibility and metric persistence. No survival rate, balance, spatial combat,
seasons or S2 performance-load claim is made here.

- `src/SowSiege.Core`: pure BCL records and seeded fixed-tick workload. No file I/O,
  engine APIs or content identifiers. `Simulation.Tick()` consumes one logical tick.
- `src/SowSiege.Sim`: JSON boundary (`ContentLoader.Load`) and CLI / timing adapter.
- `tests/SowSiege.Tests`: xUnit reproducibility, alternate-content and input-effect checks.
- `bench/SowSiege.Bench`: real BenchmarkDotNet full-run measurement; workload is S0 only.

```sh
dotnet test SowSiege.sln
dotnet run --project core/src/SowSiege.Sim -- --data data --seed 42 --policy mixed --output artifacts/smoke.json --metrics artifacts/metrics.json --iterations 3
dotnet run --project core/src/SowSiege.Sim -- --data data --include-test --hero test:scout --estate test:moor --seed 42 --policy land
dotnet run -c Release --project core/bench/SowSiege.Bench -- --filter '*' --job short --exporters json
```

Policies `weapon`, `land`, `building`, `people`, `mixed`, `random` are currently
JSON-selected synthetic multipliers, **not the S4 bot implementations**. Metrics
report individual tick p95 in milliseconds after a discarded warm-up run. Wall
clock appears only in Sim. Results exclude clocks and hash observable counters.
Reproducibility is scoped to the pinned .NET 8 SDK/runtime; do not promise cross
runtime RNG compatibility without a separately specified PRNG and golden vectors.

Each data file contains one record. IDs are namespace-qualified; schema validation
runs before CLI execution in the repository check. Loader performs cross-reference
checks but is not a replacement for JSON Schema validation. The default playable
fixture count remains one hero / one estate; `data/test` fixtures load only when
requested. Tuning and initial definitions are review placeholders, not locked
creative selections.
