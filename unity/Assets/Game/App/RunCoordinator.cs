using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.App.Generated;
using Game.Input;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Game.Debug;
#endif
namespace Game.App
{
    public sealed partial class RunCoordinator : MonoBehaviour
    {
        public InteractiveSession Session { get; private set; }
        internal RunFrame CurrentFrame { get; private set; }
        public FirstPlayableFrame FirstPlayable { get; private set; }
        internal WaveRuntimeFrame CurrentWave { get; private set; }
        private RunFrame exportedFrameSource, exportedFrame;
        private WaveRuntimeFrame exportedWaveSource, exportedWave;
        // Public snapshots remain stable when callers retain them across buffer reuse.
        public RunFrame Frame
        {
            get
            {
                if (CurrentFrame == null) return null;
                if (!ReferenceEquals(exportedFrameSource, CurrentFrame))
                {
                    var enemies = new EnemyView[CurrentFrame.Enemies.Count];
                    for (var i = 0; i < enemies.Length; i++) enemies[i] = CurrentFrame.Enemies[i];
                    exportedFrame = CurrentFrame with { Enemies = Array.AsReadOnly(enemies) };
                    exportedFrameSource = CurrentFrame;
                }
                return exportedFrame;
            }
        }
        public WaveRuntimeFrame Wave
        {
            get
            {
                if (CurrentWave == null) return null;
                if (!ReferenceEquals(exportedWaveSource, CurrentWave))
                {
                    exportedWave = WaveSnapshotBuffer.Detach(CurrentWave);
                    exportedWaveSource = CurrentWave;
                }
                return exportedWave;
            }
        }
        public event Action<WaveRuntimeFrame> WaveFrameAccepted;
        public bool IsWave => CanonicalContent.IsWave;
        public FloatingStick Stick { get; private set; }
        public UiShell Ui { get; private set; }
        public int Speed { get; private set; } = 1;
        public bool MenuOpen { get; private set; }
        public AimMode Aim { get; private set; }
        public string Error { get; private set; }
        public string RecordedReplayPath => recording?.ReplayPath;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly DevelopmentProfilerTrace profilerTrace = new DevelopmentProfilerTrace();
#endif
        private readonly Dictionary<string,ContentDisplay> displays=new Dictionary<string,ContentDisplay>(StringComparer.Ordinal);
        private RunFrame previous;
        private InteractiveSession snapshotSession;
        private WorldRenderer world;
        private UiHud hud;
        private FirstPlayableAudio sound;
        private WaveAudio waveSound;
        private long lastWaveSound = -1;
        private RunPreferences preferences;
        public MetaProgression Progression { get; private set; }
#if UNITY_EDITOR
        public static string MetaDirectoryOverride { get; set; }
        public static string PreferencePrefixOverride { get; set; }
        public void EditorSetBorrowedSnapshotsForTests(RunFrame frame, WaveRuntimeFrame wave)
        { CurrentFrame = frame; CurrentWave = wave; }
        public void EditorCaptureSnapshotsForTests(InteractiveSession session, ContentCatalog catalog)
        { Session = session; runCatalog = catalog; CaptureSnapshots(); }
        public bool EditorHasDetachedSnapshotsForTests => exportedFrame != null || exportedWave != null;
        public bool EditorWaveExportSharesEnemyStorageForTests(WaveRuntimeFrame exported)
            => ReferenceEquals(exported.Enemies, CurrentWave.Enemies);

#endif
        private static string MetaDirectory
        {
            get
            {
                if(WaveBenchmarkAccount.Current != null)return WaveBenchmarkAccount.Current.ProfileDirectory;
                if(AutoplayCapture.Active!=null)return AutoplayCapture.Active.ProfileDirectory;
                if(FeelTrialAutomation.Active!=null)return FeelTrialAutomation.Active.ProfileDirectory;
                if(NormalPlayTrace.ProfileDirectory!=null)return NormalPlayTrace.ProfileDirectory;
                if(NormalGcProbe.ProfileDirectory!=null)return NormalGcProbe.ProfileDirectory;
#if UNITY_EDITOR
                if(!string.IsNullOrEmpty(MetaDirectoryOverride))return MetaDirectoryOverride;
#endif
                return Application.persistentDataPath;
            }
        }
        private ContentCatalog runCatalog;
        private string selectedChapter;
        private string selectedTarget;
        private MetaSettlement settlement;
        private MetaIdleResult recentIdle;
        private bool metaRun,abandoned;
        private readonly HashSet<string> recentGrowth=new HashSet<string>(StringComparer.Ordinal);
        private readonly DesktopControls desktopControls = new DesktopControls();
        private enum UiScreen { Title, Introduction, Run, Cards, Settings, Summary, Replay, Error, Chapters, Manor, Vassals, Challenges, Estate, Pause, ConfirmAbandon, ConfirmExit, Departure, CardPool, SpendConfirmation, Shop, Idle, Pass, Revive, FakeAdSettings, AdRemoval }
        private UiScreen screen, settingsOrigin;
        private RunSummary completedSummary;
        private Text activeHint;
        private FirstRunHint activeHintKind;
        private UiScreen hintScreen;
        private int hintStartedFrame;
        private float hintRemaining;
        private bool hintRecorded;

