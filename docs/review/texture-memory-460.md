# Texture memory inventory and budget proposal (#460)

Status: proposal / static audit, 2026-10-28. Source main `437d62a4c8f25700979f22cbf0877f648b978aaa`.
No production change, compression acceptance, Unity execution, device measurement or memory-performance claim.

## Finding

Ordinary combat, estate and UI art uses imported Texture2D assets through Resources, so platform compression is a viable **trial path**, not blocked by PNG decoding. The combat preparation script currently forces uncompressed import. Changing only a generated `.meta` default can be overwritten on the next preparation.

The [94-row inventory](texture-memory-460.csv) contains original PNG dimensions, PNG file bytes, hypothetical RGBA32 bytes, scopes, mip/readability and every serialized platform setting. Paths are unique; shared combat textures are counted once in the union. PNG file bytes are download/storage cost, not decoded/GPU memory. All figures below exclude meshes, materials, buffers, fonts, render targets, driver overhead and transient decode/upload copies.

## Loading paths and ownership

| Category | Source evidence at the pinned main | Loading / retention |
|---|---|---|
| Combat | `unity/Assets/Game/View/Art/ArtCatalog.cs:57–65`, `unity/Assets/Game/Editor/ArtPreparation.cs:27–51` | Profile registry references every manifest atlas; FromJson creates role visuals, cache is keyed by registry. The registry set is not the set of textures actually drawn in one frame. |
| Estate | `unity/Assets/Game/App/EstateArt.cs:21–34,58–75` | JSON roles; imported `Resources/EstateArt` textures cached lazily by atlas name. |
| Portrait UI | `unity/Assets/Game/View/PortraitArt.cs:15` | Imported `Resources/PortraitV0` textures, cached sprite references. |
| Title | `unity/Assets/Game/View/FallowTitle.cs:17,25`, `unity/Assets/Game/App/PortraitRunPanels.cs:81` | Imported title background/logo. |
| Extra hero | `unity/Assets/Game/App/RunCoordinator.HeroMotion.cs:50–56`, `unity/Assets/Game/View/WorldHeroRig.cs:26–61`, `unity/Assets/Game/View/HeroLiveRig.cs:53–56` | Normal ConfigureHeroMotionComparison calls PrepareHeroKnight even without a comparison flag. This allocates the legacy live rig and loads its parts/sword resources. HeroMotion baked-actions is an optional comparison texture. Do not label every extra resource dormant. |
| External review PNG | `unity/Assets/Game/View/WorldHeroProduction.cs:110–129`, `WorldHeroConceptReview.cs:18` | Optional review configuration reads external bytes and LoadImage into a runtime texture. This bypasses importer settings. Production review constructs RGBA32/no-mip and requests non-readable after decoding. Not the regular catalog path. |

No unload-on-screen-exit assumption is made: catalog/static caches can retain resources across screens. Resources presence alone does not prove residency, and a manifest reference does not prove the texture is visible. A future memory capture must log both loaded objects and actually submitted texture IDs.

## Inventory arithmetic

MiB = bytes / 1,048,576. RGBA32 without mipmaps = width × height × 4.

| Scope | Unique PNG count | RGBA32 bytes | MiB |
|---|---:|---:|---:|
| first-playable registry | 17 | 123779312 | 118.045151 |
| wave1a registry | 34 | 271536368 | 258.957260 |
| Estate resources | 4 | 34799616 | 33.187500 |
| Portrait UI resources | 50 | 8641372 | 8.241055 |
| Title resources | 2 | 12587560 | 12.004433 |
| Extra hero resources | 4 | 35647456 | 33.996063 |
| Union (FP registry is a subset of wave at this source) | 94 | 363212372 | 346.386311 |

The union is **not a measured scene peak**. A first-playable registry-only loading estimate is 118.05 MiB; wave registry-only is 258.96 MiB. Additional cached UI/estate/legacy rig resources, dynamic fonts and rendering allocations must be added according to actual screen history. Loading both catalogs must deduplicate shared Texture2D instances, not add both table rows. No exact Android, iOS or Mac resident total has been measured.

