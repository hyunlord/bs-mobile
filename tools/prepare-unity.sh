#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
node tools/unity-export.mjs "$@"
if [[ "${1:-}" == "--verify" ]]; then
  exec node tools/font-assets.mjs --verify
fi
exec node tools/font-assets.mjs
