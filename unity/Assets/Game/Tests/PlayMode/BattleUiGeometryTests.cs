#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Collections;
using System.Linq;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tests.PlayMode
{
    public sealed class BattleUiGeometryTests
    {
        [UnityTest]
        public IEnumerator ApprovedHudFitsPortraitAndLongViewportWithPauseTouchTarget()
        {
            var priorWidth = Screen.width;
            var priorHeight = Screen.height;
            var font = FontProvider.Create(Array.Empty<string>());
            GameObject owner = null;
            try
            {
                foreach (var height in new[] { 844, 932 })
                {
                    PlayModeWindow.SetCustomRenderingResolution(390, (uint)height, "Approved battle HUD geometry");
                    yield return null;
                    yield return null;
                    owner = new GameObject("Battle HUD geometry", typeof(RectTransform));
                    var ui = owner.AddComponent<UiShell>();
                    ui.Initialize(font, true);
                    ui.SetPortraitMobileSimulation(true);
                    var hud = new PortraitHud(ui, null, () => { }, false);
                    Canvas.ForceUpdateCanvases();
                    var rectangles = ui.GetComponentsInChildren<RectTransform>(true);
                    var paper = rectangles.Single(rect => rect.name == "Battle HUD parchment");
                    var pause = rectangles.Single(rect => rect.name == "멈춤");
                    var paperBounds = Pixels(paper);
                    var pauseBounds = Pixels(pause);
                    Assert.That(hud.ReservedTopPixels, Is.LessThanOrEqualTo(ui.SafePixels.height * .1f + .01f));
                    Assert.That(paperBounds.height, Is.LessThanOrEqualTo(hud.ReservedTopPixels + .01f));
                    Assert.That(paperBounds.yMax, Is.LessThanOrEqualTo(ui.SafePixels.yMax + .01f));
                    Assert.That(pauseBounds.width / ui.Canvas.scaleFactor, Is.GreaterThanOrEqualTo(48));
                    Assert.That(pauseBounds.height / ui.Canvas.scaleFactor, Is.GreaterThanOrEqualTo(48));
                    var track = rectangles.Single(rect => rect.name == "Blue XP track").GetComponent<Image>();
                    var fill = rectangles.Single(rect => rect.name == "Experience").Find("Fill").GetComponent<Image>();
                    Assert.That(track.color, Is.EqualTo(PortraitTokens.ExperienceTrack));
                    Assert.That(fill.color, Is.EqualTo(PortraitTokens.ExperienceFill));
                    Assert.That(fill.color.b, Is.GreaterThan(fill.color.r));
                    foreach (var name in new[] { "Weapon category", "Tool category", "Weapon overflow", "Tool overflow" })
                    {
                        var labelBounds = Pixels(rectangles.Single(rect => rect.name == name));
                        Assert.That(labelBounds.xMin, Is.GreaterThanOrEqualTo(paperBounds.xMin));
                        Assert.That(labelBounds.xMax, Is.LessThanOrEqualTo(paperBounds.xMax));
                        Assert.That(labelBounds.yMin, Is.GreaterThanOrEqualTo(paperBounds.yMin - .01f));
                    }
                    foreach (var item in rectangles.Where(rect => rect.name.StartsWith("Equipment ", StringComparison.Ordinal) && rect.name != "Equipment pool"))
                    {
                        var bounds = Pixels(item);
                        Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(paperBounds.xMin));
                        Assert.That(bounds.xMax, Is.LessThanOrEqualTo(paperBounds.xMax));
                        Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(paperBounds.yMin - .01f));
                    }
                    UnityEngine.Object.Destroy(owner);
                    owner = null;
                    yield return null;
                }
            }
            finally
            {
                if (owner != null) UnityEngine.Object.Destroy(owner);
                UnityEngine.Object.Destroy(font);
                PlayModeWindow.SetCustomRenderingResolution((uint)priorWidth, (uint)priorHeight, "Restored battle HUD viewport");
            }
        }

        static Rect Pixels(RectTransform rectangle)
        {
            var corners = new Vector3[4];
            rectangle.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
#endif
