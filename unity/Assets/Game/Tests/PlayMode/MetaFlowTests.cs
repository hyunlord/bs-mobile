#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using SowSiege.Core;
using Game.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tests.PlayMode
{
    public sealed class MetaFlowTests
    {
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
        }
        [UnityTest] public IEnumerator MetaPagesUseCatalogAndAbandonPersistsExactlyOneSettlement()
        {
            using var preferences=new RunPreferenceScope();
            yield return SceneManager.LoadSceneAsync("Boot");
            RunCoordinator app=null;var deadline=Time.realtimeSinceStartup+45;
            while((app=UnityEngine.Object.FindFirstObjectByType<RunCoordinator>())==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app,Is.Not.Null);yield return null;Assert.That(app.Error,Is.Null);
            Click(app,"변경 지도");yield return null;
            Assert.That(app.Ui.GetComponentsInChildren<Text>().Count(t=>t.text=="이전 변경을 완료하세요"),Is.EqualTo(9));
            Click(app,"장원으로");Click(app,"장원");yield return null;
            var next=app.Progression.Catalog.ManorBuildings.First(b=>b.Id!=app.Progression.State.ManorPriority);
            var card=app.Ui.GetComponentsInChildren<Text>().Single(t=>t.text.StartsWith(next.Name+" · ",StringComparison.Ordinal)).transform.parent.parent;
            var priority=card.GetComponentsInChildren<Button>().Single();priority.onClick.Invoke();yield return null;
            Assert.That(app.Progression.State.ManorPriority,Is.EqualTo(next.Id));
            Click(app,"돌아가기");Click(app,"가신");yield return null;
            foreach(var vassal in app.Progression.Catalog.Vassals)Assert.That(app.Ui.GetComponentsInChildren<Text>().Any(t=>t.text==vassal.Name),Is.True);
            Assert.That(app.Ui.GetComponentsInChildren<Button>().Count(b=>b.interactable&&b.GetComponentInChildren<Text>().text=="강화"),Is.Zero,"Empty wallet cannot purchase levels.");
            Click(app,"돌아가기");Click(app,"도전");yield return null;
            foreach(var challenge in app.Progression.Catalog.Challenges)Assert.That(app.Ui.GetComponentsInChildren<Text>().Any(t=>t.text==challenge.Name),Is.True);
            Click(app,"돌아가기");Click(app,"시작");yield return null;Click(app,"개척 시작");
            while(app.Session==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app.Error,Is.Null);Assert.That(app.Session,Is.Not.Null);yield return null;
            Assert.That(File.Exists(app.RecordedReplayPath+".meta"),Is.True);
            Assert.That(app.Progression.State.PendingRun,Is.Not.Null);
            Click(app,"설정");Click(app,"출정을 포기하고 정산");yield return null;
            Assert.That(app.Ui.GetComponentsInChildren<Text>().Any(t=>t.text=="출정 포기"),Is.True);
            Assert.That(app.Progression.State.PendingRun,Is.Null);
            Assert.That(app.Progression.State.CompletedRuns,Is.EqualTo(1));
            Click(app,"나가기");yield return null;yield return null;
            Assert.That(app.Progression.State.CompletedRuns,Is.EqualTo(1));
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
        }
        [UnityTest] public IEnumerator EditorPortraitAndSquareCaptureMetaPagesAndChapterTerrain()
        {
            using var preferences=new RunPreferenceScope();
            yield return SceneManager.LoadSceneAsync("Boot");
            RunCoordinator app=null;var deadline=Time.realtimeSinceStartup+45;
            while((app=UnityEngine.Object.FindFirstObjectByType<RunCoordinator>())==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app,Is.Not.Null);yield return null;Assert.That(app.Error,Is.Null);
            foreach(var square in new[]{false,true})
            {
                var shape=square?"square":"portrait";
                PlayModeWindow.SetCustomRenderingResolution(square?1080u:900u,square?1080u:1600u,"Phase2A Editor QA");
                yield return null;yield return Capture(app,"title-"+shape);
                foreach(var page in new[]{("변경 지도","chapters","장원으로"),("장원","manor","돌아가기"),("가신","vassals","돌아가기"),("도전","challenges","돌아가기")})
                {
                    Click(app,page.Item1);yield return null;
                    var scroll=app.Ui.GetComponentInChildren<ScrollRect>();scroll.verticalNormalizedPosition=1;
                    yield return Capture(app,page.Item2+"-"+shape+"-top");
                    scroll.verticalNormalizedPosition=0;
                    yield return Capture(app,page.Item2+"-"+shape+"-bottom");
                    Click(app,page.Item3);yield return null;
                }
            }
            app.StartRun(30000);deadline=Time.realtimeSinceStartup+45;
            while(app.Session==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app.Error,Is.Null);Assert.That(app.Session,Is.Not.Null);
            foreach(var square in new[]{false,true})
            {
                PlayModeWindow.SetCustomRenderingResolution(square?1080u:900u,square?1080u:1600u,"Phase2A Editor QA");
                yield return Capture(app,"chapter-terrain-"+(square?"square":"portrait"));
            }
            Click(app,"설정");Click(app,"출정을 포기하고 정산");yield return null;
            yield return Capture(app,"abandon-settlement-square");
            app.Ui.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition=0;
            yield return Capture(app,"abandon-conversion-square");
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
        }
        [UnityTest] public IEnumerator PendingSettlementIoFailureShowsErrorAndAllowsRetry()
        {
            using var preferences=new RunPreferenceScope();
            yield return SceneManager.LoadSceneAsync("Boot");
            RunCoordinator app=null;var deadline=Time.realtimeSinceStartup+45;
            while((app=UnityEngine.Object.FindFirstObjectByType<RunCoordinator>())==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app,Is.Not.Null);yield return null;app.StartRun(30000);
            while(app.Session==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app.Error,Is.Null);var original=app.Session;var pending=app.Progression.State.PendingRun;
            var obstacle=Path.Combine(RunCoordinator.MetaDirectoryOverride,"progress.bin.tmp");
            Directory.CreateDirectory(obstacle);
            LogAssert.Expect(LogType.Exception,new Regex("(UnauthorizedAccessException|IOException).*progress\\.bin\\.tmp"));
            app.StartRun(30001);yield return null;
            Assert.That(app.Error,Is.Not.Null);Assert.That(app.Ui.Content.Find("Error"),Is.Not.Null);
            Assert.That(app.Progression.State.PendingRun,Is.EqualTo(pending));
            Directory.Delete(obstacle);
            app.StartRun(30001);deadline=Time.realtimeSinceStartup+45;
            while(ReferenceEquals(app.Session,original)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app.Session,Is.Not.SameAs(original));Assert.That(app.Error,Is.Null);
            Assert.That(app.Progression.State.PendingRun.Seed,Is.EqualTo(30001));
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
        }
        [UnityTest] public IEnumerator CompletedRunSaveFailureRetriesTheTerminalResultExactlyOnce()
        {
            using var preferences=new RunPreferenceScope();
            yield return SceneManager.LoadSceneAsync("Boot");
            RunCoordinator app=null;var deadline=Time.realtimeSinceStartup+45;
            while((app=UnityEngine.Object.FindFirstObjectByType<RunCoordinator>())==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app,Is.Not.Null);yield return null;app.StartRun(30000);
            while(app.Session==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app.Error,Is.Null);Assert.That(app.Session,Is.Not.Null);
            while(app.Frame.Status!=RunStatus.Completed)
            {
                for(var n=0;n<180&&app.Frame.Status!=RunStatus.Completed;n++)
                {
                    if(app.Frame.Status==RunStatus.AwaitingCard)app.Send(ReplayCommandKind.ChooseCard,card:app.Session.View.CaptureCards().Cards[0]);
                    else app.Send(ReplayCommandKind.Advance);
                }
                if(app.Frame.Status!=RunStatus.Completed)yield return null;
            }
            var terminal=app.Session.GetSummary();var pending=app.Progression.State.PendingRun;
            var expected=MetaEngine.SettleRun(app.Progression.Catalog,app.Progression.State,pending,
                MetaRunAdapter.FromInteractive(terminal,app.Frame,app.FirstPlayable,false));
            var obstacle=Path.Combine(RunCoordinator.MetaDirectoryOverride,"progress.bin.tmp");Directory.CreateDirectory(obstacle);
            LogAssert.Expect(LogType.Exception,new Regex("(UnauthorizedAccessException|IOException).*progress\\.bin\\.tmp"));
            yield return null;
            Assert.That(app.Error,Is.Not.Null);Assert.That(app.Ui.Content.Find("Error"),Is.Not.Null);
            Assert.That(app.Progression.State.PendingRun,Is.EqualTo(pending));
            Assert.That(app.Progression.State.CompletedRuns,Is.EqualTo(0));
            Assert.That(app.Ui.GetComponentsInChildren<Button>().Any(b=>b.GetComponentInChildren<Text>().text=="정산 저장 다시 시도"),Is.True);
            Directory.Delete(obstacle);Click(app,"정산 저장 다시 시도");yield return null;
            Assert.That(app.Error,Is.Null);Assert.That(app.Ui.Content.Find("Summary"),Is.Not.Null);
            Assert.That(app.Progression.State.PendingRun,Is.Null);
            Assert.That(MetaSaveCodec.Encode(app.Progression.State),Is.EqualTo(MetaSaveCodec.Encode(expected.State)),"Retry must persist terminal facts, not abandoned facts.");
            PlayModeWindow.SetCustomRenderingResolution(900,1600,"Phase2A terminal save regression");
            app.Ui.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition=0;
            yield return Capture(app,"terminal-conversion-portrait",acceleratedCombat:true);
            var replayPath=app.RecordedReplayPath;
            using(var replay=File.OpenRead(replayPath))
            {
                var result=MetaReplayContext.Verify(File.ReadAllBytes(replayPath+".meta"),FoundationBoot.Catalog,
                    Game.App.Generated.CanonicalContent.CreateMetaCatalog(),FoundationBoot.VerifiedDataHash,ReplayCodec.Read(replay));
                Assert.That(result.EndKind,Is.EqualTo(terminal.Survived?ReplayEndKind.Duration:ReplayEndKind.Death));
            }
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
            var restored=new MetaProgression(Game.App.Generated.CanonicalContent.CreateMetaCatalog(),FoundationBoot.Catalog,
                FoundationBoot.VerifiedDataHash,RunCoordinator.MetaDirectoryOverride);
            Assert.That(restored.State.CompletedRuns,Is.EqualTo(1));Assert.That(restored.RecoveredAbandonedRun,Is.False);
        }
        [UnityTest] public IEnumerator IsolatedProgressionFixtureCapturesActualManorGrowthAndVassalUpgrade()
        {
            using var preferences=new RunPreferenceScope();
            var catalog=Game.App.Generated.CanonicalContent.CreateMetaCatalog();
            var fixture=MetaEngine.NewGame(catalog);
            foreach(var material in catalog.Materials)fixture.Wallet[material.Id]=material.WalletCap;
            foreach(var building in catalog.ManorBuildings)fixture.ManorLevels[building.Id]=building.MaxLevel;
            var growing=catalog.ManorBuildings.First();fixture.ManorLevels[growing.Id]=growing.MaxLevel-1;
            var vassal=catalog.Vassals.First(v=>v.InitiallyUnlocked);
            fixture.Vassals[vassal.Id]=fixture.Vassals[vassal.Id] with { Fragments=vassal.RankCosts[0] };
            Assert.That(MetaValidation.ValidateState(catalog,fixture),Is.Empty);
            new AtomicSaveStore(RunCoordinator.MetaDirectoryOverride,"progress.bin").Save(MetaSaveCodec.Encode(fixture),
                bytes=>MetaSaveCodec.Decode(bytes).Valid?AtomicSaveValidation.Valid:AtomicSaveValidation.Corrupt);
            yield return SceneManager.LoadSceneAsync("Boot");
            RunCoordinator app=null;var deadline=Time.realtimeSinceStartup+45;
            while((app=UnityEngine.Object.FindFirstObjectByType<RunCoordinator>())==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app,Is.Not.Null);yield return null;Assert.That(app.Error,Is.Null);
            Assert.That(app.Progression.State.ManorLevels[growing.Id],Is.EqualTo(growing.MaxLevel),"Host idle processing must actually grow the affordable building.");
            PlayModeWindow.SetCustomRenderingResolution(900,1600,"Phase2A isolated progression fixture");
            Click(app,"장원");
            Assert.That(app.Ui.GetComponentsInChildren<MetaGrowthPulse>().Length,Is.GreaterThan(0));
            yield return Capture(app,"fixture-manor-growth-portrait",true);
            PlayModeWindow.SetCustomRenderingResolution(1080,1080,"Phase2A isolated progression fixture");
            yield return Capture(app,"fixture-manor-grown-square",true);
            Click(app,"돌아가기");Click(app,"가신");yield return null;
            Click(app,"강화");Click(app,"승급");yield return null;
            Assert.That(app.Progression.State.Vassals[vassal.Id].Level,Is.EqualTo(2));
            Assert.That(app.Progression.State.Vassals[vassal.Id].Rank,Is.EqualTo(1));
            yield return Capture(app,"fixture-vassal-upgraded-square",true);
            PlayModeWindow.SetCustomRenderingResolution(900,1600,"Phase2A isolated progression fixture");
            yield return Capture(app,"fixture-vassal-upgraded-portrait",true);
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
        }
        static IEnumerator Capture(RunCoordinator app,string name,bool isolatedFixture=false,bool acceleratedCombat=false)
        {
            yield return null;yield return null;
            var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/phase2a/p2/editor-captures",name+".png"));Directory.CreateDirectory(Path.GetDirectoryName(path));
            if(File.Exists(path))File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);var deadline=Time.realtimeSinceStartup+10;
            while(!File.Exists(path)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(File.Exists(path),Is.True,"Capture missing: "+name);
            var image=new Texture2D(2,2);Assert.That(image.LoadImage(File.ReadAllBytes(path)),Is.True);
            Assert.That(image.width,Is.EqualTo(Screen.width));Assert.That(image.height,Is.EqualTo(Screen.height));UnityEngine.Object.Destroy(image);
            File.WriteAllText(path+".txt",$"surface=Unity Editor PlayMode; actualScreen={Screen.width}x{Screen.height}; safeArea={Screen.safeArea}; dataHash={FoundationBoot.VerifiedDataHash}; seed={app.Session?.GetSummary().Seed}; tick={app.Frame?.Tick}; input=automated UI actions; acceleratedCombat={acceleratedCombat}; invulnerability=false; deviceAcceptance=false; isolatedFixture={isolatedFixture}; fixtureDescription={(isolatedFixture?"valid wallet/manor/fragment fixture followed by actual menu actions; not earned normal play":"new account; no progression grants")}");
        }
        static void Click(RunCoordinator app,string label)
        {
            var button=app.Ui.GetComponentsInChildren<Button>().First(b=>b.interactable&&b.GetComponentInChildren<Text>().text==label);
            ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        }
    }
}

#endif
