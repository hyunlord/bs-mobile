#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
stage="${1:?Usage: package-stage.sh STAGE YYYYMMDD [destination]}"
date_stamp="${2:?YYYYMMDD required}"
destination="${3:-$HOME/Downloads}"
exec node tools/package-stage.mjs "$stage" "$date_stamp" "$destination"
