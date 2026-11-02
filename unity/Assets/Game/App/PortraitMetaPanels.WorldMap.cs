using System;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        public const int WorldVisibleSide = 7; // Presentation window only; map dimensions come from the response.
        public const string WorldMapGlyphs = "월드맵도시지도불러오기다배정서버연결확인내위치선택숲평산물리통과가능불새로고침자원재정보미제공채집행군전투아직연맹소속없음계응답실패보관표시북남동서";
        public static string WorldTerrainName(string terrain) => terrain == "plain" ? "평지" : terrain == "forest" ? "숲" : terrain == "mountain" ? "산" : terrain == "water" ? "물" : terrain == "bridge" ? "다리" : terrain;
        public static string WorldTargetName(WorldMapTarget target) => target.Kind == "city" ? "도시" : target.Kind == "resource" ? MaterialName(target.ContentId) + " 자원지" : target.Kind == "stronghold" ? "거점" : "몬스터";
        public static string WorldStatus(string status) => status == "connected" ? "서버 응답 확인" : status == "pending" ? "지도 불러오는 중"
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
            Action home, Action assign, Action<int, int> move, Action<int, int> select, Action connect, Action<PortraitMetaPage> navigate)
        {
            var root = Page(ui, "WorldMap", "월드맵"); var width = Width(ui) - 36;
            var body = Body(ui, root, 76, 78); float y = 0;
            Caption(ui, body, WorldStatus(client.Status), 0, y, width, 48, 16, true); y += 56;
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
                        var icon = target.Kind == "city" ? "house-fence-kit" : target.Kind == "stronghold" ? "frontier-ruin" : target.Kind == "monster" ? "frontier-empty-armor" : target.ContentId == "meta:wood" ? "firewood" : target.ContentId == "meta:iron" ? "rocks" : "sacks";
                        WorldArt(body, icon, tx, ty, cell);
                        Caption(ui, body, target.Kind == "city" && target.Id == client.WorldCity?.Id ? "내 도시" : WorldTargetName(target), tx, ty + cell - 18, cell, 18, 11, true, TextAnchor.MiddleCenter);
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
                    Caption(ui, body, WorldMapResponse.Coordinate(selectedX, selectedY) + " · " + WorldTerrainName(selected.Terrain) + " · " + (selected.Passable ? "통과 가능" : "통과 불가"), 0, y, width, 48, 16, true); y += 52;
                    var target = chunk.Target(selectedX, selectedY);
                    if (target != null)
                    {
                        Caption(ui, body, WorldTargetName(target) + (target.Kind == "resource" ? " · 재고 정보 미제공" : ""), 0, y, width, 48, 16, true); y += 52;
                        if (!string.IsNullOrEmpty(target.AllianceName)) { Caption(ui, body, "연맹 " + target.AllianceName, 0, y, width, 32, 16, true); y += 36; }
                    }
                }
            }
            Caption(ui, body, "채집·행군·전투는 아직 연결되지 않았습니다.", 0, y, width, 48, 14, true); End(body, y + 48);
            Tabs(ui, root, state, navigate, PortraitMetaPage.WorldMap);
        }
    }
}
