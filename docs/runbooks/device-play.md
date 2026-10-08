# Device recording and frame measurements

Use a development Android player built by `tools/check-unity.sh`. A physical-device pass requires actual device evidence; Editor/Mono replay and build success do not establish IL2CPP replay or frame performance. No device identifier, account, serial number, or secret is included in the recorded metadata.

Start a seeded run. The development menu exposes speed 1/2/4, level grant, invulnerability, spawn multiplier, and aim mode. Speed changes only the App scheduler: it is captured in frame CSV, not a Core replay command. Level, invulnerability, spawn and aim commands are recorded after Core accepts them. Opening menus, card selection, application pause, and fixture verification pause gameplay. The debug menu distinguishes current-tick attack events from pooled visual projectile events.

Each run uses an anonymous directory under `Application.persistentDataPath/runs/<sessionId>`:

- `run.ssreplay`: a self-contained replay, atomically replaced on pause and finish. A pause snapshot ends with a valid Quit record; resuming continues the live command stream and replaces the snapshot on the next checkpoint. A finished stream rejects additional commands.
- `recording.json`: seed, commit identity, data hash, anonymous session ID.
- `frames.csv`: every captured rendered frame, including slow, paused, and accelerated frames; screen and safe area are recorded per frame.
- `device.json`: model, OS, Unity/backend, source hash/dirty status, graphics API, and categorical thermal availability. No temperature is inferred from thermal status.
- `frame-summary.json`: local convenience summary. The host metrics tool recalculates percentiles from raw CSV and checks identity and row integrity.

For the configured package `com.hyunlord.sowsiege`, retrieve files without publishing adb device serials:

```sh
adb shell ls /sdcard/Android/data/com.hyunlord.sowsiege/files/runs
adb pull /sdcard/Android/data/com.hyunlord.sowsiege/files/runs artifacts/device-runs
PATH="$HOME/.dotnet:$PATH" dotnet run --project core/src/SowSiege.Sim -- interactive-replay data artifacts/device-runs/<sessionId>/run.ssreplay
node tools/device-metrics.mjs artifacts/device-runs/<sessionId> --commit <recorded-40-character-commit> --posture unfolded --output artifacts/device-frame-metrics.json
```

Storage access varies by Android version. For a debuggable player, `adb shell run-as com.hyunlord.sowsiege` can inspect app-owned storage; use the runtime persistent path, rather than assuming a different installation's location. Keep source CSV, replay, JSON metadata, screenshots, and build provenance together. Do not relabel a dirty build as a clean commit: `sourceHash` and `sourceDirty` remain part of the metrics configuration.

The Phase1A device metrics tool accepts only recorded Android OS identity with the IL2CPP backend. Editor/Mono, desktop IL2CPP, and iOS inputs are rejected; iOS support requires a separately authorized platform contract. Existing build, data, and source identity checks still apply.

The frame axis uses unscaled rendered-frame delta in milliseconds, not Core tick duration. Because Unity reports the preceding render interval, telemetry stages frame/tick/speed/pause metadata after presentation and writes it at the next Update. Card/menu transitions therefore retain the preceding active stall. Normal terminal telemetry finishes one Update later so the terminal render interval is included. Its late window is the final quarter of the configured run (ticks 16200–21600 for production), including the boundary-crossing frame. Eligible samples are unpaused, unsuspended, complete intervals at speed 1. Background/focus interruptions are retained with `suspended=1`; quit/retry before another Update writes the measured monotonic tail with `partial=1`. Those interrupted or partial intervals remain visible in raw CSV but are excluded from full rendered-frame statistics. A partial terminal interval cannot claim complete coverage. Sort all eligible values and select index `ceil(0.95 * count) - 1`; retain every slow frame. Maxima for entity/event counters cover the complete captured run. A complete late window requires observation before its start and through the final tick with no active accelerated samples. Partial runs remain explicitly incomplete; no eligible samples yield null p95/max. Pauses remain in raw CSV. Screen-size changes are retained; specify posture as folded, unfolded, mixed, or unknown from actual observation, never inferred hinge state.

To verify the five canonical replays on the actual IL2CPP player, open **개발 → 기기 재생 5개 검증**. The UI reads packaged bytes on the main thread and verifies them on a pure-Core worker while gameplay is paused. The result is written to `Application.persistentDataPath/parity/device-parity.json`. Pull that directory with adb. Require five passing results for seeds 30000–30004, tick 21600, backend IL2CPP, matching data hash and state hashes from `artifacts/phase1a/replays/hashes.json`. Compare each result's `inputSha256` against the exact corresponding packaged `.ssreplay` bytes; do not substitute independently regenerated commands. Preserve failed input bytes and results. The development fixture pack and debug types must be absent from a release build.

Capture actual folded and unfolded Korean UI, safe areas, joystick/menu interaction, end screen, and the open developer menu. Check Korean glyph rendering on the physical device. Record device model/OS, screen size, build/source/data identity and thermal categories alongside captures. Neither a synthetic telemetry test nor an Editor screenshot establishes physical-device usability or a 60 fps result.

A boot/font/data failure shows a diagnostic and stops the run. It does not offer an unsafe return into an invalid catalog/session. Keep the diagnostic and available replay checkpoint, correct the reported cause, then relaunch the app. The English diagnostic fallback is intentional when Korean font initialization itself fails.

Thermal collection uses Android's existing [`PowerManager.getCurrentThermalStatus()`](https://developer.android.com/reference/android/os/PowerManager#getCurrentThermalStatus()), available from API 29, through Unity's built-in Android JNI module. The integer is mapped to the documented throttling categories; older platforms or failed queries remain `unavailable`. No external Android SDK package is introduced.

## Reproduce a selected Release sample

The selected run publishes only the original `frames.csv`, `run.ssreplay`, `device.json`, and `recording.json`. Download them into one directory and use the APK's recorded source commit and matching canonical data. Verify the replay with the CLI first. Reconstruct the local summary contract below, then run `device-metrics.mjs` and `metrics.mjs` as above. Generated summary/statistics/HTML are not additional Release assets.

```sh
node --input-type=module - "$run_dir" <<'JS'
import fs from 'node:fs';
import { parseCsv } from './tools/csv.mjs';
import { headers } from './tools/device-metrics.mjs';
const dir = process.argv[2];
const identity = JSON.parse(fs.readFileSync(`${dir}/recording.json`));
const { durationTicks } = JSON.parse(fs.readFileSync('data/tuning.json'));
const frames = parseCsv(fs.readFileSync(`${dir}/frames.csv`, 'utf8'), headers).length;
fs.writeFileSync(`${dir}/frame-summary.json`, JSON.stringify({
  sessionId: identity.sessionId, build: identity.build, dataHash: identity.dataHash,
  durationTicks, lateStartTick: Math.floor(durationTicks * 3 / 4), frames
}, null, 2));
JS
```

This reconstructs a compatibility input, not an independent producer frame-count check. Retain the original device summary locally for that check. The exact replay terminal/checkpoints, raw frame continuity and complete late-window checks still apply. The selected-run description states the observed posture; never infer physical folding from dimensions.
