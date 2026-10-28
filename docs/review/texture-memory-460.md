# Texture memory inventory and budget proposal (#460)

Status: proposal / static audit, 2026-10-28. Current source main `2074f57478f56707d91094a3fa6412bd033468db`, after approved #431. Historical source437d62a is retained below.
No production change or compression acceptance. This documentation lane ran no Unity; a separate Mac Editor import probe is attributed below. No mobile/device-gameplay memory-performance claim.

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
| Estate resources | 4 | 49856512 | 47.546875 |
| Portrait UI resources | 50 | 8641372 | 8.241055 |
| Title resources | 2 | 12587560 | 12.004433 |
| Extra hero resources | 4 | 35647456 | 33.996063 |
| Union (FP registry is a subset of wave at this source) | 94 | 378269268 | 360.745686 |

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

Combat ArtPreparation explicitly sets Default texture type, sRGB, alpha from input, alpha transparency, clamp/bilinear, no mip, uncompressed, NPOT=None and size ceiling NextPowerOfTwo(max dimension). It temporarily enables readability for alpha-padding validation, then disables it (`ArtPreparation.cs:34–46`). Other resources are governed by their checked-in metadata. The table deliberately uses a uniform RGBA32 planning estimate on Mac/iOS/Android; metadata alone does not prove the final TextureFormat (for example, an image without effective alpha may import as RGB24). Mac Editor formats/object-memory were subsequently probed as detailed below; iOS/Android formats and all gameplay scene-resident totals remain **NOT_MEASURED**. No ASTC/ETC2/BC production override is enabled.

The verified flag is isReadable=false for these94 assets after preparation. The flag alone does not determine whether CPU/native copies exist, and does not imply zero CPU allocation. Preparation briefly uses readable textures and GetPixels32. Runtime Sprite/UV arrays, loading transients, native allocation overhead, GPU row alignment and driver copies are excluded. Mip overhead is zero for the inventoried base-level assets; if future trials enable mipmaps, sum each level explicitly (roughly +1/3 for large square chains, not a blanket exact constant).

### Platform planning table (not runtime measurements)

| Target | FP registry RGBA32 estimate | Wave registry RGBA32 estimate | Full 94 PNG inventory estimate | Actual imported/resident format |
|---|---:|---:|---:|---|
| Mac | 118.045151 MiB | 258.957260 MiB | 360.745686 MiB | 91 unchanged assets: historical Editor probe; 3 changed: NOT_MEASURED |
| iOS | 118.045151 MiB | 258.957260 MiB | 360.745686 MiB | NOT_MEASURED |
| Android | 118.045151 MiB | 258.957260 MiB | 360.745686 MiB | NOT_MEASURED |

Per-asset platform estimates equal each CSV row's rgba32_bytes under this deliberately uniform assumption. Identical columns express the absence of active overrides, not identical real hardware allocation or support. CSV platform settings retain each target's serialized fields.

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

#458 uses its current PR base437d62a for this inventory comparison; its earlier runtime comparison used main59d4080. The old atlas dimensions are2048×2304 in both; this document does not relabel the earlier execution source. These deltas are not combined-branch runtime measurements. For a future merged baseline, regenerate the inventory and deduplicate by actual texture asset identity. Do not add repeated branch deltas to already-expanded atlases.

## Budget proposal and next small PRs

[Proposal] A 150 MiB gameplay-resident texture target is **not currently met** by the wave registry estimate alone (258.96 MiB). Even theoretical 6×6 compression of only the previously selected three combat textures leaves wave at209.94 MiB, before extra cached UI/estate/fonts/masks; the current full inventory becomes311.73 MiB (historical437d62a inventory297.37 MiB). These are format-arithmetic scenarios, not measured savings or approved settings. Treat150 MiB as a target requiring further loading/packing/compression decisions, never a claimed pass. Apply any future budget to a named profile and lifecycle checkpoint (cold title, fresh combat, estate then combat), while retaining the independent inventory total.

1. Adopt this pinned inventory as a **report-only baseline**, not a hard pass limit. Track registry, estate, portrait, extra hero, mutable masks, fonts and capture costs separately. A total-image budget must not reward splitting one texture into many batches.
2. Compression trial: isolated branch, exact three source images and target settings, no production application before user visual approval. Compare full-size alpha borders, 115px actors and moving/crowded frames. Record PNG/source unchanged, actual imported TextureFormat/graphicsFormat, dimensions, mipmapCount, isReadable and Profiler.GetRuntimeMemorySizeLong on each target. Device/driver support and fallback cannot be proven by this document.
3. Proposed later Node audit (not implemented here): parse canonical profile manifests plus every nested Resources PNG; read IHDR (resolve LFS objects, fail explicitly if missing); parse default/platform metadata and size caps; deduplicate paths/GUIDs; emit CSV baseline/delta and per-format block estimates. Dynamic/generated resources remain explicit UNKNOWN rows. Compare base and head, not total PR descriptions. No dependency and no new CI hard limit until runtime/visual baselines are approved.
4. Later loading audit: separate all registry references from actually drawn roles and optional review objects, especially legacy hero initialization. Only after ownership/behavior review, propose removing unnecessary loads in another PR. No edits in this audit.

## Verification and limits

