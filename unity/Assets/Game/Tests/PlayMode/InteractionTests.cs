using System.Collections;
using System.Linq;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Tests.PlayMode
{
    public sealed class InteractionTests
    {
        [UnityTearDown]
        public IEnumerator Cleanup(){foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);yield return null;}

        [UnityTest]
        public IEnumerator RealUiStartsRunConsumesCardsAndResetsJoystick()
        {
            using var preferences=new RunPreferenceScope();
            yield return SceneManager.LoadSceneAsync("Boot");
            var deadline=Time.realtimeSinceStartup+45;
            RunCoordinator app=null;
            while((app=Object.FindFirstObjectByType<RunCoordinator>())==null && Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app,Is.Not.Null);yield return null;
            Assert.That(app.Error,Is.Null);
            Assert.That(FontProvider.Supports(app.Ui.Font,FontProvider.Labels+string.Concat(CanonicalContent.Displays.Select(d=>d.DisplayName))),Is.True);
            var menuScroll=app.Ui.GetComponentInChildren<ScrollRect>();
            Canvas.ForceUpdateCanvases();menuScroll.verticalNormalizedPosition=0;yield return null;
            AssertUiConsumes(app.Ui.GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<Text>().text=="설정").transform as RectTransform);
            menuScroll.verticalNormalizedPosition=1;yield return null;
            AssertUiConsumes(app.Ui.GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<Text>().text=="시작").transform as RectTransform);
            Click(app,"시작");yield return null;
            if(app.Ui.Content.Find("Introduction")!=null)Click(app,"개척 시작");
            while(app.Session==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app.Session,Is.Not.Null);yield return null;
            Assert.That(app.Session.GetSummary().Seed,Is.GreaterThanOrEqualTo(0));
            AssertUiConsumes(app.Ui.Content.Find("HUD") as RectTransform);
            app.SendMessage("OnApplicationPause",true);var suspendedTick=app.Frame.Tick;
            yield return new WaitForSecondsRealtime(.2f);Assert.That(app.Frame.Tick,Is.EqualTo(suspendedTick));
            app.SendMessage("OnApplicationPause",false);yield return null;Assert.That(app.Frame.Tick-suspendedTick,Is.LessThanOrEqualTo(2));
            var stick=app.Stick;var e=new PointerEventData(EventSystem.current){pointerId=7,position=new Vector2(150,150)};
            ExecuteEvents.Execute(stick.gameObject,e,ExecuteEvents.pointerDownHandler);e.position+=Vector2.right*stick.Radius;ExecuteEvents.Execute(stick.gameObject,e,ExecuteEvents.dragHandler);
            Assert.That(stick.Sample.X,Is.EqualTo(PlayerInput.Scale));
            yield return new WaitForSecondsRealtime(.2f);Assert.That(app.Frame.Tick,Is.GreaterThan(0));
            app.Send(ReplayCommandKind.GrantLevel);var transitionTick=app.Frame.Tick;System.Threading.Thread.Sleep(70);yield return null;yield return null;
            foreach(var label in app.GetComponentsInChildren<Text>())Assert.That(FontProvider.Supports(label.font,label.text),Is.True,label.text);
            var choice=app.Ui.GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<Text>().text=="선택").transform as RectTransform;
            yield return Reveal(app,choice);AssertUiConsumes(choice);
            var tick=app.Frame.Tick;Assert.That(stick.Sample.X,Is.Zero);Assert.That(app.Frame.Status,Is.EqualTo(RunStatus.AwaitingCard));
            yield return new WaitForSecondsRealtime(.2f);Assert.That(app.Frame.Tick,Is.EqualTo(tick));
            var offered=app.Session.View.CaptureCards();var before=app.Session.NextSequence;
            Click(app,"고정");yield return null;Assert.That(app.Session.NextSequence,Is.EqualTo(before+1));
            Click(app,"다시 뽑기 · "+offered.Rerolls);yield return null;
            Click(app,"금지");yield return null;
            Click(app,"선택");yield return null;
            Assert.That(app.Frame.Status,Is.EqualTo(RunStatus.Running));Assert.That(app.Error,Is.Null);
            var replayPath=app.RecordedReplayPath;
            foreach(var boot in Object.FindObjectsByType<FoundationBoot>())Object.Destroy(boot.gameObject);
            yield return null;
            var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../artifacts/unity/app-recordings"));System.IO.Directory.CreateDirectory(folder);var target=System.IO.Path.Combine(folder,"quit.ssreplay");System.IO.File.Copy(replayPath,target,true);
            var rows=System.IO.File.ReadAllLines(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(replayPath),"frames.csv"));var header=rows[0].Split(',');
            var tickColumn=System.Array.IndexOf(header,"tick");var deltaColumn=System.Array.IndexOf(header,"deltaMs");var pauseColumn=System.Array.IndexOf(header,"paused");
            Assert.That(rows.Skip(1).Select(line=>line.Split(',')).Any(row=>int.Parse(row[tickColumn])==transitionTick&&double.Parse(row[deltaColumn],System.Globalization.CultureInfo.InvariantCulture)>=60&&row[pauseColumn]=="0"),Is.True,"Card transition stall must remain attributed to active interval.");
            System.IO.File.Copy(replayPath+".meta",target+".meta",true);
            using(var stream=System.IO.File.OpenRead(target)){var result=MetaReplayContext.Verify(System.IO.File.ReadAllBytes(replayPath+".meta"),FoundationBoot.Catalog,CanonicalContent.CreateMetaCatalog(),FoundationBoot.VerifiedDataHash,ReplayCodec.Read(stream));Assert.That(result.EndKind,Is.EqualTo(ReplayEndKind.Quit));}
        }
        private static IEnumerator Reveal(RunCoordinator app,RectTransform rect)
        {
            var scroll=app.Ui.GetComponentInChildren<ScrollRect>();
            for(var step=0;step<=20;step++)
            {
                Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=1-step/20f;yield return null;
                var point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
                if(RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport,point))yield break;
            }
            Assert.Fail("Control cannot be reached by scrolling: "+rect.name);
        }
        private static void AssertUiConsumes(RectTransform rect)
        {
            Canvas.ForceUpdateCanvases();var point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            Assert.That(hits.Count,Is.GreaterThan(0));Assert.That(hits[0].gameObject.GetComponentInParent<Game.Input.FloatingStick>(),Is.Null,"UI must receive the raycast before the movement surface.");
        }
        private static void Click(RunCoordinator app,string text)
        {
            var button=app.Ui.GetComponentsInChildren<Button>().First(b=>b.interactable&&b.GetComponentInChildren<Text>().text==text);
            var e=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};ExecuteEvents.Execute(button.gameObject,e,ExecuteEvents.pointerClickHandler);
        }
    }
}
