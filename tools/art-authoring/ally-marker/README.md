# Ally marker B+

Offline packing of Astra's ally marker B+ (a 1.5x helmet band and a 7 px blue plume) over the Soldier B walk of
`person.militia.*` and `person.guard.*` (#352, user decision 2026-10-22).

```sh
gh release download ally-soldier-marker-bplus-20261022 -D /tmp/ally-marker \
  -p ally-marker-Bplus-band-cell0.png -p ally-marker-Bplus-plume-cell0.png -p ally-marker-Bplus-manifest-draft.json
node tools/art-authoring/ally-marker/pack.mjs /tmp/ally-marker
node tools/first-playable-art.mjs "$PWD" && node tools/wave1a-art.mjs "$PWD"
```

- The delivery draws the band and the plume on walk frame 0 only, as separate 320x240 layers. The Soldier B frames in
  `crops-people` are not changed.
- Each walk frame's helmet is found by a whole-pixel shift of frame 0's helmet (rows 2-17, columns 30-63 of the 83x135 crop,
  searched over +-5 px). After the shift, the helmet's top row and its left and right edges 3 rows lower differ from frame 0's
  by at most 1 px in every frame; the script stops otherwise. Shifts are 0 to -3 px up, none sideways.
- The band takes the frame's own alpha (it only recolours the helmet); the plume goes over it. Nothing is resampled.
- One 20x23 cell per walk frame goes into the empty strip at the top of `crops-people` (x 50, y 2, 2 px apart). The marker
  shares the soldier's texture, so it joins the soldier's draw batch.
- Both art manifests get `person.ally-marker.move` (16 frames, pivot 0,0). The View places the cell at
  `GameVisualTokens.AllyMarkerOffsetPixels` from the soldier's foot pivot in the soldier's pose, scale and facing, on the
  same frame index the soldier shows (the standing roles use frame 4).
- `packed-frames.json` records the delivery hashes, every frame's shift and helmet residual, the window, the offset and the
  cells. `AllyMarkerTests` checks the manifests and the placement against it.
