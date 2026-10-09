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
        public int RunSeed => stage < 8 ? 30000 : 30001;
        static readonly string[] Priority = { "core:muster_horn", "core:seed_bag", "core:ward_orbit", "core:harvest_scythe", "core:iron_blade", "core:soup_ladle", "core:rain_ladle" };
        string output, replayPath;
        RunCoordinator run;
        StreamWriter ledger;
        double started, nextAction, cardAt = -1, nextSample;
        int stage;
        bool capturing, ended;
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
            output = folder;
            ProfileDirectory = Path.Combine(output, "profile");
            PreferencesPrefix = "sowsiege.capture." + Guid.NewGuid().ToString("N") + ".";
            Directory.CreateDirectory(ProfileDirectory);
            ledger = new StreamWriter(Path.Combine(output, "capture-ledger.jsonl"), false) { AutoFlush = true };
            started = Time.realtimeSinceStartupAsDouble;
            Application.runInBackground = true;
            result = new Result { status = "running", commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash,
                sourceDirty = BuildIdentity.SourceDirty, dataHash = CanonicalContent.DataHash, unity = Application.unityVersion,
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
            if (run.Error != null) throw new InvalidOperationException(run.Error);
            if (Time.timeScale != 1 || run.Speed != 1) throw new InvalidOperationException("Capture requires normal speed.");
            if (Elapsed < nextAction) return;
            switch (stage)
            {
                case 0:
                    if (run.Session != null || run.Progression.State.PendingRun != null) throw new InvalidOperationException("Capture did not start at a fresh title.");
                    Capture("01-native-title.png"); stage = 1; nextAction = Elapsed + 1; break;
                case 1: Click("설정"); stage = 2; nextAction = Elapsed + .5; break;
                case 2: ClickContaining("가까운 적 자동 조준"); stage = 3; nextAction = Elapsed + .5; break;
                case 3: Click("돌아가기"); Click("시작"); stage = 4; nextAction = Elapsed + 1; break;
                case 4: Click("개척 시작"); stage = 5; nextAction = Elapsed + 2; break;
                case 5:
                    if (run.Frame == null) return;
                    replayPath = run.RecordedReplayPath; result.replay = replayPath;
                    Log("run-start", replayPath); Capture("02-native-run.png"); stage = 6; break;
                case 6: Play(); break;
                case 7:
                    if (!run.MenuOpen) return;
                    Capture("03-native-summary.png"); stage = 8; nextAction = Elapsed + 2; break;
                case 8: Click("다시 하기"); stage = 9; nextAction = Elapsed + 3; break;
                case 9:
                    if (run.Frame == null || run.Frame.Status != RunStatus.Running || run.Frame.Tick <= 0 || run.RecordedReplayPath == replayPath)
                        throw new InvalidOperationException("Restart did not advance a new normal run.");
                    result.restartReplay = run.RecordedReplayPath;
                    Log("restart-running", result.restartReplay); Capture("04-native-restart.png"); stage = 10; nextAction = Elapsed + 1; break;
                case 10: Verify(); Finish(true, "survived duration, defeated winter boss, summary displayed, restart advanced"); break;
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
                if (!summary.Survived || summary.Tick != frame.DurationTicks || summary.BossDefeated != true)
                    throw new InvalidOperationException("Normal run failed survival/full-duration/winter-boss gate: " + summary);
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
            var direction = new Vector2(target.X - frame.Lord.Position.X, target.Y - frame.Lord.Position.Y).normalized;
            var pointer = new PointerEventData(EventSystem.current) { pointerId = 1701, position = new Vector2(Screen.width * .25f, Screen.height * .2f), button = PointerEventData.InputButton.Left };
            if (!run.Stick.Active) ExecuteEvents.Execute(run.Stick.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            pointer.position = run.Stick.Origin + direction * run.Stick.Radius;
            ExecuteEvents.Execute(run.Stick.gameObject, pointer, ExecuteEvents.dragHandler);
        }

        static double Distance(WorldPoint a, WorldPoint b) => Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2);
        static int CardRank(string id) { var rank = Array.IndexOf(Priority, id); return rank < 0 ? 999 : rank; }
        void Click(string text) => ClickButton(run.Ui.GetComponentsInChildren<Button>().Single(b => b.interactable && b.GetComponentInChildren<Text>().text == text));
        void ClickContaining(string text) => ClickButton(run.Ui.GetComponentsInChildren<Button>().Single(b => b.interactable && b.GetComponentInChildren<Text>().text.Contains(text)));
        void ClickButton(Button button)
        {
            Log("ui-click", button.GetComponentInChildren<Text>().text);
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
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
            MetaReplayContext.Verify(File.ReadAllBytes(replayPath + ".meta"), FoundationBoot.Catalog, CanonicalContent.CreateMetaCatalog(), FoundationBoot.VerifiedDataHash, replay);
            result.replaySha256 = Hash(File.ReadAllBytes(replayPath));
            result.metaContextSha256 = Hash(File.ReadAllBytes(replayPath + ".meta"));
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
            result.status = success ? "complete" : "failed"; result.detail = detail; result.elapsedSeconds = Elapsed;
            Log(result.status, detail); WriteResult(); ledger?.Dispose(); ledger = null;
            UnityEngine.Debug.Log("AUTOPLAY_CAPTURE_" + (success ? "COMPLETE " : "FAILED ") + output);
            Application.Quit(success ? 0 : 1);
        }

        void OnApplicationQuit()
        {
            if (!ended && result != null) { result.status = "failed"; result.detail = "Application quit before capture completed"; result.elapsedSeconds = Elapsed; WriteResult(); }
            ledger?.Dispose(); ledger = null;
        }

        [Serializable] sealed class Entry { public double seconds; public int tick, renderFrame; public string kind, detail; }
        [Serializable] sealed class Result
        {
            public string status, detail, commit, sourceHash, dataHash, unity, isolatedProfile, preferencesPrefix, replay, restartReplay, stateHash, replaySha256, metaContextSha256;
            public bool sourceDirty, automatedInput, survived, bossDefeated;
            public int timeScale, completedTick;
            public double elapsedSeconds;
        }
    }
}
