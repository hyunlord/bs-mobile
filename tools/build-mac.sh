#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export UNITY_CONTENT_PROFILE="${UNITY_CONTENT_PROFILE:-first-playable}"
case "$UNITY_CONTENT_PROFILE" in first-playable|wave-1a) ;; *) echo "Unsupported Unity profile: $UNITY_CONTENT_PROFILE" >&2; exit 1 ;; esac
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
  -executeMethod Game.Editor.FoundationBuild.MacRelease -logFile "$OUT/build.log"
restore_urp_authoring
[[ -s "$OUT/mac-build-result.json" && -d "$UNITY_MAC_PATH" ]] || { echo 'Missing Mac build result.' >&2; exit 1; }
dotnet run --project tools/UnityResultCheck -- --release "$UNITY_MAC_PATH/Contents/Resources/Data/Managed"
bash tools/prepare-unity.sh --verify
echo "MAC_RELEASE_BUILT $UNITY_MAC_PATH"
