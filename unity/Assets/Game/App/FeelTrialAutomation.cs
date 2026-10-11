using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Game.App.Generated;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // Opt-in normal-speed UI experiment. No screenshot, audio or frame recorder is installed.
    public sealed class FeelTrialAutomation : MonoBehaviour
    {
        public static FeelTrialAutomation Active { get; private set; }
        public FeelTrialOptions Options { get; private set; }
        public string ProfileDirectory { get; private set; }
        public string PreferencesPrefix { get; private set; }
        readonly List<Button> buttons = new List<Button>(32);
        readonly FeelTrialDamage damage = new FeelTrialDamage();
        RunCoordinator run;
        PointerEventData pointer;
        string output;
        bool ended, subscribed;
        int stage, cardChoices, initialHealth, maxHealth, lastTargetTick = -1, unfocusedFrames;
        double started, runStarted, nextAction, cardAt = -1;
        Vector2 direction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
#if !UNITY_EDITOR
            try
            {
                var args = Environment.GetCommandLineArgs();
                var options = FeelTrialOptions.Parse(args);
                if (options == null) return;
                var index = Array.IndexOf(args, "--feel-trial-output");
                if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) || Array.LastIndexOf(args, "--feel-trial-output") != index)
                    throw new ArgumentException("--feel-trial-output requires one new directory.");
                var folder = Path.GetFullPath(args[index + 1]);
                AutoplayCapture.RequireEmptyOutput(folder);
                var owner = new GameObject("Feel trial normal UI"); DontDestroyOnLoad(owner);
                var trial = owner.AddComponent<FeelTrialAutomation>();
                trial.Options = options; trial.output = folder;
                trial.ProfileDirectory = Path.Combine(folder, "profile");
                trial.PreferencesPrefix = "sowsiege.feeltrial." + Guid.NewGuid().ToString("N") + ".";
                Directory.CreateDirectory(trial.ProfileDirectory);
                trial.started = Time.realtimeSinceStartupAsDouble;
                WaveCaptureInput.ConfigureMovement("evasive"); WaveCaptureInput.ConfigurePriority(null);
                Active = trial;
            }
            catch (Exception error) { Debug.LogError("FEEL_TRIAL_FAILED " + error); Application.Quit(2); }
