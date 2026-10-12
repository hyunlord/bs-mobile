using System;
using System.Collections.Generic;
using System.Linq;
using Game.App.Generated;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public enum PortraitMetaPage { Estate, Departure, Vassals, Challenges, Shop, Idle }
    public static partial class PortraitMetaPanels
    {
        public const string Glyphs = "·✓가각간감강건검경계곡곳과관구국금급기김날너는능니다달대도돌동되된됨됩뒤드랍러레려력로록료루리린만면모목물받버번벨병보본봄부비산상새생서석선설성소수스습승시신쓰씨아안않알앗앞어없에연열영완왕용우원으은을음의이인있자작잠장재적전점정제조족준중지직진짐차철초최출취카클택터틴판포표하한할해행현화확휴";
        static float Width(UiShell ui) => ui.SafeRoot.rect.width;
        static float Height(UiShell ui) => ui.SafeRoot.rect.height;
        static RectTransform Page(UiShell ui, string name, string title)
        {
            var root = UiShell.Rect("Portrait." + name, ui.Content); UiShell.Stretch(root); root.gameObject.AddComponent<Image>().color = new Color32(35, 30, 23, 255);
            if (!string.IsNullOrEmpty(title)) Caption(ui, root, title, 16, 50, Width(ui) - 32, 34, 24); return root;
        }
        static float Label(UiShell ui, Transform parent, string value, float x, float y, float width, int size = 14)
        {
            var text = PortraitWidgets.Label(ui, parent, value, x, y, width, 24, size);
            text.color = LightInk; text.resizeTextForBestFit = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            var height = Mathf.Max(24, text.preferredHeight + 6); text.rectTransform.sizeDelta = new Vector2(width, height); return height;
        }
        static Button ActionButton(UiShell ui, Transform parent, string name, string text, Action action, float x, float y, float width, bool enabled = true, float height = 48, string role = "ui.button.secondary")
        {
            Button button = null;
            button = PortraitWidgets.Button(ui, parent, text, () => { if (button.interactable) action?.Invoke(); }, x, y, width, height, enabled, role);
            button.name = name; if (height >= 60) button.GetComponentInChildren<Text>().fontSize = 22; return button;
        }
        static RectTransform Body(UiShell ui, RectTransform root, float top = 66, float bottom = 82)
        {
            var viewport = UiShell.Rect("Scroll viewport", root); viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(18, bottom); viewport.offsetMax = new Vector2(-18, -top);
            viewport.gameObject.AddComponent<Image>().color = Color.clear; viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var body = UiShell.Rect("Scroll body", viewport); body.anchorMin = new Vector2(0, 1); body.anchorMax = Vector2.one; body.pivot = new Vector2(.5f, 1);
            body.sizeDelta = Vector2.zero; scroll.viewport = viewport; scroll.content = body; return body;
        }
        static void End(RectTransform body, float y) => body.sizeDelta = new Vector2(0, y + 16);
        static Image Art(UiShell ui, Transform parent, string role, float x, float y, float width, float height)
        {
            if (string.IsNullOrEmpty(role)) return null;
            var rect = PortraitWidgets.Place("Art:" + role, parent, x, y, width, height); var image = ui.Surface(rect, role);
            image.preserveAspect = true; image.raycastTarget = false; return image;
        }
        static Material lockedPortraitMaterial;
        static void LockedPortrait(Image image)
        {
            if (image == null) return;
            if (lockedPortraitMaterial == null)
            {
                var shader = Resources.Load<Shader>("PortraitLocked");
                if (shader != null && shader.isSupported) lockedPortraitMaterial = new Material(shader) { name = "Locked portrait", hideFlags = HideFlags.HideAndDontSave };
            }
            if (lockedPortraitMaterial != null) image.material = lockedPortraitMaterial;
            else image.color = new Color(.45f, .45f, .45f, 1);
        }
        static void MaterialArt(UiShell ui, Transform parent, string materialId, string fallbackRole, float x, float y, float size)
        {
            if (materialId == "meta:grain" || materialId == "meta:timber")
            {
                PortraitWidgets.Art(ui, parent, materialId == "meta:grain" ? "r-grain" : "r-timber", x, y, size, size); return;
            }
            if (materialId != "meta:iron" && materialId != "meta:charter")
            { Art(ui, parent, fallbackRole, x, y, size, size); return; }
            var iron = materialId == "meta:iron";
            var rect = PortraitWidgets.Place("Resource:" + materialId, parent, x + (iron ? 0 : 2), y + (iron ? 5 : 2), size - (iron ? 0 : 4), size - (iron ? 10 : 4));
            rect.gameObject.AddComponent<CanvasRenderer>();
            var shape = rect.gameObject.AddComponent<PortraitBmShape>();
            shape.Shape = iron ? PortraitBmShape.Form.Ingot : PortraitBmShape.Form.Circle;
            shape.color = iron ? new Color32(158, 164, 170, 255) : new Color32(163, 39, 30, 255);
            shape.Edge = iron ? new Color32(60, 64, 69, 255) : new Color32(94, 18, 13, 255);
            shape.Border = 1.4f; shape.raycastTarget = false;
        }
        static bool Pending(MetaState state, MetaClaimKind kind) => state.PendingClaims.Any(c => c.Kind == kind && (kind == MetaClaimKind.Challenge || c.Rewards.Values.Any(v => v > 0)));
        static readonly Color PaperInk = new Color32(59, 42, 24, 255);
        static readonly Color LightInk = new Color32(246, 235, 210, 255);
        static Text Caption(UiShell ui, Transform parent, string value, float x, float y, float width, float height, int size = 13, bool paper = false, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var text = PortraitWidgets.Label(ui, parent, value, x, y, width, height, size);
            text.color = paper ? PaperInk : LightInk; text.alignment = align; text.raycastTarget = false;
            text.resizeTextForBestFit = false; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate; return text;
        }
        static RectTransform Plate(UiShell ui, Transform parent, string name, string role, float x, float y, float width, float height)
        {
            var rect = PortraitWidgets.Place(name, parent, x, y, width, height); ui.Surface(rect, role); return rect;
        }
        static RectTransform ColorPlate(Transform parent, string name, Color color, float x, float y, float width, float height)
        {
            var rect = PortraitWidgets.Place(name, parent, x, y, width, height); var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return rect;
        }
        static Button Hit(Transform parent, string name, Action action, float x, float y, float width, float height, bool enabled = true)
        {
            var rect = PortraitWidgets.Place(name, parent, x, y, width, height); var image = rect.gameObject.AddComponent<Image>(); image.color = Color.clear;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.interactable = enabled;
            button.onClick.AddListener(() => { if (button.interactable) action?.Invoke(); }); return button;
        }
        static void Tabs(UiShell ui, RectTransform root, MetaState state, Action<PortraitMetaPage> navigate)
        {
            var pages = new[] { PortraitMetaPage.Estate, PortraitMetaPage.Vassals, PortraitMetaPage.Challenges, PortraitMetaPage.Shop };
            var names = new[] { "영지", "가신", "도전", "상점" }; var assets = new[] { "o-crest", "v-furrow_guard", "c-rally_boundary", "o-chest" };
            var bar = ColorPlate(root, "Navigation", new Color32(52, 41, 27, 255), 0, Height(ui) - 116, Width(ui), 116);
            ColorPlate(bar, "Gold rule", new Color32(139, 106, 51, 255), 0, 0, Width(ui), 2);
            var cell = (Width(ui) - 16) / 4;
            for (var i = 0; i < pages.Length; i++)
            {
                var page = pages[i]; var x = 8 + i * cell;
                PortraitWidgets.Art(ui, bar, assets[i], x + (cell - 40) / 2, 12, 40, 42);
                Caption(ui, bar, names[i], x, 56, cell, 24, 13, false, TextAnchor.MiddleCenter);
                Hit(bar, "Tab." + page, () => navigate(page), x, 4, cell, 82);
                if (page == PortraitMetaPage.Challenges && Pending(state, MetaClaimKind.Challenge))
                    ColorPlate(bar, "Claim dot", new Color32(194, 39, 28, 255), x + cell - 24, 10, 12, 12);
            }
        }
        static float Wallet(UiShell ui, Transform parent, MetaCatalog catalog, MetaState state, float width, float y, Action shop = null)
        {
            for (var i = 0; i < catalog.Materials.Length; i++)
            {
                var material = catalog.Materials[i]; var x = (i % 2) * (width / 2); var row = y + i / 2 * 36;
                Art(ui, parent, material.ArtRole, x, row, 26, 26); state.Wallet.TryGetValue(material.Id, out var value);
                Label(ui, parent, material.Name + " " + value, x + 30, row, width / 2 - 32);
            }
            y += Mathf.Ceil(catalog.Materials.Length / 2f) * 36;
            if (shop != null)
            {
                ActionButton(ui, parent, "Estate.Gems", "보석 [n] +", shop, 0, y, width, true, 44);
                return y + 52;
            }
            return y + Label(ui, parent, "보석 [n] · 준비 중", 0, y, width) + 8;
        }
        static int EffectLevel(MetaCatalog catalog, MetaState state, ManorEffect effect) => catalog.ManorBuildings.Where(b => b.Effect == effect).Sum(b => state.ManorLevels.TryGetValue(b.Id, out var level) ? level : 0);
        static int Slots(MetaCatalog catalog, MetaState state) => Math.Min(catalog.Economy.MaximumVassalSlots, catalog.Economy.BaseVassalSlots + EffectLevel(catalog, state, ManorEffect.VassalSlots) / catalog.Economy.BarracksLevelsPerSlot);
        static string ChapterName(MetaChapter chapter) => chapter.Index == 1 ? "새봄의 터" : chapter.Name;
        public static void Estate(UiShell ui, MetaCatalog catalog, MetaState state, Action<string> priority, Action depart,
            Action settings, Action<PortraitMetaPage> navigate, Action<long> claim)
        {
            var root = Page(ui, "Estate", ""); var width = Width(ui); var inner = width - 20;
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
            var next = catalog.Chapters.OrderBy(c => c.Index).FirstOrDefault(c => c.Index > state.HighestClearedChapter) ?? catalog.Chapters.Last();
            var banner = Plate(ui, root, "Next chapter", "ui.button.primary", 10, 104, width - 78, 56);
            Caption(ui, banner, "다음 출정", 25, 5, width - 130, 18, 11);
            Caption(ui, banner, "챕터 " + next.Index + " · " + ChapterName(next), 25, 22, width - 130, 27, 19);
            PortraitRunPanels.RoundIcon(ui, root, "Estate.Settings", "settings", settings, width - 60, 110);
            var landHeight = Mathf.Max(300, Height(ui) - 392);
            var land = PortraitWidgets.Place("Manor landscape", root, 10, 172, inner, landHeight);
            var landscape = land.gameObject.AddComponent<PortraitManorDrawing>(); landscape.raycastTarget = false;
            var images = new[] { "b-forge", "b-scriptorium", "b-granary", "b-barracks" }; var cell = inner / 2;
            for (var i = 0; i < catalog.ManorBuildings.Length; i++)
            {
                var building = catalog.ManorBuildings[i]; var x = i % 2 * cell; var y = i / 2 * landHeight / 2;
                var artHeight = landHeight / 2 - 56; var selected = state.ManorPriority == building.Id;
                if (i < images.Length) PortraitWidgets.Art(ui, land, images[i], x + 6, y + 14, cell - 12, artHeight);
                else Art(ui, land, building.ArtRole, x + 6, y + 14, cell - 12, artHeight);
                state.ManorLevels.TryGetValue(building.Id, out var level);
                var name = Plate(ui, land, "Building name:" + building.Id, "ui.button.secondary", x + 12, y + landHeight / 2 - 44, cell - 24, 34);
                Caption(ui, name, building.Name + " Lv " + level + (selected ? " · 우선" : ""), 8, 0, cell - 40, 34, 13, true, TextAnchor.MiddleCenter);
                Hit(land, "Estate.Priority:" + building.Id, () => priority(building.Id), x + 6, y + 8, cell - 12, landHeight / 2 - 12);
                if (selected)
                {
                    var flag = PortraitWidgets.Place("Priority flag", land, x + cell * .62f, y + 5, 34, 44);
                    var drawing = flag.gameObject.AddComponent<PortraitManorDrawing>(); drawing.Flag = true; drawing.raycastTarget = false;
                }
            }
            var chestX = inner * .32f; var chestY = landHeight * .50f;
            PortraitBmPanels.Shape(land, "Production chest rim", chestX, chestY, 76, 76, PortraitBmShape.Form.Circle,
                new Color32(42, 37, 29, 235), new Color32(243, 213, 140, 255), 3);
            PortraitWidgets.Art(ui, land, "o-chest", chestX + 16, chestY + 8, 44, 39);
            var chestCaption = Caption(ui, land, Pending(state, MetaClaimKind.Idle) ? "받기" : "생산 상자", chestX, chestY + 47, 76, 22, 11, false, TextAnchor.MiddleCenter);
            chestCaption.color = new Color32(243, 213, 140, 255); chestCaption.fontStyle = FontStyle.Bold;
            Hit(land, "Estate.Idle", () => navigate(PortraitMetaPage.Idle), chestX, chestY, 76, 76);
            var pendingClaims = state.PendingClaims.Where(c => c.Kind == MetaClaimKind.Idle && c.Rewards.Values.Any(v => v > 0)).ToArray();
            for (var i = 0; i < pendingClaims.Length; i++)
            {
                var sequence = pendingClaims[i].Sequence;
                ActionButton(ui, root, "Estate.Claim:" + sequence, "생산 보상 받기", () => claim(sequence), 20, Height(ui) - 234 - i * 48, width - 40, true, 44);
            }
            var departButton = ActionButton(ui, root, "Estate.Depart", "출정", depart, 20, Height(ui) - 204, width - 40, true, 70, "ui.button.destructive");
            PortraitWidgets.Art(ui, departButton.transform, "c-fertile_duel", 65, 13, 44, 42);
            Tabs(ui, root, state, navigate);
        }
    }
}

