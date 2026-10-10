using System;
using System.Collections;
using System.IO;
using System.Threading;
using Game.App.Generated;
using SowSiege.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.App
{
    public sealed class GoldenMinuteCapture : MonoBehaviour
    {
        const int RingSize = 12;
        readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
        readonly double[] intervals = new double[12000];
        readonly GoldenCaptureFrame[] frames = new GoldenCaptureFrame[RingSize];
        readonly GpuSlot[] gpu = new GpuSlot[RingSize];
        RunCoordinator run;
        string folder, failure;
        GoldenCaptureWriter writer;
        GoldenMinuteAudio audio;
        Action<bool, string> complete;
        double firstTime, lastTime, firstDsp, nextDeadline;
        int submitted, pending, completedReadbacks, gpuErrors, ringOverflows, missedSlots, intervalCount, width, height, finalTick, maximumQueueDepth, initialGcCount;
        long submissionAllocations, callbackAllocations;
        bool stopping, finalized, battleSelected, writerCompleting;
        int oldFrameRate;

        sealed class GpuSlot
        {
            public RenderTexture Texture;
            public NativeArray<byte> Native;
            public Action<AsyncGPUReadbackRequest> Callback;
        }

        public void Initialize(RunCoordinator coordinator, string output, Action<bool, string> completion)
        {
            run = coordinator; folder = output; complete = completion;
            if (!SystemInfo.supportsAsyncGPUReadback) throw new InvalidOperationException("This native backend does not support asynchronous GPU capture.");
            width = Screen.width; height = Screen.height;
            for (var index = 0; index < RingSize; index++)
            {
                var slotIndex = index;
                frames[index] = new GoldenCaptureFrame(checked(width * height * 4));
                gpu[index] = new GpuSlot {
                    Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB),
                    Native = new NativeArray<byte>(width * height * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                    Callback = request => Readback(slotIndex, request)
                };
                gpu[index].Texture.Create();
            }
            writer = new GoldenCaptureWriter(folder, width, height, frames);
            var listener = FindFirstObjectByType<AudioListener>();
            if (listener != null) audio = GoldenMinuteAudio.BeginMixedOutput(listener, Path.Combine(folder, "audio.wav"));
            oldFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
            StartCoroutine(Record());
        }

        IEnumerator Record()
        {
            while (!finalized)
            {
                yield return endOfFrame;
                if (!stopping && run != null) run.NotifySmoothnessEndOfFrame();
                if (!stopping && run.Frame != null)
                {
                    try { SubmitFrame(); }
                    catch (Exception exception) { failure = exception.ToString(); StopCapture(); }
                }
                if (writer?.Error != null) { failure = writer.Error; StopCapture(); }
                if (audio?.Error != null) { failure = audio.Error; StopCapture(); }
                if (stopping) TryFinalize();
            }
        }

        void SubmitFrame()
        {
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                if (Time.timeScale != 1 || run.Speed != 1) throw new InvalidOperationException("Golden minute requires normal speed.");
                if (Screen.width != width || Screen.height != height) throw new InvalidOperationException("Capture dimensions changed during the run.");
                if (run.Frame.Status == RunStatus.Completed && run.Frame.Tick < run.Frame.TickRate * 60)
                    throw new InvalidOperationException("Run ended before sixty gameplay seconds.");
                var now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
                // On a 60 Hz presentation stream, take every real frame despite submillisecond jitter.
                // On faster displays use accumulated 60 Hz deadlines, not now + 1/60 drift.
                if (submitted > 0 && now < nextDeadline - .002) return;
                var slot = frames[submitted % RingSize];
                var buffer = gpu[submitted % RingSize];
                if (Volatile.Read(ref slot.State) != 0)
                {
                    ringOverflows++; throw new InvalidOperationException("Capture ring overflow; no frame duplication or silent drop permitted.");
                }
                if (submitted == 0) { initialGcCount = GC.CollectionCount(0); firstTime = now; firstDsp = AudioSettings.dspTime; nextDeadline = now; audio?.MarkVideoStart(now, firstDsp); }
                else
                {
                    var interval = now - lastTime;
                    if (intervalCount >= intervals.Length) throw new InvalidOperationException("Capture interval evidence capacity exceeded.");
                    intervals[intervalCount++] = interval;
                }
                while (nextDeadline <= now + .002) nextDeadline += 1d / 60;
                slot.Sequence = submitted; slot.Tick = run.Frame.Tick; slot.RenderFrame = Time.renderedFrameCount;
                slot.CardPause = run.Frame.Status == RunStatus.AwaitingCard ? 1 : 0;
                slot.WallSeconds = now - firstTime; slot.DspSeconds = AudioSettings.dspTime - firstDsp;
                slot.Battle = !battleSelected && run.Frame.Tick >= run.Frame.TickRate * 55 && run.Frame.Status == RunStatus.Running;
                if (slot.Battle) battleSelected = true;
                Volatile.Write(ref slot.State, 1);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(buffer.Texture);
                pending++; submitted++; lastTime = now; finalTick = run.Frame.Tick;
                missedSlots = Math.Max(missedSlots, CumulativeMissingSlots(now - firstTime, submitted));
                maximumQueueDepth = Math.Max(maximumQueueDepth, submitted - writer.Written);
                try { AsyncGPUReadback.RequestIntoNativeArray(ref buffer.Native, buffer.Texture, 0, TextureFormat.RGBA32, buffer.Callback); }
                catch { pending--; gpuErrors++; Volatile.Write(ref slot.State, 0); throw; }
                if (run.Frame.Tick >= run.Frame.TickRate * 60) StopCapture();
            }
            finally { submissionAllocations += GC.GetAllocatedBytesForCurrentThread() - allocationStart; }
        }

        public static int CumulativeMissingSlots(double elapsedSeconds, int capturedFrames)
        {
            if (elapsedSeconds < 0 || double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (capturedFrames < 1) throw new ArgumentOutOfRangeException(nameof(capturedFrames));
            // One shared 2 ms clock-jitter grace plus nearest-slot rounding, never per-interval grace.
            // A later burst cannot erase the peak deficit retained by the caller.
            var expectedIntervals = (int)Math.Floor(Math.Max(0, elapsedSeconds - .002) * 60 + .5);
            return Math.Max(0, expectedIntervals - (capturedFrames - 1));
        }

        void Readback(int index, AsyncGPUReadbackRequest request)
        {
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                if (request.hasError)
                {
                    gpuErrors++; failure = "GPU readback failed; incomplete capture preserved.";
                    // Preserve ordering with the failed slot explicitly marked; never encode its pixels.
                    Volatile.Write(ref frames[index].State, 0);
                    StopCapture(); return;
                }
                gpu[index].Native.CopyTo(frames[index].Pixels);
                completedReadbacks++;
                Volatile.Write(ref frames[index].State, 2);
                writer.Notify();
            }
            finally { pending--; callbackAllocations += GC.GetAllocatedBytesForCurrentThread() - allocationStart; }
        }

        public void StopCapture()
        {
            if (stopping) return;
            stopping = true;
            audio?.StopAudio();
        }

        void TryFinalize()
        {
            if (pending != 0) return;
            if (!writerCompleting) { writerCompleting = true; writer?.Complete(); }
            if (writer != null && !writer.Completed || audio != null && !audio.Completed) return;
            if (writer?.Error != null) failure = writer.Error;
            if (audio == null || audio.NonzeroSampleCount == 0) failure = failure ?? "Actual mixed audio is missing or silent.";
            if (audio != null && audio.DroppedBlocks > 0) failure = failure ?? "Audio capture dropped blocks.";
            if (writer != null && writer.Written != submitted) failure = failure ?? "Submitted and encoded frame counts differ.";
            if (missedSlots > 0) failure = failure ?? "Actual live capture missed 60 Hz slots; do not label this sample a 60 fps pass.";
            finalized = true;
            ReleaseBuffers();
            Application.targetFrameRate = oldFrameRate;
            run.WriteSmoothnessDiagnostics(folder);
            Array.Sort(intervals, 0, intervalCount);
            File.WriteAllText(Path.Combine(folder, "golden-minute.json"), JsonUtility.ToJson(new Evidence {
                commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash, sourceDirty = BuildIdentity.SourceDirty,
                profile = CanonicalContent.ProfileName, dataHash = CanonicalContent.DataHash,
                frames = writer?.Written ?? 0, submittedFrames = submitted, completedReadbacks = completedReadbacks, maximumQueueDepth = maximumQueueDepth,
                globalGcCollections = GC.CollectionCount(0) - initialGcCount, gpuErrors = gpuErrors, ringOverflows = ringOverflows,
                missed60HzSlots = missedSlots, finalTick = finalTick, tickRate = run?.Frame?.TickRate ?? 0,
                wallSeconds = submitted == 0 ? 0 : lastTime - firstTime, width = width, height = height,
                measuredFps = submitted > 1 ? (submitted - 1) / (lastTime - firstTime) : 0,
                frameP95Ms = intervalCount == 0 ? 0 : intervals[Math.Min(intervalCount - 1, (int)Math.Ceiling(intervalCount * .95) - 1)] * 1000,
                frameMaxMs = intervalCount == 0 ? 0 : intervals[intervalCount - 1] * 1000,
                submissionAllocatedBytes = submissionAllocations, callbackAllocatedBytes = callbackAllocations,
                imageWriterAllocatedBytes = writer?.AllocatedBytes ?? 0, audioWriterAllocatedBytes = audio?.WriterAllocatedBytes ?? 0,
                audio = audio != null && audio.NonzeroSampleCount > 0, audioDroppedBlocks = audio?.DroppedBlocks ?? 0,
                audioStartOffsetSeconds = audio?.VideoOffsetSeconds ?? 0, battleScreenshot = writer != null && writer.BattleWritten,
                automatedNormalInput = true, failure = failure, requestedFps = 60
            }, true));
            complete(failure == null, failure ?? "Live native frames captured asynchronously with actual timestamps; no synthesized frames or visual acceptance claim.");
        }

        void ReleaseBuffers()
        {
            foreach (var slot in gpu)
            {
                if (slot == null) continue;
                if (slot.Native.IsCreated) slot.Native.Dispose();
                if (slot.Texture != null) { slot.Texture.Release(); Destroy(slot.Texture); }
            }
        }

        void OnApplicationQuit()
        {
            if (finalized) return;
            StopCapture();
            // Only forced application shutdown waits; normal recording drains without blocking gameplay.
            AsyncGPUReadback.WaitAllRequests();
            writer?.Complete(); writer?.Join(5000); audio?.Join(5000);
            ReleaseBuffers();
        }

        [Serializable] sealed class Evidence
        {
            public string commit, sourceHash, profile, dataHash, failure;
            public bool sourceDirty, audio, battleScreenshot, automatedNormalInput;
            public int frames, submittedFrames, finalTick, tickRate, width, height, requestedFps, gpuErrors, ringOverflows, missed60HzSlots, audioDroppedBlocks, completedReadbacks, maximumQueueDepth, globalGcCollections;
            public long submissionAllocatedBytes, callbackAllocatedBytes, imageWriterAllocatedBytes, audioWriterAllocatedBytes;
            public double wallSeconds, measuredFps, frameP95Ms, frameMaxMs, audioStartOffsetSeconds;
        }
    }
}
