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

Primary action provenance: `TRAINING-README-r2.md` explicitly reuses #563's blue
primary button. There is no new rectangular training-submit button in the 12-part
manifest. `Barracks.Train` therefore keeps `ui.button.primary` →
`PortraitArt.Get("ui-btn-blue")`; applying `training-cancel-active` or the square
quantity backing here would use the wrong approved component. Quantity value,
total cost, progress, cancellation and pending refund use the new atlas pieces.

## r3 resource icon reuse

`PortraitV0/r-iron.png` is byte-identical to approved #589 `research-iron-yield-r1-2x.png` (96×96, existing painted iron ingots), from Release `slg-screen-assets-581-20261030`. SHA256 `72cc5a40aee76d6f7a218710d01b2ce0a98deaf24a75ec61d2626e1db11b6653`. No newly generated icon; reused in cost and server wallet. Gems have no server balance field yet and display `—`.

## r3 wallet icons (2026-10-31)

`wallet-iron` (Release `wallet-iron-20261031-r1`, `wallet-iron-r1-44px.png`) and `wallet-gem` (Release `hero-basic-gem-20261030-r1`, `wallet-gem-r1-44px.png`) are packed at 2x into free space of the same 1024x828 atlas; source hashes are in `training.json`. Iron (wallet and cost rows) and gems (wallet) use them; grain and timber keep the PortraitV0 icons. Recreate with the 44px files downloaded into `artifacts/wallet-icons`:

```sh
node tools/art-authoring/barracks-ui/pack.mjs artifacts/barracks-r2 artifacts/wallet-icons
```
