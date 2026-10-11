using System;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class HeroGaitProbeTests
    {
        [Test]
        public void NormalRunDoesNotEnableGaitProbe()
            => Assert.That(HeroGaitProbe.Requested(new[]{"game"}),Is.False);
        [Test]
        public void CaptureFlagAloneDoesNotEnableGaitProbe()
            => Assert.That(HeroGaitProbe.Requested(new[]{"game","--autoplay-capture"}),Is.False);
        [Test]
        public void ExplicitProbeRequiresCaptureAndKnownOutput()
        {
            Assert.Throws<ArgumentException>(()=>HeroGaitProbe.Requested(new[]{"game","--hero-gait-probe"}));
            Assert.Throws<ArgumentException>(()=>HeroGaitProbe.Requested(new[]{"game","--hero-gait-probe","--autoplay-capture"}));
            Assert.That(HeroGaitProbe.Requested(new[]{"game","--hero-gait-probe","--autoplay-capture","--capture-output","/tmp/gait"}),Is.True);
        }
    }
}
