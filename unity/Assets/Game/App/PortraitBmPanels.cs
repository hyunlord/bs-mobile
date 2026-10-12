using System;
using System.Collections.Generic;
using Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public enum BmFakeOutcome { Success, Failure }
    public sealed class BmAdOffer
    {
        public bool Visible { get; }
        public bool Enabled { get; }
        public string Label { get; }
        public string UnavailableReason { get; }
        public BmAdOffer(bool visible, bool enabled, string label, string unavailableReason = "")
        { Visible = visible; Enabled = enabled; Label = label; UnavailableReason = unavailableReason; }
    }
    public sealed class BmIdleView
    {
        public long ElapsedSeconds { get; }
        public long CapacitySeconds { get; }
        public string RewardText { get; }
        public bool CanClaim { get; }
        public string ArtRole { get; }
        public BmAdOffer Ad { get; }
        public BmIdleView(long elapsedSeconds, long capacitySeconds, string rewardText, bool canClaim, BmAdOffer ad, string artRole = null)
        { ArtRole = artRole; ElapsedSeconds = elapsedSeconds; CapacitySeconds = capacitySeconds; RewardText = rewardText; CanClaim = canClaim; Ad = ad; }
    }
    public sealed class BmReviveView
    {
        public BmAdOffer Ad { get; }
        public string RunCaption { get; }
        public BmReviveView(BmAdOffer ad, string runCaption = null)
        { Ad = ad; RunCaption = runCaption ?? "계절 [n] · 시간 [n] · 레벨 [n]"; }
    }
    public sealed class BmShopView
    {
        public IReadOnlyList<string> CosmeticLabels { get; }
        public IReadOnlyList<string> ThemeLabels { get; }
        public bool AdRemovalOwned { get; }
        public IReadOnlyList<string> CosmeticArtRoles { get; }
        public IReadOnlyList<string> ThemeArtRoles { get; }
        public BmShopView(IReadOnlyList<string> cosmeticLabels, IReadOnlyList<string> themeLabels, bool adRemovalOwned,
            IReadOnlyList<string> cosmeticArtRoles = null, IReadOnlyList<string> themeArtRoles = null)
        { CosmeticLabels = cosmeticLabels; ThemeLabels = themeLabels; AdRemovalOwned = adRemovalOwned; CosmeticArtRoles = cosmeticArtRoles; ThemeArtRoles = themeArtRoles; }
    }
    public sealed class BmPassView
    {
        public IReadOnlyList<string> FreeRewards { get; }
        public IReadOnlyList<string> PaidRewards { get; }
        public BmPassView(IReadOnlyList<string> freeRewards, IReadOnlyList<string> paidRewards)
        { FreeRewards = freeRewards; PaidRewards = paidRewards; }
    }

    // These views only present policy supplied by the host. Visibility is never inferred from a clock or wallet here.
    public static class PortraitBmPanels
    {
        public const string Glyphs = "·가간개거격결고공관광기김나는능니다달도돌되라러력로료마며면모무묶받발번보부비산상생서석선설성수숨스습시실쓰아안않어없여외용원유으은을음이인일입있자장재전점정제준중즌지짐출치칩택테판패한험현형화확활힘";
        static float Width(UiShell ui) => ui.SafeRoot.rect.width;
        static float Height(UiShell ui) => ui.SafeRoot.rect.height;
        static RectTransform Root(UiShell ui, string name, string title)
        {
            var root = UiShell.Rect("Portrait." + name, ui.Content); UiShell.Stretch(root); ui.Surface(root, "ui.panel");
            Label(ui, root, title, 18, 16, Width(ui) - 36, 24); return root;
        }
        static float Label(UiShell ui, Transform parent, string text, float x, float y, float width, int size = 14)
        {
            var label = PortraitWidgets.Label(ui, parent, text ?? "", x, y, width, 24, size);
            label.resizeTextForBestFit = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow;
            var height = Mathf.Max(24, label.preferredHeight + 6); label.rectTransform.sizeDelta = new Vector2(width, height); return height;
        }
        static Button Button(UiShell ui, Transform parent, string name, string text, Action action, float x, float y, float width, bool enabled = true)
        {
            Button button = null;
            button = PortraitWidgets.Button(ui, parent, text, () => { if (button.interactable) action?.Invoke(); }, x, y, width, 48, enabled);
            button.name = name; return button;
        }
        static RectTransform Body(UiShell ui, RectTransform root, float bottom)
        {
            var viewport = UiShell.Rect("Scroll viewport", root); viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(18, bottom); viewport.offsetMax = new Vector2(-18, -66);
            viewport.gameObject.AddComponent<Image>().color = Color.clear; viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var body = UiShell.Rect("Scroll body", viewport); body.anchorMin = new Vector2(0, 1); body.anchorMax = Vector2.one; body.pivot = new Vector2(.5f, 1); body.sizeDelta = Vector2.zero;
            scroll.viewport = viewport; scroll.content = body; return body;
        }
        static void End(RectTransform body, float height) => body.sizeDelta = new Vector2(0, height + 16);
        static void Art(UiShell ui, Transform parent, string role, float x, float y, float width, float height)
        {
            if (string.IsNullOrEmpty(role)) return;
            var rect = PortraitWidgets.Place("Art:" + role, parent, x, y, width, height);
            var image = ui.Surface(rect, role); image.preserveAspect = true; image.raycastTarget = false;
        }
        static readonly Color PaperInk = new Color32(59, 42, 24, 255);
        static readonly Color Ivory = new Color32(246, 235, 210, 255);
        static readonly Color Gold = new Color32(243, 213, 140, 255);
        static readonly Color Muted = new Color32(201, 188, 159, 255);
        static RectTransform Page(UiShell ui, string name, Color background)
        {
            var root = UiShell.Rect("Portrait." + name, ui.Content); UiShell.Stretch(root);
            var image = root.gameObject.AddComponent<Image>(); image.color = background; image.raycastTarget = false;
            return root;
        }
        static Text Text(UiShell ui, Transform parent, string text, float x, float y, float width, float height,
            int size, Color color, bool centered = false)
        {
            var label = PortraitWidgets.Label(ui, parent, text, x, y, width, height, size);
            label.color = color; label.alignment = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            label.resizeTextForBestFit = false; label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }
        static RectTransform Panel(UiShell ui, Transform parent, string name, float x, float y, float width, float height, string role = "ui.panel")
        {
            var panel = PortraitWidgets.Place(name, parent, x, y, width, height); ui.Surface(panel, role); return panel;
        }
        static Button Action(UiShell ui, Transform parent, string name, string text, Action action,
            float x, float y, float width, float height = 52, bool enabled = true, string role = "ui.button.secondary", int size = 15)
        {
            var button = PortraitWidgets.Button(ui, parent, text, action ?? (() => { }), x, y, width, height, enabled, role);
            button.name = name; var label = button.GetComponentInChildren<Text>(); label.fontSize = size;
            label.color = role == "ui.button.primary" ? Color.white : PaperInk; return button;
        }
        static void Picture(UiShell ui, Transform parent, string name, float x, float y, float width, float height)
            => PortraitWidgets.Art(ui, parent, name, x, y, width, height);
        public static Button OptionalAdButton(UiShell ui, Transform parent, BmAdOffer offer, Action action, float x, float y, float width)
        {
            if (offer == null || !offer.Visible) return null;
            return Action(ui, parent, "OptionalAd", offer.Enabled || string.IsNullOrEmpty(offer.UnavailableReason) ? offer.Label : offer.UnavailableReason,
                action, x, y, width, 48, offer.Enabled);
        }
        public static void Shop(UiShell ui, BmShopView view, Action pass, Action adRemoval, Action back, Action<PortraitMetaPage> navigate = null)
        {
            var root = Page(ui, "Shop", new Color32(34, 30, 23, 255)); var width = Width(ui); var content = width - 20;
            Text(ui, root, "상점", 16, 50, 180, 38, 24, Ivory);
            Text(ui, root, "보석 [n]", width - 112, 52, 96, 34, 14, new Color32(191, 227, 250, 255), true);
            Text(ui, root, "영주 외형", 10, 94, 94, 28, 16, Gold);
            Text(ui, root, "능력치 없음 · 그림만 바뀜", 104, 98, content - 94, 24, 11, Muted);
            var cell = (content - 8) * .5f;
            for (var i = 0; i < 2; i++)
            {
                var card = Panel(ui, root, "Shop.Cosmetic:" + i, 10 + i * (cell + 8), 128, cell, 196, i == 0 ? "ui.card.epic" : "ui.card.rare");
                var artBack = PortraitWidgets.Place("Cosmetic backdrop", card, 10, 10, cell - 20, 124);
                artBack.gameObject.AddComponent<Image>().color = i == 0 ? new Color32(80, 55, 109, 255) : new Color32(48, 75, 119, 255);
                Picture(ui, card, "hero", (cell - 88) * .5f, 20, 88, 111);
                var name = view.CosmeticLabels != null && i < view.CosmeticLabels.Count ? view.CosmeticLabels[i] : "[외형 이름]";
                Text(ui, card, name, 13, 136, cell - 26, 28, 13, PaperInk, true);
                Text(ui, card, "[가격 미정]", 13, 164, cell - 26, 20, 12, PaperInk, true);
            }
            var theme = Panel(ui, root, "Shop.Theme", 10, 334, content, 88);
            Picture(ui, theme, "b-granary", 10, 12, 72, 63);
            Text(ui, theme, "영지 테마", 90, 15, content - 110, 28, 16, PaperInk);
            Text(ui, theme, "장원·밭 그림만 바뀜 · [구성 미정]", 90, 43, content - 110, 28, 12, PaperInk);
            var passButton = Action(ui, root, "Shop.Pass", "", pass, 10, 432, content, 76, true, "ui.button.primary");
            Picture(ui, passButton.transform, "c-seed_crown", 25, 14, 58, 47);
            Text(ui, passButton.transform, "시즌 패스", 94, 12, content - 118, 28, 17, Color.white);
            Text(ui, passButton.transform, "무료 줄 · 유료 줄 [가격 미정]", 94, 39, content - 118, 24, 12, Ivory);
            var removal = Action(ui, root, "Shop.AdRemoval", "", adRemoval, 10, 518, cell, 92, true, "ui.panel");
            Text(ui, removal.transform, "광고 제거", 12, 10, cell - 24, 25, 15, PaperInk);
            Text(ui, removal.transform, "1회 구매 · 광고 보상 바로 받기", 12, 35, cell - 24, 28, 11, PaperInk);
            Text(ui, removal.transform, view.AdRemovalOwned ? "보유 중" : "[가격 미정]", 12, 63, cell - 24, 20, 12, PaperInk);
            var gems = Panel(ui, root, "Shop.GemBundle", 18 + cell, 518, cell, 92);
            Text(ui, gems, "보석 묶음", 12, 10, cell - 24, 25, 15, PaperInk);
            Text(ui, gems, "외형·패스에만 사용", 12, 37, cell - 24, 22, 11, PaperInk);
            Text(ui, gems, "[구성·가격 미정]", 12, 62, cell - 24, 22, 12, PaperInk);
            Text(ui, root, "보기 전용 · 실제 결제되지 않습니다", 16, 626, width - 32, 24, 12, Muted, true);
            var tabs = PortraitWidgets.Place("Shop.Tabs", root, 0, Height(ui) - 116, width, 116);
            tabs.gameObject.AddComponent<Image>().color = new Color32(51, 40, 25, 255);
            string[] icons = { "o-crest", "v-furrow_guard", "c-rally_boundary", "o-chest" };
            string[] titles = { "영지", "가신", "도전", "상점" };
            var pages = new[] { PortraitMetaPage.Estate, PortraitMetaPage.Vassals, PortraitMetaPage.Challenges, PortraitMetaPage.Shop };
            for (var i = 0; i < 4; i++)
            {
                var x = i * width / 4;
                Picture(ui, tabs, icons[i], x + (width / 4 - 40) * .5f, 13, 40, 40);
                Text(ui, tabs, titles[i], x, 55, width / 4, 24, 12, i == 3 ? Gold : Muted, true);
                var page = pages[i];
                var tab = Action(ui, tabs, i == 0 ? "Shop.Back" : "Tab." + page, "",
                    i == 0 ? back : () => navigate?.Invoke(page), x, 4, width / 4, 78, i == 0 || navigate != null);
                tab.image.color = Color.clear;
            }
        }
        // Geometry is in points from source/Pass.html, not proportions of the render resolution.
        static class PassBoard
        {
            public const float Margin = 16, BackTop = 50, BackSize = 44, TitleLeft = 74;
            public const float MedalTop = 112, MedalSize = 46, ProgressLeft = 72;
            public const float ColumnsTop = 176, RowsTop = 204, IndexWidth = 40, ColumnGap = 10;
            public const float RowHeight = 72, RowGap = 8, FooterTop = 650, ActionTop = 682, ActionHeight = 66;
            public const int HeadingSize = 22, BodySize = 16, CaptionSize = 11;
        }
        internal static PortraitBmShape Shape(Transform parent, string name, float x, float y, float width, float height,
            PortraitBmShape.Form form, Color color, Color edge, float border = 0)
        {
            var rect = PortraitWidgets.Place(name, parent, x, y, width, height);
            var shape = rect.gameObject.AddComponent<PortraitBmShape>(); shape.Shape = form; shape.color = color;
            shape.Edge = edge; shape.Border = border; shape.raycastTarget = false; return shape;
        }
        static Text Strong(UiShell ui, Transform parent, string text, float x, float y, float width, float height,
            int size, Color color, bool centered = false)
        {
            var label = Text(ui, parent, text, x, y, width, height, size, color, centered);
            label.fontStyle = FontStyle.Bold; return label;
        }
        static void PassRewardIcon(UiShell ui, Transform parent, int row, bool paid, float x, float y)
        {
            if ((paid && (row == 0 || row == 4)) || (!paid && row == 2))
                Shape(parent, "Reward.Gem", x, y + 2, 24, 22, PortraitBmShape.Form.Gem,
                    new Color32(79, 163, 224, 255), new Color32(23, 63, 102, 255), 1.4f);
            else if (paid && row == 2)
                Shape(parent, "Reward.Iron", x, y + 5, 24, 18, PortraitBmShape.Form.Ingot,
                    new Color32(158, 164, 170, 255), new Color32(60, 64, 69, 255), 1.4f);
            else if (!paid && row == 3)
                Shape(parent, "Reward.Seal", x + 2, y + 3, 22, 22, PortraitBmShape.Form.Circle,
                    new Color32(163, 39, 30, 255), new Color32(94, 18, 13, 255), 1.4f);
            else Picture(ui, parent, !paid && row == 1 ? "r-timber" : "r-grain", x, y, 28, 28);
        }
        public static void Pass(UiShell ui, BmPassView view, Action back)
        {
            var root = Page(ui, "Pass", new Color32(26, 23, 18, 255)); var width = Width(ui);
            Shape(root, "Pass.Sky", 0, 0, width, Height(ui) * .6f, PortraitBmShape.Form.VerticalGradient,
                new Color32(35, 48, 79, 255), new Color32(26, 23, 18, 255));
            var backButton = Action(ui, root, "Pass.Back", "", back, PassBoard.Margin, PassBoard.BackTop,
                PassBoard.BackSize, PassBoard.BackSize);
            backButton.image.color = Color.clear;
            Shape(backButton.transform, "Back.Ring", 0, 0, PassBoard.BackSize, PassBoard.BackSize,
                PortraitBmShape.Form.Circle, new Color32(47, 42, 33, 255), new Color32(184, 135, 58, 255), 2);
            Strong(ui, backButton.transform, "‹", 0, -1, PassBoard.BackSize, PassBoard.BackSize, 28, Gold, true);
            Strong(ui, root, "시즌 패스", PassBoard.TitleLeft, 50, width - 90, 29, PassBoard.HeadingSize, Ivory);
            Text(ui, root, "[시즌 이름] · 남은 기간 [n]일", PassBoard.TitleLeft, 80, width - 90, 20, 12, Muted);
            Shape(root, "Pass.ProgressMedal", PassBoard.Margin, PassBoard.MedalTop, PassBoard.MedalSize,
                PassBoard.MedalSize, PortraitBmShape.Form.Circle, Gold, Gold);
            Strong(ui, root, "[n]", PassBoard.Margin, PassBoard.MedalTop, PassBoard.MedalSize, PassBoard.MedalSize, 20, PaperInk, true);
            Panel(ui, root, "Pass.Progress", PassBoard.ProgressLeft, 116, width - 88, 14, "ui.xp.rail");
            Text(ui, root, "판을 마칠 때마다 패스 경험치 · [n] / [n]", PassBoard.ProgressLeft, 135,
                width - 88, 24, PassBoard.CaptionSize, Muted);
            var firstColumn = PassBoard.Margin + PassBoard.IndexWidth + PassBoard.ColumnGap;
            var column = (width - 2 * PassBoard.Margin - PassBoard.IndexWidth - 2 * PassBoard.ColumnGap) * .5f;
            var secondColumn = firstColumn + column + PassBoard.ColumnGap;
            Strong(ui, root, "무료", firstColumn, PassBoard.ColumnsTop, column, 20, 13, Ivory, true);
            Strong(ui, root, "유료", secondColumn, PassBoard.ColumnsTop, column, 20, 13, Gold, true);
            for (var i = 0; i < 5; i++)
            {
                var y = PassBoard.RowsTop + i * (PassBoard.RowHeight + PassBoard.RowGap);
                Strong(ui, root, (i + 1).ToString(), PassBoard.Margin, y, PassBoard.IndexWidth,
                    PassBoard.RowHeight, PassBoard.BodySize, Gold, true);
                var free = Panel(ui, root, "Pass.Free:" + i, firstColumn, y, column, PassBoard.RowHeight, "ui.slot");
                var paid = Panel(ui, root, "Pass.Paid:" + i, secondColumn, y, column, PassBoard.RowHeight, "ui.slot");
                var freeText = view.FreeRewards != null && i < view.FreeRewards.Count ? view.FreeRewards[i] : "[n]";
                var paidText = view.PaidRewards != null && i < view.PaidRewards.Count ? view.PaidRewards[i] : "[n]";
                PassRewardIcon(ui, free, i, false, 22, 22);
                Strong(ui, free, freeText, 54, 15, column - 61, 42, PassBoard.BodySize, Ivory, true);
                if (i == 3)
                {
                    var art = PortraitWidgets.Place("Pass.CosmeticBackdrop", paid, 7, 7, column - 14, PassBoard.RowHeight - 14);
                    art.gameObject.AddComponent<Image>().color = new Color32(79, 53, 106, 255);
                    Picture(ui, paid, "hero", (column - 44) * .5f, 8, 44, 56);
                    Strong(ui, paid, "외형", 11, 47, 46, 18, 11, Ivory);
                }
                else
                {
                    PassRewardIcon(ui, paid, i, true, 22, 22);
                    Strong(ui, paid, paidText, 54, 15, column - 61, 42, PassBoard.BodySize, Ivory, true);
                }
            }
            Text(ui, root, "모든 보상은 고정 보상 · 판 안 힘은 없음", 16, PassBoard.FooterTop, width - 32, 24, 12, Muted, true);
            var purchase = Action(ui, root, "Pass.PaidPreview", "", null, 20, PassBoard.ActionTop, width - 40,
                PassBoard.ActionHeight, false, "ui.button.primary");
            var colors = purchase.colors; colors.disabledColor = Color.white; purchase.colors = colors;
            Strong(ui, purchase.transform, "유료 줄 열기", 28, 9, width - 96, 28, 20, Color.white, true);
            Text(ui, purchase.transform, "[가격 미정] · 보기 전용", 28, 37, width - 96, 20, 12, new Color32(216, 225, 245, 255), true);
        }
        static string Duration(long seconds)
        { seconds = Math.Max(0, seconds); return seconds / 3600 + "시간 " + (seconds / 60 % 60) + "분"; }
        public static void Idle(UiShell ui, BmIdleView view, Action claim, Action rewardedClaim, Action back)
        {
            var root = Page(ui, "Idle", new Color32(30, 39, 23, 255)); var width = Width(ui);
            var top = Mathf.Max(136, (Height(ui) - 844) * .5f + 170);
            var panel = Panel(ui, root, "Idle.GranaryPanel", 20, top, width - 40, 470);
            Picture(ui, panel, "b-granary", (width - 220) * .5f, -52, 180, 158);
            Text(ui, panel, "곡물창고 생산", 18, 112, width - 76, 38, 22, PaperInk, true);
            Text(ui, panel, "모인 시간 " + Duration(view.ElapsedSeconds) + " / 최대 " + Duration(view.CapacitySeconds), 18, 157, width - 76, 36, 13, PaperInk, true);
            var names = new[] { "곡물", "목재", "철재", "인장" };
            var icons = new[] { "r-grain", "r-timber", "r-iron", "r-seal" };
            var rewards = (view.RewardText ?? "").Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries);
            var cell = (width - 94) / 4;
            for (var i = 0; i < 4; i++)
            {
                var tile = PortraitWidgets.Place("Idle.Resource:" + i, panel, 18 + i * (cell + 6), 198, cell, 68);
                tile.gameObject.AddComponent<Image>().color = new Color32(234, 219, 182, 255);
                if (i < 2) Picture(ui, tile, icons[i], (cell - 28) * .5f, 7, 28, 28);
                else if (i == 2)
                    Shape(tile, "Resource.Iron", (cell - 26) * .5f, 12, 26, 18, PortraitBmShape.Form.Ingot,
                        new Color32(158, 164, 170, 255), new Color32(60, 64, 69, 255), 1.4f);
                else
                    Shape(tile, "Resource.Seal", (cell - 24) * .5f, 9, 24, 24, PortraitBmShape.Form.Circle,
                        new Color32(163, 39, 30, 255), new Color32(94, 18, 13, 255), 1.4f);
                var amount = "0";
                for (var j = 0; j < rewards.Length; j++)
                    if (rewards[j].StartsWith(names[i] + " ", StringComparison.Ordinal)) amount = rewards[j].Substring(names[i].Length + 1);
                Text(ui, tile, "+" + amount, 0, 37, cell, 26, 15, PaperInk, true);
            }
            Action(ui, panel, "Idle.Claim", "받기", claim, 18, 328, width - 76, 60, view.CanClaim, "ui.button.primary", 21);
            if (view.Ad != null && view.Ad.Visible)
            {
                var ad = OptionalAdButton(ui, panel, view.Ad, rewardedClaim, 18, 400, width - 76); ad.name = "Idle.OptionalAd";
            }
            PortraitRunPanels.RoundIcon(ui, root, "Idle.Back", "×", back, width - 68, top - 50);
        }
        public static void Revive(UiShell ui, BmReviveView view, Action result, Action rewardedRevive, Action retrySave = null)
        {
            var root = Page(ui, "Revive", new Color(0.04f, 0.035f, 0.03f, .94f)); var width = Width(ui);
            var shift = Mathf.Max(0, (Height(ui) - 844) * .5f);
            var hasAd = retrySave == null && view.Ad != null && view.Ad.Visible;
            var second = hasAd || retrySave != null;
            Text(ui, root, "쓰러졌다", 0, 190 + shift, width, 48, 34, new Color32(233, 201, 185, 255), true);
            Text(ui, root, view.RunCaption, 12, 240 + shift, width - 24, 36, 14, Muted, true);
            var panel = Panel(ui, root, "Revive.OfferPanel", 20, 300 + shift, width - 40, 300);
            Text(ui, panel, second ? "이번 판에 한 번, 다시 일어날 수 있다" : "이번 출정을 마칩니다", 18, 18, width - 76, 52, 20, PaperInk);
            Text(ui, panel, second ? "체력 [n]%로 일어나고 주변 적을 밀어낸다. 지금까지 모은 장비와 레벨은 그대로." : "지금까지의 결과로 결산합니다.", 18, 76, width - 76, 68, 14, PaperInk);
            Text(ui, panel, "그만두면 지금까지의 결과로 결산한다(사망 배율).", 18, 146, width - 76, 46, 13, PaperInk);
            var buttonWidth = second ? (width - 84) * .5f : width - 76;
            Action(ui, panel, "Revive.Result", "결산으로", result, 18, 222, buttonWidth, 60, true, "ui.button.secondary", 17);
            if (retrySave != null)
                Action(ui, panel, "Revive.RetrySave", "보상 저장\n다시 시도", retrySave, 26 + buttonWidth, 222, buttonWidth, 60, true, "ui.button.primary", 15);
            if (hasAd)
                Action(ui, panel, "Revive.OptionalAd", view.Ad.Enabled || string.IsNullOrEmpty(view.Ad.UnavailableReason) ? view.Ad.Label : view.Ad.UnavailableReason,
                    rewardedRevive, 26 + buttonWidth, 222, buttonWidth, 60, view.Ad.Enabled, "ui.button.primary", 15);
        }
        public static void FakeSettings(UiShell ui, BmFakeOutcome outcome, Action<BmFakeOutcome> change, Action back)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var root = Root(ui, "FakeAdSettings", "개발 광고 시험"); var width = Width(ui) - 36; var body = Body(ui, root, 82);
            var y = Label(ui, body, "실제 광고가 재생되지 않으며 결제되지 않습니다.", 0, 0, width) + 16;
            Button(ui, body, "FakeAd.Success", "성공" + (outcome == BmFakeOutcome.Success ? " · 현재 선택" : ""), () => change(BmFakeOutcome.Success), 0, y, width); y += 56;
            Button(ui, body, "FakeAd.Failure", "실패" + (outcome == BmFakeOutcome.Failure ? " · 현재 선택" : ""), () => change(BmFakeOutcome.Failure), 0, y, width); y += 56;
            End(body, y); Button(ui, root, "FakeAd.Back", "돌아가기", back, 18, Height(ui) - 62, width);
