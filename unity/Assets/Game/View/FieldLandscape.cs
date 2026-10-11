using UnityEngine;

namespace Game.View
{
    public readonly struct FieldProp
    {
        public readonly int Kind;
        public readonly Vector2 Position, Size;
        public readonly float Degrees;
        public FieldProp(int kind, Vector2 position, Vector2 size, float degrees)
        { Kind = kind; Position = position; Size = size; Degrees = degrees; }
    }

    public static class FieldLandscape
    {
        public const float SectorSize = 12;
        public const int PropsPerSector = 8;
        public static int Sector(float coordinate) => Mathf.FloorToInt(coordinate / SectorSize);
        public static uint Hash(int seed, int x, int y, int slot)
        {
            unchecked
            {
                var value = (uint)seed ^ (uint)x * 0x9e3779b9u ^ (uint)y * 0x85ebca6bu ^ (uint)slot * 0xc2b2ae35u;
                value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }
        public static FieldProp Prop(int seed, int x, int y, int slot)
        {
            var hash = Hash(seed, x, y, slot);
            var position = new Vector2((x + .12f + (hash & 1023) / 1023f * .76f) * SectorSize,
                (y + .12f + ((hash >> 10) & 1023) / 1023f * .76f) * SectorSize);
            var kind = slot % 3;
            var scale = .8f + ((hash >> 20) & 255) / 255f * .35f;
            return new FieldProp(kind, position, (kind == 0 ? new Vector2(1, 1.5f) : kind == 1 ? new Vector2(.65f, 1) : new Vector2(1.5f, 1)) * scale,
                kind == 2 ? (hash % 3 - 1f) * 12 : 0);
        }
        public static Vector2 CreekPoint(int seed, int segment, int row)
            => new Vector2(segment * 3f, row * 24f + 7 + (Hash(seed, segment, row, 100) & 1023) / 1023f * 1.2f);
        public static Rect MaskWindow(Vector2 center, float halfWidth, float halfHeight)
        {
            const float snap = 1;
            var width = Mathf.Max(28, Mathf.Ceil((halfWidth * 2 + 6) / snap) * snap);
            var height = Mathf.Max(18, Mathf.Ceil((halfHeight * 2 + 6) / snap) * snap);
            return new Rect(Mathf.Floor((center.x - width * .5f) / snap) * snap,
                Mathf.Floor((center.y - height * .5f) / snap) * snap, width, height);
        }
        public static Vector4 MaskTransform(Rect window)
            => new Vector4(1 / window.width, 1 / window.height, -window.xMin / window.width, -window.yMin / window.height);
    }
}
