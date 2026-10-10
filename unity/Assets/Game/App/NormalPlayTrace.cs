using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.App.Generated;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Game.App
{
    // Opt-in observation of normal rendering; never invokes the capture or replay pipeline.
    public sealed class NormalPlayTrace : MonoBehaviour
    {
        public Action SampleDiagnostics;
        public Action<string> WriteDiagnostics;
        public static string ProfileDirectory { get; } = ReadProfileDirectory();
        static string ReadProfileDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--smoothness-trace") < 0) return null;
            var index = Array.IndexOf(args, "--smoothness-profile");
            if (index < 0) return null;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Smoothness profile directory is missing.");
            var path = Path.GetFullPath(args[index + 1]);
            if (File.Exists(path) || Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length != 0) throw new IOException("Smoothness profile must be new or empty.");
            return path;
        }
        const int FrameCapacity = 18000, EntityCapacity = 250000, SampleCapacity = 128;
        readonly FrameRow[] frames = new FrameRow[FrameCapacity];
        readonly EntityRow[] entities = new EntityRow[EntityCapacity];
        readonly Sample[] samples = new Sample[SampleCapacity];
        readonly HashSet<int> enemyIds = new HashSet<int>(5);
        RunCoordinator run;
        Mouse syntheticMouse;
        string folder;
        bool scripted, quit, begun, finished, inFrame, startupScreenshot, timingOut, phasesArmed;
        int frameCount, entityCount, sampleCount, droppedFrames, droppedEntities, uiStage, buttonPhase, inputPreparationStep;
        double beganAt, phaseBeganAt, initializedAt, nextUiAttempt;
        string failure;
        Vector2 pendingClick, stickOrigin;
        Vector2 commandedDirection = Vector2.right;
        FrameRow current;
        struct FrameRow
        {
            public int UnityFrame, Tick, Phase, Width, Height;
            public double Wall, Dt, Residual;
            public Vector2 Input, Authoritative, Predicted, Camera, CameraShake;
            public float CameraSize, PredictionSpeed;
            public bool Paused, Focused;
        }
        struct Sample { public string Kind; public int Id; public Vector2 World; }
        struct EntityRow { public int Frame, Id; public string Kind; public Vector2 World, Screen; }

        public static NormalPlayTrace Create(RunCoordinator coordinator)
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--smoothness-trace") < 0) return null;
            if (Array.IndexOf(args, "--autoplay-capture") >= 0) throw new InvalidOperationException("Normal trace cannot run with autoplay capture.");
            var outputIndex = Array.IndexOf(args, "--smoothness-trace-output");
            var output = outputIndex < 0 ? Path.Combine(Application.persistentDataPath, "normal-trace-" + Guid.NewGuid().ToString("N"))
                : outputIndex + 1 < args.Length ? Path.GetFullPath(args[outputIndex + 1]) : throw new ArgumentException("Trace output directory is missing.");
            if (File.Exists(output) || Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length != 0)
                throw new IOException("Normal trace output must be new or empty.");
            var trace = coordinator.gameObject.AddComponent<NormalPlayTrace>();
            trace.run = coordinator; trace.folder = output;
            trace.initializedAt = Time.realtimeSinceStartupAsDouble; trace.nextUiAttempt = trace.initializedAt + 2;
            Directory.CreateDirectory(output);
            trace.scripted = Array.IndexOf(args, "--smoothness-scripted-input") >= 0;
            trace.commandedDirection = ScriptDirection(Array.IndexOf(args, "--smoothness-oblique") >= 0);
            trace.quit = Array.IndexOf(args, "--smoothness-trace-quit") >= 0;
            if (trace.scripted && ProfileDirectory == null) throw new ArgumentException("Scripted trace requires --smoothness-profile for isolated saves.");
            if (trace.scripted)
            {
                trace.syntheticMouse = InputSystem.AddDevice<Mouse>("Smoothness diagnostic mouse");
                InputSystem.onBeforeUpdate += trace.DriveInput;
            }
            UnityEngine.Debug.Log($"Normal trace ready: scripted={trace.scripted}; focus={Application.isFocused}; screen={Screen.width}x{Screen.height}; mouse={trace.syntheticMouse?.deviceId}; output={output}");
            return trace;
        }

        public void BeginSession()
        {
            if (begun) return;
            begun = true; beganAt = Time.realtimeSinceStartupAsDouble; phaseBeganAt = beganAt; phasesArmed = !scripted; inputPreparationStep = 0;
            enemyIds.Clear();
            UnityEngine.Debug.Log($"Normal trace session started: focus={Application.isFocused}; tick={run.Frame?.Tick}; mouse={syntheticMouse?.position.ReadValue()}");
        }
        public void BeginFrame()
        {
            inFrame = begun && !finished;
            if (!inFrame) return;
            sampleCount = 0;
            current = new FrameRow { UnityFrame = Time.frameCount, Wall = Time.realtimeSinceStartupAsDouble - beganAt,
                Width = Screen.width, Height = Screen.height, Focused = Application.isFocused, Dt = Time.unscaledDeltaTime, Phase = scripted && phasesArmed ? MovementPhase(Time.realtimeSinceStartupAsDouble - phaseBeganAt) : -1 };
        }
        public static Vector2 ScriptDirection(bool oblique) => oblique ? new Vector2(.8f, .6f) : Vector2.right;
        public static int MovementPhase(double seconds) => seconds < 0 ? -1 : Math.Min(4, (int)(seconds / 4));
        public void ObserveInput(Vector2 input, int tick, double residual, bool paused, Vector2 authoritative, Vector2 predicted, float speed)
        {
            if (!inFrame) return;
            if (scripted && !phasesArmed && !paused && (input - commandedDirection * .25f).sqrMagnitude <= .0001f)
            {
                phasesArmed = true; phaseBeganAt = Time.realtimeSinceStartupAsDouble; current.Phase = 0;
            }
            current.Input = input;
 current.Tick = tick; current.Residual = residual; current.Paused = paused;
            current.Authoritative = authoritative; current.Predicted = predicted; current.PredictionSpeed = speed;
        }
        public void Entity(string kind, int id, Vector2 world)
        {
            if (!inFrame) return;
            if (kind == "enemy")
            {
                if (!enemyIds.Contains(id) && enemyIds.Count < 5) enemyIds.Add(id);
                if (!enemyIds.Contains(id)) return;
            }
            else if (kind != "lord" && kind != "projectile" && kind != "orbit") return;
            if (sampleCount == samples.Length) { droppedEntities++; return; }
            samples[sampleCount++] = new Sample { Kind = kind, Id = id, World = world };
        }
        public void EndFrame(Camera camera, Vector2 cameraShake)
        {
            if (!inFrame) return;
            inFrame = false;
            if (frameCount == frames.Length) { droppedFrames++; return; }
            if (camera != null)
            {
                current.Camera = camera.transform.position; current.CameraSize = camera.orthographicSize;
                for (var i = 0; i < sampleCount; i++)
                {
                    if (entityCount == entities.Length) { droppedEntities++; continue; }
                    var sample = samples[i];
                    entities[entityCount++] = new EntityRow { Frame = frameCount, Kind = sample.Kind, Id = sample.Id,
                        World = sample.World, Screen = camera.WorldToScreenPoint(sample.World) };
                }
            }
            current.CameraShake = cameraShake;
            frames[frameCount++] = current;
            SampleDiagnostics?.Invoke();
        }
        IEnumerator StartupTimeout()
        {
            timingOut = true; failure = "Normal UI startup did not reach a session within twenty seconds.";
            UnityEngine.Debug.LogError("Normal trace startup timeout: " + StartupState());
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "startup-timeout.png"));
            yield return new WaitForEndOfFrame();
            yield return null;
            Finish();
            if (quit) Application.Quit(1);
        }
        public static string ObservedObjectName(UnityEngine.Object value) => value != null ? value.name : "none";
        string StartupState()
        {
            var events = EventSystem.current;
            var module = events != null ? events.currentInputModule : null;
            var selected = events != null ? events.currentSelectedGameObject : null;
            return $"focus={Application.isFocused}; module={(module != null ? module.GetType().Name : "none")}; moduleActive={module != null && module.isActiveAndEnabled}; selected={ObservedObjectName(selected)}; mouse={syntheticMouse?.position.ReadValue()}; buttonPhase={buttonPhase}; attempts={uiStage}";
        }
        void LogStartupTarget(Button button)
        {
            var hits = new List<RaycastResult>();
            var events = EventSystem.current;
            if (events != null) events.RaycastAll(new PointerEventData(events) { position = pendingClick }, hits);
            var text = button != null ? button.GetComponentInChildren<Text>() : null;
            var hit = hits.Count > 0 ? hits[0].gameObject : null;
            UnityEngine.Debug.Log($"Normal trace UI target={ObservedObjectName(button)}; text={(text != null ? text.text : "none")}; point={pendingClick}; topHit={ObservedObjectName(hit)}; {StartupState()}");
        }
        void LateUpdate()
        {
            if (!begun && !finished && !timingOut)
            {
                if (!startupScreenshot && Time.realtimeSinceStartupAsDouble - initializedAt >= 1)
                {
                    startupScreenshot = true;
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "startup.png"));
                    UnityEngine.Debug.Log("Normal trace startup observation: " + StartupState());
                }
                if (scripted && Time.realtimeSinceStartupAsDouble - initializedAt >= 20) StartCoroutine(StartupTimeout());
            }
            if (begun && !finished && scripted && !phasesArmed && Time.realtimeSinceStartupAsDouble - beganAt >= 10)
            {
                failure = "Synthetic slow drag never reached normal input sampling within ten seconds.";
                Finish(); if (quit) Application.Quit(1);
            }
            if (begun && !finished && phasesArmed && Time.realtimeSinceStartupAsDouble - phaseBeganAt >= 18)


            {
                Finish();
                if (quit) Application.Quit();
            }
        }
        void DriveInput()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || timingOut || finished || syntheticMouse == null || run == null || run.Ui == null) return;
            if (!begun)
            {
                if (buttonPhase == 0)
                {
                    if (Time.realtimeSinceStartupAsDouble < nextUiAttempt) return;
                    foreach (var button in run.Ui.GetComponentsInChildren<Button>())
                    {
                        var text = button.GetComponentInChildren<Text>();
                        if (!button.interactable || text == null || (text.text != (run.IsWave ? "시작하기" : "시작") && text.text != "개척 시작")) continue;
                        var rect = (RectTransform)button.transform;
                        pendingClick = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                        LogStartupTarget(button);
                        buttonPhase = 1; break;
                    }
                }
                if (buttonPhase == 1) { QueueMouse(pendingClick, false); buttonPhase = 2; }
                else if (buttonPhase == 2) { QueueMouse(pendingClick, true); buttonPhase = 3; }
                else if (buttonPhase == 3) { QueueMouse(pendingClick, false); buttonPhase = 0; uiStage++; nextUiAttempt = Time.realtimeSinceStartupAsDouble + .5; UnityEngine.Debug.Log("Normal trace UI release: " + StartupState()); }
                return;
            }
            if (!phasesArmed)
            {
                stickOrigin = new Vector2(Screen.width * .25f, Screen.height * .28f);
                if (inputPreparationStep < 2) { QueueMouse(stickOrigin, false); inputPreparationStep++; }
                else if (inputPreparationStep == 2) { QueueMouse(stickOrigin, true); inputPreparationStep++; }
                else if (run.Stick.Active) QueueMouse(stickOrigin + commandedDirection * run.Stick.Radius * .25f, true);
                else { QueueMouse(stickOrigin, false); inputPreparationStep = 1; }
                return;
            }
            var phase = MovementPhase(Time.realtimeSinceStartupAsDouble - phaseBeganAt);

            if (phase == 0 || phase == 2)
            {
                if (!run.Stick.Active)
                {
                    stickOrigin = new Vector2(Screen.width * .25f, Screen.height * .28f);
                    QueueMouse(stickOrigin, true);
                }
                else QueueMouse(stickOrigin + commandedDirection * run.Stick.Radius * (phase == 0 ? .25f : 1f), true);
            }
            else QueueMouse(stickOrigin, false);
        }
        void QueueMouse(Vector2 position, bool down)
            => InputSystem.QueueStateEvent(syntheticMouse, new MouseState { position = position }.WithButton(MouseButton.Left, down));
        void Finish()
        {
            if (finished || folder == null) return;
            finished = true;
            if (syntheticMouse != null) { QueueMouse(stickOrigin, false); InputSystem.onBeforeUpdate -= DriveInput; }
            Directory.CreateDirectory(folder);
            using (var file = new StreamWriter(Path.Combine(folder, "normal-frames.csv")))
            {
                file.WriteLine("frame,unityFrame,tick,wallSeconds,unityDtSeconds,residualSeconds,phase,inputX,inputY,paused,authoritativeX,authoritativeY,predictedX,predictedY,predictionSpeed,cameraX,cameraY,cameraHalfHeight,width,height,focused,cameraShakeX,cameraShakeY");
                for (var i = 0; i < frameCount; i++) { var r = frames[i]; file.WriteLine(FormattableString.Invariant($"{i},{r.UnityFrame},{r.Tick},{r.Wall:F9},{r.Dt:F9},{r.Residual:F9},{r.Phase},{r.Input.x:F6},{r.Input.y:F6},{r.Paused},{r.Authoritative.x:F9},{r.Authoritative.y:F9},{r.Predicted.x:F9},{r.Predicted.y:F9},{r.PredictionSpeed:F9},{r.Camera.x:F9},{r.Camera.y:F9},{r.CameraSize:F9},{r.Width},{r.Height},{r.Focused},{r.CameraShake.x:F9},{r.CameraShake.y:F9}")); }
            }
            using (var file = new StreamWriter(Path.Combine(folder, "normal-entities.csv")))
            {
                file.WriteLine("frame,kind,id,worldX,worldY,screenX,screenY");
                for (var i = 0; i < entityCount; i++) { var r = entities[i]; file.WriteLine(FormattableString.Invariant($"{r.Frame},{r.Kind},{r.Id},{r.World.x:F9},{r.World.y:F9},{r.Screen.x:F6},{r.Screen.y:F6}")); }
            }
            WriteDiagnostics?.Invoke(folder);
            File.WriteAllText(Path.Combine(folder, "normal-trace.txt"), FormattableString.Invariant($"commit={BuildIdentity.Commit}\nsourceHash={BuildIdentity.SourceHash}\nprofile={CanonicalContent.ProfileName}\nsyntheticInput={scripted}\nisolatedProfile={ProfileDirectory ?? "none: existing normal profile"}\ninputPath=InputSystem Mouse state -> EventSystem -> UI/FloatingStick; not physical input\ncapture=false\nprewarm=false\nfailure={failure ?? "none"}\nframes={frameCount}\nentities={entityCount}\ndroppedFrames={droppedFrames}\ndroppedEntities={droppedEntities}\ncommandedDirectionX={commandedDirection.x:F6}\ncommandedDirectionY={commandedDirection.y:F6}\nphaseStartWallSeconds={phaseBeganAt - beganAt:F9}\nphasesArmed={phasesArmed}\nphase0=commanded direction 25 percent 4s; phase1=release 4s; phase2=commanded direction 100 percent 4s; phase3=release 4s; phase4=release remainder\nworldCoordinates=Unity world units; screenCoordinates=actual native pixels; stable first five enemy IDs are never replaced on death\n"));
        }
        void OnApplicationQuit() => Finish();
        void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= DriveInput;
            if (syntheticMouse != null && syntheticMouse.added) InputSystem.RemoveDevice(syntheticMouse);
        }
    }
}
