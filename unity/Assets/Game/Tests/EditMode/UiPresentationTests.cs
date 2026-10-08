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
