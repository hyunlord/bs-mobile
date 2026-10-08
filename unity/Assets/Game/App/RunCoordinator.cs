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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Game.Debug;
#endif
namespace Game.App
{
    public sealed class RunCoordinator : MonoBehaviour
    {
        public InteractiveSession Session { get; private set; }
        public RunFrame Frame { get; private set; }
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
                var fontCorpus=displays.Values.Select(d=>d.DisplayName).ToList();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                fontCorpus.Add(DebugOverlay.RequiredGlyphs);
#endif
                font=FontProvider.Create(fontCorpus);ownsFont=true;
                var uiObject=new GameObject("Game UI",typeof(RectTransform));uiObject.transform.SetParent(transform,false);Ui=uiObject.AddComponent<UiShell>();Ui.Initialize(font);Stick=Ui.TouchSurface.gameObject.AddComponent<FloatingStick>();
                var events=new GameObject("UI events");events.transform.SetParent(transform,false);events.AddComponent<EventSystem>();events.AddComponent<InputSystemUIInputModule>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                debug=gameObject.AddComponent<DebugOverlay>();debug.Initialize(font,DebugAction);debug.SetColors(GamePalette.Panel,GamePalette.Text,GamePalette.Button,GamePalette.ButtonText);Stick.ExtraBlocker=debug.BlocksPointer;
#endif
                ShowMeta();
            }
            catch(Exception e){Fail(e);}
        }
        public void ShowMeta()
        {
            MenuOpen=true;Stick.ResetStick();Ui.Clear();hud=null;var panel=Ui.Panel("Meta");Ui.Label(panel,"씨앗과 공성",24,56);Ui.Label(panel,"한 손으로 화면을 끌어 이동하세요.\n공격과 도구는 자동으로 작동합니다.",16,64);
            Ui.Button(panel,"시작",()=>StartRun());Ui.Button(panel,"설정",()=>ShowSettings(false));
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
                Session=new InteractiveSession(c,options);Frame=Session.View.CaptureFrame();previous=Frame;accumulator=0;finished=false;telemetryClosed=false;suspendedInterval=false;intervalStarted=0;Speed=1;Error=null;cardsIdentity=null;MenuOpen=false;
                recording=new RunRecording(Application.persistentDataPath,options,BuildIdentity.Commit);var device=DeviceFacts.Capture();device.sourceHash=BuildIdentity.SourceHash;device.sourceDirty=BuildIdentity.SourceDirty;telemetry=new FrameTelemetry(recording.DirectoryPath,recording.SessionId,BuildIdentity.Commit,CanonicalContent.DataHash,Frame.DurationTicks,device);
                world=new GameObject("World renderer").AddComponent<WorldRenderer>();var settings=CanonicalContent.Presentation.Camera;
                world.Initialize(Camera.main,new WorldCameraSettings(settings.WorldUnitsPerUnityUnit,settings.MinHalfHeight,settings.MaxHalfHeight,settings.EstatePadding,settings.FollowMilliseconds,settings.ZoomMilliseconds));world.AcceptFrame(Frame);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                invulnerable=false;spawnPermille=1000;
#endif
                ShowHud();
            }
            catch(Exception e){Fail(e);}
            loading=false;
        }
        private void ShowHud(){Ui.Clear();hud=new UiHud(Ui);MenuOpen=false;cardsIdentity=null;Stick.ResetStick();}
        private bool Paused => MenuOpen || loading || parityRunning || applicationPaused || focusLost || Session==null || Session.View.Status!=RunStatus.Running
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            || debug!=null && debug.IsOpen
#endif
            ;
        private void Update()
        {
            if(Ui==null||Stick==null)return;
            if(telemetry!=null&&!telemetryClosed)
            {
                telemetry.CompleteInterval(Time.unscaledDeltaTime,suspendedInterval);suspendedInterval=false;intervalStarted=0;
                if(finished){telemetry.Finish();telemetryClosed=true;}
            }
            var pausedAtFrameStart=Paused;var speedAtFrameStart=Speed;
            Stick.Blocked=Paused;if(Paused){Stick.ResetStick();accumulator=0;}
            Ui.ShowStick(Stick.Active,Stick.Origin,Stick.Offset);
            if(Session==null||loading||Error!=null)return;
            if(!Paused)
            {
                if(discardResumeDelta)discardResumeDelta=false;else accumulator+=Time.unscaledDeltaTime*Speed;
                var step=1d/Frame.TickRate;
                while(accumulator>=step && Session.View.Status==RunStatus.Running){accumulator-=step;Send(ReplayCommandKind.Advance,Stick.Sample);}
            }
            if(Session.View.Status==RunStatus.AwaitingCard)ShowCards();
            hud?.Present(Frame);
            if(world!=null)
            {
                var visible=Screen.safeArea;if(hud!=null)visible.height=Mathf.Max(1,visible.height-120*Ui.Canvas.scaleFactor);
                world.Present(previous,Frame,Paused?1:(float)(accumulator*Frame.TickRate),Time.unscaledDeltaTime,visible);
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
                previous=Frame;Frame=Session.View.CaptureFrame();world?.AcceptFrame(Frame);
                if(kind!=ReplayCommandKind.Advance){Stick.ResetStick();accumulator=0;cardsIdentity=null;}
                if(kind==ReplayCommandKind.Advance && Frame.Tick%1800==0)recording.Checkpoint(Session);
                if(Frame.Status==RunStatus.Running&&MenuOpen&&kind==ReplayCommandKind.ChooseCard)ShowHud();
            }
            catch(Exception e){Fail(e);}
        }
        private void ShowCards()
        {
            var offer=Session.View.CaptureCards();var identity=Screen.width+"x"+Screen.height+"/"+string.Join("|",offer.Cards)+"/"+offer.Rerolls+"/"+offer.Bans+"/"+offer.Locks+"/"+offer.LockedCardId;
            if(cardsIdentity==identity)return;cardsIdentity=identity;MenuOpen=true;Stick.ResetStick();accumulator=0;Ui.Clear();hud=null;
            var panel=Ui.Panel("Cards");Ui.Label(panel,$"레벨 {Frame.Level} · 성장 선택",24,40);Ui.Label(panel,"선택하면 계속됩니다",14,24);
            var cardParent=(Transform)panel;
            if(Screen.height/(float)Screen.width<1.35f)
            {
                var columns=UiShell.Rect("Three cards",panel);var horizontal=columns.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();horizontal.spacing=8;horizontal.childControlWidth=true;horizontal.childForceExpandWidth=true;horizontal.childControlHeight=true;horizontal.childForceExpandHeight=false;cardParent=columns;
            }
            foreach(var id in offer.Cards)
            {
                var cardBody=UiShell.Rect("Card",cardParent);var vertical=cardBody.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();vertical.spacing=4;vertical.childControlHeight=true;vertical.childForceExpandHeight=false;vertical.childControlWidth=true;vertical.childForceExpandWidth=true;cardBody.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth=1;
                if(!displays.TryGetValue(id,out var display)){Fail(new InvalidOperationException("Missing display metadata: "+id));return;}
                var level=Frame.Equipment.FirstOrDefault(e=>e.Id==id)?.Level??0;
                Ui.Label(cardBody,display.DisplayName,18,32);Ui.Label(cardBody,Category(display.Category)+" · "+(level>0?$"현재 레벨 {level}":"현재 보유 없음")+(offer.LockedCardId==id?" · 고정":""),14,24);
                Ui.Button(cardBody,"선택",()=>Send(ReplayCommandKind.ChooseCard,card:id));
                var row=UiShell.Rect("Card actions",cardBody);var layout=row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();layout.spacing=8;layout.childForceExpandWidth=true;row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight=48;
                Ui.Button(row,"금지",()=>Send(ReplayCommandKind.BanCard,card:id),offer.Bans>0);Ui.Button(row,offer.LockedCardId==id?"고정됨":"고정",()=>Send(ReplayCommandKind.LockCard,card:id),offer.Locks>0&&offer.LockedCardId!=id);
            }
            Ui.Button(panel,$"다시 뽑기 · {offer.Rerolls}",()=>Send(ReplayCommandKind.RerollCards),offer.Rerolls>0);Ui.Label(panel,$"금지 {offer.Bans} · 고정 {offer.Locks}\n희귀도는 선택 후 결정됩니다",14,44);
        }
        private static string Category(string category)=>category switch {"weapon"=>"무기","tool"=>"도구","people"=>"사람","land"=>"땅","building"=>"건물","item"=>"장신구","evolution"=>"진화",_=>category};
        private void ShowSettings(bool inRun)
        {
            MenuOpen=true;Stick.ResetStick();Ui.Clear();hud=null;var panel=Ui.Panel("Settings");Ui.Label(panel,"설정",24,48);
            Ui.Label(panel,"바라보는 방향",18,32);Ui.Button(panel,(Aim==AimMode.Movement?"✓ ":"")+"이동 방향",()=>SetAim(AimMode.Movement,inRun));Ui.Button(panel,(Aim==AimMode.NearestEnemy?"✓ ":"")+"가까운 적 자동 조준",()=>SetAim(AimMode.NearestEnemy,inRun));Ui.Button(panel,"돌아가기",()=>{if(inRun)ShowHud();else ShowMeta();});
        }
        private void SetAim(AimMode aim,bool inRun){Aim=aim;if(inRun)Send(ReplayCommandKind.SetAimMode,value:(int)aim);ShowSettings(inRun);}
        private void CompleteRun()
        {
            finished=true;MenuOpen=true;Stick.ResetStick();var summary=Session.GetSummary();recording.Finish(Session,summary.Survived?ReplayEndKind.Duration:ReplayEndKind.Death);Ui.Clear();hud=null;
            var panel=Ui.Panel("Summary");Ui.Label(panel,summary.Survived?"한 해 완료":"사망",24,48);Ui.Label(panel,$"결산 · 레벨 {summary.Level}",18,32);
            double xp=summary.KillExperience+summary.HarvestExperience+summary.TaxExperience;
            Ui.Label(panel,$"경험치 출처\n사냥 {Ratio(summary.KillExperience,xp)} · 수확 {Ratio(summary.HarvestExperience,xp)} · 세금 {Ratio(summary.TaxExperience,xp)}",16,64);
            double damage=summary.WeaponDamage+summary.ToolActivationDamage+summary.ToolGrowthDamage+summary.AllyDamage;
            Ui.Label(panel,$"피해 비중\n무기 {Ratio(summary.WeaponDamage,damage)}\n도구 {Ratio(summary.ToolActivationDamage+summary.ToolGrowthDamage,damage)} · 아군 {Ratio(summary.AllyDamage,damage)}",16,96);
            Ui.Button(panel,"다시 하기",()=>StartRun());Ui.Button(panel,"나가기",()=>StartCoroutine(ReturnMeta()));
        }
        private static string Ratio(long value,double total)=>total>0?(value/total).ToString("P0"):"0%";
        private IEnumerator ReturnMeta(){StopRecording();Session=null;Frame=null;yield return SceneManager.LoadSceneAsync("Meta");ShowMeta();}
        private void StopRecording(){if(recording!=null){if(Session!=null&&!finished)recording.Finish(Session,ReplayEndKind.Quit);recording.Dispose();recording=null;}if(telemetry!=null&&!telemetryClosed){if(intervalStarted>0)telemetry.CompleteInterval((float)(Time.realtimeSinceStartupAsDouble-intervalStarted),suspendedInterval,true);telemetry.Finish();}telemetry?.Dispose();telemetry=null;telemetryClosed=true;intervalStarted=0;}
        private void OnApplicationPause(bool pause)
        {
            suspendedInterval=true;applicationPaused=pause;Stick?.ResetStick();accumulator=0;discardResumeDelta=true;
            if(pause&&Session!=null&&!finished)recording?.Checkpoint(Session);
        }
        private void OnApplicationFocus(bool focus)
        {
            suspendedInterval=true;focusLost=!focus;Stick?.ResetStick();accumulator=0;discardResumeDelta=true;
        }
        private void OnApplicationQuit()=>StopRecording();
        private void OnDestroy(){StopRecording();if(ownsFont&&font!=null)Destroy(font);}
        private void Fail(Exception e)
        {
            Error=e.Message;MenuOpen=true;Stick?.ResetStick();
            try { if(Session!=null&&!finished)recording?.Checkpoint(Session); } catch(Exception snapshotError) { UnityEngine.Debug.LogWarning("Replay checkpoint failed: "+snapshotError.Message); }
            if(Ui==null)
            {
                font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var root=new GameObject("Boot error",typeof(RectTransform));root.transform.SetParent(transform,false);Ui=root.AddComponent<UiShell>();Ui.Initialize(font);
            }
            Ui.Clear();hud=null;var panel=Ui.Panel("Error");Ui.Label(panel,"Unable to start / continue",24,64);Ui.Label(panel,Error,16,160);
            UnityEngine.Debug.LogException(e);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private IEnumerator VerifyFixtures()
        {
            parityRunning=true;MenuOpen=true;debug.SetOpen(false);Ui.Clear();hud=null;var panel=Ui.Panel("Replay verification");var status=Ui.Label(panel,"기록 검증 중",16,240);
            var uris=Enumerable.Range(30000,5).Select(id=>Application.isEditor?new Uri(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../artifacts/phase1a/replays",id+".ssreplay"))).AbsoluteUri:Application.streamingAssetsPath+"/replays/"+id+".ssreplay").ToArray();
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
