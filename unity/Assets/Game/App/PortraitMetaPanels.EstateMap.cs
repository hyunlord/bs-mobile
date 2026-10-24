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
        public const string EstateMapGlyphs = "영지 시제품 저장 안 함 장원 사본 전체 동네 건물 우리 집 망루 우물 경작지 벌목장 철공소 폐허 아직 짓지 않음 완성 재고 전문화 공사 중 이동 닫기 확정 취소 옮길 칸을 누르세요 놓을 수 있습니다 지금 자리입니다 열린 구역 밖입니다 공사 중에는 옮길 수 없습니다 와 과 겹칩니다 옮겼습니다 저장하지 않는 시제품입니다 닫힌 출정 목표 생산 재고를 처음 받기 터 2곳 하나 옮기기 달성 보상 모든 받았습니다 가신 도전 상점 보석 Lv / + − ▸ ▾ 0123456789";
        static readonly Color OpenGround = new Color32(104, 124, 70, 255);
        static readonly Color ClosedGround = new Color32(40, 37, 33, 255);
        static readonly Color Fog = new Color32(24, 22, 20, 255);
        static readonly Color GridLine = new Color32(36, 48, 26, 110);
        static readonly Color BlockTop = new Color32(176, 154, 112, 255);
        static readonly Color BlockLeft = new Color32(132, 112, 80, 255);
        static readonly Color BlockRight = new Color32(104, 88, 62, 255);
        static readonly Color RuinTint = new Color32(128, 128, 128, 255);
        static readonly Color SelectedFoot = new Color32(243, 213, 140, 150);
        static readonly Color ValidGhost = new Color32(96, 190, 104, 150);
        static readonly Color InvalidGhost = new Color32(205, 64, 48, 150);
        static readonly Color BarInk = new Color32(20, 16, 12, 205);
        const float BarTop = 6, BarHeight = 40, GoalTop = 50, GoalHeight = 44, BottomBar = 56, BottomGap = 8, PanelHeight = 150;
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
            var top = GoalTop + GoalHeight;
            var bottom = BottomBar + BottomGap * 2 + (screen.Selected != null ? PanelHeight + BottomGap : 0);
            screen.Map.SetFrame(viewport.rect.size, top, bottom);
            if (!screen.Map.Framed) screen.Map.CentreOn(screen.HomeCentre());
            else if (screen.Selected != null && !screen.Map.InView(screen.Centre(screen.Selected.Value))) screen.Map.CentreOn(screen.Centre(screen.Selected.Value));

            var ground = Shapes(viewport, "EstateMap.Ground", view);
            foreach (var zone in screen.Config.Zones) ground.Footprint(zone.X, zone.Y, zone.Width, zone.Height, screen.Open(zone) ? OpenGround : ClosedGround);
            for (var u = 0; u <= screen.Map.Columns; u++) ground.Line(new Vector2(u, 0), new Vector2(u, screen.Map.Rows), GridLine);
            for (var v = 0; v <= screen.Map.Rows; v++) ground.Line(new Vector2(0, v), new Vector2(screen.Map.Columns, v), GridLine);
            foreach (var zone in screen.Config.Zones.Where(z => !screen.Open(z)))
                view.Pin(Caption(ui, viewport, "닫힌 구역", 0, 0, 120, 24, 13, false, TextAnchor.MiddleCenter), new Vector2(zone.X + zone.Width * .5f, zone.Y + zone.Height * .5f), 0);
            if (screen.Selected != null)
            {
                var selected = screen.Building(screen.Selected.Value); var kind = screen.Kind(selected.Kind);
                ground.Footprint(selected.X, selected.Y, kind.Width, kind.Height, SelectedFoot);
            }

            // Buildings in draw order: front corner screen y, then instance order (#368).
            foreach (var building in screen.State.Estate.Buildings.OrderBy(b => SortY(screen, b)).ThenBy(b => b.Id))
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
            if (screen.Moving && screen.Selected != null)
            {
                var kind = screen.Kind(screen.Building(screen.Selected.Value).Kind);
                Shapes(viewport, "EstateMap.Preview", view).Footprint(screen.Preview.x, screen.Preview.y, kind.Width, kind.Height, screen.PreviewValid ? ValidGhost : InvalidGhost);
            }
            view.Tapped = cell =>
            {
                if (screen.Moving) { if (cell != null) { screen.Place(cell.Value); actions.Redraw(); } return; }
                var hit = cell == null ? null : screen.BuildingAt(cell.Value);
                if (hit != screen.Selected) { screen.Select(hit); actions.Redraw(); }
            };
            view.Layout();

            ResourceStrip(ui, root, screen.Catalog, screen.State, actions.Navigate, width);
            GoalLine(ui, root, screen, actions, width);
            var zoomNames = new[] { "전체", "동네", "건물" };
            var controlsY = height - bottom - 104;
            var chip = ColorPlate(root, "EstateMap.ZoomLevel", BarInk, 10, controlsY + 60, 56, 28);
            var zoomCaption = Caption(ui, chip, zoomNames[screen.Map.Zoom], 0, 0, 56, 28, 13, false, TextAnchor.MiddleCenter);
            view.Zoomed = () => zoomCaption.text = zoomNames[screen.Map.Zoom];
            PortraitRunPanels.RoundIcon(ui, root, "EstateMap.ZoomIn", "+", () => view.Step(1), width - 54, controlsY);
            PortraitRunPanels.RoundIcon(ui, root, "EstateMap.ZoomOut", "−", () => view.Step(-1), width - 54, controlsY + 52);
            if (screen.Selected != null) SelectionPanel(ui, root, screen, actions, width, height - BottomBar - BottomGap * 2 - PanelHeight);
            BottomBarWithTabs(ui, root, account, actions, width, height);
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
                Caption(ui, line, "영지 시제품 · 저장 안 함" + (screen.FromSave ? "" : " · 장원 사본"), 10, 2, lineWidth - 34, 20, 12).color = new Color32(243, 213, 140, 255);
                Caption(ui, line, screen.Notice.Length > 0 ? screen.Notice : screen.GoalText(), 10, 21, lineWidth - 34, 20, 13);
                Caption(ui, line, "▾", lineWidth - 24, 0, 20, GoalHeight, 14);
            }
            Hit(line, "EstateMap.Goal", () => { screen.ToggleGoal(); actions.Redraw(); }, 0, 0, lineWidth, GoalHeight);
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

        static void SelectionPanel(UiShell ui, RectTransform root, EstateMapScreen screen, EstateMapActions actions, float width, float y)
        {
            var building = screen.Building(screen.Selected.Value); var kind = screen.Kind(building.Kind);
            var panel = ColorPlate(root, "EstateMap.Selection", new Color32(52, 41, 27, 245), 8, y, width - 16, PanelHeight);
            panel.GetComponent<Image>().raycastTarget = true; // taps on the panel never reach the map below it
            ColorPlate(panel, "Gold rule", new Color32(139, 106, 51, 255), 0, 0, width - 16, 2);
            Caption(ui, panel, screen.Name(building.Kind) + " Lv " + building.Level + " / " + kind.MaxLevel(), 14, 8, width - 44, 28, 18);
            Caption(ui, panel, screen.Status(building), 14, 36, width - 44, 22, 13);
            var half = (width - 54) / 2;
            if (screen.Moving)
            {
                var reason = Caption(ui, panel, "옮길 칸을 누르세요 · " + screen.ReasonText(screen.PreviewReason), 14, 60, width - 44, 30, 13);
                reason.color = screen.PreviewValid ? new Color32(168, 222, 150, 255) : new Color32(240, 140, 120, 255);
                ActionButton(ui, panel, "EstateMap.Confirm", "확정", () => { screen.Confirm(); actions.Redraw(); }, 14, 94, half, screen.PreviewValid, 48, "ui.button.primary");
                ActionButton(ui, panel, "EstateMap.Cancel", "취소", () => { screen.Cancel(); actions.Redraw(); }, 24 + half, 94, half, true, 48);
                return;
            }
            var busy = screen.UnderConstruction(building.Id);
            if (busy) Caption(ui, panel, screen.ReasonText("under-construction"), 14, 60, width - 44, 30, 13);
            ActionButton(ui, panel, "EstateMap.Move", "이동", () => { screen.BeginMove(); actions.Redraw(); }, 14, 94, half, !busy, 48, "ui.button.primary");
            ActionButton(ui, panel, "EstateMap.Close", "닫기", () => { screen.Select(null); actions.Redraw(); }, 24 + half, 94, half, true, 48);
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
        enum Kind { Footprint, Line, Block }
        struct Shape { public Kind Kind; public Vector2 A, B; public float Rise; public Color Top, Left, Right; }
        readonly List<Shape> shapes = new List<Shape>();
        public EstateMapModel Model;
        public int Count => shapes.Count;

        public void Footprint(int u, int v, int width, int height, Color color) =>
            shapes.Add(new Shape { Kind = Kind.Footprint, A = new Vector2(u, v), B = new Vector2(u + width, v + height), Top = color });
        public void Line(Vector2 from, Vector2 to, Color color) => shapes.Add(new Shape { Kind = Kind.Line, A = from, B = to, Top = color });
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
                        var p = Local(a); var q = Local(b); var normal = new Vector2(q.y - p.y, p.x - q.x).normalized * .5f;
                        Quad(mesh, p + normal, q + normal, q - normal, p - normal, shape.Top); break;
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
        struct Standing { public RectTransform Rect; public RectInt Footprint; }
        struct Pinned { public RectTransform Rect; public Vector2 Grid; public float Lift; }
        readonly List<Standing> standing = new List<Standing>();
        readonly List<Pinned> pinned = new List<Pinned>();
        readonly List<EstateIsoShapes> shapes = new List<EstateIsoShapes>();
        readonly Dictionary<int, Vector2> fingers = new Dictionary<int, Vector2>();
        float pinchBase;
        public EstateMapModel Model;
        public Action<Vector2Int?> Tapped;
        public Action Zoomed;
        RectTransform Rect => (RectTransform)transform;

        public void Track(EstateIsoShapes graphic) => shapes.Add(graphic);
        // Art stands on its footprint: bottom centre on the footprint's front corner, as wide as the footprint diamond.
        public void Standing(RectTransform rect, int u, int v, int width, int height) =>
            standing.Add(new Standing { Rect = rect, Footprint = new RectInt(u, v, width, height) });
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
