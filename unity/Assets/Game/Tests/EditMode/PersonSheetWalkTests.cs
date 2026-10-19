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
        static ArtCatalog People(string file)
        {
            var m = Manifest(file);
            m.roles = m.roles.Where(r => r.id.StartsWith("person.", StringComparison.Ordinal)).ToArray();
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
            foreach (var kind in Others) Assert.That(Role(kind, "move").frames.Length, Is.EqualTo(1), kind + " keeps its time-based single frame");
            Assert.That(GameVisualTokens.PersonWalkCyclePixels, Is.EqualTo(record.cyclePixels));
        }

        [Test] public void OneStrideOfTravelPlaysEachFrameOnceInOrder()
        {
            var move = People("wave1a-manifest.json").ResolveRole("person.militia.move");
            Assert.That(PersonSheetWalk.Applies(move), Is.True);
            var stride = PersonSheetWalk.Stride(move);
            Assert.That(stride, Is.EqualTo(Record().cyclePixels * Record().unitsPerPixel).Within(1e-4f));
            var walk = new PersonSheetWalk();
            var position = Vector2.zero;
            Assert.That(walk.Frame("group", 1, position, true, move, 1), Is.EqualTo(0));
            position.x += stride / 32;   // half a frame in, so every following step lands mid-frame
            var frames = new int[16];
            for (var i = 0; i < 16; i++) { frames[i] = walk.Frame("group", 1, position, true, move, 1); position.x -= stride / 16; }
            Assert.That(frames, Is.EqualTo(Enumerable.Range(0, 16).ToArray()), "direction does not matter; distance does");
            Assert.That(walk.Frame("group", 1, position, true, move, 1), Is.EqualTo(0), "one stride later the cycle repeats");
        }

        [Test] public void StationaryActorsStandAndKeepTheirPhase()
        {
            var move = People("wave1a-manifest.json").ResolveRole("person.guard.move");
            var walk = new PersonSheetWalk();
            var stride = PersonSheetWalk.Stride(move);
            walk.Frame("person", 7, Vector2.zero, true, move, 1);
            Assert.That(walk.Frame("person", 7, new Vector2(stride * 5.5f / 16, 0), true, move, 2), Is.EqualTo(5));
            Assert.That(walk.Frame("person", 7, new Vector2(stride * 5.5f / 16, 0), false, move, 3), Is.EqualTo(-1), "no Core movement: standing role");
            Assert.That(walk.Frame("person", 7, new Vector2(stride * 5.5f / 16, 0), true, move, 4), Is.EqualTo(5), "a pause holds the stride");
        }

        [Test] public void ActorsNotDrawnInAGenerationAreForgotten()
        {
            var move = People("first-playable-manifest.json").ResolveRole("person.militia.move");
            var walk = new PersonSheetWalk();
            walk.Frame("group", 1, Vector2.zero, true, move, 1);
            walk.Frame("group", 2, Vector2.zero, true, move, 1);
            walk.Frame("group", 2, Vector2.one, true, move, 2);
            walk.Retire(2);
            Assert.That(walk.Count, Is.EqualTo(1));
        }

        [Test] public void SingleFrameMoveRolesKeepTheTimeBasedPath()
        {
            var catalog = People("first-playable-manifest.json");
            foreach (var kind in Others) Assert.That(PersonSheetWalk.Applies(catalog.Resolve("person", kind, "move")), Is.False, kind);
            foreach (var kind in Soldiers) Assert.That(PersonSheetWalk.Applies(catalog.Resolve("person", kind, "move")), Is.True, kind);
        }
    }
}
