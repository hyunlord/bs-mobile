# Approved people walks (#436)

Status: source art approved 2026-10-27; game integration and runtime/visual verification pending.
Source: [m3-people-walk-review-433-20261027](https://github.com/hyunlord/bs-mobile/releases/tag/m3-people-walk-review-433-20261027), art issue #433. Release body approval supersedes historical pending strings inside its candidate manifests. No source art is redrawn.

## Reproduce

Download the three `person-{vassal,peasant,returning}-r1-16.png` files from the Release to a local source directory, then:

```sh
node tools/art-authoring/people-walk/pack.mjs artifacts/people433-source
node tools/art-authoring/people-walk/check.mjs
```

The packer reads the fixed historical atlas via Git LFS at `a44bf7ae6a5811de057c8f12410ce4a5d8fb0d3f`, verifies approved source hashes, and extends actors1254×1254 to1920×1940 using the checked-in padded union-crop layout. This baseline includes the approved #411 sprout emblem crops. It preserves the ENTIRE original pixel block including transparent/unreferenced areas. Three clips share the existing actors texture, avoiding a new texture per walk. The54 legacy Unity SpriteImporter crop rectangles move up686 in bottom-origin coordinates to continue reading the same pixels; GUID/fileIDs remain unchanged. Manifest coordinates remain top-origin. Both profiles' unrelated role definitions and bindings remain unchanged.

Union crop across16 frames, no scaling: vassal159×205, peasant186×204, returning187×224. Pivot/worldSize compensate for the crop, preserving source baseline225 and original units-per-pixel. Returning's taller sack does not shrink its shared peasant legs. All48 frames have2px transparent padding. Idle/work/muster/return roles keep their original art.

Texture count delta0; base RGBA memory delta8,609,136 bytes (~8.21MiB), saving1,878,016 bytes (~1.79MiB) versus the previous2048-square candidate, no mipmaps. Existing import max2048 stays unchanged. This is a source allocation estimate, not measured GPU/native memory. Actual texture submission order and runtime batches still require parent PlayMode evidence. New atlas dimensions must not be downscaled by the importer. ArtPreparation already sets NPOT=None and Uncompressed; the1920×1940 dimensions retain that lossless contract. The #411 installed.json remains its historical whole-file receipt; current emblem preservation is verified by equality of the entire baseline1254-square decoded RGBA block.

## Distance playback, not forty-millisecond runtime promise

`frameMs=40` is preserved authoring metadata. PersonSheetWalk plays multi-frame movement by distance with existing2.5cycles/s cap. Each new role uses existing optional `cyclePixels`; old Soldier B roles without it retain102.3603456. No Core speed/timing change.

| Role | Source cycle px (derived double) | Units per source pixel | One cycle Unity distance |
|---|---:|---:|---:|
|vassal|59.985697116093824|.0019652562111801246|.11788726383937306|
|peasant/returning|61.06071581787939|.0020198466614906836|.12333328299297505|

These are source calculations, NOT actual Unity float/runtime measurements. Derivation reproduces approved PeopleGait.cs: `2*(TargetGap + Integral(-25)-Integral(8) + RotatedToe(-25).x-RotatedToe(8).x)`. PNG rows are top-down, corresponding to Unity GetPixel(x,height-1-y); alpha>=128, pixel centers+.5, first-lowest contour tie,2001-point integral at.05degrees. Source hashes are asserted in `derive-stride.mjs`. Vassal has192 contour columns and toe(1420.5,898.5); peasant221 and toe(1474.5,935.5). Returning shares peasant leg source/config/gait (Release SHARED-RIG records equal motion.csv).

To recompute, put approved vassal/peasant `*-rig-source.png`, `*-rig-config.json`, `*-manifest-candidate.json` in a local directory:

```sh
node tools/art-authoring/people-walk/derive-stride.mjs artifacts/people433-source
```

Old common cycle value would be1.7064×/1.6764× too long for these sources. All existing militia/guard tests remain, plus both-profile three-role stride/frame/phase and peasant→returning tests. No claim of no sliding above the existing rate cap. Runtime transition alignment, original activity art discontinuities, replay hashes and visual approval remain explicit downstream checks.
