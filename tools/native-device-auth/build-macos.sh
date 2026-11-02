#!/bin/bash
set -euo pipefail
root=$(git rev-parse --show-toplevel)
output="$root/unity/Assets/Plugins/macOS"
mkdir -p "$output"
flags=()
case "${SOW_DEV_AUTH:-0}" in
  0) ;;
  1) flags+=(-DSOW_DEV_AUTH=1) ;;
  *) echo "SOW_DEV_AUTH must be 0 or 1" >&2; exit 2 ;;
esac
xcrun clang++ ${flags[@]+"${flags[@]}"} -dynamiclib -fobjc-arc -arch arm64 -arch x86_64 -mmacosx-version-min=11.0 \
  -framework Foundation -framework Security -framework LocalAuthentication \
  "$root/unity/Assets/Plugins/iOS/SowDeviceIdentity.mm" -o "$output/libSowDeviceIdentity.dylib"
