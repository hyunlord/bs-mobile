using SowSiege.Core;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Game.App.Generated;
using Game.View;
using UnityEngine;
#endif

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        partial void BeginLongFrame();
        partial void EndLongFrame();
        partial void BeginLongFrameCommand(ReplayCommandKind kind);
        partial void EndLongFrameCommand(ReplayCommandKind kind);
        partial void BeginLongFrameSnapshot();
        partial void EndLongFrameSnapshot();
        partial void BeginLongFrameHud();
        partial void EndLongFrameHud();
        partial void LongFrameViewPresented();
        partial void LongFrameLifecycle(bool focusEvent, bool value);
        partial void FinishLongFrames();
        partial void LongFrameBenchmarkMetadata(BenchmarkResult result);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void RefreshLongFrameCountersAfterLoading()
        {
            if (longFrames != null && !longFramesClosed) longFrames.RefreshRegistrationAfterLoading();
        }
        void ResetLongFramesForRepeat()
        {
            if (longFrames != null && !longFramesClosed) FinishLongFrames();
            longFrames = null; longFrameStages = null; longFrameSession = null; longFrameWorld = null;
            longFramesChecked = false; longFramesClosed = false; longFramePending = false; longFrameActive = false;
            longFrame = default; longFrameEventId = -1; longFramePresentationId = -1;
            longFrameEnemyKinds.Clear(); longFrameEffectKinds.Clear();
        }
        DevelopmentLongFrameRecorder longFrames;
        DevelopmentLongFrameRecorder.FrameSample longFrame;
        LongFrameStages longFrameStages;
        bool longFramesChecked, longFrameActive, longFramesClosed, longFramePending;
        int longFrameGcStart;
        long longFrameCommandStart, longFrameSnapshotStart, longFrameHudStart, longFrameEventId = -1, longFramePresentationId = -1;
        InteractiveSession longFrameSession;
        WorldRenderer longFrameWorld;
        RunStatus longFrameStatus;
        int longFrameSeason = -1;
        readonly HashSet<string> longFrameEnemyKinds = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<(string Source, string Kind)> longFrameEffectKinds = new HashSet<(string Source, string Kind)>();
        string longFrameOutput;
        static double LongFrameMilliseconds(long elapsed) => elapsed * 1000d / Stopwatch.Frequency;

        sealed class LongFrameStages : IRuntimeDiagnostics
        {
            readonly long[] starts = new long[10];
            internal readonly double[] Milliseconds = new double[10];
            internal IRuntimeDiagnostics Forward;
            internal bool Active;
            public void Begin(RuntimeStage stage)
            {
                Forward?.Begin(stage);
                if (Active) starts[(int)stage] = Stopwatch.GetTimestamp();
            }
            public void End(RuntimeStage stage)
            {
                if (Active) Milliseconds[(int)stage] += LongFrameMilliseconds(Stopwatch.GetTimestamp() - starts[(int)stage]);
                Forward?.End(stage);
            }
            internal void Reset() => Array.Clear(Milliseconds, 0, Milliseconds.Length);
        }

        partial void BeginLongFrame()
        {
            if (!longFramesChecked)
            {
                longFramesChecked = true;
                if (DevelopmentLongFrameRecorder.Requested(Environment.GetCommandLineArgs()))
                {
                    WorldRenderer.RegisterDiagnosticsMarkers();
                    longFrames = new DevelopmentLongFrameRecorder();
                    WorldRenderer.DiagnosticCapacityGrowth += ObserveLongFrameCapacityGrowth;
                    longFrameStages = new LongFrameStages();
                    longFrameOutput = Path.Combine(Application.persistentDataPath, "long-frame-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
                }
            }
            if (longFrames == null || longFramesClosed) return;
            var now = Stopwatch.GetTimestamp();
            if (longFramePending)
            {
                longFrame.ObservedFrame = Time.frameCount;
                longFrame.EndedTimestamp = now;
                longFrame.ObservedTimestamp = now;
                longFrame.WallMs = LongFrameMilliseconds(now - longFrame.StartedTimestamp);
                longFrame.Gc = GC.CollectionCount(0) - longFrameGcStart;
                longFrames.Observe(longFrame);
                longFramePending = false;
            }
            if (Session == null || CurrentFrame == null || (waveBenchmarkRequested && (!waveBenchmarkReady || waveBenchmarkEnded))) return;
            if (!ReferenceEquals(Session, longFrameSession))
            {
                longFrameSession = Session; longFrameEventId = CurrentWave != null && CurrentWave.Events.Count > 0 ? CurrentWave.Events[CurrentWave.Events.Count - 1].Id : -1;
                longFramePresentationId = CurrentFrame.Events.Count > 0 ? CurrentFrame.Events[CurrentFrame.Events.Count - 1].Id : -1;
                longFrameEnemyKinds.Clear(); longFrameEffectKinds.Clear();
                longFrameStatus = CurrentFrame.Status; longFrameSeason = CurrentFrame.Season;
                LongFrameEvent(DevelopmentLongFrameRecorder.EventFlags.Other, recording?.ReplayPath ?? "benchmark-session-start", seed);
            }
            if (!ReferenceEquals(Session.Diagnostics, longFrameStages))
            {
                longFrameStages.Forward = Session.Diagnostics;
                Session.Diagnostics = longFrameStages;
            }
            if (world != null)
            {
                world.PresentationDiagnosticsEnabled = true;
                if (!ReferenceEquals(world, longFrameWorld))
                {
                    if (longFrameWorld != null) longFrameWorld.DiagnosticEffectCreated -= ObserveLongFrameEffect;
                    longFrameWorld = world;
                    longFrameWorld.DiagnosticEffectCreated += ObserveLongFrameEffect;
                }
            }
            longFrameStages.Reset();
            var windowPosition = Screen.mainWindowPosition;
            longFrame = new DevelopmentLongFrameRecorder.FrameSample
            {
                Frame = Time.frameCount, Tick = CurrentFrame.Tick, RuntimeSeconds = CurrentFrame.Tick / (double)CurrentFrame.TickRate,
                Enemies = CurrentFrame.Enemies.Count, StartedTimestamp = now, Width = Screen.width, Height = Screen.height,
                WindowX = windowPosition.x, WindowY = windowPosition.y,
                Focused = Application.isFocused, Paused = Paused, ViewMs = -1, SpriteBatchMs = -1, EffectsMs = -1
            };
            longFrameGcStart = GC.CollectionCount(0);
            longFrameActive = true;
        }
        partial void EndLongFrame()
        {
            if (!longFrameActive) return;
            longFrameActive = false;
            var s = longFrameStages.Milliseconds;
            longFrame.EnemyMs = s[(int)RuntimeStage.Enemy]; longFrame.SeparationMs = s[(int)RuntimeStage.Separation];
            longFrame.ResidualMs = s[(int)RuntimeStage.Residual]; longFrame.MovementMs = s[(int)RuntimeStage.Movement];
            longFrame.WorkMs = s[(int)RuntimeStage.Work]; longFrame.AttackMs = s[(int)RuntimeStage.Attack];
            longFrame.CollectMs = RuntimeDiagnosticsCapabilities.FineStagesEnabled ? s[(int)RuntimeStage.Collect] : -1;
            longFrame.HarvestMs = RuntimeDiagnosticsCapabilities.FineStagesEnabled ? s[(int)RuntimeStage.Harvest] : -1;
            longFrame.SpawnMs = RuntimeDiagnosticsCapabilities.FineStagesEnabled ? s[(int)RuntimeStage.Spawn] : -1;
            longFrame.ClearMs = RuntimeDiagnosticsCapabilities.FineStagesEnabled ? s[(int)RuntimeStage.Clear] : -1;
            longFrame.Focused &= Application.isFocused;
            longFrame.UpdateExitTimestamp = Stopwatch.GetTimestamp();
            longFramePending = true;
        }
        partial void BeginLongFrameCommand(ReplayCommandKind kind)
        {
            if (!longFrameActive || kind != ReplayCommandKind.Advance) return;
            longFrameStages.Active = true; longFrameCommandStart = Stopwatch.GetTimestamp();
        }
        partial void EndLongFrameCommand(ReplayCommandKind kind)
        {
            if (!longFrameActive || kind != ReplayCommandKind.Advance) return;
            longFrame.CoreMs += LongFrameMilliseconds(Stopwatch.GetTimestamp() - longFrameCommandStart);
            longFrame.Advances++; longFrameStages.Active = false;
        }
        partial void BeginLongFrameSnapshot() { if (longFrameActive) longFrameSnapshotStart = Stopwatch.GetTimestamp(); }
        partial void EndLongFrameSnapshot()
        {
            if (!longFrameActive) return;
            longFrame.SnapshotMs += LongFrameMilliseconds(Stopwatch.GetTimestamp() - longFrameSnapshotStart);
            ObserveLongFrameEvents();
        }
        partial void BeginLongFrameHud() { if (longFrameActive) longFrameHudStart = Stopwatch.GetTimestamp(); }
        partial void EndLongFrameHud() { if (longFrameActive) longFrame.HudMs += LongFrameMilliseconds(Stopwatch.GetTimestamp() - longFrameHudStart); }
        partial void LongFrameViewPresented() { if (longFrameActive) longFrame.ViewMs = world.LastDiagnostics.TotalMs; }
        void LongFrameEvent(DevelopmentLongFrameRecorder.EventFlags flag, string detail = null, long delta = 0)
        {
            longFrames.RecordEvent(flag, Time.frameCount, Stopwatch.GetTimestamp(), Screen.width, Screen.height, detail, delta);
        }
        void ObserveLongFrameEvents()
        {
            if (CurrentFrame.Status == RunStatus.AwaitingCard && longFrameStatus != RunStatus.AwaitingCard)
                LongFrameEvent(DevelopmentLongFrameRecorder.EventFlags.Card, "level-up-offer");
            longFrameStatus = CurrentFrame.Status;
            if (CurrentFrame.Season != longFrameSeason)
            {
                LongFrameEvent(DevelopmentLongFrameRecorder.EventFlags.WaveStart, "season-transition", CurrentFrame.Season);
                longFrameSeason = CurrentFrame.Season;
            }
            for (var i = 0; i < CurrentFrame.Enemies.Count; i++)
            {
                var id = CurrentFrame.Enemies[i].DefinitionId;
                if (longFrameEnemyKinds.Add(id)) LongFrameEvent(DevelopmentLongFrameRecorder.EventFlags.FirstEnemy, id);
            }
            for (var i = 0; i < CurrentFrame.Events.Count; i++)
            {
                var e = CurrentFrame.Events[i];
                if (e.Id <= longFramePresentationId) continue;
                longFramePresentationId = e.Id;
                if (e.Kind == PresentationKind.KillExperience || e.Kind == PresentationKind.HarvestExperience || e.Kind == PresentationKind.TaxExperience)
                {
                    longFrame.ExperienceDelta += e.Amount;
                    longFrame.Events |= DevelopmentLongFrameRecorder.EventFlags.Experience;
                }
            }
            if (CurrentWave == null) return;
            for (var i = 0; i < CurrentWave.Events.Count; i++)
            {
                var e = CurrentWave.Events[i];
                if (e.Id <= longFrameEventId) continue;
                longFrameEventId = e.Id;
                var flag = DevelopmentLongFrameRecorder.EventFlags.None;
                if (e.Kind == "evolution-activated") flag = DevelopmentLongFrameRecorder.EventFlags.Evolution;
                else if (e.Kind == "enemy-spawn" && e.Source == runCatalog.WaveRuntime.BossId) flag = DevelopmentLongFrameRecorder.EventFlags.Boss;
                else if (e.Kind == "harvest-complete") flag = DevelopmentLongFrameRecorder.EventFlags.Harvest;
                else if (e.Kind == "reward-collected")
                {
                    flag = DevelopmentLongFrameRecorder.EventFlags.Collect;
                }
                if (flag != DevelopmentLongFrameRecorder.EventFlags.None) LongFrameEvent(flag, e.Source, e.Amount);
            }
            for (var i = 0; i < CurrentWave.Attacks.Count; i++)
            {
                var effect = CurrentWave.Attacks[i];
                if (longFrameEffectKinds.Add((effect.Source, effect.Kind))) LongFrameEvent(DevelopmentLongFrameRecorder.EventFlags.FirstEffect, effect.Source);
            }
        }
        void ObserveLongFrameEffect(string source, string kind)
        {
            if (longFramesClosed || !longFrameEffectKinds.Add((source, kind))) return;
            LongFrameEvent(DevelopmentLongFrameRecorder.EventFlags.FirstEffect, source);
        }
        void ObserveLongFrameCapacityGrowth(string collection, int before, int after, int count)
        {
            if (longFrames == null || longFramesClosed) return;
            longFrames.RecordCapacityGrowth(Time.frameCount, CurrentFrame?.Tick ?? 0, Stopwatch.GetTimestamp(), collection, before, after, count);
        }
        partial void LongFrameLifecycle(bool focusEvent, bool value)
        {
            if (longFrames == null || longFramesClosed) return;
            if (focusEvent && !value) longFrame.Focused = false;
            LongFrameEvent(focusEvent ? DevelopmentLongFrameRecorder.EventFlags.FocusChanged : DevelopmentLongFrameRecorder.EventFlags.PauseChanged,
                focusEvent ? "application-focus" : "application-pause", value ? 1 : 0);
        }
        partial void LongFrameBenchmarkMetadata(BenchmarkResult result)
        {
            if (longFrames == null) return;
            result.diagnosticOnly = true; result.performanceEligible = false;
        }
        partial void FinishLongFrames()
        {
            if (longFrames == null || longFramesClosed) return;
            longFramesClosed = true;
            try
            {
                var destination = benchmarkOutput == null ? longFrameOutput : Path.Combine(benchmarkOutput, "long-frame-diagnostics");
                longFrames.Write(destination);
                File.WriteAllText(Path.Combine(destination, "provenance.json"), JsonUtility.ToJson(new LongFrameProvenance
                {
                    commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash, sourceDirty = BuildIdentity.SourceDirty,
                    dataHash = benchmarkReplay?.Header.Options.DataHash ?? CanonicalContent.DataHash,
                    replaySha256 = benchmarkReplaySha, replayPath = recording?.ReplayPath, seed = seed, finalUnclosedInterval = longFramePending || longFrameActive
                }, true));
                UnityEngine.Debug.Log("Development long-frame diagnostics: " + destination);
            }
            finally
            {
                if (Session != null && ReferenceEquals(Session.Diagnostics, longFrameStages)) Session.Diagnostics = longFrameStages.Forward;
                WorldRenderer.DiagnosticCapacityGrowth -= ObserveLongFrameCapacityGrowth;
                if (longFrameWorld != null) longFrameWorld.DiagnosticEffectCreated -= ObserveLongFrameEffect;
                longFrames.Dispose();
            }
        }
#if UNITY_EDITOR
        public void EditorBeginLongFrameForTests(InteractiveSession session, ContentCatalog catalog, DevelopmentLongFrameRecorder recorder)
        {
            Session = session; runCatalog = catalog; CaptureSnapshots();
            longFramesChecked = true; longFrames = recorder; longFrameStages = new LongFrameStages();
            BeginLongFrame();
        }
        public DevelopmentLongFrameRecorder.FrameSample EditorApplyLongFrameForTests(ReplayCommand command)
        {
            BeginLongFrameCommand(command.Kind);
            try { Session.Apply(command); }
            finally { EndLongFrameCommand(command.Kind); }
            CaptureSnapshots(); EndLongFrame(); return longFrame;
        }
        public void EditorDetachLongFrameForTests()
        {
            if (Session != null && ReferenceEquals(Session.Diagnostics, longFrameStages)) Session.Diagnostics = longFrameStages.Forward;
            WorldRenderer.DiagnosticCapacityGrowth -= ObserveLongFrameCapacityGrowth;
            longFramesClosed = true; longFrames = null;
        }
#endif
        [Serializable] sealed class LongFrameProvenance
        {
            public string commit, sourceHash, dataHash, replaySha256, replayPath;
            public int seed;
            public bool sourceDirty, finalUnclosedInterval;
            public bool diagnosticOnly = true, performanceEligible = false;
            public bool fineCoreStagesEnabled = RuntimeDiagnosticsCapabilities.FineStagesEnabled;
            public string contract = "Development-only; Update-to-next-Update intervals: StartedTimestamp is entry, UpdateExitTimestamp is update exit, EndedTimestamp is next entry. Final interval without a next Update is omitted. Core stages inclusive/nested; movement is AI/contact loop. HUD cost is Present wrapper, exact text/rebuild/effect work is in separately observed ProfilerRecorder counters. SpriteBatchMs and EffectsMs are -1, not zero. Window position uses Screen.mainWindowPosition; size/position changes sampled at frame entry and focus/pause callbacks recorded. First enemy/effect means first observed in this recorder session (benchmark pre-roll not observed). WaveStart marks season transition, not invented spawn wave. ExperienceDelta sums actual new kill/harvest/tax experience presentation events; no mass threshold. Counter observations are not timestamp-proven exact-frame attribution. Stacks require separate raw profiler export.";
        }
#endif
    }
}
