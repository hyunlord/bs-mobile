using System;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using SowSiege.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.P1Capture
{
    [InitializeOnLoad]
    public static class P1Capture
    {
        const string ActiveKey = "SowSiege.P1Capture.Active";
        const string PreferencesKey = "SowSiege.P1Capture.Preferences";
        static RecorderController recorder;
        static RunCoordinator run;
        static string output;
        static double started, cardAt = -1, completedAt = -1, nextSample;
        static double unscaledStarted;
        static double preparationAt;
        static bool movieStarted, movieStopped;
        static CaptureClockGuard clock = new CaptureClockGuard();
        static int stage;
        static long lastEvent;
        static int people;
        static string replayPath;
        static string recordingError;
        static bool playerLoopInstalled;
        static int diagnosticFrames;
        static StreamWriter ledger;
        static readonly string[] Priority = { "core:muster_horn", "core:seed_bag", "core:ward_orbit", "core:harvest_scythe", "core:iron_blade", "core:soup_ladle", "core:rain_ladle" };

        [Serializable] sealed class PreferencesSnapshot
        {
            public bool[] present;
            public float[] values;
        }

        static P1Capture()
        {
            EditorApplication.update += Update;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (movieStarted && !movieStopped && (type == LogType.Error || type == LogType.Exception)) recordingError = message;
            };
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode) return;
                CapturePlayerLoop.Uninstall();
                playerLoopInstalled = false;
                RestorePreferences();
                if (SessionState.GetBool(ActiveKey, false)) Finish("failed: play mode exited before completion");
                if (Environment.GetEnvironmentVariable("P1_CAPTURE_EXIT") == "1" && !string.IsNullOrEmpty(SessionState.GetString("SowSiege.P1Capture.Output", "")))
                    EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetString("SowSiege.P1Capture.Result", "") == "complete" ? 0 : 1);
            };
        }

        [MenuItem("Sow & Siege/P1/Record normal automated run")]
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start capture from Edit mode.");
            output = Environment.GetEnvironmentVariable("P1_CAPTURE_DIR") ?? Path.GetFullPath("../artifacts/p1-recorder");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Use an empty P1_CAPTURE_DIR; all earlier capture evidence is preserved.");
            Directory.CreateDirectory(output);
            var snapshot = new PreferencesSnapshot { present = new bool[RunPreferences.Keys.Length], values = new float[RunPreferences.Keys.Length] };
            for (var i = 0; i < RunPreferences.Keys.Length; i++)
            {
                var key = RunPreferences.Prefix + RunPreferences.Keys[i];
                snapshot.present[i] = PlayerPrefs.HasKey(key);
                snapshot.values[i] = i < 2 ? PlayerPrefs.GetFloat(key) : PlayerPrefs.GetInt(key);
            }
            var preferencesJson = JsonUtility.ToJson(snapshot);
            SessionState.SetString(PreferencesKey, preferencesJson);
            File.WriteAllText(Path.Combine(output, "preferences-before.json"), preferencesJson);
            SessionState.SetString("SowSiege.P1Capture.Output", output);
            SessionState.SetBool(ActiveKey, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
            EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
            EditorApplication.isPlaying = true;
        }

        static void Update()
        {
            if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying) return;
            if (!playerLoopInstalled)
            {
                try
                {
                    CapturePlayerLoop.Install(PlayerLoopUpdate);
                    playerLoopInstalled = true;
                }
                catch (Exception error) { Finish("failed: " + error); }
            }
        }

        public static void PlayerLoopUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            try { Pump(); }
            catch (Exception error) { Finish("failed: " + error); }
        }

        static void Pump()
        {
            if (recordingError != null) throw new InvalidOperationException("Recording emitted an error: " + recordingError);
            if (run == null) run = UnityEngine.Object.FindFirstObjectByType<RunCoordinator>();
            if (run != null && run.Error != null) throw new InvalidOperationException(run.Error);
            if (run == null || run.Ui == null) return;
            if (stage == 0)
            {
                output = SessionState.GetString("SowSiege.P1Capture.Output", "");
                ledger = new StreamWriter(Path.Combine(output, "capture-ledger.tsv"), false) { AutoFlush = true };
                ledger.WriteLine("mediaSeconds\ttick\tkind\tdetail\tunscaledSeconds\trenderFrame");
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "01-editor-title.png"));
                preparationAt = Time.unscaledTimeAsDouble;
                Log("preparation-title", "outside movie; preserve initial title before neutral run scene loading");
                stage = -1;
                return;
            }
            if (stage == -1)
            {
                if (Time.unscaledTimeAsDouble - preparationAt < 1) return;
                run.StartNeutralRun(30000);
                stage = -2;
                return;
            }
            if (stage == -2)
            {
                if (run.Frame == null) return;
                if (run.Frame.Tick != 0) throw new InvalidOperationException("Initial run advanced before capture preparation: " + run.Frame.Tick);
                ClickButton("설정");
                CaptureRunGate.RequirePausedAtZero(run.Frame.Tick, run.MenuOpen);
                preparationAt = Time.unscaledTimeAsDouble;
                Log("preparation-pause", "ordinary settings button; tick=0; no gameplay skipped");
                stage = -3;
                return;
            }
            if (stage == -3)
            {
                CaptureRunGate.RequirePausedAtZero(run.Frame.Tick, run.MenuOpen);
                if (Time.unscaledTimeAsDouble - preparationAt < 1) return;
                var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                settings.FrameRate = 30;
                settings.FrameRatePlayback = FrameRatePlayback.Variable;
                settings.CapFrameRate = true;
                settings.SetRecordModeToManual();
                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "P1 normal automated input";
                movie.Enabled = true;
                movie.OutputFile = Path.Combine(output, "full-run");
                movie.CaptureAudio = true;
                movie.CaptureAlpha = false;
                movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 720, OutputHeight = 1560 };
                movie.EncoderSettings = new CoreEncoderSettings { Codec = CoreEncoderSettings.OutputCodec.MP4, EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High };
                settings.AddRecorderSettings(movie);
                recorder = new RecorderController(settings);
                recorder.PrepareRecording();
                if (!recorder.StartRecording()) throw new InvalidOperationException("Recorder refused capture.");
                started = Time.timeAsDouble;
                unscaledStarted = Time.unscaledTimeAsDouble;
                movieStarted = true;
                Log("capture-start", "Unity Recorder 5.1.7; automated pointer/card inputs; 1x; neutral first-playable rules without meta injection; seed=30000");
                Log("identity", $"commit={BuildIdentity.Commit};sourceHash={BuildIdentity.SourceHash};dirty={BuildIdentity.SourceDirty};dataHash={FoundationBoot.VerifiedDataHash};profile=first-playable;unity={Application.unityVersion}");
                Log("selection-rule", "early=start at run-start for180 seconds; late=end at run-complete+8 seconds for180 seconds; continuous clips at1x with no omitted in-clip frames");
                stage = 1;
                return;
            }
            var elapsed = Time.timeAsDouble - started;
            if (diagnosticFrames < 20 || stage == 2)
            {
                Log("clock-frame", $"phase=LateUpdate;timeFloat={Time.time:R};timeDouble={Time.timeAsDouble:R};delta={Time.deltaTime:R};unscaledDelta={Time.unscaledDeltaTime:R};frame={Time.frameCount};stage={stage}");
                diagnosticFrames++;
            }
            if (!movieStopped) clock.Validate(elapsed, Time.unscaledTimeAsDouble - unscaledStarted);
            if (elapsed > 1500) throw new TimeoutException("Capture exceeded 25 minutes of media time.");
            if (stage == 1)
            {
                CaptureRunGate.RequirePausedAtZero(run.Frame.Tick, run.MenuOpen);
                if (elapsed < 3) return;
                run.Send(ReplayCommandKind.SetAimMode, value: (int)AimMode.NearestEnemy);
                ClickButton("돌아가기");
                if (run.MenuOpen || run.Frame.Tick != 0) throw new InvalidOperationException("Normal settings return did not resume at tick zero.");
                var unscaledElapsed = Time.unscaledTimeAsDouble - unscaledStarted;
                clock.Arm(elapsed, unscaledElapsed);
                diagnosticFrames = 0;
                Log("clock-baseline", $"excludedPreRunOffsetSeconds={unscaledElapsed - elapsed:F6};mediaOriginUnchanged=true;thresholdSeconds=0.15;baselineCount=1");
                Log("run-start", run.RecordedReplayPath);
                replayPath = run.RecordedReplayPath;
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "02-editor-run.png"));
                stage = 3;
            }
            if (stage == 3)
            {
                var frame = run.Frame;
                if (run.Speed != 1 || Time.timeScale != 1) throw new InvalidOperationException("Evidence requires normal speed.");
                if (elapsed >= nextSample)
                {
                    Log("sample", $"health={frame.Lord.Health};level={frame.Level};season={frame.Season};people={frame.People.Count}");
                    nextSample = elapsed + 5;
                }
                foreach (var e in frame.Events.Where(e => e.Id > lastEvent))
                {
                    if (e.Kind == PresentationKind.HarvestExperience || e.Kind == PresentationKind.BossWarning) Log(e.Kind.ToString(), e.SourceId);
                    lastEvent = Math.Max(lastEvent, e.Id);
                }
                if (frame.People.Count != people)
                {
                    people = frame.People.Count;
                    Log("people-changed", string.Join(",", frame.People.Select(p => $"{p.Id}:{p.SourceId}:{p.Role}:{p.Members}")));
                }
                if (frame.Status == RunStatus.Completed)
                {
                    var summary = run.Session.GetSummary();
                    if (!summary.Survived || summary.Tick != 27000 || summary.BossDefeated != true)
                        throw new InvalidOperationException("Normal run did not survive full duration and defeat winter boss: " + summary);
                    completedAt = elapsed;
                    Log("run-complete", run.Session.GetSummary().ToString());
                    stage = 4;
                    return;
                }
                if (frame.Status == RunStatus.AwaitingCard)
                {
                    if (cardAt < 0) { cardAt = elapsed; Log("cards-visible", string.Join(",", run.Session.View.CaptureCards().Cards)); }
                    if (elapsed - cardAt >= 2)
                    {
                        var card = run.Session.View.CaptureCards().Cards.OrderBy(CardRank).First();
                        Log("automated-card-choice", card);
                        run.Send(ReplayCommandKind.ChooseCard, card: card);
                        cardAt = -1;
                    }
                    return;
                }
                Move(frame);
            }
            if (stage == 4 && elapsed - completedAt >= 3)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "03-editor-summary.png"));
                stage = 5;
            }
            if (stage == 5 && elapsed - completedAt >= 10)
            {
                Log("movie-stop", "summary retained; movie stops before restart scene; strict clock guard covered all recorded gameplay and summary");
                recorder.StopRecording();
                recorder = null;
                movieStopped = true;
                if (recordingError != null) throw new InvalidOperationException("Recorder finalization failed: " + recordingError);
                run.StartNeutralRun(30001);
                Log("restart-request", "seed=30001; outside movie; screenshot evidence only");
                stage = 6;
            }
            if (stage == 6 && elapsed - completedAt >= 14)
            {
                if (run.Frame == null || run.Frame.Status == RunStatus.Completed) throw new InvalidOperationException("Restart did not reach a new run.");
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "04-editor-restart.png"));
                stage = 7;
            }
            if (stage == 7 && elapsed - completedAt >= 16)
            {
                using (var stream = File.OpenRead(replayPath))
                {
                    if (File.Exists(replayPath + ".meta")) throw new InvalidOperationException("Neutral evidence must not contain a meta replay context.");
                    var replay = ReplayCodec.Read(stream);
                    if (replay.Commands.Any(c => c.Kind != ReplayCommandKind.Advance && c.Kind != ReplayCommandKind.ChooseCard && c.Kind != ReplayCommandKind.SetAimMode))
                        throw new InvalidOperationException("Replay contains a command outside normal movement, card choice, or aim setting.");
                    Log("replay-invariant", $"passed;commands={replay.Commands.Count};no grants, invulnerability, or spawn overrides");
                }
                foreach (var file in new[] { "01-editor-title.png", "02-editor-run.png", "03-editor-summary.png", "04-editor-restart.png" })
                    if (!File.Exists(Path.Combine(output, file))) throw new IOException("Missing screenshot: " + file);
                Finish("complete");
            }
        }

        static int CardRank(string id) { var rank = Array.IndexOf(Priority, id); return rank < 0 ? 999 : rank; }

        static void ClickButton(string label)
        {
            var button = run.Ui.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>()?.text == label);
            if (!button.interactable) throw new InvalidOperationException("Normal UI button is unavailable: " + label);
            button.onClick.Invoke();
        }

        static void Move(RunFrame frame)
        {
            var farm = frame.Farms.Where(f => f.Ripe).OrderBy(f => Math.Pow(f.Position.X-frame.Lord.Position.X,2)+Math.Pow(f.Position.Y-frame.Lord.Position.Y,2)).FirstOrDefault();
            var angle = frame.Tick / 180d;
            var target = farm != null ? farm.Position : new WorldPoint(frame.Estate.X + (int)(Math.Cos(angle) * 1500), frame.Estate.Y + (int)(Math.Sin(angle) * 1500));
            var direction = new Vector2(target.X - frame.Lord.Position.X, target.Y - frame.Lord.Position.Y).normalized;
            var origin = new Vector2(Screen.width * .25f, Screen.height * .2f);
            var pointer = new PointerEventData(EventSystem.current) { pointerId = 1701, position = origin };
            if (!run.Stick.Active) run.Stick.OnPointerDown(pointer);
            pointer.position = run.Stick.Origin + direction * run.Stick.Radius;
            run.Stick.OnDrag(pointer);
        }

        static void Log(string kind, string detail)
        {
            ledger?.WriteLine($"{(movieStarted ? Time.timeAsDouble - started : -1):F3}\t{run?.Frame?.Tick ?? 0}\t{kind}\t{detail?.Replace('\n', ' ').Replace('\t', ' ')}\t{(movieStarted ? Time.unscaledTimeAsDouble - unscaledStarted : -1):F3}\t{Time.renderedFrameCount}");
        }

        static void RestorePreferences()
        {
            var json = SessionState.GetString(PreferencesKey, "");
            if (string.IsNullOrEmpty(json)) return;
            var snapshot = JsonUtility.FromJson<PreferencesSnapshot>(json);
            for (var i = 0; i < RunPreferences.Keys.Length; i++)
            {
                var key = RunPreferences.Prefix + RunPreferences.Keys[i];
                if (!snapshot.present[i]) PlayerPrefs.DeleteKey(key);
                else if (i < 2) PlayerPrefs.SetFloat(key, snapshot.values[i]);
                else PlayerPrefs.SetInt(key, (int)snapshot.values[i]);
            }
            PlayerPrefs.Save();
            var restored = new PreferencesSnapshot { present = new bool[RunPreferences.Keys.Length], values = new float[RunPreferences.Keys.Length] };
            for (var i = 0; i < RunPreferences.Keys.Length; i++)
            {
                var key = RunPreferences.Prefix + RunPreferences.Keys[i];
                restored.present[i] = PlayerPrefs.HasKey(key);
                restored.values[i] = i < 2 ? PlayerPrefs.GetFloat(key) : PlayerPrefs.GetInt(key);
            }
            var restoredJson = JsonUtility.ToJson(restored);
            var folder = SessionState.GetString("SowSiege.P1Capture.Output", "");
            if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder, "preferences-after.json"), restoredJson);
            if (restoredJson != json) throw new InvalidOperationException("Preferences restoration verification failed.");
            SessionState.EraseString(PreferencesKey);
        }

        static void Finish(string result)
        {
            output = output ?? SessionState.GetString("SowSiege.P1Capture.Output", "");
            Log("capture-end", result);
            recorder?.StopRecording();
            ledger?.Dispose();
            SessionState.SetBool(ActiveKey, false);
            CapturePlayerLoop.Uninstall();
            playerLoopInstalled = false;
            SessionState.SetString("SowSiege.P1Capture.Result", result);
            File.WriteAllText(Path.Combine(output, "capture-result.txt"), result);
            EditorApplication.isPlaying = false;
            recorder = null; run = null; ledger = null; stage = 0;
            cardAt = -1; completedAt = -1; nextSample = 0; lastEvent = 0;
            people = 0; replayPath = null;
            clock = new CaptureClockGuard();
            diagnosticFrames = 0;
            movieStarted = false; movieStopped = false;
            recordingError = null;
            Debug.Log("P1 capture: " + result);
        }
    }
}
