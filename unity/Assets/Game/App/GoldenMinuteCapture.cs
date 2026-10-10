using System;
using System.Collections;
using System.Globalization;
using System.IO;
using Game.App.Generated;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // Opt-in presentation evidence. Wall-clock timestamps retain card pauses and capture stalls.
    public sealed class GoldenMinuteCapture : MonoBehaviour
    {
        RunCoordinator run;
        string folder;
        StreamWriter frames, timestamps;
        GoldenMinuteAudio audio;
        Action<bool, string> complete;
        double firstTime, previousTime, nextTime;
        string previousFile;
        int frameCount;
        bool stopped, battleCaptured;

        public void Initialize(RunCoordinator coordinator, string output, Action<bool, string> completion)
        {
            run = coordinator; folder = output; complete = completion;
            Directory.CreateDirectory(Path.Combine(folder, "frames"));
            frames = new StreamWriter(Path.Combine(folder, "frames.ffconcat"));
            frames.WriteLine("ffconcat version 1.0");
            timestamps = new StreamWriter(Path.Combine(folder, "frames.csv"));
            timestamps.WriteLine("frame,wall_seconds,tick,card_pause");
            StartCoroutine(Record());
        }

        IEnumerator Record()
        {
            while (!stopped)
            {
                yield return new WaitForEndOfFrame();
                if (stopped || run.Frame == null) continue;
                var now = Time.realtimeSinceStartupAsDouble;
                if (frameCount > 0 && now < nextTime) continue;
                Texture2D texture = null;
                string error = null;
                bool done = false;
                try
                {
                    if (Time.timeScale != 1 || run.Speed != 1) throw new InvalidOperationException("Golden minute requires normal speed.");
                    if (run.Frame.Status == RunStatus.Completed && run.Frame.Tick < run.Frame.TickRate * 60)
                        throw new InvalidOperationException("Run ended before sixty gameplay seconds; preserve this failed sample.");
                    if (frameCount == 0)
                    {
                        firstTime = now;
                        var listener = FindFirstObjectByType<AudioListener>();
                        if (listener != null)
                        {
                            audio = GoldenMinuteAudio.BeginMixedOutput(listener, Path.Combine(folder, "audio.wav"));
                        }
                    }
                    texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (texture == null) throw new IOException("Native framebuffer capture returned no texture.");
                    var name = "frames/frame-" + frameCount.ToString("D6", CultureInfo.InvariantCulture) + ".jpg";
                    File.WriteAllBytes(Path.Combine(folder, name), texture.EncodeToJPG(92));
                    if (previousFile != null) AppendPrevious(now - previousTime);
                    previousFile = name; previousTime = now; frameCount++;
                    timestamps.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F6},{2},{3}", frameCount - 1, now - firstTime, run.Frame.Tick, run.Frame.Status == RunStatus.AwaitingCard ? 1 : 0));
                    if (!battleCaptured && run.Frame.Tick >= run.Frame.TickRate * 55 && run.Frame.Status == RunStatus.Running)
                    {
                        File.WriteAllBytes(Path.Combine(folder, "05-native-golden-battle.png"), texture.EncodeToPNG());
                        battleCaptured = true;
                    }
                    nextTime = now + 1d / 30;
                    done = run.Frame.Tick >= run.Frame.TickRate * 60;
                }
                catch (Exception exception) { error = exception.ToString(); }
                finally { if (texture != null) Destroy(texture); }
                if (error != null || done)
                {
                    StopCapture();
                    if (error == null && (audio == null || audio.NonzeroSampleCount == 0)) error = "Actual mixed audio was silent or missing; capture cannot claim audible evidence.";
                    complete(error == null, error ?? "Sixty normal gameplay seconds captured live; card pauses preserved; no full-run or visual acceptance claim.");
                }
            }
        }

        void AppendPrevious(double duration)
        {
            frames.WriteLine("file '" + previousFile + "'");
            frames.WriteLine("duration " + duration.ToString("F6", CultureInfo.InvariantCulture));
        }

        public void StopCapture()
        {
            if (stopped) return;
            stopped = true;
            audio?.StopAudio();
            if (previousFile != null)
            {
                AppendPrevious(1d / 30);
                frames.WriteLine("file '" + previousFile + "'");
            }
            frames?.Dispose(); timestamps?.Dispose();
            File.WriteAllText(Path.Combine(folder, "golden-minute.json"), JsonUtility.ToJson(new Evidence {
                commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash, sourceDirty = BuildIdentity.SourceDirty,
                profile = CanonicalContent.ProfileName, dataHash = CanonicalContent.DataHash,
                frames = frameCount, finalTick = run?.Frame?.Tick ?? 0, tickRate = run?.Frame?.TickRate ?? 0,
                wallSeconds = frameCount == 0 ? 0 : previousTime - firstTime,
                width = Screen.width, height = Screen.height, audio = audio != null && audio.NonzeroSampleCount > 0,
                battleScreenshot = battleCaptured, automatedNormalInput = true
            }, true));
        }

        void OnApplicationQuit() => StopCapture();
        [Serializable] sealed class Evidence
        {
            public string commit, sourceHash, profile, dataHash;
            public bool sourceDirty, audio, battleScreenshot, automatedNormalInput;
            public int frames, finalTick, tickRate, width, height;
            public double wallSeconds;
        }
    }
}
