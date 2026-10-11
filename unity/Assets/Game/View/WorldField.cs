using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        bool FieldActive => acceptedFrame?.Field != null;
        public int CameraMapClampCount => followCamera?.MapClampCount ?? 0;
        public bool CameraMapClampedThisFrame => followCamera != null && followCamera.MapClampedThisFrame;
        public int VisibleEnemyCount
        {
            get
            {
                if (acceptedFrame == null || renderCamera == null) return 0;
                var count = 0;
                var center = renderCamera.transform.position;
                var halfHeight = renderCamera.orthographicSize;
                var halfWidth = halfHeight * renderCamera.aspect;
                for (var i = 0; i < acceptedFrame.Enemies.Count; i++)
                {
                    var point = Point(acceptedFrame.Enemies[i].Position);
                    if (point.x >= center.x - halfWidth && point.x <= center.x + halfWidth
                        && point.y >= center.y - halfHeight && point.y <= center.y + halfHeight) count++;
                }
                return count;
            }
        }
        readonly FieldProp[] fieldProps = new FieldProp[512];
        int fieldPropCount, fieldSeed, fieldLeft = int.MinValue, fieldRight, fieldBottom, fieldTop;
        static readonly string[] FieldRoles = { "fallow.prop.dead-tree", "fallow.prop.stone", "fallow.prop.furrow" };

        bool FieldVisible(Vector2 point, Vector2 size)
        {
            var center = renderCamera.transform.position;
            var halfHeight = renderCamera.orthographicSize;
            var halfWidth = halfHeight * renderCamera.aspect;
            var radius = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 1.5f;
            return point.x + radius >= center.x - halfWidth && point.x - radius <= center.x + halfWidth
                && point.y + radius >= center.y - halfHeight && point.y - radius <= center.y + halfHeight;
        }

        void DrawFieldLandmarks()
        {
            var center = (Vector2)renderCamera.transform.position;
            var halfHeight = renderCamera.orthographicSize;
            var halfWidth = halfHeight * renderCamera.aspect;
            var left = FieldLandscape.Sector(center.x - halfWidth - 3);
            var right = FieldLandscape.Sector(center.x + halfWidth + 3);
            var bottom = FieldLandscape.Sector(center.y - halfHeight - 3);
            var top = FieldLandscape.Sector(center.y + halfHeight + 3);
            var seed = acceptedFrame.Field.Seed;
            if (left != fieldLeft || right != fieldRight || bottom != fieldBottom || top != fieldTop || seed != fieldSeed)
            {
                fieldPropCount = 0; fieldLeft = left; fieldRight = right; fieldBottom = bottom; fieldTop = top; fieldSeed = seed;
                for (var y = bottom; y <= top; y++) for (var x = left; x <= right; x++)
                    for (var slot = 0; slot < FieldLandscape.PropsPerSector; slot++)
                    {
                        if (fieldPropCount == fieldProps.Length) throw new System.InvalidOperationException("Field viewport exceeds its bounded prop cache.");
                        fieldProps[fieldPropCount++] = FieldLandscape.Prop(seed, x, y, slot);
                    }
            }
            for (var i = 0; i < fieldPropCount; i++)
            {
                var prop = fieldProps[i];
                FieldPropAt(FieldRoles[prop.Kind], prop.Position, prop.Size, prop.Kind == 2 ? 3 : 6, prop.Degrees, .8f);
            }
            var firstSegment = Mathf.FloorToInt((center.x - halfWidth - 3) / 3);
            var lastSegment = Mathf.CeilToInt((center.x + halfWidth + 3) / 3);
            var firstRow = Mathf.FloorToInt((center.y - halfHeight - 10) / 24);
            var lastRow = Mathf.CeilToInt((center.y + halfHeight) / 24);
            for (var row = firstRow; row <= lastRow; row++) for (var segment = firstSegment; segment <= lastSegment; segment++)
            {
                var a = FieldLandscape.CreekPoint(seed, segment, row);
                var b = FieldLandscape.CreekPoint(seed, segment + 1, row);
                var delta = b - a;
                FieldPropAt("fallow.prop.stream", (a + b) * .5f, new Vector2(delta.magnitude + .45f, .85f), 2, Angle(delta));
                if ((FieldLandscape.Hash(seed, segment, row, 101) & 15) == 0)
                {
                    FieldPropAt("fallow.prop.bridge", a, new Vector2(1.85f, 1.35f), 4);
                    FieldPropAt("fallow.prop.watermill", a + new Vector2(-.8f, .65f), new Vector2(2.4f, 2.1f), 5);
                }
            }
            var home = Point(acceptedFrame.Field.Home);
            var cottage = Resolve("wave", "workshop-complete", "default");
            var cottageSize = cottage.WorldSize * (1.9f / cottage.WorldSize.x);
            Draw(cottage, 5, home + new Vector2(-1, .65f), 0, cottageSize);
            FieldPropAt("fallow.prop.watermill", home + new Vector2(-1.8f, 2.76f), new Vector2(2.4f, 2.1f), 5);
            FieldPropAt("fallow.prop.stone", home + new Vector2(-2.4f, 1.08f), new Vector2(.8f, 1.3f), 6);
            FieldPropAt("fallow.prop.path", home, new Vector2(1.5f, 4), 3, -12, .6f);
        }

        void FieldPropAt(string role, Vector2 point, Vector2 size, int layer, float degrees = 0, float opacity = 1)
        {
            if (!FieldVisible(point, size) || !TryFallowArt(role, out var visual)) return;
            var uv = visual.UvRects[0];
            var pixels = new Vector2(Mathf.Abs(uv.width) * visual.Texture.width, Mathf.Abs(uv.height) * visual.Texture.height);
            var fit = Mathf.Min(size.x / pixels.x, size.y / pixels.y);
            Draw(visual, layer, point, 0, role == "fallow.prop.stream" ? size : pixels * fit, degrees, opacity: opacity);
        }

        void DrawFieldArena()
        {
            var arena = acceptedFrame.Field.Arena;
            if (arena == null) return;
            var edge = Resolve("boundary", "edge", "default");
            var radius = (float)arena.Radius / settings.WorldUnitsPerUnityUnit;
            var center = Point(arena.Center);
            var count = Mathf.CeilToInt(2 * Mathf.PI * radius / edge.WorldSize.x);
            for (var i = 0; i < count; i++)
            {
                var angle = (i + .5f) * 2 * Mathf.PI / count;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Draw(edge, GameVisualTokens.GrowthLayer - 1, point, 0,
                    new Vector2(2 * Mathf.PI * radius / count * 1.03f, edge.WorldSize.y), angle * Mathf.Rad2Deg + 90,
                    opacity: GameVisualTokens.WaveFenceOpacity);
            }
        }
    }
}
