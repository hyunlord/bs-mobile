#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Tests.PlayMode
{
    public sealed class UiCaptureTests
    {
        [UnityTearDown]
        public IEnumerator Cleanup(){foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);yield return null;}

        [UnityTest]
        public IEnumerator PortraitAndSquareRetainRunAndCaptureRealUi()
        {
            using var preferences=new RunPreferenceScope();
            PlayModeWindow.SetCustomRenderingResolution(900,1600,"Phase1B QA");
            yield return SceneManager.LoadSceneAsync("Boot");
            RunCoordinator app=null;var deadline=Time.realtimeSinceStartup+45;
            while((app=UnityEngine.Object.FindFirstObjectByType<RunCoordinator>())==null&&Time.realtimeSinceStartup<deadline)yield return null;
            yield return null;Assert.That(app.Error,Is.Null);yield return Capture("meta-portrait");
            Click(app,"시작");yield return null;yield return Capture("intro-portrait");
            PlayModeWindow.SetCustomRenderingResolution(1080,1080,"Phase1B QA");yield return null;yield return Capture("intro-square");
            Click(app,"돌아가기");yield return null;yield return Capture("meta-square");
            Click(app,"설정");yield return null;yield return Capture("settings-square");
            var squareSettings=app.Ui.GetComponentInChildren<ScrollRect>();squareSettings.verticalNormalizedPosition=0;yield return null;yield return Capture("settings-square-bottom");
            Click(app,"돌아가기");PlayModeWindow.SetCustomRenderingResolution(900,1600,"Phase1B QA");yield return null;
            Click(app,"설정");yield return null;yield return Capture("settings-portrait");
            var settingsScroll=app.Ui.GetComponentInChildren<ScrollRect>();settingsScroll.verticalNormalizedPosition=0;yield return null;yield return Capture("settings-portrait-bottom");Click(app,"돌아가기");app.StartRun(30000);
            while(app.Session==null&&Time.realtimeSinceStartup<deadline)yield return null;
            var debug=app.GetComponent<Game.Debug.DebugOverlay>();debug.SetOpen(true);yield return null;Click(app,"무적 전환");debug.SetOpen(false);var original=app.Session;
            while(app.Frame.Tick<18000)
            {
                for(var n=0;n<180 && app.Frame.Tick<18000;n++)
                {
                    if(app.Frame.Status==RunStatus.AwaitingCard)app.Send(ReplayCommandKind.ChooseCard,card:app.Session.View.CaptureCards().Cards[0]);
                    else app.Send(ReplayCommandKind.Advance,new PlayerInput(0,0));
                }
                yield return null;
            }
            if(app.Frame.Status==RunStatus.AwaitingCard)app.Send(ReplayCommandKind.ChooseCard,card:app.Session.View.CaptureCards().Cards[0]);
            yield return null;yield return Capture("run-portrait-late");
            debug.SetOpen(true);yield return null;
            foreach(var text in app.GetComponentsInChildren<Text>())Assert.That(Game.View.FontProvider.Supports(text.font,text.text),Is.True,text.text);
            yield return Capture("debug-portrait");
            var scroll=debug.GetComponentInChildren<ScrollRect>();
            for(var i=0;i<10&&scroll.verticalNormalizedPosition>.05f;i++){ExecuteEvents.Execute(scroll.gameObject,new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-100)},ExecuteEvents.scrollHandler);yield return null;}
            Assert.That(scroll.verticalNormalizedPosition,Is.LessThan(.1f));yield return Capture("debug-portrait-bottom");debug.SetOpen(false);
            app.Send(ReplayCommandKind.GrantLevel);yield return null;yield return Capture("cards-portrait");
            var cardScroll=app.Ui.GetComponentInChildren<ScrollRect>();cardScroll.verticalNormalizedPosition=0;yield return null;yield return Capture("cards-portrait-bottom");
            var pausedTick=app.Frame.Tick;PlayModeWindow.SetCustomRenderingResolution(1080,1080,"Phase1B QA");yield return null;yield return null;
            Assert.That(app.Session,Is.SameAs(original));Assert.That(app.Frame.Tick,Is.EqualTo(pausedTick));yield return Capture("cards-square");
            Click(app,"선택");yield return null;yield return Capture("run-square-late");
            while(app.Frame.Status!=RunStatus.Completed)
            {
                for(var n=0;n<180&&app.Frame.Status!=RunStatus.Completed;n++)
                {
                    if(app.Frame.Status==RunStatus.AwaitingCard)app.Send(ReplayCommandKind.ChooseCard,card:app.Session.View.CaptureCards().Cards[0]);
                    else app.Send(ReplayCommandKind.Advance);
                }
                yield return null;
            }
            yield return null;Assert.That(app.Frame.Tick,Is.EqualTo(app.Frame.DurationTicks));yield return Capture("summary-square");
            var summaryScroll=app.Ui.GetComponentInChildren<ScrollRect>();summaryScroll.verticalNormalizedPosition=0;yield return null;yield return Capture("summary-square-bottom");SaveReplay(app,"duration",ReplayEndKind.Duration);
            Click(app,"다시 하기");yield return null;yield return null;
            Assert.That(app.Session,Is.Not.SameAs(original));Assert.That(app.Error,Is.Null);
            var retrySession=app.Session;debug.SetOpen(true);app.StartRun(30000);while(app.Session==retrySession)yield return null;
            while(app.Frame.Status!=RunStatus.Completed)
            {
                for(var n=0;n<180&&app.Frame.Status!=RunStatus.Completed;n++)
                {
                    if(app.Frame.Status==RunStatus.AwaitingCard)app.Send(ReplayCommandKind.ChooseCard,card:app.Session.View.CaptureCards().Cards[0]);else app.Send(ReplayCommandKind.Advance,new PlayerInput(1000,1000));
                }
                yield return null;
            }
            debug.SetOpen(false);yield return null;Assert.That(app.Session.GetSummary().EndReason,Is.EqualTo("death"));
            PlayModeWindow.SetCustomRenderingResolution(900,1600,"Phase1B QA");yield return null;yield return Capture("death-portrait");SaveReplay(app,"death",ReplayEndKind.Death);
            var dead=app.Session;Click(app,"다시 하기");yield return null;yield return null;Assert.That(app.Session,Is.Not.SameAs(dead));
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
        }
        [UnityTest]
        public IEnumerator WholeCardPointerSelectsWhileSecondaryActionsNeverChoose()
        {
            if(Game.App.Generated.CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Requires wave-1a export.");
            Game.View.ArtCatalog.ProfileName="wave-1a";
            var catalog=Game.App.Generated.CanonicalContent.CreateCatalog();
            var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,Game.App.Generated.CanonicalContent.DataHash));
            var id=catalog.WaveRuntime.Gear.Keys.First();
            var frame=session.View.CaptureFrame();var wave=((IWaveRunView)session.View).CaptureWaveRuntime();
            var offer=new CardOfferView(new[]{id},null,0,1,1);
            var envelope=WavePresentation.Envelope(catalog,frame,offer,wave) with
                {Cards=new[]{new OfferedCardDetail(id,"common",1,0,1,Array.Empty<string>())}};
            var displays=Game.App.Generated.CanonicalContent.Displays.ToDictionary(d=>d.Id);
            var font=Game.View.FontProvider.Create(displays.Values.Select(d=>d.DisplayName));
            var root=new GameObject("Whole card pointer QA",typeof(RectTransform));
            var events=EventSystem.current;GameObject eventOwner=null;
            if(events==null){eventOwner=new GameObject("Card QA events");events=eventOwner.AddComponent<EventSystem>();}
            var ui=root.AddComponent<Game.View.UiShell>();ui.Initialize(font);
            var commands=new System.Collections.Generic.List<ReplayCommandKind>();
            try
            {
                foreach(var resolution in new[]{(width:900u,height:1600u),(width:1080u,height:1080u)})
                {
                    PlayModeWindow.SetCustomRenderingResolution(resolution.width,resolution.height,"Card pointer QA");yield return null;
                    ui.Clear();yield return null;
                    UiRunPanels.Cards(ui,frame,envelope,offer,catalog,displays,(kind,card)=>{Assert.That(card,Is.EqualTo(id));commands.Add(kind);});
                    yield return null;Canvas.ForceUpdateCanvases();yield return null;
                    var primary=ui.Content.GetComponentsInChildren<Button>().Single(b=>b.name=="Choose "+id);
                    var title=primary.GetComponentsInChildren<Text>().First();
                    var body=primary.GetComponentsInChildren<Text>().First(t=>t.text.StartsWith("기본 효과",StringComparison.Ordinal));
                    var icon=primary.GetComponentsInChildren<Image>().Single(image=>image.name=="Icon "+id);
                    void Pointer(RectTransform target)
                    {
                        var position=RectTransformUtility.WorldToScreenPoint(null,target.TransformPoint(target.rect.center));
                        var pointer=new PointerEventData(events){button=PointerEventData.InputButton.Left,position=position};
                        var hits=new System.Collections.Generic.List<RaycastResult>();events.RaycastAll(pointer,hits);
                        var hit=hits.First(result=>result.gameObject.transform.IsChildOf(ui.transform));
                        Assert.That(ExecuteEvents.ExecuteHierarchy(hit.gameObject,pointer,ExecuteEvents.pointerClickHandler),Is.Not.Null);
                    }
                    foreach(var target in new[]{title.rectTransform,body.rectTransform,icon.rectTransform})
                    {
                        commands.Clear();Pointer(target);
                        Assert.That(commands,Is.EqualTo(new[]{ReplayCommandKind.ChooseCard}),target.name);
                    }
                    foreach(var action in new[]{("금지",ReplayCommandKind.BanCard),("고정",ReplayCommandKind.LockCard)})
                    {
                        var button=ui.Content.GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text==action.Item1);
                        Assert.That(button.transform.IsChildOf(primary.transform),Is.False,"Secondary controls are outside the primary selection surface.");
                        commands.Clear();Pointer((RectTransform)button.transform);
                        Assert.That(commands,Is.EqualTo(new[]{action.Item2}));
                        button.interactable=false;commands.Clear();Pointer((RectTransform)button.transform);
                        Assert.That(commands,Is.Empty,"Disabled footer controls must not fall through to card selection.");
                    }
                    commands.Clear();ExecuteEvents.Execute(primary.gameObject,new BaseEventData(events),ExecuteEvents.submitHandler);
                    Assert.That(commands,Is.EqualTo(new[]{ReplayCommandKind.ChooseCard}),"Keyboard submit keeps the ordinary choose path.");
                }
            }
            finally{UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(font);if(eventOwner!=null)UnityEngine.Object.Destroy(eventOwner);}
            yield return null;
        }
        [UnityTest]
        public IEnumerator CanonicalNamesFitNarrowCardColumns()
        {
            var names=Game.App.Generated.CanonicalContent.Displays.Select(value=>value.DisplayName).Distinct().ToArray();
            var font=Game.View.FontProvider.Create(names);
            var root=new GameObject("Canonical title layout",typeof(RectTransform));
            var ui=root.AddComponent<Game.View.UiShell>();ui.Initialize(font);
            var column=Game.View.UiShell.Rect("Narrow card",ui.Content);column.sizeDelta=new Vector2(150,700);
            var layout=column.gameObject.AddComponent<VerticalLayoutGroup>();layout.childControlHeight=true;layout.childForceExpandHeight=false;
            var title=ui.Label(column,"",18,32);
            foreach(var name in names)
            {
                title.text=name;LayoutRebuilder.ForceRebuildLayoutImmediate(column);Canvas.ForceUpdateCanvases();
                Assert.That(title.rectTransform.rect.height,Is.GreaterThanOrEqualTo(title.preferredHeight-.1f),name);
            }
            UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(font);yield return null;
        }
        private static void SaveReplay(RunCoordinator app,string name,ReplayEndKind kind)
        {
            var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/unity/app-recordings"));Directory.CreateDirectory(folder);var target=Path.Combine(folder,name+".ssreplay");File.Copy(app.RecordedReplayPath,target,true);
            File.Copy(app.RecordedReplayPath+".meta",target+".meta",true);
            using(var stream=File.OpenRead(target)){var result=MetaReplayContext.Verify(File.ReadAllBytes(target+".meta"),FoundationBoot.Catalog,Game.App.Generated.CanonicalContent.CreateMetaCatalog(),FoundationBoot.VerifiedDataHash,ReplayCodec.Read(stream));Assert.That(result.EndKind,Is.EqualTo(kind));}
        }
        private static void Click(RunCoordinator app,string label)
        {
            var button=app.GetComponentsInChildren<Button>().First(b=>b.interactable&&(label=="선택"?b.name.StartsWith("Choose ",StringComparison.Ordinal):b.GetComponentInChildren<Text>().text==label));
            ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        }
        private static IEnumerator Capture(string name)
        {
            yield return null;yield return null;
            var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/phase1b/m3-runtime",name+".png"));Directory.CreateDirectory(Path.GetDirectoryName(path));
            if(File.Exists(path))File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            var deadline=Time.realtimeSinceStartup+10;
            while(!File.Exists(path)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(File.Exists(path),Is.True,"Capture missing: "+name);
            var image=new Texture2D(2,2);Assert.That(image.LoadImage(File.ReadAllBytes(path)),Is.True);UnityEngine.Object.Destroy(image);
            File.WriteAllText(path+".txt",$"actualScreen={Screen.width}x{Screen.height};safeArea={Screen.safeArea}");
        }
    }
}
#endif
