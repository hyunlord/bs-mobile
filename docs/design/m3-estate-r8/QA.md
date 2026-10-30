# Independent r8 documentation / geometry review

**PASS — no blocking documentation or static-geometry defect found.** Exact r8 coordinates and the4→3 production threshold remain proposals awaiting user approval. **Do not merge or schedule automatic merge of this documentation PR before that approval.** B art-direction approval does not approve this exact design.

## Independently checked

- 13 distinct zone IDs, each one rectangle;1664 summed and unique cells,0overlap,1connected union,0enclosed missing cells. The outline is a stepped approximation under the current single-rectangle-per-ID Core contract, not the approved painting's pixel outline.
- Start exactly(24,24,12,12), only start initially open. All10 initial building kind/position/size/startsComplete fields match r7,40unique building cells inside start. Source SHA-256 matches the referenced r7 file; historicalr3–r7 bytes match currentHEAD.
- 24 actual shared-edge pairs recomputed from cell ownership, exactly matching reported edge lengths and every neighbor list. Core `StartZoneUnlock` uses ANY open edge neighbor; cost/reserve/production/idle-work remain separate required checks. Corner contact is not adjacency.
- River100cells, connected and entirely within the union;0overlap with start or building footprints. Dry land has1194/370-cell banks. Each of3 complete bridge rectangles is within the union, avoids buildings, has two dry endpoints on different banks and crosses water (3/2/2cells). All dry land plus bridges is connected.
- The proposal changes only seven unlock thresholds4→3; other unlock fields match r7. Current producing kinds are field/lumber_camp/ironworks, and Core counts completed facilities rather than summed levels. Graph reachability is13/13 at3; this is not an economy/time/playability test and does not authorize data changes.
- `AccountEstateZone` has a single rectangle; no new grouped IDs or multipart-zone runtime structure is invented. ConfigVersion mismatch rejection exists in Core. Retaining save format6 does not migrate r7 saves; proposal explicitly requires a new r8 estate/save path and keeps automatic migration out of scope.

## Visual review

Actually opened `geometry-topdown.png` and `geometry-isometric.png`. Both show readable cell grids,13 zone labels/legend entries,10 building letters and B1–B3 bridges. Bridges cross the river and start footprints are clear. Isometric64×32 proportions are retained; wider-than-portrait footprint is explicitly disclosed. The r7 appendix is additive and points to the proposal without replacing its historical contract.

## Limits

No runtime configuration was created/loaded, no save roundtrip was executed, and no normal resource progression, camera/UI, navigation, construction collision or performance was tested. Water/bridges are View-only proposals; the document correctly discloses that current Core does not block building movement onto water-display cells. Runtime road routing and first-screen readability remain implementation-PR work.

Independent recomputed results are retained locally in `output/estate-r8-design-476/qa/independent-geometry.json`; they do not substitute for user design acceptance or later runtime gates.

## Commands and independent method

- `python3 - <<'PY' ... PY` (read-only inline audit, output only to QA): loaded `geometry-proposal.json` and actualr7 JSON; expanded each rectangle into an independently owned cell map. Counted inter-owner4-neighbor cell edges to derive adjacency rather than reusing the generator's rectangle edge formula. Flood-filled union/complement/dry banks and checked each bridge's endpoints against distinct dry components. Compared initial building fields and computed sourceSHA-256; compared historical files to `git show HEAD:<path>`. Actual result:13/1664/0overlap/0holes,24exact adjacency pairs, all3bridges valid.
- A second `python3 -` read-only comparison verified each proposed unlock object equals r7 except completedProductionSites4→3 in exactly7zones. Actual result: no other unlock changes.
- `rg -n 'ProductionSitesComplete|ConfigVersion|Adjacent' core/src/SowSiege.Core/AccountEstate.cs` and bounded `sed` reads verified actualCore contracts at lines85,199,446–470; `AccountEstateContract.cs` confirms one rectangle per zone.
- `view_image` opened both finalPNG diagrams. No generator rerun, code execution through Core, Unity launch or runtime test occurred. Runtime status: **NOT_RUN**.

## Subsequent user decision (2026-10-28)

The user approved PR #478 and the production threshold 4-to-3 correction, and authorized squash merge after updating main and passing CI. The pending-approval statements above describe the earlier review state. Game implementation remains a separate task.
