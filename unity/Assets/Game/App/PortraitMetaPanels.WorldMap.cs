using System;
using Game.View;
using SowSiege.Core;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        public static void WorldMap(UiShell ui, MetaState state, BarracksServerClient client, int viewX, int viewY, int selectedX, int selectedY,
            Action home, Action assign, Action<int, int> move, Action<int, int> select, Action connect, Action<PortraitMetaPage> navigate, WorldMarchActions marches = null)
        {
            var root = Page(ui, "WorldMap", "");
            WorldWalletHeader(ui, root, client);
            WorldMapViewport(ui, root, state, client, viewX, viewY, selectedX, selectedY,
                home, assign, move, select, connect, navigate, marches, WorldWalletBodyTop);
        }
    }
}
