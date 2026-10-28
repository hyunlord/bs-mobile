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