namespace Game.App
{
    [RequireComponent(typeof(CanvasRenderer))]
    sealed class PortraitManorDrawing : UnityEngine.UI.MaskableGraphic
    {
        public bool Flag;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect;
            if (Flag)
            {
                Quad(mesh, new Vector2(2, rect.yMin), new Vector2(5, rect.yMin), new Vector2(5, rect.yMin + 42), new Vector2(2, rect.yMin + 42), new Color32(90, 59, 18, 255));
                var start = mesh.currentVertCount; var color = new Color32(179, 38, 30, 255);
                mesh.AddVert(new Vector3(5, rect.yMin + 39), color, Vector2.zero); mesh.AddVert(new Vector3(30, rect.yMin + 39), color, Vector2.zero);
                mesh.AddVert(new Vector3(24, rect.yMin + 31), color, Vector2.zero); mesh.AddVert(new Vector3(30, rect.yMin + 23), color, Vector2.zero);
                mesh.AddVert(new Vector3(5, rect.yMin + 23), color, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 4); mesh.AddTriangle(start + 2, start + 3, start + 4); return;
            }
            var center = rect.center; var radius = 18f; mesh.AddVert(center, new Color32(126, 157, 83, 255), Vector2.zero);
            const int segments = 64;
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2 / segments; var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var edge = new Vector2(Mathf.Sign(direction.x) * (rect.width / 2 - radius), Mathf.Sign(direction.y) * (rect.height / 2 - radius));
                var point = center + edge + direction * radius;
                mesh.AddVert(point, new Color32(66, 94, 43, 255), Vector2.zero);
                if (i > 0) mesh.AddTriangle(0, i, i + 1);
            }
            Quad(mesh, new Vector2(rect.xMin + rect.width * .35f, rect.yMin), new Vector2(rect.xMin + rect.width * .47f, rect.yMin), new Vector2(rect.xMin + rect.width * .65f, rect.yMax), new Vector2(rect.xMin + rect.width * .53f, rect.yMax), new Color32(156, 145, 92, 210));
            Quad(mesh, new Vector2(rect.xMin, rect.center.y - 20), new Vector2(rect.xMax, rect.center.y - 20), new Vector2(rect.xMax, rect.center.y + 20), new Vector2(rect.xMin, rect.center.y + 20), new Color32(164, 151, 94, 120));
        }
        static void Quad(UnityEngine.UI.VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            var start = mesh.currentVertCount; mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero); mesh.AddVert(c, tint, Vector2.zero); mesh.AddVert(d, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
