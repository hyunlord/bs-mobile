# S4 CSV/report independent audit

관문: 수정 필요 — 순위·교차·CSV 재생성 로직은 요구사항과 맞지만, 잘못된 입력을 통과시키는 검증 누락 4개를 재현했다.

Scope: read-only review of tools/s4-report.mjs, s4-report-input.mjs, s4-league.mjs, test-s4-report.mjs, CSV contract. Repository report/runner files not edited. Independent mutations are /tmp/bs-s4-report-mutations.mjs; results /tmp/bs-s4-report-mutations.json. Nine invalid variants all accepted by validateTables at audit time. Tests imported to reuse their explicit synthetic fixture; no actual league outcomes altered.

## F1 — P1: contradictory survival and terminal state changes ranking and survival claims

Locations: tools/s4-report-input.mjs:75-93; tools/s4-league.mjs extractS4Case terminal checks; tools/test-s4-report.mjs observed-cohort test.

Reproductions accepted:
- terminal timeline lordHealth=0 while run/repeats survived=true;
- run/repeats survived=false with positive terminal health and fixture-duration;
- alive fixture-duration shortened to450 despite requested900, with consistent final damage/XP and repeat tick values.

Impact: survivalRate uses survived while nAlive uses lordHealth, so the same row can be counted both dead and surviving. Since survived is first rank component, impossible metadata can change dominance. Early alive cutoff is admitted as a completed requested fixture. Existing observed-cohort test currently encodes precisely this impossible early-alive state.

Fix: both producer and report validator must enforce endReason allowed set; survived iff terminal HP>0; death iff HP<=0/endReason death (a nonempty death cause where contract demands); alive duration/fixture-duration must reach requestedTicks, with full vs truncated labels matching mode. Early death allowed; do not demand all cases reach cap. Convert cohort fixture to an actual early death with health/flags/repeats consistent.

## F2 — P2: missing timeline grid and regressing progression accepted

Locations: tools/s4-report-input.mjs:85-90; tools/s4-league.mjs timeline loop.

Accepted deleting the450 periodic sample of a still-running900tick case while retaining0/900; accepted intermediate level999/season3 returning to level2/season0. Also accepted foodFinal999 when terminal sample food0.

Impact: nObserved becomes smaller at intermediate ticks without an actual death/termination, changing plotted means and observed cohort; reported sample precision no longer matches timelineSampleIntervalTicks. Progression curves show impossible negative growth despite monotonic cumulative XP.

Fix: require exact ordered ticks0, each configured interval before terminal, and terminal (deduplicated when aligned); permit off-grid early death terminal. Validate nondecreasing level/season with four-season bounds and terminal food equality. Existing cumulative damage/XP monotonic checks are sound and should remain.

## F3 — P2: card catalog is not tied to selected profile pool

Locations: tools/s4-report-input.mjs:69-71,94-98; tools/s4-league.mjs runtimeIdentity cardCatalog checks.

Accepted appending test:foreign weapon to cardCatalog and a matching offered/chosen card although selectedIds.weapons excludes it. Existing synthetic fixture also has only one card definition for a selected12-card pool, so it cannot catch completeness drift.

Impact: card-distribution can incorporate impossible outside-profile choices, and completeness of recorded card catalog is unproven.

Fix: require exact set of selected weapons/tools/charters, matching kind for each id; forbid item/evolution cards. Validate chosen cards against that catalog. Keep zero-card cases valid. Update fixture to a complete catalog. For current runtime with no removal, chosen equipment/charter final ownership reconciliation is additionally useful.

## F4 — P2: declared provenance/season protocol is only partly validated

Locations: tools/s4-report-input.mjs:38-56.

Accepted sourceCommit='not-a-commit' and seasonDurationTicksJson=[1] while report prints source commit and full configured durations. scenario, shortenedSimulation and crossoverRule are required but not checked against actual protocol. Producer enforces some of these but standalone CSV replay validator does not.

Fix: require40hex commit; scenario normal; declared crossover rule exact; mode-consistent shortened flag; positive four season durations summing to configured21600, four unique names, tickRate30; stage sourceDirty false (smoke may true). Validate runtime metadata object and warmup bounds if claiming them in provenance. All such inputs must remain read only from CSV.

## Verified strengths / no finding

