using System;
using System.Linq;
using Game.App;
using Game.App.Generated;
using SowSiege.Core;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class UiPresentationTests
    {
        [TestCase(.5f)]
        [TestCase(1f)]
        [TestCase(2f)]
        public void JoystickArtStaysCenteredAndThumbFitsWithoutChangingInput(float scale)
        {
            var owner=new GameObject("Joystick visual regression",typeof(RectTransform));
            var events=new GameObject("Joystick test events",typeof(UnityEngine.EventSystems.EventSystem));
            var font=FontProvider.Create(Array.Empty<string>());
            try
            {
                var ui=owner.AddComponent<UiShell>();ui.Initialize(font);ui.Canvas.scaleFactor=scale;
                var stick=ui.TouchSurface.gameObject.AddComponent<Game.Input.FloatingStick>();stick.RadiusCanvasUnits=UiTokens.StickRadius;
                var data=new UnityEngine.EventSystems.PointerEventData(events.GetComponent<UnityEngine.EventSystems.EventSystem>()){pointerId=7,position=new Vector2(150,250)};
                stick.OnPointerDown(data);
                for(var i=0;i<8;i++)
                {
                    var direction=new Vector2(Mathf.Cos(i*Mathf.PI/4),Mathf.Sin(i*Mathf.PI/4));
                    data.position=stick.Origin+direction*stick.Radius;stick.OnDrag(data);
                    var expected=new SowSiege.Core.PlayerInput((short)Mathf.RoundToInt(direction.x*SowSiege.Core.PlayerInput.Scale),(short)Mathf.RoundToInt(direction.y*SowSiege.Core.PlayerInput.Scale));
                    Assert.That(stick.Sample,Is.EqualTo(expected));
                    ui.ShowStick(true,stick.Origin,stick.Offset);
                    var knob=(RectTransform)owner.transform.Find("Joystick/Knob");
                    Assert.That(knob.anchoredPosition.magnitude+UiTokens.StickThumbDiameter/2,Is.LessThanOrEqualTo(UiTokens.StickRadius+.001f));
                }
                var baseImage=owner.transform.Find("Joystick").GetComponent<UnityEngine.UI.Image>();
                var thumbImage=owner.transform.Find("Joystick/Knob").GetComponent<UnityEngine.UI.Image>();
                Assert.That(baseImage.sprite.rect.size,Is.EqualTo(new Vector2(174,174)));
                Assert.That(thumbImage.sprite.rect.size,Is.EqualTo(new Vector2(153,153)));
                Assert.That(baseImage.sprite.pivot,Is.EqualTo(baseImage.sprite.rect.size*.5f));
                stick.OnPointerUp(data);ui.ShowStick(stick.Active,stick.Origin,stick.Offset);
                Assert.That(stick.Active,Is.False);Assert.That(stick.Sample,Is.EqualTo(default(SowSiege.Core.PlayerInput)));
                Assert.That(baseImage.gameObject.activeSelf,Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(events);UnityEngine.Object.DestroyImmediate(font); }
        }
        [TestCase(1920,1080,330,880)]
        [TestCase(900,1600,280,1400)]
        public void JoystickPresentationFitsUsableScreenWithoutChangingLogicalRadius(int width,int height,float bottom,float top)
        {
            var scale=width/900f;
            var usable=Rect.MinMaxRect(0,bottom,width,top);
            var visualScale=UiShell.StickPresentationScale(new Vector2(width,height),usable,scale);
            var radius=UiTokens.StickRadius*scale*visualScale;
            if(height>width)Assert.That(visualScale,Is.EqualTo(1),"Portrait art size stays unchanged when it fits.");
            Assert.That(UiShell.StickPresentationCenter(usable.center,usable,radius),Is.EqualTo(usable.center),"Already safe touch origins stay in place.");
            var center=UiShell.StickPresentationCenter(new Vector2(width*.25f,height*.2f),usable,radius);
            Assert.That(center.x-radius,Is.GreaterThanOrEqualTo(usable.xMin-.001f));
            Assert.That(center.x+radius,Is.LessThanOrEqualTo(usable.xMax+.001f));
            Assert.That(center.y-radius,Is.GreaterThanOrEqualTo(usable.yMin-.001f));
            Assert.That(center.y+radius,Is.LessThanOrEqualTo(usable.yMax+.001f));
            for(var i=0;i<8;i++)
            {
                var direction=new Vector2(Mathf.Cos(i*Mathf.PI/4),Mathf.Sin(i*Mathf.PI/4));
                var visual=UiShell.StickVisualOffset(direction*UiTokens.StickRadius*scale,scale)*scale*visualScale;
                Assert.That(visual.magnitude+UiTokens.StickThumbDiameter*.5f*scale*visualScale,Is.LessThanOrEqualTo(radius+.001f));
            }
        }

        [Test]
        public void AuthoredSettingsDoNotEmitChangesUntilUserInteraction()
        {
            var owner=new GameObject("UI setting regression",typeof(RectTransform));
            var font=FontProvider.Create(Array.Empty<string>());
            try
            {
                var ui=owner.AddComponent<UiShell>();ui.Initialize(font);var panel=ui.Panel("Settings");var calls=0;var enabled=false;var volume=0f;
                var toggle=ui.Toggle(panel,"진동",false,value=>{calls++;enabled=value;});
                var slider=ui.Slider(panel,"음량",.4f,value=>{calls++;volume=value;});
                Assert.That(calls,Is.Zero);Assert.That(slider.value,Is.EqualTo(.4f));
                Assert.That(toggle.image.sprite,Is.Not.Null);Assert.That(toggle.image.type,Is.EqualTo(UnityEngine.UI.Image.Type.Sliced));Assert.That(toggle.image.sprite.border.x,Is.GreaterThan(0));Assert.That(slider.targetGraphic,Is.Not.Null);
                toggle.onClick.Invoke();slider.value=.8f;
                Assert.That(calls,Is.EqualTo(2));Assert.That(enabled,Is.True);Assert.That(volume,Is.EqualTo(.8f));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(font); }
        }
        [Test]
        public void HudKeepsIconObjectsForFreshSnapshotsAndLevelChangesButRefreshesChangedIds()
        {
            var owner=new GameObject("HUD identity regression",typeof(RectTransform));
            var font=FontProvider.Create(Array.Empty<string>());
            try
            {
                var ui=owner.AddComponent<UiShell>();ui.Initialize(font);
                var catalog=CanonicalContent.CreateCatalog();
                var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
                var frame=session.View.CaptureFrame();var fp=session.View.CaptureFirstPlayable();
                var weapon=catalog.Weapons.Keys.First();var charter=catalog.Runtime.Charters.Keys.First();
                var hud=new UiHud(ui,catalog,null);
                hud.Present(frame with { Equipment=new[]{new EquipmentView(weapon,1)} },fp with { Charters=Array.Empty<string>() });
                var icons=ui.Content.Find("HUD/Equipment");var original=icons.GetChild(0);
                hud.Present(frame with { Equipment=new[]{new EquipmentView(weapon,2)} },fp with { Charters=new string[0] });
                Assert.That(icons.childCount,Is.EqualTo(1));Assert.That(icons.GetChild(0),Is.SameAs(original),"Fresh arrays and rank changes must not rebuild unchanged icon IDs.");
                hud.Present(frame with { Equipment=new[]{new EquipmentView(weapon,2)} },fp with { Charters=new[]{charter} });
                Assert.That(icons.childCount,Is.EqualTo(2));Assert.That(icons.GetChild(1).name,Is.EqualTo("Icon "+charter));
                hud.Present(frame with { Equipment=new[]{new EquipmentView(weapon,2)} },null);
                Assert.That(icons.childCount,Is.EqualTo(1));Assert.That(icons.GetChild(0).name,Is.EqualTo("Icon "+weapon));
                var other=catalog.Weapons.Keys.First(id=>id!=weapon);
                hud.Present(frame with { Equipment=new[]{new EquipmentView(weapon,1),new EquipmentView(other,1)} },null);
                hud.Present(frame with { Equipment=new[]{new EquipmentView(other,1),new EquipmentView(weapon,1)} },null);
                Assert.That(icons.GetChild(0).name,Is.EqualTo("Icon "+other));Assert.That(icons.GetChild(1).name,Is.EqualTo("Icon "+weapon));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(font); }
        }
        [TestCase(0,0,"0%")]
        [TestCase(1,4,"25%")]
        public void SummaryRatiosHandleEmptyAndActualChannels(long amount,double total,string expected)
        {
            Assert.That(UiRunPanels.Ratio(amount,total),Is.EqualTo(expected));
        }
    }
}
