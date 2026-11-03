#!/bin/bash
set -euo pipefail
root=$(git rev-parse --show-toplevel)
output="$root/unity/Assets/Plugins/macOS"
mkdir -p "$output"
xcrun clang++ -dynamiclib -fobjc-arc -arch arm64 -arch x86_64 -mmacosx-version-min=11.0 \
  -framework Foundation -framework Security -framework LocalAuthentication \
  "$root/unity/Assets/Plugins/iOS/SowDeviceIdentity.mm" -o "$output/libSowDeviceIdentity.dylib"
