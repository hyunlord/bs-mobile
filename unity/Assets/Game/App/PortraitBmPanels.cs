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
        public BmReviveView(BmAdOffer ad) { Ad = ad; }
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
        static float Tiles(UiShell ui, Transform parent, string title, IReadOnlyList<string> entries,
            IReadOnlyList<string> roles, float width, float y)
        {
            y += Label(ui, parent, title, 0, y, width, 18) + 10;
            if (entries == null || entries.Count == 0)
                return y + Label(ui, parent, "준비 중 · [n]", 0, y, width) + 16;
            var cell = (width - 12) / 2;
            for (var index = 0; index < entries.Count; index += 2)
            {
                float rowHeight = 0;
                var cards = new RectTransform[Math.Min(2, entries.Count - index)];
                for (var column = 0; column < cards.Length; column++)
                {
                    var item = index + column;
                    var card = PortraitWidgets.Place(title + ".Tile:" + item, parent, column * (cell + 12), y, cell, 190);
                    cards[column] = card;
                    var paper = ui.Surface(card, "ui.hint"); paper.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
                    var role = roles != null && item < roles.Count ? roles[item] : null;
                    Art(ui, card, role, 12, 12, cell - 24, 104);
                    var cursor = 124f;
                    cursor += Label(ui, card, entries[item], 14, cursor, cell - 28, 16) + 4;
                    cursor += Label(ui, card, "가격 [n] · 보기 전용", 14, cursor, cell - 28) + 14;
                    rowHeight = Mathf.Max(rowHeight, cursor);
                }
                foreach (var card in cards) card.sizeDelta = new Vector2(cell, rowHeight);
                y += rowHeight + 12;
            }
            return y + 8;
        }
        public static Button OptionalAdButton(UiShell ui, Transform parent, BmAdOffer offer, Action action, float x, float y, float width)
        {
            if (offer == null || !offer.Visible) return null;
            // The host can provide an immediate-reward label for an owned ad-removal entitlement.
            return Button(ui, parent, "OptionalAd", offer.Enabled || string.IsNullOrEmpty(offer.UnavailableReason) ? offer.Label : offer.UnavailableReason,
                action, x, y, width, offer.Enabled);
        }
        public static void Shop(UiShell ui, BmShopView view, Action pass, Action adRemoval, Action back)
        {
            var root = Root(ui, "Shop", "상점"); var width = Width(ui) - 36; var body = Body(ui, root, 82); float y = 0;
            y += Label(ui, body, "보석 [n]", 0, y, width, 22) + 8;
            y += Label(ui, body, "보기 전용 · 실제 결제되지 않습니다.", 0, y, width) + 16;
            y = Tiles(ui, body, "외형 · 능력치는 달라지지 않습니다", view.CosmeticLabels, view.CosmeticArtRoles, width, y);
            y = Tiles(ui, body, "장원 테마", view.ThemeLabels, view.ThemeArtRoles, width, y);
            Button(ui, body, "Shop.Pass", "시즌 패스 보기", pass, 0, y, width); y += 60;
            y += Label(ui, body, "광고 제거 · " + (view.AdRemovalOwned ? "보유 중" : "가격 [n]"), 0, y, width, 18) + 4;
            Button(ui, body, "Shop.AdRemoval", "광고 제거 보기", adRemoval, 0, y, width); y += 64;
            y += Label(ui, body, "보석 묶음", 0, y, width, 18) + 8;
            for (var i = 0; i < 3; i++)
            {
                var row = PortraitWidgets.Place("Shop.GemBundle:" + i, body, 0, y, width, 74);
                var paper = ui.Surface(row, "ui.hint"); paper.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
                Label(ui, row, "보석 [n] · 가격 [n]\n준비 중", 16, 10, width - 32); y += 86;
            }
            End(body, y); Button(ui, root, "Shop.Back", "돌아가기", back, 18, Height(ui) - 62, width);
        }
        public static void Pass(UiShell ui, BmPassView view, Action back)
        {
            var root = Root(ui, "Pass", "시즌 패스"); var width = Width(ui) - 36; var body = Body(ui, root, 82); float y = 0;
            y += Label(ui, body, "고정 보상 · 판 안 힘은 없음", 0, y, width, 18) + 8;
            y += Label(ui, body, "가격 [n] · 보기 전용", 0, y, width) + 16;
            var column = (width - 12) / 2;
            Label(ui, body, "무료", 0, y, column, 18); Label(ui, body, "유료", column + 12, y, column, 18); y += 36;
            var freeCount = view.FreeRewards?.Count ?? 0; var paidCount = view.PaidRewards?.Count ?? 0;
            var rows = Math.Max(1, Math.Max(freeCount, paidCount));
            for (var i = 0; i < rows; i++)
            {
                var free = PortraitWidgets.Place("Pass.Free:" + i, body, 0, y, column, 60);
                var paid = PortraitWidgets.Place("Pass.Paid:" + i, body, column + 12, y, column, 60);
                var freePaper = ui.Surface(free, "ui.hint"); freePaper.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
                var paidPaper = ui.Surface(paid, "ui.hint"); paidPaper.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
                paidPaper.color = new Color32(246, 225, 174, 255);
                var left = Label(ui, free, i < freeCount ? view.FreeRewards[i] : "[n]", 14, 14, column - 28);
                var right = Label(ui, paid, i < paidCount ? view.PaidRewards[i] : "[n]", 14, 14, column - 28);
                var rowHeight = Mathf.Max(left, right) + 28;
                free.sizeDelta = paid.sizeDelta = new Vector2(column, rowHeight);
                y += rowHeight + 12;
            }
            End(body, y); Button(ui, root, "Pass.Back", "돌아가기", back, 18, Height(ui) - 62, width);
        }
        static string Duration(long seconds)
        { seconds = Math.Max(0, seconds); return seconds / 3600 + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00"); }
        public static void Idle(UiShell ui, BmIdleView view, Action claim, Action rewardedClaim, Action back)
        {
            var root = Root(ui, "Idle", "생산 상자"); var width = Width(ui) - 36;
            var hasAd = view.Ad != null && view.Ad.Visible; var body = Body(ui, root, hasAd ? 194 : 138); float y = 0;
            if (!string.IsNullOrEmpty(view.ArtRole)) { Art(ui, body, view.ArtRole, (width - 144) / 2, y, 144, 128); y += 144; }
            y += Label(ui, body, "모인 시간 · " + Duration(view.ElapsedSeconds), 0, y, width, 20) + 8;
            y += Label(ui, body, "보관 한도 · " + Duration(view.CapacitySeconds), 0, y, width) + 16;
            y += Label(ui, body, string.IsNullOrEmpty(view.RewardText) ? "보상 없음" : view.RewardText, 0, y, width, 18) + 12;
            End(body, y);
            Button(ui, root, "Idle.Claim", "받기", claim, 18, Height(ui) - (hasAd ? 174 : 118), width, view.CanClaim);
            if (hasAd) { var ad = OptionalAdButton(ui, root, view.Ad, rewardedClaim, 18, Height(ui) - 118, width); ad.name = "Idle.OptionalAd"; }
            Button(ui, root, "Idle.Back", "돌아가기", back, 18, Height(ui) - 62, width);
        }
        public static void Revive(UiShell ui, BmReviveView view, Action result, Action rewardedRevive)
        {
            var root = Root(ui, "Revive", "쓰러짐"); var width = Width(ui) - 36; var hasAd = view.Ad != null && view.Ad.Visible;
            var body = Body(ui, root, hasAd ? 138 : 82); End(body, Label(ui, body, hasAd ? "여기서 마치거나 다시 일어설 수 있습니다." : "이번 출정을 마칩니다.", 0, 0, width, 18));
            Button(ui, root, "Revive.Result", "결산으로", result, 18, Height(ui) - (hasAd ? 118 : 62), width);
            if (hasAd) { var ad = OptionalAdButton(ui, root, view.Ad, rewardedRevive, 18, Height(ui) - 62, width); ad.name = "Revive.OptionalAd"; }
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
}
