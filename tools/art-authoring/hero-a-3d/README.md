# A knight: actual3D production experiment — issue256

Status: **visual quality not accepted**. Revision1–4 are preserved failed experiments. Revision6 starts with a two-clip review candidate; the full8-direction bake is a comparison candidate and retains the same art limitations. Rendering completion is checked from the output manifest, not this plan. No Unity/Core files are changed by these tools.

## Sources and rights (checked2026-10-12)

- Identity reference: user supplied `/Users/rexxa/Downloads/heroes.png`, leftmost traditional adult knight. Other heroes are not incorporated.
- Model: project-authored closed meshes, articulated hands, plate shells, cloth panels, physical sword. No billboard reference planes. Source generation is `produce.py`; `.blend` snapshots are under `artifacts/m1-256/3d/revision*/`.
- Diffuse material: `materials/knight-gouache-atlas.png`, generated using built-in imagegen with the approved reference. Four quadrants are adult face, ivory quilt, cobalt cloth, blond hair. Mapped as UV color onto3D geometry. It is **not** Quaternius art or described as CC0.
- Motion1: Quaternius [Universal Animation Library](https://quaternius.com/packs/universalanimationlibrary.html), [author upload](https://opengameart.org/content/universal-animation-library), Standard free package. Motion2: [author upload](https://opengameart.org/content/universal-animation-library-2), Standard free package. Both downloaded packages include explicit CC0-1.0 licenses, copied unchanged into `licenses/`.
- Blender4.5.14 LTS arm64: official `download.blender.org/release/Blender4.5/` distribution. Read-only mounted under local artifacts; not installed as a runtime dependency. Cycles CPU, fixed maximum4threads; no GPU/Unity jobs.

## Workflow

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

Run Blender4.5.14 with `KNIGHT_FULL=1`, `--background --threads 4 --python-exit-code 1 --python tools/art-authoring/hero-a-3d/bake_preview.py`. Full output: `artifacts/m1-256/3d/revision6/full/manifest.json`. Then `python3 tools/art-authoring/hero-a-3d/pack_preview.py artifacts/m1-256/3d/revision6/full`. Packer rejects empty sprites and alpha touching the image edge. Full direction names are screen-facing compass (E=right); the historical early preview E was camera-azimuth E and faces left.

Source-motion root displacement is removed by camera tracking, so Core owns movement. Cloth lag is authored, and skeleton curves retain stock timing. Runtime memory/cost must be measured in the actual build. CPU authoring duration is recorded separately in the manifest.

Code checks: Python compileall; no-excuse audit; real Blender render; exact pre/post helper-extraction image SHA `61128418a64b24fbe12818a62ce6cb19f5b7c217483764ede3587427fcf0999c`;40early-preview frames passed no-clipping checks. Full output checks remain dependent on bake completion.

## Head ratio and production clock

The reference annotation supplied during review is53px head /441px body (.12018). Revision5 was numerically too large against that reference (6.888heads); revision6 scales head-bound geometry about the chin by.87883787, preserving UVs, bones and body. Actual projected revision6 ratio.1297715 (7.70585heads) is within the reference±10% band. The old generated proportions are preserved as failures.

Measured local wallclock: officialBlender download birth21:08:26KST, first validated40frame preview manifest21:29:44 on2026-10-12:21m18s elapsed including research/model iterations. The final corrected40frame render itself took37.50s. Full2000frame bake cost is recorded in its manifest after completion; the roughly30minute prior estimate is not a measured result.

## Authoring source distribution

Repository policy forbids Python additions and caps each PR at5MB. Blender's native Python authoring source and diffuse PNG therefore remain external production inputs, preserved under `artifacts/m1-256/authoring/hero-a-3d/` and delivered as individually named Release source assets. They are not a runtime dependency or a renamed file used to evade the policy. Restore those source files to the paths described above in a local authoring workspace before running the reproduction commands. Git retains license texts, provenance, the Unity playback code and compact measurements; it does not contain the Python/model/image payload. The full `.blend` candidate also remains external.
