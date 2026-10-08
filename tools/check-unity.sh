#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity}"
OUT="${UNITY_CHECK_OUTPUT:-$ROOT/artifacts/unity}"
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Library/Developer/CommandLineTools}"
export UNITY_APK_PATH="$OUT/sow-siege.apk"
mkdir -p "$OUT"
[[ -x "$EDITOR" ]] || { echo "Unity editor missing: $EDITOR" >&2; exit 1; }
actual_version="$("$EDITOR" -version)"
[[ "$actual_version" == "6000.6.4f1" ]] || { echo "Expected Unity 6000.6.4f1, got: $actual_version" >&2; exit 1; }
bash tools/prepare-unity.sh
dotnet run --project core/src/SowSiege.Sim --configuration Release -- interactive-fixtures data artifacts/phase1b/replays first-playable > "$OUT/dotnet-replay.log"
node tools/verify-unity-boundaries.mjs
node --test tools/test-unity-boundaries.mjs tools/test-unity-results.mjs tools/test-unity-export.mjs
"$EDITOR" -batchmode -quit -projectPath "$ROOT/unity" -executeMethod Game.Editor.FoundationBuild.Configure -logFile "$OUT/configure.log"
for platform in EditMode PlayMode; do
  result="$OUT/$platform.xml"
  rm -f "$result"
  "$EDITOR" -batchmode -projectPath "$ROOT/unity" -runTests -testPlatform "$platform" -testResults "$result" -logFile "$OUT/$platform.log"
  dotnet run --project tools/UnityResultCheck -- "$result"
done
"$EDITOR" -batchmode -quit -projectPath "$ROOT/unity" -executeMethod Game.Editor.ReleaseBoundaryCheck.Run -logFile "$OUT/release-boundary.log"
dotnet run --project tools/UnityResultCheck -- --release "$ROOT/artifacts/unity/release-scripts" > "$OUT/release-boundary.txt"
cat "$OUT/release-boundary.txt"
bash tools/prepare-unity.sh --verify
rm -f "$UNITY_APK_PATH" "$OUT/build-result.json"
"$EDITOR" -batchmode -quit -projectPath "$ROOT/unity" -buildTarget Android -executeMethod Game.Editor.FoundationBuild.Android -logFile "$OUT/android-build.log"
[[ -s "$UNITY_APK_PATH" && -s "$OUT/build-result.json" ]] || { echo 'Missing successful Android build output' >&2; exit 1; }
node --input-type=module - "$OUT/build-result.json" <<'JS'
import assert from 'node:assert/strict';
import fs from 'node:fs';
const report = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
assert.equal(report.result, 'Succeeded');
assert.equal(report.backend, 'IL2CPP');
assert.equal(report.architecture, 'ARM64');
console.log('UNITY_ANDROID_BUILD_PASS '+JSON.stringify(report));
JS
