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

    // M3a-2 estate map prototype page (#366, development option only). Temporary art: the four existing manor sprites,
    // labelled colour plates for the other kinds and solid-colour zones, until Astra's estate art is approved.
    public static partial class PortraitMetaPanels
    {
        public const string EstateMapGlyphs = "영지 시제품 저장 안 함 장원 사본 전체 동네 건물 우리 집 망루 우물 경작지 벌목장 철공소 폐허 아직 짓지 않음 완성 재고 전문화 공사 중 이동 닫기 확정 취소 옮길 칸을 누르세요 놓을 수 있습니다 지금 자리입니다 열린 구역 밖입니다 공사 중에는 옮길 수 없습니다 와 과 겹칩니다 옮겼습니다 저장하지 않는 시제품입니다 닫힌 출정 Lv / + − 0123456789";
        static readonly Color OpenGround = new Color32(92, 118, 62, 255);
        static readonly Color ClosedGround = new Color32(28, 25, 22, 255);
        static readonly Color GridLine = new Color32(30, 40, 22, 90);
        static readonly Color SitePlate = new Color32(158, 137, 98, 255);
        static readonly Color RuinTint = new Color32(130, 130, 130, 255);
        static readonly Color SelectedRim = new Color32(243, 213, 140, 255);
        static readonly Color ValidGhost = new Color32(96, 190, 104, 150);
        static readonly Color InvalidGhost = new Color32(205, 64, 48, 150);

        public static void EstateMap(UiShell ui, EstateMapScreen screen, MetaState account, EstateMapActions actions)
        {
            var root = Page(ui, "EstateMap", ""); var width = Width(ui); var inner = width - 20;
            ResourceBar(ui, root, screen.Catalog, screen.State, actions.Navigate);
            var banner = ColorPlate(root, "EstateMap.Banner", new Color32(52, 41, 27, 255), 10, 100, width - 78, 44);
            var notice = screen.Notice.Length > 0 ? screen.Notice : "영지 시제품 · 저장 안 함" + (screen.FromSave ? "" : " · 장원 사본");
            Caption(ui, banner, notice, 12, 0, width - 102, 44, 13);
            PortraitRunPanels.RoundIcon(ui, root, "Estate.Settings", "settings", actions.Settings, width - 60, 100);

            const float top = 152; var bottom = Height(ui) - 116 - 80;
            var viewport = PortraitWidgets.Place("EstateMap.Viewport", root, 10, top, inner, bottom - top);
            viewport.gameObject.AddComponent<Image>().color = ClosedGround; viewport.gameObject.AddComponent<RectMask2D>();
            var view = viewport.gameObject.AddComponent<EstateMapView>(); view.Model = screen.Map;
            if (!screen.Map.Framed) { screen.Map.SetViewport(viewport.rect.size); screen.Map.CentreOn(screen.HomeCentre()); }

            foreach (var zone in screen.Config.Zones)
            {
                var plate = view.Add(ColorPlate(viewport, "Zone:" + zone.Id, screen.Open(zone) ? OpenGround : ClosedGround, 0, 0, 0, 0), zone.X, zone.Y, zone.Width, zone.Height);
                if (!screen.Open(zone)) Stretched(ui, plate, "닫힌 구역", 13, new Color32(150, 140, 120, 255));
            }
            for (var x = 1; x < screen.Map.Columns; x++) view.Add(ColorPlate(viewport, "Grid column", GridLine, 0, 0, 0, 0), x, 0, 0, screen.Map.Rows);
            for (var y = 1; y < screen.Map.Rows; y++) view.Add(ColorPlate(viewport, "Grid row", GridLine, 0, 0, 0, 0), 0, y, screen.Map.Columns, 0);

            foreach (var building in screen.State.Estate.Buildings)
            {
                var kind = screen.Kind(building.Kind); var id = building.Id; var selected = screen.Selected == id;
                var rect = PortraitWidgets.Place("EstateMap.Building:" + id, viewport, 0, 0, 0, 0); view.Add(rect, building.X, building.Y, kind.Width, kind.Height);
                if (selected) Fill(rect, "Selected rim", SelectedRim, -3);
                var art = EstateMapScreen.Art(building.Kind);
                if (art == null) Fill(rect, "Site plate", building.Level == 0 ? RuinTint : SitePlate, 2);
                else
                {
                    Fill(rect, "Site ground", new Color32(120, 104, 74, building.Level == 0 ? (byte)120 : (byte)200), 2);
                    var image = PortraitWidgets.Art(ui, rect, art, 0, 0, 0, 0); UiShell.Stretch(image.rectTransform);
                    image.preserveAspect = true; image.raycastTarget = false; if (building.Level == 0) image.color = RuinTint;
                }
                if (screen.Moving && selected) rect.GetComponentsInChildren<Graphic>().ToList().ForEach(g => g.color = g.color * new Color(1, 1, 1, .45f));
                view.Label(Stretched(ui, rect, screen.Name(building.Kind) + " " + building.Level, 12, LightInk, true));
                var hit = rect.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = !screen.Moving;
                var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = hit; button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { if (screen.Moving) return; screen.Select(id); actions.Redraw(); });
            }
            if (screen.Moving && screen.Selected != null)
            {
                var kind = screen.Kind(screen.Building(screen.Selected.Value).Kind);
                view.Add(ColorPlate(viewport, "EstateMap.Preview", screen.PreviewValid ? ValidGhost : InvalidGhost, 0, 0, 0, 0),
                    screen.Preview.x, screen.Preview.y, kind.Width, kind.Height);
            }
            view.Tapped = point =>
            {
                if (screen.Moving) { if (screen.Map.TryCell(point, out var cell)) { screen.Place(cell); actions.Redraw(); } return; }
                if (screen.Selected != null) { screen.Select(null); actions.Redraw(); }
            };
            view.Layout();

            var zoomNames = new[] { "전체", "동네", "건물" };
            var chip = ColorPlate(root, "EstateMap.ZoomLevel", new Color32(52, 41, 27, 220), 18, top + 8, 64, 28);
            var zoomCaption = Caption(ui, chip, zoomNames[screen.Map.Zoom], 0, 0, 64, 28, 13, false, TextAnchor.MiddleCenter);
            view.Zoomed = () => zoomCaption.text = zoomNames[screen.Map.Zoom];
            // Side by side in the strip above the map so they do not cover the north-east buildings at the default zoom.
            PortraitRunPanels.RoundIcon(ui, root, "EstateMap.ZoomIn", "+", () => view.Step(1), width - 64, top + 8);
            PortraitRunPanels.RoundIcon(ui, root, "EstateMap.ZoomOut", "−", () => view.Step(-1), width - 116, top + 8);

            if (screen.Selected != null) SelectionPanel(ui, root, screen, actions, width, bottom);
            var depart = ActionButton(ui, root, "Estate.Depart", "출정", actions.Depart, 20, bottom + 8, width - 40, true, 64, "ui.button.destructive");
            PortraitWidgets.Art(ui, depart.transform, "c-fertile_duel", 65, 11, 44, 42);
            Tabs(ui, root, account, actions.Navigate);
        }

        static void SelectionPanel(UiShell ui, RectTransform root, EstateMapScreen screen, EstateMapActions actions, float width, float bottom)
        {
            var building = screen.Building(screen.Selected.Value); var kind = screen.Kind(building.Kind);
            const float height = 168; var panel = ColorPlate(root, "EstateMap.Selection", new Color32(52, 41, 27, 245), 10, bottom - height, width - 20, height);
            panel.GetComponent<Image>().raycastTarget = true; // taps on the panel never reach the map below it
            ColorPlate(panel, "Gold rule", new Color32(139, 106, 51, 255), 0, 0, width - 20, 2);
            Caption(ui, panel, screen.Name(building.Kind) + " Lv " + building.Level + " / " + kind.MaxLevel(), 14, 10, width - 48, 28, 18);
            Caption(ui, panel, screen.Status(building), 14, 40, width - 48, 22, 13);
            var half = (width - 58) / 2;
            if (screen.Moving)
            {
                var reason = Caption(ui, panel, "옮길 칸을 누르세요 · " + screen.ReasonText(screen.PreviewReason), 14, 66, width - 48, 34, 13);
                reason.color = screen.PreviewValid ? new Color32(168, 222, 150, 255) : new Color32(240, 140, 120, 255);
                ActionButton(ui, panel, "EstateMap.Confirm", "확정", () => { screen.Confirm(); actions.Redraw(); }, 14, 108, half, screen.PreviewValid, 48, "ui.button.primary");
                ActionButton(ui, panel, "EstateMap.Cancel", "취소", () => { screen.Cancel(); actions.Redraw(); }, 24 + half, 108, half, true, 48);
                return;
            }
            var busy = screen.UnderConstruction(building.Id);
            if (busy) Caption(ui, panel, screen.ReasonText("under-construction"), 14, 66, width - 48, 34, 13);
            ActionButton(ui, panel, "EstateMap.Move", "이동", () => { screen.BeginMove(); actions.Redraw(); }, 14, 108, half, !busy, 48, "ui.button.primary");
            ActionButton(ui, panel, "EstateMap.Close", "닫기", () => { screen.Select(null); actions.Redraw(); }, 24 + half, 108, half, true, 48);
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

        static void Fill(RectTransform parent, string name, Color color, float inset)
        {
            var rect = UiShell.Rect(name, parent); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
        }

        static Text Stretched(UiShell ui, RectTransform parent, string value, int size, Color color, bool bottomStrip = false)
        {
            var text = PortraitWidgets.Label(ui, parent, value, 0, 0, 0, 0, size);
            var rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = bottomStrip ? new Vector2(1, 0) : Vector2.one;
            rect.pivot = new Vector2(.5f, 0); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = bottomStrip ? new Vector2(0, 20) : Vector2.zero;
            text.alignment = TextAnchor.MiddleCenter; text.color = color; text.raycastTarget = false; text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow; text.fontStyle = FontStyle.Bold;
            if (bottomStrip) { var shadow = text.gameObject.AddComponent<Outline>(); shadow.effectColor = new Color32(30, 22, 12, 255); }
            return text;
        }
    }

    // Lays the map's grid-anchored rects out for the current zoom and turns one-finger drags into pans, two-finger pinches
    // and mouse-wheel turns into zoom steps, and taps on empty ground into cell taps. Drags that start on a building pan the
    // map too; uGUI cancels the building's click once a drag begins, so looking around never selects or builds.
    public sealed class EstateMapView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        // Labels hide on the whole-map zoom where a two-cell building is too small to carry a name.
        const float LabelMinimumCell = 30;
        struct Placed { public RectTransform Rect; public RectInt Grid; }
        readonly List<Placed> placed = new List<Placed>();
        readonly List<Text> labels = new List<Text>();
        readonly Dictionary<int, Vector2> fingers = new Dictionary<int, Vector2>();
        float pinchBase;
        public EstateMapModel Model;
        public Action<Vector2> Tapped;
        public Action Zoomed;
        RectTransform Rect => (RectTransform)transform;

        public RectTransform Add(RectTransform rect, int x, int y, int width, int height)
        {
            placed.Add(new Placed { Rect = rect, Grid = new RectInt(x, y, width, height) }); return rect;
        }
        public void Label(Text text) => labels.Add(text);

        public void Layout()
        {
            if (Model == null) return;
            Model.SetViewport(Rect.rect.size);
            var cell = Model.Cell;
            foreach (var item in placed)
            {
                var origin = Model.GridToViewport(item.Grid.position);
                // Zero-width rects are grid lines: one canvas unit thick.
                var size = new Vector2(item.Grid.width == 0 ? 1 : item.Grid.width * cell, item.Grid.height == 0 ? 1 : item.Grid.height * cell);
                PortraitWidgets.Position(item.Rect, item.Grid.width == 0 ? origin.x - .5f : origin.x, item.Grid.height == 0 ? origin.y - .5f : origin.y, size.x, size.y);
            }
            foreach (var label in labels) label.enabled = cell >= LabelMinimumCell;
        }

        public void Step(int direction)
        {
            if (Model != null && Model.StepZoom(direction, Model.Viewport * .5f)) { Layout(); Zoomed?.Invoke(); }
        }

        void OnRectTransformDimensionsChange() { if (Model != null && isActiveAndEnabled) Layout(); }

        Vector2 Local(PointerEventData data, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screen, data.pressEventCamera ?? data.enterEventCamera, out var local);
            var rect = Rect.rect; return new Vector2(local.x - rect.xMin, rect.yMax - local.y);
        }

        float Spread() { var points = fingers.Values.Take(2).ToArray(); return Vector2.Distance(points[0], points[1]); }

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
            var spread = Spread(); var direction = EstateMapModel.PinchDirection(spread / pinchBase);
            if (direction == 0) return;
            pinchBase = spread;
            var centre = Local(data, (fingers.Values.First() + fingers.Values.Skip(1).First()) * .5f);
            if (Model.StepZoom(direction, centre)) { Layout(); Zoomed?.Invoke(); }
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
            if (!data.dragging) Tapped?.Invoke(Local(data, data.position));
        }
    }
}
