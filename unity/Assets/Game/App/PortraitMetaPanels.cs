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
            var root = UiShell.Rect("Portrait." + name, ui.Content); UiShell.Stretch(root); ui.Surface(root, "ui.panel");
            Label(ui, root, title, 18, 14, Width(ui) - 100, 24); return root;
        }
        static float Label(UiShell ui, Transform parent, string value, float x, float y, float width, int size = 14)
        {
            var text = PortraitWidgets.Label(ui, parent, value, x, y, width, 24, size);
            text.resizeTextForBestFit = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            var height = Mathf.Max(24, text.preferredHeight + 6); text.rectTransform.sizeDelta = new Vector2(width, height); return height;
        }
        static Button ActionButton(UiShell ui, Transform parent, string name, string text, Action action, float x, float y, float width, bool enabled = true, float height = 48)
        {
            Button button = null;
            button = PortraitWidgets.Button(ui, parent, text, () => { if (button.interactable) action?.Invoke(); }, x, y, width, height, enabled);
            button.name = name; return button;
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
        static void Art(UiShell ui, Transform parent, string role, float x, float y, float width, float height)
        {
            if (string.IsNullOrEmpty(role)) return;
            var rect = PortraitWidgets.Place("Art:" + role, parent, x, y, width, height); var image = ui.Surface(rect, role);
            image.preserveAspect = true; image.raycastTarget = false;
        }
        static bool Pending(MetaState state, MetaClaimKind kind) => state.PendingClaims.Any(c => c.Kind == kind && (kind == MetaClaimKind.Challenge || c.Rewards.Values.Any(v => v > 0)));
        static void Tabs(UiShell ui, RectTransform root, MetaState state, Action<PortraitMetaPage> navigate)
        {
            var pages = new[] { PortraitMetaPage.Estate, PortraitMetaPage.Vassals, PortraitMetaPage.Challenges, PortraitMetaPage.Shop };
            var names = new[] { "영지", "가신", "도전", "상점" }; var width = (Width(ui) - 36 - 18) / 4;
            for (var i = 0; i < pages.Length; i++)
            {
                var page = pages[i]; var x = 18 + i * (width + 6);
                ActionButton(ui, root, "Tab." + page, names[i], () => navigate(page), x, Height(ui) - 62, width);
                if (page == PortraitMetaPage.Challenges && Pending(state, MetaClaimKind.Challenge))
                { var dot = PortraitWidgets.Place("Claim dot", root, x + width - 13, Height(ui) - 59, 7, 7); dot.gameObject.AddComponent<Image>().color = new Color32(164, 45, 34, 255); }
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
            var root = Page(ui, "Estate", "휴경의 왕국 · 영지"); var width = Width(ui) - 36;
            ActionButton(ui, root, "Estate.Settings", "설정", settings, Width(ui) - 74, 10, 56);
            var body = Body(ui, root, 66, 140); var y = Wallet(ui, body, catalog, state, width, 0, () => navigate(PortraitMetaPage.Shop));
            var next = catalog.Chapters.OrderBy(c => c.Index).FirstOrDefault(c => c.Index > state.HighestClearedChapter) ?? catalog.Chapters.Last();
            y += Label(ui, body, "다음 출정 · " + ChapterName(next), 0, y, width, 18) + 12;
            var cell = (width - 12) / 2;
            for (var i = 0; i < catalog.ManorBuildings.Length; i++)
            {
                var building = catalog.ManorBuildings[i]; var x = i % 2 * (cell + 12); var row = y + i / 2 * 184;
                Art(ui, body, building.ArtRole, x, row, cell, 104);
                state.ManorLevels.TryGetValue(building.Id, out var level);
                Label(ui, body, building.Name + " · " + level, x + 4, row + 104, cell - 8, 16);
                ActionButton(ui, body, "Estate.Priority:" + building.Id, state.ManorPriority == building.Id ? "우선 성장 ✓" : "우선 성장", () => priority(building.Id), x, row + 132, cell, true, 44);
            }
            y += Mathf.Ceil(catalog.ManorBuildings.Length / 2f) * 184;
            y += Label(ui, body, "재료가 모이면 장원이 자랍니다", 0, y, width) + 8;
            ActionButton(ui, body, "Estate.Idle", Pending(state, MetaClaimKind.Idle) ? "생산 상자 · 받을 보상이 있습니다" : "생산 상자", () => navigate(PortraitMetaPage.Idle), 0, y, width); y += 56;
            foreach (var pending in state.PendingClaims.Where(c => c.Kind == MetaClaimKind.Idle && c.Rewards.Values.Any(v => v > 0)))
            {
                y += Label(ui, body, MetaScreens.Amounts(catalog, pending.Rewards), 0, y, width);
                var sequence = pending.Sequence; ActionButton(ui, body, "Estate.Claim:" + sequence, "받기", () => claim(sequence), 0, y, width); y += 56;
            }
            End(body, y);
            var start = ActionButton(ui, root, "Estate.Depart", "출정 준비", depart, 18, Height(ui) - 124, width);
            var colors = start.colors; colors.normalColor = new Color32(145, 47, 37, 255); colors.highlightedColor = colors.normalColor; start.colors = colors;
            Tabs(ui, root, state, navigate);
        }
    }
}
