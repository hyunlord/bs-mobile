using System;
using System.Collections.Generic;
using System.Linq;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    public sealed class EstateMapActions
    {
        public Action Redraw, Depart, Settings;
        public Action<int, Vector2> ViewChanged;
        public Action<PortraitMetaPage> Navigate;
    }

    // Estate map prototype page (#366, development option only). Since #368 the map is isometric and fills the screen like the
    // approved key art (m3-estate-keyarts-r2 A-30day-ui): a thin resource bar, a collapsible goal line, compact tabs and a
    // small departure button over the map. Astra's approved stage sprites provide the building silhouettes.
    public static partial class PortraitMetaPanels
    {
        public const string EstateMapGlyphs = "영지 시제품 저장 안 함 계정 사본 새 전체 동네 건물 우리 집 망루 우물 경작지 벌목장 철공소 폐허 아직 짓지 않음 완성 재고 전문화 공사 중 이동 닫기 확정 취소 옮길 칸을 누르세요 놓을 수 있습니다 지금 자리입니다 열린 구역 밖입니다 공사 중에는 옮길 수 없습니다 와 과 겹칩니다 탐사로 열 땅 탐사전 여는 땅입니다 이 시제품에서는 열리지 않음 M3c 옮겼습니다 저장하지 않는 시제품입니다 닫힌 출정 목표 생산 재고를 처음 받기 터 2곳 하나 옮기기 달성 보상 모든 받았습니다 가신 도전 상점 보석 짓기 올리기 예약 다음 최고 레벨 공사 시작 진행 없음 비어 있음 자동 착공 켬 끔 대기열 받음 과제 재료가 모자랍니다 첫 경로 몫은 남겨 둡니다 먼저 부모 시설을 지어야 합니다 가득 찼습니다 하나를 골라 성장 방향을 정해요 으로 정하기 고르기 선택 지금 변경 다시 바꿀 수 있음 출정 중에는 고정 구역 개방 준비 원정 보급 창고 장기 검술 공방 카드 연구실 도시 기록소 호위 병영 건설 지원 시간 줄임 철재 생산 집중 판 효과는 M3b에서 연결 분 Lv / + − × ▸ ▾ % 장식 중심 마을 확장 북동 숲 서쪽 언덕 풍차 밭 휴경지 강가 영지 둘레 0123456789:";
        // Open land under the terrain patches: a mid tone of Astra's painted fallow/sage ground.
        static readonly Color OpenGround = new Color32(124, 116, 84, 255);
        static readonly Color GridLine = new Color32(36, 48, 26, 110);
        static readonly Color BlockTop = new Color32(176, 154, 112, 255);
        static readonly Color BlockLeft = new Color32(132, 112, 80, 255);
        static readonly Color BlockRight = new Color32(104, 88, 62, 255);
        static readonly Color SelectedFoot = new Color32(243, 213, 140, 150);
        static readonly Color ValidGhost = new Color32(96, 190, 104, 150);
        static readonly Color InvalidGhost = new Color32(205, 64, 48, 150);
        static readonly Color BarInk = new Color32(20, 16, 12, 205);
        const float BarTop = 6, BarHeight = 40, GoalTop = 50, GoalHeight = 44, BottomBar = 56, BottomGap = 8, WorkHeight = 44;
        static readonly Color GoodInk = new Color32(168, 222, 150, 255), BadInk = new Color32(240, 140, 120, 255), Gold = new Color32(243, 213, 140, 255);

        public static void EstateMap(UiShell ui, EstateMapScreen screen, MetaState account, EstateMapActions actions)
        {
            var root = Page(ui, "EstateMap", ""); var width = Width(ui); var height = Height(ui);
            var rules = EstateDecorRules.Load(); var night = rules.night;
            root.GetComponent<Image>().color = night.Colour;

            // The map fills the page; bars and panels sit over it and become insets so every row can still be dragged into view.
            var viewport = PortraitWidgets.Place("EstateMap.Viewport", root, 0, 0, width, height);
            viewport.gameObject.AddComponent<Image>().color = Color.clear; viewport.gameObject.AddComponent<RectMask2D>();
            var view = viewport.gameObject.AddComponent<EstateMapView>(); view.Model = screen.Map;
            var top = GoalTop + GoalHeight; var panel = PanelHeightFor(screen);
            var bottom = BottomBar + BottomGap * 3 + WorkHeight + (panel > 0 ? panel + BottomGap : 0);
            screen.Map.SetFrame(viewport.rect.size, top, bottom);
            screen.Map.SetFocus(screen.OpenAreas(night.openingMargin));
            if (!screen.Map.Framed) screen.FrameFirstView();
            // A newly selected building hidden behind the panel is brought into view; while moving, the camera stays where the
            // player put it so the destination preview remains visible.
            else if (screen.Selected != null && !screen.Moving && !screen.Map.InView(screen.Centre(screen.Selected.Value))) screen.Map.CentreOn(screen.Centre(screen.Selected.Value));

            // Painted map (#375, #379): ground over the whole painted area, Astra's terrain patches and earth roads, the art of the
            // land outside the open zones, one continuous night by distance from open land, warm lamps, then yard grounds,
            // shadows, and buildings and props sorted by foot. Grid lines show only while a building is being moved.
            var art = EstateArt.Load(); var tint = art.Tint;
            var terrainTint = viewport.gameObject.AddComponent<EstateTerrainMaterial>();
            terrainTint.Configure(tint, screen.Config.Zones.Where(screen.Open), rules.Colour("closed"));
            ui.Canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            var plan = screen.Plan(rules, art);
            bool Decor(EstateDrawItem item) => screen.DecorEnabled || !DecorSources.Contains(item.Source);
            var ground = Shapes(viewport, "EstateMap.Ground", view);
            // One cell inside the night's edge, so the opaque rim always covers the ground's last pixel row.
            ground.Footprint(plan.Area.x + 1, plan.Area.y + 1, plan.Area.width - 2, plan.Area.height - 2, night.Ground);
            foreach (var zone in screen.Config.Zones.Where(screen.Open)) ground.Footprint(zone.X, zone.Y, zone.Width, zone.Height, OpenGround);
            foreach (var item in plan.In(EstateDrawLayer.Terrain).Where(Decor)) Paint(viewport, view, art, item.Source == "terrain" ? terrainTint.Material : tint, item.Source == "road" ? "EstateMap.Road" : "EstateMap.Terrain", item);
            if (screen.Moving)
            {
                var grid = Shapes(viewport, "EstateMap.Grid", view);
                foreach (var zone in screen.Config.Zones.Where(screen.Open))
                {
                    for (var u = zone.X; u <= zone.X + zone.Width; u++) grid.Line(new Vector2(u, zone.Y), new Vector2(u, zone.Y + zone.Height), GridLine);
                    for (var v = zone.Y; v <= zone.Y + zone.Height; v++) grid.Line(new Vector2(zone.X, v), new Vector2(zone.X + zone.Width, v), GridLine);
                }
            }
            // The land to come in foot order, then the night over it: above the painted land, below every open-land sprite and
            // the bars. The night is one texture by distance from open land, so no zone edge shows inside it.
            foreach (var item in plan.In(EstateDrawLayer.Veiled).OrderBy(i => i.SortY).ThenBy(i => i.Key, StringComparer.Ordinal))
                Paint(viewport, view, art, tint, "EstateMap.Veiled:" + item.Source, item);
            var darkness = UiShell.Rect("EstateMap.Night", viewport); UiShell.Stretch(darkness);
            var nightGraphic = darkness.gameObject.AddComponent<EstateNight>(); nightGraphic.raycastTarget = false; nightGraphic.Model = screen.Map;
            // The night reaches `nightOverhang` cells past the painted area: terrain patches spill past their blocks, and whatever
            // spills stays under the night before its opaque rim.
            var o = night.nightOverhang; var nightArea = new RectInt(plan.Area.x - o, plan.Area.y - o, plan.Area.width + 2 * o, plan.Area.height + 2 * o);
            nightGraphic.Show(nightArea, EstateNight.Field(night, nightArea, screen.Config.Zones.Where(screen.Open).Select(z => new RectInt(z.X, z.Y, z.Width, z.Height)).ToArray()));
            view.Track(nightGraphic);
            foreach (var item in plan.In(EstateDrawLayer.Landmark).OrderBy(i => i.SortY).ThenBy(i => i.Key, StringComparer.Ordinal))
                Paint(viewport, view, art, tint, "EstateMap.Landmark:" + item.Role, item);
            var lamps = Shapes(viewport, "EstateMap.Lamps", view);
            foreach (var (grid, cells) in plan.Lamps) lamps.Glow(grid, cells, night.Lamp);
            // Warm light is ground illumination, never a gold cloud floating over the night.
            lamps.transform.SetSiblingIndex(darkness.GetSiblingIndex());
            foreach (var item in plan.In(EstateDrawLayer.Ground).Where(Decor)) Paint(viewport, view, art, tint, item.Source == "yard-ground" ? "EstateMap.Decor:yard-ground" : "EstateMap.Site", item);
            // No zone labels (#379): a tap on closed land names it; the whole view marks only the zone that can open next.
            var next = screen.NextZone();
            if (next != null)
            {
                var marker = ColorPlate(viewport, "EstateMap.NextZone", Gold, 0, 0, 14, 14); marker.localRotation = Quaternion.Euler(0, 0, 45);
                ColorPlate(marker, "EstateMap.NextZone.Core", BarInk, 4, 4, 6, 6);
                view.Pin(marker, new Vector2(next.X + next.Width * .5f, next.Y + next.Height * .5f), 0);
                marker.gameObject.SetActive(screen.Map.Zoom == screen.Map.WholeMap);
                view.Zoomed += () => marker.gameObject.SetActive(screen.Map.Zoom == screen.Map.WholeMap);
            }
            if (screen.Selected != null)
            {
                var selected = screen.Building(screen.Selected.Value); var kind = screen.Kind(selected.Kind);
                Shapes(viewport, "EstateMap.Selection", view).Footprint(selected.X, selected.Y, kind.Width, kind.Height, SelectedFoot);
            }
            var standing = plan.In(EstateDrawLayer.Standing).Where(Decor).OrderBy(i => i.SortY).ThenBy(i => i.BuildingId < 0 ? 1 : 0).ThenBy(i => i.Key, StringComparer.Ordinal).ToArray();
            foreach (var item in standing.Where(i => i.BuildingId >= 0))
                Paint(viewport, view, art, tint, "EstateMap.Shadow:" + item.BuildingId, item, art.Shadow(item.BuildingArt, item.Stage), new Color(1, 1, 1, .8f));
            foreach (var item in standing)
            {
                if (item.BuildingId < 0) { Paint(viewport, view, art, tint, DecorSources.Contains(item.Source) ? "EstateMap.Decor:" + item.Source : "EstateMap.Piece:" + item.Role, item); continue; }
                var building = screen.Building(item.BuildingId); var kind = screen.Kind(building.Kind);
                var colour = item.Colour; if (screen.Moving && screen.Selected == building.Id) colour.a *= .45f;
                Paint(viewport, view, art, tint, "EstateMap.Building:" + building.Id, item, art.Body(item.BuildingArt, item.Stage), colour);
                // Names stay hidden until the building is selected (#368).
                if (screen.Selected == building.Id)
                    view.Pin(NameTag(ui, viewport, screen.Name(building.Kind) + " Lv " + building.Level), new Vector2(building.X + kind.Width * .5f, building.Y + kind.Height * .5f), 128);
            }
            foreach (var item in plan.In(EstateDrawLayer.Foreground).Where(Decor)) Paint(viewport, view, art, tint, "EstateMap.Piece:" + item.Role, item);
            if (screen.Moving && screen.Selected != null)
            {
                var kind = screen.Kind(screen.Building(screen.Selected.Value).Kind);
                Shapes(viewport, "EstateMap.Preview", view).Footprint(screen.Preview.x, screen.Preview.y, kind.Width, kind.Height, screen.PreviewValid ? ValidGhost : InvalidGhost);
            }
            view.Tapped = cell =>
            {
                if (screen.Moving) { if (cell != null) { screen.Place(cell.Value); actions.Redraw(); } return; }
                var hit = cell == null ? null : screen.BuildingAt(cell.Value);
                var zone = hit == null && cell != null ? screen.ZoneAt(cell.Value) : null;
                if (zone != null && !screen.Open(zone)) { if (screen.ZoneChoice != zone.Id) { screen.SelectZone(zone.Id); actions.Redraw(); } return; }
                if (hit != screen.Selected || screen.ZoneChoice != null || screen.QueueOpen) { screen.Select(hit); actions.Redraw(); }
            };
            view.Layout();

            ResourceStrip(ui, root, screen.Catalog, screen.State, actions.Navigate, width);
            GoalLine(ui, root, screen, actions, width);
            string ZoomName() => screen.Map.Zoom == screen.Map.WholeMap ? "전체" : screen.Map.Zoom == screen.Map.Opening ? "영지" : screen.Map.Zoom == EstateMapModel.Neighbourhood ? "동네" : "건물";
            var controlsY = height - bottom - 104;
            var decorToggle = ActionButton(ui, root, "EstateMap.Decor", "장식 " + (screen.DecorEnabled ? "켬" : "끔"), () => { screen.ToggleDecor(); actions.Redraw(); }, 10, controlsY + 4, 84, true, 44);
            Compact(decorToggle.transform);
            var chip = ColorPlate(root, "EstateMap.ZoomLevel", BarInk, 10, controlsY + 60, 56, 28);
            var zoomCaption = Caption(ui, chip, ZoomName(), 0, 0, 56, 28, 13, false, TextAnchor.MiddleCenter);
            view.Zoomed += () => zoomCaption.text = ZoomName();
            // The view is remembered for the next visit (#379): written when a drag ends or the zoom changes, never per frame.
            view.Moved = () => actions.ViewChanged?.Invoke(screen.Map.Zoom, screen.Map.ViewportToGrid(screen.Map.VisibleCentre));
            PortraitRunPanels.RoundIcon(ui, root, "EstateMap.ZoomIn", "+", () => view.Step(1), width - 54, controlsY);
            PortraitRunPanels.RoundIcon(ui, root, "EstateMap.ZoomOut", "−", () => view.Step(-1), width - 54, controlsY + 52);
            var panelTop = height - BottomBar - BottomGap * 3 - WorkHeight - panel;
            Text liveStatus = null;
            if (screen.Specializing) SpecializePanel(ui, root, screen, actions, width, panelTop, panel);
            else if (screen.Selected != null) liveStatus = SelectionPanel(ui, root, screen, actions, width, panelTop, panel);
            else if (screen.ZoneChoice != null) ZonePanel(ui, root, screen, actions, width, panelTop, panel);
            else if (screen.QueueOpen) QueuePanel(ui, root, screen, actions, width, panelTop, panel);
            var work = WorkStrip(ui, root, screen, actions, width, height - BottomBar - BottomGap * 2 - WorkHeight, out var claimLabel);
            // Once a second: Core applies the interval; a level, queue, zone or task change redraws (after any drag), while the
            // remaining time and stock only refresh their text.
            view.Redraw = actions.Redraw;
            view.Tick = () =>
            {
                if (screen.Advance()) { view.RequestRedraw(); return; }
                work.text = screen.WorkLine();
                if (claimLabel != null) claimLabel.text = "받기 " + screen.StockTotal;
                if (liveStatus != null && screen.Selected != null) liveStatus.text = screen.Status(screen.Building(screen.Selected.Value));
            };
            BottomBarWithTabs(ui, root, account, actions, width, height);
        }

        // Sources the [장식 켬/끔] development switch hides (#372); terrain, buildings, the river skeleton and the darkness stay.
        static readonly HashSet<string> DecorSources = new HashSet<string>(StringComparer.Ordinal) { "yard", "roadside", "empty-land", "yard-ground", "fence", "peasant" };

        // One estate sprite: its pivot stands on the item's grid point, `Width` is in scale-1 canvas units, and the colour goes to
        // the EstateTint shader as (saturation, brightness, light, alpha). Without the shader the art draws untinted.
        static Image Paint(RectTransform viewport, EstateMapView view, EstateArt art, Material tint, string name, EstateDrawItem item, Sprite sprite = null, Color? colour = null)
        {
            sprite ??= art.Get(item.Role);
            var rect = UiShell.Rect(name, viewport);
            Image image;
            if (item.Source == "terrain")
            {
                var terrain = rect.gameObject.AddComponent<EstateTerrainImage>();
                terrain.Grid = item.Grid; terrain.PaintedWidth = item.Width; image = terrain;
            }
            else image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite; image.raycastTarget = false; image.material = tint;
            var c = colour ?? item.Colour; image.color = tint != null ? c : new Color(1, 1, 1, c.a);
            var size = sprite.rect.size; var pivot = new Vector2(sprite.pivot.x / size.x, sprite.pivot.y / size.y);
            view.Prop(rect, item.Grid, item.Width / (EstateMapModel.HalfWidth * 2), pivot, item.Aspect > 0 ? item.Aspect : size.y / size.x);
            return image;
        }

        static float SortY(EstateMapScreen screen, MetaEstateBuilding building)
        {
            var kind = screen.Kind(building.Kind); return EstateMapModel.SortY(building.X, building.Y, kind.Width, kind.Height);
        }

        static Color Mul(Color a, Color b, float alpha) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a * alpha);

        static EstateIsoShapes Shapes(RectTransform viewport, string name, EstateMapView view)
        {
            var rect = UiShell.Rect(name, viewport); UiShell.Stretch(rect);
            var shapes = rect.gameObject.AddComponent<EstateIsoShapes>(); shapes.raycastTarget = false; shapes.Model = view.Model;
            view.Track(shapes); return shapes;
        }

        static Text NameTag(UiShell ui, RectTransform viewport, string value)
        {
            var plate = ColorPlate(viewport, "EstateMap.NameTag", BarInk, 0, 0, 150, 26);
            Caption(ui, plate, value, 0, 0, 150, 26, 13, false, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
            return plate.GetComponentInChildren<Text>();
        }

        static void ResourceStrip(UiShell ui, RectTransform root, MetaCatalog catalog, MetaState state, Action<PortraitMetaPage> navigate, float width)
        {
            var bar = ColorPlate(root, "Resource bar", BarInk, 8, BarTop, width - 16, BarHeight);
            ColorPlate(bar, "Gold rule", new Color32(139, 106, 51, 200), 0, BarHeight - 1, width - 16, 1);
            var cell = (width - 16) / 5;
            for (var i = 0; i < catalog.Materials.Length; i++)
            {
                var material = catalog.Materials[i]; state.Wallet.TryGetValue(material.Id, out var amount);
                MaterialArt(ui, bar, material.Id, material.ArtRole, i * cell + 8, 10, 20);
                Caption(ui, bar, amount.ToString(), i * cell + 31, 6, cell - 33, 28, 13);
            }
            Caption(ui, bar, "보석 +", cell * 4 + 6, 6, cell - 10, 28, 13);
            Hit(bar, "Estate.Gems", () => navigate(PortraitMetaPage.Shop), cell * 4, -2, cell, 44);
        }

        static void GoalLine(UiShell ui, RectTransform root, EstateMapScreen screen, EstateMapActions actions, float width)
        {
            var collapsed = screen.GoalCollapsed;
            var lineWidth = collapsed ? 92 : width - 74;
            var line = ColorPlate(root, "EstateMap.GoalLine", BarInk, 8, GoalTop, lineWidth, GoalHeight);
            if (collapsed) Caption(ui, line, "목표 ▸", 12, 0, lineWidth - 16, GoalHeight, 14);
            else
            {
                Caption(ui, line, "영지 시제품 · 저장 안 함" + (screen.FromSave ? " · 계정 영지 사본" : " · 새 영지"), 10, 2, lineWidth - 34, 20, 12).color = new Color32(243, 213, 140, 255);
                var claimWidth = screen.GoalReady ? 64 : 0;
                Caption(ui, line, screen.Notice.Length > 0 ? screen.Notice : screen.GoalText(), 10, 21, lineWidth - 34 - claimWidth, 20, 13);
                Caption(ui, line, "▾", lineWidth - 24, 0, 20, GoalHeight, 14);
                Hit(line, "EstateMap.Goal", () => { screen.ToggleGoal(); actions.Redraw(); }, 0, 0, lineWidth - claimWidth - 4, GoalHeight);
                if (screen.GoalReady) ActionButton(ui, line, "EstateMap.GoalClaim", "받기", () => { screen.ClaimGoal(); actions.Redraw(); }, lineWidth - claimWidth - 28, 0, claimWidth, true, 44, "ui.button.primary");
            }
            if (collapsed) Hit(line, "EstateMap.Goal", () => { screen.ToggleGoal(); actions.Redraw(); }, 0, 0, lineWidth, GoalHeight);
            PortraitRunPanels.RoundIcon(ui, root, "Estate.Settings", "settings", actions.Settings, width - 54, GoalTop);
        }

        static void BottomBarWithTabs(UiShell ui, RectTransform root, MetaState account, EstateMapActions actions, float width, float height)
        {
            var y = height - BottomBar - BottomGap; const float departWidth = 92;
            var bar = ColorPlate(root, "Navigation", BarInk, 8, y, width - departWidth - 24, BottomBar);
            var pages = new[] { PortraitMetaPage.Estate, PortraitMetaPage.Vassals, PortraitMetaPage.Challenges, PortraitMetaPage.Shop };
            var names = new[] { "영지", "가신", "도전", "상점" }; var cell = (width - departWidth - 24) / 4;
            for (var i = 0; i < pages.Length; i++)
            {
                var page = pages[i];
                var caption = Caption(ui, bar, names[i], i * cell, 0, cell, BottomBar, 14, false, TextAnchor.MiddleCenter);
                if (page == PortraitMetaPage.Estate) caption.color = new Color32(243, 213, 140, 255);
                Hit(bar, "Tab." + page, () => actions.Navigate(page), i * cell, 0, cell, BottomBar);
                if (page == PortraitMetaPage.Challenges && Pending(account, MetaClaimKind.Challenge))
                    ColorPlate(bar, "Claim dot", new Color32(194, 39, 28, 255), i * cell + cell - 18, 10, 10, 10);
            }
            ActionButton(ui, root, "Estate.Depart", "출정", actions.Depart, width - departWidth - 8, y, departWidth, true, BottomBar, "ui.button.primary");
        }

        static float PanelHeightFor(EstateMapScreen screen) =>
            screen.Specializing ? 300 : screen.Selected != null ? 206 : screen.ZoneChoice != null ? 150 : screen.QueueOpen ? 214 : 0;

        // Sheet and strip buttons are narrower than the portrait default, so their labels drop to 14 to stay inside.
        static void Compact(Transform parent)
        {
            foreach (var button in parent.GetComponentsInChildren<Button>())
                foreach (var text in button.GetComponentsInChildren<Text>()) { text.resizeTextForBestFit = false; text.fontSize = Mathf.Min(text.fontSize, 14); }
        }

        static RectTransform Sheet(RectTransform root, string name, float width, float y, float height)
        {
            var panel = ColorPlate(root, name, new Color32(52, 41, 27, 245), 8, y, width - 16, height);
            panel.GetComponent<Image>().raycastTarget = true; // taps on a sheet never reach the map below it
            ColorPlate(panel, "Gold rule", new Color32(139, 106, 51, 255), 0, 0, width - 16, 2);
            return panel;
        }

        static Text SelectionPanel(UiShell ui, RectTransform root, EstateMapScreen screen, EstateMapActions actions, float width, float y, float height)
        {
            var building = screen.Building(screen.Selected.Value); var kind = screen.Kind(building.Kind); var id = building.Id;
            var panel = Sheet(root, "EstateMap.Selection", width, y, height);
            Caption(ui, panel, screen.Name(building.Kind) + " Lv " + building.Level + " / " + kind.MaxLevel(), 14, 8, width - 44, 28, 18);
            var status = Caption(ui, panel, screen.Status(building), 14, 36, width - 44, 22, 13);
            var half = (width - 54) / 2; var third = (width - 64) / 3;
            if (screen.Moving)
            {
                var reason = Caption(ui, panel, "옮길 칸을 누르세요 · " + screen.ReasonText(screen.PreviewReason), 14, 64, width - 44, 30, 13);
                reason.color = screen.PreviewValid ? GoodInk : BadInk;
                ActionButton(ui, panel, "EstateMap.Confirm", "확정", () => { screen.Confirm(); actions.Redraw(); }, 14, 146, half, screen.PreviewValid, 48, "ui.button.primary");
                ActionButton(ui, panel, "EstateMap.Cancel", "취소", () => { screen.Cancel(); actions.Redraw(); }, 24 + half, 146, half, true, 48);
                return status;
            }
            var next = screen.NextStep(id);
            Caption(ui, panel, next == null ? "최고 레벨" : "다음 Lv " + (building.Level + 1) + " · " + screen.Amounts(next.Value.Cost) + " · " + EstateMapScreen.Clock(next.Value.Seconds), 14, 60, width - 44, 22, 13);
            if (screen.Notice.Length > 0) Caption(ui, panel, screen.Notice, 14, 86, width - 44, 22, 13).color = Gold;
            Button primary;
            if (screen.Reserved(id))
                primary = ActionButton(ui, panel, "EstateMap.CancelReservation:" + id, "예약 취소", () => { screen.CancelReservation(id); actions.Redraw(); }, 14, 146, third, true, 48);
            else if (next != null)
                primary = ActionButton(ui, panel, "EstateMap.Build", building.Level == 0 ? "짓기" : "올리기", () => { screen.StartWork(id); actions.Redraw(); }, 14, 146, third, !screen.UnderConstruction(id), 48, "ui.button.primary");
            else
                primary = ActionButton(ui, panel, "EstateMap.Specialize", "전문화", () => { screen.BeginSpecialize(); actions.Redraw(); }, 14, 146, third, kind.Branches != null && kind.Branches.Length > 0, 48, "ui.button.primary");
            ActionButton(ui, panel, "EstateMap.Move", "이동", () => { screen.BeginMove(); actions.Redraw(); }, 24 + third, 146, third, !screen.UnderConstruction(id), 48);
            ActionButton(ui, panel, "EstateMap.Close", "닫기", () => { screen.Select(null); actions.Redraw(); }, 34 + third * 2, 146, third, true, 48);
            if (next == null && building.Branch != null && primary != null) primary.GetComponentInChildren<Text>().text = "전문화 변경";
            Compact(panel);
            return status;
        }

        // Specialization choice, after the r2 board 03: two cards, the chosen one with a gold rim, one confirm button.
        static void SpecializePanel(UiShell ui, RectTransform root, EstateMapScreen screen, EstateMapActions actions, float width, float y, float height)
        {
            var building = screen.Building(screen.Selected.Value); var kind = screen.Kind(building.Kind);
            var panel = Sheet(root, "EstateMap.Specialization", width, y, height);
            Caption(ui, panel, screen.Name(building.Kind) + " Lv " + building.Level + " · 전문화", 14, 8, width - 92, 28, 18);
            PortraitRunPanels.RoundIcon(ui, panel, "EstateMap.BranchClose", "×", () => { screen.CloseSpecialize(); actions.Redraw(); }, width - 70, 4);
            Caption(ui, panel, "하나를 골라 성장 방향을 정해요.", 14, 38, width - 44, 22, 13);
            var branches = kind.Branches ?? Array.Empty<string>();
            for (var i = 0; i < branches.Length; i++)
            {
                var branch = branches[i]; var chosen = screen.BranchChoice == branch; var cardY = 66 + i * 84;
                if (chosen) ColorPlate(panel, "Chosen rim", Gold, 12, cardY - 2, width - 40, 80);
                var card = ColorPlate(panel, "EstateMap.BranchCard:" + branch, new Color32(236, 224, 196, 255), 14, cardY, width - 44, 76);
                Caption(ui, card, screen.BranchName(branch) + (building.Branch == branch ? " · 지금" : ""), 12, 6, width - 120, 24, 15, true).fontStyle = FontStyle.Bold;
                Caption(ui, card, screen.BranchEffect(branch), 12, 30, width - 72, 20, 13, true);
                var note = screen.BranchNote(branch);
                if (note != screen.BranchEffect(branch)) Caption(ui, card, note, 12, 50, width - 72, 20, 11, true);
                if (chosen) Caption(ui, card, "선택", width - 104, 6, 48, 24, 13, true, TextAnchor.MiddleRight).color = new Color32(163, 39, 30, 255);
                Hit(card, "EstateMap.Branch:" + branch, () => { screen.ChooseBranch(branch); actions.Redraw(); }, 0, 0, width - 44, 76);
            }
            var label = screen.BranchChoice == null ? "고르기" : screen.BranchName(screen.BranchChoice) + "으로 정하기";
            ActionButton(ui, panel, "EstateMap.BranchConfirm", label, () => { screen.ConfirmBranch(); actions.Redraw(); }, 14, 236, width - 44, screen.BranchChoice != null && screen.BranchChoice != building.Branch, 44, "ui.button.destructive");
            Caption(ui, panel, screen.Notice.Length > 0 ? screen.Notice : "다시 바꿀 수 있음 · 출정 중에는 고정", 14, 280, width - 44, 18, 11, false, TextAnchor.MiddleCenter);
        }

        static void ZonePanel(UiShell ui, RectTransform root, EstateMapScreen screen, EstateMapActions actions, float width, float y, float height)
        {
            var zone = screen.Config.Zones.Single(z => z.Id == screen.ZoneChoice);
            var panel = Sheet(root, "EstateMap.Zone", width, y, height);
            var explore = screen.ZoneLabel(zone) != "닫힌 구역";
            // The map carries no zone labels (#379); the name and its condition appear only here, on a tap.
            Caption(ui, panel, screen.ZoneName(zone) + (explore ? " · 탐사로 열 땅" : ""), 14, 8, width - 44, 28, 18);
            var unlock = explore ? null : zone.Unlock;
            Caption(ui, panel, explore ? "탐사전(M3c)으로 여는 땅입니다 · 이 시제품에서는 열리지 않음" : unlock == null ? "아직 열 수 없는 땅입니다" : "생산 터 " + unlock.CompletedProductionSites + "곳 완성 · " + screen.Amounts(unlock.Cost) + " · " + EstateMapScreen.Clock(unlock.Seconds), 14, 36, width - 44, 22, 13);
            if (screen.Notice.Length > 0) Caption(ui, panel, screen.Notice, 14, 60, width - 44, 22, 13).color = Gold;
            var half = (width - 54) / 2;
            ActionButton(ui, panel, "EstateMap.OpenZone", "개방", () => { screen.OpenZone(zone.Id); actions.Redraw(); }, 14, 92, half, unlock != null && screen.State.Estate.ZoneWork == null, 48, "ui.button.primary");
            ActionButton(ui, panel, "EstateMap.Close", "닫기", () => { screen.Select(null); actions.Redraw(); }, 24 + half, 92, half, true, 48);
            Compact(panel);
        }

        static void QueuePanel(UiShell ui, RectTransform root, EstateMapScreen screen, EstateMapActions actions, float width, float y, float height)
        {
            var estate = screen.State.Estate;
            var panel = Sheet(root, "EstateMap.Queue", width, y, height);
            Caption(ui, panel, "공사 대기열 · 진행 1 · 예약 " + screen.Config.Queue.Reservations, 14, 8, width - 44, 28, 18);
            Caption(ui, panel, estate.Active is { } work ? "진행 · " + screen.Name(screen.Building(work.BuildingId).Kind) + " Lv " + work.TargetLevel + " · " + EstateMapScreen.Clock(work.RemainingSeconds) : "진행 · 없음", 14, 38, width - 44, 22, 13);
            for (var i = 0; i < screen.Config.Queue.Reservations; i++)
            {
                var rowY = 62 + i * 46;
                if (i < estate.Reservations.Length)
                {
                    var reservation = estate.Reservations[i]; var id = reservation.BuildingId;
                    Caption(ui, panel, "예약 " + (i + 1) + " · " + screen.Name(screen.Building(id).Kind) + " Lv " + reservation.TargetLevel, 14, rowY, width - 140, 44, 13);
                    ActionButton(ui, panel, "EstateMap.CancelReservation:" + id, "취소", () => { screen.CancelReservation(id); actions.Redraw(); }, width - 116, rowY, 88, true, 44);
                }
                else Caption(ui, panel, "예약 " + (i + 1) + " · 비어 있음", 14, rowY, width - 140, 44, 13);
            }
            var third = (width - 64) / 3;
            ActionButton(ui, panel, "EstateMap.AutoStart", "자동 착공 " + (estate.AutoStart ? "켬" : "끔"), () => { screen.ToggleAutoStart(); actions.Redraw(); }, 14, 158, third, true, 48);
            ActionButton(ui, panel, "EstateMap.StartReserved", "예약 시작", () => { screen.StartReserved(); actions.Redraw(); }, 24 + third, 158, third, estate.Active == null && estate.Reservations.Length > 0, 48, "ui.button.primary");
            ActionButton(ui, panel, "EstateMap.Close", "닫기", () => { screen.Select(null); actions.Redraw(); }, 34 + third * 2, 158, third, true, 48);
            Compact(panel);
        }

        // The work line above the tabs: current construction and queue (tap for the queue), a claim for site stock, and the
        // development time skip that lets the prototype be reviewed without waiting.
        static Text WorkStrip(UiShell ui, RectTransform root, EstateMapScreen screen, EstateMapActions actions, float width, float y, out Text claimLabel)
        {
            var strip = ColorPlate(root, "EstateMap.Work", BarInk, 8, y, width - 16, WorkHeight);
            const float skip = 62; var claim = screen.StockTotal > 0 ? 78f : 0f;
            var textWidth = width - 16 - skip * 2 - claim - 12;
            var line = Caption(ui, strip, screen.WorkLine(), 10, 0, textWidth - 10, WorkHeight, 12);
            Hit(strip, "EstateMap.QueueToggle", () => { screen.ToggleQueue(); actions.Redraw(); }, 0, 0, textWidth, WorkHeight);
            claimLabel = claim > 0 ? ActionButton(ui, strip, "EstateMap.ClaimStock", "받기 " + screen.StockTotal, () => { screen.ClaimProduction(); actions.Redraw(); }, textWidth, 0, claim - 4, true, 44, "ui.button.primary").GetComponentInChildren<Text>() : null;
            ActionButton(ui, strip, "EstateMap.Skip60", "+1분", () => { screen.SkipTime(60); actions.Redraw(); }, textWidth + claim, 0, skip - 4, true, 44);
            ActionButton(ui, strip, "EstateMap.Skip600", "+10분", () => { screen.SkipTime(600); actions.Redraw(); }, textWidth + claim + skip, 0, skip - 4, true, 44);
            Compact(strip);
            return line;
        }

        static void ResourceBar(UiShell ui, RectTransform root, MetaCatalog catalog, MetaState state, Action<PortraitMetaPage> navigate)
        {
            var inner = Width(ui) - 20;
            var wallet = Plate(ui, root, "Resource bar", "ui.bar.wood", 10, 50, inner, 44);
            var resourceWidth = inner / 5;
            for (var i = 0; i < catalog.Materials.Length; i++)
            {
                var material = catalog.Materials[i]; state.Wallet.TryGetValue(material.Id, out var amount);
                MaterialArt(ui, wallet, material.Id, material.ArtRole, 12 + i * resourceWidth, 10, 24);
                Caption(ui, wallet, amount.ToString(), 36 + i * resourceWidth, 8, resourceWidth - 31, 28, 13);
            }
            Caption(ui, wallet, "보석 [n]+", resourceWidth * 4, 7, resourceWidth, 30, 11);
            Hit(wallet, "Estate.Gems", () => navigate(PortraitMetaPage.Shop), resourceWidth * 4, 0, resourceWidth, 44);
        }
    }

    // Pixel-wise open-land tint stops a large warm ground patch spilling colour beyond its open cells (#385).
    public sealed class EstateTerrainMaterial : MonoBehaviour
    {
        public Material Material { get; private set; }
        public void Configure(Material source, IEnumerable<AccountEstateZone> zones, Color closed)
        {
            if (source == null) return;
            var areas = zones.Select(z => new Vector4(z.X, z.Y, z.X + z.Width, z.Y + z.Height)).ToArray();
            if (areas.Length > 32) throw new InvalidOperationException("Estate ground supports at most 32 open zones.");
            var padded = new Vector4[32]; Array.Copy(areas, padded, areas.Length);
            Material = new Material(source) { name = "Estate ground", hideFlags = HideFlags.HideAndDontSave };
            Material.SetFloat("_EstateGround", 1); Material.SetInt("_EstateOpenCount", areas.Length);
            Material.SetVectorArray("_EstateOpenAreas", padded); Material.SetColor("_EstateClosed", closed);
        }
        void OnDestroy() { if (Material != null) Destroy(Material); }
    }

    public sealed class EstateTerrainImage : Image
    {
        public Vector2 Grid;
        public float PaintedWidth;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            base.OnPopulateMesh(mesh);
            if (sprite == null) return;
            var rect = rectTransform.rect; var size = sprite.rect.size;
            if (rect.width <= 0 || rect.height <= 0 || size.x <= 0 || size.y <= 0) return;
            var pivot = sprite.pivot / size; var vertex = new UIVertex();
            for (var i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                var fraction = new Vector2((vertex.position.x - rect.xMin) / rect.width, (vertex.position.y - rect.yMin) / rect.height);
                var offset = fraction - pivot;
                var grid = Grid + EstateMapModel.Unproject(new Vector2(offset.x * PaintedWidth, -offset.y * PaintedWidth * size.y / size.x));
                vertex.uv1 = new Vector4(grid.x, grid.y, 0, 0); mesh.SetUIVertex(vertex, i);
            }
        }
    }

    // Grid-anchored shapes are rebuilt from the current projection when the map pans or zooms.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EstateIsoShapes : MaskableGraphic
    {
        enum Kind { Footprint, Line, Block, Glow }
        struct Shape { public Kind Kind; public Vector2 A, B; public float Rise; public Color Top, Left, Right; public int Feather; }
        readonly List<Shape> shapes = new List<Shape>();
        public EstateMapModel Model;
        public int Count => shapes.Count;
        const int GlowSegments = 24;

        public void Footprint(int u, int v, int width, int height, Color color) =>
            shapes.Add(new Shape { Kind = Kind.Footprint, A = new Vector2(u, v), B = new Vector2(u + width, v + height), Top = color });
        public void Line(Vector2 from, Vector2 to, Color color, float thickness = 1) => shapes.Add(new Shape { Kind = Kind.Line, A = from, B = to, Top = color, Rise = thickness });
        // Soft light: `color` at a grid point fading to clear over a circle of `cells` (an ellipse on screen), normal blending.
        public void Glow(Vector2 centre, float cells, Color color) => shapes.Add(new Shape { Kind = Kind.Glow, A = centre, Rise = cells, Top = color });
        public void Block(int u, int v, int width, int height, float rise, Color top, Color left, Color right) =>
            shapes.Add(new Shape { Kind = Kind.Block, A = new Vector2(u, v), B = new Vector2(u + width, v + height), Rise = rise, Top = top, Left = left, Right = right });

        Vector2 Local(Vector2 grid, float rise = 0)
        {
            var point = Model.GridToViewport(grid); var rect = rectTransform.rect;
            return new Vector2(rect.xMin + point.x, rect.yMax - point.y + rise * Model.Scale);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (Model == null) return;
            foreach (var shape in shapes)
            {
                var a = shape.A; var b = shape.B;
                // Diamond corners of the footprint (top, right, bottom, left on screen).
                Vector2 n = new Vector2(a.x, a.y), e = new Vector2(b.x, a.y), s = new Vector2(b.x, b.y), w = new Vector2(a.x, b.y);
                switch (shape.Kind)
                {
                    case Kind.Footprint: Quad(mesh, Local(n), Local(e), Local(s), Local(w), shape.Top); break;
                    case Kind.Line:
                    {
                        var p = Local(a); var q = Local(b); var normal = new Vector2(q.y - p.y, p.x - q.x).normalized * (shape.Rise > 0 ? shape.Rise : 1) * .5f;
                        Quad(mesh, p + normal, q + normal, q - normal, p - normal, shape.Top); break;
                    }
                    case Kind.Glow:
                    {
                        // A fan of GlowSegments triangles; the rim is computed per vertex so a rebuild allocates nothing.
                        var start = mesh.currentVertCount; Color32 rim = shape.Top; rim.a = 0;
                        mesh.AddVert(Local(a), shape.Top, Vector2.zero);
                        for (var i = 0; i < GlowSegments; i++)
                        {
                            var angle = i * Mathf.PI * 2 / GlowSegments;
                            mesh.AddVert(Local(a + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * shape.Rise), rim, Vector2.zero);
                            mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % GlowSegments);
                        }
                        break;
                    }
                    case Kind.Block:
                    {
                        var r = shape.Rise;
                        Quad(mesh, Local(w), Local(s), Local(s, r), Local(w, r), shape.Left);
                        Quad(mesh, Local(s), Local(e), Local(e, r), Local(s, r), shape.Right);
                        Quad(mesh, Local(n, r), Local(e, r), Local(s, r), Local(w, r), shape.Top); break;
                    }
                }
            }
        }

        static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
        {
            var start = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero); mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }

    // Lays grid-anchored elements out for the current zoom and turns one-finger drags into pans, two-finger pinches and
    // mouse-wheel turns into zoom steps, and taps into diamond cells. Buildings carry no buttons: a tap picks the building whose
    // footprint contains the tapped cell, so selection follows the isometric grid rather than sprite rectangles (#368).
    // The estate's night (#379): one texture over the plan's painted area whose alpha is the darkness at each texel's signed
    // distance from open land (EstateDecorRules.Night.Alpha), drawn as a single projected quad. Grid to screen is affine, so the
    // texture's bilinear gradient stays smooth on screen and no zone edge shows inside the night. The field is built once per
    // set of open zones; a pan or zoom only moves four vertices.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EstateNight : MaskableGraphic
    {
        static Texture2D field; static string fieldKey;
        public EstateMapModel Model;
        RectInt area; Texture2D texture;
        public override Texture mainTexture => texture != null ? texture : s_WhiteTexture;
        public void Show(RectInt painted, Texture2D night) { area = painted; texture = night; SetAllDirty(); }

        public static Texture2D Field(EstateDecorRules.Night night, RectInt area, RectInt[] open)
        {
            var key = area + "|" + string.Join(";", open) + "|" + JsonUtility.ToJson(night);
            if (field != null && fieldKey == key) return field;
            var res = Mathf.Max(1, night.texelsPerCell); int width = area.width * res, height = area.height * res;
            bool Lit(int x, int y) { foreach (var r in open) if (x >= r.xMin && x < r.xMax && y >= r.yMin && y < r.yMax) return true; return false; }
            // Only cells on the lit/dark border matter for the distance; cells past the area's edge count as dark.
            var litEdge = new List<Vector2Int>(); var darkEdge = new List<Vector2Int>();
            for (var x = area.xMin - 1; x <= area.xMax; x++)
                for (var y = area.yMin - 1; y <= area.yMax; y++)
                {
                    var lit = Lit(x, y);
                    if (lit != Lit(x - 1, y) || lit != Lit(x + 1, y) || lit != Lit(x, y - 1) || lit != Lit(x, y + 1)) (lit ? litEdge : darkEdge).Add(new Vector2Int(x, y));
                }
            var pixels = new Color32[width * height]; Color32 colour = night.Colour;
            for (var j = 0; j < height; j++)
                for (var i = 0; i < width; i++)
                {
                    float px = area.xMin + (i + .5f) / res, py = area.yMin + (j + .5f) / res;
                    var inside = Lit(Mathf.FloorToInt(px), Mathf.FloorToInt(py)); var best = float.MaxValue;
                    foreach (var c in inside ? darkEdge : litEdge)
                    {
                        float dx = Mathf.Max(0, Mathf.Max(c.x - px, px - (c.x + 1))), dy = Mathf.Max(0, Mathf.Max(c.y - py, py - (c.y + 1)));
                        best = Mathf.Min(best, dx * dx + dy * dy);
                    }
                    var d = best == float.MaxValue ? 1e4f : Mathf.Sqrt(best);
                    // Toward the painted area's edge the night closes to opaque, so the land fades into the page's night colour.
                    var edge = Mathf.Min(Mathf.Min(px - area.xMin, area.xMax - px), Mathf.Min(py - area.yMin, area.yMax - py));
                    var keep = night.areaFadeCells > 0 ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(edge / night.areaFadeCells)) : 1;
                    colour.a = (byte)Mathf.RoundToInt((1 - (1 - night.Alpha(inside ? -d : d)) * keep) * 255); pixels[j * width + i] = colour;
                }
            if (field == null || field.width != width || field.height != height)
            {
                if (field != null) { if (Application.isPlaying) Destroy(field); else DestroyImmediate(field); }
                field = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Estate night", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            }
            field.SetPixels32(pixels); field.Apply(false); fieldKey = key;
            return field;
        }

        Vector2 Local(float u, float v)
        {
            var point = Model.GridToViewport(new Vector2(u, v)); var rect = rectTransform.rect;
            return new Vector2(rect.xMin + point.x, rect.yMax - point.y);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (Model == null || texture == null) return;
            Color32 c = color;
            mesh.AddVert(Local(area.xMin, area.yMin), c, new Vector2(0, 0)); mesh.AddVert(Local(area.xMax, area.yMin), c, new Vector2(1, 0));
            mesh.AddVert(Local(area.xMax, area.yMax), c, new Vector2(1, 1)); mesh.AddVert(Local(area.xMin, area.yMax), c, new Vector2(0, 1));
            mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(0, 2, 3);
        }
    }

    public sealed class EstateMapView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        struct StandingArt { public RectTransform Rect; public RectInt Footprint; }
        struct Pinned { public RectTransform Rect; public Vector2 Grid; public float Lift; }
        struct PropArt { public RectTransform Rect; public Vector2 Grid; public float Width; public Vector2 Pivot; public float Aspect; }
        readonly List<PropArt> props = new List<PropArt>();
        public int DecorCount => props.Count;
        // A decoration sprite `width` cells wide whose pivot (normalized, y up) stands on a grid point.
        public void Prop(RectTransform rect, Vector2 grid, float width, Vector2 pivot, float aspect) =>
            props.Add(new PropArt { Rect = rect, Grid = grid, Width = width, Pivot = pivot, Aspect = aspect });
        readonly List<StandingArt> standing = new List<StandingArt>();
        readonly List<Pinned> pinned = new List<Pinned>();
        readonly List<Graphic> shapes = new List<Graphic>();
        readonly Dictionary<int, Vector2> fingers = new Dictionary<int, Vector2>();
        float pinchBase;
        public EstateMapModel Model;
        public Action<Vector2Int?> Tapped;
        public Action Zoomed, Tick, Redraw, Moved;
        float tickClock;
        bool redrawPending;
        public bool Dragging => fingers.Count > 0;

        // A redraw rebuilds the page, so one that arrives mid-drag waits for the fingers to lift.
        public void RequestRedraw()
        {
            if (Dragging) redrawPending = true; else Redraw?.Invoke();
        }

        void Update()
        {
            if (Tick == null) return;
            tickClock += Time.unscaledDeltaTime;
            if (tickClock < 1) return;
            tickClock = Mathf.Min(tickClock - 1, 1);
            Tick();
        }
        RectTransform Rect => (RectTransform)transform;

        public void Track(Graphic graphic) => shapes.Add(graphic);

        // Structural draw-batch bound (#372): uGUI draws a canvas's graphics in hierarchy order and can only share a batch between
        // neighbours with the same material and texture, so the number of material/texture switches along that order is an
        // upper bound on the batches this subtree costs (the batcher may merge more when graphics do not overlap). Counts every
        // active graphic regardless of the mask, so the number depends on the layout, not on the camera position.
        public static int MaterialBreaks(Transform root)
        {
            var breaks = 0; Material material = null; Texture texture = null; var first = true;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>())
            {
                if (!graphic.isActiveAndEnabled) continue;
                var nextMaterial = graphic.materialForRendering; var nextTexture = graphic.mainTexture;
                if (first || nextMaterial != material || nextTexture != texture) { breaks++; material = nextMaterial; texture = nextTexture; first = false; }
            }
            return breaks;
        }
        // Art stands on its footprint: bottom centre on the footprint's front corner, as wide as the footprint diamond.
        public void Standing(RectTransform rect, int u, int v, int width, int height) =>
            standing.Add(new StandingArt { Rect = rect, Footprint = new RectInt(u, v, width, height) });
        // A label centred on a grid point, raised by `lift` unscaled canvas units.
        public void Pin(Text text, Vector2 grid, float lift) =>
            Pin(text.transform.parent is RectTransform parent && parent != Rect ? parent : text.rectTransform, grid, lift);
        public void Pin(RectTransform rect, Vector2 grid, float lift) => pinned.Add(new Pinned { Rect = rect, Grid = grid, Lift = lift });

        public void Layout()
        {
            if (Model == null) return;
            var scale = Model.Scale;
            foreach (var item in standing)
            {
                var f = item.Footprint; var foot = Model.GridToViewport(new Vector2(f.x + f.width, f.y + f.height));
                var size = (f.width + f.height) * EstateMapModel.HalfWidth * scale;
                PortraitWidgets.Position(item.Rect, foot.x - size * .5f, foot.y - size, size, size);
            }
            foreach (var item in props)
            {
                var point = Model.GridToViewport(item.Grid); var w = item.Width * EstateMapModel.HalfWidth * 2 * scale; var h = w * item.Aspect;
                PortraitWidgets.Position(item.Rect, point.x - item.Pivot.x * w, point.y - (1 - item.Pivot.y) * h, w, h);
            }
            foreach (var item in pinned)
            {
                var point = Model.GridToViewport(item.Grid); var size = item.Rect.sizeDelta;
                PortraitWidgets.Position(item.Rect, point.x - size.x * .5f, point.y - item.Lift * scale - size.y * .5f, size.x, size.y);
            }
            foreach (var graphic in shapes) graphic.SetVerticesDirty();
        }

        public void Step(int direction)
        {
            if (Model != null && Model.StepZoom(direction, Model.VisibleCentre))
            { Layout(); Zoomed?.Invoke(); Moved?.Invoke(); }
        }

        void OnRectTransformDimensionsChange()
        {
            if (Model == null || !isActiveAndEnabled) return;
            Model.SetFrame(Rect.rect.size, Model.TopInset, Model.BottomInset); Layout();
        }

        Vector2 Local(PointerEventData data, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screen, data.pressEventCamera ?? data.enterEventCamera, out var local);
            var rect = Rect.rect; return new Vector2(local.x - rect.xMin, rect.yMax - local.y);
        }

        // The first two tracked fingers, read with the dictionary's struct enumerator so a pinch allocates nothing per frame.
        bool TwoFingers(out Vector2 first, out Vector2 second)
        {
            first = second = default; var count = 0;
            foreach (var pair in fingers)
            {
                if (count == 0) first = pair.Value; else { second = pair.Value; return true; }
                count++;
            }
            return false;
        }

        float Spread() => TwoFingers(out var first, out var second) ? Vector2.Distance(first, second) : 0;

        public void OnBeginDrag(PointerEventData data)
        {
            fingers[data.pointerId] = data.position;
            pinchBase = fingers.Count >= 2 ? Spread() : 0;
        }

        public void OnDrag(PointerEventData data)
        {
            if (Model == null || !fingers.TryGetValue(data.pointerId, out var before)) return;
            fingers[data.pointerId] = data.position;
            if (fingers.Count == 1) { Model.Pan(Local(data, data.position) - Local(data, before)); Layout(); return; }
            if (pinchBase <= 0) return;
            if (!TwoFingers(out var first, out var second)) return;
            var spread = Vector2.Distance(first, second); var direction = EstateMapModel.PinchDirection(spread / pinchBase);
            if (direction == 0) return;
            pinchBase = spread;
            if (Model.StepZoom(direction, Local(data, (first + second) * .5f))) { Layout(); Zoomed?.Invoke(); Moved?.Invoke(); }
        }

        public void OnEndDrag(PointerEventData data)
        {
            fingers.Remove(data.pointerId);
            pinchBase = fingers.Count >= 2 ? Spread() : 0;
            if (fingers.Count == 0) Moved?.Invoke();
            if (fingers.Count == 0 && redrawPending) { redrawPending = false; Redraw?.Invoke(); }
        }

        public void OnScroll(PointerEventData data)
        {
            if (Model == null || data.scrollDelta.y == 0) return;
            if (Model.StepZoom(data.scrollDelta.y > 0 ? 1 : -1, Local(data, data.position))) { Layout(); Zoomed?.Invoke(); Moved?.Invoke(); }
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data.dragging || Model == null) return;
            Tapped?.Invoke(Model.TryCell(Local(data, data.position), out var cell) ? cell : (Vector2Int?)null);
        }
    }
}
