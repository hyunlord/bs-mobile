using System;
using Game.App;
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
        [TestCase(0,0,"0%")]
        [TestCase(1,4,"25%")]
        public void SummaryRatiosHandleEmptyAndActualChannels(long amount,double total,string expected)
        {
            Assert.That(UiRunPanels.Ratio(amount,total),Is.EqualTo(expected));
        }
    }
}
