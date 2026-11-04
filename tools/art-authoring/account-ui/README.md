# Account UI integration candidate (#638)

This packer only crops/packs existing Release PNGs. It does not generate or paint images.
Source review approval (including historical PENDING_USER) remains unchanged.

Download the manifest and referenced PNGs into these ignored directories:

- `artifacts/account-ui/recruit`: [hero-recruit-assets-592-20261030](https://github.com/hyunlord/bs-mobile/releases/tag/hero-recruit-assets-592-20261030), `MANIFEST-r1.json`, selected 2x primitives and 324x383 portraits.
- `artifacts/account-ui/research`: [slg-screen-assets-581-20261030](https://github.com/hyunlord/bs-mobile/releases/tag/slg-screen-assets-581-20261030), `MANIFEST-r2.json`, research 2x PNGs.

Run `node tools/art-authoring/account-ui/pack.mjs artifacts/account-ui` from the repository root.
Each source hash must match its Release manifest. Output manifests preserve source IDs, hashes,
rectangles and nine-slice borders. Fifteen recruitment and sixteen research sprites share two
atlases. Imported texture estimates are recorded in
`docs/metrics/texture-memory-deltas/account-ui-638.json`; estimates are not device measurements.

The first integration shows server-confirmed results. It does not implement or claim acceptance
of recruitment r2 seal/reveal animation. Actual Unity capture and user art acceptance remain separate gates.
