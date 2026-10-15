#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if [[ $# != 1 || "$1" != /* ]]; then
  echo 'Usage: bash tools/forge-walk-fixture.sh <fresh-absolute-output-directory>' >&2
  exit 1
fi
dotnet run --project core/src/SowSiege.Sim --configuration Release -- forge-walk-fixture data "$1"
