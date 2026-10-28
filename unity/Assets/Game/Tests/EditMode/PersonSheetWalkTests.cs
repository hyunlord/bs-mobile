using System;
using System.IO;
using System.Linq;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // Soldier B walk on person.militia and person.guard (#331): manifests match the pack record, and the walk plays by distance.
    public sealed class PersonSheetWalkTests
    {
        static readonly string[] Soldiers = { "militia", "guard" }, Others = { "peasant", "returning", "vassal" };
        static readonly string[] Standing = { "idle", "work", "muster", "return" };
        [Serializable] sealed class PackedRect { public int index, x, y, width, height; }
        [Serializable] sealed class PackedRecord { public float cyclePixels, unitsPerPixel; public int frameMs, standingFrame; public PackedRect[] frames; }
        static PackedRecord Record() => JsonUtility.FromJson<PackedRecord>(File.ReadAllText(Path.Combine(Application.dataPath, "../../tools/art-authoring/soldier-b/packed-frames.json")));
        static ArtManifest Manifest(string file) => JsonUtility.FromJson<ArtManifest>(File.ReadAllText(Path.Combine(Application.dataPath, "Art", file)));

        // The real person roles and their atlas only, over a blank texture of the registered size.
        static ArtCatalog People(string file, bool singleFrameFixture = false)
        {
            var m = Manifest(file);
            m.roles = m.roles.Where(r => r.id.StartsWith("person.", StringComparison.Ordinal)).ToArray();
            if (singleFrameFixture)
                foreach (var kind in Others)
                {
                    var move = m.roles.Single(r => r.id == $"person.{kind}.move");
                    move.frames = new[] { move.frames[0] };
                    move.cyclePixels = 0;
                }
            m.bindings = m.bindings.Where(b => b.kind == "person").ToArray();
            var atlases = m.roles.Select(r => r.atlas).Distinct().ToArray();
            m.atlases = m.atlases.Where(a => atlases.Contains(a.id)).ToArray();
            var textures = m.atlases.Select(a => new Texture2D(a.width, a.height, TextureFormat.Alpha8, false)).ToArray();
            return ArtCatalog.FromJson(JsonUtility.ToJson(m), textures);
        }

        [TestCase("first-playable-manifest.json")]
        [TestCase("wave1a-manifest.json")]
        public void SoldiersWalkSixteenFramesAndStandOnTheNarrowestStep(string file)
        {
            var record = Record();
            var m = Manifest(file);
            ArtRoleDefinition Role(string kind, string state) => m.roles.Single(r => r.id == $"person.{kind}.{state}");
            bool Same(ArtPixelRect a, PackedRect b) => a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
            foreach (var kind in Soldiers)
            {
                var move = Role(kind, "move");
                Assert.That(move.frames.Length, Is.EqualTo(16), kind);
                Assert.That(move.frameMs, Is.EqualTo(record.frameMs), kind);
                Assert.That(move.frames.Select((f, i) => Same(f, record.frames[i])).All(x => x), Is.True, kind + " move frames follow the pack record");
                foreach (var state in Standing)
                {
                    var role = Role(kind, state);
                    Assert.That(role.frames.Length, Is.EqualTo(1), kind + "." + state);
                    Assert.That(Same(role.frames[0], record.frames[record.standingFrame]), Is.True, kind + "." + state);
                    Assert.That((role.pivot, role.worldSize, role.tween), Is.EqualTo((move.pivot, move.worldSize, move.tween)), kind + "." + state);
                }
            }
            foreach (var kind in Others) Assert.That(Role(kind, "move").frames.Length, Is.EqualTo(16), kind + " approved people walk");
            Assert.That(GameVisualTokens.PersonWalkCyclePixels, Is.EqualTo(record.cyclePixels));
        }

        // Steps one second apart never reach the rate cap, so the walk follows distance alone.
        [Test] public void OneStrideOfTravelPlaysEachFrameOnceInOrder()
        {
            var move = People("wave1a-manifest.json").ResolveRole("person.militia.move");
            Assert.That(PersonSheetWalk.Applies(move), Is.True);
            var stride = PersonSheetWalk.Stride(move);
            Assert.That(stride, Is.EqualTo(Record().cyclePixels * Record().unitsPerPixel).Within(1e-4f));
            var walk = new PersonSheetWalk();
            var position = Vector2.zero;
            var time = 0f;
            Assert.That(walk.Frame("group", 1, position, time, true, move, 1), Is.EqualTo(0));
            position.x += stride / 32;   // half a frame in, so every following step lands mid-frame
            var frames = new int[16];
            for (var i = 0; i < 16; i++) { time += 1; frames[i] = walk.Frame("group", 1, position, time, true, move, 1); position.x -= stride / 16; }
            Assert.That(frames, Is.EqualTo(Enumerable.Range(0, 16).ToArray()), "direction does not matter; distance does");
            Assert.That(walk.Frame("group", 1, position, time + 1, true, move, 1), Is.EqualTo(0), "one stride later the cycle repeats");
        }

        [Test] public void StationaryActorsStandAndKeepTheirPhase()
        {
            var move = People("wave1a-manifest.json").ResolveRole("person.guard.move");
            var walk = new PersonSheetWalk();
            var stride = PersonSheetWalk.Stride(move);
            var at = new Vector2(stride * 5.5f / 16, 0);
            walk.Frame("person", 7, Vector2.zero, 0, true, move, 1);
            Assert.That(walk.Frame("person", 7, at, 1, true, move, 2), Is.EqualTo(5));
            Assert.That(walk.Frame("person", 7, at, 2, false, move, 3), Is.EqualTo(-1), "no Core movement: standing role");
            Assert.That(walk.Frame("person", 7, at, 3, true, move, 4), Is.EqualTo(5), "a pause holds the stride");
        }

        // Cycles per second over 10 s of 60 Hz rendering at a steady speed, counted from the frames actually returned.
        static float CyclesPerSecond(ArtVisual move, float unitsPerSecond)
        {
            var walk = new PersonSheetWalk();
            int previous = 0, wraps = 0;
            for (var i = 0; i <= 600; i++)
            {
                var frame = walk.Frame("group", 1, new Vector2(unitsPerSecond * i / 60f, 0), i / 60f, true, move, 1);
                if (frame < previous) wraps++;
                previous = frame;
            }
            return wraps / 10f;
        }

        [Test] public void FastGroupsWalkAtTheRateCapAndSlowSoldiersByDistance()
        {
            var move = People("wave1a-manifest.json").ResolveRole("person.militia.move");
            var stride = PersonSheetWalk.Stride(move);
            Assert.That(GameVisualTokens.PersonWalkMaxCyclesPerSecond, Is.EqualTo(2.5f));
            // wave-1a group: muster horn speed 90 per tick at 30 Hz = 2.7 units/s; by distance it would be 6.4 cycles/s.
            Assert.That(2.7f / stride, Is.GreaterThan(6f));
            Assert.That(CyclesPerSecond(move, 2.7f), Is.EqualTo(2.5f).Within(0.1f), "capped: one cycle per 0.4 s, the feet slide");
            // first-playable militia/guard: returnSpeed 18 per tick at 30 Hz = 0.54 units/s, under the cap, so distance rules.
            Assert.That(CyclesPerSecond(move, 0.54f), Is.EqualTo(0.54f / stride).Within(0.1f), "by distance: one cycle per 0.786 s");
        }

        [Test] public void ActorsNotDrawnInAGenerationAreForgotten()
        {
            var move = People("first-playable-manifest.json").ResolveRole("person.militia.move");
            var walk = new PersonSheetWalk();
            walk.Frame("group", 1, Vector2.zero, 0, true, move, 1);
            walk.Frame("group", 2, Vector2.zero, 0, true, move, 1);
            walk.Frame("group", 2, Vector2.one, 1, true, move, 2);
            walk.Retire(2);
            Assert.That(walk.Count, Is.EqualTo(1));
        }

        [Test] public void SingleFrameMoveRolesKeepTheTimeBasedPath()
        {
            var catalog = People("first-playable-manifest.json", singleFrameFixture: true);
            foreach (var kind in Others) Assert.That(PersonSheetWalk.Applies(catalog.Resolve("person", kind, "move")), Is.False, kind + " synthetic single-frame fallback remains supported");
            foreach (var kind in Soldiers) Assert.That(PersonSheetWalk.Applies(catalog.Resolve("person", kind, "move")), Is.True, kind);
        }

        [TestCase("first-playable-manifest.json", "vassal", 59.985697116f, 0.50310559f)]
        [TestCase("first-playable-manifest.json", "peasant", 61.060715818f, 0.51708075f)]
        [TestCase("first-playable-manifest.json", "returning", 61.060715818f, 0.51708075f)]
        [TestCase("wave1a-manifest.json", "vassal", 59.985697116f, 0.50310559f)]
        [TestCase("wave1a-manifest.json", "peasant", 61.060715818f, 0.51708075f)]
        [TestCase("wave1a-manifest.json", "returning", 61.060715818f, 0.51708075f)]
        public void ApprovedPeopleUseTheirOwnSourceStrideInBothProfiles(string file, string kind, float sourceStride, float sourceCellWorldSize)
        {
            var catalog = People(file);
            var move = catalog.Resolve("person", kind, "move");
            Assert.That(PersonSheetWalk.Applies(move), Is.True);
            Assert.That(move.UvRects.Count, Is.EqualTo(16));
            Assert.That(move.UvRects.Distinct().Count(), Is.EqualTo(16));
            Assert.That(move.FrameMs, Is.EqualTo(40));
            Assert.That(move.Tween, Is.EqualTo("none"));
            Assert.That(move.CyclePixels, Is.EqualTo(sourceStride).Within(0.00002f));
            var stride = PersonSheetWalk.Stride(move);
            Assert.That(stride, Is.EqualTo(sourceStride * sourceCellWorldSize / 256).Within(0.000001f), "union crop must not change source scale");
            Assert.That(move.Pivot.y * move.CellPixels.y, Is.EqualTo(2).Within(0.00001f), "source baseline225 in union bottom227");
            var walk = new PersonSheetWalk();
            walk.Frame("person", 12, Vector2.zero, 0, true, move, 1);
            for (var i = 1; i <= 16; i++)
            {
                var at = Vector2.left * (stride * (i - .5f) / 16);
                Assert.That(walk.Frame("person", 12, at, i, true, move, i), Is.EqualTo(i - 1), "reverse travel preserves ordered distance phases");
                Assert.That(walk.Frame("person", 12, at, i + .1f, false, move, i), Is.EqualTo(-1), "stationary uses original activity art");
                Assert.That(walk.Frame("person", 12, at, i + .2f, true, move, i), Is.EqualTo(i - 1), "stationary keeps phase");
            }
        }

        [TestCase("first-playable-manifest.json")]
        [TestCase("wave1a-manifest.json")]
        public void ChangingToReturningKeepsThePeasantLegScaleAndPhase(string file)
        {
            var catalog = People(file);
            var peasant = catalog.Resolve("person", "peasant", "move");
            var returning = catalog.Resolve("person", "returning", "move");
            Assert.That(PersonSheetWalk.Stride(returning), Is.EqualTo(PersonSheetWalk.Stride(peasant)).Within(0.000001f));
            Assert.That(returning.WorldSize.y / returning.CellPixels.y, Is.EqualTo(peasant.WorldSize.y / peasant.CellPixels.y).Within(0.000001f));
            var walk = new PersonSheetWalk();
            walk.Frame("person", 42, Vector2.zero, 0, true, peasant, 1);
            var at = Vector2.right * (PersonSheetWalk.Stride(peasant) * 4.5f / 16);
            Assert.That(walk.Frame("person", 42, at, 1, true, peasant, 2), Is.EqualTo(4));
            Assert.That(walk.Frame("person", 42, at, 2, true, returning, 3), Is.EqualTo(4));
        }
    }
}
