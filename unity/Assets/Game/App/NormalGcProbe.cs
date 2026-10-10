using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Game.App.Generated;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.App
{
    public struct NormalPlayMetrics
    {
        public double CoreMs, DrawMs, SnapshotMs, HudMs;
        public int CoreTicks, Enemies, VisibleEnemies, DrawCalls, SpriteInstances;
        public float CameraX, CameraY, CameraHalfHeight, LordX, LordY;
        public int MapWidth, MapHeight;
        public bool LordBoundaryBlocked, CameraBoundaryClamped;
    }

    // Diagnostic-only normal UI input and whole-application GC observation; no recorder components.
    public sealed class NormalGcProbe : MonoBehaviour
    {
        public static string ProfileDirectory { get; } = ReadPath("--normal-gc-profile");
        public static string PreferencesPrefix { get; } = Requested(Environment.GetCommandLineArgs()) ? "sowsiege.normalprobe." + Guid.NewGuid().ToString("N") + "." : null;
        public static int? RequestedSeed => ParseSeed(Environment.GetCommandLineArgs());
        public static string RequestedTopology => ParseTopology(Environment.GetCommandLineArgs());
        public static int? ParseSeed(string[] args)
        {
            if (!Requested(args)) return null;
            var value = ReadOption(args, "--normal-gc-seed", "30001");
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed) || seed < 0 || seed == int.MaxValue)
                throw new ArgumentException("--normal-gc-seed requires a nonnegative restartable integer.");
            return seed;
        }
        public static string ParseTopology(string[] args)
        {
            if (!Requested(args)) return null;
            var value = ReadOption(args, "--normal-gc-topology", "old");
            if (value != "old" && value != "field") throw new ArgumentException("--normal-gc-topology requires old or field.");
            return value;
        }
        static string ReadOption(string[] args, string flag, string fallback)
        {
            var index = Array.IndexOf(args, flag);
            if (index < 0) return fallback;
            if (Array.LastIndexOf(args, flag) != index || index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException(flag + " requires one value.");
            return args[index + 1];
        }
        public static bool Requested(string[] args)
        {
            var enabled = Array.IndexOf(args, "--normal-gc-probe") >= 0;
            if (enabled && (Array.IndexOf(args, "--autoplay-capture") >= 0 || Array.IndexOf(args, "--smoothness-trace") >= 0 || Array.IndexOf(args, "--golden-minute") >= 0 || Array.IndexOf(args, "--feel-trial") >= 0 || Array.IndexOf(args, "--wave-benchmark") >= 0 || Array.IndexOf(args, "--field-video") >= 0))
                throw new ArgumentException("Normal GC probe cannot run with capture or other trace instrumentation.");
            return enabled;
        }
        static string ReadPath(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            if (!Requested(args)) return null;
            var i = Array.IndexOf(args, flag);
            if (i < 0 || Array.LastIndexOf(args, flag) != i || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException(flag + " requires a new output directory.");
            var p = Path.GetFullPath(args[i + 1]);
            if (File.Exists(p) || Directory.Exists(p) && Directory.GetFileSystemEntries(p).Length != 0) throw new IOException(flag + " must be new or empty.");
            return p;
        }
        public static int CollectionDelta(int previous, int current)
        {
            if (previous < 0 || current < previous) throw new ArgumentOutOfRangeException(nameof(current));
            return current - previous;
        }
        public static bool ReachedSixMinutes(int tick, int tickRate) => tickRate > 0 && tick >= checked(tickRate * 360);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!Requested(Environment.GetCommandLineArgs())) return;
            _ = RequestedSeed; _ = RequestedTopology;
            var folder = ReadPath("--normal-gc-output");
            if (ProfileDirectory == null) throw new InvalidOperationException("Isolated profile required.");
            if (folder == ProfileDirectory) throw new ArgumentException("GC output and isolated profile must be distinct directories.");
            var owner = new GameObject("Normal GC probe"); DontDestroyOnLoad(owner);
            var probe = owner.AddComponent<NormalGcProbe>(); probe.folder = folder;
        }
        const int Capacity = 100000;
        readonly Row[] rows = new Row[Capacity];
        readonly List<Button> buttons = new List<Button>(64);
        struct Row { public int Frame, Tick, G0, G1, G2, D0, D1, D2; public double Wall, Dt, UnityDt; public bool Paused, Focus; public NormalPlayMetrics Metrics; }
        RunCoordinator run;
        Mouse mouse;
        NormalPlayTrace.ScriptedUiDeviceFilter filter;
        string folder, failure;
        bool begun, ended, pendingCard;
        int count, previous0, previous1, previous2, initial0, initial1, initial2, buttonPhase, titleStage, cardSelections, lastTargetTick = -1;
        double setupAt, nextUi, cardAt = -1;
        long beganStamp, previousStamp;
        Vector2 click, stickOrigin, direction;
        void Awake() { setupAt = Time.realtimeSinceStartupAsDouble; }
        void Update()
        {
            if (ended) return;
            if (run == null) run = FindFirstObjectByType<RunCoordinator>();
            if (mouse == null && run != null && run.Ui != null && EventSystem.current != null)
            {
                mouse = InputSystem.AddDevice<Mouse>("NormalGcProbeMouse");
                filter = new NormalPlayTrace.ScriptedUiDeviceFilter(EventSystem.current.GetComponent<InputSystemUIInputModule>(), mouse);
                InputSystem.onBeforeUpdate += DriveInput;
            }
            if (!begun && Time.realtimeSinceStartupAsDouble - setupAt > 45) Finish("Normal UI did not start the run within 45 seconds.");
        }
        void LateUpdate()
        {
            if (ended || run == null || run.Frame == null) return;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!begun)
            {
                begun = true; beganStamp = previousStamp = now;
                initial0 = previous0 = GC.CollectionCount(0); initial1 = previous1 = GC.CollectionCount(1); initial2 = previous2 = GC.CollectionCount(2);
            }
            if (count == Capacity) { Finish("Numeric observation capacity exhausted."); return; }
            var g0 = GC.CollectionCount(0); var g1 = GC.CollectionCount(1); var g2 = GC.CollectionCount(2);
            rows[count++] = new Row { Frame = Time.frameCount, Tick = run.Frame.Tick, Wall = Seconds(now - beganStamp), Dt = Seconds(now - previousStamp), UnityDt = Time.unscaledDeltaTime,
                G0 = g0, G1 = g1, G2 = g2, D0 = CollectionDelta(previous0, g0), D1 = CollectionDelta(previous1, g1), D2 = CollectionDelta(previous2, g2),
                Paused = run.Frame.Status == RunStatus.AwaitingCard || run.CaptureClockPaused, Focus = Application.isFocused, Metrics = run.ReadNormalPlayMetrics() };
            previousStamp = now; previous0 = g0; previous1 = g1; previous2 = g2;
            if (ReachedSixMinutes(run.Frame.Tick, run.Frame.TickRate)) Finish(null);
            else if (run.Frame.Status == RunStatus.Completed) Finish("Normal run ended before six gameplay minutes; no resurrection or grants applied.");
            else if (Seconds(now - beganStamp) > 900) Finish("Normal run exceeded 15 wall-clock minutes.");
        }
        static double Seconds(long ticks) => ticks / (double)System.Diagnostics.Stopwatch.Frequency;
        void DriveInput()
        {
            if (ended || mouse == null || run == null || run.Ui == null || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            if (buttonPhase > 0)
            {
                if (buttonPhase == 1) { Queue(click, false); buttonPhase = 2; }
                else if (buttonPhase == 2) { Queue(click, true); buttonPhase = 3; }
                else { Queue(click, false); buttonPhase = 0; nextUi = Time.realtimeSinceStartupAsDouble + .5; if (pendingCard) { cardSelections++; pendingCard = false; cardAt = -1; } }
                return;
            }
            if (run.Frame == null)
            {
                if (Time.realtimeSinceStartupAsDouble < nextUi) return;
                buttons.Clear(); run.Ui.GetComponentsInChildren(false, buttons);
                foreach (var b in buttons)
                {
                    if (!b.interactable) continue;
                    var t = b.GetComponentInChildren<Text>(); if (t == null) continue;
                    if (titleStage == 0 ? b.name != "출정 준비" : titleStage == 1 ? !t.text.Contains("씨앗 자루") : t.text != "시작하기" && t.text != "개척 시작") continue;
                    PrepareClick(b); titleStage++; break;
                }
                return;
            }
            if (run.Frame.Status == RunStatus.AwaitingCard)
            {
                Queue(stickOrigin, false);
                if (cardAt < 0) cardAt = Time.realtimeSinceStartupAsDouble;
                if (Time.realtimeSinceStartupAsDouble - cardAt < 2 || Time.realtimeSinceStartupAsDouble < nextUi) return;
                // Card choices are discrete UI transitions, separately counted; no per-frame card scans.
                var selected = WaveCaptureInput.ChooseCard(run.Session.View.CaptureCards(), run.Frame, FoundationBoot.Catalog, "meta:grain");
                buttons.Clear(); run.Ui.GetComponentsInChildren(false, buttons);
                foreach (var b in buttons) if (b.interactable && b.name == "Choose " + selected) { PrepareClick(b); pendingCard = true; break; }
                return;
            }
            if (run.Frame.Status != RunStatus.Running) { Queue(stickOrigin, false); return; }
            if (run.Frame.Tick != lastTargetTick)
            {
                lastTargetTick = run.Frame.Tick;
                var target = GrowthTarget(); var origin = run.Frame.Lord.Position;
                direction = CaptureArrivalSteering.Direction(new Vector2(target.X - origin.X, target.Y - origin.Y), FoundationBoot.Catalog.Tuning.World.Map.LordSpeed);
            }
            if (!run.Stick.Active) { stickOrigin = new Vector2(Screen.width * .25f, Screen.height * .28f); Queue(stickOrigin, true); }
            else Queue(stickOrigin + direction * run.Stick.Radius, true);
        }
        WorldPoint GrowthTarget()
        {
            var f = run.Frame; var wave = run.Wave; var p = f.Lord.Position;
            var target = new WorldPoint(f.Estate.X + (int)(Math.Cos(f.Tick / 180d) * 1500), f.Estate.Y + (int)(Math.Sin(f.Tick / 180d) * 1500));
            if (wave == null) return target;
            long best = long.MaxValue;
            for (var i = 0; i < wave.Rewards.Count; i++) { var reward = wave.Rewards[i]; var d = Distance(reward.Position, p); if (d < best) { best = d; target = reward.Position; } }
            if (best != long.MaxValue) return target;
            for (var i = 0; i < wave.Work.Count; i++) { var work = wave.Work[i]; if (work.Kind == "grain" && work.Complete && work.Health > 0) { var d = Distance(work.Position, p); if (d < best) { best = d; target = work.Position; } } }
            if (best != long.MaxValue) return target;
            for (var i = 0; i < wave.Work.Count; i++) { var work = wave.Work[i]; if (work.Kind == "building" && !work.Complete && work.Health > 0) { var d = Distance(work.Position, p); if (d < best) { best = d; target = work.Position; } } }
            if (best != long.MaxValue) return new WorldPoint(target.X + (int)(Math.Cos(f.Tick / 30d) * 100), target.Y + (int)(Math.Sin(f.Tick / 30d) * 100));
            return target;
        }
        static long Distance(WorldPoint a, WorldPoint b) { var x = (long)a.X - b.X; var y = (long)a.Y - b.Y; return x * x + y * y; }
        void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= DriveInput;
            filter?.Dispose();
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            ClearPreferences();
        }
        static void ClearPreferences()
        {
            if (PreferencesPrefix == null) return;
            foreach (var key in RunPreferences.Keys) PlayerPrefs.DeleteKey(PreferencesPrefix + key);
            PlayerPrefs.Save();
        }
        void PrepareClick(Button button) { var r = (RectTransform)button.transform; click = RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)); buttonPhase = 1; }
        void Queue(Vector2 position, bool down) => InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left, down));
        void Finish(string reason)
        {
            if (ended) return; ended = true; failure = reason;
            if (mouse != null) { Queue(stickOrigin, false); InputSystem.onBeforeUpdate -= DriveInput; filter?.Dispose(); filter = null; InputSystem.RemoveDevice(mouse); mouse = null; }
            string replayPath = null, replayHash = null, stateHash = null;
            var replayVerified = false; var fieldVersion = 0; var replayFormat = 0;
            try
            {
                if (run?.Session != null)
                {
                    replayPath = run.FinishNormalProbeRecording();
                    using var stream = File.OpenRead(replayPath);
                    var replay = ReplayCodec.Read(stream);
                    foreach (var command in replay.Commands) if (!AutoplayCapture.IsOrdinaryCommand(command.Kind)) throw new InvalidOperationException("Probe replay contains nonordinary input.");
                    if (replay.Header.Options.Feel != null) throw new InvalidOperationException("Capture-free topology comparison requires all rule experiments OFF.");
                    if ((replay.Header.Options.Field != null) != (RequestedTopology == "field")) throw new InvalidOperationException("Replay topology differs from requested comparison.");
                    fieldVersion = replay.Header.Options.Field?.Version ?? 0;
                    replayFormat = replay.Header.FormatVersion;
                    var verified = run.VerifyNormalProbeReplay(replay);
                    stateHash = verified.StateHash; replayVerified = true;
                    using var hash = SHA256.Create(); replayHash = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(replayPath))).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception error) { failure = (failure ?? "") + "\nReplay finalization: " + error; }
            finally { ClearPreferences(); }
            Directory.CreateDirectory(folder);
            using (var file = new StreamWriter(Path.Combine(folder, "normal-gc-frames.csv")))
            {
                file.WriteLine("frame,tick,wallSeconds,wallDtSeconds,unityDtSeconds,gen0,gen1,gen2,delta0,delta1,delta2,paused,focused,nextWallDtSeconds,coreMs,coreTicks,drawMs,snapshotMs,hudMs,enemies,visibleEnemies,drawCalls,spriteInstances,cameraX,cameraY,cameraHalfHeight,lordX,lordY,mapWidth,mapHeight,lordBoundaryBlocked,cameraBoundaryClamped");
                for (var i = 0; i < count; i++) { var r = rows[i]; file.WriteLine(FormattableString.Invariant($"{r.Frame},{r.Tick},{r.Wall:R},{r.Dt:R},{r.UnityDt:R},{r.G0},{r.G1},{r.G2},{r.D0},{r.D1},{r.D2},{(r.Paused ? 1 : 0)},{(r.Focus ? 1 : 0)},{(i + 1 < count ? rows[i + 1].Dt : -1):R},{r.Metrics.CoreMs:R},{r.Metrics.CoreTicks},{r.Metrics.DrawMs:R},{r.Metrics.SnapshotMs:R},{r.Metrics.HudMs:R},{r.Metrics.Enemies},{r.Metrics.VisibleEnemies},{r.Metrics.DrawCalls},{r.Metrics.SpriteInstances},{r.Metrics.CameraX:R},{r.Metrics.CameraY:R},{r.Metrics.CameraHalfHeight:R},{r.Metrics.LordX:R},{r.Metrics.LordY:R},{r.Metrics.MapWidth},{r.Metrics.MapHeight},{(r.Metrics.LordBoundaryBlocked ? 1 : 0)},{(r.Metrics.CameraBoundaryClamped ? 1 : 0)}")); }
            }
            var paused = 0; var unfocused = 0; for (var i = 0; i < count; i++) { if (rows[i].Paused) paused++; if (!rows[i].Focus) unfocused++; }
            File.WriteAllText(Path.Combine(folder, "normal-gc-result.json"), JsonUtility.ToJson(new Result { commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash, dataHash = CanonicalContent.DataHash, sourceDirty = BuildIdentity.SourceDirty,
                captureOff = true, automatedNormalInput = true, seed = RequestedSeed ?? -1, topology = RequestedTopology, inputPolicy = "reward-ripe-building-circle-v1", rulesOff = true, fieldRulesVersion = fieldVersion, replayFormat = replayFormat, replay = replayPath, replaySha256 = replayHash, replayVerified = replayVerified, stateHash = stateHash, profile = CanonicalContent.ProfileName, isolatedProfile = ProfileDirectory, frames = count, firstTick = count == 0 ? -1 : rows[0].Tick, finalTick = count == 0 ? -1 : rows[count - 1].Tick,
                wallSeconds = count == 0 ? 0 : rows[count - 1].Wall, gen0Collections = previous0 - initial0, gen1Collections = previous1 - initial1, gen2Collections = previous2 - initial2, pausedFrames = paused, unfocusedFrames = unfocused, cardSelections = cardSelections, failure = failure,
                boundary = "Whole application including Core, normal UI/input and replay/telemetry; no image/audio recording, capture harness, per-frame file I/O or LINQ movement. coreMs is accepted Core Advance work per rendered frame; divide by coreTicks for per-tick cost. drawMs is CPU screen construction, not GPU or Present. World geometry coordinates are Unity units; map dimensions are Core units. Current/next wall dt includes ordinary rendering/pacing. CollectionCount changes sampled in LateUpdate; current/next dt does not identify incremental GC slice duration. Ordinary synthetic mouse; reward/ripe/unfinished-building then circle movement, no grants or speed changes." }, true));
            Application.Quit(failure == null ? 0 : 1);
        }
        [Serializable] sealed class Result
        {
            public string commit, sourceHash, dataHash, profile, isolatedProfile, failure, boundary, topology, inputPolicy, replay, replaySha256, stateHash;
            public bool sourceDirty, captureOff, automatedNormalInput, rulesOff, replayVerified;
            public int seed, fieldRulesVersion, replayFormat, frames, firstTick, finalTick, gen0Collections, gen1Collections, gen2Collections, pausedFrames, unfocusedFrames, cardSelections;
            public double wallSeconds;
        }
    }
}
