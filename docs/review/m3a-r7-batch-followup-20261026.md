# M3a r7 batch and visual follow-up

Date:2026-10-26. Status: static diagnosis and implementation only; runtime verification pending before budget changes. Previous01:00 deadline retained.

## Existing measurements (before this change)

| Source | Configuration/state label | base | landmark delta | total |
|---|---|---:|---:|---:|
| ad6bc52 wave-1a log598/627 | r6 on / off |12 /8|2 /2|14 /10|
| 80a558b wave-1a log604/633 | r7 on / off |10 /10|2 /2|12 /12|
| 80a558b wave-1a log869–1380 | historical r3–r6 on / off |10 /10|2 /2|12 /12|

Off regression is observed8→10, not measured9→10:9 was the budget. State labels alone do not prove the toggle changed; the next run logs actualDecorEnabled and checks it.

## Layer / texture explanation

| Plane | Texture | Ordering / evidence |
|---|---|---|
| Lv0 Standing parts | estate-decor | old ruin, fence, rocks, grass all share atlas; source=reclaimable-site remains when optional decor is off |
| Home shadow | estate-building-shadows | drawn before Standing bodies |
| Home body | estate-buildings | sorted between Lv0 parts by Y |
| Landmark ground | estate-terrain | grouped before decor landmarks |
| Landmark props | estate-decor | landmark increment remains2 |

EstateDecor builds Lv0 parts for every config; PortraitMetaPanels.EstateMap previously put them into the Y-sorted Standing plane. Static coordinate calculation puts5/31 r6 and12/24 r7 site pieces before/after the home. Thus decor→building→decor splitting is a concrete candidate for the two extra switches. Current aggregate CSV cannot assign every one of the ten runs, so exact contribution is NOT yet proven.

The next run writes batch-runs-{version}-{state}.csv with Graphic index, parent plane, name, material, texture and each run boundary, before any budget assertion. It separately measures withoutLandmarksAndLv0Parts / withoutLandmarks / all, retaining the original r6/r7 budgets. Removing Lv0 parts is a structural diagnostic, not a GPU measurement. No budget increase has been made.

## View changes to verify

Lv0 becomes a low foundation/fence/weed assembly with three variants; large ruin walls remain terrain landmarks. Existing terrain/yard sprites provide lower-alpha earth and sage patches; dirt paths remain above infill. Warm rim contrast is reduced. These are unverified visual edits, not a claimed match to keyart C.

Core, data, saves and replay code are unchanged. Two Unity profiles, native retry once, first/whole/construction/completed keyart comparison and edge crops remain NOT RUN. The existing failed records remain preserved.

## Prepared rendering change

Flat Lv0 parts now draw in Ground before all building shadows and Standing bodies, with the same terrain tint and retained SiteId/moving-selection alpha. Four deterministic nearby-site variants replace ID-modulo repetition; r7 default neighbors within4 cells have no identical variant in the static JSON calculation. A new EditMode test covers that specific starting layout. Arbitrary future dense placements are not guaranteed four-colourable and must be evaluated separately.

RIM change only broadens/reduces the rim term (0.28 exp(-d²/0.72) →0.14 exp(-d²/3.2)); the existing continuous rounded distance and bilinear field remain. Removal of visible stair steps/white edges is NOT claimed without capture.

Static checks: boundary checker PASS9 assemblies; git diff --check PASS. New tests are written, not executed.
