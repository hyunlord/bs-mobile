using Game.View;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class WorldHintQueueTests
    {
        [Test] public void SteadyQueueStorageDoesNotAllocate()
        {
            if(!Game.App.AllocationCounterProbe.CurrentThreadAvailable())Assert.Ignore("Runtime allocation counter is unavailable; .NET probe provides allocation evidence.");
            var queue=new WorldHintQueue();
            queue.Enqueue(WorldHintKind.HarvestExperience,15);queue.Present(0,1);
            var before=System.GC.GetAllocatedBytesForCurrentThread();
            for(var i=0;i<10000;i++){queue.Enqueue(WorldHintKind.HarvestExperience,15);queue.Present(.5f,1);}
            var allocated=System.GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.That(allocated,Is.Zero);
            Assert.That(queue.Present(1,1),Is.EqualTo("수확 +150000 경험치"));
        }

        [Test] public void OnlyOneHintIsShownInArrivalOrderWithoutOverwritingTheActiveHint()
        {
            var queue=new WorldHintQueue();
            queue.Enqueue(WorldHintKind.Ripe);queue.Enqueue(WorldHintKind.HarvestExperience,15);queue.Enqueue(WorldHintKind.Repair);
            Assert.That(queue.Present(0,1),Is.EqualTo("익음"));
            Assert.That(queue.Present(.9f,1),Is.EqualTo("익음"));
            Assert.That(queue.Present(1,1),Is.EqualTo("수확 +15 경험치"));
            Assert.That(queue.Present(2,1),Is.EqualTo("수리 중"));
            Assert.That(queue.Present(3,1),Is.Null);
        }

        [TestCase(7)] [TestCase(8)]
        public void RecordedBurstPreservesExperienceAndPendingKindsWithoutReplacingActiveCaption(int burst)
        {
            var queue=new WorldHintQueue();
            queue.Enqueue(WorldHintKind.HarvestExperience,15);
            Assert.That(queue.Present(0,.9f),Is.EqualTo("수확 +15 경험치"));
            queue.Enqueue(WorldHintKind.Ripe);queue.Enqueue(WorldHintKind.Repair);queue.Enqueue(WorldHintKind.CompletionExperience,20);
            for(var i=0;i<burst;i++)
            {queue.Enqueue(WorldHintKind.HarvestExperience,15);queue.Enqueue(WorldHintKind.Ripe);queue.Enqueue(WorldHintKind.Repair);queue.Enqueue(WorldHintKind.CompletionExperience,2);}
            Assert.That(queue.Count,Is.EqualTo(WorldHintQueue.Capacity));
            Assert.That(queue.Present(.8f,.9f),Is.EqualTo("수확 +15 경험치"));
            Assert.That(queue.Present(1,.9f),Is.EqualTo("익음"));
            Assert.That(queue.Present(2,.9f),Is.EqualTo("수리 중"));
            Assert.That(queue.Present(3,.9f),Is.EqualTo("완료 +"+(20+burst*2)+" 경험치"));
            Assert.That(queue.Present(4,.9f),Is.EqualTo("수확 +"+(burst*15)+" 경험치"));
            Assert.That(queue.Present(5,.9f),Is.Null);
            queue.Clear();Assert.That(queue.Count,Is.Zero);
        }
    }
}
