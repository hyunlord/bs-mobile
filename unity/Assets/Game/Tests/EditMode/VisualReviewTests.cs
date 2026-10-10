using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
namespace Tests.EditMode
{
    public sealed class VisualReviewTests
    {
        [Test]
        public void CaptureTickIsStateAfterPreviousAdvanceBeforeCommandsAtThatTick()
        {
            var catalog=CanonicalContent.CreateCatalog();
            var options=new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash);
            var session=new InteractiveSession(catalog,options);
            var commands=new[]{new ReplayCommand(0,0,ReplayCommandKind.SetAimMode,Value:(int)AimMode.Movement),new ReplayCommand(1,0,ReplayCommandKind.Advance),new ReplayCommand(2,1,ReplayCommandKind.Advance),new ReplayCommand(3,2,ReplayCommandKind.SetAimMode,Value:(int)AimMode.Movement)};
            var index=0;
            while(RunCoordinator.ApplyVisualReviewCommandBeforeTick(session,commands,ref index,2)){}
            Assert.That(session.View.CaptureFrame().Tick,Is.EqualTo(2));
            Assert.That(index,Is.EqualTo(3));
            Assert.That(session.NextSequence,Is.EqualTo(3));
            Assert.That(RunCoordinator.ApplyVisualReviewCommandBeforeTick(session,commands,ref index,2),Is.False);
        }
    }
}