### Three largest combat-registry textures

| Asset under unity/Assets/Art | Dimensions | RGBA32 bytes / MiB | 4×4 16-byte blocks | 6×6 16-byte blocks |
|---|---|---:|---:|---:|
| hero-knight.png | 2564×2164 | 22193984 / 21.165833 | 5548496 | 2472128 |
| fallow-enemy-rigs.png | 2048×2304 | 18874368 / 18.000000 | 4718592 | 2101248 |
| fallow-hero-b-layers.png | 2048×2048 | 16777216 / 16.000000 | 4194304 | 1871424 |

The optional `HeroMotion/baked-actions.png` also ties at 16 MiB; estate-terrain is 2048×2008 / 15.6875 MiB. The three selected entries are wave-registry references; the old B layer may not be drawn by the current approved hero. Compression-independent follow-up: inspect whether unused registry roles and the normally allocated hidden legacy rig can be omitted safely. This audit does not remove them.

Block estimates use `ceil(width / blockWidth) × ceil(height / blockHeight) × 16`. They are arithmetic candidates, not selected formats or promises of hardware support. A 4×4 block candidate can represent BC7 or ASTC 4×4; ASTC 6×6 has the other footprint. Target-specific format support, fallback and visual quality must be verified separately. Thin ink, small symbols, alpha edges and moving legs are required review samples.

## Importer state, platforms and missing runtime quantities

All 94 inventoried PNGs have mipmaps disabled and isReadable false. DefaultTexturePlatform is Automatic format (-1), Uncompressed (0), with no active platform overrides. Standalone/Android/iOS blocks have overridden=0: their serialized compression field, sometimes 1, is **inactive**, not evidence of compressed output. CSV preserves these inactive values rather than silently interpreting them as effective settings.

Combat ArtPreparation explicitly sets Default texture type, sRGB, alpha from input, alpha transparency, clamp/bilinear, no mip, uncompressed, NPOT=None and size ceiling NextPowerOfTwo(max dimension). It temporarily enables readability for alpha-padding validation, then disables it (`ArtPreparation.cs:34–46`). Other resources are governed by their checked-in metadata. The table deliberately uses a uniform RGBA32 planning estimate on Mac/iOS/Android; metadata alone does not prove the final TextureFormat (for example, an image without effective alpha may import as RGB24). actual imported format and resident bytes remain **NOT_MEASURED** on all three. No ASTC/ETC2/BC production override is enabled.

A readable CPU image copy is not retained for these 94 assets after preparation. This does not mean zero CPU allocation overall. Preparation briefly uses readable textures and GetPixels32. Runtime Sprite/UV arrays, loading transients, native allocation overhead, GPU row alignment and driver copies are excluded. Mip overhead is zero for the inventoried base-level assets; if future trials enable mipmaps, sum each level explicitly (roughly +1/3 for large square chains, not a blanket exact constant).

### Separate dynamic and generated resources

- Embedded GowunBatang Regular/Bold are generated by `tools/prepare-unity.sh`, not tracked PNGs. Source/size/hash/importer contract: `tools/font-assets/manifest.json` and the two `.ttf.meta` templates; provenance: `unity/Assets/Resources/Fonts/GowunBatang/PROVENANCE.md`. Their glyph atlas size/format depends on requested text and has not been measured.
- `FontProvider.cs:25–38,42–48` creates OS fonts and requests characters; OS fallback varies by platform. Dynamic font textures must be inventoried from the runtime Font material, not estimated from TTF download size.
- Estate night mask (`PortraitMetaPanels.EstateMap.cs:764–809`) is runtime RGBA32/no-mip. Width/height = area dimensions × effective texelsPerCell; Apply(false) retains CPU readability. Budget GPU base bytes **plus** CPU image bytes, temporary Color32 array, and blur scratch separately. Do not charge this mutable mask as a compressible imported PNG.
- OfflineReplayCapture creates RGB24 readback images; review/test cameras create RenderTextures. These are capture overhead, not ordinary gameplay atlas cost. Record them separately when measuring a capture session.

