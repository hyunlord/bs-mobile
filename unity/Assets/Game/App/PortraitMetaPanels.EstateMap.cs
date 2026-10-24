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
        public Action<PortraitMetaPage> Navigate;
    }

    // Estate map prototype page (#366, development option only). Since #368 the map is isometric and fills the screen like the
    // approved key art (m3-estate-keyarts-r2 A-30day-ui): a thin resource bar, a collapsible goal line, compact tabs and a
    // small departure button over the map. Temporary art: the four manor sprites, raised colour blocks for the other kinds.
    public static partial class PortraitMetaPanels
    {
        public const string EstateMapGlyphs = "영지 시제품 저장 안 함 계정 사본 새 전체 동네 건물 우리 집 망루 우물 경작지 벌목장 철공소 폐허 아직 짓지 않음 완성 재고 전문화 공사 중 이동 닫기 확정 취소 옮길 칸을 누르세요 놓을 수 있습니다 지금 자리입니다 열린 구역 밖입니다 공사 중에는 옮길 수 없습니다 와 과 겹칩니다 탐사로 열 땅 탐사전 여는 땅입니다 이 시제품에서는 열리지 않음 M3c 옮겼습니다 저장하지 않는 시제품입니다 닫힌 출정 목표 생산 재고를 처음 받기 터 2곳 하나 옮기기 달성 보상 모든 받았습니다 가신 도전 상점 보석 짓기 올리기 예약 다음 최고 레벨 공사 시작 진행 없음 비어 있음 자동 착공 켬 끔 대기열 받음 과제 재료가 모자랍니다 첫 경로 몫은 남겨 둡니다 먼저 부모 시설을 지어야 합니다 가득 찼습니다 하나를 골라 성장 방향을 정해요 으로 정하기 고르기 선택 지금 변경 다시 바꿀 수 있음 출정 중에는 고정 구역 개방 준비 원정 보급 창고 장기 검술 공방 카드 연구실 도시 기록소 호위 병영 건설 지원 시간 줄임 철재 생산 집중 판 효과는 M3b에서 연결 분 Lv / + − × ▸ ▾ % 장식 0123456789:";
        static readonly Color OpenGround = new Color32(104, 124, 70, 255);
        static readonly Color ClosedGround = new Color32(40, 37, 33, 255);
        // Closed land: the M2 darkness grammar (#374) — blue-grey cover with a soft edge toward open land and a faint warm rim
        // (GameVisualTokens.EstateRim in the M2 estate View) on the boundary.
        static readonly Color Fog = new Color32(38, 50, 60, 255);
        static readonly Color FogCover = new Color32(38, 50, 60, 236);
        static readonly Color Rim = new Color32(0xE5, 0xC9, 0x7F, 120);
        const float FogFeatherCells = .9f;
        static readonly Color GridLine = new Color32(36, 48, 26, 110);
        static readonly Color BlockTop = new Color32(176, 154, 112, 255);
        static readonly Color BlockLeft = new Color32(132, 112, 80, 255);
        static readonly Color BlockRight = new Color32(104, 88, 62, 255);
        static readonly Color RuinTint = new Color32(128, 128, 128, 255);
        static readonly Color SelectedFoot = new Color32(243, 213, 140, 150);
        static readonly Color ValidGhost = new Color32(96, 190, 104, 150);
        static readonly Color InvalidGhost = new Color32(205, 64, 48, 150);
        static readonly Color BarInk = new Color32(20, 16, 12, 205);
        static readonly Color RoadSand = new Color32(176, 152, 104, 255);
        const float BarTop = 6, BarHeight = 40, GoalTop = 50, GoalHeight = 44, BottomBar = 56, BottomGap = 8, WorkHeight = 44;
        static readonly Color GoodInk = new Color32(168, 222, 150, 255), BadInk = new Color32(240, 140, 120, 255), Gold = new Color32(243, 213, 140, 255);
        // Block height of a temporary plate building, in unscaled canvas units.
        const float BlockRise = 22;

        public static void EstateMap(UiShell ui, EstateMapScreen screen, MetaState account, EstateMapActions actions)
        {
            var root = Page(ui, "EstateMap", ""); var width = Width(ui); var height = Height(ui);
            root.GetComponent<Image>().color = Fog;

            // The map fills the page; bars and panels sit over it and become insets so every row can still be dragged into view.
            var viewport = PortraitWidgets.Place("EstateMap.Viewport", root, 0, 0, width, height);
            viewport.gameObject.AddComponent<Image>().color = Color.clear; viewport.gameObject.AddComponent<RectMask2D>();
            var view = viewport.gameObject.AddComponent<EstateMapView>(); view.Model = screen.Map;
            var top = GoalTop + GoalHeight; var panel = PanelHeightFor(screen);
            var bottom = BottomBar + BottomGap * 3 + WorkHeight + (panel > 0 ? panel + BottomGap : 0);
            screen.Map.SetFrame(viewport.rect.size, top, bottom);
            if (!screen.Map.Framed) screen.Map.CentreOn(screen.HomeCentre());
            // A newly selected building hidden behind the panel is brought into view; while moving, the camera stays where the
            // player put it so the destination preview remains visible.
            else if (screen.Selected != null && !screen.Moving && !screen.Map.InView(screen.Centre(screen.Selected.Value))) screen.Map.CentreOn(screen.Centre(screen.Selected.Value));

            var ground = Shapes(viewport, "EstateMap.Ground", view);
            foreach (var zone in screen.Config.Zones) ground.Footprint(zone.X, zone.Y, zone.Width, zone.Height, screen.Open(zone) ? OpenGround : ClosedGround);
            // Grid lines only on open land.
            foreach (var zone in screen.Config.Zones.Where(screen.Open))
            {
                for (var u = zone.X; u <= zone.X + zone.Width; u++) ground.Line(new Vector2(u, zone.Y), new Vector2(u, zone.Y + zone.Height), GridLine);
                for (var v = zone.Y; v <= zone.Y + zone.Height; v++) ground.Line(new Vector2(zone.X, v), new Vector2(zone.X + zone.Width, v), GridLine);
            }
            // Darkness over closed zones: above the ground, below decoration, buildings, previews and the bars.
            var fog = Shapes(viewport, "EstateMap.Fog", view);
            foreach (var zone in screen.Config.Zones.Where(z => !screen.Open(z)))
            {
                bool Faces(int du, int dv) => screen.Config.Zones.Any(o => screen.Open(o) && SharesEdge(zone, o, du, dv));
                fog.Fog(zone.X, zone.Y, zone.Width, zone.Height, FogCover, FogFeatherCells, Faces(-1, 0), Faces(0, -1), Faces(1, 0), Faces(0, 1));
                foreach (var open in screen.Config.Zones.Where(screen.Open))
                    if (SharedEdge(zone, open, out var from, out var to)) fog.Line(from, to, Rim, 2);
                view.Pin(Caption(ui, viewport, screen.ZoneLabel(zone), 0, 0, 160, 24, 13, false, TextAnchor.MiddleCenter), new Vector2(zone.X + zone.Width * .5f, zone.Y + zone.Height * .5f), 0);
            }
            // Decoration (#372): road cells join the ground mesh, flat props lie on the ground layer, standing props sort with
            // the buildings by their foot.
            var decor = screen.DecorEnabled ? screen.Decor() : new List<EstateDecorItem>();
            foreach (var item in decor.Where(i => i.Source == EstateDecorSource.Road))
                ground.Footprint(Mathf.FloorToInt(item.Grid.x), Mathf.FloorToInt(item.Grid.y), 1, 1, RoadSand);
            if (screen.Selected != null)
            {
                var selected = screen.Building(screen.Selected.Value); var kind = screen.Kind(selected.Kind);
                ground.Footprint(selected.X, selected.Y, kind.Width, kind.Height, SelectedFoot);
            }
            foreach (var item in decor.Where(i => i.Flat && i.Role.Length > 0)) DecorProp(viewport, view, item);

            void DrawBuilding(MetaEstateBuilding building)
            {
                var kind = screen.Kind(building.Kind); var art = EstateMapScreen.Art(building.Kind);
                var dimmed = screen.Moving && screen.Selected == building.Id;
                var alpha = dimmed ? .45f : 1;
                if (art == null)
                {
                    var block = Shapes(viewport, "EstateMap.Building:" + building.Id, view);
                    var tint = building.Level == 0 ? RuinTint : Color.white;
                    block.Block(building.X, building.Y, kind.Width, kind.Height, BlockRise, Mul(BlockTop, tint, alpha), Mul(BlockLeft, tint, alpha), Mul(BlockRight, tint, alpha));
                }
                else
                {
                    var rect = PortraitWidgets.Place("EstateMap.Building:" + building.Id, viewport, 0, 0, 0, 0);
                    var image = PortraitWidgets.Art(ui, rect, art, 0, 0, 0, 0); UiShell.Stretch(image.rectTransform);
                    image.preserveAspect = true; image.raycastTarget = false;
                    image.color = Mul(building.Level == 0 ? RuinTint : Color.white, Color.white, alpha);
                    view.Standing(rect, building.X, building.Y, kind.Width, kind.Height);
                }
                // Names stay hidden until the building is selected (#368).
                if (screen.Selected == building.Id)
                    view.Pin(NameTag(ui, viewport, screen.Name(building.Kind) + " Lv " + building.Level), new Vector2(building.X + kind.Width * .5f, building.Y + kind.Height * .5f), art == null ? BlockRise + 34 : (kind.Width + kind.Height) * EstateMapModel.HalfWidth * .9f);
            }

            // Buildings and standing props in draw order: foot screen y (a footprint's front corner), buildings before props on
            // a tie, then instance order (#368).
            var drawables = screen.State.Estate.Buildings.Select(b => (Y: SortY(screen, b), Order: 0, Id: b.Id, Building: b, Prop: default(EstateDecorItem)))
                .Concat(decor.Where(i => !i.Flat && i.Role.Length > 0).Select((p, i) => (Y: EstateMapModel.Project(p.Grid).y, Order: 1, Id: i, Building: (MetaEstateBuilding)null, Prop: p)))
                .OrderBy(d => d.Y).ThenBy(d => d.Order).ThenBy(d => d.Id);
            foreach (var drawable in drawables)
            {
                if (drawable.Building != null) DrawBuilding(drawable.Building);
                else DecorProp(viewport, view, drawable.Prop);
            }
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
            var zoomNames = new[] { "전체", "동네", "건물" };
            var controlsY = height - bottom - 104;
            var decorToggle = ActionButton(ui, root, "EstateMap.Decor", "장식 " + (screen.DecorEnabled ? "켬" : "끔"), () => { screen.ToggleDecor(); actions.Redraw(); }, 10, controlsY + 4, 84, true, 44);
            Compact(decorToggle.transform);
            var chip = ColorPlate(root, "EstateMap.ZoomLevel", BarInk, 10, controlsY + 60, 56, 28);
            var zoomCaption = Caption(ui, chip, zoomNames[screen.Map.Zoom], 0, 0, 56, 28, 13, false, TextAnchor.MiddleCenter);
            view.Zoomed = () => zoomCaption.text = zoomNames[screen.Map.Zoom];
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

        // Places one decoration sprite; a role missing from the art catalog is skipped (counted by the screen for the PR report).
        static void DecorProp(RectTransform viewport, EstateMapView view, EstateDecorItem item)
        {
            ArtVisual visual;
            try { visual = ArtCatalog.Load().ResolveRole(item.Role); }
            catch (InvalidOperationException) { view.MissingDecor++; return; }
            var sprite = visual.Sprite; if (sprite == null) { view.MissingDecor++; return; }
            var rect = UiShell.Rect("EstateMap.Decor:" + item.Source, viewport);
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite; image.raycastTarget = false;
            var size = sprite.rect.size;
            view.Prop(rect, item.Grid, item.Width, new Vector2(sprite.pivot.x / size.x, sprite.pivot.y / size.y), size.y / size.x);
        }

        // True when `other` lies directly on the (du, dv) side of `zone` and the two share part of that edge.
        static bool SharesEdge(AccountEstateZone zone, AccountEstateZone other, int du, int dv) =>
            du != 0 ? (du < 0 ? other.X + other.Width == zone.X : zone.X + zone.Width == other.X) && other.Y < zone.Y + zone.Height && zone.Y < other.Y + other.Height
                    : (dv < 0 ? other.Y + other.Height == zone.Y : zone.Y + zone.Height == other.Y) && other.X < zone.X + zone.Width && zone.X < other.X + other.Width;

        static bool SharedEdge(AccountEstateZone a, AccountEstateZone b, out Vector2 from, out Vector2 to)
        {
            from = to = default;
            if (a.X + a.Width == b.X || b.X + b.Width == a.X)
            {
                var x = a.X + a.Width == b.X ? b.X : a.X; var y0 = Mathf.Max(a.Y, b.Y); var y1 = Mathf.Min(a.Y + a.Height, b.Y + b.Height);
                if (y1 <= y0) return false; from = new Vector2(x, y0); to = new Vector2(x, y1); return true;
            }
            if (a.Y + a.Height == b.Y || b.Y + b.Height == a.Y)
            {
                var y = a.Y + a.Height == b.Y ? b.Y : a.Y; var x0 = Mathf.Max(a.X, b.X); var x1 = Mathf.Min(a.X + a.Width, b.X + b.Width);
                if (x1 <= x0) return false; from = new Vector2(x0, y); to = new Vector2(x1, y); return true;
            }
            return false;
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
            Caption(ui, panel, screen.ZoneLabel(zone), 14, 8, width - 44, 28, 18);
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

    // Grid-anchored isometric shapes (ground diamonds, grid lines, raised blocks, footprints) drawn in one mesh in viewport space.
    // The model's projection is re-read on every rebuild, so a pan or zoom only marks the mesh dirty.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EstateIsoShapes : MaskableGraphic
    {
        enum Kind { Footprint, Line, Block, Fog }
        struct Shape { public Kind Kind; public Vector2 A, B; public float Rise; public Color Top, Left, Right; public int Feather; }
        readonly List<Shape> shapes = new List<Shape>();
        public EstateMapModel Model;
        public int Count => shapes.Count;

        public void Footprint(int u, int v, int width, int height, Color color) =>
            shapes.Add(new Shape { Kind = Kind.Footprint, A = new Vector2(u, v), B = new Vector2(u + width, v + height), Top = color });
        public void Line(Vector2 from, Vector2 to, Color color, float thickness = 1) => shapes.Add(new Shape { Kind = Kind.Line, A = from, B = to, Top = color, Rise = thickness });
        // A cover over a grid rectangle whose chosen sides (u-min, v-min, u-max, v-max) fade to transparent over `feather` cells.
        public void Fog(int u, int v, int width, int height, Color color, float feather, bool uMin, bool vMin, bool uMax, bool vMax) =>
            shapes.Add(new Shape { Kind = Kind.Fog, A = new Vector2(u, v), B = new Vector2(u + width, v + height), Top = color, Rise = feather,
                Feather = (uMin ? 1 : 0) | (vMin ? 2 : 0) | (uMax ? 4 : 0) | (vMax ? 8 : 0) });
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
                    case Kind.Fog:
                    {
                        // A 3×3 patch: the outer vertex row/column of a feathered side is transparent.
                        var f = shape.Rise;
                        var us = new[] { a.x, a.x + ((shape.Feather & 1) != 0 ? f : 0), b.x - ((shape.Feather & 4) != 0 ? f : 0), b.x };
                        var vs = new[] { a.y, a.y + ((shape.Feather & 2) != 0 ? f : 0), b.y - ((shape.Feather & 8) != 0 ? f : 0), b.y };
                        Color32 At(int i, int j)
                        {
                            var clear = i == 0 && (shape.Feather & 1) != 0 || i == 3 && (shape.Feather & 4) != 0 || j == 0 && (shape.Feather & 2) != 0 || j == 3 && (shape.Feather & 8) != 0;
                            var c = shape.Top; if (clear) c.a = 0; return c;
                        }
                        for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++)
                        {
                            var start = mesh.currentVertCount;
                            mesh.AddVert(Local(new Vector2(us[i], vs[j])), At(i, j), Vector2.zero); mesh.AddVert(Local(new Vector2(us[i + 1], vs[j])), At(i + 1, j), Vector2.zero);
                            mesh.AddVert(Local(new Vector2(us[i + 1], vs[j + 1])), At(i + 1, j + 1), Vector2.zero); mesh.AddVert(Local(new Vector2(us[i], vs[j + 1])), At(i, j + 1), Vector2.zero);
                            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
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
    public sealed class EstateMapView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        struct StandingArt { public RectTransform Rect; public RectInt Footprint; }
        struct Pinned { public RectTransform Rect; public Vector2 Grid; public float Lift; }
        struct PropArt { public RectTransform Rect; public Vector2 Grid; public float Width; public Vector2 Pivot; public float Aspect; }
        readonly List<PropArt> props = new List<PropArt>();
        public int MissingDecor;
        public int DecorCount => props.Count;
        // A decoration sprite `width` cells wide whose pivot (normalized, y up) stands on a grid point.
        public void Prop(RectTransform rect, Vector2 grid, float width, Vector2 pivot, float aspect) =>
            props.Add(new PropArt { Rect = rect, Grid = grid, Width = width, Pivot = pivot, Aspect = aspect });
        readonly List<StandingArt> standing = new List<StandingArt>();
        readonly List<Pinned> pinned = new List<Pinned>();
        readonly List<EstateIsoShapes> shapes = new List<EstateIsoShapes>();
        readonly Dictionary<int, Vector2> fingers = new Dictionary<int, Vector2>();
        float pinchBase;
        public EstateMapModel Model;
        public Action<Vector2Int?> Tapped;
        public Action Zoomed, Tick, Redraw;
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

        public void Track(EstateIsoShapes graphic) => shapes.Add(graphic);

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
        public void Pin(Text text, Vector2 grid, float lift)
        {
            var rect = text.transform.parent is RectTransform parent && parent != Rect ? parent : text.rectTransform;
            pinned.Add(new Pinned { Rect = rect, Grid = grid, Lift = lift });
        }

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
            { Layout(); Zoomed?.Invoke(); }
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
            if (Model.StepZoom(direction, Local(data, (first + second) * .5f))) { Layout(); Zoomed?.Invoke(); }
        }

        public void OnEndDrag(PointerEventData data)
        {
            fingers.Remove(data.pointerId);
            pinchBase = fingers.Count >= 2 ? Spread() : 0;
            if (fingers.Count == 0 && redrawPending) { redrawPending = false; Redraw?.Invoke(); }
        }

        public void OnScroll(PointerEventData data)
        {
            if (Model == null || data.scrollDelta.y == 0) return;
            if (Model.StepZoom(data.scrollDelta.y > 0 ? 1 : -1, Local(data, data.position))) { Layout(); Zoomed?.Invoke(); }
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data.dragging || Model == null) return;
            Tapped?.Invoke(Model.TryCell(Local(data, data.position), out var cell) ? cell : (Vector2Int?)null);
        }
    }
}
