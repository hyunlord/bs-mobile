# r8 terrain production assets (#488)

This delivery provides reusable terrain art and an offline composition of all 13 recovered r8 regions. It does not change game code/data, install art in Unity, or depict completed estate development. Final art approval remains with the user.

Release: [estate-r8-terrain-488-20261028](https://github.com/hyunlord/bs-mobile/releases/tag/estate-r8-terrain-488-20261028).

## Contract and contents

The native grid is 64×32px per cell; a 2×2 footprint is 128×64px. Padding does not add logical land. Every PNG has an explicit canvas, floor-center pivot, version and source provenance in the Release metadata. Bright/dark counterparts share registration but were painted as paired images; small internal silhouette differences are disclosed rather than described as identical alpha.

| Group | Variants | PNGs | Accepted source versions |
|---|---:|---:|---|
| Flat repeating ground materials |5|20|Field bright r4; other bright/dark r3b|
| Forest,field,ruin,hill,fallow clusters |20|80|Forest/field r1; other12 bounded-context export r2|
| Registered river chunks and context modules |12+9|84|r5|
| Three bridge placements |3|12|r1; east dark registration r2|
| Small bank accents |4|16|Original bright export r2; dark imagegen revision export r3|

All groups include native1× and2×, bright/dark. The river's12 chunks reconstruct the approved route exactly; nine context modules are optional registered extracts, not arbitrary seamless tiles. Do not overlay modules on top of the complete chunks. Logical geometry retains13 rectangles and1664 cells,100 water cells and three bridge placements. Water remains presentation only.

## Composition and evidence

**The overview and approved B illustration have substantially different camera framing and geography. Their side-by-side image is a comparison, not a claim that the composition reproduces B.**

The preview is rendered deterministically from delivered PNGs only. No old building assets or newly generated whole-map painting are used. Reserved footprints of the ten starting buildings remain empty. Ground textures use shared global UVs, and repeated texture pixels are stitched before projection to prevent antialiased quad-edge gaps. Props use deterministic varied spacing and variants while avoiding reserved slots and river/bridge footprints.

The approved r8 geometry projects to 2400×1200 native pixels. The full preview fits a 2600×1600 native canvas at 0.15 scale inside 390×844. This is explicitly an offline camera fit, not a runtime screenshot or an exact camera match to the earlier B illustration.

- [All 13 recovered terrain,390×844](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/terrain-r8-all13-reclaimed-r1-390x844.png)
- [Native composition](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/terrain-r8-all13-reclaimed-r1-native.png)
- [Approved B comparison](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/terrain-r8-vs-approved-B-r1.png)
- [Native river detail](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/terrain-r8-river-detail-native-r1.png)

- [Native-scale northern bridge camera,390×844](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/terrain-r8-north-bridge-native-camera-r1-390x844.png): an unscaled crop;2×2 floors remain128×64 display pixels. It shows only part of the estate.

The Release placement JSON records every used file/hash, location, footprint, layer and camera transform. PIECES.md lists sizes/pivots and explicit usage limits. Failed patch seams, overly bright bank accents, regular placement rows and earlier river candidates remain preserved locally; none is selected in the final upload allowlist.

## Verification and limits

Independent visual checks cover native/2× PNGs on light,navy and saturated backgrounds, paired registration, repeat seams, dark-versus-ground brightness, bridge deck alignment and the final composition. Mechanical checks cover exact file selection, hashes, dimensions, alpha padding and placement provenance. These are art/export checks; they do not establish runtime performance, device rendering or final human approval.

Final QA and publication inventory are recorded in the Release QA.md. Repository changes are limited to this review document. Historical originals and earlier Releases remain unchanged.
