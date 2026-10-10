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
        public static int? RequestedSeed { get; } = ParseRequestedSeed(Environment.GetCommandLineArgs());
        public static int? ParseRequestedSeed(string[] args)
        {
            var index = Array.IndexOf(args, "--smoothness-seed");
            if (index < 0) return null;
            if (Array.IndexOf(args, "--smoothness-trace") < 0 || index + 1 >= args.Length ||
                !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
                throw new ArgumentException("--smoothness-seed requires --smoothness-trace and a signed 32-bit integer.");
            return seed;
        }
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
        long projectileDeadline;
        string failure;
        bool awaitProjectile, projectileReady;
        int projectilePreparationTitleStage, projectileCardClicks, recordedProjectileSamples, recordedFriendlyProjectileSamples;
        double projectileReadyAt;
        string projectileSetupCard;
        Vector2 pendingClick, stickOrigin;
        Vector2 commandedDirection = Vector2.right;
        FrameRow current;
        struct FrameRow
        {
            public int UnityFrame, Tick, Phase, Width, Height;
            public double Wall, Dt, Residual, EngineFrameTime, CallbackEnd;
            public Vector2 Input, Authoritative, Predicted, Camera, CameraShake;
            public float CameraSize, PredictionSpeed;
            public bool Paused, Focused;
        }
        struct Sample { public string Kind; public int Id; public Vector2 World; public bool FriendlyProjectile; }
        struct EntityRow { public int Frame, Id; public string Kind; public Vector2 World, Screen; public bool FriendlyProjectile; }

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
            trace.commandedDirection = ScriptDirection(Array.IndexOf(args, "--smoothness-oblique") >= 0, Array.IndexOf(args, "--smoothness-left") >= 0);
            trace.quit = Array.IndexOf(args, "--smoothness-trace-quit") >= 0;
            trace.awaitProjectile = Array.IndexOf(args, "--smoothness-await-projectile") >= 0;
            if(trace.awaitProjectile && !coordinator.IsWave)throw new ArgumentException("Projectile setup requires the wave profile.");
            if(trace.awaitProjectile && !trace.scripted) throw new ArgumentException("Projectile setup requires scripted normal input.");
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
            projectileDeadline = System.Diagnostics.Stopwatch.GetTimestamp() + 120L * System.Diagnostics.Stopwatch.Frequency;
            begun = true; beganAt = Time.realtimeSinceStartupAsDouble; phaseBeganAt = beganAt; phasesArmed = !scripted; inputPreparationStep = 0;
            enemyIds.Clear();
            UnityEngine.Debug.Log($"Normal trace session started: focus={Application.isFocused}; tick={run.Frame?.Tick}; mouse={syntheticMouse?.position.ReadValue()}");
        }
        public void BeginFrame()
        {
            inFrame = begun && !finished && (!awaitProjectile || phasesArmed);
            if (!inFrame) return;
            sampleCount = 0;
            current = new FrameRow { UnityFrame = Time.frameCount, Wall = Time.realtimeSinceStartupAsDouble - beganAt,
                EngineFrameTime = Time.unscaledTimeAsDouble, Width = Screen.width, Height = Screen.height, Focused = Application.isFocused, Dt = Time.unscaledDeltaTime, Phase = scripted && phasesArmed ? MovementPhase(Time.realtimeSinceStartupAsDouble - phaseBeganAt) : -1 };
        }
        public static Vector2 ScriptDirection(bool oblique, bool left = false) => (oblique ? new Vector2(.8f, .6f) : Vector2.right) * (left ? -1 : 1);
        public static int MovementPhase(double seconds) => seconds < 0 ? -1 : Math.Min(4, (int)(seconds / 4));
        public void ObserveInput(Vector2 input, int tick, double residual, bool paused, Vector2 authoritative, Vector2 predicted, float speed)
        {
            if (begun && scripted && (!awaitProjectile || projectileReady) && !phasesArmed && !paused && (input - commandedDirection * .25f).sqrMagnitude <= .0001f)
            {
                phasesArmed = true; phaseBeganAt = Time.realtimeSinceStartupAsDouble; current.Phase = 0;
                if(awaitProjectile)enemyIds.Clear();
            }
            if (!inFrame) return;
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
            samples[sampleCount++] = new Sample { Kind = kind, Id = id, World = world, FriendlyProjectile=kind=="projectile"&&IsFriendlyProjectile(id) };
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
                        World = sample.World, Screen = camera.WorldToScreenPoint(sample.World), FriendlyProjectile=sample.FriendlyProjectile };
                    if(sample.Kind=="projectile")recordedProjectileSamples++;
                    if(sample.FriendlyProjectile)recordedFriendlyProjectileSamples++;
                }
            }
            current.CameraShake = cameraShake;
            current.CallbackEnd = Time.realtimeSinceStartupAsDouble - beganAt;
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
            if (CheckProjectileTimeout()) return;
            if (begun && !finished && scripted && !phasesArmed && (!awaitProjectile || projectileReady) && Time.realtimeSinceStartupAsDouble - (awaitProjectile?projectileReadyAt:beganAt) >= 10)
            {
                failure = "Synthetic slow drag never reached normal input sampling within ten seconds.";
                Finish(); if (quit) Application.Quit(1);
            }
            if (begun && !finished && phasesArmed && Time.realtimeSinceStartupAsDouble - phaseBeganAt >= 18)


            {
                Finish();
                if (quit) Application.Quit(failure==null?0:1);
            }
        }
        bool CheckProjectileTimeout()
        {
            if (!begun || finished || !awaitProjectile || projectileReady || System.Diagnostics.Stopwatch.GetTimestamp() < projectileDeadline) return false;
            failure = "No naturally equipped projectile appeared within 120 wall-clock seconds of normal UI play.";
            UnityEngine.Debug.LogError(failure + " Tick=" + run.Frame?.Tick + "; card click attempts=" + projectileCardClicks);
            Finish();
            if (quit) Application.Quit(1);
            return true;
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
                        if (!button.interactable || text == null) continue;
                        if(awaitProjectile && projectilePreparationTitleStage==0)
                        { if(button.name!="출정 준비")continue; projectilePreparationTitleStage=1; }
                        else if(awaitProjectile && projectilePreparationTitleStage==1)
                        { if(!text.text.Contains("씨앗 자루"))continue; projectilePreparationTitleStage=2; }
                        else if(text.text != (run.IsWave ? "시작하기" : "시작") && text.text != "개척 시작")continue;
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
            if(awaitProjectile && DriveProjectileCard())return;
            if(awaitProjectile && !projectileReady)
            {
                if(HasFriendlyProjectile())
                {
                    projectileReady=true;projectileReadyAt=Time.realtimeSinceStartupAsDouble;inputPreparationStep=0;
                    QueueMouse(stickOrigin,false);
                    UnityEngine.Debug.Log("Normal projectile setup ready at tick "+run.Frame.Tick+"; chosen cards="+projectileCardClicks);
                    return;
                }
                var angle=(float)(Time.realtimeSinceStartupAsDouble-beganAt)*.35f;
                var target=new Vector2(run.Frame.Estate.X+Mathf.Cos(angle)*850,run.Frame.Estate.Y+Mathf.Sin(angle)*850);
                var direction=(target-new Vector2(run.Frame.Lord.Position.X,run.Frame.Lord.Position.Y)).normalized;
                if(!run.Stick.Active){stickOrigin=new Vector2(Screen.width*.25f,Screen.height*.28f);QueueMouse(stickOrigin,true);}
                else QueueMouse(stickOrigin+direction*run.Stick.Radius,true);
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
        bool HasFriendlyProjectile()
        {
            if(run.Wave==null)return false;
            for(var i=0;i<run.Wave.Projectiles.Count;i++)if(!run.Wave.Projectiles[i].Hostile)return true;
            return false;
        }
        bool IsFriendlyProjectile(int id)
        {
            if(run.Wave==null)return false;
            for(var i=0;i<run.Wave.Projectiles.Count;i++)
            { var projectile=run.Wave.Projectiles[i];if(projectile.Id==id)return !projectile.Hostile; }
            return false;
        }
        bool DriveProjectileCard()
        {
            if(run.Frame==null || run.Frame.Status!=SowSiege.Core.RunStatus.AwaitingCard)
            {
                if(buttonPhase!=0){QueueMouse(pendingClick,false);buttonPhase=0;return true;}
                return false;
            }
            if(buttonPhase==0)
            {
                Button selected=null;var bestRank=int.MaxValue;
                foreach(var button in run.Ui.GetComponentsInChildren<Button>())
                {
                    if(!button.interactable||!button.name.StartsWith("Choose ",StringComparison.Ordinal))continue;
                    var id=button.name.Substring(7);
                    var rank=id=="core:seed_bag"?0:id=="core:ember_wand"?1:id=="core:rain_ladle"?2:10;
                    if(rank>=bestRank)continue;
                    selected=button;bestRank=rank;projectileSetupCard=id;
                }
                if(selected==null){QueueMouse(stickOrigin,false);return true;}
                var rect=(RectTransform)selected.transform;
                pendingClick=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
                buttonPhase=1;
            }
            if(buttonPhase==1){QueueMouse(pendingClick,false);buttonPhase=2;}
            else if(buttonPhase==2){QueueMouse(pendingClick,true);buttonPhase=3;}
            else
            {
                QueueMouse(pendingClick,false);buttonPhase=0;projectileCardClicks++;
                UnityEngine.Debug.Log("Normal projectile setup ordinary card click: "+projectileSetupCard);
            }
            return true;
        }
        void QueueMouse(Vector2 position, bool down)
            => InputSystem.QueueStateEvent(syntheticMouse, new MouseState { position = position }.WithButton(MouseButton.Left, down));
        void Finish()
        {
            if (finished || folder == null) return;
            finished = true;
            if(awaitProjectile&&recordedFriendlyProjectileSamples==0&&failure==null)failure="No friendly projectile render samples were stored during the measured phases.";
            if (syntheticMouse != null) { QueueMouse(stickOrigin, false); InputSystem.onBeforeUpdate -= DriveInput; }
            Directory.CreateDirectory(folder);
            using (var file = new StreamWriter(Path.Combine(folder, "normal-frames.csv")))
            {
                file.WriteLine("frame,unityFrame,tick,wallSeconds,unityDtSeconds,residualSeconds,phase,inputX,inputY,paused,authoritativeX,authoritativeY,predictedX,predictedY,predictionSpeed,cameraX,cameraY,cameraHalfHeight,width,height,focused,cameraShakeX,cameraShakeY,engineFrameTimeSeconds,callbackEndWallSeconds");
                for (var i = 0; i < frameCount; i++) { var r = frames[i]; file.WriteLine(FormattableString.Invariant($"{i},{r.UnityFrame},{r.Tick},{r.Wall:F9},{r.Dt:F9},{r.Residual:F9},{r.Phase},{r.Input.x:F6},{r.Input.y:F6},{r.Paused},{r.Authoritative.x:F9},{r.Authoritative.y:F9},{r.Predicted.x:F9},{r.Predicted.y:F9},{r.PredictionSpeed:F9},{r.Camera.x:F9},{r.Camera.y:F9},{r.CameraSize:F9},{r.Width},{r.Height},{r.Focused},{r.CameraShake.x:F9},{r.CameraShake.y:F9},{r.EngineFrameTime:F9},{r.CallbackEnd:F9}")); }
            }
            using (var file = new StreamWriter(Path.Combine(folder, "normal-entities.csv")))
            {
                file.WriteLine("frame,kind,id,worldX,worldY,screenX,screenY,friendlyProjectile");
                for (var i = 0; i < entityCount; i++) { var r = entities[i]; file.WriteLine(FormattableString.Invariant($"{r.Frame},{r.Kind},{r.Id},{r.World.x:F9},{r.World.y:F9},{r.Screen.x:F6},{r.Screen.y:F6},{r.FriendlyProjectile}")); }
            }
            WriteDiagnostics?.Invoke(folder);
            File.WriteAllText(Path.Combine(folder, "normal-trace.txt"), FormattableString.Invariant($"commit={BuildIdentity.Commit}\nsourceHash={BuildIdentity.SourceHash}\nprofile={CanonicalContent.ProfileName}\nsyntheticInput={scripted}\nisolatedProfile={ProfileDirectory ?? "none: existing normal profile"}\ninputPath=InputSystem Mouse state -> EventSystem -> UI/FloatingStick; not physical input\ncapture=false\nframeClocks=engineFrameTimeSeconds: Unity unscaled frame clock since startup; wallSeconds: BeginFrame realtime relative to session; callbackEndWallSeconds: EndFrame realtime relative to session; not display presentation timestamps\nprojectilePreparation={awaitProjectile}\nprojectileObserved={projectileReady}\nprojectileCardClicks={projectileCardClicks}\nrequestedSeed={RequestedSeed?.ToString(CultureInfo.InvariantCulture) ?? "normal default"}\nprojectileTimeoutClock=Stopwatch monotonic wall clock; requires a player callback\nrecordedProjectileSamples={recordedProjectileSamples}\nrecordedFriendlyProjectileSamples={recordedFriendlyProjectileSamples}\nprojectileReadyWallSeconds={projectileReadyAt-beganAt:F9}\nprewarm=false\nfailure={failure ?? "none"}\nframes={frameCount}\nentities={entityCount}\ndroppedFrames={droppedFrames}\ndroppedEntities={droppedEntities}\ncommandedDirectionX={commandedDirection.x:F6}\ncommandedDirectionY={commandedDirection.y:F6}\nphaseStartWallSeconds={phaseBeganAt - beganAt:F9}\nphasesArmed={phasesArmed}\nphase0=commanded direction 25 percent 4s; phase1=release 4s; phase2=commanded direction 100 percent 4s; phase3=release 4s; phase4=release remainder\nworldCoordinates=Unity world units; screenCoordinates=actual native pixels; stable first five enemy IDs are never replaced on death\n"));
        }
        void OnApplicationQuit() => Finish();
        void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= DriveInput;
            if (syntheticMouse != null && syntheticMouse.added) InputSystem.RemoveDevice(syntheticMouse);
        }
    }
}
