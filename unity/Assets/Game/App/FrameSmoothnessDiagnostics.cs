using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Game.App
{
    public enum SmoothnessScope { CoreApply, CoreSnapshot, AcceptPresentation, Hud, World, Telemetry, Recording, Count }

    public sealed class FrameSmoothnessDiagnostics
    {
        const int FrameCapacity = 18000, EntityCapacity = 8192, JumpCapacity = 65536;
        FrameRow[] rows;
        JumpRow[] jumps;
        EntitySample[] samples;
        Dictionary<(string, int), Track> tracks;
        (string, int)[] removals;
        long[] scopeBytes, scopeTicks;
        int frameCount, sampleCount, jumpCount, droppedFrames, droppedSamples, droppedJumps;
        long updateStart, previousStart, sampleTime, allocatedStart;
        public bool Enabled { get; private set; }
        public bool AllocationCounterAvailable { get; private set; }
        public int FrameCount => frameCount;
        static long Now => System.Diagnostics.Stopwatch.GetTimestamp();
        static double Ms(long ticks) => ticks * 1000d / System.Diagnostics.Stopwatch.Frequency;
        struct FrameRow
        {
            public int Tick, UnityFrame, Spawns, Despawns, Entities, WorstId;
            public string WorstKind;
            public bool Paused, Reset;
            public float InputX, InputY, Speed, WorldJump, ScreenJump, ResidualPixels, CameraPixels;
            public double Interval, SampleSubmit, SampleEndOfFrame, UpdateMs, UnityUnscaledDeltaMs;
            public long Allocated;
        }
        struct EntitySample { public string Kind; public int Id; public Vector2 World; }
        struct Track { public Vector2 World, Screen; public int Frame; public float Speed; }
        struct JumpRow
        {
            public int Frame, Id; public string Kind;
            public float World, Screen, Camera, Residual, ExpectedPixels;
            public bool Paused, Reset;
        }
        public readonly struct AllocationScope : IDisposable
        {
            readonly FrameSmoothnessDiagnostics owner;
            readonly int index;
            readonly long start, startedAt;
            public AllocationScope(FrameSmoothnessDiagnostics owner, SmoothnessScope scope)
            { this.owner = owner; index = owner.Enabled && owner.frameCount < FrameCapacity ? owner.frameCount * (int)SmoothnessScope.Count + (int)scope : -1; start = index >= 0 && owner.AllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0; startedAt = index >= 0 ? Now : 0; }
            public void Dispose()
            { if(owner.Enabled && index >= 0 && index < owner.scopeBytes.Length) { owner.scopeTicks[index] += Now - startedAt; if(owner.AllocationCounterAvailable) owner.scopeBytes[index] += GC.GetAllocatedBytesForCurrentThread() - start; } }
        }
        public AllocationScope Scope(SmoothnessScope scope) => new AllocationScope(this, scope);
        public void Begin()
        {
            AllocationCounterAvailable = AllocationCounterProbe.CurrentThreadAvailable();
            rows ??= new FrameRow[FrameCapacity]; jumps ??= new JumpRow[JumpCapacity]; samples ??= new EntitySample[EntityCapacity];
            tracks ??= new Dictionary<(string, int), Track>(EntityCapacity); removals ??= new (string, int)[EntityCapacity];
            scopeBytes ??= new long[FrameCapacity * (int)SmoothnessScope.Count];
            scopeTicks ??= new long[FrameCapacity * (int)SmoothnessScope.Count];
            Array.Clear(rows, 0, rows.Length); Array.Clear(scopeBytes, 0, scopeBytes.Length); Array.Clear(scopeTicks, 0, scopeTicks.Length);
            frameCount = sampleCount = jumpCount = droppedFrames = droppedSamples = droppedJumps = 0;
            tracks.Clear(); previousStart = sampleTime = 0; Enabled = true;
        }
        public void BeginFrame(int tick, bool paused)
        {
            if(!Enabled) return;
            updateStart = Now; allocatedStart = AllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0; sampleCount = 0;
            if(!Enabled || frameCount >= FrameCapacity) return;
            rows[frameCount] = new FrameRow { Tick = tick, UnityFrame = Time.frameCount, Paused = paused,
                UnityUnscaledDeltaMs = Time.unscaledDeltaTime * 1000d, Interval = previousStart == 0 ? 0 : Ms(updateStart - previousStart), SampleSubmit = -1, SampleEndOfFrame = -1 };
            previousStart = updateStart;
        }
        public void InputSample(Vector2 input)
        {
            if(!Enabled || frameCount >= FrameCapacity) return;
            sampleTime = Now; rows[frameCount].InputX = input.x; rows[frameCount].InputY = input.y;
        }
        public void Entity(string kind, int id, Vector2 world)
        {
            if(!Enabled) return;
            if(sampleCount == EntityCapacity) { droppedSamples++; return; }
            samples[sampleCount++] = new EntitySample { Kind = kind, Id = id, World = world };
        }
        public void Submit(Camera camera, int tick, bool paused, bool reset, float inputSpeed)
        {
            if(!Enabled || frameCount >= FrameCapacity || camera == null) return;
            ref var row = ref rows[frameCount]; row.Tick = tick; row.Paused = paused; row.Reset = reset; row.Speed = inputSpeed;
            row.SampleSubmit = sampleTime == 0 ? -1 : Ms(Now - sampleTime);
            row.Entities = sampleCount;
            var intervalSeconds = Math.Max(.000001, row.Interval / 1000d);
            for(var i = 0; i < sampleCount; i++)
            {
                var sample = samples[i]; var key = (sample.Kind, sample.Id);
                var screen = (Vector2)camera.WorldToScreenPoint(sample.World);
                var worldSpeed = 0f;
                if(tracks.TryGetValue(key, out var prior) && prior.Frame == frameCount - 1)
                {
                    var worldJump = Vector2.Distance(prior.World, sample.World);
                    var screenJump = Vector2.Distance(prior.Screen, screen);
                    var oldUnderCurrentCamera = (Vector2)camera.WorldToScreenPoint(prior.World);
                    var cameraJump = Vector2.Distance(prior.Screen, oldUnderCurrentCamera);
                    var residual = Vector2.Distance(oldUnderCurrentCamera, screen);
                    worldSpeed = worldJump / (float)intervalSeconds;
                    var expectedSpeed = sample.Kind == "lord" ? Mathf.Max(inputSpeed, prior.Speed) : prior.Speed;
                    var worldBudget = expectedSpeed * (float)intervalSeconds;
                    var pixelsPerUnit = camera.pixelHeight / (2 * camera.orthographicSize);
                    var expectedPixels = worldBudget * pixelsPerUnit;
                    if(residual > row.ResidualPixels)
                    { row.ResidualPixels = residual; row.WorstKind = sample.Kind; row.WorstId = sample.Id; }
                    row.WorldJump = Mathf.Max(row.WorldJump, worldJump); row.ScreenJump = Mathf.Max(row.ScreenJump, screenJump); row.CameraPixels = Mathf.Max(row.CameraPixels, cameraJump);
                    if(residual > expectedPixels + 6)
                    {
                        if(jumpCount < JumpCapacity) jumps[jumpCount++] = new JumpRow { Frame = frameCount, Id = sample.Id, Kind = sample.Kind, World = worldJump, Screen = screenJump, Camera = cameraJump, Residual = residual, ExpectedPixels = expectedPixels, Paused = paused, Reset = reset };
                        else droppedJumps++;
                    }
                }
                else row.Spawns++;
                if(tracks.Count < EntityCapacity || tracks.ContainsKey(key)) tracks[key] = new Track { World = sample.World, Screen = screen, Frame = frameCount, Speed = worldSpeed };
                else droppedSamples++;
            }
            var count = 0;
            foreach(var pair in tracks) if(pair.Value.Frame != frameCount) { if(count < removals.Length) removals[count++] = pair.Key; }
            for(var i = 0; i < count; i++) tracks.Remove(removals[i]);
            row.Despawns = count;
        }
        public void EndFrame()
        {
            if(!Enabled) return;
            if(frameCount >= FrameCapacity) { droppedFrames++; return; }
            rows[frameCount].Allocated = AllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocatedStart : -1;
            rows[frameCount].UpdateMs = Ms(Now - updateStart); frameCount++;
        }
        public void EndOfFrame()
        {
            if(!Enabled || frameCount == 0 || sampleTime == 0 || rows[frameCount - 1].UnityFrame != Time.frameCount) return;
            rows[frameCount - 1].SampleEndOfFrame = Ms(Now - sampleTime);
        }
        public void Write(string folder)
        {
            if(rows == null) return;
            Enabled = false;
            Directory.CreateDirectory(folder);
            using(var file = new StreamWriter(Path.Combine(folder, "smoothness-frames.csv")))
            {
                file.WriteLine("frame,unityFrame,tick,intervalMs,softwareSampleToSubmitMs,softwareSampleToEndOfFrameMs,updateMs,totalUpdateAllocatedBytes,coreApplyBytes,coreSnapshotBytes,acceptPresentationBytes,hudBytes,worldBytes,telemetryBytes,recordingBytes,unattributedBytes,inputX,inputY,predictionSpeed,paused,reset,entities,spawns,despawns,maxWorldJump,maxScreenJumpPixels,maxCameraJumpPixels,maxCameraCompensatedJumpPixels,worstKind,worstId,coreApplyMs,coreSnapshotMs,acceptPresentationMs,hudMs,worldMs,telemetryMs,recordingMs,unattributedUpdateMs,unityUnscaledDeltaMs");
                for(var i = 0; i < frameCount; i++)
                {
                    var r = rows[i]; long accounted = 0;
                    file.Write(FormattableString.Invariant($"{i},{r.UnityFrame},{r.Tick},{r.Interval:F6},{r.SampleSubmit:F6},{r.SampleEndOfFrame:F6},{r.UpdateMs:F6},{r.Allocated}"));
                    for(var scope = 0; scope < (int)SmoothnessScope.Count; scope++) { var value = AllocationCounterAvailable ? scopeBytes[i * (int)SmoothnessScope.Count + scope] : -1; accounted += value; file.Write("," + value.ToString(CultureInfo.InvariantCulture)); }
                    var unattributed = AllocationCounterAvailable ? r.Allocated - accounted : -1;
                    file.Write(FormattableString.Invariant($",{unattributed},{r.InputX:F6},{r.InputY:F6},{r.Speed:F6},{r.Paused},{r.Reset},{r.Entities},{r.Spawns},{r.Despawns},{r.WorldJump:F6},{r.ScreenJump:F6},{r.CameraPixels:F6},{r.ResidualPixels:F6},{r.WorstKind},{r.WorstId}"));
                    double accountedMs = 0;
                    for(var scope = 0; scope < (int)SmoothnessScope.Count; scope++)
                    {
                        var value = Ms(scopeTicks[i * (int)SmoothnessScope.Count + scope]); accountedMs += value;
                        file.Write("," + value.ToString("F6", CultureInfo.InvariantCulture));
                    }
                    file.WriteLine(FormattableString.Invariant($",{r.UpdateMs - accountedMs:F6},{r.UnityUnscaledDeltaMs:F6}"));
                }
            }
            using(var file = new StreamWriter(Path.Combine(folder, "smoothness-jumps.csv")))
            {
                file.WriteLine("frame,kind,id,worldJump,screenJumpPixels,cameraJumpPixels,cameraCompensatedPixels,expectedMovementPixels,paused,reset");
                for(var i = 0; i < jumpCount; i++) { var r = jumps[i]; file.WriteLine(FormattableString.Invariant($"{r.Frame},{r.Kind},{r.Id},{r.World:F6},{r.Screen:F6},{r.Camera:F6},{r.Residual:F6},{r.ExpectedPixels:F6},{r.Paused},{r.Reset}")); }
            }
            File.WriteAllText(Path.Combine(folder, "smoothness-boundary.txt"),
                $"Software input sample to CPU render submission / EndOfFrame only; not physical input-to-photon.\nAllocation counter: {(AllocationCounterAvailable ? "available: known allocation calibration passed" : "unavailable: known allocation calibration failed; every byte column is -1, never zero-allocation evidence")}.\nStopwatch scope durations are disjoint CPU elapsed times, independent of allocation counter availability; unattributedUpdateMs includes input, diagnostics and unscoped work. Recording measures accepted-command serialization/write only. Scope byte counters are disjoint when available; CoreSnapshot includes immutable snapshot allocations. No frame-global profiler value is attributed to individual scopes.\nRaw jumps retained even across pause/reset; anomalies compare camera-compensated movement to observed velocity plus 6 pixels.\nDropped frame rows: {droppedFrames}; dropped entity samples: {droppedSamples}; dropped jump rows: {droppedJumps}.\n");
        }
    }
}
