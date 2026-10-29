# Estate development UI visibility (#509)

2026-10-29. Implementation f751f8102816d037d109f52740dab2006d70b7b2.

Development build OR an explicit development option enables decoration/time-skip buttons, prototype/no-save caption and zoom-state label. Normal callers default to no development controls. Normal zoom, drag, production, tabs and departure remain. No Core/data/save implementation changed. The existing map entry remains development-only; this change does not ship the prototype as a production estate.

## Verification

| Profile | EditMode pass/fail/skip | PlayMode pass/fail/skip |
|---|---|---|
| first-playable | 910/0/25 | 55/0/51 |
| wave-1a | 920/0/15 | 93/0/13 |

The stock runner preserves exit1 for two missing opt-in fixture environments. Existing supplemental frozen combat and melee fixture commands each ran1/0/0; combined skip checker:0 unexplained,1 listed(optional recorded-input export). No skip exception changed. Commands follow [prior prerequisite procedure](playmode-capture-session-414-20261026.md).

Initial preparation failed before Unity: the local harness mistakenly set UNITY_WAVE_BENCHMARK=1 globally, incompatible with first-playable. Corrected the local harness to keep normal suites and opt-in supplemental exports separate; game code and expectations unchanged. Raw first failure remains artifacts/dev-ui-509/full; valid run artifacts/dev-ui-509/retry1. No second occurrence.

Own lock released11:29:29 KST. Tracked files unchanged after run. Four-case policy test and actual on/off UI regression passed, including account save byte equality. Both390x844 captures cyanPixels0, compilation before/after false. Two independent visual reviews found no new clipping/overlap; root also inspected both images. Editor policy-off capture is NOT native Release binary evidence.

## Screens

Release: [estate-development-ui-509-20261029](https://github.com/hyunlord/bs-mobile/releases/tag/estate-development-ui-509-20261029). Only the two preselected on/off review samples are published.

- estate-development-chrome-off-390x844.png: developer chrome absent; production controls retained.
- estate-development-chrome-on-390x844.png: developer chrome and functionality retained.

No asset changes, performance claim, or native build. Draft until user approval.
