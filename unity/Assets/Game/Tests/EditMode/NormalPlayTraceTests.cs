using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class NormalPlayTraceTests
    {
        [Test]
        public void OptionalSeedUsesOnlyExplicitNormalTraceConfiguration()
        {
            Assert.That(NormalPlayTrace.ParseRequestedSeed(new[] { "app" }), Is.Null);
            Assert.That(NormalPlayTrace.ParseRequestedSeed(new[] { "app", "--smoothness-trace", "--smoothness-seed", "1078312934" }), Is.EqualTo(1078312934));
            Assert.Throws<System.ArgumentException>(() => NormalPlayTrace.ParseRequestedSeed(new[] { "--smoothness-seed", "1" }));
            Assert.Throws<System.ArgumentException>(() => NormalPlayTrace.ParseRequestedSeed(new[] { "--smoothness-trace", "--smoothness-seed" }));
            Assert.Throws<System.ArgumentException>(() => NormalPlayTrace.ParseRequestedSeed(new[] { "--smoothness-trace", "--smoothness-seed", "2147483648" }));
        }

        [TestCase(false, 1f, 0f)]
        [TestCase(true, .8f, .6f)]
        public void ScriptDirectionPreservesMagnitudeAcrossCardinalAndObliqueProtocols(bool oblique, float x, float y)
        {
            var direction = NormalPlayTrace.ScriptDirection(oblique);
            Assert.That(direction.x, Is.EqualTo(x));
            Assert.That(direction.y, Is.EqualTo(y));
            Assert.That(direction.magnitude, Is.EqualTo(1).Within(.000001));
            Assert.That((direction * .25f).magnitude, Is.EqualTo(.25f).Within(.000001));
        }

        [TestCase(false, -1f, 0f)]
        [TestCase(true, -.8f, -.6f)]
        public void LeftOptionReversesDirectionWithoutChangingMagnitude(bool oblique,float x,float y)
        {
            var direction=NormalPlayTrace.ScriptDirection(oblique,true);
            Assert.That(direction.x,Is.EqualTo(x));
            Assert.That(direction.y,Is.EqualTo(y));
            Assert.That(direction.magnitude,Is.EqualTo(1).Within(.000001));
            Assert.That(direction,Is.EqualTo(-NormalPlayTrace.ScriptDirection(oblique)));
        }

        [Test]
        public void StartupLoggingAcceptsADestroyedSelectedUiObject()
        {
            var selection = new UnityEngine.GameObject("destroyed selection");
            Assert.That(NormalPlayTrace.ObservedObjectName(selection), Is.EqualTo("destroyed selection"));
            UnityEngine.Object.DestroyImmediate(selection);
            Assert.That(NormalPlayTrace.ObservedObjectName(selection), Is.EqualTo("none"));
            Assert.That(NormalPlayTrace.ObservedObjectName(null), Is.EqualTo("none"));
        }

        [TestCase(-1, -1)]
        [TestCase(0, 0)]
        [TestCase(3.999, 0)]
        [TestCase(4, 1)]
        [TestCase(8, 2)]
        [TestCase(12, 3)]
        [TestCase(16, 4)]
        [TestCase(18, 4)]
        public void ScriptedSegmentsHaveExplicitFourSecondMovementAndReleaseWindows(double seconds, int phase)
            => Assert.That(NormalPlayTrace.MovementPhase(seconds), Is.EqualTo(phase));
    }
}
