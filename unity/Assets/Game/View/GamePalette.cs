using UnityEngine;
namespace Game.View
{
    public static class GamePalette
    {
        private static Color Hex(uint value, float alpha = 1) => new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f, alpha);
        public static readonly Color Ground = Hex(0xE7E5DF), Enemy = Hex(0x202322), Lord = Hex(0x376D8A), AllyFill = Hex(0xD8E0E0), Attack = Hex(0x8B959E, .30f), Growing = Hex(0xB8BDB1), Ready = Hex(0x748769), KillXp = Hex(0x5986AA), HarvestXp = Hex(0xAD873C), Panel = Hex(0xF7F6F2, .96f), Text = Hex(0x252A2D), MutedText = Hex(0x596269), Stroke = Hex(0x9FA8AA), Danger = Hex(0x8F3835), Button = Hex(0x33434B), ButtonText = Color.white;
        public const int TitleSize = 24, CardTitleSize = 18, BodySize = 16, SmallSize = 14, DebugSize = 12;
        public const float TouchSize = 48;
    }
}
