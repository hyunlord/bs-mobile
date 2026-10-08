#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
node --test tools/test-repository-budget.mjs
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
npm run validate
node tools/content-report.mjs --check
node tools/metrics.mjs --selftest
node tools/pr-policy.mjs
dotnet run --project tools/ArchitectureGuard -- --self-test
dotnet run --project tools/ArchitectureGuard -- "$PWD"
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release --logger 'trx;LogFileName=tests.trx' --results-directory "$PWD/artifacts/tests"
dotnet build core/src/SowSiege.Core/SowSiege.Core.csproj --no-restore --configuration Debug --framework netstandard2.1
dotnet format --no-restore --verify-no-changes
dotnet format tools/ArchitectureGuard --no-restore --verify-no-changes
TARGET_PARITY_ROOT=$(mktemp -d "$PWD/artifacts/target-parity.XXXXXX")
node tools/verify-target-parity.mjs "$TARGET_PARITY_ROOT/run" 4
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
