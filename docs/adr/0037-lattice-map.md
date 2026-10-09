# ADR0037: Publish a read-only Lattice content map

Status: Accepted by the user's browser-map request.
Date: 2026-10-10.
Related issue: [#137](https://github.com/hyunlord/bs-mobile/issues/137).

The JSON data remains canonical. A declarative `.lattice/lens.json` classifies the seven content kinds and original runtimeProjection depth and calculates three findings. The Lattice core and three static screens are built from an immutable external tool commit; no gameplay code or original design document is changed.

The map publishes at https://hyunlord.github.io/bs-mobile/ through GitHub Pages. PRs build and upload a static artifact without deployment permissions. Main/workflow-dispatch builds deploy through the github-pages environment. All actions and the Lattice tool are pinned. Cache/site outputs are ignored.

Depth means a source projection, not actual handler execution or play evidence. The site explicitly distinguishes original effects from profile overrides and base weapon shapes from profile forms. Wider adapters/screens/MCP are subsequent Lattice work; held device work remains held.

Validation is the consumer Core check, actual graph/oracle comparison, tool Node20/22/24 CI, browser home/list/detail flows and deployed Pages observation. A successful static site is not a gameplay acceptance gate.

## Consequences and cost

The map is a static snapshot and must rebuild after source changes. Lattice's version is pinned, so tool upgrades require a reviewed commit change. Generated graph/site artifacts are not committed. Existing gameplay and device gates retain their original meaning.

## Rejected alternatives

- A handwritten HTML map: rejected because counts and findings would drift from canonical JSON.
- Completing every adapter before publication: rejected by the user in favor of a thin browser-visible path first.
- Running an unpinned external tool or deploying PR output: rejected because publication must be reproducible and PR builds must not have deployment authority.
