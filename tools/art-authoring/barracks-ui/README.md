# Approved training r2 parts (#589)

Source: GitHub Release `slg-screen-assets-581-20261030`, `TRAINING-MANIFEST-r2.json`
and `MANIFEST-r2.json`. The source manifest's old `PENDING_USER` flag is historical;
user approval was supplied in the #583 implementation conversation.

The 12 training parts are supplied in 1x and 2x (24 PNGs). Runtime uses the 2x copies,
plus existing progress track/fill and approved levy/guard portraits, packed losslessly
into one 1024x828 texture with 4px gutters. No source image is recolored or repainted.
The runtime JSON preserves file names, source hashes, rects, central pivots and LBRT
nine-slice borders. Pixels are loaded through the existing Resources/Sprite pattern.

Recreate after downloading the named Release files into `artifacts/barracks-r2`:

```sh
node tools/art-authoring/barracks-ui/pack.mjs artifacts/barracks-r2
```

Memory estimates, not device measurements: Mac RGBA32 3,391,488 bytes;
iOS/Android ASTC4 847,872 bytes each. One additional UI texture is used; actual draw
calls and visual QA remain a Unity runtime gate. The queue retains GDD15's active
cancellation prohibition; only waiting jobs expose a cancellation confirmation.
Quotes, costs, UTC completion and occupancy come from the server DTO, never from
client-side multiplication or the device clock. Missing quotes disable training.