## Recent PR costs: own-base comparisons, not cumulative totals

Calculated by reading both profile manifests at each PR base/head, summing unique atlas dimensions. These are pending branches, not additions already present in pinned main.

| PR | Base → head | first-playable delta | wave delta | Explanation |
|---|---|---:|---:|---|
| #432 | 2c7987c → 9abe1dd | +26624000 B (+25.390625 MiB) | 0 | New 2080×3200 combined five-enemy atlas. Historical actors remains for non-enemies. Internal failed separate-atlas→combined repack cost is only +4992000 B (+4.760742 MiB); do not report that as the entire PR cost. |
| #439 | 2c7987c → 9ed80aa | +8609136 B (+8.210312 MiB) | +8609136 B (+8.210312 MiB) | Shared actors1254²→1920×1940. Both profiles share one object: union delta once, not twice. Earlier2048² variant was +10487152 B; compact packing saves1878016 B (1.791016 MiB). |
| #458 | 437d62a → 51a294a | 0 | +18874368 B (+18 MiB) | Shared fallow-enemy-rigs2048×2304→3072², 18→36 MiB. |

These deltas are not combined-branch runtime measurements. For a future merged baseline, regenerate the inventory and deduplicate by actual texture asset identity. Do not add repeated branch deltas to already-expanded atlases.

## Budget proposal and next small PRs

[Proposal] A 150 MiB gameplay-resident texture target is **not currently met** by the wave registry estimate alone (258.96 MiB). Even theoretical 6×6 compression of only the top three leaves wave at209.94 MiB, before extra cached UI/estate/fonts/masks; the full inventory becomes297.37 MiB. These are format-arithmetic scenarios, not measured savings or approved settings. Treat150 MiB as a target requiring further loading/packing/compression decisions, never a claimed pass. Apply any future budget to a named profile and lifecycle checkpoint (cold title, fresh combat, estate then combat), while retaining the independent inventory total.

1. Adopt this pinned inventory as a **report-only baseline**, not a hard pass limit. Track registry, estate, portrait, extra hero, mutable masks, fonts and capture costs separately. A total-image budget must not reward splitting one texture into many batches.
2. Compression trial: isolated branch, exact three source images and target settings, no production application before user visual approval. Compare full-size alpha borders, 115px actors and moving/crowded frames. Record PNG/source unchanged, actual imported TextureFormat/graphicsFormat, dimensions, mipmapCount, isReadable and Profiler.GetRuntimeMemorySizeLong on each target. Device/driver support and fallback cannot be proven by this document.
3. Proposed later Node audit (not implemented here): parse canonical profile manifests plus every nested Resources PNG; read IHDR (resolve LFS objects, fail explicitly if missing); parse default/platform metadata and size caps; deduplicate paths/GUIDs; emit CSV baseline/delta and per-format block estimates. Dynamic/generated resources remain explicit UNKNOWN rows. Compare base and head, not total PR descriptions. No dependency and no new CI hard limit until runtime/visual baselines are approved.
4. Later loading audit: separate all registry references from actually drawn roles and optional review objects, especially legacy hero initialization. Only after ownership/behavior review, propose removing unnecessary loads in another PR. No edits in this audit.

## Verification and limits

Read-only git inspection and Node arithmetic at the pinned SHA; no Unity, build, process termination or shared-Mac lock. The CSV was generated from the two manifest atlas arrays and all tracked nested Resources PNGs using PNG IHDR and LFS object content. Metadata was read from the same Git ref, not a potentially reimported worktree. Reproduction inputs are the source SHA, paths and dimensions in CSV; proposed reusable CLI remains a separate task. The full inventory sum is363212372 bytes, 94 unique rows; all mip/readability flags are0. Source file line numbers above refer to the pinned SHA.

Graft located loader code, but returned an older implementation; current git source was read directly and overrides the stale search result. Actual Mac/iOS/Android texture formats, resident memory, compression quality, loading peak, frame timing and user visual approval remain unmeasured. This document does not imply any of those gates passed.
