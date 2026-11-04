# Approved world map art (#703)

Sources are fixed in `sources.json`: SHA-256, exact Release filename, size,
role, top-left anchor, Unity pivot and zero slice border. Original source pixels
are copied into atlas rectangles without scaling, painting, color changes or
alpha changes. `pack` is the existing estate packer. No new dependencies.

## Approval and source boundary

- `slg-world-assets-577-20261030`: user-approved terrain r2 (#580) and unchanged
  approved r1 castles/resources/monsters/strongholds. The r2 manifest's reused
  canopy and mountain support sprites are retained at their supplied 2x size.
- `hero-walk-world-status-20261030-r1`: approved protection, gathering, combat,
  and blank alliance flag. The separate hero walk sheets are already owned by
  the hero integration and are not duplicated here.
- Terrain r1 was rejected and is not registered. River/coast r2 are contextual
  patches, not arbitrary seamless sockets. Their registration does not authorize
  invented world geometry. The offline COMPOSITION fixture is not server world
  layout data. Large patches retain top-left anchors.
- All PNGs have border `[0,0,0,0]`; none is a nine-slice surface.

## Reproduce

Download each `assets[].file` from its `assets[].release` in
`https://github.com/hyunlord/bs-mobile/releases` into `artifacts/world-map/source`.
Do not substitute the similarly named historical r1 terrain files.

```sh
node tools/art-authoring/world-map/pack.mjs
node tools/art-authoring/world-map/verify.mjs
```

Outputs: `unity/Assets/Resources/WorldMapArt/{terrain,objects}.png` and
`world-map.json`. Existing importer conventions retain 4096 maximum dimensions,
no mipmaps, clamp sampling, and platform texture formats. Texture deltas in
`docs/metrics/texture-memory-deltas/world-map-703.json` are ESTIMATE, not measured.
The pixel verifier checks source SHA, metadata and every registered RGBA row.

## Runtime contract and assumptions

`Game.App.WorldMapArt.Load()` exposes `Get(role)` (cached Sprite), `Has(role)`,
`Size(role)` (raw PNG pixel size), and `Pivot(role)` (normalized Unity coordinates).
Rect coordinates are top-left in JSON and converted once during Sprite creation.
Art loading does not choose gameplay state, tier, target, timing or resource count.

Object anchors come from MANIFEST-r2: 96x112 canvas at (48,88), giving Unity
pivot (0.5,24/112). The status Release explicitly has no pivot contract. Registration
assumptions are therefore recorded per source: protection aligns with the same
castle ground anchor; gathering/combat are centered overlays; the pennant attaches
at bottom center. These assumptions need actual Unity visual verification. The
blank cloth has no invented alliance emblem. Stone and all three castle tiers
are registered but do not create new economy or progression states.

The renderer owns placement and projection. Gathering belongs above its resource
site, not the home castle. Protection/combat/tier selection requires authoritative
state. Do not infer absent server fields from these available pictures.

## Verification boundary

Static RGBA identity and manifest checks do not prove Unity import, draw order,
terrain continuity, mobile readability, native performance or C05. These remain
separate integration gates. No Unity execution was performed during registration.
