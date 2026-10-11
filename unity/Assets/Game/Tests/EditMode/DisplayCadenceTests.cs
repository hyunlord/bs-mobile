using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class DisplayCadenceTests
    {
        [TestCase(60d, 1)] [TestCase(120d, 2)] [TestCase(59.94d, 1)] [TestCase(119.88d, 2)]
        [TestCase(180d, 3)] [TestCase(240d, 4)] [TestCase(144d, 0)] [TestCase(75d, 0)]
        [TestCase(0d, 0)] [TestCase(double.NaN, 0)] [TestCase(double.PositiveInfinity, 0)]
        public void DesktopUsesSixtyRatherThanNativeRefresh(double refresh, int expected)
            => Assert.That(DisplayCadence.SyncInterval(refresh, false), Is.EqualTo(expected));

        [TestCase(60d)] [TestCase(120d)]
        public void MobileUsesTargetFrameRate(double refresh)
            => Assert.That(DisplayCadence.SyncInterval(refresh, true), Is.Zero);

        [Test] public void MonitorChangeRecomputesInterval()
        {
            Assert.That(DisplayCadence.SyncInterval(120, false), Is.EqualTo(2));
            Assert.That(DisplayCadence.SyncInterval(60, false), Is.EqualTo(1));
            Assert.That(DisplayCadence.SyncInterval(120, false), Is.EqualTo(2));
        }
    }
}
