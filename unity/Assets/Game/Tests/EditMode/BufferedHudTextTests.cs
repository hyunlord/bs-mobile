using System;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Tests.EditMode
{
    public sealed class BufferedHudTextTests
    {
        [Test]
        public void ChangingValuesFormatExactlyWithoutManagedStringAllocation()
        {
            var owner = new GameObject("Buffered HUD regression", typeof(RectTransform));
            var font = FontProvider.Create(Array.Empty<string>());
            try
            {
                var source = owner.AddComponent<Text>(); source.font = font; source.fontSize = 24;
                var label = BufferedHudText.Create(source);
                label.Begin(); label.Append("체력 "); label.Append(long.MinValue); label.Append("/"); label.Append(long.MaxValue); label.End();
                Assert.That(label.CaptureTextForTesting(), Is.EqualTo("체력 -9223372036854775808/9223372036854775807"));
                for (var i = 0; i < 100; i++) Present(label, i);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++) Present(label, i);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero, "Formatting changing values must not create managed strings.");
                Assert.That(label.CaptureTextForTesting(), Is.EqualTo("16:39 · 레벨 999"));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(font); }
        }
        [Test]
        public void FontAtlasRebuiltInsideCanvasPassKeepsExistingHudGeometry()
        {
            var owner = new GameObject("Font atlas regression", typeof(RectTransform), typeof(Canvas));
            var font = FontProvider.Create(Array.Empty<string>());
            var rebuilds = 0;
            Action<Font> observed = value => { if (value == font) rebuilds++; };
            Font.textureRebuilt += observed;
            try
            {
                owner.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var rect = UiShell.Rect("Existing top HUD", owner.transform); rect.sizeDelta = new Vector2(400, 80);
                var source = rect.gameObject.AddComponent<Text>(); source.font = font; source.fontSize = 32;
                var label = BufferedHudText.Create(source); Present(label, 56);
                Assert.That(label.GetComponent<CanvasRenderer>(), Is.Not.Null, "Custom graphics require their own CanvasRenderer before OnEnable.");
                var lower = UiShell.Rect("Equipment rank", owner.transform); lower.sizeDelta = new Vector2(60, 40);
                var lowerSource = lower.gameObject.AddComponent<Text>(); lowerSource.font = font; lowerSource.fontSize = 24;
                var rank = BufferedHudText.Create(lowerSource); rank.Begin(); rank.Append(5); rank.End();
                Canvas.ForceUpdateCanvases();
                var mesh = label.canvasRenderer.GetMesh();
                Assert.That(mesh.bounds.size.x, Is.GreaterThan(0));
                var before = rebuilds;
                var glyphs = new char[512];
                for (var i = 0; i < glyphs.Length; i++) glyphs[i] = (char)(0xac00 + i);
                var probe = new AtlasRebuildElement(owner.transform, font, new string(glyphs));
                CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(probe);
                Canvas.ForceUpdateCanvases();
                Assert.That(rebuilds, Is.GreaterThan(before), "The regression must exercise a real dynamic font atlas rebuild inside the Canvas pass.");
                Canvas.ForceUpdateCanvases();
                mesh = label.canvasRenderer.GetMesh();
                Assert.That(mesh.vertexCount, Is.EqualTo(label.CaptureTextForTesting().Length * 4), "Nested font callbacks must not leave another label in uGUI's shared vertex buffer.");
                Assert.That(mesh.bounds.size.x, Is.GreaterThan(0));
                Assert.That(mesh.bounds.size.y, Is.GreaterThan(0));
                Assert.That(label.CaptureTextForTesting(), Is.EqualTo("00:56 · 레벨 56"));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Font.textureRebuilt -= observed;
                UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(font);
            }
        }
        sealed class AtlasRebuildElement : ICanvasElement
        {
            readonly Font font;
            readonly string characters;
            public Transform transform { get; }
            public AtlasRebuildElement(Transform owner, Font font, string characters)
            { transform = owner; this.font = font; this.characters = characters; }
            public void Rebuild(CanvasUpdate update)
            {
                if (update == CanvasUpdate.PreRender) font.RequestCharactersInTexture(characters, 96);
            }
            public bool IsDestroyed() => transform == null;
            public void LayoutComplete() { }
            public void GraphicUpdateComplete() { }
        }
        static void Present(BufferedHudText label, int value)

        {
            label.Begin(); label.Append(value / 60, 2); label.Append(":"); label.Append(value % 60, 2);
            label.Append(" · 레벨 "); label.Append(value); label.End();
        }
    }
}
