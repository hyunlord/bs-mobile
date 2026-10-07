Closes #19

## Gate result
관문: 통과 — Core 변경 없이 test:dummy_rake를 추가하고 test:scout으로 실행. Only two test data files changed. S0 synthetic smoke evidence; not gameplay balance evidence.

## Verification
- `export PATH=/Users/rexxa/.dotnet:$PATH; ./tools/check.sh` exited 0: 6 tests passed, 0 warnings/errors, ArchitectureGuard 0 violations, 18 policy/seed cases × 3 deterministic repeats.
- `dotnet run --project core/src/SowSiege.Sim -- --data data --seed 42 --policy mixed --include-test --hero test:scout --estate test:moor --output artifacts/runbook-dummy.json --metrics artifacts/runbook-dummy-metrics.json --iterations 3` exited 0.
- All 3 results: heroId=test:scout, estateId=test:moor, toolId=test:dummy_rake, damage=14168, growth=7200, hash=6A46FC8D5A38FD17487F8C35A19719E9922685DF49C1E956747C4D02A0849979.
- Explicit JSON assertion passed for 3 results, identities, positive damage/growth, equal hashes.
- `git diff --cached --check` passed. Raw local evidence: /tmp/bs-mobile-runbook-check.log and /tmp/bs-mobile-runbook-trial/artifacts/runbook-dummy.json (will be included in S0 evidence handoff).
- Projects token lacks read:project; tracked in #2. Does not block data validation.

## Screens
해당 없음: 헤드리스 S0 데이터 연기 시험.
