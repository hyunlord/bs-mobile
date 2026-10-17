using System.Collections;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class HeroLiveRigTests
    {
        [UnityTest]
        public IEnumerator RealSpriteSkinDeformsSharedPartsWhenUpperSwingChanges()
        {
            var owner = new GameObject("Live hero test");
            var rig = new HeroLiveRig(owner.transform);
            try
            {
                var gait = new HeroGaitSample(0, 0, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, false, HeroGaitReleaseReason.None);
                rig.Present(Vector2.zero, Vector2.one, gait, true, 0, false, -1, Color.white);
                yield return null;
                yield return null;
                Assert.That(rig.SpriteSkinCount, Is.EqualTo(14));
                Assert.That(rig.BoneCount, Is.EqualTo(15));
                Assert.That(rig.DeformedSkinCount, Is.EqualTo(14), "Every cutout must be skinned, not a Transform-only imitation.");
                var first = rig.SampleDeformedVertex(13);
                var leg = rig.SampleDeformedVertex(3);
                rig.Present(Vector2.zero, Vector2.one, gait, true, .55f, false, -1, Color.white);
                yield return null;
                yield return null;
                Assert.That(Vector3.Distance(first, rig.SampleDeformedVertex(13)), Is.GreaterThan(.01f));
                Assert.That(Vector3.Distance(leg, rig.SampleDeformedVertex(3)), Is.LessThan(.0001f), "The upper-body swing must not replace the walking leg pose.");
                var normalSwing = rig.SampleDeformedVertex(13);
                rig.Present(Vector2.zero, Vector2.one, gait, true, .55f, true, -1, Color.white);
                yield return null;
                yield return null;
                Assert.That(Vector3.Distance(normalSwing, rig.SampleDeformedVertex(13)), Is.GreaterThan(.01f), "Evolution must visibly change the sword pose.");
                Assert.That(Vector3.Distance(leg, rig.SampleDeformedVertex(3)), Is.LessThan(.0001f));
                rig.Present(Vector2.zero, Vector2.one, gait, true, .55f, true, .5f, Color.white);
                yield return null;
                yield return null;
                var hit = rig.SampleDeformedVertex(13);
                rig.Present(Vector2.zero, Vector2.one, gait, true, -1, false, .5f, Color.white);
                yield return null;
                yield return null;
                Assert.That(Vector3.Distance(hit, rig.SampleDeformedVertex(13)), Is.LessThan(.0001f), "Hit reaction must take priority over either attack clip.");
                for (var i = 0; i < 10; i++) rig.Present(Vector2.zero, Vector2.one, gait, true, .5f, false, -1, Color.white);
                var allocated = System.GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 60; i++) rig.Present(Vector2.zero, Vector2.one, gait, true, .5f, false, -1, Color.white);
                Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - allocated, Is.EqualTo(0), "Pose updates cannot allocate per frame.");
                rig.Hide();
            }
            finally { rig.Dispose(); Object.Destroy(owner); }
            yield return null;
        }

        [Test]
        public void BothMethodsShareOrderedJointSamplesAndAtlasClipRanges()
        {
            var data = HeroLiveRigData.Load();
            Assert.That(data.frames.Length, Is.EqualTo(16), "The baked action atlas contains sixteen cells.");
            Assert.That(data.gaitFrames.Length, Is.EqualTo(HeroGaitState.WalkFrames + 1));
            Assert.That(data.bones.Length + 1, Is.EqualTo(data.jointNames.Length));
            for (var i = 1; i < data.jointNames.Length; i++)
            {
                Assert.That(data.bones[i - 1].id, Is.EqualTo(data.jointNames[i]));
                Assert.That(data.JointIndex(data.bones[i - 1].parent), Is.LessThan(i), "Live bind transforms require parent-first joints.");
            }
            var nextFrame = 0;
            foreach (var id in new[] { "swing", "evolved-swing", "hit" })
            {
                var clip = data.Clip(id);
                Assert.That(clip.firstFrame, Is.EqualTo(nextFrame));
                Assert.That(clip.frameCount, Is.GreaterThan(1));
                Assert.That(clip.durationSeconds, Is.GreaterThan(0));
                nextFrame += clip.frameCount;
            }
            Assert.That(nextFrame, Is.EqualTo(data.frames.Length));
            foreach (var frame in data.frames) Assert.That(frame.joints.Length, Is.EqualTo(data.jointNames.Length));
            foreach (var frame in data.gaitFrames) Assert.That(frame.joints.Length, Is.EqualTo(data.jointNames.Length));
        }
    }
}
