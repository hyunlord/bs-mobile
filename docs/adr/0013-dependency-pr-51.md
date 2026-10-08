# ADR 0013 appendix: dependency PR #51

- Status: proposed; CI pending, adopted only on protected merge
- Issue: #52
- Policy: [ADR 0013](0013-dependency-policy.md)

## Decision
Apply only the verified minor/patch updates listed below, subject to full CI. No major update or unknown SHA update is approved by this appendix.

- BenchmarkDotNet: see signed Dependabot diff → see signed Dependabot diff (version-update:semver-minor)
- Microsoft.NET.Test.Sdk: see signed Dependabot diff → see signed Dependabot diff (version-update:semver-minor)
- xunit: see signed Dependabot diff → see signed Dependabot diff (version-update:semver-patch)

## Risk and validation
Minor releases, especially 0.x packages, may break compatibility. Full build, deterministic tests, content/schema/architecture/PR policy, repository budget and secrets checks remain mandatory. Passing CI does not prove upstream safety or all runtime behavior. No new package identity or policy exception is authorized.

## Alternative
Manual-only minor/patch updates were rejected because the user requested weekly grouped automatic updates after CI. Major/unknown updates remain in the central review issue.
