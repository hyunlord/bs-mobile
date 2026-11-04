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
        static void WorldMapViewport(UiShell ui, RectTransform root, MetaState state, BarracksServerClient client, int viewX, int viewY, int selectedX, int selectedY,
            Action home, Action assign, Action<int, int> move, Action<int, int> select, Action connect, Action<PortraitMetaPage> navigate, WorldMarchActions marches, float bodyTop, WorldMapCameraState camera)
        {
            var width = Width(ui) - 36;
            var body = Body(ui, root, bodyTop, 78); float y = 0;
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
            if (chunk == null) camera?.Clear();
            if (chunk != null)
            {
                camera = camera ?? new WorldMapCameraState();
                Caption(ui, body, "위치 " + WorldMapResponse.Coordinate(viewX, viewY), 0, y, width, 28, 16); y += 32;
                var mapHeight = Mathf.Min(390, width * 1.05f);
                DrawObservedWorld(ui, body, client, camera, viewX, viewY, selectedX, selectedY, move, select, width, mapHeight, y);
                y += mapHeight + 8;
                var gap = 8f; var buttonWidth = (width - 3 * gap) / 4;
                ActionButton(ui, body, "World.West", "서", () => move(-1, 0), 0, y, buttonWidth, !client.Busy && viewX > 0);
                ActionButton(ui, body, "World.North", "북", () => move(0, -1), buttonWidth + gap, y, buttonWidth, !client.Busy && viewY > 0);
                ActionButton(ui, body, "World.South", "남", () => move(0, 1), 2 * (buttonWidth + gap), y, buttonWidth, !client.Busy);
                ActionButton(ui, body, "World.East", "동", () => move(1, 0), 3 * (buttonWidth + gap), y, buttonWidth, !client.Busy); y += 56;
                y = WorldTargetPanel(ui, body, chunk, selectedX, selectedY, width, y);
            }
            if (marches != null && client.Connected) y = WorldMarchPanel(ui, body, client, chunk?.Target(selectedX,selectedY), marches, width, y);
            Caption(ui, body, "정찰·전투는 아직 연결되지 않았습니다.", 0, y, width, 48, 14); End(body, y + 48);
            Tabs(ui, root, state, navigate, PortraitMetaPage.WorldMap);
        }
        static void DrawObservedWorld(UiShell ui, RectTransform body, BarracksServerClient client, WorldMapCameraState camera,
            int viewX, int viewY, int selectedX, int selectedY, Action<int, int> move, Action<int, int> select,
            float width, float height, float top)
        {
            var chunk = client.WorldChunk;
            var viewport = PortraitWidgets.Place("World.Viewport", body, 0, top, width, height);
            viewport.gameObject.AddComponent<Image>().color = new Color32(42, 52, 40, 255);
            viewport.gameObject.AddComponent<RectMask2D>();
            var model = camera.Observe(chunk, viewX, viewY, new Vector2(width, height));
            var view = viewport.gameObject.AddComponent<EstateMapView>(); view.Model = model;
            var drawing = viewport.gameObject.AddComponent<WorldMapDrawing>(); drawing.Model = model; drawing.Chunk = chunk;
            drawing.Move = (dx, dy) => { camera.RequestedChunkMove = new Vector2Int(dx, dy); move(dx, dy); }; drawing.CanMove = () => !client.Busy;
            var art = WorldMapArt.Load();
            Terrain(null, "world-terrain-plain");
            Terrain("forest", "world-terrain-forest");
            Terrain("mountain", "world-terrain-hill");
            Terrain("water", "world-terrain-ocean");
            foreach (var tile in chunk.Tiles)
            {
                var local = new Vector2(tile.X - chunk.X * chunk.Size + .5f, tile.Y - chunk.Y * chunk.Size + .5f);
                if (tile.Terrain == "forest") Piece("World.Tree." + tile.X + "." + tile.Y, "world-forest-broadleaf", local, new Vector2(72, 72), new Vector2(.5f, .15f));
                else if (tile.Terrain == "mountain") Piece("World.Peak." + tile.X + "." + tile.Y, "world-mountain-peaks", local, new Vector2(72, 64), new Vector2(.5f, .2f));
                else if (tile.Terrain == "bridge")
                {
                    var bridge = PortraitWidgets.Place("World.Bridge." + tile.X + "." + tile.Y, viewport, 0, 0, 64, 48);
                    var image = bridge.gameObject.AddComponent<Image>(); image.sprite = EstateArt.Load().Get("bridge-ne-sw-base"); image.raycastTarget = false;
                    drawing.Add(bridge, local, new Vector2(64, 48), new Vector2(.5f, .5f));
                }
                var x = tile.X; var y = tile.Y;
                var hit = PortraitWidgets.Place("World.Tile." + x + "." + y, viewport, 0, 0, 64, 32);
                var target = hit.gameObject.AddComponent<WorldMapTileRaycast>(); target.color = Color.clear;
                var button = hit.gameObject.AddComponent<Button>(); button.targetGraphic = target; button.transition = Selectable.Transition.None;
                button.interactable = !client.Busy; button.onClick.AddListener(() => select(x, y));
                drawing.Add(hit, local, new Vector2(64, 32), new Vector2(.5f, .5f));
            }
            foreach (var target in chunk.Targets.OrderBy(t => t.X + t.Y).ThenBy(t => t.Id, StringComparer.Ordinal))
            {
                var local = new Vector2(target.X - chunk.X * chunk.Size + .5f, target.Y - chunk.Y * chunk.Size + .5f);
                var role = WorldMapCameraState.TargetRole(target);
                if (role != null) Piece("World.Target." + target.Id, role, local, new Vector2(72, 84), art.Pivot(role));
                if (!string.IsNullOrEmpty(target.AllianceName)) Piece("World.Alliance." + target.Id, "world-alliance-flag", local + new Vector2(.35f, -.35f), new Vector2(24, 36), art.Pivot("world-alliance-flag"));
                if (target.Kind == "resource" && client.WorldMarches?.Any(m => m.TargetId == target.Id && m.State == "gathering") == true)
                    Piece("World.Gathering." + target.Id, "world-status-gathering", local, new Vector2(28, 28), new Vector2(.5f, .5f), 66);
                if (target.Kind == "city" && target.Id == client.WorldCity?.Id)
                {
                    var shield = client.WorldShield;
                    if (shield != null && shield.ShieldUntilMs > shield.UtcHighWatermark)
                        Piece("World.Protection." + target.Id, "world-protection-normal", local, new Vector2(72, 84), art.Pivot("world-protection-normal"));
                    var label = Caption(ui, viewport, "내 도시", 0, 0, 64, 24, 14, true, TextAnchor.MiddleCenter);
                    drawing.Add(label.rectTransform, local, new Vector2(64, 24), new Vector2(.5f, .5f), -16);
                }
            }
            if (chunk.Tile(selectedX, selectedY) != null)
            {
                var selected = Caption(ui, viewport, "•", 0, 0, 24, 24, 22, false, TextAnchor.MiddleCenter);
                drawing.Add(selected.rectTransform, new Vector2(selectedX - chunk.X * chunk.Size + .5f, selectedY - chunk.Y * chunk.Size + .5f), new Vector2(24, 24), new Vector2(.5f, .5f), 76);
            }
            ActionButton(ui, viewport, "World.ZoomIn", "+", () => view.Step(1), width - 52, 8, 48, true);
            ActionButton(ui, viewport, "World.ZoomOut", "−", () => view.Step(-1), width - 52, 64, 48, true);
            view.Tapped = cell => { if (!client.Busy && cell.HasValue) select(cell.Value.x + chunk.X * chunk.Size, cell.Value.y + chunk.Y * chunk.Size); };
            view.Zoomed = drawing.Layout; view.Moved = drawing.Layout;
            drawing.Layout();

            void Terrain(string kind, string role)
            {
                var rect = PortraitWidgets.Place("World.Terrain." + (kind ?? "base"), viewport, 0, 0, width, height);
                var terrain = rect.gameObject.AddComponent<WorldMapTerrainGraphic>();
                terrain.Model = model; terrain.Chunk = chunk; terrain.Patch = art.Get(role); terrain.Terrain = kind; terrain.raycastTarget = false;
                drawing.Add(terrain); view.Track(terrain);
            }
            void Piece(string name, string role, Vector2 local, Vector2 size, Vector2 pivot, float lift = 0)
            {
                var rect = PortraitWidgets.Place(name, viewport, 0, 0, size.x, size.y);
                var image = rect.gameObject.AddComponent<Image>(); image.sprite = art.Get(role); image.raycastTarget = false;
                drawing.Add(rect, local, size, pivot, lift);
            }
        }
    }
}
