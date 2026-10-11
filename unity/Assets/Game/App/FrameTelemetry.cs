using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed class FrameTelemetry : IDisposable
    {
        public const string Header = "frame,tick,deltaMs,speed,paused,enemies,projectiles,visualProjectiles,people,farms,buildings,width,height,safeX,safeY,safeWidth,safeHeight,thermal,suspended,partial";
        readonly StreamWriter writer;
        readonly DeviceFacts device;
        readonly List<double> late;
        const int QueueCapacity = 4096;
        readonly Sample[] queued = new Sample[QueueCapacity];
        readonly object queueLock = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread writerThread;
        int readIndex, writeIndex, queuedCount;
        bool closing;
        Exception writeError;
        struct Sample
        {
            public int frame, tick, speed, enemies, projectiles, visuals, people, farms, buildings;
            public double ms;
            public Rect screen, safe;
            public string thermal;
            public bool paused, suspended, partial;
        }
        readonly FrameSummary summary;
        readonly int lateStart;
        readonly int duration;
        int previousTick = -1;
        bool finished;
        bool lateInvalid;
        bool terminalMeasured;
        RunFrame pendingFrame;
        int pendingSpeed, pendingVisuals;
        Rect pendingScreen, pendingSafeArea;
        bool pendingPaused;
        public string CsvPath { get; }
        public string SummaryPath { get; }

        public FrameTelemetry(string directoryPath, string sessionId, string buildIdentity, string dataHash, int durationTicks, DeviceFacts facts)
        {
            if (durationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(durationTicks));
            Directory.CreateDirectory(directoryPath);
            device = facts ?? throw new ArgumentNullException(nameof(facts));
            late = new List<double>(Math.Max(4096, checked(durationTicks * 2)));
            duration = durationTicks; lateStart = durationTicks * 3 / 4;
            summary = new FrameSummary { sessionId = sessionId, build = buildIdentity, dataHash = dataHash, durationTicks = duration, lateStartTick = lateStart };
            CsvPath = Path.Combine(directoryPath, "frames.csv"); SummaryPath = Path.Combine(directoryPath, "frame-summary.json");
            File.WriteAllText(Path.Combine(directoryPath, "device.json"), JsonUtility.ToJson(device, true));
            writer = new StreamWriter(CsvPath, false); writer.WriteLine(Header);
            writerThread = new Thread(WriteLoop) { IsBackground = true, Name = "SowSiege frame telemetry" };
            writerThread.Start();
        }

        public void BeginInterval(RunFrame frame, int speed, int activeVisualProjectiles, Rect screen, Rect safeArea, bool paused)
        {
            if (finished || pendingFrame != null) throw new InvalidOperationException("Previous interval must be completed before another begins.");
            pendingFrame = frame ?? throw new ArgumentNullException(nameof(frame));
            pendingSpeed = speed; pendingVisuals = activeVisualProjectiles; pendingScreen = screen; pendingSafeArea = safeArea; pendingPaused = paused;
        }

        public void CompleteInterval(float unscaledDeltaSeconds, bool suspended = false, bool partial = false)
        {
            if (pendingFrame == null) return;
            Capture(unscaledDeltaSeconds, pendingFrame, pendingSpeed, pendingVisuals, pendingScreen, pendingSafeArea, pendingPaused, suspended, partial);
            pendingFrame = null;
        }

        public void Capture(float unscaledDeltaSeconds, RunFrame frame, int speed, int activeVisualProjectiles, Rect screen, Rect safeArea, bool paused, bool suspended = false, bool partial = false)
        {
            if (finished) throw new InvalidOperationException("Telemetry is closed.");
            if (float.IsNaN(unscaledDeltaSeconds) || float.IsInfinity(unscaledDeltaSeconds) || unscaledDeltaSeconds < 0 || (speed != 1 && speed != 2 && speed != 4) || frame.Tick < previousTick || activeVisualProjectiles < 0)
                throw new ArgumentException("Invalid frame sample.");
            device.RefreshThermal();
            var ms = (double)unscaledDeltaSeconds * 1000;
            var count = frame.Counts;
            Enqueue(new Sample { frame = summary.frames, tick = frame.Tick, ms = ms, speed = speed, paused = paused,
                enemies = count.Enemies, projectiles = count.Projectiles, visuals = activeVisualProjectiles, people = count.People,
                farms = count.Farms, buildings = count.Buildings, screen = screen, safe = safeArea, thermal = device.thermal,
                suspended = suspended, partial = partial });
            if (summary.frames == 0) summary.firstTick = frame.Tick;
            summary.lastTick = frame.Tick;
            summary.frames++;
            if (paused) summary.pausedFrames++;
            if (suspended) summary.suspendedFrames++;
            if (partial) summary.partialFrames++;
            summary.maxEnemies = Math.Max(summary.maxEnemies, count.Enemies); summary.maxProjectiles = Math.Max(summary.maxProjectiles, count.Projectiles);
            summary.maxVisualProjectiles = Math.Max(summary.maxVisualProjectiles, activeVisualProjectiles); summary.maxPeople = Math.Max(summary.maxPeople, count.People);
            summary.maxFarms = Math.Max(summary.maxFarms, count.Farms); summary.maxBuildings = Math.Max(summary.maxBuildings, count.Buildings);
            if (frame.Tick >= lateStart && frame.Tick <= duration)
            {
                if (summary.firstLateTick < 0) summary.firstLateTick = frame.Tick;
                summary.lastLateTick = frame.Tick;
                if (!paused && !suspended && !partial && speed == 1) { if (late.Count < late.Capacity) late.Add(ms); else { summary.sampleOverflow++; lateInvalid = true; } if (frame.Tick == duration) terminalMeasured = true; }
                if (!paused && !suspended && speed != 1) lateInvalid = true;
                if (!paused && partial) lateInvalid = true;
            }
            previousTick = frame.Tick;

        }

        public void Finish()
        {
            if (finished) return;
            finished = true;
            lock (queueLock) closing = true;
            wake.Set(); writerThread.Join(); wake.Dispose();
            if (writeError != null) throw new IOException("Frame telemetry writer failed.", writeError);
            late.Sort(); summary.lateSampleCount = late.Count;
            summary.hasLateSamples = late.Count > 0;
            if (late.Count > 0) { summary.frameP95Ms = late[(int)Math.Ceiling(late.Count * .95) - 1]; summary.frameMaxMs = late[late.Count - 1]; }
            summary.lateComplete = summary.sampleOverflow == 0 && summary.firstTick >= 0 && summary.firstTick <= lateStart && summary.lastLateTick == duration && terminalMeasured && !lateInvalid && late.Count > 0;
            File.WriteAllText(SummaryPath, JsonUtility.ToJson(summary, true));
        }
        public void Dispose() => Finish();
        void Enqueue(Sample sample)
        {
            lock (queueLock)
            {
                if (writeError != null) throw new IOException("Frame telemetry writer failed.", writeError);
                if (queuedCount == queued.Length) { summary.sampleOverflow++; lateInvalid = true; return; }
                queued[writeIndex] = sample; writeIndex = (writeIndex + 1) % queued.Length; queuedCount++;
            }
            wake.Set();
        }
        void WriteLoop()
        {
            try
            {
                while (true)
                {
                    Sample sample; bool available;
                    lock (queueLock)
                    {
                        if (queuedCount == 0) { if (closing) break; sample = default; available = false; }
                        else { sample = queued[readIndex]; readIndex = (readIndex + 1) % queued.Length; queuedCount--; available = true; }
                    }
                    if (!available) { wake.WaitOne(); continue; }
                    WriteSample(sample);
                    if (sample.frame % 120 == 119 || sample.paused) writer.Flush();
                }
                writer.Flush();
            }
            catch (Exception error) { lock (queueLock) writeError = error; }
            finally { writer.Dispose(); }
        }
        void WriteSample(Sample sample)
        {
            Span<char> buffer = stackalloc char[48];
            WriteNumber(sample.frame, buffer); WriteNumber(sample.tick, buffer); WriteNumber(sample.ms, buffer);
            WriteNumber(sample.speed, buffer); WriteNumber(sample.paused ? 1 : 0, buffer);
            WriteNumber(sample.enemies, buffer); WriteNumber(sample.projectiles, buffer); WriteNumber(sample.visuals, buffer);
            WriteNumber(sample.people, buffer); WriteNumber(sample.farms, buffer); WriteNumber(sample.buildings, buffer);
            WriteNumber(sample.screen.width, buffer); WriteNumber(sample.screen.height, buffer);
            WriteNumber(sample.safe.x, buffer); WriteNumber(sample.safe.y, buffer); WriteNumber(sample.safe.width, buffer); WriteNumber(sample.safe.height, buffer);
            writer.Write(sample.thermal); writer.Write(','); WriteNumber(sample.suspended ? 1 : 0, buffer);
            writer.WriteLine(sample.partial ? "1" : "0");
        }
        void WriteNumber(double value, Span<char> buffer)
        {
            if (!value.TryFormat(buffer, out var count, provider: CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Telemetry number exceeds the formatting buffer.");
            writer.Write(buffer.Slice(0, count)); writer.Write(',');
        }

        [Serializable]
        sealed class FrameSummary
        {
            public string sessionId, build, dataHash;
            public int sampleOverflow;
            public int durationTicks, lateStartTick, frames, pausedFrames, suspendedFrames, partialFrames, lateSampleCount;
            public int firstTick = -1, lastTick = -1, firstLateTick = -1, lastLateTick = -1;
            public bool hasLateSamples, lateComplete;
            public double frameP95Ms, frameMaxMs;
            public int maxEnemies, maxProjectiles, maxVisualProjectiles, maxPeople, maxFarms, maxBuildings;
        }
    }
}
