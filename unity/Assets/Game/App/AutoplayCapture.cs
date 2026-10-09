using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.App.Generated;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    public sealed class AutoplayCapture : MonoBehaviour
    {
        public static AutoplayCapture Active { get; private set; }
        public string ProfileDirectory { get; private set; }
        public string PreferencesPrefix { get; private set; }
        int captureSeed=30000;
        public int RunSeed => stage < 8 ? captureSeed : checked(captureSeed+1);
        static readonly string[] Priority = { "core:muster_horn", "core:seed_bag", "core:ward_orbit", "core:harvest_scythe", "core:iron_blade", "core:soup_ladle", "core:rain_ladle" };
        string output, replayPath, captureTarget;
        RunCoordinator run;
        StreamWriter ledger;
        double started, nextAction, cardAt = -1, nextSample, restartAt;
        int stage;
        bool capturing, ended, waveAttached;
        long lastWaveEvent=-1;
        Result result;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            var args = Environment.GetCommandLineArgs();
            if (!IsRequested(args)) return;
            try
            {
                var folder = ParseOutput(args, Path.Combine(Application.persistentDataPath, "autoplay-captures"));
                var owner = new GameObject("Opt-in native autoplay capture");
                DontDestroyOnLoad(owner);
                Active = owner.AddComponent<AutoplayCapture>();
                Active.Initialize(folder);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError("AUTOPLAY_CAPTURE_FAILED " + error);
                Application.Quit(2);
            }
#endif
        }

        public static bool IsRequested(string[] args) => args.Contains("--autoplay-capture");

        public static string ParseOutput(string[] args, string defaultRoot)
        {
            var positions = args.Select((value, index) => new { value, index }).Where(x => x.value == "--capture-output").ToArray();
            if (positions.Length > 1) throw new ArgumentException("Only one --capture-output is permitted.");
            if (positions.Length == 0) return Path.Combine(defaultRoot, Guid.NewGuid().ToString("N"));
            var index = positions[0].index;
            if (index + 1 == args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("--capture-output requires a directory.");
            return Path.GetFullPath(args[index + 1]);
        }

        public static void RequireEmptyOutput(string folder)
        {
            if (File.Exists(folder) || Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
                throw new IOException("Capture requires a new or empty output directory; previous evidence is preserved.");
        }

        void Initialize(string folder)
        {
            RequireEmptyOutput(folder);
            var args=Environment.GetCommandLineArgs();
            var seedArgument=Array.IndexOf(args,"--capture-seed");
            if(seedArgument>=0&&(seedArgument+1>=args.Length||!int.TryParse(args[seedArgument+1],out captureSeed)||captureSeed<0||captureSeed==int.MaxValue))throw new ArgumentException("--capture-seed requires a nonnegative restartable integer.");
            var priorityArgument=Array.IndexOf(args,"--capture-priority");
            if(priorityArgument>=0){if(priorityArgument+1>=args.Length)throw new ArgumentException("--capture-priority requires comma-separated card IDs.");WaveCaptureInput.ConfigurePriority(args[priorityArgument+1]);}
            var targetArgument=Array.IndexOf(args,"--capture-target-material");
            if(targetArgument>=0){if(targetArgument+1>=args.Length)throw new ArgumentException("--capture-target-material requires an ID.");captureTarget=args[targetArgument+1];}
            output = folder;
            ProfileDirectory = Path.Combine(output, "profile");
            PreferencesPrefix = "sowsiege.capture." + Guid.NewGuid().ToString("N") + ".";
            Directory.CreateDirectory(ProfileDirectory);
            ledger = new StreamWriter(Path.Combine(output, "capture-ledger.jsonl"), false) { AutoFlush = true };
            started = Time.realtimeSinceStartupAsDouble;
            Application.runInBackground = true;
            result = new Result { status = "running", commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash,
                sourceDirty = BuildIdentity.SourceDirty, dataHash = CanonicalContent.DataHash, profile = CanonicalContent.ProfileName, unity = Application.unityVersion,
                automatedInput = true, timeScale = 1, isolatedProfile = ProfileDirectory, preferencesPrefix = PreferencesPrefix };
            WriteResult();
            Log("capture-start", "automated pointer and UI button input; 1x; fresh chapter1 profile; focus-independent capture; no OS-user or human-input claim");
            nextAction = 3;
        }

        void Update()
        {
            if (ended || ledger == null) return;
            try
            {
                if (Elapsed > 1800) throw new TimeoutException("Normal capture exceeded 30 minutes.");
                if (!capturing) Pump();
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        double Elapsed => Time.realtimeSinceStartupAsDouble - started;

        void Pump()
        {
            if (Elapsed > 1800) throw new TimeoutException("Normal capture exceeded 30 minutes.");
            if (run == null) run = FindFirstObjectByType<RunCoordinator>();
            if (run == null || run.Ui == null)
            {
                var boot = FindFirstObjectByType<FoundationBoot>();
                if (boot != null && boot.Error != null) throw new InvalidOperationException(boot.Error);
                return;
            }
            if(!waveAttached){run.WaveFrameAccepted+=RecordWaveFrame;waveAttached=true;}
            if (run.Error != null) throw new InvalidOperationException(run.Error);
            if (Time.timeScale != 1 || run.Speed != 1) throw new InvalidOperationException("Capture requires normal speed.");
            if (Elapsed < nextAction) return;
            switch (stage)
            {
                case 0:
                    if (run.Session != null || run.Progression?.State.PendingRun != null) throw new InvalidOperationException("Capture did not start at a fresh title.");
                    Capture("01-native-title.png"); stage = 1; nextAction = Elapsed + 1; break;
                case 1: Click("설정"); stage = 2; nextAction = Elapsed + .5; break;
                case 2: ClickContaining("가까운 적 자동 조준"); stage = 3; nextAction = Elapsed + .5; break;
                case 3: Click("돌아가기");
                    if(captureTarget!=null)
                    {
                        if(!run.IsWave||!FoundationBoot.Catalog.WaveRuntime.MaterialTargets.TryGetValue(captureTarget,out var tool))throw new InvalidOperationException("Capture target is not supported by this profile.");
                        ClickContaining(CanonicalContent.Displays.Single(d=>d.Id==tool).DisplayName);
                        Log("target-material-click",captureTarget);
                    }
                    Click("시작"); stage = 4; nextAction = Elapsed + 1; break;
                case 4: Click("개척 시작"); stage = 5; nextAction = Elapsed + 2; break;
                case 5:
                    if (run.Frame == null) return;
                    replayPath = run.RecordedReplayPath; result.replay = replayPath;
                    Log("run-start", "seed="+RunSeed+";profile="+CanonicalContent.ProfileName+";"+replayPath); Capture("02-native-run.png"); stage = 6; break;
                case 6: Play(); break;
                case 7:
                    if (!run.MenuOpen) return;
                    Capture("03-native-summary.png"); stage = 8; nextAction = Elapsed + 2; break;
                case 8: lastWaveEvent=-1; restartAt = Elapsed; Click("다시 하기"); stage = 9; nextAction = Elapsed + 3; break;
                case 9:
                    if (!CaptureRestartGate.IsReady(Elapsed - restartAt, replayPath, run.RecordedReplayPath,
                        run.Frame?.Status == RunStatus.Running, run.Frame?.Tick ?? 0)) return;
                    result.restartReplay = run.RecordedReplayPath;
                    Log("restart-running", result.restartReplay); Capture("04-native-restart.png"); stage = 10; nextAction = Elapsed + 1; break;
                case 10: Verify(); Finish(true, run.IsWave ? "wave-1a actual outcome recorded; summary displayed; restart advanced; no survival gate" : "survived duration, defeated winter boss, summary displayed, restart advanced"); break;
            }
        }

        void Play()
        {
            var frame = run.Frame;
            if (Elapsed >= nextSample)
            {
                Log("sample", $"health={frame.Lord.Health};level={frame.Level};season={frame.Season};people={frame.People.Count}");
                nextSample = Elapsed + 5;
            }
            if (frame.Status == RunStatus.Completed)
            {
                var summary = run.Session.GetSummary();
                result.completedTick = summary.Tick; result.bossDefeated = summary.BossDefeated == true; result.survived = summary.Survived; result.stateHash = summary.StateHash;
                if (!run.IsWave && (!summary.Survived || summary.Tick != frame.DurationTicks || summary.BossDefeated != true))
                    throw new InvalidOperationException("Normal run failed survival/full-duration/winter-boss gate: " + summary);
                if(run.Wave!=null)Log("wave-counters",string.Join(";",run.Wave.Counters.Select(p=>p.Key+"="+p.Value)));
                Log("run-complete", summary.ToString()); stage = 7; nextAction = Elapsed + 3; return;
            }
            if (frame.Status == RunStatus.AwaitingCard)
            {
                if (cardAt < 0) { cardAt = Elapsed; Log("cards-visible", string.Join(",", run.Session.View.CaptureCards().Cards)); }
                if (Elapsed - cardAt < 2) return;
                var card = run.Session.View.CaptureCards().Cards.OrderBy(CardRank).First();
                var parent = run.Ui.GetComponentsInChildren<RectTransform>().First(t => t.name == "Card " + card);
                ClickButton(parent.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == "선택"));
                Log("card-click", card); cardAt = -1; return;
            }
            var farm = frame.Farms.Where(f => f.Ripe).OrderBy(f => Distance(f.Position, frame.Lord.Position)).FirstOrDefault();
            var angle = frame.Tick / 180d;
            var target = farm != null ? farm.Position : new WorldPoint(frame.Estate.X + (int)(Math.Cos(angle) * 1500), frame.Estate.Y + (int)(Math.Sin(angle) * 1500));
            if(run.Wave!=null)target=WaveCaptureInput.Target(frame,run.Wave,target,FoundationBoot.Catalog);
            var direction = new Vector2(target.X - frame.Lord.Position.X, target.Y - frame.Lord.Position.Y).normalized;
            var pointer = new PointerEventData(EventSystem.current) { pointerId = 1701, position = new Vector2(Screen.width * .25f, Screen.height * .2f), button = PointerEventData.InputButton.Left };
            if (!run.Stick.Active) ExecuteEvents.Execute(run.Stick.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            pointer.position = run.Stick.Origin + direction * run.Stick.Radius;
            ExecuteEvents.Execute(run.Stick.gameObject, pointer, ExecuteEvents.dragHandler);
        }

        static double Distance(WorldPoint a, WorldPoint b) => Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2);
        static int CardRank(string id) { if(Game.App.Generated.CanonicalContent.ProfileName=="wave-1a")return WaveCaptureInput.CardRank(id); var rank = Array.IndexOf(Priority, id); return rank < 0 ? 999 : rank; }
        void Click(string text) => ClickButton(run.Ui.GetComponentsInChildren<Button>().Single(b => b.interactable && b.GetComponentInChildren<Text>().text == text));
        void ClickContaining(string text) => ClickButton(run.Ui.GetComponentsInChildren<Button>().Single(b => b.interactable && b.GetComponentInChildren<Text>().text.Contains(text)));
        void ClickButton(Button button)
        {
            Log("ui-click", button.GetComponentInChildren<Text>().text);
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        void RecordWaveFrame(WaveRuntimeFrame frame)
        {
            foreach(var value in frame.Events)
            {
                if(value.Id<=lastWaveEvent)continue;lastWaveEvent=value.Id;
                Log("wave-event",$"eventId={value.Id};tick={value.Tick};kind={value.Kind};source={value.Source};subject={value.SubjectId};x={value.Position.X};y={value.Position.Y};targetX={value.Target.X};targetY={value.Target.Y};amount={value.Amount}");
            }
        }

        void Capture(string name) { capturing = true; StartCoroutine(CaptureFrame(name)); }
        IEnumerator CaptureFrame(string name)
        {
            yield return new WaitForEndOfFrame();
            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture == null || texture.width <= 0 || texture.height <= 0) throw new IOException("No rendered frame for " + name);
                var png = texture.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(output, name), png);
                Log("screenshot", $"{name};width={texture.width};height={texture.height};sha256={Hash(png)}");
            }
            catch (Exception error) { Finish(false, error.ToString()); }
            finally { if (texture != null) Destroy(texture); capturing = false; }
        }

        public static bool IsOrdinaryCommand(ReplayCommandKind kind) => kind == ReplayCommandKind.Advance || kind == ReplayCommandKind.ChooseCard || kind == ReplayCommandKind.SetAimMode;
        void Verify()
        {
            using var stream = File.OpenRead(replayPath);
            var replay = ReplayCodec.Read(stream);
            if (replay.Commands.Any(c => !IsOrdinaryCommand(c.Kind))) throw new InvalidOperationException("Replay contains nonordinary commands.");
            if (run.IsWave) ReplayRunner.Verify(FoundationBoot.Catalog, FoundationBoot.VerifiedDataHash, replay);
            else MetaReplayContext.Verify(File.ReadAllBytes(replayPath + ".meta"), FoundationBoot.Catalog, CanonicalContent.CreateMetaCatalog(), FoundationBoot.VerifiedDataHash, replay);
            result.replaySha256 = Hash(File.ReadAllBytes(replayPath));
            if (!run.IsWave) result.metaContextSha256 = Hash(File.ReadAllBytes(replayPath + ".meta"));
            foreach (var name in new[] { "01-native-title.png", "02-native-run.png", "03-native-summary.png", "04-native-restart.png" })
                if (!File.Exists(Path.Combine(output, name))) throw new IOException("Missing screenshot: " + name);
            Log("replay-verified", $"commands={replay.Commands.Count};sha256={result.replaySha256}");
        }

        static string Hash(byte[] bytes) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        void Log(string kind, string detail) => ledger?.WriteLine(JsonUtility.ToJson(new Entry { seconds = Elapsed, tick = run?.Frame?.Tick ?? 0, renderFrame = Time.renderedFrameCount, kind = kind, detail = detail }));
        void WriteResult() => File.WriteAllText(Path.Combine(output, "capture-result.json"), JsonUtility.ToJson(result, true));
        void Finish(bool success, string detail)
        {
            if (ended) return;
            ended = true;
            if(waveAttached&&run!=null){run.WaveFrameAccepted-=RecordWaveFrame;waveAttached=false;}
            FinalizeCapture(success, detail, code =>
            {
                try { UnityEngine.Debug.Log("AUTOPLAY_CAPTURE_" + (code == 0 ? "COMPLETE " : "FAILED ") + output); }
                finally { Application.Quit(code); }
            });
        }

        void FinalizeCapture(bool success, string detail, Action<int> exit)
        {
            CaptureCompletion.Run(success,
                () => Log("capture-stop-request", "requestedSuccess=" + success + "; " + detail + "; final result and exit code are authoritative"),
                () => { try { ledger?.Dispose(); } finally { ledger = null; } },
                (accepted, error) =>
                {
                    result.status = accepted ? "complete" : "failed";
                    result.detail = error == null ? detail : detail + "\n" + error;
                    result.elapsedSeconds = Elapsed;
                    WriteResult();
                }, exit);
        }

        void OnApplicationQuit()
        {
            if (!ended && result != null)
            {
                ended = true;
                FinalizeCapture(false, "Application quit before capture completed", _ => { });
            }
        }

        [Serializable] sealed class Entry { public double seconds; public int tick, renderFrame; public string kind, detail; }
        [Serializable] sealed class Result
        {
            public string profile, status, detail, commit, sourceHash, dataHash, unity, isolatedProfile, preferencesPrefix, replay, restartReplay, stateHash, replaySha256, metaContextSha256;
            public bool sourceDirty, automatedInput, survived, bossDefeated;
            public int timeScale, completedTick;
            public double elapsedSeconds;
        }
    }
}
