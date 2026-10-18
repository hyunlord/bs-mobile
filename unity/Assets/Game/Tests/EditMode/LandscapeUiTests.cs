using System;
using System.Linq;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.EditMode
{
    public sealed class LandscapeUiTests
    {
        [Test]
        public void PendingViewportScaleIsSynchronizedBeforeFixedCardGeometry()
        {
            var owner=new GameObject("Delayed CanvasScaler regression",typeof(RectTransform));
            var font=FontProvider.Create(Array.Empty<string>());
            try
            {
                var ui=owner.AddComponent<UiShell>();ui.Initialize(font);
                ui.Canvas.scaleFactor=Screen.width/1326f;
                Canvas.ForceUpdateCanvases();
                ui.RefreshSafeArea();ui.SynchronizeViewport();
                var expectedScale=Screen.width/900f;
                Assert.That(ui.Canvas.scaleFactor,Is.EqualTo(expectedScale).Within(.00001));
                var safe=Screen.safeArea;if(safe.width<=0||safe.height<=0)safe=new Rect(0,0,Screen.width,Screen.height);
                Assert.That(ui.SafeRoot.rect.width,Is.EqualTo(safe.width/expectedScale).Within(.1f));
                Assert.That(ui.SafeRoot.rect.height,Is.EqualTo(safe.height/expectedScale).Within(.1f));
                var hud=UiShell.Rect("HUD",ui.Content);hud.sizeDelta=new Vector2(900,56);
                var hint=ui.Hint("화면 빈 곳을 끌어 이동하세요. 공격은 자동입니다.");
                Assert.That(UiRunPanels.PlaceLandscapeBattleHint(ui,hint),Is.True);
                Canvas.ForceUpdateCanvases();
                var container=(RectTransform)hint.transform.parent;
                Assert.That(container.parent,Is.SameAs(hud));
                Assert.That(container.anchoredPosition.y+container.rect.height,Is.LessThanOrEqualTo(hud.rect.height));
                Assert.That(hint.preferredHeight,Is.LessThanOrEqualTo(hint.rectTransform.rect.height+.5f));
                UiRunPanels.LayoutHint(ui,hint,false,false);
                Assert.That(hint.transform.parent.parent,Is.SameAs(ui.Content));
                Assert.That(container.sizeDelta,Is.EqualTo(new Vector2(720,120)));
                Assert.That(hint.fontSize,Is.EqualTo(UiTokens.Body));
                Assert.That(container.pivot,Is.EqualTo(new Vector2(.5f,.5f)));
                UiRunPanels.LayoutHint(ui,hint,false,true);
                Assert.That(container.parent,Is.SameAs(hud));
                Assert.That(container.sizeDelta.y,Is.EqualTo(18));
                Assert.That(hint.fontSize,Is.EqualTo(12));
                foreach(var message in new[]{"화면 빈 곳을 끌어 이동하세요. 공격은 자동입니다.","황금빛으로 익은 밭에 다가가 수확하세요.","징집된 백성이 적과 싸웁니다. 다시 일터로 돌아오는 것도 지켜보세요."})
                {
                    hint.text=message;UiRunPanels.LayoutHint(ui,hint,false,true);Canvas.ForceUpdateCanvases();
                    Assert.That(hint.preferredHeight,Is.LessThanOrEqualTo(hint.rectTransform.rect.height+.5f),message);
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(font);}
        }

        [TestCase(1920,1080)]
        [TestCase(1440,900)]
        [TestCase(1280,800)]
        public void EveryWaveCardFitsAllTextAndActionsInLandscape(int width,int height)
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            var priorArtProfile=ArtCatalog.ProfileName;ArtCatalog.ProfileName=CanonicalContent.ProfileName;
            var owner=new GameObject("Landscape cards",typeof(RectTransform));
            var font=FontProvider.Create(Array.Empty<string>());
            try
            {
                var ui=owner.AddComponent<UiShell>();ui.Initialize(font);
                ui.GetComponent<CanvasScaler>().enabled=false;ui.Canvas.scaleFactor=width/900f;
                ui.SafeRoot.anchorMin=ui.SafeRoot.anchorMax=Vector2.zero;ui.SafeRoot.pivot=Vector2.zero;
                ui.SafeRoot.sizeDelta=new Vector2(900,height/ui.Canvas.scaleFactor);
                Canvas.ForceUpdateCanvases();
                var catalog=CanonicalContent.CreateCatalog();Assert.That(catalog.WaveRuntime,Is.Not.Null,"This fixture requires wave-1a content.");
                var session=new InteractiveSession(catalog,new(new(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
                var frame=session.View.CaptureFrame();var wave=((IWaveRunView)session.View).CaptureWaveRuntime();
                var displays=CanonicalContent.Displays.ToDictionary(d=>d.Id);
                var ids=catalog.WaveRuntime.Gear.Keys.Concat(catalog.WaveRuntime.Evolutions.Keys).Concat(catalog.WaveRuntime.Items.Keys).Distinct().ToArray();
                foreach(var id in ids)
                {
                    var offer=new CardOfferView(new[]{id,id,id},null,2,2,2);
                    var envelope=WavePresentation.Envelope(catalog,frame,offer,wave);
                    // One detail per ID, with three equal-width offers to exercise the narrowest card geometry.
                    envelope=envelope with { Cards=envelope.Cards.Take(1).ToArray() };
                    UiRunPanels.LandscapeCards(ui,frame,envelope,offer,catalog,displays,(_,__)=>{},()=>{});
                    var hint=ui.Hint("무기와 도구를 함께 골라 전투와 영지를 키우세요.");
                    Assert.That(UiRunPanels.PlaceLandscapeCardHint(ui,hint),Is.True,"Fresh-profile first-card hint must use the reserved header, not require a ScrollRect.");
                    Canvas.ForceUpdateCanvases();
                    var hintParent=(RectTransform)hint.transform.parent.parent;
                    Assert.That(-hintParent.anchoredPosition.y+hintParent.rect.height,Is.LessThanOrEqualTo(48),"Hint must remain above the cards.");
                    var panel=ui.Content.Find("Cards") as RectTransform;
                    foreach(var text in panel.GetComponentsInChildren<Text>())
                    {
                        Assert.That(text.preferredHeight,Is.LessThanOrEqualTo(text.rectTransform.rect.height+.5f),id+" text overflow: "+text.text);
                        if(text.name=="Complete card text")StringAssert.Contains(displays[id].EffectDescription,text.text);
                    }
                    var buttons=panel.GetComponentsInChildren<Button>();Assert.That(buttons.Length,Is.EqualTo(11));
                    foreach(var button in buttons)
                    {
                        var rect=(RectTransform)button.transform;
                        Assert.That(rect.anchoredPosition.x,Is.GreaterThanOrEqualTo(0));
                        Assert.That(rect.anchoredPosition.x+rect.rect.width,Is.LessThanOrEqualTo(panel.rect.width+.5f));
                        Assert.That(-rect.anchoredPosition.y+rect.rect.height,Is.LessThanOrEqualTo(panel.rect.height+.5f));
                    }
                    UnityEngine.Object.DestroyImmediate(panel.gameObject);
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(font);ArtCatalog.ProfileName=priorArtProfile;}
        }
        [TestCase(1920,1080)]
        [TestCase(1440,900)]
        [TestCase(1280,800)]
        public void LandscapeBudgetRetainsBattleAndCapsJoystick(int width,int height)
        {
            Assert.That(UiTokens.LandscapeLoadoutFraction,Is.LessThanOrEqualTo(.12));
            Assert.That(1-UiTokens.LandscapeHudFraction-UiTokens.LandscapeLoadoutFraction,Is.GreaterThanOrEqualTo(.75));
            var scale=width/900f;
            var visual=UiShell.StickPresentationScale(new Vector2(width,height),new Rect(0,height*.11f,width,height*.78f),scale);
            Assert.That(2*UiTokens.StickRadius*scale*visual*1080/height,Is.LessThanOrEqualTo(160.001f));
            Assert.That(UiTokens.LandscapeStickAlpha,Is.InRange(.1f,.7f));
        }
    }
}
