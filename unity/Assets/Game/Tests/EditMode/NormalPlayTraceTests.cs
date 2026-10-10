using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class NormalPlayTraceTests
    {
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
