using System.Linq;
using Game.View;
using UnityEngine;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        const float WorldWalletBodyTop = 100;
        static void WorldWalletHeader(UiShell ui, RectTransform root, BarracksServerClient client)
        {
            Caption(ui, root, "월드맵", 16, 50, Width(ui) - 32, 34, 24);
        }
        static float WorldWalletSummary(UiShell ui, RectTransform body, BarracksServerClient client, float width, float y)
        {
            if(client.Latest?.resources!=null)
            {
                Caption(ui,body,string.Join(" · ",client.Latest.resources.Select(r=>MaterialName(r.id)+" "+r.amount)),0,y,width,72,15);y+=80;
            }
            if(client.Latest?.held!=null&&client.Latest.held.Length>0)
            {Caption(ui,body,"보관함 · "+string.Join(" · ",client.Latest.held.Select(r=>MaterialName(r.id)+" "+r.amount)),0,y,width,64,15);y+=72;}
            return y;
        }
    }
}
