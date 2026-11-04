using System;
using System.Linq;
using Game.View;
using UnityEngine;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        public const string WorldWalletGlyphs = "월드맵 곡물 목재 철재 보석 인장 보관함 미확인 자원 — · 0123456789";
        const float WorldWalletBodyTop = 132;

        static void WorldWalletHeader(UiShell ui, RectTransform root, BarracksServerClient client) =>
            WorldWalletHeader(ui, root, client?.Latest?.resources);

        public static void WorldWalletHeader(UiShell ui, RectTransform root, BarracksResource[] resources)
        {
            var title = Caption(ui, root, "월드맵", 16, 8, Width(ui) - 32, 34, 24);
            title.font.RequestCharactersInTexture(WorldWalletGlyphs, title.fontSize, title.fontStyle);
            BarracksWallet(ui, root, resources, Width(ui));
            Art(ui, root, "icon.core.muster_roll.default", 16, 98, 22, 22);
            var charter = Caption(ui, root, "인장 " + BarracksWalletAmount(resources, "meta:charter"), 44, 94, Width(ui) - 60, 30, 13);
            charter.name = "World.Wallet:meta:charter";
        }

        public static string WorldHeldSummary(BarracksResource[] held) =>
            string.Join(" · ", (held ?? Array.Empty<BarracksResource>()).Select(resource =>
                WorldWalletResourceName(resource.id) + " " + resource.amount.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        static string WorldWalletResourceName(string id)
        {
            switch (id)
            {
                case "meta:grain": return "곡물";
                case "meta:timber": return "목재";
                case "meta:iron": return "철재";
                case "meta:charter": return "인장";
                case "slg:gem": return "보석";
                default: return "미확인 자원";
            }
        }

        static float WorldWalletSummary(UiShell ui, RectTransform body, BarracksServerClient client, float width, float y)
        {
            var held = WorldHeldSummary(client.Latest?.held);
            if (held.Length == 0) return y;
            var label = Label(ui, body, "보관함 · " + held, 0, y, width);
            return y + label + 8;
        }
    }
}