#endif
        }

        void Update()
        {
            if (ended || Active != this) return;
            try
            {
                if (run == null) run = FindFirstObjectByType<RunCoordinator>();
                if (Time.realtimeSinceStartupAsDouble - started > 600) throw new TimeoutException("Trial exceeded ten wall-clock minutes; no speedup applied.");
                if (run == null || run.Ui == null || EventSystem.current == null) return;
                if (run.Error != null) throw new InvalidOperationException(run.Error);
                if (!run.IsWave || Time.timeScale != 1 || run.Speed != 1) throw new InvalidOperationException("Trial requires normal-speed wave-1a.");
                if (!subscribed) { run.WaveFrameAccepted += Observe; subscribed = true; }
                if (pointer == null) pointer = new PointerEventData(EventSystem.current) { pointerId = 1702, button = PointerEventData.InputButton.Left };
                if (!Application.isFocused) { unfocusedFrames++; return; }
                var now = Time.realtimeSinceStartupAsDouble;
                if (now < nextAction) return;
                if (run.CurrentFrame == null)
                {
                    if (stage == 0) { if (Click("시작하기")) { stage++; nextAction = now + .5; } }
                    else if (Click("개척 시작")) { stage++; nextAction = now + .5; }
                    return;
                }
                if (runStarted == 0) { runStarted = now; initialHealth = run.CurrentFrame.Lord.Health; }
                maxHealth = Math.Max(maxHealth, run.CurrentFrame.Lord.MaxHealth);
                if (run.CurrentFrame.Status == RunStatus.Completed || run.CurrentFrame.Tick >= FeelTrialOptions.DurationSeconds * run.CurrentFrame.TickRate) { Finish(null); return; }
                if (run.CurrentFrame.Status == RunStatus.AwaitingCard)
                {
                    run.Stick.ResetStick();
                    if (cardAt < 0) cardAt = now;
                    if (now - cardAt < 2) return;
                    var card = WaveCaptureInput.ChooseCard(run.Session.View.CaptureCards(), run.CurrentFrame, FoundationBoot.Catalog);
                    if (Click("Choose " + card, true)) { cardAt = -1; cardChoices++; }
                    return;
                }
                if (run.CurrentFrame.Status != RunStatus.Running || run.CaptureClockPaused) return;
                if (lastTargetTick != run.CurrentFrame.Tick)
                {
                    lastTargetTick = run.CurrentFrame.Tick;
                    var frame = run.CurrentFrame; var angle = frame.Tick / 180d;
                    var fallback = new WorldPoint(frame.Estate.X + (int)(Math.Cos(angle) * 1500), frame.Estate.Y + (int)(Math.Sin(angle) * 1500));
                    var target = WaveCaptureInput.Target(frame, run.CurrentWave, fallback, FoundationBoot.Catalog);
                    direction = CaptureArrivalSteering.Direction(new Vector2(target.X - frame.Lord.Position.X, target.Y - frame.Lord.Position.Y), FoundationBoot.Catalog.Tuning.World.Map.LordSpeed);
                }
                pointer.position = new Vector2(Screen.width * .25f, Screen.height * .2f);
                if (!run.Stick.Active) ExecuteEvents.Execute(run.Stick.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                pointer.position = run.Stick.Origin + direction * run.Stick.Radius;
                ExecuteEvents.Execute(run.Stick.gameObject, pointer, ExecuteEvents.dragHandler);
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        bool Click(string text, bool name = false)
        {
            buttons.Clear(); run.Ui.GetComponentsInChildren(false, buttons);
            foreach (var button in buttons)
            {
                if (!button.interactable) continue;
                var label = button.GetComponentInChildren<Text>();
                if (name ? button.name != text : label == null || label.text != text) continue;
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                return true;
            }
            return false;
        }
        void Observe(WaveRuntimeFrame frame)
        {
            foreach (var value in frame.Events) damage.Observe(value);
        }
        void Finish(string failure)
        {
            if (ended) return; ended = true;
            if (subscribed && run != null) run.WaveFrameAccepted -= Observe;
            run?.Stick?.ResetStick();
            var result = new Result { mode = Options.Mode, seed = FeelTrialOptions.Seed, inputPolicy = FeelTrialOptions.InputPolicy, durationLimitSeconds = FeelTrialOptions.DurationSeconds,
                commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash, sourceDirty = BuildIdentity.SourceDirty, dataHash = CanonicalContent.DataHash,
                profile = CanonicalContent.ProfileName, unity = Application.unityVersion, initialHealth = initialHealth, maximumHealth = maxHealth,
                hitCount = damage.Hits, actualHealthLost = damage.LostHealth, cardChoices = cardChoices, failure = failure, feedback = Options.Feedback,
                fasterLord = Options.Rules.FasterLord, immediateContact = Options.Rules.ImmediateContact, readableHealth = Options.Rules.ReadableHealth,
                unfocusedFrames = unfocusedFrames, wallSeconds = Time.realtimeSinceStartupAsDouble - started, gameplayWallSeconds = runStarted == 0 ? 0 : Time.realtimeSinceStartupAsDouble - runStarted };
            try
            {
                if (run?.CurrentFrame != null)
                {
                    result.tick = run.CurrentFrame.Tick; result.tickRate = run.CurrentFrame.TickRate; result.durationSeconds = result.tick / (double)result.tickRate;
                    result.remainingHealth = run.CurrentFrame.Lord.Health; result.maximumHealth = run.CurrentFrame.Lord.MaxHealth;
                    result.survived = result.tick >= FeelTrialOptions.DurationSeconds * result.tickRate && result.remainingHealth > 0;
                    result.outcome = result.survived ? "survived-observation" : result.remainingHealth <= 0 ? "death" : "interrupted";
                    result.replay = run.RecordedReplayPath;
                    run.FinishFeelTrialRecording();
                    using var stream = File.OpenRead(result.replay); var replay = ReplayCodec.Read(stream);
                    foreach (var command in replay.Commands) if (!AutoplayCapture.IsOrdinaryCommand(command.Kind)) throw new InvalidOperationException("Nonordinary trial command.");
                    var verification = ReplayRunner.Verify(FoundationBoot.Catalog, FoundationBoot.VerifiedDataHash, replay);
                    result.stateHash = verification.StateHash;
                    result.replayVerified = true;
                    result.replayFormat = replay.Header.FormatVersion;
                    if (replay.Header.Options.Feel != Options.Rules) throw new InvalidOperationException("Replay feel options differ from requested trial.");
                    using var hash = SHA256.Create(); result.replaySha256 = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(result.replay))).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception error) { result.failure = (failure ?? "") + "\n" + error; }
            finally
            {
                foreach (var key in RunPreferences.Keys) PlayerPrefs.DeleteKey(PreferencesPrefix + key);
                PlayerPrefs.Save();
                WaveCaptureInput.ConfigureMovement(null); WaveCaptureInput.ConfigurePriority(null);
            }
            File.WriteAllText(Path.Combine(output, "feel-trial-result.json"), JsonUtility.ToJson(result, true));
            UnityEngine.Debug.Log("FEEL_TRIAL_" + (result.failure == null ? "COMPLETE " : "FAILED ") + output);
            Application.Quit(result.failure == null ? 0 : 1);
        }
        void OnApplicationQuit() { if (!ended && Active == this) Finish("Application quit before observation completed."); }
        void OnDestroy() { if (subscribed && run != null) run.WaveFrameAccepted -= Observe; if (Active == this) Active = null; }
        [Serializable] sealed class Result
        {
            public string mode, inputPolicy, commit, sourceHash, dataHash, profile, unity, failure, outcome, replay, replaySha256, stateHash;
            public bool sourceDirty, survived, fasterLord, immediateContact, readableHealth, feedback, replayVerified;
            public bool captureOff = true, automatedNormalInput = true;
            public int seed, durationLimitSeconds, replayFormat, tick, tickRate, initialHealth, maximumHealth, remainingHealth, hitCount, cardChoices, unfocusedFrames;
            public long actualHealthLost;
            public double durationSeconds, wallSeconds, gameplayWallSeconds;
            public string boundary = "Same seed and reactive normal UI policy, not identical input commands. One real-time native run per combination. No recorder, state grants, resurrection or accelerated time. Lost health sums actual lord-hit damage, including repeated loss after healing. Death is an observed outcome, not a harness failure.";
        }
    }
}
