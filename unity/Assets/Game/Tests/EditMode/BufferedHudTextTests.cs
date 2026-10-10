using System;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

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
        static void Present(BufferedHudText label, int value)
        {
            label.Begin(); label.Append(value / 60, 2); label.Append(":"); label.Append(value % 60, 2);
            label.Append(" · 레벨 "); label.Append(value); label.End();
        }
    }
}
