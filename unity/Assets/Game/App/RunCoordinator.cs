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
    public sealed class RunCoordinator : MonoBehaviour
    {
        public InteractiveSession Session { get; private set; }
        public RunFrame Frame { get; private set; }
        public FirstPlayableFrame FirstPlayable { get; private set; }
        public FloatingStick Stick { get; private set; }
        public UiShell Ui { get; private set; }
        public int Speed { get; private set; } = 1;
        public bool MenuOpen { get; private set; }
        public AimMode Aim { get; private set; }
        public string Error { get; private set; }
        public string RecordedReplayPath => recording?.ReplayPath;
        private readonly Dictionary<string,ContentDisplay> displays=new Dictionary<string,ContentDisplay>(StringComparer.Ordinal);
        private RunFrame previous;
        private WorldRenderer world;
        private UiHud hud;
        private FirstPlayableAudio sound;
        private RunPreferences preferences;
        private enum UiScreen { Title, Introduction, Run, Cards, Settings, Summary, Replay, Error }
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
        private string cardsIdentity;
        private bool finished,loading,parityRunning,applicationPaused,focusLost,discardResumeDelta,telemetryClosed,suspendedInterval;
        private double intervalStarted;
        private int seed;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private DebugOverlay debug;
        private bool invulnerable;
        private int spawnPermille=1000;
#endif
        public void Initialize()
        {
            try
            {
                foreach(var item in CanonicalContent.Displays) displays.Add(item.Id,item);
                preferences=RunPreferences.Load();Aim=preferences.Aim;
                var fontCorpus=displays.Values.SelectMany(d=>new[]{d.DisplayName,d.EffectDescription,d.GrowthDescription,d.EvolutionHint}).ToList();
                fontCorpus.Add("씨앗과 공성 새싹 변경의 한 해 적을 물리치고 영지를 키우세요 개척을 시작하기 전에 화면을 끌어 이동합니다 무기와 도구는 자동으로 작동합니다 카드를 골라 전투와 영지의 성장을 함께 준비하세요 개척 시작 시작 설정 돌아가기 배경음 효과음 진동 화면 흔들림 피해 숫자 자동 조준 이동 방향 가까운 적 소리와 화면 판을 잠시 멈췄습니다");
                foreach(FirstRunHint value in Enum.GetValues(typeof(FirstRunHint)))fontCorpus.Add(HintText(value));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                fontCorpus.Add(DebugOverlay.RequiredGlyphs);
#endif
                font=FontProvider.Create(fontCorpus);ownsFont=true;
                var uiObject=new GameObject("Game UI",typeof(RectTransform));uiObject.transform.SetParent(transform,false);Ui=uiObject.AddComponent<UiShell>();Ui.Initialize(font);Stick=Ui.TouchSurface.gameObject.AddComponent<FloatingStick>();Stick.RadiusCanvasUnits=UiTokens.StickRadius;
                var events=new GameObject("UI events");events.transform.SetParent(transform,false);events.AddComponent<EventSystem>();events.AddComponent<InputSystemUIInputModule>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                debug=gameObject.AddComponent<DebugOverlay>();debug.Initialize(font,DebugAction);debug.SetColors(GamePalette.Panel,GamePalette.Text,GamePalette.Button,GamePalette.ButtonText);Stick.ExtraBlocker=debug.BlocksPointer;
#endif
                gameObject.AddComponent<AudioListener>();sound=gameObject.AddComponent<FirstPlayableAudio>();sound.Initialize(FoundationBoot.Catalog);ApplyPreferences();
                ShowMeta();
            }
            catch(Exception e){Fail(e);}
        }
        public void ShowMeta()
        {
            screen=UiScreen.Title;MenuOpen=true;Stick.ResetStick();ClearUi();hud=null;
            var panel=Ui.Panel("Meta");Ui.Label(panel,"씨앗과 공성",UiTokens.Title,72);
            Ui.Label(panel,"새싹 변경의 한 해\n적을 물리치고 영지를 키우세요.",UiTokens.Body,96);
            Ui.Button(panel,"시작",()=>{if(preferences.IntroductionSeen)StartRun();else ShowIntroduction();});
            Ui.Button(panel,"설정",()=>ShowSettings(false));
        }
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
            if(loading)return;StartCoroutine(BeginRun(requestedSeed ?? (int)(DateTime.UtcNow.Ticks & int.MaxValue)));
        }
        private IEnumerator BeginRun(int nextSeed)
        {
            loading=true;StopRecording();Stick.ResetStick();yield return SceneManager.LoadSceneAsync("Run");
            try
            {
                seed=nextSeed;var c=FoundationBoot.Catalog;var options=new InteractiveOptions(new RunOptions(seed,c.Tuning.DefaultHero,c.Tuning.DefaultEstate,"mixed",ManualCards:true),Aim,FoundationBoot.VerifiedDataHash);
                Session=new InteractiveSession(c,options);CaptureSnapshots();previous=Frame;accumulator=0;finished=false;telemetryClosed=false;suspendedInterval=false;intervalStarted=0;Speed=1;Error=null;cardsIdentity=null;MenuOpen=false;completedSummary=null;
                recording=new RunRecording(Application.persistentDataPath,options,BuildIdentity.Commit);var device=DeviceFacts.Capture();device.sourceHash=BuildIdentity.SourceHash;device.sourceDirty=BuildIdentity.SourceDirty;telemetry=new FrameTelemetry(recording.DirectoryPath,recording.SessionId,BuildIdentity.Commit,CanonicalContent.DataHash,Frame.DurationTicks,device);
                world=new GameObject("World renderer").AddComponent<WorldRenderer>();var settings=CanonicalContent.Presentation.Camera;
                world.Initialize(Camera.main,new WorldCameraSettings(settings.WorldUnitsPerUnityUnit,settings.MinHalfHeight,settings.MaxHalfHeight,settings.EstatePadding,settings.FollowMilliseconds,settings.ZoomMilliseconds),c.Tuning.DefaultEstate,Frame.MapWidth,Frame.MapHeight);world.AcceptFrame(Frame,FirstPlayable);ApplyPreferences();sound.ResetRun();sound.AcceptFrame(Frame,FirstPlayable);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                invulnerable=false;spawnPermille=1000;
#endif
                ShowHud();
            }
            catch(Exception e){Fail(e);}
            loading=false;
        }
        private void ClearUi(){Ui.Clear();activeHint=null;}
        private void ShowHud(){screen=UiScreen.Run;ClearUi();hud=new UiHud(Ui,FoundationBoot.Catalog,()=>ShowSettings(true));MenuOpen=false;cardsIdentity=null;Stick.ResetStick();}
        private bool Paused => MenuOpen || loading || parityRunning || applicationPaused || focusLost || Session==null || Session.View.Status!=RunStatus.Running
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            || debug!=null && debug.IsOpen
#endif
            ;
        private void Update()
        {
            if(Ui==null||Stick==null)return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(UnityEngine.InputSystem.Keyboard.current?.f12Key.wasPressedThisFrame==true)debug?.SetOpen(!debug.IsOpen);
#endif
            if(telemetry!=null&&!telemetryClosed)
            {
                telemetry.CompleteInterval(Time.unscaledDeltaTime,suspendedInterval);suspendedInterval=false;intervalStarted=0;
                if(finished){telemetry.Finish();telemetryClosed=true;}
            }
            var pausedAtFrameStart=Paused;var speedAtFrameStart=Speed;
            Stick.Blocked=Paused;if(Paused){Stick.ResetStick();accumulator=0;}
            Ui.ShowStick(Stick.Active,Stick.Origin,Stick.Offset);
            sound?.SetPaused(applicationPaused||focusLost);
            if(Session==null||loading||Error!=null)return;
            if(!Paused)
            {
                if(discardResumeDelta)discardResumeDelta=false;else accumulator+=Time.unscaledDeltaTime*Speed;
                var step=1d/Frame.TickRate;
                while(accumulator>=step && Session.View.Status==RunStatus.Running){accumulator-=step;Send(ReplayCommandKind.Advance,Stick.Sample);}
            }
            if(Session.View.Status==RunStatus.AwaitingCard&&(screen==UiScreen.Run||screen==UiScreen.Cards))ShowCards();
            hud?.Present(Frame,FirstPlayable);
            UpdateHints();
            if(world!=null)
            {
                var visible=Screen.safeArea;
                if(hud!=null)
                {
                    var bottom=Mathf.Min(UiTokens.BottomWorldInset*Ui.Canvas.scaleFactor,visible.height*.2f);
                    visible.yMin+=bottom;visible.yMax=Mathf.Max(visible.yMin+1,visible.yMax-hud.ReservedTopPixels);
                }
                world.ShowAnnouncements=screen==UiScreen.Run;
                world.Present(previous,Frame,FirstPlayable,Paused?1:(float)(accumulator*Frame.TickRate),Time.unscaledDeltaTime,visible);
            }
            if(!finished&&telemetry!=null)
            {
                telemetry.BeginInterval(Frame,speedAtFrameStart,world!=null?world.ActiveVisualProjectiles:0,new Rect(0,0,Screen.width,Screen.height),Screen.safeArea,pausedAtFrameStart);
                intervalStarted=Time.realtimeSinceStartupAsDouble;
            }
            if(Session.View.Status==RunStatus.Completed&&!finished)CompleteRun();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debug?.Present(new DebugSnapshot(seed,Frame,Time.unscaledDeltaTime*1000,CanonicalContent.DataHash,Speed,invulnerable,spawnPermille,Aim,world!=null?world.ActiveVisualProjectiles:0,world!=null?world.DroppedEffects:0,world!=null?world.UnsupportedShapeCount:0));
#endif
        }
        public void Send(ReplayCommandKind kind,PlayerInput input=default,string card=null,int value=0)
        {
            if(Session==null||finished)return;
            try
            {
                var command=new ReplayCommand(Session.NextSequence,Frame.Tick,kind,input,card,value);Session.Apply(command);recording.WriteAccepted(command);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if(kind==ReplayCommandKind.SetInvulnerable)invulnerable=value!=0;
                if(kind==ReplayCommandKind.SetSpawnPermille)spawnPermille=value;
#endif
                if(kind==ReplayCommandKind.SetAimMode)Aim=(AimMode)value;
                previous=Frame;CaptureSnapshots();world?.AcceptFrame(Frame,FirstPlayable);sound?.AcceptFrame(Frame,FirstPlayable);
                if(kind!=ReplayCommandKind.Advance){Stick.ResetStick();accumulator=0;cardsIdentity=null;}
                if(kind==ReplayCommandKind.Advance && Frame.Tick%1800==0)recording.Checkpoint(Session);
                if(Frame.Status==RunStatus.Running&&MenuOpen&&kind==ReplayCommandKind.ChooseCard)ShowHud();
            }
            catch(Exception e){Fail(e);}
        }
        private void CaptureSnapshots()
        {
            var frame=Session.View.CaptureFrame();
            var firstPlayable=Session.View.CaptureFirstPlayable() ?? throw new InvalidOperationException("First-playable view is required.");
            Frame=frame;FirstPlayable=firstPlayable;
        }
        private void ShowCards()
        {
            var offer=Session.View.CaptureCards();
            var detail=string.Join("|",FirstPlayable.Cards.Select(c=>$"{c.Id}:{c.Rarity}:{c.CurrentLevel}:{c.NextLevel}:{string.Join(",",c.EvolutionIds)}"));
            detail+=string.Join("|",FirstPlayable.Evolutions.Select(e=>$"{e.Id}:{e.Available}:{e.Activated}"));
            var identity=Screen.width+"x"+Screen.height+"/"+detail+"/"+offer.Rerolls+"/"+offer.Bans+"/"+offer.Locks+"/"+offer.LockedCardId;
            if(cardsIdentity==identity&&screen==UiScreen.Cards)return;
            cardsIdentity=identity;screen=UiScreen.Cards;MenuOpen=true;Stick.ResetStick();accumulator=0;ClearUi();hud=null;
            UiRunPanels.Cards(Ui,Frame,FirstPlayable,offer,FoundationBoot.Catalog,displays,(kind,id)=>Send(kind,card:id));
            Ui.Button(Ui.Content.GetComponentInChildren<ScrollRect>().content,"설정",()=>ShowSettings(true));
        }
        private void ShowSettings(bool inRun)
        {
            if(screen!=UiScreen.Settings)settingsOrigin=screen;
            screen=UiScreen.Settings;MenuOpen=true;Stick.ResetStick();accumulator=0;ClearUi();hud=null;
            var panel=Ui.Panel("Settings");Ui.Label(panel,"설정",UiTokens.Title,72);
            Ui.Label(panel,inRun&&!finished?"판을 잠시 멈췄습니다":"소리와 화면",UiTokens.Caption,40);
            Ui.Slider(panel,"배경음",preferences.MusicVolume,v=>{preferences.MusicVolume=v;ApplyPreferences(true);});
            Ui.Slider(panel,"효과음",preferences.EffectsVolume,v=>{preferences.EffectsVolume=v;ApplyPreferences(true);});
            Ui.Toggle(panel,"진동",preferences.Haptics,v=>{preferences.Haptics=v;ApplyPreferences(true);});
            Ui.Toggle(panel,"화면 흔들림",preferences.Shake,v=>{preferences.Shake=v;ApplyPreferences(true);});
            Ui.Toggle(panel,"피해 숫자",preferences.DamageNumbers,v=>{preferences.DamageNumbers=v;ApplyPreferences(true);});
            Ui.Label(panel,"자동 조준",UiTokens.Heading,48);
            Ui.Button(panel,(Aim==AimMode.Movement?"✓ ":"")+"이동 방향",()=>SetAim(AimMode.Movement,inRun));
            Ui.Button(panel,(Aim==AimMode.NearestEnemy?"✓ ":"")+"가까운 적 자동 조준",()=>SetAim(AimMode.NearestEnemy,inRun));
            Ui.Button(panel,"돌아가기",RestoreSettingsOrigin);
        }
        private void SetAim(AimMode aim,bool inRun)
        {
            if(inRun&&Session!=null&&!finished)Send(ReplayCommandKind.SetAimMode,value:(int)aim);
            else Aim=aim;
            preferences.Aim=Aim;preferences.Save();ShowSettings(inRun);
        }
        private void RestoreSettingsOrigin()
        {
            if(settingsOrigin==UiScreen.Summary){ShowSummary();return;}
            if(settingsOrigin==UiScreen.Introduction){ShowIntroduction();return;}
            if(settingsOrigin==UiScreen.Title){ShowMeta();return;}
            if(Frame?.Status==RunStatus.AwaitingCard){cardsIdentity=null;ShowCards();return;}
            ShowHud();
        }
        private void ApplyPreferences(bool save=false)
        {
            if(world!=null){world.ShakeEnabled=preferences.Shake;world.ShowDamageNumbers=preferences.DamageNumbers;}
            sound?.SetPreferences(preferences.MusicVolume,preferences.EffectsVolume,preferences.Haptics);
            if(save)preferences.Save();
        }
        private void CompleteRun()
        {
            finished=true;MenuOpen=true;Stick.ResetStick();completedSummary=Session.GetSummary();
            recording.Finish(Session,completedSummary.Survived?ReplayEndKind.Duration:ReplayEndKind.Death);ShowSummary();
        }
        private void ShowSummary()
        {
            screen=UiScreen.Summary;MenuOpen=true;ClearUi();hud=null;
            UiRunPanels.Summary(Ui,completedSummary,Frame,FirstPlayable,displays,()=>StartRun(),()=>StartCoroutine(ReturnMeta()));
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
                if(screen!=hintScreen){Destroy(activeHint.transform.parent.gameObject);activeHint=null;return;}
                if(!hintRecorded&&!applicationPaused&&!focusLost&&Time.frameCount>hintStartedFrame&&activeHint.gameObject.activeInHierarchy&&!activeHint.canvasRenderer.cull)
                {
                    var corners=new Vector3[4];activeHint.rectTransform.GetWorldCorners(corners);
                    var bounds=new Rect(corners[0],corners[2]-corners[0]);
                    if(bounds.Overlaps(Screen.safeArea)){preferences.MarkHintSeen(activeHintKind);hintRecorded=true;}
                }
                hintRemaining-=Time.unscaledDeltaTime;
                if(hintRemaining<=0){Destroy(activeHint.transform.parent.gameObject);activeHint=null;}
                return;
            }
            FirstRunHint? next=null;
            if(screen==UiScreen.Cards&&!preferences.HasSeen(FirstRunHint.Card))next=FirstRunHint.Card;
            else if(screen==UiScreen.Run)
            {
                if(!preferences.HasSeen(FirstRunHint.Move))next=FirstRunHint.Move;
                else if(!preferences.HasSeen(FirstRunHint.Harvest)&&Frame.Farms.Any(f=>f.Ripe))next=FirstRunHint.Harvest;
                else if(!preferences.HasSeen(FirstRunHint.Muster)&&FirstPlayable.People.Any(p=>p.Activity=="muster"))next=FirstRunHint.Muster;
            }
            if(!next.HasValue||applicationPaused||focusLost)return;
            activeHintKind=next.Value;hintScreen=screen;hintRecorded=false;hintStartedFrame=Time.frameCount;hintRemaining=5;
            activeHint=Ui.Hint(HintText(activeHintKind));
            if(screen==UiScreen.Cards)
            {
                var container=(RectTransform)activeHint.transform.parent;
                container.SetParent(Ui.Content.GetComponentInChildren<ScrollRect>().content,false);
                container.SetSiblingIndex(2);
                var layout=container.gameObject.AddComponent<LayoutElement>();
                layout.minHeight=layout.preferredHeight=96;layout.flexibleHeight=0;
            }
        }
        private IEnumerator ReturnMeta(){StopRecording();Session=null;Frame=null;FirstPlayable=null;yield return SceneManager.LoadSceneAsync("Meta");ShowMeta();}
        private void StopRecording(){if(recording!=null){if(Session!=null&&!finished)recording.Finish(Session,ReplayEndKind.Quit);recording.Dispose();recording=null;}if(telemetry!=null&&!telemetryClosed){if(intervalStarted>0)telemetry.CompleteInterval((float)(Time.realtimeSinceStartupAsDouble-intervalStarted),suspendedInterval,true);telemetry.Finish();}telemetry?.Dispose();telemetry=null;telemetryClosed=true;intervalStarted=0;}
        private void OnApplicationPause(bool pause)
        {
            suspendedInterval=true;applicationPaused=pause;Stick?.ResetStick();accumulator=0;discardResumeDelta=true;sound?.SetPaused(pause||focusLost);preferences?.Save();
            if(pause&&Session!=null&&!finished)recording?.Checkpoint(Session);
        }
        private void OnApplicationFocus(bool focus)
        {
            suspendedInterval=true;focusLost=!focus;Stick?.ResetStick();accumulator=0;discardResumeDelta=true;sound?.SetPaused(applicationPaused||focusLost);
        }
        private void OnApplicationQuit(){preferences?.Save();StopRecording();}
        private void OnDestroy(){StopRecording();if(ownsFont&&font!=null)Destroy(font);}
        private void Fail(Exception e)
        {
            Error=e.Message;screen=UiScreen.Error;MenuOpen=true;Stick?.ResetStick();
            try { if(Session!=null&&!finished)recording?.Checkpoint(Session); } catch(Exception snapshotError) { UnityEngine.Debug.LogWarning("Replay checkpoint failed: "+snapshotError.Message); }
            if(Ui==null)
            {
                font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var root=new GameObject("Boot error",typeof(RectTransform));root.transform.SetParent(transform,false);Ui=root.AddComponent<UiShell>();Ui.Initialize(font);
            }
            ClearUi();hud=null;var panel=Ui.Panel("Error");Ui.Label(panel,"Unable to start / continue",UiTokens.Title,96);Ui.Label(panel,Error,UiTokens.Body,160);
            UnityEngine.Debug.LogException(e);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private IEnumerator VerifyFixtures()
        {
            parityRunning=true;screen=UiScreen.Replay;MenuOpen=true;debug.SetOpen(false);ClearUi();hud=null;var panel=Ui.Panel("Replay verification");var status=Ui.Label(panel,"기록 검증 중",UiTokens.Body,240);
            var uris=Enumerable.Range(30000,5).Select(id=>Application.isEditor?new Uri(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../artifacts/phase1b/replays",id+".ssreplay"))).AbsoluteUri:Application.streamingAssetsPath+"/replays/"+id+".ssreplay").ToArray();
            yield return DeviceParity.Run(FoundationBoot.Catalog,CanonicalContent.DataHash,uris,System.IO.Path.Combine(Application.persistentDataPath,"parity"),text=>status.text=text);
            parityRunning=false;Ui.Button(panel,"돌아가기",()=>{if(Frame.Status==RunStatus.AwaitingCard){cardsIdentity=null;ShowCards();}else ShowHud();});
        }
        private void DebugAction(DebugIntent intent)
        {
            Stick?.ResetStick();accumulator=0;if(Session==null||finished||parityRunning)return;
            switch(intent)
            {
                case DebugIntent.VerifyFixtures:if(!parityRunning)StartCoroutine(VerifyFixtures());break;
                case DebugIntent.Speed1:Speed=1;break;case DebugIntent.Speed2:Speed=2;break;case DebugIntent.Speed4:Speed=4;break;
                case DebugIntent.GrantLevel:if(Frame.Status==RunStatus.Running)Send(ReplayCommandKind.GrantLevel);break;
                case DebugIntent.ToggleInvulnerable:invulnerable=!invulnerable;Send(ReplayCommandKind.SetInvulnerable,value:invulnerable?1:0);break;
                case DebugIntent.SpawnNormal:spawnPermille=1000;Send(ReplayCommandKind.SetSpawnPermille,value:spawnPermille);break;
                case DebugIntent.SpawnDouble:spawnPermille=2000;Send(ReplayCommandKind.SetSpawnPermille,value:spawnPermille);break;
                case DebugIntent.AimMovement:Aim=AimMode.Movement;Send(ReplayCommandKind.SetAimMode,value:(int)Aim);break;
                case DebugIntent.AimNearest:Aim=AimMode.NearestEnemy;Send(ReplayCommandKind.SetAimMode,value:(int)Aim);break;
            }
        }
#endif
    }
}
