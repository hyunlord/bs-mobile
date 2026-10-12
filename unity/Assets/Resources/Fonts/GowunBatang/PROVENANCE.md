# Gowun Batang

User-approved embedded display font for portrait-v0 headings and ornamented buttons.
Body text retains the existing Korean sans-serif font.

Source: https://github.com/google/fonts/tree/bd8f81ddb5c74d5c8897b36ad88b440266245103/ofl/gowunbatang
Files: GowunBatang-Regular.ttf, GowunBatang-Bold.ttf, OFL.txt (unmodified).
License: SIL Open Font License 1.1; see OFL.txt included alongside the fonts.
Fetched: 2026-10-12. No font download occurs at runtime.

The original TTF files are generated build inputs, not Git blobs. Run
`./tools/prepare-unity.sh` to obtain the exact originals from the pinned revision;
`./tools/prepare-unity.sh --verify` checks their hashes and importer settings
without network access. The manifest and preserved importer GUIDs are in
`tools/font-assets/`. A missing font downloads over HTTPS with a 30-second timeout,
three-redirect limit and exact byte/hash validation. A corrupt cached font fails
closed; remove it explicitly before retrying preparation.

Generated files live under `unity/Assets/Generated/Resources/Fonts/GowunBatang/`.
Unity includes both fonts and the OFL in the app, preserving existing
`Resources.Load("Fonts/GowunBatang/...")` paths. Gameplay has no network dependency.
The fonts are neither modified nor subsetted. Repository byte-budget policy is
unchanged; builds retain the full approved Korean and Latin coverage.
