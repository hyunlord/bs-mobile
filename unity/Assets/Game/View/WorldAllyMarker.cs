using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        // Ally marker B+ (#352, user decision 2026-10-22): Astra's helmet band and blue plume over every militia and guard body that
        // plays the Soldier B sheet. The sheet itself is unchanged; tools/art-authoring/ally-marker packs one marker cell per walk
        // frame into the same crops-people atlas, so the marker shares the soldier's batch. The cell sits at
        // GameVisualTokens.AllyMarkerOffsetPixels from the soldier's foot pivot and takes the soldier's pose, scale and facing.
        public const string AllyMarkerRole = "person.ally-marker.move";
        ArtVisual allyMarker;
        bool allyMarkerResolved;
        // Comparison switch for tests and measurements; markers are on in every profile.
        public bool AllyMarkersEnabled { get; set; } = true;
        public int AllyMarkerDraws { get; private set; }
        public int AllyMarkerFrameMask { get; private set; }

        static bool IsAllyRole(string role) => role == "militia" || role == "guard";

        ArtVisual AllyMarker()
        {
            if (allyMarkerResolved) return allyMarker;
            allyMarkerResolved = true;
            try { allyMarker = art.ResolveRole(AllyMarkerRole); }
            catch (System.InvalidOperationException) { allyMarker = null; }
            return allyMarker;
        }

        // The walk frame a body shows: its own rectangle in the role's "move" sheet (standing roles reuse the sheet's frame 4).
        static int SheetFrame(ArtVisual walk, Rect shown)
        {
            for (var i = 0; i < walk.UvRects.Count; i++) if (walk.UvRects[i] == shown) return i;
            return -1;
        }

        // Where DrawActor puts a body (foot pivot at position + pose offset, size scaled by the pose, mirrored by facing, turned by
        // pose and lean), the marker cell goes at AllyMarkerOffsetPixels from that pivot with the same scale, mirror and turn.
        public static (Vector2 at, Vector2 size, float degrees) PlaceAllyMarker(ArtVisual body, ArtVisual marker, Vector2 position, Vector2 size, ActorPose pose,
            ActorFacingPose facing)
        {
            var scale = new Vector2(size.x / body.WorldSize.x * pose.Scale.x, size.y / body.WorldSize.y * pose.Scale.y);
            var unitsPerPixel = new Vector2(body.WorldSize.x / body.CellPixels.x, body.WorldSize.y / body.CellPixels.y);
            var local = Vector2.Scale(Vector2.Scale(GameVisualTokens.AllyMarkerOffsetPixels, unitsPerPixel), scale);
            local.x *= facing.SignedWidth;
            var degrees = pose.Degrees + facing.LeanDegrees;
            var dimensions = Vector2.Scale(marker.WorldSize, scale);
            dimensions.x *= facing.SignedWidth;
            return (position + pose.Offset + (Vector2)(Quaternion.Euler(0, 0, degrees) * local), dimensions, degrees);
        }

        // Draw right after the soldier body (and its outline) with the same arguments, so the marker lands on the body's helmet.
        void DrawAllyMarker(string role, ArtVisual body, Rect? uv, int layer, Vector2 position, float time, Vector2 size, bool walking, float attackProgress,
            ActorFacingPose facing, ActorPose? poseOverride)
        {
            if (!AllyMarkersEnabled || !IsAllyRole(role)) return;
            var marker = AllyMarker();
            if (marker == null) return;
            var walk = Resolve("person", role, "move");
            if (!PersonSheetWalk.Applies(walk) || walk.UvRects.Count != marker.UvRects.Count) return;
            var frame = SheetFrame(walk, uv ?? body.UvRects[0]);
            if (frame < 0) return;
            var pose = poseOverride ?? ActorMotion.Sample(time, walking, attackProgress, -1);
            var (at, dimensions, degrees) = PlaceAllyMarker(body, marker, position, size, pose, facing);
            Draw(marker, layer, at, time, dimensions, degrees, uv: marker.UvRects[frame], applyTween: false);
            AllyMarkerDraws++; AllyMarkerFrameMask |= 1 << frame;
        }
    }
}
