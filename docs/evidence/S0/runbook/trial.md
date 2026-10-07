관문: 통과(로컬·원격 CI·병합) — 독립 신규 에이전트가 AGENTS.md와 연결된 add-content/handle-issue 런북으로 테스트 전용 도구를 추가했다.

## Scope and provenance
First read ONLY /Users/rexxa/bs-mobile/AGENTS.md. Cloned https://github.com/hyunlord/bs-mobile into /tmp/bs-mobile-runbook-trial. Read docs/runbooks/add-content.md and docs/runbooks/handle-issue.md. No source implementation files inspected; only test hero JSON read by documented script. No Core/production content edits. Parent supplied no implementation instructions beyond task boundary and .NET PATH.
Base: 1a95c2074b838979b733ab2bda341f1382776fac
Head: 04ceabd2fde8fd6c1f789be9127ec2644d057a07
Issue: https://github.com/hyunlord/bs-mobile/issues/19
PR: https://github.com/hyunlord/bs-mobile/pull/21
Environment: Darwin arm64; .NET 8.0.425; Node v25.8.2.

## Commands (actual sequence, ordinary polling omitted)
```
cat /Users/rexxa/bs-mobile/AGENTS.md
git clone https://github.com/hyunlord/bs-mobile /tmp/bs-mobile-runbook-trial
cat docs/runbooks/add-content.md docs/runbooks/handle-issue.md
gh repo view --json nameWithOwner
gh issue list --limit 50
gh api repos/hyunlord/bs-mobile/milestones
gh project list --owner hyunlord --format json
gh issue create --title 'Validate tool runbook with an independent dummy tool' --body 'Core 변경 없이 더미 도구를 데이터로 추가하고 테스트 영웅으로 실행한다. Scope: test-only dummy rake and test:scout startingTool. Validation: tools/check.sh and three deterministic simulator iterations; no Core or production-content edits. Project placement is blocked by missing read:project scope, tracked in #2.' --label type/content --label area/data --label phase/0 --milestone '0. 기획 + 개발 기반'
gh issue comment 2 --body 'Independent add-content runbook trial: gh project list --owner hyunlord --format json failed: authentication token is missing required scopes [read:project]. Board placement and status remain unverified. Existing issue is reused to avoid duplication.'
git rev-parse HEAD
git switch -c content/15-dummy-tool
git branch -m content/19-dummy-tool
```
Initial branch number was agent input error, immediately renamed before commit/push. Not a runbook defect.
Wrote data/test/tools/dummy_rake.json verbatim from runbook. Used documented Python matcher to update the sole test:scout record startingTool, preserving all other data. Python JSON indentation reformatted tags but did not change them.
```
export PATH=/Users/rexxa/.dotnet:$PATH
./tools/check.sh > /tmp/bs-mobile-runbook-check.log 2>&1
dotnet run --project core/src/SowSiege.Sim -- --data data --seed 42 --policy mixed --include-test --hero test:scout --estate test:moor --output artifacts/runbook-dummy.json --metrics artifacts/runbook-dummy-metrics.json --iterations 3 > /tmp/bs-mobile-runbook-sim.log 2>&1
git diff --check
git diff --stat
git add data/test/tools/dummy_rake.json data/test/heroes
git diff --cached --check
git diff --cached --stat
git commit -m 'test(data): prove new tools work without core edits' -m 'Exercise the independent-agent runbook with a test-only tool.' -m 'Confidence: high
Scope-risk: narrow
Tested: tools/check.sh; 3 simulator iterations with positive damage and growth and identical hashes
Not-tested: gameplay fun, mobile visuals, balance'
git push -u origin HEAD
gh pr create --title 'test(data): prove tool extension through the runbook' --body-file /tmp/bs-mobile-dummy-tool-pr.md
gh pr checks 21 --watch > /tmp/bs-mobile-runbook-ci.log 2>&1
```
Additional JSON assertion verified exactly 3 rows, heroId=test:scout, estateId=test:moor, toolId=test:dummy_rake, positive damage/growth and one distinct hash.

## Actual local output
check.sh exit 0: build warnings=0 errors=0; tests passed=6 failed=0 skipped=0; ArchitectureGuard violations=0; smoke 18 policy/seed cases ×3 repeats.
All dummy results: damage=14168 growth=7200 ticks=900 hash=6A46FC8D5A38FD17487F8C35A19719E9922685DF49C1E956747C4D02A0849979.
Cached diff: data/test/heroes/scout.json and data/test/tools/dummy_rake.json only (13 insertions, 1 deletion).

## Instruction gaps and limits
No implementation-blocking instruction gap encountered. Runbook successfully exposes all needed data/CLI steps. `git diff --stat` before staging omits untracked new tool; cached diff --stat used additionally to verify the full two-file scope. Consider documenting cached stat explicitly.
GitHub Projects unavailable: `error: your authentication token is missing required scopes [read:project]`. Existing needs-decision #2 comment https://github.com/hyunlord/bs-mobile/issues/2#issuecomment-6045888617 preserves actual failure. Board placement/status unverified.
Evidence is a synthetic S0 scaffold smoke. Gameplay fun, balance, mobile performance, visuals, and S2 complete run not verified.

## Remote completion
`gh pr checks 21 --watch` exit 0; quality pass 1m27s; secrets pass 8s. CI: https://github.com/hyunlord/bs-mobile/actions/runs/37679734407 .
`gh pr merge 21 --auto --squash --delete-branch` exit 0. PR state MERGED at 2026-10-07T20:10:09Z. Merge SHA ba99f8eaf0fe4a25a3691253aa9769516c57382f.
`gh pr view 21 --json state,mergedAt,mergeCommit,url,headRefName` confirmed actual merge.
`git ls-remote --heads origin content/19-dummy-tool` returned no entries (deleted). Clone on updated main.
Evidence folder is an uncommitted handoff for parent S0 archive/commit; only the 2 intended data files entered this PR.