#else
            back?.Invoke();
#endif
        }
    }
    // Small vectors authored in the reference HTML (SVG icons, circles and CSS gradient).
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PortraitBmShape : MaskableGraphic
    {
        public enum Form { Circle, Gem, Ingot, VerticalGradient }
        public Form Shape;
        public Color Edge;
        public float Border;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect;
            if (Shape == Form.VerticalGradient)
            {
                mesh.AddVert(new Vector3(rect.xMin, rect.yMin), Edge, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMax, rect.yMin), Edge, Vector2.zero);
                mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(2, 3, 0); return;
            }
            var count = Shape == Form.Circle ? 64 : Shape == Form.Gem ? 5 : 4;
            Fan(mesh, rect, count, Border > 0 ? Edge : color);
            if (Border > 0) Fan(mesh, new Rect(rect.xMin + Border, rect.yMin + Border,
                Mathf.Max(0, rect.width - 2 * Border), Mathf.Max(0, rect.height - 2 * Border)), count, color);
        }
        void Fan(VertexHelper mesh, Rect rect, int count, Color tint)
        {
            var start = mesh.currentVertCount; mesh.AddVert(rect.center, tint, Vector2.zero);
            for (var i = 0; i < count; i++)
            {
                Vector2 point;
                if (Shape == Form.Circle)
                {
                    var angle = i * Mathf.PI * 2 / count;
                    point = rect.center + new Vector2(Mathf.Cos(angle) * rect.width * .5f, Mathf.Sin(angle) * rect.height * .5f);
                }
                else if (Shape == Form.Gem)
                {
                    var x = i == 0 ? .25f : i == 1 ? .75f : i == 2 ? 1 : i == 3 ? .5f : 0;
                    var y = i < 2 ? 1 : i == 3 ? 0 : .68f;
                    point = new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
                }
                else
                {
                    var x = i == 0 ? .15f : i == 1 ? .85f : i == 2 ? 1 : 0;
                    point = new Vector2(rect.xMin + x * rect.width, i < 2 ? rect.yMax : rect.yMin);
                }
                mesh.AddVert(point, tint, Vector2.zero);
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % count);
            }
        }
    }

}
