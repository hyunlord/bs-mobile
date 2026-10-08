using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed class FrameTelemetry : IDisposable
    {
        public const string Header = "frame,tick,deltaMs,speed,paused,enemies,projectiles,visualProjectiles,people,farms,buildings,width,height,safeX,safeY,safeWidth,safeHeight,thermal,suspended,partial";
        readonly StreamWriter writer;
        readonly DeviceFacts device;
        readonly List<double> late = new List<double>();
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
            duration = durationTicks; lateStart = durationTicks * 3 / 4;
            summary = new FrameSummary { sessionId = sessionId, build = buildIdentity, dataHash = dataHash, durationTicks = duration, lateStartTick = lateStart };
            CsvPath = Path.Combine(directoryPath, "frames.csv"); SummaryPath = Path.Combine(directoryPath, "frame-summary.json");
            File.WriteAllText(Path.Combine(directoryPath, "device.json"), JsonUtility.ToJson(device, true));
            writer = new StreamWriter(CsvPath, false); writer.WriteLine(Header);
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
            writer.WriteLine(string.Join(",", new[] {
                N(summary.frames), N(frame.Tick), N(ms), N(speed), paused ? "1" : "0", N(count.Enemies), N(count.Projectiles), N(activeVisualProjectiles),
                N(count.People), N(count.Farms), N(count.Buildings), N(screen.width), N(screen.height), N(safeArea.x), N(safeArea.y), N(safeArea.width), N(safeArea.height), device.thermal, suspended ? "1" : "0", partial ? "1" : "0" }));
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
                if (!paused && !suspended && !partial && speed == 1) { late.Add(ms); if (frame.Tick == duration) terminalMeasured = true; }
                if (!paused && !suspended && speed != 1) lateInvalid = true;
                if (!paused && partial) lateInvalid = true;
            }
            previousTick = frame.Tick;
            if (summary.frames % 120 == 0 || paused) writer.Flush();
        }

        public void Finish()
        {
            if (finished) return;
            writer.Flush(); writer.Dispose(); finished = true;
            late.Sort(); summary.lateSampleCount = late.Count;
            summary.hasLateSamples = late.Count > 0;
            if (late.Count > 0) { summary.frameP95Ms = late[(int)Math.Ceiling(late.Count * .95) - 1]; summary.frameMaxMs = late[late.Count - 1]; }
            summary.lateComplete = summary.firstTick >= 0 && summary.firstTick <= lateStart && summary.lastLateTick == duration && terminalMeasured && !lateInvalid && late.Count > 0;
            File.WriteAllText(SummaryPath, JsonUtility.ToJson(summary, true));
        }
        public void Dispose() => Finish();
        static string N(IFormattable value) => value.ToString(null, CultureInfo.InvariantCulture);
        [Serializable]
        sealed class FrameSummary
        {
            public string sessionId, build, dataHash;
            public int durationTicks, lateStartTick, frames, pausedFrames, suspendedFrames, partialFrames, lateSampleCount;
            public int firstTick = -1, lastTick = -1, firstLateTick = -1, lastLateTick = -1;
            public bool hasLateSamples, lateComplete;
            public double frameP95Ms, frameMaxMs;
            public int maxEnemies, maxProjectiles, maxVisualProjectiles, maxPeople, maxFarms, maxBuildings;
        }
    }
}
