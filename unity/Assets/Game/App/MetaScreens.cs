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
    public static class MetaScreens
    {
        public const string Glyphs = "변경 지도 장원 가신 도전 재료 보유 상한 우선 성장 자동 부족 최대 다음 출정 동행 해제 강화 승급 조각 잠김 연구 완료 진행 환산 초과 소멸 귀환 포기 생존 보스 처치 개척 재개 복구 곡물 목재 철재 인장 공격 체력 이동 경험치 아군 생산";
        static RectTransform Card(UiShell ui, Transform parent, string title, string role)
        {
            var card = UiShell.Rect(title, parent); ui.Surface(card, "ui.card.common");
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)UiTokens.CardInset,(int)UiTokens.CardInset,(int)UiTokens.CardInset,(int)UiTokens.CardInset);
            layout.spacing = UiTokens.Gap; layout.childControlHeight = true; layout.childForceExpandHeight = false; layout.childControlWidth = true; layout.childForceExpandWidth = true;
            var row = UiShell.Rect("Heading", card); var horizontal = row.gameObject.AddComponent<HorizontalLayoutGroup>(); horizontal.spacing = UiTokens.Gap; horizontal.childForceExpandHeight = false; horizontal.childForceExpandWidth = false;
            if (!string.IsNullOrEmpty(role)) { var icon = UiShell.Rect("Portrait",row); var size=icon.gameObject.AddComponent<LayoutElement>(); size.minWidth=size.preferredWidth=UiTokens.IconSize;size.minHeight=size.preferredHeight=UiTokens.IconSize;ui.Surface(icon,role).preserveAspect=true; }
            ui.Label(row,title,UiTokens.Heading,UiTokens.IconSize).GetComponent<LayoutElement>().flexibleWidth=1;
            return card;
        }
        public static string Amounts(MetaCatalog catalog, IReadOnlyDictionary<string,int> values) => string.Join(" · ",catalog.Materials.Where(m=>values.ContainsKey(m.Id)).Select(m=>m.Name+" "+values[m.Id]));
        public static void Wallet(UiShell ui, Transform panel, MetaCatalog catalog, MetaState state)
        {
            ui.Label(panel,string.Join(" · ",catalog.Materials.Select(m=>m.Name+" "+state.Wallet[m.Id]+"/"+m.WalletCap)),UiTokens.Small,UiTokens.TouchHeight);
        }
        public static void Chapters(UiShell ui, MetaCatalog catalog, MetaState state, Action<string> select, Action back)
        {
            var panel=ui.Panel("Chapters");ui.Label(panel,"변경 지도",UiTokens.Title,UiTokens.TouchHeight);ui.Button(panel,"장원으로",back);
            ui.Label(panel,"한 해를 끝까지 생존하고 보스를 처치하면 다음 변경이 열립니다.",UiTokens.Small,UiTokens.TouchHeight);
            foreach(var chapter in catalog.Chapters.OrderBy(c=>c.Index))
            {
                var open=chapter.Index<=state.HighestClearedChapter+1;var card=Card(ui,panel,chapter.Index+" · "+chapter.Name,chapter.Terrain.FirstOrDefault()?.ArtRole);
                ui.Label(card,chapter.Description,UiTokens.Body,UiTokens.TouchHeight);
                ui.Label(card,$"건설 부지 {chapter.SiteCount} · 위협 {chapter.ThreatPermille/10f:0.#}%",UiTokens.Small,UiTokens.Heading);
                ui.Label(card,"판당 환산 상한 · "+Amounts(catalog,chapter.RewardCaps),UiTokens.Small,UiTokens.TouchHeight);
                ui.Button(card,open?(chapter.Index<=state.HighestClearedChapter?"다시 출정":"출정"):"이전 변경을 완료하세요",()=>select(chapter.Id),open);
            }
        }
        public static void Manor(UiShell ui, MetaCatalog catalog, MetaState state, Action<string> priority, Action back, ISet<string> grown=null)
        {
            var panel=ui.Panel("Manor");ui.Label(panel,"장원",UiTokens.Title,UiTokens.TouchHeight);ui.Button(panel,"돌아가기",back);Wallet(ui,panel,catalog,state);
            ui.Label(panel,"재료가 모이면 자동으로 성장합니다. 깃발 하나로 먼저 키울 건물을 정하세요.",UiTokens.Body,UiTokens.TouchHeight);
            foreach(var building in catalog.ManorBuildings)
            {
                var level=state.ManorLevels[building.Id];var card=Card(ui,panel,building.Name+$" · {level}/{building.MaxLevel}",building.ArtRole);ui.Label(card,building.Description,UiTokens.Body,UiTokens.TouchHeight);
                if(grown!=null&&grown.Contains(building.Id)){card.Find("Heading/Portrait").gameObject.AddComponent<MetaGrowthPulse>();ui.Label(card,"자동 성장 완료",UiTokens.Small,UiTokens.Heading);}
                var cost=building.BaseCost.Keys.Union(building.CostPerLevel.Keys).ToDictionary(k=>k,k=>(building.BaseCost.TryGetValue(k,out var b)?b:0)+(building.CostPerLevel.TryGetValue(k,out var s)?s:0)*level);
                ui.Label(card,level>=building.MaxLevel?"최대 성장":"다음 성장 · "+Amounts(catalog,cost),UiTokens.Small,UiTokens.TouchHeight);
                if(level<building.MaxLevel)ui.Label(card,"부족 · "+Amounts(catalog,cost.ToDictionary(p=>p.Key,p=>Math.Max(0,p.Value-state.Wallet[p.Key]))),UiTokens.Caption,UiTokens.Heading);
                ui.Button(card,state.ManorPriority==building.Id?"우선 깃발 지정됨":"우선 깃발",()=>priority(building.Id),state.ManorPriority!=building.Id);
            }
        }
        static string Ability(MetaAbility ability) => ability switch { MetaAbility.Attack=>"공격",MetaAbility.Health=>"체력",MetaAbility.Movement=>"이동",MetaAbility.Growth=>"생산",MetaAbility.Allies=>"아군",MetaAbility.Experience=>"경험치",_=>throw new ArgumentOutOfRangeException(nameof(ability)) };
        public static void Vassals(UiShell ui, MetaCatalog catalog, MetaState state, Action<string> upgrade, Action<string> rank, Action<string> toggle, Action back)
        {
            var panel=ui.Panel("Vassals");ui.Label(panel,"가신",UiTokens.Title,UiTokens.TouchHeight);ui.Button(panel,"돌아가기",back);Wallet(ui,panel,catalog,state);
            int effect(ManorEffect kind)=>catalog.ManorBuildings.Where(b=>b.Effect==kind).Sum(b=>state.ManorLevels[b.Id]);
            var slots=Math.Min(catalog.Economy.MaximumVassalSlots,catalog.Economy.BaseVassalSlots+effect(ManorEffect.VassalSlots)/catalog.Economy.BarracksLevelsPerSlot);
            ui.Label(panel,$"동행 {state.ActiveVassalIds.Length}/{slots} · 동행한 가신의 능력만 출정에 적용됩니다.",UiTokens.Small,UiTokens.TouchHeight);
            foreach(var definition in catalog.Vassals)
            {
                var value=state.Vassals[definition.Id];var active=state.ActiveVassalIds.Contains(definition.Id);var card=Card(ui,panel,definition.Name,definition.ArtRole);ui.Label(card,definition.Description,UiTokens.Body,UiTokens.TouchHeight);
                if(!value.Unlocked){ui.Label(card,"잠김 · "+string.Join(" · ",catalog.Challenges.Where(c=>c.UnlockVassalIds.Contains(definition.Id)).Select(c=>c.Name)),UiTokens.Small,UiTokens.TouchHeight);continue;}
                var bonus=definition.BasePermille+(value.Level-1)*definition.PerLevelPermille+value.Rank*definition.PerRankPermille;
                ui.Label(card,$"레벨 {value.Level}/{definition.MaxLevel} · 승급 {value.Rank}/{definition.MaxRank} · {Ability(definition.Ability)} +{bonus/10f:0.#}%",UiTokens.Small,UiTokens.TouchHeight);
                ui.Button(card,active?"동행 해제":"동행",()=>toggle(definition.Id),active||state.ActiveVassalIds.Length<slots);
                var cap=Math.Min(definition.MaxLevel,catalog.Economy.BaseVassalLevelCap+effect(ManorEffect.VassalLevelCap)*catalog.Economy.LevelCapPerForgeLevel);
                var cost=definition.LevelCostBase.Keys.Union(definition.LevelCostStep.Keys).ToDictionary(k=>k,k=>(definition.LevelCostBase.TryGetValue(k,out var b)?b:0)+(definition.LevelCostStep.TryGetValue(k,out var s)?s:0)*(value.Level-1));
                ui.Label(card,"강화 비용 · "+Amounts(catalog,cost)+$" · 현재 상한 {cap}",UiTokens.Small,UiTokens.TouchHeight);
                ui.Button(card,"강화",()=>upgrade(definition.Id),value.Level<cap&&cost.All(p=>state.Wallet[p.Key]>=p.Value));
                var required=value.Rank<definition.MaxRank?definition.RankCosts[value.Rank]:0;
                ui.Label(card,$"조각 {value.Fragments}/{definition.FragmentCap} · 다음 승급 {required}",UiTokens.Small,UiTokens.Heading);
                ui.Button(card,"승급",()=>rank(definition.Id),value.Rank<definition.MaxRank&&value.Fragments>=required);
            }
        }
        public static void Challenges(UiShell ui, MetaCatalog catalog, MetaState state, IReadOnlyDictionary<string,ContentDisplay> displays, Action back)
        {
            var panel=ui.Panel("Challenges");ui.Label(panel,$"도전 · {state.CompletedChallenges.Length}/{catalog.Challenges.Length}",UiTokens.Title,UiTokens.TouchHeight);ui.Button(panel,"돌아가기",back);
            foreach(var challenge in catalog.Challenges)
            {
                var done=state.CompletedChallenges.Contains(challenge.Id);state.Metrics.TryGetValue(challenge.Metric.ToString(),out var progress);var card=Card(ui,panel,challenge.Name,null);
                ui.Label(card,done?"완료":$"진행 {Math.Min(progress,challenge.Target)}/{challenge.Target} · 연구 {challenge.ResearchLevel} 필요",UiTokens.Small,UiTokens.TouchHeight);
                ui.Label(card,"해금 · "+string.Join(" · ",challenge.UnlockContentIds.Select(id=>displays[id].DisplayName).Concat(challenge.UnlockVassalIds.Select(id=>catalog.Vassals.Single(v=>v.Id==id).Name))),UiTokens.Body,UiTokens.TouchHeight);
                ui.Label(card,"보상 · "+Amounts(catalog,challenge.Rewards),UiTokens.Small,UiTokens.TouchHeight);
            }
        }
        public static void Settlement(UiShell ui, Transform panel, MetaCatalog catalog, MetaSettlement settlement)
        {
            ui.Label(panel,settlement.Cleared?"변경 개척 완료":"다시 준비하는 계절",UiTokens.Heading,UiTokens.TouchHeight);
            ui.Label(panel,"이번 판 환산 · "+Amounts(catalog,settlement.Awarded),UiTokens.Body,UiTokens.TouchHeight);
            if(settlement.Overflow.Values.Any(v=>v>0))ui.Label(panel,"보유 상한 초과 · "+Amounts(catalog,settlement.Overflow),UiTokens.Small,UiTokens.TouchHeight);
            if(settlement.BuildingsGrown.Length>0)ui.Label(panel,"자동 성장 · "+string.Join(" · ",settlement.BuildingsGrown.Select(id=>catalog.ManorBuildings.Single(b=>b.Id==id).Name)),UiTokens.Small,UiTokens.TouchHeight);
            if(settlement.ChallengesCompleted.Length>0)ui.Label(panel,"도전 완료 · "+string.Join(" · ",settlement.ChallengesCompleted.Select(id=>catalog.Challenges.Single(c=>c.Id==id).Name)),UiTokens.Small,UiTokens.TouchHeight);
            Wallet(ui,panel,catalog,settlement.State);
        }
    }
}
