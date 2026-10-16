using System;
using System.IO;
using System.Linq;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class HeroSheetTests
    {
        const float Stride = 1;
        static float Ticks(int ticks60) => ticks60 / 60f;

        [Test] public void MovingAttackShowsTheKeyPoseFor7TicksThenWalks()
        {
            var s = new HeroActionSelector();
            Assert.That(s.Offer(1, "core:iron_blade", HeroAction.Slash, false, 10, 1, true), Is.True);
            var key = s.Pose(1 + Ticks(3), true, false, false, 0, Stride);
            Assert.That((key.Clip, key.Action), Is.EqualTo((HeroClip.Key, HeroAction.Slash)));
            Assert.That(s.Pose(1 + Ticks(8), true, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Walk));
            Assert.That((s.KeysShown, s.FullShown), Is.EqualTo((1, 0)));
        }

        [Test] public void StandingAttackPlaysTheFullActionFrameByFrameThenIdles()
        {
            var s = new HeroActionSelector();
            s.Offer(1, "core:iron_blade", HeroAction.Slash, false, 10, 2, false);
            for (var frame = 0; frame < HeroSheetSpec.SlashFrames; frame++)
            {
                var pose = s.Pose(2 + Ticks(frame * HeroSheetSpec.SlashTicks + 1), false, false, false, 0, Stride);
                Assert.That((pose.Clip, pose.Frame), Is.EqualTo((HeroClip.Action, frame)));
            }
            Assert.That(s.Pose(2 + Ticks(HeroSheetSpec.SlashFrames * HeroSheetSpec.SlashTicks + 1), false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Idle));
            var aim = new HeroActionSelector();
            aim.Offer(1, "core:canal_bow", HeroAction.Aim, false, 10, 0, false);
            Assert.That(aim.Pose(Ticks(5 * HeroSheetSpec.ActionTicks + 1), false, false, false, 0, Stride).Frame, Is.EqualTo(5));
        }

        [Test] public void MovingDuringAFullActionCutsToWalkingAtOnce()
        {
            var s = new HeroActionSelector();
            s.Offer(1, "core:muster_horn", HeroAction.Blow, false, 10, 0, false);
            Assert.That(s.Pose(Ticks(15), false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Action));
            Assert.That(s.Pose(Ticks(16), true, false, false, .01f, Stride).Clip, Is.EqualTo(HeroClip.Walk));
            Assert.That(s.Pose(Ticks(17), false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Idle), "the cut action does not resume");
            Assert.That(s.CutByMovement, Is.EqualTo(1));
        }

        [Test] public void SameTickPicksEvolvedThenDamageThenLowerWeaponId()
        {
            var s = new HeroActionSelector();
            s.Offer(5, "core:iron_blade", HeroAction.Slash, false, 30, 1, false);
            s.Offer(5, "core:canal_bow", HeroAction.Aim, false, 20, 1, false);          // lower damage: dropped
            Assert.That(s.Pose(1.01f, false, false, false, 0, Stride).Action, Is.EqualTo(HeroAction.Slash));
            s.Offer(5, "core:storm_fork", HeroAction.Cast, true, 5, 1, false);          // evolved wins
            Assert.That(s.Pose(1.02f, false, false, false, 0, Stride).Action, Is.EqualTo(HeroAction.Cast));
            var ties = new HeroActionSelector();
            ties.Offer(7, "core:ditch_pike", HeroAction.Slash, false, 10, 1, false);
            ties.Offer(7, "core:canal_bow", HeroAction.Aim, false, 10, 1, false);       // same damage: lower id (canal < ditch) wins
            Assert.That(ties.Pose(1.01f, false, false, false, 0, Stride).Action, Is.EqualTo(HeroAction.Aim));
        }

        [Test] public void HigherPriorityReplacesFromTheStartAndTheSameActionWaitsPoint4Seconds()
        {
            var s = new HeroActionSelector();
            s.Offer(1, "core:canal_bow", HeroAction.Aim, false, 10, 0, false);
            Assert.That(s.Offer(4, "core:muster_horn", HeroAction.Blow, false, 5, .1f, false), Is.False, "lower priority during an action");
            Assert.That(s.Offer(5, "core:iron_blade", HeroAction.Slash, false, 30, .2f, false), Is.True);
            Assert.That(s.Pose(.21f, false, false, false, 0, Stride).Frame, Is.EqualTo(0));
            Assert.That(s.Replaced, Is.EqualTo(1));
            Assert.That(s.Offer(20, "core:iron_blade", HeroAction.Slash, false, 30, 1.5f, true), Is.True);
            Assert.That(s.Offer(25, "core:iron_blade", HeroAction.Slash, false, 30, 1.85f, true), Is.False, "0.35 s after the same action");
            Assert.That(s.Offer(26, "core:iron_blade", HeroAction.Slash, false, 30, 1.91f, true), Is.True, "0.41 s after");
        }

        [Test] public void HeroHitOverridesTheActionWhichDoesNotResume()
        {
            var s = new HeroActionSelector();
            s.Offer(1, "core:iron_blade", HeroAction.Slash, false, 10, 0, false);
            var hit = s.Pose(Ticks(10), false, true, false, 0, Stride);
            Assert.That((hit.Clip, hit.Frame), Is.EqualTo((HeroClip.Hit, 0)));
            Assert.That(s.Pose(Ticks(10 + 6), false, true, false, 0, Stride).Frame, Is.EqualTo(0));
            Assert.That(s.Pose(Ticks(10 + 8), false, true, false, 0, Stride).Frame, Is.EqualTo(1), "r5 recoil holds 7 ticks");
            Assert.That(s.Pose(Ticks(10 + 40), false, true, false, 0, Stride).Frame, Is.EqualTo(1), "the last hit frame holds while the hit lasts");
            Assert.That(s.Offer(9, "core:canal_bow", HeroAction.Aim, false, 50, Ticks(20), false), Is.False, "attacks during the hit are dropped");
            Assert.That(s.Pose(Ticks(30), false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Idle));
            Assert.That(s.Pose(Ticks(31), false, false, true, 0, Stride).Clip, Is.EqualTo(HeroClip.Death));
        }

        [Test] public void WalkCycleFollowsDistanceSoPlaybackMatchesSpeed()
        {
            var s = new HeroActionSelector();
            Assert.That(s.Pose(0, true, false, false, .5f * Stride, Stride).Frame, Is.EqualTo(4), "half a stride = half the 8-frame cycle");
            Assert.That(s.Pose(0, true, false, false, .125f * Stride, Stride).Frame, Is.EqualTo(5));
            Assert.That(s.Pose(10, true, false, false, 0, Stride).Frame, Is.EqualTo(5), "standing still in time does not advance the walk");
            Assert.That(s.Pose(0, false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Idle));
            Assert.That(s.Pose(Ticks(HeroSheetSpec.IdleTicks * 7 + 1), false, false, false, 0, Stride).Frame, Is.EqualTo(1), "idle loops its 6 frames");
        }

        [Test] public void WeaponMappingFollowsTheFeedbackTable()
        {
            Assert.That(HeroSheetSpec.ActionFor("core:iron_blade", false, "attack"), Is.EqualTo(HeroAction.Slash));
            Assert.That(HeroSheetSpec.ActionFor("core:ditch_pike", false, "attack"), Is.EqualTo(HeroAction.Slash));
            Assert.That(HeroSheetSpec.ActionFor("core:canal_bow", true, "attack"), Is.EqualTo(HeroAction.Aim));
            Assert.That(HeroSheetSpec.ActionFor("core:granary_sling", false, "attack"), Is.EqualTo(HeroAction.Aim));
            Assert.That(HeroSheetSpec.ActionFor("core:storm_fork", false, "attack"), Is.EqualTo(HeroAction.Cast));
            Assert.That(HeroSheetSpec.ActionFor("core:muster_horn", false, "attack"), Is.EqualTo(HeroAction.Blow));
            Assert.That(HeroSheetSpec.ActionFor("core:ward_orbit", false, "hit"), Is.EqualTo(HeroAction.None), "orbiting shields: no action");
            Assert.That(HeroSheetSpec.ActionFor("core:ward_orbit", true, "hit"), Is.EqualTo(HeroAction.Throw));
            Assert.That(HeroSheetSpec.ActionFor("core:spore_fan", true, "attack"), Is.EqualTo(HeroAction.None));
            Assert.That(HeroSheetSpec.ActionFor("core:iron_blade", false, "hit"), Is.EqualTo(HeroAction.None));
        }

        // #268: the hero must resolve only roles every profile registers, with the delivered frame counts.
        [TestCase("first-playable-manifest.json")]
        [TestCase("wave1a-manifest.json")]
        public void EveryProfileRegistersTheSameKnightSheetRoles(string manifest)
        {
            var m = JsonUtility.FromJson<ArtManifest>(File.ReadAllText(Path.Combine(Application.dataPath, "Art", manifest)));
            int Frames(string role) => m.roles.Single(r => r.id == HeroSheetSpec.RolePrefix + role).frames.Length;
            Assert.That(Frames("idle"), Is.EqualTo(HeroSheetSpec.IdleFrames));
            Assert.That(Frames("walk"), Is.EqualTo(HeroSheetSpec.WalkFrames));
            Assert.That(Frames("hit"), Is.EqualTo(HeroSheetSpec.HitFrames));
            Assert.That(Frames("death"), Is.EqualTo(1));
            Assert.That(Frames("action.slash"), Is.EqualTo(HeroSheetSpec.SlashFrames));
            foreach (var a in new[] { "aim", "cast", "blow", "throw" }) Assert.That(Frames("action." + a), Is.EqualTo(HeroSheetSpec.ActionFrames), a);
            foreach (var a in HeroSheetSpec.ActionNames.Skip(1)) Assert.That(Frames("key." + a), Is.EqualTo(1), a);
            foreach (var state in new[] { "idle", "walk", "hit", "death" })
                Assert.That(m.bindings.Single(b => b.kind == "hero" && b.contentId == "core:frontier_knight" && b.state == state).roleId, Is.EqualTo(HeroSheetSpec.RolePrefix + state));
            Assert.That(m.roles.Where(r => r.id.StartsWith(HeroSheetSpec.RolePrefix, StringComparison.Ordinal)).All(r => r.atlas == "hero-knight"), Is.True);
        }

        // The runtime timings must be the delivered frameTicks60 recorded by the pack tool.
        [Test] public void FrameTimingsMatchTheDeliveredSheets()
        {
            var text = File.ReadAllText(Path.Combine(Application.dataPath, "../../tools/art-authoring/knight-sheets/packed-frames.json"));
            int TicksOf(string clip)
            {
                var at = text.IndexOf("\"clip\": \"" + clip + "\"", StringComparison.Ordinal);
                Assert.That(at, Is.GreaterThanOrEqualTo(0), clip);
                var t = text.IndexOf("\"frameTicks60\": ", at, StringComparison.Ordinal) + "\"frameTicks60\": ".Length;
                return int.Parse(new string(text.Skip(t).TakeWhile(char.IsDigit).ToArray()));
            }
            Assert.That(TicksOf("hero-a-walk"), Is.EqualTo(HeroSheetSpec.WalkTicks));
            Assert.That(TicksOf("hero-a-slash"), Is.EqualTo(HeroSheetSpec.SlashTicks));
            foreach (var clip in new[] { "hero-a-aim", "hero-a-cast", "hero-a-blow", "hero-a-throw" }) Assert.That(TicksOf(clip), Is.EqualTo(HeroSheetSpec.ActionTicks), clip);
            Assert.That(TicksOf("hero-a-moving-keys"), Is.EqualTo(HeroSheetSpec.KeyTicks));
            Assert.That(TicksOf("hero-a-idle"), Is.EqualTo(HeroSheetSpec.IdleTicks));
            Assert.That(TicksOf("hero-a-hit-r5"), Is.EqualTo(HeroSheetSpec.HitFrameTicks[0]));
            var hit = text.IndexOf("\"clip\": \"hero-a-hit-r5\"", StringComparison.Ordinal);
            var second = text.IndexOf("\"frameTicks60\": ", text.IndexOf("\"frameTicks60\": ", hit, StringComparison.Ordinal) + 1, StringComparison.Ordinal) + "\"frameTicks60\": ".Length;
            Assert.That(int.Parse(new string(text.Skip(second).TakeWhile(char.IsDigit).ToArray())), Is.EqualTo(HeroSheetSpec.HitFrameTicks[1]));
        }
    }
}
