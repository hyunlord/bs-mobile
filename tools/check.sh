#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
node --test tools/test-ci-scope.mjs tools/test-repository-budget.mjs
node tools/repository-budget.mjs
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p artifacts
command -v dotnet >/dev/null || { echo 'Install .NET 8 SDK (see README).'; exit 1; }
npm ci --ignore-scripts
npm test
node --test tools/test-verify-evidence.mjs
node --test tools/test-pr-policy.mjs tools/test-dependency-policy.mjs
node --test tools/test-csv.mjs tools/test-s4*.mjs tools/test-r3-remains-report.mjs tools/test-retrospective-c-prime.mjs
node --test tools/test-target-parity.mjs
node --test tools/test-meta-target-parity.mjs
node --test tools/test-unity-export.mjs tools/test-unity-boundaries.mjs
node --test tools/wave1a-content.test.mjs
DOTNET="$(command -v dotnet)" node --test tools/test-unity-results.mjs
node tools/verify-unity-boundaries.mjs
node --test tools/test-diagnostic-*.mjs
node --test tools/test-weapon-holdout.mjs
node --test tools/test-first-playable*.mjs
npm run validate
node tools/first-playable-content.mjs data
node tools/meta-content.mjs "$PWD"
node --test tools/test-meta-content.mjs
node --test tools/first-playable-art.test.mjs
node tools/first-playable-art.mjs "$PWD"
node --test tools/wave1a-art.test.mjs
node tools/wave1a-art.mjs "$PWD"
node tools/wave1a-audio.mjs --check
node tools/content-report.mjs --check
node tools/metrics.mjs --selftest
node --test tools/test-metrics-device.mjs
node --test tools/test-device-metrics.mjs
node --test tools/test-mac-wave-frame-window.mjs
node --test tools/test-wave-benchmark.mjs tools/test-restore-urp-authoring.mjs
node tools/pr-policy.mjs
dotnet run --project tools/ArchitectureGuard -- --self-test
dotnet run --project tools/ArchitectureGuard -- "$PWD"
dotnet restore
dotnet build --no-restore --configuration Release
FIRST_PLAYABLE_COVERAGE_ROOT=$(mktemp -d "$PWD/artifacts/first-playable-coverage.XXXXXX")
BS_FIRST_PLAYABLE_COVERAGE_OUTPUT="$FIRST_PLAYABLE_COVERAGE_ROOT/runtime.json" dotnet test --no-build --configuration Release --logger 'trx;LogFileName=tests.trx' --results-directory "$PWD/artifacts/tests"
node tools/first-playable-content.mjs data "$FIRST_PLAYABLE_COVERAGE_ROOT/runtime.json"
dotnet build core/src/SowSiege.Core/SowSiege.Core.csproj --no-restore --configuration Debug --framework netstandard2.1
dotnet format --no-restore --verify-no-changes
dotnet format tools/ArchitectureGuard --no-restore --verify-no-changes
dotnet format tools/UnityResultCheck --no-restore --verify-no-changes
TARGET_PARITY_ROOT=$(mktemp -d "$PWD/artifacts/target-parity.XXXXXX")
node tools/verify-target-parity.mjs "$TARGET_PARITY_ROOT/run" 4
FIRST_PLAYABLE_PARITY_ROOT=$(mktemp -d "$PWD/artifacts/first-playable-parity.XXXXXX")
node tools/verify-first-playable-target-parity.mjs "$FIRST_PLAYABLE_PARITY_ROOT/run" 4
META_PARITY_ROOT=$(mktemp -d "$PWD/artifacts/meta-parity.XXXXXX")
node tools/verify-meta-target-parity.mjs "$META_PARITY_ROOT/run"
WAVE1A_PARITY_ROOT=$(mktemp -d "$PWD/artifacts/wave1a-parity.XXXXXX")
node tools/verify-wave1a-target-parity.mjs "$WAVE1A_PARITY_ROOT/run"
WAVE_HISTORY_PARITY_ROOT=$(mktemp -d "$PWD/artifacts/wave-history-parity.XXXXXX")
node tools/verify-wave-history-parity.mjs "$WAVE_HISTORY_PARITY_ROOT/run" --no-build
DIAGNOSTIC_SMOKE_ROOT=$(mktemp -d "$PWD/artifacts/diagnostic-smoke.XXXXXX")
node tools/diagnostic-runner.mjs smoke "$DIAGNOSTIC_SMOKE_ROOT/run" 4
WEAPON_SMOKE_ROOT=$(mktemp -d "$PWD/artifacts/weapon-smoke.XXXXXX")
node tools/weapon-holdout.mjs smoke "$WEAPON_SMOKE_ROOT/run" 4
FIRST_PLAYABLE_SMOKE_ROOT=$(mktemp -d "$PWD/artifacts/first-playable-smoke.XXXXXX")
node tools/first-playable-league.mjs smoke "$FIRST_PLAYABLE_SMOKE_ROOT/run" 4
./tools/league.sh
cp artifacts/metrics.json artifacts/metrics-s2.json
node tools/league.mjs s4-smoke --profile s4-stage-one --workers 2
cp artifacts/league-s4-smoke/metrics.json artifacts/metrics.json
node tools/s4-report.mjs artifacts/league-s4-smoke artifacts/s4-smoke-report --no-plots
S4B_SMOKE_ROOT=$(mktemp -d "$PWD/artifacts/s4b-smoke.XXXXXX")
node tools/s4b-league.mjs smoke-A --profile s4b-01 --output "$S4B_SMOKE_ROOT/A" --workers 2
node tools/s4b-report.mjs "$S4B_SMOKE_ROOT/A" "$S4B_SMOKE_ROOT/A-report"
node tools/r3-remains-report.mjs "$S4B_SMOKE_ROOT/A" "$S4B_SMOKE_ROOT/A-remains-report"
node tools/s4b-league.mjs smoke-B --profile s4b-01 --output "$S4B_SMOKE_ROOT/B" --workers 2
node tools/s4b-report.mjs "$S4B_SMOKE_ROOT/B" "$S4B_SMOKE_ROOT/B-report"
node tools/r3-remains-report.mjs "$S4B_SMOKE_ROOT/B" "$S4B_SMOKE_ROOT/B-remains-report"