        private RunRecording recording;
        private FrameTelemetry telemetry;
        private Font font;
        private bool ownsFont;
        private double accumulator;
        private RunFrame cardsFrame;
        private int cardsWidth,cardsHeight;
        private Vector2 cardsSafeSize;
        private float cardsCanvasScale;
        private readonly Vector3[] hintCorners=new Vector3[4];
        private Text pooledHint;
        private Vector2 hintViewportSize,hintSafeSize;
        private float hintCanvasScale;
        private bool finished,loading,parityRunning,applicationPaused,focusLost,discardResumeDelta,telemetryClosed,suspendedInterval;
        private double intervalStarted;
        private int seed;
#if UNITY_EDITOR
        private bool editorCaptureActive, editorLastPause, editorLastFocus = true;
        public event Action<string,bool,bool> EditorCaptureLifecycle;
        public bool EditorCaptureFocusLost => focusLost;
        public bool EditorCaptureApplicationPaused => applicationPaused;
        public bool EditorCaptureSuspendedInterval => suspendedInterval;
        public void EditorCaptureNotifyFocus(bool focus) => OnApplicationFocus(focus);
        public void EditorCaptureNotifyPause(bool pause) => OnApplicationPause(pause);
        public void SetEditorCaptureActive(bool active)
        {
            editorCaptureActive=active;
            if(active){applicationPaused=false;focusLost=false;}
            else{applicationPaused=editorLastPause;focusLost=!editorLastFocus&&AutoplayCapture.Active==null;}
        }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private DebugOverlay debug;
        private bool invulnerable;
        private int spawnPermille=1000;
#endif
        public void Initialize()
        {
            DesktopControls.InitializeWindow();
            try
            {
                foreach(var item in CanonicalContent.Displays) displays.Add(item.Id,item);
                preferences=RunPreferences.Load(PreferencePrefix);Aim=preferences.Aim;
                { Progression=new MetaProgression(CanonicalContent.CreateMetaCatalog(),FoundationBoot.Catalog,FoundationBoot.VerifiedDataHash,MetaDirectory,
                    projectionPolicy:IsWave?MetaProjectionPolicy.AccountOnlyWave:MetaProjectionPolicy.LegacyProjectCatalog,
                    rewardDelivery:IsWave?MetaRewardDelivery.PendingClaims:MetaRewardDelivery.LegacyAutoCredit);
                selectedChapter=Progression.Catalog.Chapters.OrderBy(c=>c.Index).First().Id;
                recentIdle=Progression.LastIdle;if(recentIdle!=null)recentGrowth.UnionWith(recentIdle.BuildingsGrown); }
                InitializeRewardedAds();
                var fontCorpus=displays.Values.SelectMany(d=>new[]{d.DisplayName,d.EffectDescription,d.GrowthDescription,d.EvolutionHint}).ToList();
                fontCorpus.Add("씨앗과 공성 새싹 변경의 한 해 적을 물리치고 영지를 키우세요 개척을 시작하기 전에 화면을 끌어 이동합니다 무기와 도구는 자동으로 작동합니다 카드를 골라 전투와 영지의 성장을 함께 준비하세요 개척 시작 시작 설정 돌아가기 배경음 효과음 진동 화면 흔들림 피해 숫자 자동 조준 이동 방향 가까운 적 소리와 화면 판을 잠시 멈췄습니다");
                fontCorpus.Add(MetaScreens.Glyphs);
                if(IsWave){fontCorpus.Add(PortraitGlyphs);fontCorpus.Add(PortraitMetaPanels.Glyphs);fontCorpus.Add(PortraitMetaGlyphs);fontCorpus.Add(PortraitBmPanels.Glyphs);fontCorpus.Add(BmGlyphs);}
                fontCorpus.Add("새싹 들판 도구의 흔적을 키워 수확하세요 목표 재료 초반 도구 선택에 등장합니다 곡물 목재 특허장 선택");
                if(IsWave)fontCorpus.Add(FallowTitle.Glyphs);
                { fontCorpus.AddRange(Progression.Catalog.Chapters.SelectMany(c=>new[]{c.Name,c.Description}));
                fontCorpus.AddRange(Progression.Catalog.ManorBuildings.SelectMany(c=>new[]{c.Name,c.Description}));
                fontCorpus.AddRange(Progression.Catalog.Vassals.SelectMany(c=>new[]{c.Name,c.Description}));
                fontCorpus.AddRange(Progression.Catalog.Challenges.Select(c=>c.Name)); }
                foreach(FirstRunHint value in Enum.GetValues(typeof(FirstRunHint)))fontCorpus.Add(HintText(value));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                fontCorpus.Add(DebugOverlay.RequiredGlyphs);
#endif
                font=FontProvider.Create(fontCorpus);ownsFont=true;
                var uiObject=new GameObject("Game UI",typeof(RectTransform));uiObject.transform.SetParent(transform,false);Ui=uiObject.AddComponent<UiShell>();Ui.Initialize(font,IsWave);Stick=Ui.TouchSurface.gameObject.AddComponent<FloatingStick>();Stick.RadiusCanvasUnits=Ui.MovementRadiusCanvasUnits;
                var events=new GameObject("UI events");events.transform.SetParent(transform,false);events.AddComponent<EventSystem>();events.AddComponent<InputSystemUIInputModule>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                debug=gameObject.AddComponent<DebugOverlay>();debug.Initialize(font,DebugAction);debug.SetColors(GamePalette.Panel,GamePalette.Text,GamePalette.Button,GamePalette.ButtonText);Stick.ExtraBlocker=debug.BlocksPointer;
#endif
                gameObject.AddComponent<AudioListener>();sound=gameObject.AddComponent<FirstPlayableAudio>();sound.Initialize(FoundationBoot.Catalog);if(IsWave){waveSound=gameObject.AddComponent<WaveAudio>();waveSound.Initialize();}ApplyPreferences();
                ShowMeta();
                LandscapeUiReview.Create(this);
                TryStartVisualReview();
                if(!visualReviewRequested)TryStartWaveBenchmark();
            }
            catch(Exception e){Fail(e);}
        }
        public void ShowMeta()
        {
            screen=UiScreen.Title;MenuOpen=true;Stick.ResetStick();ClearUi();hud=null;
            if(Ui.PortraitEnabled){ShowPortraitTitle();return;}
            if(IsWave&&FoundationBoot.Catalog.WaveRuntime?.ChapterId=="meta:chapter_1")
            {
                FallowTitle.Show(Ui,selectedTarget,FoundationBoot.Catalog.WaveRuntime.MaterialTargets,
                    id=>displays[id].DisplayName,material=>{selectedTarget=material;ShowMeta();},
                    ()=>{if(preferences.IntroductionSeen)StartRun();else ShowIntroduction();},()=>ShowSettings(false));
                return;
            }
            var panel=Ui.Panel("Meta");Ui.Label(panel,"씨앗과 공성",UiTokens.Title,72);
            if(IsWave)
            {
                Ui.Label(panel,"새싹 들판 · 도구의 흔적을 키워 수확하세요",UiTokens.Body,96);
                Ui.Label(panel,selectedTarget==null?"목표 재료 미선택 · 아래에서 골라 주세요":"목표 재료 선택됨 · 초반 도구 선택에 등장합니다",UiTokens.Small,48);
                foreach(var target in FoundationBoot.Catalog.WaveRuntime.MaterialTargets)
                {
                    var material=target.Key;var tool=target.Value;
                    var materialName=material=="meta:grain"?"곡물":material=="meta:timber"?"목재":material=="meta:charter"?"특허장":material;
                    Ui.Button(panel,(selectedTarget==material?"선택됨 · ":"")+materialName+" · "+displays[tool].DisplayName,()=>{selectedTarget=material;ShowMeta();});
                }
                Ui.Button(panel,"시작",()=>{if(preferences.IntroductionSeen)StartRun();else ShowIntroduction();});
                Ui.Button(panel,"설정",()=>ShowSettings(false));return;
            }
            Ui.Label(panel,"새싹 변경의 한 해\n적을 물리치고 영지를 키우세요.",UiTokens.Body,96);
            try
            {
                var idle=Progression.AdvanceIdle();recentGrowth.UnionWith(idle.BuildingsGrown);
                if(idle.CreditedSeconds>0||recentIdle==null)recentIdle=idle;
            }
            catch(Exception e){Fail(e);return;}
            MetaScreens.Wallet(Ui,panel,Progression.Catalog,Progression.State);
            if(recentIdle!=null&&recentIdle.Awarded.Values.Any(v=>v>0))Ui.Label(panel,"장원 생산 · "+MetaScreens.Amounts(Progression.Catalog,recentIdle.Awarded)+$" · {recentIdle.CreditedSeconds/60}분",UiTokens.Small,UiTokens.TouchHeight);
            Ui.Button(panel,"시작",()=>{if(preferences.IntroductionSeen)StartRun();else ShowIntroduction();});
            Ui.Button(panel,"변경 지도",ShowChapters);
            Ui.Button(panel,"장원",ShowManor);
            Ui.Button(panel,"가신",ShowVassals);
            Ui.Button(panel,"도전",ShowChallenges);
            if(Progression.RecoveredAbandonedRun)Ui.Label(panel,"중단된 출정을 포기로 정산했습니다.",UiTokens.Small,UiTokens.TouchHeight);
            if(Progression.RecoveredTerminalRun)Ui.Label(panel,"지난 출정의 정산을 복구했습니다.",UiTokens.Small,UiTokens.TouchHeight);
            Ui.Button(panel,"설정",()=>ShowSettings(false));
        }
        private void MetaPage(UiScreen next,Action draw)
        {
            screen=next;MenuOpen=true;Stick.ResetStick();ClearUi();hud=null;draw();
        }
        private void MetaAction(Action action,Action refresh)
        {
            try{var before=Progression.State.ManorLevels;action();foreach(var building in Progression.State.ManorLevels)if(building.Value>before[building.Key])recentGrowth.Add(building.Key);refresh();}catch(Exception e){Fail(e);}
        }
        private void ShowChapters()=>MetaPage(UiScreen.Chapters,()=>MetaScreens.Chapters(Ui,Progression.Catalog,Progression.State,id=>{selectedChapter=id;if(preferences.IntroductionSeen)StartRun();else ShowIntroduction();},ShowMeta));
        private void ShowManor()=>MetaPage(UiScreen.Manor,()=>MetaScreens.Manor(Ui,Progression.Catalog,Progression.State,id=>MetaAction(()=>Progression.SetManorPriority(id),ShowManor),ShowMeta,recentGrowth));
        private void ShowVassals()=>MetaPage(UiScreen.Vassals,()=>MetaScreens.Vassals(Ui,Progression.Catalog,Progression.State,id=>MetaAction(()=>Progression.UpgradeVassal(id),ShowVassals),id=>MetaAction(()=>Progression.RankUpVassal(id),ShowVassals),id=>MetaAction(()=>Progression.ToggleVassal(id),ShowVassals),ShowMeta));
        private void ShowChallenges()=>MetaPage(UiScreen.Challenges,()=>MetaScreens.Challenges(Ui,Progression.Catalog,Progression.State,displays,ShowMeta));
        private void ShowIntroduction()
        {
            screen=UiScreen.Introduction;MenuOpen=true;ClearUi();hud=null;
            var panel=Ui.Panel("Introduction");Ui.Label(panel,"개척을 시작하기 전에",UiTokens.Title,72);
            Ui.Label(panel,"화면을 끌어 이동합니다.\n무기와 도구는 자동으로 작동합니다.\n카드를 골라 전투와 영지의 성장을 함께 준비하세요.",UiTokens.Body,180);
            Ui.Button(panel,"개척 시작",()=>{preferences.IntroductionSeen=true;preferences.Save();StartRun();});
            Ui.Button(panel,"돌아가기",ShowMeta);
        }
        public void StartRun(int? requestedSeed=null)
        {
            if(loading)return;if(Session?.View.Status==RunStatus.Completed&&!finished){RetryCompleteRun();return;}StartCoroutine(BeginRun(requestedSeed ?? AutoplayCapture.Active?.RunSeed ?? (FeelTrialAutomation.Active != null ? FeelTrialOptions.Seed : (int?)null) ?? NormalPlayTrace.RequestedSeed ?? NormalGcProbe.RequestedSeed ?? (int)(DateTime.UtcNow.Ticks & int.MaxValue)));
        }
        public void StartNeutralRun(int requestedSeed)
        {
            if(loading)return;if(Session?.View.Status==RunStatus.Completed&&!finished){RetryCompleteRun();return;}StartCoroutine(BeginRun(requestedSeed,false));
        }
        private IEnumerator BeginRun(int nextSeed,bool useMeta=true)
        {
            loading=true; useMeta = useMeta && Progression!=null;
            try
            {
                if(metaRun&&Session!=null&&!finished&&Progression.State.PendingRun!=null)SettleAccountRun(true);
                if(useMeta&&IsWave&&!CanDepart(selectedChapter))throw new InvalidOperationException("출정할 수 없는 지역입니다.");
                StopRecording();Stick.ResetStick();
            }
            catch(Exception e){loading=false;Fail(e);yield break;}
            yield return SceneManager.LoadSceneAsync("Run");
            try
            {
                seed=nextSeed;var started=useMeta?Progression.BeginRun(selectedChapter,seed):null;var c=started?.Catalog??FoundationBoot.Catalog;runCatalog=c;settlement=null;resultFragments.Clear();metaRun=useMeta;abandoned=false;
                SnapshotFeelPreferences();
                var options=new InteractiveOptions(new RunOptions(seed,c.Tuning.DefaultHero,c.Tuning.DefaultEstate,"mixed",ManualCards:true,TargetMaterial:IsWave?selectedTarget:null),Aim,FoundationBoot.VerifiedDataHash,activeFeelRules,DepartureFieldOptions(),DepartureHostRewards(useMeta),MeleeAim:CatalogRunOptions.MeleeAim(c));
                Session=new InteractiveSession(c,options);guardedOffer=null;portraitCardActions=null;CaptureSnapshots();previous=CurrentFrame;accumulator=0;finished=false;telemetryClosed=false;suspendedInterval=false;intervalStarted=0;Speed=1;Error=null;cardsFrame=null;MenuOpen=false;completedSummary=null;
                recording=new RunRecording(MetaDirectory,options,BuildIdentity.Commit);if(started?.ReplayContext!=null)System.IO.File.WriteAllBytes(recording.ReplayPath+".meta",started.ReplayContext);var device=DeviceFacts.Capture();device.sourceHash=BuildIdentity.SourceHash;device.sourceDirty=BuildIdentity.SourceDirty;telemetry=new FrameTelemetry(recording.DirectoryPath,recording.SessionId,BuildIdentity.Commit,CanonicalContent.DataHash,CurrentFrame.DurationTicks,device);
                world=new GameObject("World renderer").AddComponent<WorldRenderer>();var settings=CanonicalContent.Presentation.Camera;
                world.SetFallowChapter(IsWave&&c.WaveRuntime?.ChapterId=="meta:chapter_1");
                world.MusterPresentationChanged=(source,formation)=>UnityEngine.Debug.Log($"[muster-presentation] tick={CurrentFrame.Tick} source={source} path={formation.Path} evolution={formation.EvolutionId??"none"} ratio={formation.Ratio:0.###} soldiers={formation.Soldiers} scale={formation.Scale:0.###}");
                world.Initialize(Camera.main,new WorldCameraSettings(settings.WorldUnitsPerUnityUnit,settings.MinHalfHeight,settings.MaxHalfHeight,settings.EstatePadding,settings.FollowMilliseconds,settings.ZoomMilliseconds),c.Tuning.DefaultEstate,CurrentFrame.MapWidth,CurrentFrame.MapHeight);if(useMeta&&!IsWave)world.SetChapterTerrain(Progression.Catalog.Chapters.Single(ch=>ch.Id==selectedChapter).Terrain);world.AcceptWave(c,CurrentWave);world.AcceptFrame(CurrentFrame,FirstPlayable);ApplyPreferences();sound.ResetRun();waveSound?.ResetRun();bossPhaseCue.Reset();lastWaveSound=-1;if(IsWave)sound.AcceptCommonFeedback(CurrentFrame,waveSound.PlayCommon);else sound.AcceptFrame(CurrentFrame,FirstPlayable);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                invulnerable=false;spawnPermille=1000;
#endif
                BeginRewardedRun();
                InitializeSmoothness();
                if(Ui.PortraitEnabled){preferences.IntroductionSeen=true;preferences.Save();}
                ShowHud();
            }
            catch(Exception e){Fail(e);}
            loading=false;
            if(Error==null)AutoplayCapture.Active?.OnRunReady(this);
        }
        private void ClearUi(){desktopControls.Reset();ReleaseHint();Ui.Clear();}
        private void ShowHud(){screen=UiScreen.Run;ClearUi();hud=new UiHud(Ui,runCatalog??FoundationBoot.Catalog,()=>{if(Ui.PortraitEnabled)OpenPause();else ShowSettings(true);},activeHitFeedback);hud.Present(CurrentFrame,FirstPlayable);MenuOpen=false;cardsFrame=null;Stick.ResetStick();}
        // Read-only diagnostic view; does not alter lifecycle or the simulation clock.
        public bool CaptureClockPaused => Paused;
        private bool Paused => MenuOpen || loading || parityRunning || applicationPaused || focusLost || Session==null || Session.View.Status!=RunStatus.Running
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            || debug!=null && debug.IsOpen
#endif
            ;
        private void UpdateRunFrame()
        {
            if (visualReviewRequested) return;
            if (waveBenchmarkRequested) { UpdateWaveBenchmark(); return; }
            if(Ui==null||Stick==null)return;
            DesktopControls.UpdateWindow();
            Ui.SynchronizeViewport();
            UpdatePortraitNavigation();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            profilerTrace.Pump(CurrentFrame?.Tick ?? 0);
            profilerTrace.StartScheduled(recording, CurrentFrame?.Tick ?? 0, CanonicalContent.DataHash,
                recording != null && Session != null && !finished && !parityRunning && !Paused && Speed == 1 && Session.View.Status == RunStatus.Running);
            if(UnityEngine.InputSystem.Keyboard.current?.f12Key.wasPressedThisFrame==true)debug?.SetOpen(!debug.IsOpen);
#endif
            if(telemetry!=null&&!telemetryClosed)
            {
                using var allocationScope=smoothness.Scope(SmoothnessScope.Telemetry);
                using var telemetryScope=RunProfilerMarkers.Telemetry.Auto();
                telemetry.CompleteInterval(Time.unscaledDeltaTime,suspendedInterval);suspendedInterval=false;intervalStarted=0;
                if(finished){telemetry.Finish();telemetryClosed=true;}
            }
            var pausedAtFrameStart=Paused;var speedAtFrameStart=Speed;
            Stick.Blocked=Paused;if(Paused){Stick.ResetStick();accumulator=0;}
            var movement=desktopControls.Sample(Stick,Paused);
            var impactComparison=AutoplayCapture.Active?.ImpactComparison;
            if(impactComparison!=null&&CurrentFrame!=null&&!Paused)
                movement=DesktopControls.ResolveMovement(default,false,impactComparison.Direction(CurrentFrame.Tick,CurrentFrame.TickRate),false);
            smoothness.InputSample(PresentationInput(movement));
            Ui.ShowStick(Stick.Active,Stick.Origin,Stick.Offset);
            sound?.SetPaused(applicationPaused||focusLost);waveSound?.SetPaused(applicationPaused||focusLost);
            if(Session==null||loading||Error!=null)return;
            if(!Paused)
            {
                if(discardResumeDelta)discardResumeDelta=false;else accumulator+=Time.unscaledDeltaTime*Speed;
                var step=1d/CurrentFrame.TickRate;
                var advanceBudget=new FrameAdvanceBudget(Speed==1);
                while(Session.View.Status==RunStatus.Running&&advanceBudget.TryAdvance(ref accumulator,step))Send(ReplayCommandKind.Advance,movement);
            }
            BeginLongFrameHud();
            var probeHudStart=ProbeEnabled?ProbeTimestamp():0;
            using(RunProfilerMarkers.Hud.Auto())
            using(smoothness.Scope(SmoothnessScope.Hud))
            {
                if(Session.View.Status==RunStatus.AwaitingCard&&(screen==UiScreen.Run||screen==UiScreen.Cards))ShowCards();
                hud?.Present(CurrentFrame,FirstPlayable);RefreshPortraitCardGate();UpdateHints();
            }
            if(ProbeEnabled)probeHudMs+=ProbeElapsed(probeHudStart);
            EndLongFrameHud();
            if(world!=null)
            {
                if(Ui.PortraitEnabled)world.SetPresentationViewport(Ui.PortraitPixels);
                var visible=Ui.PortraitEnabled?Ui.SafePixels:Screen.safeArea;
                if(hud!=null)
                {
                    if(Ui.PortraitEnabled||(IsWave&&runCatalog.WaveRuntime?.ChapterId=="meta:chapter_1"))
                    {
                        var overlays=Ui.GetWorldOverlayBounds();
                        visible=Rect.MinMaxRect(Mathf.Max(visible.xMin,overlays.xMin),Mathf.Max(visible.yMin,overlays.yMin),Mathf.Min(visible.xMax,overlays.xMax),Mathf.Min(visible.yMax,overlays.yMax));
                    }
                    else
                    {
                        var bottom=Mathf.Min(UiTokens.BottomWorldInset*Ui.Canvas.scaleFactor,visible.height*.2f);
                        visible.yMin+=bottom;visible.yMax=Mathf.Max(visible.yMin+1,visible.yMax-hud.ReservedTopPixels);
                    }
                }
                world.ShowAnnouncements=screen==UiScreen.Run;
                world.ShowTransientText=screen!=UiScreen.Revive;
                PredictLord(movement);
                var probeDrawStart=ProbeEnabled?ProbeTimestamp():0;
                world.HeroLocomotionPerTick=(float)(Session?.LocomotionSpeedPerTick??0);
                using(RunProfilerMarkers.WorldPresent.Auto())
                using(smoothness.Scope(SmoothnessScope.World)) PresentWorld(renderInterpolation.Present(CurrentFrame.Tick,(float)(accumulator*CurrentFrame.TickRate),Paused,Time.unscaledDeltaTime,CurrentFrame.TickRate),Time.unscaledDeltaTime,visible);
                LongFrameViewPresented();
                if(ProbeEnabled)probeDrawMs+=ProbeElapsed(probeDrawStart);
                smoothness.Submit(Camera.main,CurrentFrame.Tick,Paused,lordPrediction.ResetThisFrame,lordPrediction.Speed);
            }
            if(!finished&&telemetry!=null)
            {
                using var allocationScope=smoothness.Scope(SmoothnessScope.Telemetry);
                using var telemetryScope=RunProfilerMarkers.Telemetry.Auto();
                telemetry.BeginInterval(CurrentFrame,speedAtFrameStart,world!=null?world.ActiveVisualProjectiles:0,new Rect(0,0,Screen.width,Screen.height),Screen.safeArea,pausedAtFrameStart);
                intervalStarted=Time.realtimeSinceStartupAsDouble;
            }
            if(Session.View.Status==RunStatus.Completed&&!finished){try{CompleteRun();}catch(Exception e){Fail(e);}}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debug?.Present(new DebugSnapshot(seed,CurrentFrame,Time.unscaledDeltaTime*1000,CanonicalContent.DataHash,Speed,invulnerable,spawnPermille,Aim,world!=null?world.ActiveVisualProjectiles:0,world!=null?world.DroppedEffects:0,world!=null?world.UnsupportedShapeCount:0));
#endif
        }
        public void Send(ReplayCommandKind kind,PlayerInput input=default,string card=null,int value=0)
        {
            if(Session==null||finished)return;
            try
            {
                if(kind==ReplayCommandKind.Advance&&AutoplayCapture.Active?.ImpactComparison is ImpactComparisonCapture comparison)
                    input=DesktopControls.ResolveMovement(default,false,comparison.Direction(CurrentFrame.Tick,CurrentFrame.TickRate),false);
                var command=new ReplayCommand(Session.NextSequence,CurrentFrame.Tick,kind,input,card,value);
                var probeCoreStart=ProbeEnabled?ProbeTimestamp():0;
                BeginLongFrameCommand(kind);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                try
                {
#endif
                    using(RunProfilerMarkers.CoreApply.Auto())
                    using(smoothness.Scope(SmoothnessScope.CoreApply))Session.Apply(command);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                }
                finally { EndLongFrameCommand(kind); }
#endif
                if(ProbeEnabled&&kind==ReplayCommandKind.Advance){probeCoreMs+=ProbeElapsed(probeCoreStart);probeCoreTicks++;}
                using(smoothness.Scope(SmoothnessScope.Recording))recording.WriteAccepted(command);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if(kind==ReplayCommandKind.SetInvulnerable)invulnerable=value!=0;
                if(kind==ReplayCommandKind.SetSpawnPermille)spawnPermille=value;
#endif
                if(kind==ReplayCommandKind.SetAimMode)Aim=(AimMode)value;
                previous=CurrentFrame;CaptureSnapshots();
                if(ProbeEnabled&&kind==ReplayCommandKind.Advance&&CurrentFrame.Field==null)
                {
                    var p=CurrentFrame.Lord.Position;
                    probeLordBlocked|=(input.X<0&&p.X<=0)||(input.X>0&&p.X>=CurrentFrame.MapWidth)||(input.Y<0&&p.Y<=0)||(input.Y>0&&p.Y>=CurrentFrame.MapHeight);
                }
                if(kind==ReplayCommandKind.Advance)ObserveLordMovement(input);
                using(RunProfilerMarkers.AcceptPresentation.Auto())
                using(smoothness.Scope(SmoothnessScope.AcceptPresentation)){AcceptCurrentWorldPresentation();if(!IsWave)sound?.AcceptFrame(CurrentFrame,FirstPlayable);}
                if(kind!=ReplayCommandKind.Advance){Stick.ResetStick();accumulator=0;cardsFrame=null;}
                if(kind==ReplayCommandKind.Advance && CurrentFrame.Tick%1800==0 && CurrentFrame.Status!=RunStatus.Completed)recording.Checkpoint(Session);
                if(CurrentFrame.Status==RunStatus.Running&&MenuOpen&&kind==ReplayCommandKind.ChooseCard)ShowHud();
            }
            catch(Exception e){Fail(e);}
        }
        private void CaptureSnapshots()
        {
            BeginLongFrameSnapshot();
            var probeSnapshotStart=ProbeEnabled?ProbeTimestamp():0;
            using var allocationScope=smoothness.Scope(SmoothnessScope.CoreSnapshot);
            if(!ReferenceEquals(snapshotSession,Session))
            {
                snapshotSession=Session;
                var capacity=runCatalog.Tuning.World.Threat.EnemyCap;
                bossPresentation=runCatalog.WaveRuntime==null?null:new BossPresentationTimeline(capacity,DeliverBossPresentation);
                bossPresentationWorld=null;
                bossPresentation?.WarmStorage((IBufferedRunView)Session.View,(IBufferedWaveRunView)Session.View);
            }
            var presentationSlot=bossPresentation?.PrepareCapture();
            RunFrame frame;FirstPlayableFrame firstPlayable;
            using(RunProfilerMarkers.CaptureRun.Auto())frame=presentationSlot==null?Session.View.CaptureFrame():((IBufferedRunView)Session.View).CaptureFrame(presentationSlot.RunBuffer);
            using(RunProfilerMarkers.CaptureFirstPlayable.Auto())firstPlayable=Session.View.CaptureFirstPlayable();
            CurrentWave=presentationSlot==null?(Session.View as IWaveRunView)?.CaptureWaveRuntime():((IBufferedWaveRunView)Session.View).CaptureWaveRuntime(presentationSlot.WaveBuffer);
            CurrentFrame=frame;FirstPlayable=firstPlayable ?? WavePresentation.Envelope(runCatalog,frame,Session.View.CaptureCards(),CurrentWave);
            if(presentationSlot!=null)bossPresentation.Commit(presentationSlot,frame,CurrentWave,FirstPlayable);
            var subscribers=WaveFrameAccepted;
            if(CurrentWave!=null&&subscribers!=null) subscribers(Wave);
            if(ProbeEnabled)probeSnapshotMs+=ProbeElapsed(probeSnapshotStart);
            EndLongFrameSnapshot();
        }
        private void ShowCards()
        {
            Ui.SynchronizeViewport();
            if(ReferenceEquals(cardsFrame,CurrentFrame)&&cardsWidth==Screen.width&&cardsHeight==Screen.height&&cardsSafeSize==Ui.SafeRoot.rect.size&&cardsCanvasScale==Ui.Canvas.scaleFactor&&screen==UiScreen.Cards)return;
            cardsSafeSize=Ui.SafeRoot.rect.size;cardsCanvasScale=Ui.Canvas.scaleFactor;
            var offer=Session.View.CaptureCards();
            cardsFrame=CurrentFrame;cardsWidth=Screen.width;cardsHeight=Screen.height;screen=UiScreen.Cards;MenuOpen=true;Stick.ResetStick();accumulator=0;ClearUi();hud=null;
            if(Ui.PortraitEnabled){ShowPortraitCards(offer);return;}
            UiRunPanels.Cards(Ui,CurrentFrame,FirstPlayable,offer,runCatalog??FoundationBoot.Catalog,displays,(kind,id)=>Send(kind,card:id),()=>ShowSettings(true));
            var scroll=Ui.Content.GetComponentInChildren<ScrollRect>();
            if(scroll!=null)Ui.Button(scroll.content,"설정",()=>ShowSettings(true));
        }
        private void ShowSettings(bool inRun)
        {
            if(screen!=UiScreen.Settings)settingsOrigin=screen;
            screen=UiScreen.Settings;MenuOpen=true;Stick.ResetStick();accumulator=0;ClearUi();hud=null;
            if(Ui.PortraitEnabled){PortraitRunPanels.Settings(Ui,preferences,Aim,()=>ApplyPreferences(true),value=>SetAim(value,inRun),RestoreSettingsOrigin,fakeAds:FakeAdSettingsAction);return;}
            var panel=Ui.Panel("Settings");Ui.Label(panel,"설정",UiTokens.Title,72);
            Ui.Label(panel,inRun&&!finished?"판을 잠시 멈췄습니다":"소리와 화면",UiTokens.Caption,40);
            Ui.Slider(panel,"배경음",preferences.MusicVolume,v=>{preferences.MusicVolume=v;ApplyPreferences(true);});
            Ui.Slider(panel,"효과음",preferences.EffectsVolume,v=>{preferences.EffectsVolume=v;ApplyPreferences(true);});
            Ui.Toggle(panel,"진동",preferences.Haptics,v=>{preferences.Haptics=v;ApplyPreferences(true);});
            Ui.Toggle(panel,"화면 흔들림",preferences.Shake,v=>{preferences.Shake=v;ApplyPreferences(true);});
            Ui.Toggle(panel,"피해 숫자",preferences.DamageNumbers,v=>{preferences.DamageNumbers=v;ApplyPreferences(true);});
            AddFeelSettings(panel);
            Ui.Label(panel,"자동 조준",UiTokens.Heading,48);
            Ui.Button(panel,(Aim==AimMode.Movement?"✓ ":"")+"이동 방향",()=>SetAim(AimMode.Movement,inRun));
            Ui.Button(panel,(Aim==AimMode.NearestEnemy?"✓ ":"")+"가까운 적 자동 조준",()=>SetAim(AimMode.NearestEnemy,inRun));
            Ui.Button(panel,"돌아가기",RestoreSettingsOrigin);
            if(inRun&&!finished)Ui.Button(panel,"출정을 포기하고 정산",AbandonCurrentRun);
        }
        private void SetAim(AimMode aim,bool inRun)
        {
            if(inRun&&Session!=null&&!finished)Send(ReplayCommandKind.SetAimMode,value:(int)aim);
            else Aim=aim;
            preferences.Aim=Aim;preferences.Save();ShowSettings(inRun);
        }
        private void RestoreSettingsOrigin()
        {
            if(Ui.PortraitEnabled){RestorePortraitSettings();return;}
            if(settingsOrigin==UiScreen.Summary){ShowSummary();return;}
            if(settingsOrigin==UiScreen.Introduction){ShowIntroduction();return;}
            if(settingsOrigin==UiScreen.Title){ShowMeta();return;}
            if(CurrentFrame?.Status==RunStatus.AwaitingCard){cardsFrame=null;ShowCards();return;}
            ShowHud();
        }
        private void ApplyPreferences(bool save=false)
        {
            ConfigureHeroMotionComparison();
            var impactEnabled=AutoplayCapture.Active?.ImpactComparison?.Enabled??!benchmarkImpactBaseline;
            if(world!=null){world.ImpactFeedbackEnabled=impactEnabled;world.ShakeEnabled=preferences.Shake;world.ShowDamageNumbers=preferences.DamageNumbers;world.HitFeedbackEnabled=activeHitFeedback;world.HeroRigPreviewEnabled=activeHeroRig;world.HeroRigExplicit=explicitHeroRig;}
            sound?.SetImpactFeedback(impactEnabled);waveSound?.SetImpactFeedback(impactEnabled);
            sound?.SetPreferences(preferences.MusicVolume,preferences.EffectsVolume,preferences.Haptics);waveSound?.SetVolume(preferences.EffectsVolume);
            if(save)preferences.Save();
        }
        private void CompleteRun()
        {
            if(TryDeferForRevive())return;
            MenuOpen=true;Stick.ResetStick();completedSummary=Session.GetSummary();
            if(metaRun&&settlement==null)settlement=SettleAccountRun(false);
            recording.Finish(Session,completedSummary.Survived?ReplayEndKind.Duration:ReplayEndKind.Death);
            finished=true;Error=null;ShowSummary();StopDiagnosticTrace("run-complete");
        }
        private void RetryCompleteRun()
        {
            try{CompleteRun();}catch(Exception e){Fail(e);}
        }
        private void AbandonCurrentRun()
        {
            try
            {
                completedSummary=Session.GetSummary();
                if(metaRun)settlement=SettleAccountRun(true);
                recording.Finish(Session,ReplayEndKind.Quit);finished=true;abandoned=true;ShowSummary();
            }
            catch(Exception e){Fail(e);}
        }
        private void ShowSummary()
        {
            screen=UiScreen.Summary;MenuOpen=true;ClearUi();hud=null;
            if(Ui.PortraitEnabled){ShowPortraitSummary();return;}
            UiRunPanels.Summary(Ui,completedSummary,CurrentFrame,FirstPlayable,displays,()=>StartRun(),()=>StartCoroutine(ReturnMeta()),abandoned,IsWave);
            if(settlement!=null)recentGrowth.UnionWith(settlement.BuildingsGrown);
            if(settlement!=null)MetaScreens.Settlement(Ui,Ui.Content.GetComponentInChildren<ScrollRect>().content,Progression.Catalog,settlement);
            Ui.Button(Ui.Content.GetComponentInChildren<ScrollRect>().content,"설정",()=>ShowSettings(false));
        }
        private static string HintText(FirstRunHint hint)=>hint switch
        {
            FirstRunHint.Move=>"화면 빈 곳을 끌어 이동하세요. 공격은 자동입니다.",
            FirstRunHint.Card=>"무기와 도구를 함께 골라 전투와 영지를 키우세요.",
            FirstRunHint.Harvest=>"황금빛으로 익은 밭에 다가가 수확하세요.",
            FirstRunHint.Muster=>"징집된 백성이 적과 싸웁니다. 다시 일터로 돌아오는 것도 지켜보세요.",
            _=>throw new ArgumentOutOfRangeException(nameof(hint))
        };
        private void UpdateHints()
        {
            if(activeHint!=null)
            {
                if(Ui.PortraitEnabled&&!PortraitHintFits){ReleaseHint();return;}
                if(screen!=hintScreen){ReleaseHint();return;}
                if(hintViewportSize!=new Vector2(Screen.width,Screen.height)||hintSafeSize!=Ui.SafeRoot.rect.size||hintCanvasScale!=Ui.Canvas.scaleFactor)LayoutActiveHint();
                if(!hintRecorded&&!applicationPaused&&!focusLost&&Time.frameCount>hintStartedFrame&&activeHint.gameObject.activeInHierarchy&&!activeHint.canvasRenderer.cull)
                {
                    activeHint.rectTransform.GetWorldCorners(hintCorners);
                    var bounds=new Rect(hintCorners[0],hintCorners[2]-hintCorners[0]);
                    if(bounds.Overlaps(Screen.safeArea)){preferences.MarkHintSeen(activeHintKind);hintRecorded=true;}
                }
                hintRemaining-=Time.unscaledDeltaTime;
                if(hintRemaining<=0)ReleaseHint();
                return;
            }
            FirstRunHint? next=null;
            if(!Ui.PortraitEnabled&&screen==UiScreen.Cards&&!preferences.HasSeen(FirstRunHint.Card))next=FirstRunHint.Card;
            else if(screen==UiScreen.Run)
            {
                if(!preferences.HasSeen(FirstRunHint.Move))next=FirstRunHint.Move;
                else if(!preferences.HasSeen(FirstRunHint.Harvest)&&HasRipeFarm())next=FirstRunHint.Harvest;
                else if(!preferences.HasSeen(FirstRunHint.Muster)&&HasMuster())next=FirstRunHint.Muster;
            }
            if(!next.HasValue||applicationPaused||focusLost||(Ui.PortraitEnabled&&!PortraitHintFits))return;
            activeHintKind=next.Value;hintScreen=screen;hintRecorded=false;hintStartedFrame=Time.frameCount;hintRemaining=5;
            if(pooledHint==null)pooledHint=Ui.Hint(HintText(activeHintKind));
            activeHint=pooledHint;activeHint.text=HintText(activeHintKind);
            LayoutActiveHint();
        }
        private void LayoutActiveHint()
        {
            if(Ui.PortraitEnabled)LayoutPortraitHint();
            else UiRunPanels.LayoutHint(Ui,activeHint,screen==UiScreen.Cards,Screen.width>Screen.height);
            hintViewportSize=new Vector2(Screen.width,Screen.height);hintSafeSize=Ui.SafeRoot.rect.size;hintCanvasScale=Ui.Canvas.scaleFactor;
        }
        private bool HasRipeFarm()
        {for(var i=0;i<CurrentFrame.Farms.Count;i++)if(CurrentFrame.Farms[i].Ripe)return true;return false;}
        private bool HasMuster()
        {for(var i=0;i<FirstPlayable.People.Count;i++)if(FirstPlayable.People[i].Activity=="muster")return true;return false;}
        private void ReleaseHint()
        {
            if(pooledHint!=null){var container=pooledHint.transform.parent;container.SetParent(Ui.SafeRoot,false);container.gameObject.SetActive(false);}
            activeHint=null;
        }
        private IEnumerator ReturnMeta(){if(metaRun&&Session!=null&&!finished){settlement=SettleAccountRun(true);}StopRecording();Session=null;CurrentFrame=null;FirstPlayable=null;yield return SceneManager.LoadSceneAsync("Meta");if(Ui.PortraitEnabled)ShowPortraitEstate();else ShowMeta();}
        private void StopRecording(){try{using var scope=RunProfilerMarkers.Telemetry.Auto();if(recording!=null){if(Session!=null&&!finished&&Session.View.Status!=RunStatus.Completed)recording.Finish(Session,ReplayEndKind.Quit);recording.Dispose();recording=null;}if(telemetry!=null&&!telemetryClosed){if(intervalStarted>0)telemetry.CompleteInterval((float)(Time.realtimeSinceStartupAsDouble-intervalStarted),suspendedInterval,true);telemetry.Finish();}telemetry?.Dispose();telemetry=null;telemetryClosed=true;intervalStarted=0;}finally{StopDiagnosticTrace("recording-closed");}}
        private void OnApplicationPause(bool pause)
        {
            LongFrameLifecycle(false, pause);
            if (offlineReplayCapture != null) return;
            if (waveBenchmarkRequested) { BenchmarkLifecycle(false, pause); return; }
#if UNITY_EDITOR
            editorLastPause=pause;
            EditorCaptureLifecycle?.Invoke("application-pause",pause,editorCaptureActive);
            if(editorCaptureActive)return;
#endif
            desktopControls.Reset();
            suspendedInterval=true;applicationPaused=pause;Stick?.ResetStick();accumulator=0;discardResumeDelta=true;sound?.SetPaused(pause||focusLost);if(!waveBenchmarkRequested)preferences?.Save();
            if(pause&&Session!=null&&!finished&&Session.View.Status!=RunStatus.Completed)recording?.Checkpoint(Session);
            if(pause){PausePortraitForLifecycle();StopDiagnosticTrace("application-paused");}
        }
        private void OnApplicationFocus(bool focus)
        {
            LongFrameLifecycle(true, focus);
            if (offlineReplayCapture != null) return;
            if (waveBenchmarkRequested) { BenchmarkLifecycle(true, focus); return; }
#if UNITY_EDITOR
            editorLastFocus=focus;
            EditorCaptureLifecycle?.Invoke("application-focus",focus,editorCaptureActive);
            if(editorCaptureActive)return;
#endif
            desktopControls.Reset();
            suspendedInterval=true;focusLost=!focus&&AutoplayCapture.Active==null;Stick?.ResetStick();accumulator=0;discardResumeDelta=true;sound?.SetPaused(applicationPaused||focusLost);
            if(!focus){PausePortraitForLifecycle();StopDiagnosticTrace("focus-lost");}
        }
        private void OnApplicationQuit(){FinishLongFrames();if(!waveBenchmarkRequested)preferences?.Save();StopRecording();}
        private void OnDestroy(){FinishHeroMotionComparisonMetrics();FinishLongFrames();offlineReplayCapture?.Dispose();StopRecording();if(ownsFont&&font!=null)Destroy(font);}
        private void StopDiagnosticTrace(string reason)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            profilerTrace.Stop(CurrentFrame?.Tick ?? 0,reason);
#endif
        }
        private void Fail(Exception e)
        {
            StopDiagnosticTrace("error");
            Error=e.Message;screen=UiScreen.Error;MenuOpen=true;Stick?.ResetStick();
            try { if(Session!=null&&!finished&&Session.View.Status!=RunStatus.Completed)recording?.Checkpoint(Session); } catch(Exception snapshotError) { UnityEngine.Debug.LogWarning("Replay checkpoint failed: "+snapshotError.Message); }
            if(Ui==null)
            {
                font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var root=new GameObject("Boot error",typeof(RectTransform));root.transform.SetParent(transform,false);Ui=root.AddComponent<UiShell>();Ui.Initialize(font);
            }
            var completionFailed=Session?.View.Status==RunStatus.Completed&&!finished;
            ClearUi();hud=null;var panel=Ui.Panel("Error");
            Ui.Label(panel,completionFailed?"정산을 저장하지 못했습니다":"게임을 계속할 수 없습니다",UiTokens.Title,96);
            Ui.Label(panel,completionFailed?"저장 공간을 확인한 뒤 다시 시도해 주세요. 이 화면에서 정산을 다시 시도할 수 있습니다.":"저장 공간을 확인하고 게임을 다시 실행해 주세요. 문제가 계속되면 오류 기록을 확인해 주세요.",UiTokens.Body,160);
            if(completionFailed)Ui.Button(panel,"정산 저장 다시 시도",RetryCompleteRun);
            UnityEngine.Debug.LogException(e);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private IEnumerator VerifyFixtures()
        {
            parityRunning=true;screen=UiScreen.Replay;MenuOpen=true;debug.SetOpen(false);ClearUi();hud=null;var panel=Ui.Panel("Replay verification");var status=Ui.Label(panel,"기록 검증 중",UiTokens.Body,240);
            var uris=Enumerable.Range(30000,5).Select(id=>Application.isEditor?new Uri(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../artifacts/phase1b/replays",id+".ssreplay"))).AbsoluteUri:Application.streamingAssetsPath+"/replays/"+id+".ssreplay").ToArray();
            yield return DeviceParity.Run(FoundationBoot.Catalog,CanonicalContent.DataHash,uris,System.IO.Path.Combine(Application.persistentDataPath,"parity"),text=>status.text=text);
            parityRunning=false;Ui.Button(panel,"돌아가기",()=>{if(CurrentFrame.Status==RunStatus.AwaitingCard){cardsFrame=null;ShowCards();}else ShowHud();});
        }
        private void DebugAction(DebugIntent intent)
        {
            if(intent==DebugIntent.TraceStop){StopDiagnosticTrace("manual");return;}
            if(intent==DebugIntent.TraceStart){if(recording!=null&&!finished&&!parityRunning&&profilerTrace.Start(recording,CurrentFrame.Tick,CanonicalContent.DataHash))debug.SetOpen(false);return;}
            Stick?.ResetStick();accumulator=0;if(Session==null||finished||parityRunning)return;
            switch(intent)
            {
                case DebugIntent.VerifyFixtures:if(!parityRunning)StartCoroutine(VerifyFixtures());break;
                case DebugIntent.Speed1:Speed=1;break;case DebugIntent.Speed2:Speed=2;break;case DebugIntent.Speed4:Speed=4;break;
                case DebugIntent.GrantLevel:if(CurrentFrame.Status==RunStatus.Running)Send(ReplayCommandKind.GrantLevel);break;
                case DebugIntent.ToggleInvulnerable:invulnerable=!invulnerable;Send(ReplayCommandKind.SetInvulnerable,value:invulnerable?1:0);break;
                case DebugIntent.SpawnNormal:spawnPermille=1000;Send(ReplayCommandKind.SetSpawnPermille,value:spawnPermille);break;
                case DebugIntent.SpawnDouble:spawnPermille=2000;Send(ReplayCommandKind.SetSpawnPermille,value:spawnPermille);break;
                case DebugIntent.SpawnStress:spawnPermille=InteractiveSession.MaximumSpawnPermille;Send(ReplayCommandKind.SetSpawnPermille,value:spawnPermille);break;
                case DebugIntent.AimMovement:Aim=AimMode.Movement;Send(ReplayCommandKind.SetAimMode,value:(int)Aim);break;
                case DebugIntent.AimNearest:Aim=AimMode.NearestEnemy;Send(ReplayCommandKind.SetAimMode,value:(int)Aim);break;
            }
        }
#endif
    }
}
