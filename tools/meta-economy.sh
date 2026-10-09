#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project core/src/SowSiege.Sim --configuration Release -- meta-economy "$PWD" "${1:-artifacts/meta-economy.csv}" "${2:-21}"
