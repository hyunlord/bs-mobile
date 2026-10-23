# Estate building effects

Offline packing of Astra's approved M2 building effects into `unity/Assets/Art/estate-effects.png`. The effects are the
watchtower arrow and impact, the well heal ring, and the granary inward ring r2 (#347, user decisions 2026-10-23).

```sh
gh release download m2-building-fx-20261022 -D /tmp/fx-a
gh release download m2-granary-magnet-r2-20261022 -D /tmp/fx-b
node tools/art-authoring/estate-effects/pack.mjs /tmp/fx-a /tmp/fx-b
node tools/wave1a-art.mjs "$PWD"
```

- The script checks each delivered file's sha256 before packing.
- Each cell is reduced to its approved display size at the 115 px hero (240 px = 1.081423 Unity units). The reduction is
  an area average over premultiplied alpha; nothing is redrawn.
- The arrow (46 px) and impact (14 px) keep 2 texels per display pixel.
- The heal ring (170 px) and granary ring (576 px canvas, an exact half of the delivery cell) keep 1.
- The rings keep Astra's 0.6 alpha cap.
- `wave1a-manifest.json` gets these roles, all on the `estate-effects` atlas:
  - `fx.estate.watchtower.arrow`: pivot at the tip.
  - `fx.estate.watchtower.impact`
  - `fx.estate.well.heal_ring`: 4 frames × 100 ms, pivot at the ground ellipse centre.
  - `fx.estate.granary.magnet_ring`: 4 frames × 100 ms, centred.
- `packed-frames.json` records the delivery hashes, the cells and the atlas sha256.
- `EstateViewTests.EffectArtKeepsTheApprovedDisplaySize` checks the roles.
- `WorldEstate.cs` draws them from Core events and frames.
