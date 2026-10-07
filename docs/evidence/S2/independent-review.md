# Independent S2 review evidence

Reproducer: /tmp/bs-s2-audit.ZQZr5o/Program.cs (temporary project; no repo source edits).

Command:
```sh
/Users/rexxa/.dotnet/dotnet run --project /tmp/bs-s2-audit.ZQZr5o/Audit.csproj
```

Initial observed output before fixes:
```
Moved enemy query: expected 1, actual 0
B repeated horn: people unchanged True, reported output delta 1
```

Fixtures: SpatialHash indexes enemy at (0,0), then moves it to (20,0) and queries (20,0) range 1. Rule B fixture calls EstateSystem.ApplyGrowth(horn) twice at the same tick and compares complete serialized People state to growth ledger delta.

Partial fix verification (current Core build):
```
B repeated horn: people unchanged False, reported output delta 1
Real Combat.Activate knockback query: expected 1, actual 1
First killing blow audit:first; reported death cause audit:later
```
Direct raw Position mutation still leaves the index stale as expected, but the real Combat.Activate call now routes through SpatialHash.Move and passes. B now recruits/assigns another worker rather than claiming unchanged work. Death-cause defect remains at this check.

Current test execution:
`dotnet test core/tests/SowSiege.Tests --configuration Release`: 10 passed, 0 failed.

Further independent fixtures, after all three fixes:
```
B repeated horn: people unchanged False, reported output delta 1
First killing blow audit:first; reported death cause audit:first
Real Combat.Activate knockback query: expected 1, actual 1
Two high-damage hits at HP3: actual ledger 3, HP 0
Crop stages 0,1,2,3, after harvest stage 0, harvests 1
Ruin rebuilt: health 35, rebuilds 1
C expiry role returning, arriving role peasant
```

Inspection notes:
- Result hash includes options (seed), catalog, complete World fields and tracked RNG call count. TrackedRandom exposes only seeded Random.Next(int), so under the pinned runtime seed plus draw count describes progression. No claim of cross-runtime RNG portability.
- Load runner samples before/after entities with explicit fixture restoration; the measured scope explicitly includes end-tick restoration and is not represented as natural gameplay.
- Actual-damage fixture verifies overkill is capped and zero-health enemies are not double-counted by subsequent attacks.

Final verification at this review checkpoint:
- `/Users/rexxa/.dotnet/dotnet test core/tests/SowSiege.Tests --configuration Release`: 19 passed, 0 failed.
- `/Users/rexxa/.dotnet/dotnet run --project tools/ArchitectureGuard -- /Users/rexxa/bs-mobile`: ArchitectureGuard 0 violations.
- No repository source edits by reviewer; all reproducer changes are under /tmp/bs-s2-audit.ZQZr5o/.

## Final features re-audit: squads, casualties, manual cards, damage ledgers

New defects reproduced before correction:
```
Squad groups into one entity: Members=5, entity count=56, population=60
Load after squad return/refill: expected entities 60, actual 64; total HP before 17701, after 18901
```
The load fixture previously added replacements after squad aggregation but did not remove excess agent entities after a returning squad split. Return splitting also fabricated 1200 HP for a wounded five-member squad. Both findings were reported to root/core owner and corrected.

Re-run command (no repository modifications):
`/Users/rexxa/.dotnet/dotnet run --project /tmp/bs-s2-audit.ZQZr5o/Audit.csproj`

Observed after corrections:
```
B repeated horn: people unchanged False, reported output delta 1
First killing blow audit:first; reported death cause audit:first
Real Combat.Activate knockback query: expected 1, actual 1
Two high-damage hits at HP3: actual ledger 3, HP 0
Crop stages 0,1,2,3, after harvest stage 0, harvests 1
Ruin rebuilt: health 35, rebuilds 1
C expiry role returning, arriving role peasant
Squad groups into one entity: Members=5, entity count=56, population=60
Load after squad return/refill: expected entities 60, actual 60; total HP before 17701, after 17701
Manual cards: offer count 3, lock changes hash True, reroll preserves locked True
Manual ban removed card True, cleared lock True
Manual selection resumes tick 2
Base ally damage separate: AllyDamage 3, WeaponDamage 0, total Result.Damage 3
Contact death/resummon: survivors before horn 0, militia after 1, newId True
```

Notes:
- The first legacy direct-mutation spatial fixture remains intentionally failing (directly assigning Position bypasses index maintenance); the actual Combat.Activate path passes and is the regression criterion.
- First-killing-blow fixture now clears friendly defenders so new contact interception does not obscure the intended test.
- Load cardinality is 60 simulated PersonState entities; PopulationMembers is separately reported and may differ while militia are grouped.
- No duplicate full test suite run in this final re-audit; core owner runs full suite and formatting. These are independent executable state fixtures, not a final S2 stage PASS or performance approval.
