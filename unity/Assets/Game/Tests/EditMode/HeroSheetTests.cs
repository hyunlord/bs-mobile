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
        static ArtManifest Manifest(string file) => JsonUtility.FromJson<ArtManifest>(File.ReadAllText(Path.Combine(Application.dataPath, "Art", file)));
        static HeroSheetTimings Timings(ArtManifest m) => new HeroSheetTimings(suffix =>
        {
            var role = m.roles.Single(r => r.id == HeroSheetSpec.RolePrefix + suffix);
            return (role.frames.Length, role.frameMs);
        });
        // The selector as the game builds it: timings from the registered knight roles.
        static HeroActionSelector Selector() => new HeroActionSelector(Timings(Manifest("wave1a-manifest.json")));

        [Test] public void MovingAttackShowsTheKeyPoseFor7TicksThenWalks()
        {
            var s = Selector();
            Assert.That(s.Offer(1, "core:iron_blade", HeroAction.Slash, false, 10, 1, true), Is.True);
            var key = s.Pose(1 + Ticks(3), true, false, false, 0, Stride);
            Assert.That((key.Clip, key.Action), Is.EqualTo((HeroClip.Key, HeroAction.Slash)));
            Assert.That(s.Pose(1 + Ticks(8), true, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Walk));
            Assert.That((s.KeysShown, s.FullShown), Is.EqualTo((1, 0)));
        }

        [Test] public void StandingAttackPlaysTheFullActionFrameByFrameThenIdles()
        {
            var s = Selector();
            s.Offer(1, "core:iron_blade", HeroAction.Slash, false, 10, 2, false);
            var slash = s.Timings.Action(HeroAction.Slash);
            for (var frame = 0; frame < slash.Frames; frame++)
            {
                var pose = s.Pose(2 + Ticks(frame * slash.Ticks + 1), false, false, false, 0, Stride);
                Assert.That((pose.Clip, pose.Frame), Is.EqualTo((HeroClip.Action, frame)));
            }
            Assert.That(s.Pose(2 + Ticks(slash.Frames * slash.Ticks + 1), false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Idle));
            var aim = Selector();
            aim.Offer(1, "core:canal_bow", HeroAction.Aim, false, 10, 0, false);
            var aimClip = aim.Timings.Action(HeroAction.Aim);
            Assert.That(aim.Pose(Ticks((aimClip.Frames - 1) * aimClip.Ticks + 1), false, false, false, 0, Stride).Frame, Is.EqualTo(aimClip.Frames - 1));
        }

        [Test] public void MovingDuringAFullActionCutsToWalkingAtOnce()
        {
            var s = Selector();
            s.Offer(1, "core:muster_horn", HeroAction.Blow, false, 10, 0, false);
            Assert.That(s.Pose(Ticks(15), false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Action));
            Assert.That(s.Pose(Ticks(16), true, false, false, .01f, Stride).Clip, Is.EqualTo(HeroClip.Walk));
            Assert.That(s.Pose(Ticks(17), false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Idle), "the cut action does not resume");
            Assert.That(s.CutByMovement, Is.EqualTo(1));
        }

        [Test] public void SameTickPicksEvolvedThenDamageThenLowerWeaponId()
        {
            var s = Selector();
            s.Offer(5, "core:iron_blade", HeroAction.Slash, false, 30, 1, false);
            s.Offer(5, "core:canal_bow", HeroAction.Aim, false, 20, 1, false);          // lower damage: dropped
            Assert.That(s.Pose(1.01f, false, false, false, 0, Stride).Action, Is.EqualTo(HeroAction.Slash));
            s.Offer(5, "core:storm_fork", HeroAction.Cast, true, 5, 1, false);          // evolved wins
            Assert.That(s.Pose(1.02f, false, false, false, 0, Stride).Action, Is.EqualTo(HeroAction.Cast));
            var ties = Selector();
            ties.Offer(7, "core:ditch_pike", HeroAction.Slash, false, 10, 1, false);
            ties.Offer(7, "core:canal_bow", HeroAction.Aim, false, 10, 1, false);       // same damage: lower id (canal < ditch) wins
            Assert.That(ties.Pose(1.01f, false, false, false, 0, Stride).Action, Is.EqualTo(HeroAction.Aim));
        }

        [Test] public void HigherPriorityReplacesFromTheStartAndTheSameActionWaitsPoint4Seconds()
        {
            var s = Selector();
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
            var s = Selector();
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

        [Test] public void HitAgeStartsWithTheHitPoseSoTheFlashLastsTwoFrames()
        {
            var s = Selector();
            Assert.That(s.HitAge(1), Is.EqualTo(-1));
            s.Pose(1, false, true, false, 0, Stride);
            Assert.That(s.HitAge(1 + Ticks(1)), Is.LessThan(HeroSheetSpec.HitFlashFrames / 60f), "flash on the first frames");
            Assert.That(s.HitAge(1 + Ticks(3)), Is.GreaterThan(HeroSheetSpec.HitFlashFrames / 60f), "then the r5 pose alone");
            s.Pose(1.5f, false, false, false, 0, Stride);
            Assert.That(s.HitAge(1.5f), Is.EqualTo(-1));
        }

        [Test] public void WalkCycleFollowsDistanceSoPlaybackMatchesSpeed()
        {
            var s = Selector();
            var walk = s.Timings.Walk; var idle = s.Timings.Idle;
            Assert.That(s.Pose(0, true, false, false, .5f * Stride, Stride).Frame, Is.EqualTo(walk.Frames / 2), "half a stride = half the walk cycle");
            Assert.That(s.Pose(0, true, false, false, .125f * Stride, Stride).Frame, Is.EqualTo(Mathf.FloorToInt(.625f * walk.Frames)));
            Assert.That(s.Pose(10, true, false, false, 0, Stride).Frame, Is.EqualTo(Mathf.FloorToInt(.625f * walk.Frames)), "standing still in time does not advance the walk");
            Assert.That(s.Pose(0, false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Idle));
            Assert.That(s.Pose(Ticks(idle.Ticks * (idle.Frames + 1) + 1), false, false, false, 0, Stride).Frame, Is.EqualTo(1), "idle loops its frames");
        }

        // #317: the walk cycle uses the manifest frameMs exactly; the other clips keep 60 Hz ticks.
        [Test] public void WalkStrideUsesTheExactFrameMsWithoutTickRounding()
        {
            var between = new HeroClipTiming(16, 38);
            Assert.That(between.Ticks, Is.EqualTo(2), "frame-timed clips still round to ticks");
            Assert.That(between.Seconds, Is.EqualTo(32 / 60f).Within(1e-6f));
            Assert.That(between.CycleSeconds, Is.EqualTo(.608f).Within(1e-6f));
            // 31.2 world units per tick (default speed x 1.3) at 30 Hz = 0.936 Unity units per second.
            Assert.That(HeroSheetSpec.WalkStride(31.2f, 30, 1000, between), Is.EqualTo(.936f * .608f).Within(1e-5f));
            Assert.That(HeroSheetSpec.WalkStride(0, 30, 1000, between), Is.EqualTo(0));
            var delivered = Manifest("wave1a-manifest.json").roles.Single(r => r.id == HeroSheetSpec.RolePrefix + "walk");
            var walk = new HeroClipTiming(delivered.frames.Length, delivered.frameMs);
            Assert.That(walk.CycleSeconds, Is.EqualTo(delivered.frames.Length * delivered.frameMs / 1000f).Within(1e-6f));
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
            Assert.That(HeroSheetSpec.ActionFor("core:canal_bow", false, "review-impact-tell"), Is.EqualTo(HeroAction.Aim), "review composition cast");
            Assert.That(HeroSheetSpec.ActionFor("core:storm_fork", false, "chain-link"), Is.EqualTo(HeroAction.Cast));
        }

        // #268: the hero must resolve only roles every profile registers, with the same frames and frameMs in each profile.
        [TestCase("first-playable-manifest.json")]
        [TestCase("wave1a-manifest.json")]
        public void EveryProfileRegistersTheSameKnightSheetRoles(string manifest)
        {
            var m = Manifest(manifest);
            var other = Manifest(manifest == "wave1a-manifest.json" ? "first-playable-manifest.json" : "wave1a-manifest.json");
            ArtRoleDefinition Role(ArtManifest source, string role) => source.roles.Single(r => r.id == HeroSheetSpec.RolePrefix + role);
            int Frames(string role) => Role(m, role).frames.Length;
            Assert.That(Timings(m), Is.Not.Null, "every clip has frames and a frameMs of at least one tick");
            Assert.That(Frames("death"), Is.EqualTo(1));
            foreach (var a in HeroSheetSpec.ActionNames.Skip(1)) Assert.That(Frames("key." + a), Is.EqualTo(1), a);
            foreach (var role in m.roles.Where(r => r.id.StartsWith(HeroSheetSpec.RolePrefix, StringComparison.Ordinal)))
            {
                var same = Role(other, role.id.Substring(HeroSheetSpec.RolePrefix.Length));
                Assert.That((same.frames.Length, same.frameMs), Is.EqualTo((role.frames.Length, role.frameMs)), role.id);
            }
            foreach (var state in new[] { "idle", "walk", "hit", "death" })
                Assert.That(m.bindings.Single(b => b.kind == "hero" && b.contentId == "core:frontier_knight" && b.state == state).roleId, Is.EqualTo(HeroSheetSpec.RolePrefix + state));
            Assert.That(m.roles.Where(r => r.id.StartsWith(HeroSheetSpec.RolePrefix, StringComparison.Ordinal)).All(r => r.atlas == "hero-knight"), Is.True);
        }

        // The runtime reads frames and frameMs from the manifest; they must be the delivered cells and frameTicks60 that the pack
        // tool recorded. Each clip has one frame time (only the hit's last frame differs: it holds while the hit lasts).
        [Serializable] sealed class PackedFrame { public int frameTicks60; }
        [Serializable] sealed class PackedClip { public string clip; public string role; public string[] keys; public PackedFrame[] frames; }
        [Serializable] sealed class PackedRecord { public PackedClip[] clips; }
        [Test] public void FrameCountsAndTimesMatchTheDeliveredSheets()
        {
            var record = JsonUtility.FromJson<PackedRecord>(File.ReadAllText(Path.Combine(Application.dataPath, "../../tools/art-authoring/knight-sheets/packed-frames.json")));
            var timings = Timings(Manifest("wave1a-manifest.json"));
            Assert.That(record.clips.Length, Is.GreaterThan(0));
            foreach (var clip in record.clips)
            {
                if (clip.keys != null && clip.keys.Length > 0)
                {
                    for (var i = 0; i < clip.keys.Length; i++)
                    {
                        var action = (HeroAction)Array.IndexOf(HeroSheetSpec.ActionNames, clip.keys[i]);
                        Assert.That(timings.Key(action).Ticks, Is.EqualTo(clip.frames[i].frameTicks60), clip.keys[i]);
                    }
                    continue;
                }
                var t = clip.role switch
                {
                    "idle" => timings.Idle, "walk" => timings.Walk, "hit" => timings.Hit,
                    _ => timings.Action((HeroAction)Array.IndexOf(HeroSheetSpec.ActionNames, clip.role.Substring("action.".Length)))
                };
                Assert.That(t.Frames, Is.EqualTo(clip.frames.Length), clip.clip);
                Assert.That(t.Ticks, Is.EqualTo(clip.frames[0].frameTicks60), clip.clip);
                var timed = clip.role == "hit" ? clip.frames.Length - 1 : clip.frames.Length;
                Assert.That(clip.frames.Take(timed).All(f => f.frameTicks60 == clip.frames[0].frameTicks60), Is.True, clip.clip + ": one frame time per clip");
            }
        }

        // A clip redrawn under the same role (Astra's new walk) plays its own frame count and frameMs without code changes.
        [Test] public void RedrawnClipsPlayTheirOwnFramesAndFrameMs()
        {
            var delivered = Manifest("wave1a-manifest.json");
            var timings = new HeroSheetTimings(suffix =>
            {
                if (suffix == "walk") return (12, 83);           // 12 frames, 5 ticks
                if (suffix == "action.slash") return (4, 100);   // 4 frames, 6 ticks
                if (suffix == "hit") return (3, 67);             // 3 frames, 4 ticks
                var role = delivered.roles.Single(r => r.id == HeroSheetSpec.RolePrefix + suffix);
                return (role.frames.Length, role.frameMs);
            });
            Assert.That((timings.Walk.Frames, timings.Walk.Ticks), Is.EqualTo((12, 5)));
            var s = new HeroActionSelector(timings);
            Assert.That(s.Pose(0, true, false, false, .5f * Stride, Stride).Frame, Is.EqualTo(6), "half a stride = frame 6 of 12");
            Assert.That(s.Pose(0, true, false, false, .4f * Stride, Stride).Frame, Is.EqualTo(10));
            var slash = new HeroActionSelector(timings);
            slash.Offer(1, "core:iron_blade", HeroAction.Slash, false, 10, 0, false);
            Assert.That(slash.Pose(Ticks(3 * 6 + 1), false, false, false, 0, Stride).Frame, Is.EqualTo(3));
            Assert.That(slash.Pose(Ticks(4 * 6 + 1), false, false, false, 0, Stride).Clip, Is.EqualTo(HeroClip.Idle), "4 frames x 6 ticks, then idle");
            var hit = new HeroActionSelector(timings);
            Assert.That(hit.Pose(1, false, true, false, 0, Stride).Frame, Is.EqualTo(0));
            Assert.That(hit.Pose(1 + Ticks(9), false, true, false, 0, Stride).Frame, Is.EqualTo(2));
            Assert.That(hit.Pose(1 + Ticks(40), false, true, false, 0, Stride).Frame, Is.EqualTo(2), "the last frame holds");
            Assert.That(HeroSheetSpec.TicksOf(167), Is.EqualTo(10));
            Assert.That(HeroSheetSpec.TicksOf(117), Is.EqualTo(7));
            Assert.That(() => new HeroClipTiming(0, 167), Throws.ArgumentException);
        }

        // The previous knight's frame the sheets were fitted to: 0.6 world units tall, 188x242 px.
        static readonly Vector2 PreviousFrameWorld = new Vector2(.6f * 188 / 242, .6f);
        static Vector2 Footprint(Vector2 worldSize, Vector2 cellPx, float footPivotY) => Vector2.Scale(worldSize, HeroSheetSpec.FigureShareOfCell(cellPx, footPivotY));

        [Test] public void FootprintComesFromEachRolesOwnCellAndFootBaseline([Values("wave1a-manifest.json", "first-playable-manifest.json")] string file)
        {
            var roles = Manifest(file).roles.Where(r => r.id.StartsWith(HeroSheetSpec.RolePrefix, StringComparison.Ordinal)).ToArray();
            Assert.That(roles, Is.Not.Empty);
            foreach (var role in roles)
            {
                var footprint = Footprint(role.worldSize, new Vector2(role.frames[0].width, role.frames[0].height), role.pivot.y);
                Assert.That(footprint.x, Is.EqualTo(PreviousFrameWorld.x).Within(1e-4f), role.id);
                Assert.That(footprint.y, Is.EqualTo(PreviousFrameWorld.y).Within(1e-4f), role.id);
            }
        }

        [Test] public void AClipInADifferentCellKeepsTheSameWorldFootprint()
        {
            // pack.mjs sizes a role so its 115 px body reference is the previous figure height: worldSize = figure * cell / 115.
            const float figure = .6f * 209 / 242;
            foreach (var (cell, foot) in new[] { (new Vector2(320, 240), 55f / 240), (new Vector2(384, 288), .25f), (new Vector2(256, 320), .1f) })
            {
                var worldSize = cell * (figure / HeroSheetSpec.BodyReferencePx);
                var footprint = Footprint(worldSize, cell, foot);
                Assert.That(footprint.x, Is.EqualTo(PreviousFrameWorld.x).Within(1e-4f), $"{cell}");
                Assert.That(footprint.y, Is.EqualTo(PreviousFrameWorld.y).Within(1e-4f), $"{cell}");
            }
        }

        [Test] public void ACellTheFigureDoesNotFitAboveTheFootBaselineIsRejected()
        {
            Assert.Throws<ArgumentException>(() => HeroSheetSpec.FigureShareOfCell(new Vector2(320, 150), 55f / 240));
            Assert.Throws<ArgumentException>(() => HeroSheetSpec.FigureShareOfCell(new Vector2(90, 240), .2f));
            Assert.Throws<ArgumentException>(() => HeroSheetSpec.FigureShareOfCell(new Vector2(320, 240), 1));
        }
    }
}
