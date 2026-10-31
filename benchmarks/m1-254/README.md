# M1-2 actual-damage samples (#254)

These are **new accepted-input recordings**, not the original C05 benchmark. They preserve C05 commands before tick 13500, then use ordinary movement and the first offered legal card. No invulnerability, spawn multiplier, health edits, grants or rule changes are used. Both recordings end in genuine death before the intended 30-second window. Never describe them as surviving 30 seconds.

| Sample | Suffix rule selected before generation | Actual window | Global enemies | Positive damage events | HP lost |
|---|---|---|---|---|---|
| candidate1 | Move toward alive-enemy centroid; hold within 200 Core units | 13500–13779, 9.30 s | 399–829 | 178 | 1778 |
| candidate2 | Same approach for exactly 50 ticks; then return to prefix lord position and hold within 200 units | 13500–13643, 4.77 s | 814–824 | 160 | 1778 |

Candidate1 failed the duration target. Candidate2 was the single fixed follow-up and also failed duration, but all its observed suffix ticks have 800+ global enemies. It is the selected bounded **failure sample** for actual attacker-attributed damage across 1×, 1.4× warnings-off and 1.4× warnings-on. All three variants must consume identical bytes and reach the pinned death hash. Warnings and camera scale cannot improve damage outcomes under identical inputs; this comparison concerns visibility, not player behavioral benefit.

`contract.json` pins original provenance and each new tape. `*-ticks.csv` and `*-hits.csv` are small canonical failure observations. Global enemy count is not on-screen visible count. Hit IDs come from the opt-in Core observer; actual HP loss is positive, and projectile shooter ownership remains separate from projectile ID. The Unity viewport observer supplies screen-entry history later; missing ownership/history must remain explicit.

Original C05 has no invulnerability commands. Across its entire 14400 ticks it takes only two hits: tick 2572 (99 enemies, 10 HP) and tick 2957 (123 enemies, 12 HP). Its existing early/mid/800+ review windows contain no actual damage. They cannot support a nonzero incoming-hit cohort, and these new tapes must not replace its performance regression baseline.

## Reproduce

From the repository root, prepare the unchanged frozen data and regenerate into a **new** directory:

```sh
node tools/prepare-wave-benchmark.mjs --data-only
PATH="$HOME/.dotnet:$PATH" dotnet run --project benchmarks/m1-254/generator/Generator.csproj -- artifacts/m1-254-regeneration
```

The generator refuses an existing output directory, validates original C05 SHA-256, replays each new tape to its death hash, and requires byte-identical reproduction of both pinned new SHA-256 values. It does not overwrite canonical fixtures. Frozen data is regenerated from `5118e2c2a06f6449be672a77a64b61c9a349d69b`; it is not duplicated here.

The observer and projectile diagnostic ownership are deliberately excluded from historical state codecs. No old event fields, input format or Core rules are changed. Restored/missing projectile ownership is unknown (`-1`), never inferred from a matching definition ID.
