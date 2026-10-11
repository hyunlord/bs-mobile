#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace Game.App
{
    // Diagnostic observation only. Recorder values carry observation time, not an invented exact frame join.
    public sealed class DevelopmentLongFrameRecorder : IDisposable
    {
        public const int ContextFrames = 4;
        const int RingSize = ContextFrames + 1;
        const int MaximumCounters = 128;
        [Flags] public enum EventFlags { None = 0, FocusChanged = 1, PauseChanged = 2, WindowChanged = 4, Card = 8, Spawn = 16, Harvest = 32, Collect = 64, Other = 128, WaveStart = 256, Boss = 512, Evolution = 1024, Experience = 2048, FirstEnemy = 4096, FirstEffect = 8192 }
        public struct FrameSample
        {
            public int Frame, ObservedFrame, Tick, Enemies, Advances, Gc, Width, Height, WindowX, WindowY;
            public long StartedTimestamp, UpdateExitTimestamp, EndedTimestamp, ObservedTimestamp, ExperienceDelta;
            public double RuntimeSeconds, WallMs, CoreMs, EnemyMs, SeparationMs, ResidualMs, ClearMs, MovementMs, WorkMs, AttackMs, CollectMs, HarvestMs, SpawnMs, SnapshotMs, ViewMs, SpriteBatchMs, HudMs, EffectsMs;
            public bool Focused, Paused;
            public EventFlags Events;
        }
        struct EventSample { public int Frame, Width, Height; public long Timestamp, Delta; public string Detail; public EventFlags Flags; public bool FromFrameSample; }
        struct CounterState { public string Name, Unit, Status; public ProfilerRecorder Recorder; }
        struct CounterObservation { public long Value; public string Unit, Status; }
        static readonly string[] RequiredCounters = { "PlayerLoop", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "Rendering", "RenderLoop", "Gfx.WaitForPresentOnGfxThread", "Gfx.PresentFrame", "GfxDeviceMetal.WaitForLastPresent", "WaitForTargetFPS", "Semaphore.WaitForSignal", "CreateGPUProgram", "Instantiate", "Loading", "SowSiege.View.SpriteBatchRebuild", "SowSiege.View.SpriteBatchSubmit", "SowSiege.View.SpriteBatchFirstUse", "SowSiege.View.HudTextUpdate", "SowSiege.View.HudTextDirty", "SowSiege.View.EffectCreate" };
        readonly FrameSample[] ring = new FrameSample[RingSize], archive;
        readonly CounterObservation[] ringCounters, archivedCounters;
        readonly EventSample[] events;
        readonly CounterState[] counters;
        int ringCount, nextRing, saved, eventCount, lastFrame = -1, lastSavedFrame = -1, contextThrough = -1;
        bool disposed, havePrevious;
        FrameSample previous;
        public int SavedFrames => saved;
        public int LongFrames { get; private set; }
        public int ObservedFrames { get; private set; }
        public bool Overflowed { get; private set; }
        public static bool Requested(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            var first = Array.IndexOf(args, "--diagnostic-long-frames");
            if (first >= 0 && first != Array.LastIndexOf(args, "--diagnostic-long-frames")) throw new ArgumentException("--diagnostic-long-frames may appear once.");
            return first >= 0;
        }
        public DevelopmentLongFrameRecorder(int capacity = 4096, bool startCounters = true, int eventCapacity = 65536)
            : this(capacity, eventCapacity, startCounters, null) { }
        DevelopmentLongFrameRecorder(int capacity, int eventCapacity, bool startCounters, string deferredCounter)
        {
            if (capacity < RingSize) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (eventCapacity < 1) throw new ArgumentOutOfRangeException(nameof(eventCapacity));
            archive = new FrameSample[capacity]; events = new EventSample[eventCapacity];
            counters = deferredCounter != null ? new[] { new CounterState { Name = deferredCounter, Unit = "unavailable", Status = "not-registered-at-start" } } : startCounters ? StartCounters() : Array.Empty<CounterState>();
            ringCounters = new CounterObservation[RingSize * counters.Length]; archivedCounters = new CounterObservation[capacity * counters.Length];
        }
#if UNITY_EDITOR
        public static DevelopmentLongFrameRecorder WithDeferredMarkerForTesting(string name) => new DevelopmentLongFrameRecorder(8, 8, false, name ?? throw new ArgumentNullException(nameof(name)));
#endif
        static bool Wanted(string name)
        {
            for (var i = 0; i < RequiredCounters.Length; i++) if (name == RequiredCounters[i]) return true;
            return name.StartsWith("Gfx.WaitForPresent", StringComparison.Ordinal) || name.IndexOf("Semaphore", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("CreateGPUProgram", StringComparison.Ordinal) >= 0 || name.IndexOf("Instantiate", StringComparison.Ordinal) >= 0 || name.IndexOf("Loading", StringComparison.Ordinal) >= 0;
        }
        static CounterState[] StartCounters()
        {
            var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
            var selected = new List<CounterState>(MaximumCounters);
            try
            {
                for (var i = 0; i < handles.Count; i++)
                {
                    var description = ProfilerRecorderHandle.GetDescription(handles[i]);
                    var name = description.Name;
                    if (!Wanted(name)) continue;
                    if (selected.Count == MaximumCounters) throw new InvalidOperationException("Long-frame counter capacity exceeded.");
                    var recorder = new ProfilerRecorder(handles[i], 1, ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                    selected.Add(new CounterState { Name = name, Unit = description.UnitType.ToString(), Recorder = recorder, Status = recorder.Valid ? "registered" : "unavailable" });
                }
                for (var required = 0; required < RequiredCounters.Length; required++)
                {
                    var found = false;
                    for (var i = 0; i < selected.Count; i++) if (selected[i].Name == RequiredCounters[required]) { found = true; break; }
                    if (!found)
                    {
                        if (selected.Count == MaximumCounters) throw new InvalidOperationException("Long-frame counter capacity exceeded.");
                        selected.Add(new CounterState { Name = RequiredCounters[required], Unit = "unavailable", Status = "not-registered-at-start" });
                    }
                }
                return selected.ToArray();
            }
            catch { for (var i = 0; i < selected.Count; i++) { var recorder = selected[i].Recorder; recorder.Dispose(); } throw; }
        }
        public void RefreshRegistrationAfterLoading()
        {
            ThrowIfDisposed();
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            for (var index = 0; index < counters.Length; index++)
            {
                if (counters[index].Recorder.Valid) continue;
                for (var handle = 0; handle < handles.Count; handle++)
                {
                    var description = ProfilerRecorderHandle.GetDescription(handles[handle]);
                    if (description.Name != counters[index].Name) continue;
                    var recorder = new ProfilerRecorder(handles[handle], 1, ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                    counters[index] = new CounterState { Name = description.Name, Unit = description.UnitType.ToString(), Recorder = recorder, Status = recorder.Valid ? "registered-after-loading" : "unavailable" };
                    break;
                }
            }
        }
        public void RecordEvent(EventFlags flags, int frame, long timestamp, int width, int height, string detail = null, long delta = 0)
        {
            ThrowIfDisposed();
            AppendEvent(flags, frame, timestamp, width, height, false, detail, delta);
        }
        void AppendEvent(EventFlags flags, int frame, long timestamp, int width, int height, bool fromFrameSample, string detail = null, long delta = 0)
        {
            if (flags == EventFlags.None) return;
            if (eventCount == events.Length) { Overflowed = true; return; }
            events[eventCount++] = new EventSample { Flags = flags, Frame = frame, Timestamp = timestamp, Width = width, Height = height, FromFrameSample = fromFrameSample, Detail = detail, Delta = delta };
        }
        public void Observe(in FrameSample supplied)
        {
            ThrowIfDisposed();
            if (supplied.Frame <= lastFrame || supplied.ObservedFrame <= supplied.Frame || supplied.EndedTimestamp < supplied.StartedTimestamp || supplied.ObservedTimestamp < supplied.EndedTimestamp ||
                double.IsNaN(supplied.WallMs) || double.IsInfinity(supplied.WallMs) || supplied.WallMs < 0)
                throw new ArgumentException("Completed frames must be monotonic with valid observation times.");
            var sample = supplied;
            if (havePrevious)
            {
                if (sample.Focused != previous.Focused) sample.Events |= EventFlags.FocusChanged;
                if (sample.Paused != previous.Paused) sample.Events |= EventFlags.PauseChanged;
                if (sample.Width != previous.Width || sample.Height != previous.Height || sample.WindowX != previous.WindowX || sample.WindowY != previous.WindowY) sample.Events |= EventFlags.WindowChanged;
            }
            previous = sample; havePrevious = true; lastFrame = sample.Frame; ObservedFrames++;
            if (sample.Events != EventFlags.None) AppendEvent(sample.Events, sample.Frame, sample.EndedTimestamp, sample.Width, sample.Height, true, null, sample.ExperienceDelta);
            var slot = nextRing; ring[slot] = sample;
            for (var index = 0; index < counters.Length; index++)
            {
                var recorder = counters[index].Recorder;
                ringCounters[slot * counters.Length + index] = new CounterObservation
                {
                    Value = recorder.Valid && recorder.IsRunning && recorder.Count > 0 ? recorder.LastValue : -1,
                    Unit = counters[index].Unit, Status = counters[index].Status
                };
            }
            nextRing = (nextRing + 1) % RingSize; ringCount = Math.Min(RingSize, ringCount + 1);
            if (sample.WallMs > 25)
            {
                LongFrames++; contextThrough = sample.Frame + ContextFrames;
                for (var offset = ringCount - 1; offset >= 0; offset--)
                {
                    var candidate = (slot - offset + RingSize) % RingSize;
                    if (ring[candidate].Frame >= sample.Frame - ContextFrames) Save(candidate);
                }
            }
            else if (sample.Frame <= contextThrough) Save(slot);
        }
        void Save(int slot)
        {
            if (ring[slot].Frame <= lastSavedFrame) return;
            if (saved == archive.Length) { Overflowed = true; return; }
            archive[saved] = ring[slot];
            Array.Copy(ringCounters, slot * counters.Length, archivedCounters, saved * counters.Length, counters.Length);
            lastSavedFrame = ring[slot].Frame; saved++;
        }
        void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(DevelopmentLongFrameRecorder)); }
        public void Write(string directory)
        {
            ThrowIfDisposed();
            if (Directory.Exists(directory)) throw new IOException("Long-frame output directory must be new.");
            Directory.CreateDirectory(directory);
            using (var writer = new StreamWriter(Path.Combine(directory, "long-frames.csv"))) WriteFramesCsv(writer, true);
            using (var writer = new StreamWriter(Path.Combine(directory, "context-frames.csv"))) WriteFramesCsv(writer, false);
            using (var writer = new StreamWriter(Path.Combine(directory, "counter-observations.csv")))
            {
                writer.WriteLine("candidateFrame,observedFrame,observedTimestamp,counter,unit,value,registration,association");
                for (var row = 0; row < saved; row++) for (var counter = 0; counter < counters.Length; counter++)
                {
                    var observation = archivedCounters[row * counters.Length + counter];
                    writer.WriteLine(string.Join(",", N(archive[row].Frame), N(archive[row].ObservedFrame), N(archive[row].ObservedTimestamp), Csv(counters[counter].Name), Csv(observation.Unit), N(observation.Value), Csv(observation.Status), "last-completed-frame-per-recorder-api-not-exact-interval-join"));
                }
            }
            using (var writer = new StreamWriter(Path.Combine(directory, "events.csv"))) WriteEventsCsv(writer);
            File.WriteAllText(Path.Combine(directory, "contract.txt"), "Development-only diagnostic, not acceptance. Frames observed=" + N(ObservedFrames) + "; >25ms=" + N(LongFrames) + "; selected=" + N(saved) + "; overflow=" + Overflowed + ".\nCounter LastValue is previous frame per Recorder API, observed at ObservedFrame. Candidate frame identity is supplied by caller, not timestamp-verified. Unit column is authoritative; -1 means unavailable/no samples, never proven zero. Marker totals aggregate all threads and nested markers must not be summed. Startup marker slots are retried after scene loading and rendered warmup. Earlier -1 observations remain unavailable; registered-after-loading does not retroactively establish availability. Newly named optional markers outside startup slots are not added. Stopwatch timestamps use System.Diagnostics.Stopwatch.Frequency=" + N(System.Diagnostics.Stopwatch.Frequency) + ".\nContext keeps four preceding and following frame IDs where observed; gaps are not synthesized. Callback events retain their own frame/timestamp; frame-sample-boundary events indicate the supplied frame boundary, not an exact callback time. Core/View values are externally supplied and nested Core stages must not be summed. No call stacks are collected here: representative stacks require the separate raw Profiler capture/export. Overflow means incomplete evidence.\n");
        }
        public void WriteFramesCsv(TextWriter writer, bool onlyLongFrames)
        {
            writer.WriteLine("frame,observedFrame,startTimestamp,endTimestamp,observedTimestamp,updateExitTimestamp,tick,runtimeSeconds,enemies,advances,gc,wallMs,coreMs,enemyMs,separationMs,residualMs,clearMs,movementMs,workMs,attackMs,collectMs,harvestMs,spawnMs,snapshotMs,viewMs,spriteBatchMs,hudMs,effectsMs,focused,paused,width,height,windowX,windowY,eventFlags,experienceDelta,densityBin");
            for (var i = 0; i < saved; i++)
            {
                var r = archive[i]; if (onlyLongFrames && r.WallMs <= 25) continue;
                writer.WriteLine(string.Join(",", N(r.Frame), N(r.ObservedFrame), N(r.StartedTimestamp), N(r.EndedTimestamp), N(r.ObservedTimestamp), N(r.UpdateExitTimestamp), N(r.Tick), N(r.RuntimeSeconds), N(r.Enemies), N(r.Advances), N(r.Gc), N(r.WallMs), N(r.CoreMs), N(r.EnemyMs), N(r.SeparationMs), N(r.ResidualMs), N(r.ClearMs), N(r.MovementMs), N(r.WorkMs), N(r.AttackMs), N(r.CollectMs), N(r.HarvestMs), N(r.SpawnMs), N(r.SnapshotMs), N(r.ViewMs), N(r.SpriteBatchMs), N(r.HudMs), N(r.EffectsMs), r.Focused ? "1" : "0", r.Paused ? "1" : "0", N(r.Width), N(r.Height), N(r.WindowX), N(r.WindowY), N((int)r.Events), N(r.ExperienceDelta), DensityBin(r.Enemies)));
            }
        }
        public void WriteEventsCsv(TextWriter writer)
        {
            writer.WriteLine("frame,timestamp,width,height,eventFlags,source,detail,delta");
            for (var i = 0; i < eventCount; i++) { var e = events[i]; writer.WriteLine(string.Join(",", N(e.Frame), N(e.Timestamp), N(e.Width), N(e.Height), N((int)e.Flags), e.FromFrameSample ? "frame-sample-boundary" : "callback", Csv(e.Detail ?? string.Empty), N(e.Delta))); }
        }
        static string DensityBin(int enemies) => enemies < 400 ? "below-400" : enemies < 600 ? "400-599" : enemies < 800 ? "600-799" : "800-plus";
        static string N<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        public void Dispose()
        {
            if (disposed) return;
            for (var i = 0; i < counters.Length; i++) counters[i].Recorder.Dispose();
            disposed = true;
        }
    }
}
#endif
