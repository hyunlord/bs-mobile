#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p artifacts
command -v dotnet >/dev/null || { echo 'Install .NET 8 SDK (see README).'; exit 1; }
npm ci --ignore-scripts
npm test
node --test tools/test-pr-policy.mjs
npm run validate
node tools/metrics.mjs --selftest
node tools/pr-policy.mjs
dotnet run --project tools/ArchitectureGuard -- --self-test
dotnet run --project tools/ArchitectureGuard -- "$PWD"
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release --logger 'trx;LogFileName=tests.trx' --results-directory "$PWD/artifacts/tests"
dotnet format --no-restore --verify-no-changes
dotnet format tools/ArchitectureGuard --no-restore --verify-no-changes
./tools/league.sh
