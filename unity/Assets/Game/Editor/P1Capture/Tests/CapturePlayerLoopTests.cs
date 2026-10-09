using NUnit.Framework;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Game.P1Capture.Tests
{
    public sealed class CapturePlayerLoopTests
    {
        [Test]
        public void OwnDelegateRunsImmediatelyAfterScriptLateUpdateAndRemovalPreservesOthers()
        {
            var called = false;
            var loop = new PlayerLoopSystem
            {
                type = typeof(PreLateUpdate),
                subSystemList = new[]
                {
                    new PlayerLoopSystem { type = typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate) },
                    new PlayerLoopSystem { type = typeof(CapturePlayerLoopTests) }
                }
            };
            var installed = false;
            loop = CapturePlayerLoop.Insert(loop, () => called = true, ref installed);
            Assert.That(installed, Is.True);
            Assert.That(loop.subSystemList.Length, Is.EqualTo(3));
            Assert.That(loop.subSystemList[1].type, Is.EqualTo(typeof(CapturePlayerLoop)));
            loop.subSystemList[1].updateDelegate();
            Assert.That(called, Is.True);
            loop = CapturePlayerLoop.Remove(loop);
            Assert.That(loop.subSystemList.Length, Is.EqualTo(2));
            Assert.That(loop.subSystemList[0].type, Is.EqualTo(typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate)));
            Assert.That(loop.subSystemList[1].type, Is.EqualTo(typeof(CapturePlayerLoopTests)));
        }

        [Test]
        public void ReinstallDoesNotAccumulateCaptureNodes()
        {
            var loop = new PlayerLoopSystem { subSystemList = new[] { new PlayerLoopSystem { type = typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate) } } };
            for (var i = 0; i < 3; i++)
            {
                loop = CapturePlayerLoop.Remove(loop);
                var installed = false;
                loop = CapturePlayerLoop.Insert(loop, () => { }, ref installed);
                Assert.That(installed, Is.True);
                Assert.That(loop.subSystemList.Length, Is.EqualTo(2));
            }
        }
    }
}
