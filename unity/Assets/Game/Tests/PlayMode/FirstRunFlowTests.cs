using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.App;
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
    internal sealed class RunPreferenceScope : IDisposable
    {
        readonly Dictionary<string, float> floats = new Dictionary<string, float>();
        readonly Dictionary<string, int> integers = new Dictionary<string, int>();
        public RunPreferenceScope()
        {
            foreach(var key in RunPreferences.Keys)
            {
                var name=RunPreferences.Prefix+key;
                if(PlayerPrefs.HasKey(name))
                {
                    if(key=="musicVolume"||key=="sfxVolume")floats[name]=PlayerPrefs.GetFloat(name);
                    else integers[name]=PlayerPrefs.GetInt(name);
                }
                PlayerPrefs.DeleteKey(name);
            }
            PlayerPrefs.Save();
        }
        public void Dispose()
        {
            foreach(var key in RunPreferences.Keys)PlayerPrefs.DeleteKey(RunPreferences.Prefix+key);
            foreach(var pair in floats)PlayerPrefs.SetFloat(pair.Key,pair.Value);
            foreach(var pair in integers)PlayerPrefs.SetInt(pair.Key,pair.Value);
            PlayerPrefs.Save();
        }
    }
    public sealed class FirstRunFlowTests
    {
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
        }
        [UnityTest] public IEnumerator FirstIntroductionAndHintsPersistWhileCardSettingsReturnToOffer()
        {
            using var preferences=new RunPreferenceScope();
            yield return SceneManager.LoadSceneAsync("Boot");
            var deadline=Time.realtimeSinceStartup+45;RunCoordinator app=null;
            while((app=UnityEngine.Object.FindFirstObjectByType<RunCoordinator>())==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app,Is.Not.Null);yield return null;Assert.That(app.Error,Is.Null);
            Click(app,"시작");yield return null;
            Assert.That(app.Ui.Content.Find("Introduction"),Is.Not.Null);Assert.That(app.Session,Is.Null);
            Assert.That(RunPreferences.Load().IntroductionSeen,Is.False);
            Click(app,"개척 시작");
            while(app.Session==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app.Session,Is.Not.Null);yield return null;yield return null;
            Assert.That(RunPreferences.Load().IntroductionSeen,Is.True);
            Assert.That(RunPreferences.Load().HasSeen(FirstRunHint.Move),Is.True);
            Assert.That(RunPreferences.Load().HasSeen(FirstRunHint.Card),Is.False);
            app.Send(ReplayCommandKind.GrantLevel);yield return null;yield return null;yield return null;
            Assert.That(RunPreferences.Load().HasSeen(FirstRunHint.Card),Is.True);
            var offered=app.Session.View.CaptureCards().Cards.ToArray();var tick=app.Frame.Tick;
            Click(app,"설정");yield return null;
            var sliders=app.Ui.GetComponentsInChildren<Slider>();Assert.That(sliders.Length,Is.EqualTo(2));
            sliders[0].value=0;sliders[1].value=.23f;
            Click(app,"화면 흔들림");Click(app,"피해 숫자");Click(app,"진동");
            var before=app.Session.NextSequence;Click(app,"가까운 적 자동 조준");yield return null;yield return null;
            Assert.That(app.Ui.Content.Find("Settings"),Is.Not.Null,"AwaitingCard must not replace settings in Update.");
            Assert.That(app.Session.NextSequence,Is.EqualTo(before+1));Assert.That(app.Frame.Tick,Is.EqualTo(tick));
            Assert.That(app.Aim,Is.EqualTo(AimMode.NearestEnemy));
            var world=UnityEngine.Object.FindFirstObjectByType<WorldRenderer>();
            Assert.That(world.ShakeEnabled||world.ShowDamageNumbers,Is.False);
            Assert.That(app.GetComponents<AudioSource>().Single(s=>s.loop).volume,Is.Zero);
            Click(app,"돌아가기");yield return null;
            Assert.That(app.Ui.Content.Find("Cards"),Is.Not.Null);Assert.That(app.Session.View.CaptureCards().Cards,Is.EqualTo(offered));
            Click(app,"선택");yield return null;Assert.That(app.MenuOpen,Is.False);
            var replayPath=app.RecordedReplayPath;
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
            using(var input=System.IO.File.OpenRead(replayPath))Assert.That(ReplayRunner.Verify(FoundationBoot.Catalog,FoundationBoot.VerifiedDataHash,ReplayCodec.Read(input)).EndKind,Is.EqualTo(ReplayEndKind.Quit));
            var saved=RunPreferences.Load();Assert.That(saved.Aim,Is.EqualTo(AimMode.NearestEnemy));Assert.That(saved.EffectsVolume,Is.EqualTo(.23f).Within(.001f));
            Assert.That(saved.Haptics||saved.Shake||saved.DamageNumbers,Is.False);
            yield return SceneManager.LoadSceneAsync("Boot");app=null;deadline=Time.realtimeSinceStartup+45;
            while((app=UnityEngine.Object.FindFirstObjectByType<RunCoordinator>())==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app,Is.Not.Null);yield return null;Assert.That(app.Aim,Is.EqualTo(AimMode.NearestEnemy));
            Click(app,"시작");
            while(app.Session==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app.Session,Is.Not.Null);Assert.That(app.Ui.Content.Find("Introduction"),Is.Null);
            yield return null;yield return null;Assert.That(app.Ui.Content.Find("Hint"),Is.Null,"Seen movement guidance must not repeat after a new boot.");
            foreach(var boot in UnityEngine.Object.FindObjectsByType<FoundationBoot>())UnityEngine.Object.Destroy(boot.gameObject);
            yield return null;
        }
        static void Click(RunCoordinator app,string label)
        {
            var button=app.Ui.GetComponentsInChildren<Button>().First(b=>b.interactable&&b.GetComponentInChildren<Text>().text==label);
            ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        }
    }
}