- dominance intersects each peopleRule×seed condition's co-first policy set; full ties and a universally co-first subset both fail. Food/material/residence are excluded from rank tuple.
- crossover requires both cumulative damage values positive, includes equality, preserves earliest recrossing, returns null when absent. Interval crossing separately derives adjacent cumulative deltas.
- XP ratios preserve zero-total null, use per-case and pooled denominators separately; no XP or food conversion to damage.
- observed curves expose nObserved and nAlive, do not forward-fill dead cases, and report warns changing cohort bias.
- deterministic repeats are exactly three unique indices with matching hash/ticks/survival/endReason, not independent samples. Matrix keys/policies/rules/seeds are checked.
- generator reads seven CSV files only; source manifest hashes those CSV bytes. Markdown explicitly states raw JSON SHA records are not independently revalidated by CSV-only replay.
- effect units are not summed into one score; zero-use effects stay listed as missing league proof.

## Separate strict adapter fixes already completed under explicit root authorization

RuntimeContentLoader + JS validator now reject unsupported damage-pulse subjects other than weapon-front and modifier foodCost>0. Added regression cases; JS68/68 and focused C#10/10 PASS. These fixes did not alter selected data/league outcomes. Logs /tmp/bs-s4-validator-tests.log and /tmp/bs-s4-content-guard-tests.log.

Additional guard note: removing mixed tool's secondary growth action passes standalone validator but fails S4ContentTests, so whole-CI gate still catches it. A profile-level actual-target coverage assertion would make validation.json independently demonstrate the mixed-tool gate.

Graft source lookup automatically refreshed its generated graph before the read-only audit; no source edits came from that action. Approximate context saving20,581 tokens.

## Independent after-fix verification

Original nine mutation cases: **9/9 rejected**. Re-ran unchanged `/tmp/bs-s4-report-mutations.mjs` after owner fixes. The harness output path is reused; immutable after-fix copy is `/tmp/bs-s4-report-mutations-after.json`, execution log `/tmp/bs-s4-report-mutations-after.log`. Original pre-fix findings above remain the historical observed record.

| Mutation | After-fix rejection |
|---|---|
| survived=true with terminalHP0 | survival and terminal health mismatch |
| survived=false with positive terminalHP | survival and terminal health mismatch |
| alive450tick run with requested900 | premature alive duration |
| level999/season3 then regression | regressing level |
| foodFinal inconsistent with terminal | terminal food mismatch |
| sourceCommit not40hex | invalid sourceCommit |
| one invalid season duration | invalid season configuration |
| missing periodic450tick sample | missing/unexpected periodic sample |
| foreign card outside selectedIds | card catalog outside selected ids |

The imported owner report suite also executed **40/40 PASS** during this independent run. No source/report/runner files edited by this follow-up audit.

Remaining bounded caveat from original F1/F4 recommendations: standalone CSV validation still accepts alive run with endReason='death', smoke metadata shortenedSimulation='false', and scenario='load'. Independently reproduced `/tmp/bs-s4-report-protocol-check.json` with `/tmp/bs-s4-report-protocol-check.mjs`. These were additional protocol clauses in the original recommendations, not members of the original nine-case mutation harness. Producer validation can protect actual generated league evidence, but CSV-only replay does not yet reject these three contradictory declarations. Root/docs owner notified; no autonomous source changes during freeze.

Mixed-tool secondary-action removal remains covered by C# S4ContentTests as previously noted; no expansion requested during source freeze.

## Final independent closure

관문: 감사 범위 통과 — 최초9건 및 후속 프로토콜5건 **14/14 차단**, 보고서 테스트 **43/43 통과**.

After the final owner fix, re-ran both independent scripts. The three remaining clauses were tested in five directions: alive→death label, dead→fixture-duration label, alive smoke→full-duration label, smoke falsely untruncated, non-normal scenario. All reject with explicit lifecycle/mode/protocol errors. The original nine were rerun again and still reject.

Final artifacts:
- `/tmp/bs-s4-report-mutations-final.json` and `/tmp/bs-s4-report-mutations-final.log` — original9 rejected, embedded owner suite43PASS.
- `/tmp/bs-s4-report-protocol-after.json` and `/tmp/bs-s4-report-protocol-after.log` — protocol5 rejected, including lifecycle both directions.

The preceding residual caveat is now closed. This audit establishes fail-closed behavior for the reproduced input contradictions and reviewed arithmetic; it does not claim complete numeric league balance, human fun, mobile usability or unmeasured effects. No source edits in this independent closure pass.
