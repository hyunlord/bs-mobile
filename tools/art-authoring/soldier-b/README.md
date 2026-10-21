# Soldier B walk

Offline packing of Astra's approved Soldier B r3 walk into `unity/Assets/Art/crops-people.png` for `person.militia.*` and
`person.guard.*` (#331).

```sh
gh release download soldier-b-rank-rig-r3-20261019 -D /tmp/soldier-b \
  -p soldier-b-r3-walk16.png -p soldier-b-r3-walk-manifest-draft.json -p candidate-02.json
node tools/art-authoring/soldier-b/pack.mjs /tmp/soldier-b
node tools/first-playable-art.mjs "$PWD" && node tools/wave1a-art.mjs "$PWD"
```

- The delivery is 16 cells of 320x240 without padding. The script checks the sheet hash and each cell's RGBA hash, then cuts every
  frame to the union of the opaque bounds of all 16 cells (83x135), so one pivot serves the whole clip. Pixels are copied, never
  resampled.
- The frames go into the bottom-left band of `crops-people` (two rows of eight, 2 px apart). The band held the old militia and
  guard art and alpha<=1 residue; the script clears it first, so re-running it reproduces the same atlas.
- Both art manifests get the same ten roles: `move` is the 16-frame walk (`frameMs` 36); `idle`, `work`, `muster` and `return`
  use the standing frame, the one with the narrowest boot spread in the 10 px above the foot baseline (frames 4 and 12 tie at
  23 px; frame 4 has the flatter sole). militia and guard stay separate roles so a later tint can tell them apart.
- Scale is 92% of the hero's pixel scale (user decision): `1.081423 / 240 * 0.92` units per source pixel.
- `packed-frames.json` records the hashes, crop, pivot, scale, standing-frame measurements and `cyclePixels`, the root travel of
  one walk cycle (`stride` in the Release's `candidate-02.json`). The View plays the walk by distance with it
  (`PersonSheetWalk`, `GameVisualTokens.PersonWalkCyclePixels`), capped at `GameVisualTokens.PersonWalkMaxCyclesPerSecond` (2.5 cycles per
  second, user decision); faster movement plays at the cap and the feet slide. `PersonSheetWalkTests` checks the manifests against
  this record.
