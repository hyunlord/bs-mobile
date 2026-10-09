# P1 normal-input Recorder evidence

Editor-only capture helper. No component or automation is compiled into a player.
Unity 6000.6's official package matrix releases Recorder **5.1.7**:
https://docs.unity3d.com/6000.6/Documentation/Manual/com.unity.recorder.html
API configuration follows the MovieRecorder sample in the official 5.1.7 UPM package.

From Edit mode use `Sow & Siege > P1 > Record normal automated run`, or launch GUI Unity with
`-projectPath /Users/rexxa/bs-mobile/unity -executeMethod Game.P1Capture.P1Capture.Start`.
Do not pass `-quit`, `-batchmode`, or `-nographics`. Set `P1_CAPTURE_DIR` to a fresh absolute output directory.
Default is `artifacts/p1-recorder` relative to the repository. Allow about 17 minutes plus editor setup. Set P1_CAPTURE_EXIT=1 only for a dedicated command-line capture editor; it exits after preferences restoration.
Keep Game View active and do not supply competing manual input. Unity execution must remain serialized.

The helper records Game View with actual HUD/cards/audio, 720×1560, variable frame rate capped at 30 fps, H.264.
It starts seed 30000 through `StartNeutralRun` without P2 chapter or retainer injection, uses visible floating-stick pointer events,
waits two seconds on every naturally earned card offer, and chooses a card through `Send`.
It selects the ordinary nearest-enemy aim setting. It never advances ticks directly,
changes health/spawns, grants levels, or changes playback speed. RunCoordinator owns its normal update clock.
The title is recorded before invoking StartNeutralRun; the introductory page is bypassed.
Existing user sound settings apply. The helper snapshots every RunPreferences key before play and restores their exact prior values and absence after exiting play mode, because normal hint progression saves preferences. preferences-before.json and preferences-after.json must match.

Selection rules are fixed before recording:
- Early clip: 180 continuous media seconds beginning at the ledger's `run-start` time.
- Late clip: 180 continuous media seconds ending eight seconds after `run-complete`.
- Both preserve 1× speed and in-clip continuity, including visible card pauses.

`capture-ledger.tsv` records the source/data identity, timing, harvest, people, cards, summary,
and replay command invariant. The result only passes after normal survival to tick 27000,
winter boss defeat, actual restart, and a replay containing only movement/card/aim commands.
A failure remains explicitly failed; never substitute a stress fixture or relabel a death.
`full-run.mp4` is the original source; trims must record exact offsets and source hash.
The four `*-editor-*.png` screenshots prove only this editor session, not native-app
or clean-macOS-user acceptance. That gate requires independent native evidence.

Preflight (offline, not capture proof): seed 30000 with the same card ranking and farm/circuit
movement survived 27000 normal ticks, defeated the boss, first harvested at tick 372,
and naturally selected muster_horn at tick 1742. Actual pointer sampling can differ;
the captured replay and visible video remain authoritative.

Timing is deliberately **variable-rate**, preserving Recorder's explicit MP4 timestamps.
Constant Recorder captureDeltaTime does not affect the game's unscaled clock:
https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Time-captureDeltaTime.html
Recorder 5.1.7 MovieRecorder.ComputeMediaTime uses session time for VFR; its CoreEncoder
supports VFR H.264 and audio. Ledger mediaSeconds uses the same Time.time clock, with
unscaledSeconds and rendered frame alongside it. The drift guard is armed exactly once at actual run-start. The pre-run encoder/scene startup offset is logged and excluded, while the media timestamp origin is unchanged. A cumulative drift over 0.15 seconds after that baseline fails capture, including across card pauses, summary, and restart.
Validate actual MP4 packet timestamps/duration and audio with ffprobe before acceptance;
trim by timestamp without forcing `-r` or changing playback rate. Card pauses are visible
and listed, so core tick deltas can be checked against active media intervals.
