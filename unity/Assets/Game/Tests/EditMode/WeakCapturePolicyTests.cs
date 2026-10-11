using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class WeakCapturePolicyTests
    {
        [Test]
        public void ObservationNeverRequestsThirdWeaponOrEvolution()
        {
            Assert.That(WeakCapturePolicy.Preference(false,false,true,false,2),Is.EqualTo(int.MaxValue));
            Assert.That(WeakCapturePolicy.Preference(false,true,false,false,1),Is.EqualTo(int.MaxValue));
            Assert.That(WeakCapturePolicy.Preference(true,false,false,false,1),Is.LessThan(WeakCapturePolicy.Preference(false,false,true,true,1)));
            Assert.That(WeakCapturePolicy.Preference(false,false,true,false,1),Is.LessThan(int.MaxValue));
            Assert.That(WeakCapturePolicy.Preference(false,false,true,false,1),Is.LessThan(WeakCapturePolicy.Preference(false,false,false,true,1)));
            Assert.That(WeakCapturePolicy.Preference(true,false,false,false,1),Is.LessThan(WeakCapturePolicy.Preference(false,false,true,false,1)));
        }
    }
}
