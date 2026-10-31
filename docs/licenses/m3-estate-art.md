# M3 estate map art (#375)

Generated for this project by Astra with the built-in image_gen tool and released for review in GitHub Releases; the user approved their use in the estate map prototype on 2026-10-24. No external stock art is included.

| Release | Used files |
|---|---|
| [m3-estate-autodecor-20261024](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-autodecor-20261024) | buildings-30-body-atlas, buildings-30-shadow-atlas, buildings-manifest, terrain-fallow/-sage/-path/-restoration, yard-earth/-grass/-stone, the 13 yard and roadside props, the 9 empty-land clusters |
| [m3-estate-life-kits-20261024](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-life-kits-20261024) | house/field fences, sheep paddock, river-bend/riverbank/bridge-landing patches, frontier-ink-edge, the two bridges (base and front rail), the three peasant work poses |
| [m3-estate-life-kits-r2-20261024](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-life-kits-r2-20261024) | tree-cluster-4/-5 and the six frontier traces (pad islands removed; replace r1, same names and sizes) |

Runtime files are in `unity/Assets/Resources/EstateArt/`. `tools/estate-art.mjs` builds them from the downloaded Release files listed in `tools/estate-art.json`:

- The building body atlas is copied byte for byte. The shadow atlas is downsampled to half size.
- Every other asset is downsampled with a premultiplied-alpha box filter to its listed `maxSize`, so it is never larger than its source, and packed onto the decor and terrain atlases.
- `estate-art.json` records each source file's SHA-256, its size, pivot, display width and layer.
- For each building stage it also records the body's opaque width inside its 256×320 cell (alpha above 8, as `assembly.js` `trim()` measures it). Buildings are drawn at screen width ÷ that width, like Astra's assembly.
- Pixels are not repainted, recoloured or thresholded. Restoration colours are applied at draw time by the `Game/UI/EstateTint` shader.
- Some sources carry saturated RGB in near-transparent edge pixels; they are kept as delivered.

To take a re-exported file, download it over the old one and run `node tools/estate-art.mjs --source DIR`. `--check` verifies the committed atlases against the manifest.

Use is governed by the applicable [OpenAI output terms](https://openai.com/policies/terms-of-use/) and [commercial-use explanation](https://help.openai.com/en/articles/6783457-what-is-chatgpt), or the applicable [services agreement](https://openai.com/policies/services-agreement/). Generated output is not classified as CC0; ownership language does not establish uniqueness or third-party clearance. Release status was "user art review pending" for the autodecor and life kits; the integration follows the user's instruction of 2026-10-24 and is not a final art approval.

## Approved construction, riverbank/tree and carrier additions (#426, 2026-10-27)

The user's integration instruction approves these project-generated assets; it does not add a third-party asset licence. The same OpenAI-output terms above apply. Only these revisions enter the runtime atlas:

| Release | Selected source files and anchor contract |
|---|---|
| [m3-construction-review-407-20261026](https://github.com/hyunlord/bs-mobile/releases/tag/m3-construction-review-407-20261026) | building-stage1/2 lit/dark **1x**, building-stage3 lit/dark **r2-1x**, field-stage1/2/3 lit/dark 1x; timber-pile, stone-pile, ladder, wheelbarrow, worker-hammer, worker-carry 1x. Stages: 136×160, ground anchor (68,124). Each small prop retains its ASSET-LIST ground anchor. |
| [m3-riverbank-clusters-review-416-20261026](https://github.com/hyunlord/bs-mobile/releases/tag/m3-riverbank-clusters-review-416-20261026) | Eight bank shapes and six tree groups: lit r1 **1x**, dark **r2-1x** only. ASSET-LIST.md size/anchor, ASSET-LIST-r2.md dark revision. Source PNG colours are unchanged. |
| [m3-walk-next-review-423-20261027](https://github.com/hyunlord/bs-mobile/releases/tag/m3-walk-next-review-423-20261027) | worker-carry-se/sw-r1 individual frames 00–15, full 256×256 cells. ATLAS-CONTRACT overrides obsolete 4px inset metadata. Ground anchors SE(122,231), SW(134,231), 50ms/frame, cell display width66. |

The source registry and generated manifest list every filename and SHA-256. Sources are not copied into a new distribution ZIP. Decor atlas repacking changes its rectangles, but building-body, building-shadow and terrain atlas bytes are unchanged. New sources are packed at their native 1x/full-cell size without downsampling. Runtime water-only masking removes embedded old river-bank stones without modifying the approved source file. Its mask edge and bank placement still require actual game capture review.
