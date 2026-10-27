# Approved sprout emblem crops (#405)

The user approved all 15 crops from Releases
`sprout-emblem-replacement-402-20261026`, `sprout-emblem-batch2-405-20261026`,
and `sprout-emblem-batch3-405-20261026`. Earlier Release review descriptions
remain historical; approval is the newer task decision. `measured_volley` stays unchanged.

`approved.json` pins the delivered PNG SHA-256, original atlas SHA-256,
crop rectangle, existing role aliases/pivots, and reported alignment anchors.
Batch 2/3 anchors are art alignment proxies, not runtime measurements.
The original batch 1 provenance did not specify a canvas anchor; its existing
manifest pivot and rectangle are retained. Source artwork limitations are
preserved: the banner pole is approximately 6 px shorter and the shield rim differs slightly.

Download the named PNG assets into one source directory. Keep the original
eight atlases in a separate baseline directory before installing; they must
match the source SHA values in `approved.json`. Original atlas objects are also
available from base commit `041e1e4539c5b36031e1bedcb919d81478c196ea` via Git LFS.

```sh
node --test tools/art-authoring/sprout-emblems/replace.test.mjs
node tools/art-authoring/sprout-emblems/replace.mjs --install SOURCE_DIRECTORY ORIGINAL_ATLAS_DIRECTORY
node tools/art-authoring/sprout-emblems/replace.mjs --check SOURCE_DIRECTORY ORIGINAL_ATLAS_DIRECTORY
node tools/wave1a-art.mjs "$PWD"
node tools/first-playable-art.mjs "$PWD"
```

Installation validates all inputs before writing. It copies decoded RGBA bytes
without resampling, blending, changing alpha, repacking, or editing manifests.
Every pixel outside the 15 cells must remain unchanged, including
`measured_volley`. `installed.json` records before/after hashes and changed-pixel
counts. Check mode regenerates in memory, compares every output RGBA byte,
checks the PNG output hashes, and checks both manifest files byte-for-byte.
The vassal cell is shared by idle/move/work/muster/return in both manifests.

The only procedural change is `WorldOrbits.TemporaryWardShield`'s crest:
one seed, curved stem and two leaves instead of wheat stalks. Shield geometry,
palette, rim, size and pivot stay unchanged. Its visual acceptance requires an
omitted-role fallback render; loaded-role screenshots do not exercise it.

## Runtime evidence still required

- Actual charge leader flag: `WorldMinions` draws `icon.core.levy_banner.default`.
  Use the recorded `core-muster_horn-evolved` combat case, not a card screenshot.
- Orbit shield: recorded `core-ward_orbit-evolved` case, same frame before/after.
- Vassal shield and carpenter complete/damaged/ruin: first-playable renderer;
  the wave workshop is a different visual. Existing replay snapshots may not
  contain every state; label any focused renderer fixtures explicitly.
- Cards/evolution list: use actual UI paths; remaining icons may share one
  explicitly labeled icon-list fixture screenshot.
- `wave1a.wave.harvest-flag.default` currently has no C# drawing consumer.
  Its crop replacement is approved but only an explicit role-render fixture
  can show it; do not claim it appears in ordinary gameplay or add a consumer.

This preparation runs no Unity/native build or game capture. It changes no
Core rule or replay. C05 is deferred unless rendering batch count changes;
pixel replacement alone does not add batches. Runtime verification and the
single PR follow #386, #390, and the enemy PR, owned by the parent session.

## Local preparation verification (2026-10-26)

- Importer tests: 2/2 (exact bytes including transparent RGB; reject bounds,
  dimensions and overlap).
- Independent delivered-cell comparison: 15/15 RGBA exact.
- Outside approved cells: 0 changed pixels in each of eight atlases.
- Both manifests: unchanged bytes; source role metadata retained.
- `measured_volley` unchanged RGBA SHA-256:
  `0f0cc3069a2d7a4324754f2925187dea9090088e870e3a5e5fdaaba150b7111c`.
- Wave art validator: 493 roles / 415 bindings; first-playable: 372 / 343.
- Procedural crest raster math: one connected component (1212 pixels).
  This is not a Unity rendered acceptance result.
- Not tested: Unity compilation, native capture, fallback visual acceptance,
  runtime batch count, replay execution. Parent owns those subsequent gates.
