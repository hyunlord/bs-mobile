# Hero B continuous contact contract (#209)

Authoring is staged with `node tools/art-authoring/hero-b/bake.mjs [output-directory]`; default is `artifacts/grounded-motion/hero-new`. No Unity file is overwritten. `verify.mjs [directory]` checks authored pixels, not runtime zero-slip.

- Composite atlas: 2048×2048; 512px cells, 4columns. Walk0–11, idle12, attack13, hit14.
- Layer atlas: 2048×2048; four1024×1024 sheets in quadrants. Cell256px. Top-left back, top-right far leg, bottom-left near leg, bottom-right front. Composition order back → far → near → front. Alpha is straight, not premultiplied.
- Back contains cape and far arm; front contains torso/head/near arm/sword. Legs have independent source pixels so inverse warping reveals no baked silhouette hole.
- `baked-frames.json`: actual opaque sole coordinates at512px authoring resolution, joint geometry, stance flags, phase, cycle travel256px, hip/full-weight lines276/376 (top-left coordinates). Contact flags only cover walking; stop/turn/reverse/knockback/action pose releases contacts.
- Mask256² is the authored scalar deformation weight (R=far,G=near). It is a linear vertical ramp from zero at hip276px to one at376px and below. Shader can evaluate this exact ramp analytically instead of allocating another sampler.

## Dedicated hero shader, one existing batch quad

Material-property uniform `_RigFrame=(column/4, 1-(row+1)/4, .25,.25)` locates a frame inside each normalized sheet. Frame-local uv is the quad's0–1 coordinate. Sheet offsets in bottom-left coordinates: back(0,.5), far(.5,.5), near(0,0), front(.5,0). Final texture uv is `offset + .5 * (_RigFrame.xy + localUv * _RigFrame.zw)`. Clamp inside the cell by half a packed texture texel and keep transparent padding.

`_RigFarDelta.xy` and `_RigNearDelta.xy` are continuous desired layer displacements in frame-local uv units; supply zero for released contacts. `_RigWarp=(hipY,fullY,0,0)=(1-276/512,1-376/512,0,0)`. Body layers use unwarped uv. Leg layers use the exact inverse below. Hero-only uniforms avoid expanding arrays for every ordinary SpriteBatch instance.

The rendered body frame must use full512px extent and pivot(280/512,92/512). If the generic manifest keeps inset504px rectangles, the dedicated renderer expands world dimensions by512/504 before applying full-frame uv. World size in the manifest fragment is a candidate .85² and must be compared at actual native screen scale.

## Exact inverse linear lower-limb deformation

Forward deformation is `p = q + w(q.y) * delta`, where `h=hipY`, `f=fullY`, `r=h-f`, `w=clamp((h-q.y)/r,0,1)`.

For `delta.y < r` the inverse is unique:

- if `p.y >= h`: `q=p`;
- else if `p.y <= f+delta.y`: `q=p-delta`;
- otherwise `q.y=(p.y-h*delta.y/r)/(1-delta.y/r)`, `q.x=p.x-((h-q.y)/r)*delta.x`.

This leaves hips fixed and translates the sole fully. Per stance save world contact `C`. For current rendered origin `P`, signed dimensions `S`, baked frame sole `s` and pivot `v`, desired delta is `(C-P)/S - (s/512-v)` (convert top-left soleY to bottom-left first). Thus the transformed sole is algebraically C, including between atlas changes. Actual GPU raster contact still requires native sampling evidence; this document does not claim that the authoring check proves zero pixel drift.

Phase is accumulated rendered locomotion distance divided by `cycleTravelPixels * worldWidth/512`. The 1.3× switch changes distance, naturally changing cadence. Never advance phase using wall time or nominal speed. Camera movement is excluded. Release on discontinuity/turn/reversal/knockback rather than distorting a pinned leg beyond its anatomical reach. Reacquire with the current visible sole and record the release reason.
