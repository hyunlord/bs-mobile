# A knight: actual3D production experiment — issue256

Status: **visual quality not accepted**. Revision6 is a completed production-method comparison candidate: idle plus seven requested motions, eight directions each, 64 atlases and 2,000 frames. All frames passed the nonempty/edge-clipping check. This is an artifact check, not user approval. Failed earlier models, direction mapping and death framing remain preserved. No Unity/Core files are changed by the authoring tools.

## Sources and rights (checked2026-10-12)

- Identity reference: user supplied `/Users/rexxa/Downloads/heroes.png`, leftmost traditional adult knight. Other heroes are not incorporated.
- Model: project-authored closed meshes, articulated hands, plate shells, cloth panels, physical sword. No billboard reference planes. External source generation is `artifacts/m1-256/authoring/hero-a-3d/produce.py`; `.blend` snapshots are under `artifacts/m1-256/3d/revision*/`.
- Diffuse material: external `artifacts/m1-256/authoring/hero-a-3d/materials/knight-gouache-atlas.png`, generated using built-in imagegen with the approved reference. Four quadrants are adult face, ivory quilt, cobalt cloth, blond hair. Mapped as UV color onto3D geometry and packed into the model. It is **not** Quaternius art or described as CC0.
- Motion1: Quaternius [Universal Animation Library](https://quaternius.com/packs/universalanimationlibrary.html), [author upload](https://opengameart.org/content/universal-animation-library), Standard free package. Motion2: [author upload](https://opengameart.org/content/universal-animation-library-2), Standard free package. Both downloaded packages include explicit CC0-1.0 licenses, copied into `licenses/` with line-ending/trailing-space normalization only; original bytes are preserved under local `artifacts/m1-256/authoring/licenses-original/`.
- Blender4.5.14 LTS arm64: official `download.blender.org/release/Blender4.5/` distribution. Read-only mounted under local artifacts; not installed as a runtime dependency. Cycles CPU, fixed maximum4threads; no GPU/Unity jobs.

## Workflow

The named scripts below are external authoring inputs under `artifacts/m1-256/authoring/hero-a-3d/`, not Git files. Restored source layouts are described below.

1. `inspect_rig.py` imports library1 to `imported-stock.blend`; `inspect_motion2.py` imports library2 to `imported-stock2.blend`.
2. `produce.py` authors the true volumetric character and saves one front¾ review frame and `.blend`. Current output `revision6`.
3. `inspect_stock_motion.py` records skeletal trajectories; clip names A/B/C alone are **not** evidence of overhead/spin semantics.
4. `bake_preview.py` defaults to E walk/light-slash early review. `KNIGHT_FULL=1` renders idle plus the7requested motions in8directions. `KNIGHT_POSE_SAMPLE=1` restricts to4diagnostic poses and is not a complete delivery. It verifies equivalent anatomical bind positions before copying weights between the two original source skeletons. It does not assign differently named source actions to an incompatible rig.
5. `pack_preview.py` assembles rendered frames into atlases without repainting or synthesizing body imagery.

## Motion provenance and limitations

- Walk: unretimed `Walk_Loop` from library1,32 samples across its original32frame/24fps duration in the full bake (historical early preview used24).
- Light slash: unretimed `Sword_Regular_A` from library2,12 samples across its original10.4frame/24fps duration in the full bake (historical early preview used16).
- Cape lag is an explicit authored render-only vertex deformation. Stock skeletal curves are unchanged.
- Full comparison heavy uses `Sword_Attack` from library1, spin uses `Sword_Regular_C` from library2. Skeletal trajectory inspection measured444degrees chest yaw in C; the heavy clip raises hand to1.54m then lowers it to.58m. Selected poses are reviewed before the full bake; action names alone do not prove the requested visual effect.
- Output cells384×512; atlas8columns. `fps = sampleCount / sourceDuration`, preserving timing instead of assuming every clip is24fps.
- Actual evaluated skinned mesh vertices projected through the review camera measure716.19px body /92.94px head =7.70585heads (`rendered-landmarks.json`). This numerical measurement does not establish art acceptance.
- No runtime memory/performance claim is made from CPU authoring times. No art PASS is claimed from frame count, license, model ratio, or successful render.
- Current known art limits: overly geometric face/armor, cap-like hair, cloth articulation remains a simple authored lag. These limits remain visible in the comparison candidate. Earlier incorrect source-rig slash preview remains in revision1 evidence and is not delivered.

## Diffuse generation prompt

Use case: stylized-concept. Actual3D knight material atlas, square four equal quadrants, no margins/gutters/text. Only the LEFTMOST adult blond traditional knight in the supplied image is the identity/style reference. Detailed ink and matte gouache. Top left: orthographic front diffuse face, mature handsome blond knight age30, strong cheekbones/angular jaw, grey eyes, neutral closed mouth, skin extends to corners. Top right: seamless ivory diamond-quilted cloth, subtle stitches and grain, no belts/buttons. Bottom left: seamless cobalt wool, broad brush grain, no large folds. Bottom right: swept ash-blond strands, honey highlights/ochre roots, no face. Surface materials only, no character silhouette or lettering.

Generation source: `01a12e6b-fd47-7672-b224-1a5be75715a9/exec-a4f0ed94-29c3-44f8-a683-5d5a3e92842c.png` in imagegen output. Original preserved; project copy is the atlas above.

## Full comparison command and validation

The immutable result is `artifacts/m1-256/3d/revision6/full/manifest.json`, frozen at 2026-10-12 22:26 KST. SHA-256: `45d0bb3376340db236a737d9f5eb4cc84983d7e4a26d0bca0e6232bdb6b21658`. `final-validation.json` and `final-full-pack-validation.log` in the parent revision6 directory record all 64 atlases / 2,000 frames. PNG payload totals 91,558,111 bytes; loading every atlas as RGBA would occupy 1,660,944,384 bytes. This is not a runtime-memory measurement or mobile suitability claim.

Reproduce in a **separate local scratch workspace**, never over the frozen result. Restore the published `.blend` to `artifacts/m1-256/3d/revision6/knight-production.blend` there. Restore the two actual bake scripts and packer from their individually named Release assets. The historical wrappers contain an absolute `BASE`; change only that local copy's `BASE` to the scratch workspace's `artifacts/m1-256/3d` directory. They require Blender4.5.14 and the model, whose textures and stock actions are packed.

```sh
# BLENDER is the locally installed Blender4.5.14 executable.
# Both bake scripts below have BASE pointing at the scratch workspace.
KNIGHT_FULL=1 "$BLENDER" --background --threads 4 --python-exit-code 1 \
  --python artifacts/m1-256/3d/bake_full_revision6.py
KNIGHT_FULL=1 KNIGHT_CLIPS=death "$BLENDER" --background --threads 4 --python-exit-code 1 \
  --python artifacts/m1-256/3d/bake_death_reframe_v2_revision6.py
uv run artifacts/m1-256/authoring/hero-a-3d/pack_preview.py \
  artifacts/m1-256/3d/revision6/death-reframe-v2
```

The first bake uses orthographic scale3.8. Its death frames contain a documented sword-edge failure; the final death-only bake uses scale5.5, derived from all 480 evaluated pose/direction bounds (minimum fitting scale5.20646). Preserve that first death folder, then replace only the eight death entries, frame folders and atlases with the `death-reframe-v2` outputs. Keep their updated `bodyHeightPixels`/pivot metadata. Pack the resulting `full` folder with the same packer and require 64 unique `(id,direction)` entries / 2,000 frames. Packer rejects empty sprites and alpha>20 within two pixels of an image edge. Directions are screen-facing compass (E=right); the historical early preview E faced left.

Source-motion root displacement is removed by camera tracking, so Core owns movement. Cloth lag is authored, and skeleton curves retain stock timing. Runtime memory/cost must be measured in the actual build. CPU authoring duration is recorded separately in the manifest.

Code checks before external source preservation: Python compileall, no-excuse audit, Ruff fatal/syntax subset (`E9,F63,F7,F82`), real Blender renders, and the complete 2,000-frame pack check. Full strict typechecking was not performed. Consolidated `produce.py` and the original revision5→head-scale pipeline produced identical alpha silhouettes but RGB differences up to2/255 (mean absolute below0.000065/255); PNG metadata also contains creation time and render duration. Exact pixel/byte reproduction is therefore **not claimed**. Both source pipelines, the original `.blend`, and `source-reproduction-difference.json` are preserved.

## Head ratio and production clock

The reference annotation supplied during review is53px head /441px body (.12018). Revision5 was numerically too large against that reference (6.888heads); revision6 scales head-bound geometry about the chin by.87883787, preserving UVs, bones and body. Actual projected revision6 ratio.1297715 (7.70585heads) is within the reference±10% band. The old generated proportions are preserved as failures.

Measured local wallclock: officialBlender download birth21:08:26KST, first validated40frame preview manifest21:29:44 on2026-10-12:21m18s elapsed including research/model iterations. The corrected40frame render took37.50s. The original 2,000-frame bake took1,981.795s (33m01.795s); the successful 480-frame death rebake took387.929s (6m27.929s). Their39m29.724s sum excludes the failed4.8-scale retry, bound inspection, packing and review; it is not total labor time. Download-to-final-freeze elapsed1h17m49s, including those iterations and other waits.

## Authoring source distribution

Repository policy forbids Python additions and caps each PR at5MB. Blender's native Python source and diffuse PNG remain external production inputs under `artifacts/m1-256/authoring/hero-a-3d/`. They are supplied as individually named Release source assets, without a source/evidence archive. They are not runtime dependencies or renamed files used to evade policy. Git retains license texts and provenance; it does not contain Python/model/image payloads.

Minimum selectable source assets for rebaking the delivered model:

| Local source | Bytes | Purpose |
|---|---:|---|
| `artifacts/m1-256/3d/revision6/knight-production.blend` | 198,540,365 | Geometry, both licensed rigs/actions, packed texture |
| `artifacts/m1-256/3d/bake_full_revision6.py` | 7,357 | Actual full-bake recipe |
| `artifacts/m1-256/3d/bake_death_reframe_v2_revision6.py` | 7,326 | Final measured death framing |
| `artifacts/m1-256/authoring/hero-a-3d/pack_preview.py` | 2,377 | Atlas assembly and clipping guard |
| `licenses/quaternius-library1.txt` | 430 | Library1 CC0 text |
| `licenses/quaternius-library2.txt` | 522 | Library2 CC0 text |

For editable construction rather than rebaking, additionally publish `produce.py` (13,823 bytes), `model_geometry.py` (6,546), `inspect_rig.py` (994), `inspect_motion2.py` (846), and `materials/knight-gouache-atlas.png` (3,490,612), with the two stock-download URLs above. These scripts derive the repo root from their original layout: restore their local copies under `<scratch>/tools/art-authoring/hero-a-3d/` with the material below `materials/`; do not add them to Git. Inspection/bind/landmark helpers and original head-scale wrapper remain available as separate optional source assets. The `.blend` rebake path is the smaller dependency surface. Release publication status is owned by the issue256 report, not inferred from this inventory.
