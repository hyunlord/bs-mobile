using System;
using System.Linq;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        public const int WorldVisibleSide = 7; // Presentation window only; map dimensions come from the response.
        public const string WorldMapGlyphs = "가각간같거결경계고공과관군귀기남내는능니다답도동되드량러력령로료를리마막맵맹명몬못물미반발배버병보부북불비사산상새서선성세소속수숲스습시실아않았약없었에여연영예오와요용운웅원월위유으은을음응의이인입자장재적전절점정제종주준중지직진집착찰채청출치침태택터통투패편평표필하한할함합해했행호확환회•−";
        public static string WorldTerrainName(string terrain) => terrain == "plain" ? "평지" : terrain == "forest" ? "숲" : terrain == "mountain" ? "산" : terrain == "water" ? "물" : terrain == "bridge" ? "다리" : terrain;
        public static string WorldTargetName(WorldMapTarget target) => target.Kind == "city" ? "도시" : target.Kind == "resource" ? MaterialName(target.ContentId) + " 자원지" : target.Kind == "stronghold" ? "거점" : "몬스터";
        public static string WorldStatus(string status) => status == "connected" ? "서버 응답 확인" : status == "dispatched" ? "행군 발령 확정" : status == "recalled" ? "회군 요청 확정" : status == "returned" ? "귀환 확정" : status == "pending" ? "지도 불러오는 중"
            : status == "city-not-assigned" ? "도시가 아직 배정되지 않았습니다."
            : status == "invalid-chunk" ? "지도의 경계입니다. 마지막 확인 지도를 유지합니다."
            : status == "world-capacity-exhausted" ? "도시 배정 공간이 없습니다."
            : status == "not-connected" || status == "unauthorized" ? "계정 연결이 필요합니다."
            : "서버 응답을 확인하지 못했습니다. 다시 불러와 주세요.";
        static void WorldArt(Transform parent, string id, float x, float y, float side)
        {
            var rect = PortraitWidgets.Place("WorldArt." + id, parent, x, y, side, side);
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = EstateArt.Load().Get(id); image.preserveAspect = true; image.raycastTarget = false;
        }
        public static void WorldMap(UiShell ui, MetaState state, BarracksServerClient client, int viewX, int viewY, int selectedX, int selectedY,
            Action home, Action assign, Action<int, int> move, Action<int, int> select, Action connect, Action<PortraitMetaPage> navigate, WorldMarchActions marches = null)
        {
            var root = Page(ui, "WorldMap", "월드맵"); var width = Width(ui) - 36;
            var body = Body(ui, root, 100, 78); float y = 0;
            Caption(ui, body, WorldStatus(client.Status), 0, y, width, 48, 16); y += 56;
            if (!client.Connected)
            {
                ActionButton(ui, body, "World.Connect", "계정 연결", connect, 0, y, width, !client.Busy); y += 56;
            }
            else
            {
                ActionButton(ui, body, "World.Home", "내 도시 · 새로 고침", home, 0, y, width, !client.Busy); y += 56;
                if (client.WorldCityMissing)
                {
                    ActionButton(ui, body, "World.Assign", "도시 배정 요청", assign, 0, y, width, !client.Busy); y += 56;
                }
            }
            var chunk = client.WorldChunk;
            if (chunk != null)
            {
                var side = Math.Min(WorldVisibleSide, chunk.Size); var cell = width / WorldVisibleSide;
                Caption(ui, body, "위치 " + WorldMapResponse.Coordinate(viewX, viewY), 0, y, width, 28, 16); y += 32;
                for (int row = 0; row < side; row++) for (int column = 0; column < side; column++)
                {
                    int x = viewX + column, tileY = viewY + row;
                    var tile = chunk.Tile(x, tileY); if (tile == null) continue;
                    var target = chunk.Target(x, tileY); var tx = column * cell; var ty = y + row * cell;
                    WorldArt(body, "ground-grass-r4", tx, ty, cell);
                    var art = tile.Terrain == "forest" ? "tree-cluster-4" : tile.Terrain == "mountain" ? "rocks" : tile.Terrain == "water" ? "river-bend-patch" : tile.Terrain == "bridge" ? "bridge-ne-sw-base" : null;
                    if (art != null) WorldArt(body, art, tx, ty, cell);
                    if (target != null)
                    {
                        var icon = target.Kind == "city" ? "house-fence-kit" : target.Kind == "stronghold" ? "frontier-ruin" : target.Kind == "monster" ? "frontier-empty-armor" : target.ContentId == "meta:timber" ? "firewood" : target.ContentId == "meta:iron" ? "rocks" : "sacks";
                        WorldArt(body, icon, tx, ty, cell);
                        if (target.Kind == "resource" && client.WorldMarches?.Any(m => m.TargetId == target.Id && m.State == "gathering") == true) MarchArt(body,"march-gathering",tx+cell-24,ty,24,24);
                        if (target.Kind == "city" && target.Id == client.WorldCity?.Id) Caption(ui, body, "내", tx, ty + cell - 20, cell, 20, 14, true, TextAnchor.MiddleCenter);
                    }
                    Hit(body, "World.Tile." + x + "." + tileY, () => select(x, tileY), tx, ty, cell, cell, !client.Busy);
                    if (x == selectedX && tileY == selectedY) Caption(ui, body, "•", tx, ty, cell, 20, 18, false, TextAnchor.UpperRight);
                }
                y += side * cell + 8;
                var gap = 8f; var buttonWidth = (width - 3 * gap) / 4;
                ActionButton(ui, body, "World.West", "서", () => move(-1, 0), 0, y, buttonWidth, !client.Busy && viewX > 0);
                ActionButton(ui, body, "World.North", "북", () => move(0, -1), buttonWidth + gap, y, buttonWidth, !client.Busy && viewY > 0);
                ActionButton(ui, body, "World.South", "남", () => move(0, 1), 2 * (buttonWidth + gap), y, buttonWidth, !client.Busy);
                ActionButton(ui, body, "World.East", "동", () => move(1, 0), 3 * (buttonWidth + gap), y, buttonWidth, !client.Busy); y += 56;
                var selected = chunk.Tile(selectedX, selectedY);
                if (selected != null)
                {
                    Caption(ui, body, WorldMapResponse.Coordinate(selectedX, selectedY) + " · " + WorldTerrainName(selected.Terrain) + " · " + (selected.Passable ? "통과 가능" : "통과 불가"), 0, y, width, 48, 16); y += 52;
                    var target = chunk.Target(selectedX, selectedY);
                    if (target != null)
                    {
                        Caption(ui, body, WorldTargetName(target) + (target.Kind == "resource" ? " · 재고 확인 불가" : ""), 0, y, width, 48, 16); y += 52;
                        if (!string.IsNullOrEmpty(target.AllianceName)) { Caption(ui, body, "연맹 " + target.AllianceName, 0, y, width, 32, 16); y += 36; }
                    }
                }
            }
            if (marches != null && client.Connected) y = WorldMarchPanel(ui, body, client, chunk?.Target(selectedX,selectedY), marches, width, y);
            Caption(ui, body, "정찰·전투는 아직 연결되지 않았습니다.", 0, y, width, 48, 14); End(body, y + 48);
            Tabs(ui, root, state, navigate, PortraitMetaPage.WorldMap);
        }
    }
}
