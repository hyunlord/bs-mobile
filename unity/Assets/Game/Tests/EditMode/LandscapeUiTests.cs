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
        [TestCase(1920,1080)]
        [TestCase(1440,900)]
        [TestCase(1280,800)]
        public void EveryWaveCardFitsAllTextAndActionsInLandscape(int width,int height)
        {
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
