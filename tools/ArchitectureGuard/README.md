# Core architecture guard

Run `dotnet run --project tools/ArchitectureGuard -- "$PWD"` from the repository root.
Run `dotnet run --project tools/ArchitectureGuard -- --self-test` for executable mutation fixtures.

The CLI exits 0 only when inspection completes without violations; 1 means rejected code and 2 means incomplete inspection. Diagnostics include a source path, line, and rule ID.

| Rule | Enforced boundary |
| --- | --- |
| AG001 | Core source cannot name supported engine namespaces. Core project and inherited Directory.Build files cannot add package, project, assembly, framework, COM references or imports. Only Microsoft.NET.Sdk is accepted. |
| AG002 | Constant string expressions cannot contain namespaced content IDs or hero/estate IDs discovered from repository data/test JSON. Roslyn evaluates concatenations, Unicode escapes and constant interpolation. |
| AG003 | Numeric literals other than 0/1 (including unary -1) require an exact named, reviewed algorithm constant exemption. Naming a gameplay literal `const` is insufficient. |
| AG004 | Core cannot use system clocks, Stopwatch, environment tick counts, random GUIDs, Random.Shared or unseeded Random construction. |

All Core `.cs` files are inspected recursively, including disabled preprocessor text. `bin`/`obj` outputs are excluded. Roslyn syntax and semantic analysis is provided by the pinned .NET SDK assemblies; no NuGet package is required. This inspection complements, and does not replace, compilation and runtime determinism tests.

`algorithm-constants.json` is intentionally empty. An exemption must specify `symbol` (fully qualified const field), `value` (invariant decimal representation), and `reason` (algorithm provenance and why this is not gameplay tuning). Both symbol and value must match. Changing this file requires architecture review; do not whitelist gameplay balancing numbers.

Limits: this is a CI architectural boundary, not an adversarial sandbox or proof of arbitrary computed-string identity. Compile-time constants and recognized system APIs are inspected; runtime determinism and alternate-content tests cover behavior. S0 does not claim protection against malicious guard/workflow edits; required review/rulesets control those files.