Read-only git inspection and Node arithmetic at the pinned SHA; no Unity, build, process termination or shared-Mac lock. The CSV was generated from the two manifest atlas arrays and all tracked nested Resources PNGs using PNG IHDR and LFS object content. Metadata was read from the same Git ref, not a potentially reimported worktree. Reproduction inputs are the source SHA, paths and dimensions in CSV; proposed reusable CLI remains a separate task. The current full inventory sum is378269268 bytes,94 unique rows; all mip/readability flags are0. Loader line references unchanged by431 retain their earlier locations; EstateArt and EstateMap source shifted in431, so use the named methods and the refresh references below.

Graft located loader code, but returned an older implementation; current git source was read directly and overrides the stale search result. Actual iOS/Android texture formats, gameplay resident memory, loading peak and frame timing remain unmeasured. Separate Mac Editor import and compression evidence below does not establish gameplay-memory or user-approval gates. This document does not imply any of those gates passed.

### Recheck committed arithmetic without Unity

Run from repository root; this reads the CSV and does not implement a new checker or CI policy:

```sh
node <<'NODE'
const fs = require('node:fs');
const rows = fs.readFileSync('docs/review/texture-memory-460.csv', 'utf8')
  .trim().split('\n').slice(1).map(line => line.split(','));
if (new Set(rows.map(r => r[1])).size !== rows.length) throw Error('duplicate path');
for (const r of rows) if (+r[6] !== +r[3] * +r[4] * 4) throw Error(r[1]);
const total = rows.reduce((sum, r) => sum + +r[6], 0);
console.log({ rows: rows.length, rgba32Bytes: total, MiB: total / 1048576 });
for (const r of rows.slice(0, 3)) console.log(r[1], {
  block4Bytes: Math.ceil(+r[3] / 4) * Math.ceil(+r[4] / 4) * 16,
  block6Bytes: Math.ceil(+r[3] / 6) * Math.ceil(+r[4] / 6) * 16
});
NODE
```

Expected94rows /378269268bytes. This verifies committed arithmetic, not extraction completeness or live import settings; those require the pinned source inspection described above.

## Post-#431 refresh and historical baseline

#431 merged as2074f574 on2026-10-28. No additional PNG paths: construction stages, static props and carrying-worker SE/SW16 frames use the existing estate atlas. EstateCarryView.cs:19–27 resolves worker-carry-se/sw through EstateArt.Get; EstateConstructionArt.cs:20–27 resolves construction stage roles through the same imported atlas loader. Current inventory is94 images/360.745686 MiB; historical437d62a was94/346.386311 MiB. Combat registries and other categories are unchanged.

| Changed atlas | Previous dimensions | Current dimensions | RGBA32 delta |
|---|---|---|---:|
| estate-decor | 2048×740 | 2048×2328 | +13008896 B /12.406250 MiB |
| estate-buildings | 1280×1920 | 1280×2240 | +1638400 B /1.562500 MiB |
| estate-building-shadows | 640×960 | 640×1120 | +409600 B /0.390625 MiB |
| #431 total | | | +15056896 B /14.359375 MiB |

The **current global** largest three are hero-knight21.165833 MiB, estate-decor18.187500 MiB, fallow-enemy-rigs18 MiB. The earlier **combat-registry** top-three compression selection is unchanged; do not confuse it with this refreshed global ordering. CSV sorts all categories by uncompressed planning bytes.

## Separate Mac Editor import measurements

Probe source d257dc7 (main437d62a plus Editor-only diagnostic), Unity6000.6.4f1, Apple M4 Max/Metal. The separate probe loads each texture via AssetDatabase.LoadAssetAtPath and records TextureFormat, dimensions, mipmapCount, isReadable and Profiler.GetRuntimeMemorySizeLong. Original evidence: artifacts/astc-trial/full-rgba-r1/textures/inventory.json in the separate bs-mobile-astc-trial-460 worktree. See [compression trial PR #465](https://github.com/hyunlord/bs-mobile/pull/465) and [selected review evidence](https://github.com/hyunlord/bs-mobile/releases/tag/astc-texture-trial-461-20261028).

Historical94:93RGBA32 and1RGB24 (title-background), all mipmapCount1/readablefalse. Sum of per-object Profiler values724986304 B/691.400818 MiB. This is an **Editor object-memory sum**, not GPU-only bytes, process memory, a measured gameplay scene peak or a mobile result. Its roughly2×relation to source arithmetic does not prove a particular CPU-copy implementation.

Current CSV carries91 rows only where both PNG and importer metadata are byte-identical between437d62a and2074f574, retaining the original probe source. Their object-memory sum688283120 B/656.397934 MiB is likewise a partial historical-object sum. The three replaced estate textures are NOT_MEASURED, not assigned the old runtime values. All current94 static defaults remain no mip/readablefalse/no active platform override. No new Unity execution is claimed.

Trial source preservation (PNG LFS SHA256 unchanged after431; metadata also unchanged):

- hero-knight:76c75e804a9fb782207e26e2295bca7c74b8c8bb29eeb055d557610a7e8fe4c6
- fallow-enemy-rigs:5a69aaf13a67ea6885234be74e4d984e00b7b77323f113024b15e05ebebf8bde
- fallow-hero-b-layers:945eed1f6592292ac9138c3000f17422d90d8a9a351020533b3174b8f005dce4

The separate actual encoding trial reports combined three-texture payload55.1658→13.7915MiB (ASTC4×4) or6.1462MiB (ASTC6×6). Payload is **not runtime RAM**. ASTC6×6 hero gutter has102 alpha samples with max1, while global image alpha max-error252 is a different metric. Compression remains review-only: no production application or quality acceptance is implied. The refreshed estate-decor global top-three entry was not part of that completed trial. An additional estate trial is pending; this report does not count it as completed.
