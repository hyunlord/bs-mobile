#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export UNITY_CONTENT_PROFILE="${UNITY_CONTENT_PROFILE:-first-playable}"
case "$UNITY_CONTENT_PROFILE" in first-playable|wave-1a) ;; *) echo "Unsupported Unity profile: $UNITY_CONTENT_PROFILE" >&2; exit 1 ;; esac
export UNITY_MAC_DEVELOPMENT="${UNITY_MAC_DEVELOPMENT:-0}"
case "$UNITY_MAC_DEVELOPMENT" in
  0) BUILD_METHOD=MacRelease; BUILD_KIND=RELEASE ;;
  1) BUILD_METHOD=MacDiagnostic; BUILD_KIND=DEVELOPMENT ;;
  *) echo 'UNITY_MAC_DEVELOPMENT must be 0 or 1.' >&2; exit 1 ;;
esac
EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity}"
OUT="${UNITY_MAC_OUTPUT:-$ROOT/artifacts/phase2a/mac}"
if [[ -d "$OUT" && -n "$(ls -A "$OUT")" ]]; then
  echo 'Use an empty UNITY_MAC_OUTPUT to preserve all prior build evidence.' >&2
  exit 1
fi
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
export UNITY_MAC_PATH="$OUT/Sow and Siege.app"
[[ -x "$EDITOR" ]] || { echo "Unity editor missing: $EDITOR" >&2; exit 1; }
[[ "$("$EDITOR" -version)" == "6000.6.4f1" ]] || { echo 'Unity version mismatch' >&2; exit 1; }
[[ ! -e "$UNITY_MAC_PATH" ]] || { echo 'Use a fresh UNITY_MAC_OUTPUT to preserve prior build evidence.' >&2; exit 1; }
bash tools/prepare-unity.sh
URP_ASSET="$ROOT/unity/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"
URP_SNAPSHOT="$OUT/urp-authoring-before-build.asset"
cp "$URP_ASSET" "$URP_SNAPSHOT"
restore_urp_authoring() {
  node tools/restore-urp-authoring.mjs "$URP_ASSET" "$URP_SNAPSHOT"
}
finish_build() {
  local result=$?
  trap - EXIT
  restore_urp_authoring || result=1
  exit "$result"
}
trap finish_build EXIT
"$EDITOR" -batchmode -quit -projectPath "$ROOT/unity" -buildTarget OSXUniversal \
  -executeMethod Game.Editor.FoundationBuild.ConfigureMac -logFile "$OUT/configure.log"
restore_urp_authoring
"$EDITOR" -batchmode -quit -projectPath "$ROOT/unity" -buildTarget OSXUniversal \
  -executeMethod "Game.Editor.FoundationBuild.$BUILD_METHOD" -logFile "$OUT/build.log"
restore_urp_authoring
[[ -s "$OUT/mac-build-result.json" && -d "$UNITY_MAC_PATH" ]] || { echo 'Missing Mac build result.' >&2; exit 1; }
node --input-type=module - "$OUT/mac-build-result.json" "$UNITY_MAC_DEVELOPMENT" <<'JS'
import fs from 'node:fs';
const result = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
if (result.result !== 'Succeeded' || result.development !== (process.argv[3] === '1')) {
  throw new Error('Mac build kind/result does not match requested mode.');
}
JS
if [[ "$UNITY_MAC_DEVELOPMENT" == 0 ]]; then
  dotnet run --project tools/UnityResultCheck -- --release "$UNITY_MAC_PATH/Contents/Resources/Data/Managed"
fi
bash tools/prepare-unity.sh --verify
echo "MAC_${BUILD_KIND}_BUILT $UNITY_MAC_PATH"
