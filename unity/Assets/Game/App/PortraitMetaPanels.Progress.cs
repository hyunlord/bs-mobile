using System;
using System.Collections.Generic;
using System.Linq;
using Game.View;
using Game.App.Generated;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        public static void Vassals(UiShell ui, MetaCatalog catalog, MetaState state, string selectedId, Action<string> select,
            Action<string> requestUpgrade, Action<string> requestRank, Action<string> toggle, Action<PortraitMetaPage> navigate)
        {
            var root = Page(ui, "Vassals", "가신"); var width = Width(ui) - 36; var cell = (width - 16) / 3;
            // The six portraits stay above the scrolling detail so selection never disappears below it.
            for (var i = 0; i < catalog.Vassals.Length; i++)
            {
                var vassal = catalog.Vassals[i]; var x = 18 + i % 3 * (cell + 8); var y = 62 + i / 3 * 140;
                Art(ui, root, vassal.ArtRole, x, y, cell, 80);
                ActionButton(ui, root, "Vassals.Select:" + vassal.Id, vassal.Name + (state.Vassals[vassal.Id].Unlocked ? "" : " · 잠김"), () => select(vassal.Id), x, y + 82, cell, true, 52);
            }
            var body = Body(ui, root, 350, 82); var cursor = Wallet(ui, body, catalog, state, width, 0);
            var definition = catalog.Vassals.First(v => v.Id == selectedId); var value = state.Vassals[selectedId];
            cursor += Label(ui, body, definition.Name, 0, cursor, width, 22) + 8;
            cursor += Label(ui, body, "계정 도감 정보 · " + definition.Description, 0, cursor, width) + 8;
            cursor += Label(ui, body, "이번 출정에서는 가신 능력 보너스가 적용되지 않습니다.", 0, cursor, width) + 12;
            var cap = Math.Min(definition.MaxLevel, catalog.Economy.BaseVassalLevelCap + EffectLevel(catalog, state, ManorEffect.VassalLevelCap) * catalog.Economy.LevelCapPerForgeLevel);
            cursor += Label(ui, body, "레벨 " + value.Level + "/" + cap + " · 승급 " + value.Rank + "/" + definition.MaxRank, 0, cursor, width, 16) + 6;
            cursor += Label(ui, body, "조각 " + value.Fragments + "/" + definition.FragmentCap, 0, cursor, width) + 8;
            if (!value.Unlocked)
            {
                var unlock = catalog.Challenges.Where(c => c.UnlockVassalIds.Contains(selectedId)).Select(c => c.Name);
                cursor += Label(ui, body, "해금 조건 · " + string.Join(" · ", unlock), 0, cursor, width) + 8;
            }
            var costs = definition.LevelCostBase.Keys.Union(definition.LevelCostStep.Keys).ToDictionary(id => id,
                id => (definition.LevelCostBase.TryGetValue(id, out var basis) ? basis : 0) + (definition.LevelCostStep.TryGetValue(id, out var step) ? step : 0) * (value.Level - 1));
            cursor += Label(ui, body, "강화 비용 · " + MetaScreens.Amounts(catalog, costs), 0, cursor, width) + 8;
            var canUpgrade = value.Unlocked && value.Level < cap && costs.All(c => state.Wallet.TryGetValue(c.Key, out var amount) && amount >= c.Value);
            ActionButton(ui, body, "Vassals.Upgrade", "강화", () => requestUpgrade(selectedId), 0, cursor, width, canUpgrade); cursor += 56;
            var rankCost = value.Rank < definition.MaxRank ? definition.RankCosts[value.Rank] : 0;
            cursor += Label(ui, body, value.Rank < definition.MaxRank ? "승급 비용 · 조각 " + rankCost : "최대 승급", 0, cursor, width) + 8;
            ActionButton(ui, body, "Vassals.Rank", "승급", () => requestRank(selectedId), 0, cursor, width, value.Unlocked && value.Rank < definition.MaxRank && value.Fragments >= rankCost); cursor += 56;
            var active = state.ActiveVassalIds.Contains(selectedId);
            ActionButton(ui, body, "Vassals.Toggle", active ? "동행 해제" : "동행 기록에 선택", () => toggle(selectedId), 0, cursor, width, value.Unlocked && (active || state.ActiveVassalIds.Length < Slots(catalog, state))); cursor += 56;
            End(body, cursor); Tabs(ui, root, state, navigate);
        }

        public static void Challenges(UiShell ui, MetaCatalog catalog, MetaState state, Action<long> claim, Action<PortraitMetaPage> navigate,
            IReadOnlyDictionary<string, ContentDisplay> displays = null)
        {
            var root = Page(ui, "Challenges", "도전"); var width = Width(ui) - 36;
            Label(ui, root, "받은 보상 " + state.CompletedChallenges.Length + "/" + catalog.Challenges.Length, 18, 44, width);
            var body = Body(ui, root, 82); float y = 0;
            foreach (var challenge in catalog.Challenges)
            {
                var pending = state.PendingClaims.FirstOrDefault(c => c.Kind == MetaClaimKind.Challenge && c.SourceId == challenge.Id);
                var completed = state.CompletedChallenges.Contains(challenge.Id); state.Metrics.TryGetValue(challenge.Metric.ToString(), out var progress);
                y += Label(ui, body, challenge.Name, 0, y, width, 18) + 4;
                y += Label(ui, body, "진행 " + Math.Min(progress, challenge.Target) + "/" + challenge.Target + " · 연구 " + challenge.ResearchLevel, 0, y, width) + 4;
                y += Label(ui, body, "보상 · " + MetaScreens.Amounts(catalog, challenge.Rewards), 0, y, width) + 4;
                if (challenge.UnlockVassalIds.Length > 0)
                    y += Label(ui, body, "가신 해금 · " + string.Join(" · ", challenge.UnlockVassalIds.Select(id => catalog.Vassals.First(v => v.Id == id).Name)), 0, y, width) + 4;
                if (displays != null)
                {
                    var unlockNames = challenge.UnlockContentIds.Where(displays.ContainsKey).Select(id => displays[id].DisplayName).ToArray();
                    if (unlockNames.Length > 0)
                        y += Label(ui, body, "해금 · " + string.Join(" · ", unlockNames), 0, y, width) + 4;
                }
                var sequence = pending?.Sequence ?? 0;
                ActionButton(ui, body, "Challenges.Claim:" + challenge.Id, pending != null ? "받기" : completed ? "받음" : "진행 중", () => claim(sequence), 0, y, width, pending != null); y += 68;
            }
            End(body, y); Tabs(ui, root, state, navigate);
        }

        public static void Result(UiShell ui, MetaCatalog catalog, string chapterId, MetaRunFacts facts, MetaSettlement settlement,
            int tickRate, int level, IReadOnlyDictionary<string, int> fragmentDelta, Action retry, Action estate, BmAdOffer ad = null, Action rewardedResult = null, bool revived = false)
        {
            var cleared = settlement?.Cleared ?? (facts.Survived && facts.BossDefeated && facts.Tick >= facts.DurationTicks);
            var root = Page(ui, "Result", facts.Abandoned ? "포기" : cleared ? "클리어" : "쓰러짐"); var width = Width(ui) - 36;
            var hasAd = ad != null && ad.Visible;
            var body = Body(ui, root, 66, hasAd ? 196 : 140); var seconds = facts.Tick / Math.Max(1, tickRate); float y = 0;
            y += Label(ui, body, "버틴 시간 " + seconds / 60 + ":" + (seconds % 60).ToString("00") + " · 도달 레벨 " + facts.Level + "\n수확 " + facts.Harvests, 0, y, width, 18) + 16;
            if(revived) y += Label(ui, body, "부활한 출정", 0, y, width, 18) + 8;
            var chapter = catalog.Chapters.First(c => c.Id == chapterId);
            foreach (var material in catalog.Materials)
            {
                var award = 0; var overflow = 0; if (settlement != null) { settlement.Awarded.TryGetValue(material.Id, out award); settlement.Overflow.TryGetValue(material.Id, out overflow); } chapter.RewardCaps.TryGetValue(material.Id, out var cap);
                Art(ui, body, material.ArtRole, 0, y, 30, 30);
                y += Mathf.Max(36, Label(ui, body, material.Name + " +" + award + " · 판 상한 " + cap + "\n보관 초과 " + overflow, 40, y, width - 40)) + 8;
            }
            foreach (var delta in (fragmentDelta ?? new Dictionary<string, int>()).Where(d => d.Value > 0))
                y += Label(ui, body, catalog.Vassals.First(v => v.Id == delta.Key).Name + " · 조각 +" + delta.Value, 0, y, width, 16) + 8;
            foreach (var id in settlement?.BuildingsGrown ?? Array.Empty<string>())
                y += Label(ui, body, catalog.ManorBuildings.First(b => b.Id == id).Name + " · 성장", 0, y, width, 16) + 8;
            foreach (var id in settlement?.ChallengesCompleted ?? Array.Empty<string>())
                y += Label(ui, body, catalog.Challenges.First(c => c.Id == id).Name + " · 도전 보상을 받을 수 있습니다", 0, y, width) + 8;
            End(body, y);
            if(hasAd) { var button = PortraitBmPanels.OptionalAdButton(ui, root, ad, rewardedResult, 18, Height(ui) - 174, width); button.name = "Summary.OptionalAd"; }
            ActionButton(ui, root, "Summary.Retry", "다시 출정", retry, 18, Height(ui) - 118, width);
            ActionButton(ui, root, "Summary.Estate", "영지로", estate, 18, Height(ui) - 62, width);
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
