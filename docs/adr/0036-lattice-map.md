# ADR0036: Publish a read-only Lattice content map

Status: Accepted by the user's 2026-10-10 browser-map request.

The JSON data remains canonical. A declarative `.lattice/lens.json` classifies the seven content kinds and original runtimeProjection depth and calculates three findings. The Lattice core and three static screens are built from an immutable external tool commit; no gameplay code or original design document is changed.

The map publishes at https://hyunlord.github.io/bs-mobile/ through GitHub Pages. PRs build and upload a static artifact without deployment permissions. Main/workflow-dispatch builds deploy through the github-pages environment. All actions and the Lattice tool are pinned. Cache/site outputs are ignored.

Depth means a source projection, not actual handler execution or play evidence. The site explicitly distinguishes original effects from profile overrides and base weapon shapes from profile forms. Wider adapters/screens/MCP are subsequent Lattice work; held device work remains held.

Validation is the consumer Core check, actual graph/oracle comparison, tool Node20/22/24 CI, browser home/list/detail flows and deployed Pages observation. A successful static site is not a gameplay acceptance gate.
