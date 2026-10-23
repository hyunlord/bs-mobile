using System;
using System.IO;
using System.Linq;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // Ally marker B+ (#352): one marker cell per Soldier B walk frame in crops-people, placed on the soldier's helmet.
    public sealed class AllyMarkerTests
    {
        [Serializable] sealed class PackedRect { public int index, x, y, width, height; }
        [Serializable] sealed class Residual { public int top, left, right; }
        [Serializable] sealed class Track { public int index, dx, dy; public Residual outlineResidualPx; }
        [Serializable] sealed class Window { public int left, top, width, height; }
        [Serializable] sealed class Offset { public float x, y; }
        [Serializable] sealed class PackedRecord { public float unitsPerPixel; public int standingFrame; public Track[] tracks; public Window window; public Offset offsetPixels; public PackedRect[] cells; }
        static PackedRecord Record() => JsonUtility.FromJson<PackedRecord>(File.ReadAllText(Path.Combine(Application.dataPath, "../../tools/art-authoring/ally-marker/packed-frames.json")));
        static ArtManifest Manifest(string file) => JsonUtility.FromJson<ArtManifest>(File.ReadAllText(Path.Combine(Application.dataPath, "Art", file)));

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
        public void MarkerRoleSharesTheSoldierAtlasAndFollowsThePackRecord(string file)
        {
            var record = Record();
            var m = Manifest(file);
            var marker = m.roles.Single(r => r.id == WorldRenderer.AllyMarkerRole);
            var walk = m.roles.Single(r => r.id == "person.militia.move");
            Assert.That(marker.atlas, Is.EqualTo(walk.atlas), "same texture as the soldier, so the marker joins the soldier's batch");
            Assert.That(marker.frames.Length, Is.EqualTo(walk.frames.Length));
            Assert.That(marker.frames.Select((f, i) => f.x == record.cells[i].x && f.y == record.cells[i].y && f.width == record.window.width && f.height == record.window.height).All(x => x), Is.True);
            Assert.That((marker.pivot.x, marker.pivot.y, marker.frameMs), Is.EqualTo((0f, 0f, walk.frameMs)));
            Assert.That(marker.worldSize.x, Is.EqualTo(record.window.width * record.unitsPerPixel).Within(1e-5f));
            Assert.That(marker.worldSize.y, Is.EqualTo(record.window.height * record.unitsPerPixel).Within(1e-5f));
            Assert.That(GameVisualTokens.AllyMarkerOffsetPixels, Is.EqualTo(new Vector2(record.offsetPixels.x, record.offsetPixels.y)));
        }

        // The pack shifts frame 0's marker by whole pixels; after the shift every frame's helmet outline is within 1 px of frame 0's.
        [Test]
        public void EveryWalkFrameKeepsTheHelmetWithinOnePixel()
        {
            var record = Record();
            Assert.That(record.tracks.Length, Is.EqualTo(16));
            foreach (var t in record.tracks)
                Assert.That(new[] { t.outlineResidualPx.top, t.outlineResidualPx.left, t.outlineResidualPx.right }.All(v => Math.Abs(v) <= 1), Is.True, "frame " + t.index);
        }

        // The marker's texel at the window corner lands on the same world point as the soldier's texel there, in any pose.
        [Test]
        public void PlacementPutsTheMarkerOnTheSoldiersTexelsInEveryPose()
        {
            var record = Record();
            var catalog = People("wave1a-manifest.json");
            var body = catalog.ResolveRole("person.militia.move");
            var marker = catalog.ResolveRole(WorldRenderer.AllyMarkerRole);
            var pixel = body.WorldSize.y / body.CellPixels.y;
            foreach (var facing in new[] { new ActorFacingPose(0, 0), new ActorFacingPose(1, 0), new ActorFacingPose(1, 4) })
            foreach (var pose in new[] { new ActorPose(Vector2.zero, Vector2.one, 0), new ActorPose(new Vector2(.03f, .02f), new Vector2(1.07f, .95f), -9) })
            foreach (var scale in new[] { 1f, 1.3f })
            {
                var position = new Vector2(2, 3); var size = body.WorldSize * scale;
                // Body quad as SpriteBatch.Add draws it: pivot at position + pose offset, size scaled and mirrored, turned.
                var turn = Quaternion.Euler(0, 0, pose.Degrees + facing.LeanDegrees);
                var bodySize = Vector2.Scale(size, pose.Scale); bodySize.x *= facing.SignedWidth;
                Vector2 BodyPoint(Vector2 cell) => position + pose.Offset + (Vector2)(turn * (Vector3)Vector2.Scale(new Vector2(cell.x / body.CellPixels.x, cell.y / body.CellPixels.y) - body.Pivot, bodySize));
                var placed = WorldRenderer.PlaceAllyMarker(body, marker, position, size, pose, facing);
                Vector2 MarkerPoint(Vector2 local) => placed.at + (Vector2)(Quaternion.Euler(0, 0, placed.degrees) * (Vector3)Vector2.Scale(local, placed.size));
                // Window corners in the soldier cell (x right, y up): bottom-left and top-right.
                var bottomLeft = new Vector2(record.window.left, body.CellPixels.y - record.window.top - record.window.height);
                var topRight = bottomLeft + new Vector2(record.window.width, record.window.height);
                var tolerance = pixel * .01f;
                Assert.That(Vector2.Distance(MarkerPoint(Vector2.zero), BodyPoint(bottomLeft)), Is.LessThan(tolerance), $"bottom-left {facing.SignedWidth} {pose.Degrees} {scale}");
                Assert.That(Vector2.Distance(MarkerPoint(Vector2.one), BodyPoint(topRight)), Is.LessThan(tolerance), $"top-right {facing.SignedWidth} {pose.Degrees} {scale}");
            }
        }
    }
}
