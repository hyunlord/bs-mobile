using System;
using System.Collections.Generic;
using System.Linq;
using Game.View;
using Game.App.Generated;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        public static void Vassals(UiShell ui, MetaCatalog catalog, MetaState state, string selectedId, Action<string> select,
            Action<string> requestUpgrade, Action<string> requestRank, Action<string> toggle, Action<PortraitMetaPage> navigate, VassalRunOptions preview = null)
        {
            var root = Page(ui, "Vassals", "가신"); var width = Width(ui) - 20;
            Caption(ui, root, "출전 " + state.ActiveVassalIds.Length + "/" + Slots(catalog, state) + "칸", Width(ui) - 140, 56, 120, 26, 12, false, TextAnchor.MiddleRight);
            var body = Body(ui, root, 92, 128); var detail = Plate(ui, body, "Vassal detail", "ui.panel", 0, 0, width - 16, 292);
            var definition = catalog.Vassals.First(v => v.Id == selectedId); var value = state.Vassals[selectedId];
            var portraitWidth = 126f; ColorPlate(detail, "Portrait backing", new Color32(51, 78, 126, 255), 14, 16, portraitWidth, 258);
            var selectedPortrait = Art(ui, detail, definition.ArtRole, 14, 109, portraitWidth, 165);
            if (!value.Unlocked) LockedPortrait(selectedPortrait);
            var textX = 150f; var textWidth = width - 182;
            Caption(ui, detail, definition.Name, textX, 12, textWidth, 28, 20, true);
            var active = state.ActiveVassalIds.Contains(selectedId);
            ActionButton(ui, detail, "Vassals.Toggle", active ? "출전 중 · 해제" : "동행 기록에 선택", () => toggle(selectedId), textX, 40, textWidth, value.Unlocked && (active || state.ActiveVassalIds.Length < Slots(catalog, state)), 44);
            Caption(ui, detail, definition.Description, textX, 86, textWidth, 40, 13, true);
            var cap = Math.Min(definition.MaxLevel, catalog.Economy.BaseVassalLevelCap + EffectLevel(catalog, state, ManorEffect.VassalLevelCap) * catalog.Economy.LevelCapPerForgeLevel);
            Caption(ui, detail, "레벨 " + value.Level + " / 상한 " + cap, textX, 126, textWidth, 24, 13, true);
            Caption(ui, detail, "승급 " + value.Rank + " / " + definition.MaxRank, textX, 149, textWidth, 23, 13, true);
            var costs = definition.LevelCostBase.Keys.Union(definition.LevelCostStep.Keys).ToDictionary(id => id,
                id => (definition.LevelCostBase.TryGetValue(id, out var basis) ? basis : 0) + (definition.LevelCostStep.TryGetValue(id, out var step) ? step : 0) * (value.Level - 1));
            var canUpgrade = value.Unlocked && value.Level < cap && costs.All(c => state.Wallet.TryGetValue(c.Key, out var amount) && amount >= c.Value);
            ActionButton(ui, detail, "Vassals.Upgrade", "레벨업", () => requestUpgrade(selectedId), textX, 178, textWidth, canUpgrade, 48, "ui.button.primary");
            var rankCost = value.Rank < definition.MaxRank ? definition.RankCosts[value.Rank] : 0;
            ActionButton(ui, detail, "Vassals.Rank", "승급 · 조각 " + value.Fragments + "/" + rankCost, () => requestRank(selectedId), textX, 232, textWidth, value.Unlocked && value.Rank < definition.MaxRank && value.Fragments >= rankCost, 44);
            var cell = (width - 32) / 3;
            for (var i = 0; i < catalog.Vassals.Length; i++)
            {
                var vassal = catalog.Vassals[i]; var x = i % 3 * (cell + 8); var y = 304 + i / 3 * 158;
                var card = Plate(ui, body, "Vassal card:" + vassal.Id, vassal.Id == selectedId ? "ui.card.rare" : "ui.card.common", x, y, cell, 150);
                var portrait = Art(ui, card, vassal.ArtRole, 8, 6, cell - 16, 120);
                var unlocked = state.Vassals[vassal.Id].Unlocked;
                if (!unlocked) LockedPortrait(portrait);
                ColorPlate(card, "Name backing", new Color32(35, 46, 70, 230), 7, 108, cell - 14, 34);
                Caption(ui, card, vassal.Name + (unlocked ? "" : "\n도전으로 해금"), 9, 107, cell - 18, 36, 11, false, TextAnchor.MiddleCenter);
                Hit(body, "Vassals.Select:" + vassal.Id, () => select(vassal.Id), x, y, cell, 150);
            }
            float cursor = 304 + Mathf.Ceil(catalog.Vassals.Length / 3f) * 158;
            cursor += Label(ui, body, "강화 비용 · " + MetaScreens.Amounts(catalog, costs), 0, cursor, width - 16) + 8;
            if (!value.Unlocked)
            {
                var unlock = catalog.Challenges.Where(c => c.UnlockVassalIds.Contains(selectedId)).Select(c => c.Name);
                cursor += Label(ui, body, "해금 조건 · " + string.Join(" · ", unlock), 0, cursor, width - 16) + 8;
            }
            if (preview == null) cursor += Label(ui, body, "이번 출정에서는 가신 능력 보너스가 적용되지 않습니다.", 0, cursor, width - 16, 11);
            if (preview != null)
            {
                cursor += Label(ui, body, "다음 출정에 적용 · 출정 시 편성 고정", 0, cursor, width - 16, 11);
                foreach (var effect in preview.Snapshot.Effects)
                {
                    var limit = preview.Rules.Effects.Single(r => r.Ability == effect.Ability).Cap;
                    var name = effect.Ability switch { MetaAbility.Attack => "공격", MetaAbility.Health => "체력", MetaAbility.Movement => "이동", MetaAbility.Growth => "생산", MetaAbility.Allies => "아군", MetaAbility.Experience => "경험치", _ => throw new ArgumentOutOfRangeException() };
                    cursor += Label(ui, body, name + " 합산 " + effect.Raw + "‰ → " + effect.Bonus + "‰ / 상한 " + limit + "‰" + (effect.Raw >= limit ? " · 상한 도달" : ""), 0, cursor, width - 16, 11);
                }
            }
            End(body, cursor); Tabs(ui, root, state, navigate);
        }

        public static void Challenges(UiShell ui, MetaCatalog catalog, MetaState state, Action<long> claim, Action<PortraitMetaPage> navigate,
            IReadOnlyDictionary<string, ContentDisplay> displays = null)
        {
            var root = Page(ui, "Challenges", "도전"); var width = Width(ui) - 36;
            Caption(ui, root, "달성 " + state.CompletedChallenges.Length + " / " + catalog.Challenges.Length, Width(ui) - 160, 56, 140, 26, 12, false, TextAnchor.MiddleRight);
            var body = Body(ui, root, 92, 128); float y = 0;
            foreach (var challenge in catalog.Challenges)
            {
                var pending = state.PendingClaims.FirstOrDefault(c => c.Kind == MetaClaimKind.Challenge && c.SourceId == challenge.Id);
                var completed = state.CompletedChallenges.Contains(challenge.Id); state.Metrics.TryGetValue(challenge.Metric.ToString(), out var progress);
                var panel = Plate(ui, body, "Challenge:" + challenge.Id, "ui.panel", 0, y, width, 108);
                var iconSlot = Plate(ui, panel, "Challenge icon", "ui.slot", 12, 12, 52, 52);
                var unlockVassal = challenge.UnlockVassalIds.FirstOrDefault();
                var unlockContent = challenge.UnlockContentIds.FirstOrDefault();
                if (unlockVassal != null) Art(ui, iconSlot, catalog.Vassals.First(v => v.Id == unlockVassal).ArtRole, 4, 3, 44, 46);
                else if (unlockContent != null) { var icon = ui.Icon(iconSlot, unlockContent, 40); PortraitWidgets.Position(icon.rectTransform, 6, 6, 40, 40); }
                Caption(ui, panel, challenge.Name, 74, 12, width - 154, 26, 17, true);
                Caption(ui, panel, "진행 " + Math.Min(progress, challenge.Target) + "/" + challenge.Target + " · 연구 " + challenge.ResearchLevel, 74, 39, width - 88, 24, 12, true);
                var sequence = pending?.Sequence ?? 0;
                if (pending != null)
                {
                    var emphasis = ColorPlate(panel, "Claim ready gold border", new Color32(218, 178, 75, 255), width - 69, 7, 60, 50);
                    emphasis.GetComponent<Image>().raycastTarget = false;
                }
                ActionButton(ui, panel, "Challenges.Claim:" + challenge.Id, pending != null ? "받기" : completed ? "받음" : "진행", () => claim(sequence), width - 66, 10, 54, pending != null, 44);
                if (!completed && pending == null)
                {
                    var railWidth = width - 110; Plate(ui, panel, "Progress rail", "ui.xp.rail", 14, 79, railWidth, 12);
                    var fill = challenge.Target > 0 ? Mathf.Clamp01((float)progress / challenge.Target) : 0;
                    if (fill > 0) Plate(ui, panel, "Progress fill", "ui.xp.fill", 14, 81, Mathf.Max(4, railWidth * fill), 8);
                    Caption(ui, panel, Math.Min(progress, challenge.Target) + "/" + challenge.Target, width - 89, 70, 75, 26, 12, true, TextAnchor.MiddleRight);
                }
                else Caption(ui, panel, "보상 · " + MetaScreens.Amounts(catalog, challenge.Rewards), 14, 73, width - 28, 26, 12, true);
                var extra = new List<string>();
                if (challenge.UnlockVassalIds.Length > 0) extra.Add("가신 해금 · " + string.Join(" · ", challenge.UnlockVassalIds.Select(id => catalog.Vassals.First(v => v.Id == id).Name)));
                if (displays != null) { var names = challenge.UnlockContentIds.Where(displays.ContainsKey).Select(id => displays[id].DisplayName).ToArray(); if (names.Length > 0) extra.Add("해금 · " + string.Join(" · ", names)); }
                var extraHeight = extra.Count > 0 ? Mathf.Max(24, extra.Count * 20) : 0;
                if (extraHeight > 0) Caption(ui, panel, string.Join("\n", extra), 14, 101, width - 28, extraHeight, 11, true);
                panel.sizeDelta = new Vector2(width, 108 + extraHeight); y += 116 + extraHeight;
            }
            End(body, y); Tabs(ui, root, state, navigate);
        }

        public static void Result(UiShell ui, MetaCatalog catalog, string chapterId, MetaRunFacts facts, MetaSettlement settlement,
            int tickRate, int level, IReadOnlyDictionary<string, int> fragmentDelta, Action retry, Action estate, BmAdOffer ad = null, Action rewardedResult = null, bool revived = false)
        {
            var cleared = settlement?.Cleared ?? (facts.Survived && facts.BossDefeated && facts.Tick >= facts.DurationTicks);
            var root = Page(ui, "Result", ""); var width = Width(ui) - 28;
            var chapter = catalog.Chapters.First(c => c.Id == chapterId); var hasAd = ad != null && ad.Visible;
            PortraitWidgets.Art(ui, root, "c-seed_crown", (Width(ui) - 92) / 2, 44, 92, 74);
            Caption(ui, root, facts.Abandoned ? "포기" : cleared ? "챕터 " + chapter.Index + " 클리어" : "쓰러짐", 14, 119, width, 40, 30, false, TextAnchor.MiddleCenter);
            Caption(ui, root, ChapterName(chapter) + (revived ? " · 부활한 출정" : ""), 14, 160, width, 24, 13, false, TextAnchor.MiddleCenter);
            var body = Body(ui, root, 196, hasAd ? 156 : 96); var contentWidth = Width(ui) - 36;
            var seconds = facts.Tick / Math.Max(1, tickRate); var cell = (contentWidth - 16) / 3;
            var names = new[] { "버틴 시간", "도달 레벨", "수확" }; var values = new[] { seconds / 60 + ":" + (seconds % 60).ToString("00"), facts.Level.ToString(), facts.Harvests.ToString() };
            for (var i = 0; i < 3; i++)
            {
                var summary = ColorPlate(body, "Summary " + names[i], new Color32(47, 42, 33, 255), i * (cell + 8), 0, cell, 58);
                Caption(ui, summary, names[i], 4, 4, cell - 8, 22, 11, false, TextAnchor.MiddleCenter);
                Caption(ui, summary, values[i], 4, 26, cell - 8, 27, 18, false, TextAnchor.MiddleCenter);
            }
            float y = 68; var materials = Plate(ui, body, "Settlement materials", "ui.panel", 0, y, contentWidth, 210); float row = 16;
            Caption(ui, materials, "받은 자재", 18, row, contentWidth - 36, 28, 17, true); row += 32;
            foreach (var material in catalog.Materials)
            {
                var award = 0; var overflow = 0; if (settlement != null) { settlement.Awarded.TryGetValue(material.Id, out award); settlement.Overflow.TryGetValue(material.Id, out overflow); } chapter.RewardCaps.TryGetValue(material.Id, out var cap);
                MaterialArt(ui, materials, material.Id, material.ArtRole, 18, row, 26);
                Caption(ui, materials, material.Name, 50, row, 68, 27, 14, true);
                Caption(ui, materials, "+" + award + " / 최대 " + cap, 116, row, contentWidth - 134, 27, 13, true, TextAnchor.MiddleRight); row += 29;
                if (overflow > 0) { Caption(ui, materials, "보관 초과 " + overflow, 50, row, contentWidth - 68, 22, 11, true); row += 24; }
            }
            foreach (var delta in (fragmentDelta ?? new Dictionary<string, int>()).Where(d => d.Value > 0))
            {
                Caption(ui, materials, catalog.Vassals.First(v => v.Id == delta.Key).Name + " · 조각 +" + delta.Value, 18, row, contentWidth - 36, 26, 13, true); row += 29;
            }
            materials.sizeDelta = new Vector2(contentWidth, row + 12); y += row + 24;
            var unlocked = new List<string>();
            foreach (var id in settlement?.BuildingsGrown ?? Array.Empty<string>()) unlocked.Add(catalog.ManorBuildings.First(b => b.Id == id).Name + " · 성장");
            foreach (var id in settlement?.ChallengesCompleted ?? Array.Empty<string>()) unlocked.Add(catalog.Challenges.First(c => c.Id == id).Name + " · 도전 보상을 받을 수 있습니다");
            var bandHeight = 40 + Mathf.Max(1, unlocked.Count) * 25;
            var band = ColorPlate(body, "New unlocks", new Color32(232, 192, 112, 255), 0, y, contentWidth, bandHeight);
            Caption(ui, band, "새로 열림", 14, 8, contentWidth - 28, 26, 16, true);
            Caption(ui, band, unlocked.Count == 0 ? "새로 열린 항목 없음" : string.Join("\n", unlocked), 14, 36, contentWidth - 28, bandHeight - 38, 13, true);
            End(body, y + bandHeight);
            if (hasAd) { var button = PortraitBmPanels.OptionalAdButton(ui, root, ad, rewardedResult, 14, Height(ui) - 150, width); button.name = "Summary.OptionalAd"; }
            var retryWidth = (width - 8) / 2.4f;
            ActionButton(ui, root, "Summary.Retry", "다시 출정", retry, 14, Height(ui) - 88, retryWidth, true, 60);
            ActionButton(ui, root, "Summary.Estate", "영지로", estate, 22 + retryWidth, Height(ui) - 88, width - retryWidth - 8, true, 60, "ui.button.destructive");
        }

        public static void SpendConfirmation(UiShell ui, string title, string detail, Action confirm, Action cancel, bool affordable)
        {
            var root = Page(ui, "SpendConfirmation", title); var width = Width(ui) - 36; var body = Body(ui, root, 66, 140);
            var y = Label(ui, body, detail, 0, 0, width, 16) + 12;
            y += Label(ui, body, "확인하면 표시된 비용이 차감됩니다.", 0, y, width); End(body, y);
            ActionButton(ui, root, "Spend.Confirm", affordable ? "확인" : "재료 부족", confirm, 18, Height(ui) - 118, width, affordable);
            ActionButton(ui, root, "Spend.Cancel", "취소", cancel, 18, Height(ui) - 62, width);
        }
    }
}
