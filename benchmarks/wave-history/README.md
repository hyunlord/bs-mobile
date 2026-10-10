# Historical wave replay samples

These two original closed native input recordings were selected before the #169 query optimization to preserve full 27000-tick behavior. They supplement the shorter high-density C05 fixture in `../wave-c05/`. They are a bounded regression sample, not a complete successful-run archive or balance evidence.

- `native-c07.ssreplay`: seed 30001, original run `5795771e9cd1406db1b672f961d86373`.
- `a03.ssreplay`: seed 30000, original run `b8ff1611bea04ce3baa66d719d38e572`.

`contract.json` pins original bytes, historical results and frozen data source. Do not rewrite replay headers, append a new end record or regenerate expected hashes from the implementation under test. The historical data snapshot is regenerated from the pinned Git commit by `tools/prepare-wave-benchmark.mjs` and is not duplicated here.

Run from the repository root:

```sh
PATH="$HOME/.dotnet:$PATH" node tools/verify-wave-history-parity.mjs artifacts/wave-history-check
```

The output directory must be new. The command builds both Core targets and their distinct .NET hosts, verifies loaded assembly identity, then checks all three original replays serially against fixed end hashes. CI passes `--no-build` after its existing solution build. C05 closes by explicit Quit at tick 14400; only C07 and A03 prove 27000-tick preservation. Generated assembly metadata and summaries stay local or in CI artifacts.
