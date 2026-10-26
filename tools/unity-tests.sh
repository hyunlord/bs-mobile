#!/usr/bin/env bash
# Runs every Unity EditMode and PlayMode test for both content profiles on the Mac and prints the "## Unity tests" record that
# a PR touching unity/ must carry (docs/runbooks/unity-tests.md, ADR0068). CI has no Unity runner.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Library/Developer/CommandLineTools}"
EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity}"
OUT="${UNITY_TESTS_OUTPUT:-$ROOT/artifacts/unity-tests}"
[[ -x "$EDITOR" ]] || { echo "Unity editor missing: $EDITOR" >&2; exit 2; }
# The record names one commit, so nothing outside it may feed the run: no tracked edits anywhere and no untracked,
# non-ignored files in the roots Unity compiles, imports or reads fixtures from.
if [[ -n "$(git status --porcelain --untracked-files=no)$(git status --porcelain --untracked-files=all -- unity core data)" ]]; then
  echo 'Commit or discard local changes first (tracked edits, or untracked files under unity/ core/ data/): the record names the tested commit.' >&2
  git status --short --untracked-files=all -- unity core data >&2; exit 2
fi
COMMIT="$(git rev-parse HEAD)"
rm -rf "$OUT"; mkdir -p "$OUT"
# Fixtures the parity tests read: .NET-recorded replays for both profiles and meta snapshots.
rm -rf artifacts/phase1b/replays artifacts/wave1a/editor-replays artifacts/phase2a/p2/meta-replays
dotnet run --project core/src/SowSiege.Sim --configuration Release -- interactive-fixtures data artifacts/phase1b/replays first-playable > "$OUT/fixtures-first-playable.log" 2>&1 || { echo 'first-playable fixtures failed' >&2; exit 1; }
dotnet run --project core/src/SowSiege.Sim --configuration Release -- interactive-fixtures data artifacts/wave1a/editor-replays wave-1a > "$OUT/fixtures-wave-1a.log" 2>&1 || { echo 'wave-1a fixtures failed' >&2; exit 1; }
dotnet run --project core/src/SowSiege.Sim --configuration Release -- meta-replay-fixtures data artifacts/phase2a/p2/meta-replays > "$OUT/fixtures-meta.log" 2>&1 || { echo 'meta fixtures failed' >&2; exit 1; }
status=0
lines=()
for profile in first-playable wave-1a; do
  UNITY_CONTENT_PROFILE=$profile bash tools/prepare-unity.sh > "$OUT/prepare-$profile.log" 2>&1 || { echo "prepare $profile failed" >&2; exit 1; }
  "$EDITOR" -batchmode -quit -projectPath "$ROOT/unity" -executeMethod Game.Editor.FoundationBuild.Configure -logFile "$OUT/configure-$profile.log" || { echo "configure $profile failed (see $OUT/configure-$profile.log)" >&2; exit 1; }
  for platform in EditMode PlayMode; do
    result="$OUT/$profile-$platform.xml"
    editor_args=()
    # PlayMode captures need real end-of-frame rendering; Unity rejects that yield in batchmode.
    if [[ "$platform" == EditMode ]]; then editor_args+=(-batchmode); fi
    "$EDITOR" "${editor_args[@]}" -projectPath "$ROOT/unity" -runTests -testPlatform "$platform" -testResults "$result" -logFile "$OUT/$profile-$platform.log"
    summary="$(node -e '
      const fs=require("fs"); const p=process.argv[1];
      if(!fs.existsSync(p)){console.log("missing");process.exit(0);}
      const head=fs.readFileSync(p,"utf8").slice(0,800);
      const n=k=>Number((head.match(new RegExp(k+"=\"(\\d+)\""))||[])[1]);
      const failed=n("failed")+n("inconclusive");
      console.log(`passed ${n("passed")}, failed ${failed}, skipped ${n("skipped")}`);' "$result")"
    if [[ "$summary" == missing || "$summary" != *"failed 0,"* ]]; then status=1; fi
    lines+=("$profile $platform: $summary")
  done
done
# A test skipped by every profile never runs; only tools/unity-skip-exceptions.txt may excuse one, with a reason.
skips="$(node tools/unity-skipped-everywhere.mjs tools/unity-skip-exceptions.txt "$OUT"/*.xml 2> "$OUT/skipped-everywhere.txt")" || status=1
cat "$OUT/skipped-everywhere.txt" >&2
UNITY_CONTENT_PROFILE=first-playable bash tools/prepare-unity.sh > "$OUT/prepare-restore.log" 2>&1
if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
  echo 'Unity changed tracked files during the run; the record is not valid for this commit:' >&2
  git status --porcelain --untracked-files=no >&2; exit 1
fi
{
  echo '## Unity tests'
  echo "commit: $COMMIT"
  for line in "${lines[@]}"; do echo "$line"; done
  echo "${skips:-skipped in every profile: not checked}"
} | tee "$OUT/record.md"
[[ $status -eq 0 ]] || echo "A suite failed, did not report, or a test is skipped in every profile without a listed reason; results are in $OUT" >&2
exit $status
