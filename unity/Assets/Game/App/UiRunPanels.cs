using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.App.Generated;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;
namespace Game.App
{
    public static class UiRunPanels
    {
        public static void Cards(UiShell ui,RunFrame frame,FirstPlayableFrame firstPlayable,CardOfferView offer,ContentCatalog catalog,IReadOnlyDictionary<string,ContentDisplay> displays,Action<ReplayCommandKind,string> send,Action openSettings=null)
        {
            var fallow=CanonicalContent.IsWave&&catalog.WaveRuntime?.ChapterId=="meta:chapter_1";
            if(fallow&&Screen.width>Screen.height){LandscapeCards(ui,frame,firstPlayable,offer,catalog,displays,send,openSettings);return;}
            var panel=ui.Panel("Cards");ui.Label(panel,$"레벨 {frame.Level} · 성장 선택",UiTokens.Title,56);
            ui.Label(panel,"무기와 도구를 함께 키우세요",UiTokens.Small,36);
            var cards=UiShell.Rect("Three cards",panel);
            var wide=Screen.height/(float)Screen.width<1.35f;
            HorizontalOrVerticalLayoutGroup layout=wide?(HorizontalOrVerticalLayoutGroup)cards.gameObject.AddComponent<HorizontalLayoutGroup>():cards.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing=16;layout.childControlWidth=true;layout.childForceExpandWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;
            foreach(var id in offer.Cards)
            {
                var detail=firstPlayable.Cards.Single(c=>c.Id==id);var display=displays[id];
                var container=UiShell.Rect("Card "+id,cards);
                var stack=container.gameObject.AddComponent<VerticalLayoutGroup>();stack.spacing=8;stack.childControlWidth=true;stack.childForceExpandWidth=true;stack.childControlHeight=true;stack.childForceExpandHeight=false;
                var card=UiShell.Rect("Choose "+id,container);var surface=ui.Surface(card,"ui.card."+detail.Rarity);surface.raycastTarget=true;
                var choose=card.gameObject.AddComponent<Button>();choose.targetGraphic=surface;choose.onClick.AddListener(()=>send(ReplayCommandKind.ChooseCard,id));
                var vertical=card.gameObject.AddComponent<VerticalLayoutGroup>();vertical.padding=new RectOffset(wide?24:36,wide?24:36,36,36);vertical.spacing=8;vertical.childControlWidth=true;vertical.childForceExpandWidth=true;vertical.childControlHeight=true;vertical.childForceExpandHeight=false;
                var cardSize=container.gameObject.AddComponent<LayoutElement>();cardSize.flexibleWidth=1;
                if(wide){cardSize.minWidth=0;cardSize.preferredWidth=0;}
                var heading=UiShell.Rect("Card heading",card);var headingLayout=heading.gameObject.AddComponent<HorizontalLayoutGroup>();headingLayout.spacing=8;headingLayout.childControlWidth=true;headingLayout.childForceExpandWidth=false;headingLayout.childControlHeight=true;headingLayout.childForceExpandHeight=false;
                ui.Icon(heading,id,48);ui.Label(heading,display.DisplayName,UiTokens.Heading,48).gameObject.GetComponent<LayoutElement>().flexibleWidth=1;
                var levelText=catalog.WaveRuntime==null?Rarity(detail.Rarity)+$" · 레벨 {detail.CurrentLevel} → {detail.NextLevel}":catalog.WaveRuntime.Evolutions.ContainsKey(id)?"진화 선택":detail.CurrentLevel>0?$"보유 레벨 {detail.CurrentLevel}":"새 성장 선택";
                ui.Label(card,levelText+(offer.LockedCardId==id?" · 고정":""),UiTokens.Small,32);
                ui.Label(card,"기본 효과 · "+display.EffectDescription,UiTokens.Body,36);
                if(catalog.Tools.ContainsKey(id)&&!string.IsNullOrWhiteSpace(display.GrowthDescription))ui.Label(card,display.GrowthDescription,UiTokens.Small,32);
                var changes=catalog.WaveRuntime==null?UpgradeChanges(detail):WaveUpgradeChanges(detail,catalog.WaveRuntime,frame.TickRate);if(changes.Length>0)ui.Label(card,changes,UiTokens.Small,32);
                var clues=catalog.WaveRuntime==null?EvolutionClues(detail,firstPlayable,displays):WaveEvolutionClues(detail,firstPlayable,catalog.WaveRuntime,displays,fallow);if(clues.Length>0)ui.Label(card,clues,fallow?UiTokens.FallowEvolution:UiTokens.Caption,32);
                ui.Label(card,"카드를 눌러 선택",UiTokens.Caption,32);
                var actions=UiShell.Rect("Card actions",container);var row=actions.gameObject.AddComponent<HorizontalLayoutGroup>();row.spacing=4;row.childControlWidth=true;row.childForceExpandWidth=true;row.childControlHeight=true;row.childForceExpandHeight=false;actions.gameObject.AddComponent<LayoutElement>().minHeight=UiTokens.MinTouchHeight;
                var ban=ui.Button(actions,"금지",()=>send(ReplayCommandKind.BanCard,id),offer.Bans>0);
                var pin=ui.Button(actions,offer.LockedCardId==id?"고정됨":"고정",()=>send(ReplayCommandKind.LockCard,id),offer.Locks>0&&offer.LockedCardId!=id);
                if(fallow){FallowUiSurface.Button(ui,ban,true);FallowUiSurface.Button(ui,pin,true);}
                if(wide)foreach(Transform action in actions)
                {
                    var size=action.GetComponent<LayoutElement>();size.minWidth=UiTokens.MinTouchHeight;size.preferredWidth=UiTokens.MinTouchHeight;size.flexibleWidth=1;
                    action.GetComponentInChildren<Text>().horizontalOverflow=HorizontalWrapMode.Overflow;
                }
            }
            var reroll=ui.Button(panel,$"다시 뽑기 · {offer.Rerolls}",()=>send(ReplayCommandKind.RerollCards,null),offer.Rerolls>0);
            if(fallow)FallowUiSurface.Button(ui,reroll,true);
            ui.Label(panel,$"금지 {offer.Bans} · 고정 {offer.Locks} · 선택하면 계속됩니다",UiTokens.Small,40);
        }
        public static void LandscapeCards(UiShell ui,RunFrame frame,FirstPlayableFrame firstPlayable,CardOfferView offer,ContentCatalog catalog,IReadOnlyDictionary<string,ContentDisplay> displays,Action<ReplayCommandKind,string> send,Action openSettings=null)
        {
            var panel=UiShell.Rect("Cards",ui.Content);UiShell.Stretch(panel,12);ui.Surface(panel,"ui.panel").raycastTarget=true;
            var width=ui.SafeRoot.rect.width-24;var height=ui.SafeRoot.rect.height-24;
            var title=ui.Label(panel,$"레벨 {frame.Level} · 성장 선택",22,30);CardPosition(title.rectTransform,18,10,240,30);
            var hintRegion=UiShell.Rect("Card hint region",panel);CardPosition(hintRegion,270,8,width-378,32);
            if(openSettings!=null)
            {
                var settings=ui.Button(panel,"설정",openSettings);
                LandscapeCardButton(ui,settings,width-96,8,78,32);
            }
            var count=offer.Cards.Count;var gap=12f;var cell=(width-36-gap*(count-1))/Math.Max(1,count);
            var actionHeight=UiTokens.LandscapeActionHeight;var cardTop=48f;var footerHeight=52f;
            var cardHeight=height-cardTop-footerHeight-actionHeight-8;
            for(var i=0;i<count;i++)
            {
                var id=offer.Cards[i];var detail=firstPlayable.Cards.Single(c=>c.Id==id);var display=displays[id];var x=18+i*(cell+gap);
                var card=UiShell.Rect("Choose "+id,panel);CardPosition(card,x,cardTop,cell,cardHeight);
                var surface=ui.Surface(card,"ui.card."+detail.Rarity);surface.raycastTarget=true;surface.pixelsPerUnitMultiplier=UiTokens.FallowFrameMultiplier;
                var choose=card.gameObject.AddComponent<Button>();choose.targetGraphic=surface;choose.onClick.AddListener(()=>send(ReplayCommandKind.ChooseCard,id));
                var icon=ui.Icon(card,id,32);CardPosition(icon.rectTransform,14,12,32,32);
                var name=ui.Label(card,display.DisplayName,19,36);CardPosition(name.rectTransform,52,10,cell-66,36);
                var level=catalog.WaveRuntime.Evolutions.ContainsKey(id)?"진화 선택":detail.CurrentLevel>0?$"보유 레벨 {detail.CurrentLevel}":"새 성장 선택";
                var parts=new List<string>{level+(offer.LockedCardId==id?" · 고정":""),"기본 효과 · "+display.EffectDescription};
                if(catalog.Tools.ContainsKey(id)&&!string.IsNullOrWhiteSpace(display.GrowthDescription))parts.Add(display.GrowthDescription);
                var changes=WaveUpgradeChanges(detail,catalog.WaveRuntime,frame.TickRate);if(changes.Length>0)parts.Add(changes);
                var clues=WaveEvolutionClues(detail,firstPlayable,catalog.WaveRuntime,displays,true);if(clues.Length>0)parts.Add(clues);
                parts.Add("카드를 눌러 선택");
                var body=ui.Label(card,string.Join("\n\n",parts),UiTokens.LandscapeCardBody,cardHeight-64);body.name="Complete card text";body.alignment=TextAnchor.UpperLeft;body.lineSpacing=1.1f;
                CardPosition(body.rectTransform,14,52,cell-28,cardHeight-64);
                while(body.fontSize>UiTokens.LandscapeCardMinimum&&body.preferredHeight>body.rectTransform.rect.height)body.fontSize--;
                var ban=ui.Button(panel,"금지",()=>send(ReplayCommandKind.BanCard,id),offer.Bans>0);
                var pin=ui.Button(panel,offer.LockedCardId==id?"고정됨":"고정",()=>send(ReplayCommandKind.LockCard,id),offer.Locks>0&&offer.LockedCardId!=id);
                LandscapeCardButton(ui,ban,x,cardTop+cardHeight+6,(cell-6)*.5f,actionHeight);
                LandscapeCardButton(ui,pin,x+(cell+6)*.5f,cardTop+cardHeight+6,(cell-6)*.5f,actionHeight);
            }
            var reroll=ui.Button(panel,$"다시 뽑기 · {offer.Rerolls}",()=>send(ReplayCommandKind.RerollCards,null),offer.Rerolls>0);
            LandscapeCardButton(ui,reroll,width-210,height-40,192,32);
            var note=ui.Label(panel,$"금지 {offer.Bans} · 고정 {offer.Locks} · 선택하면 계속됩니다",14,32);CardPosition(note.rectTransform,18,height-40,width-240,32);
        }
        public static void LayoutHint(UiShell ui,Text hint,bool cards,bool landscape)
        {
            hint.fontSize=UiTokens.Body;hint.lineSpacing=1.25f;UiShell.Stretch(hint.rectTransform,UiTokens.Padding);
            var container=(RectTransform)hint.transform.parent;container.SetParent(ui.Content,false);container.gameObject.SetActive(true);
            container.GetComponent<Image>().pixelsPerUnitMultiplier=UiTokens.FramePixelsPerUnitMultiplier;
            container.anchorMin=container.anchorMax=new Vector2(.5f,.18f);container.pivot=new Vector2(.5f,.5f);
            container.anchoredPosition=Vector2.zero;container.sizeDelta=new Vector2(720,120);
            var layout=container.GetComponent<LayoutElement>();if(layout!=null)layout.enabled=false;
            if(!cards){if(landscape)PlaceLandscapeBattleHint(ui,hint);return;}
            if(PlaceLandscapeCardHint(ui,hint))return;
            var scroll=ui.Content.GetComponentInChildren<ScrollRect>();
            if(scroll==null)throw new InvalidOperationException("Card hint requires a reserved header or scroll layout.");
            container.SetParent(scroll.content,false);container.SetSiblingIndex(2);
            if(layout==null)layout=container.gameObject.AddComponent<LayoutElement>();layout.enabled=true;
            layout.minHeight=layout.preferredHeight=96;layout.flexibleHeight=0;
        }
        public static bool PlaceLandscapeBattleHint(UiShell ui,Text hint)
        {
            var hud=ui.Content.Find("HUD") as RectTransform;
            if(hud==null)return false;
            var container=(RectTransform)hint.transform.parent;container.SetParent(hud,false);
            container.anchorMin=container.anchorMax=new Vector2(.5f,0);container.pivot=new Vector2(.5f,0);
            container.anchoredPosition=Vector2.zero;container.sizeDelta=new Vector2(Mathf.Min(490,hud.rect.width*.56f),18);
            container.GetComponent<Image>().pixelsPerUnitMultiplier=UiTokens.FallowFrameMultiplier;
            UiShell.Stretch(hint.rectTransform,2);hint.fontSize=12;hint.lineSpacing=1;hint.alignment=TextAnchor.MiddleCenter;
            return true;
        }
        public static bool PlaceLandscapeCardHint(UiShell ui,Text hint)
        {
            var region=ui.Content.Find("Cards/Card hint region") as RectTransform;
            if(region==null)return false;
            var container=(RectTransform)hint.transform.parent;
            container.SetParent(region,false);UiShell.Stretch(container);container.pivot=new Vector2(.5f,.5f);
            var layout=container.GetComponent<LayoutElement>();if(layout!=null)layout.enabled=false;
            UiShell.Stretch(hint.rectTransform,3);hint.fontSize=14;hint.lineSpacing=1;hint.alignment=TextAnchor.MiddleCenter;
            return true;
        }
        static void LandscapeCardButton(UiShell ui,Button button,float x,float y,float width,float height)
        {
            FallowUiSurface.Button(ui,button,true);CardPosition((RectTransform)button.transform,x,y,width,height);
            var label=button.GetComponentInChildren<Text>();label.fontSize=16;label.lineSpacing=1;UiShell.Stretch(label.rectTransform,4);
        }
        static void CardPosition(RectTransform rect,float x,float y,float width,float height)
        {
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);
        }
        public static string UpgradeChanges(OfferedCardDetail detail)
        {
            var rows=CanonicalContent.UpgradeStats.Where(s=>s.Id==detail.Id).ToArray();var result=new List<string>();
            foreach(var stat in rows.Where(s=>s.PerLevel||s.Level==detail.NextLevel))
            {
                var before=stat.PerLevel?stat.Value*detail.CurrentLevel:rows.Where(s=>s.Label==stat.Label&&s.Level==detail.CurrentLevel).Select(s=>s.Value).FirstOrDefault();
                var after=stat.PerLevel?stat.Value*detail.NextLevel:stat.Value;
                if(before!=after)result.Add($"{stat.Label} {before}{stat.Unit} → {after}{stat.Unit}");
            }
            return string.Join(" · ",result);
        }
        public static string WaveUpgradeChanges(OfferedCardDetail detail,WaveRuntimeDefinition definition,int tickRate)
        {
            if(!definition.Gear.TryGetValue(detail.Id,out var gear)||gear.Levels==null||gear.Levels.Length==0||detail.CurrentLevel==0)return "";
            var before=gear.Levels[Math.Min(detail.CurrentLevel,gear.Levels.Length)-1];
            var after=gear.Levels[Math.Min(detail.CurrentLevel+1,gear.Levels.Length)-1];
            return WaveGrowthDescription(before,after);
        }
        public static string WaveGrowthDescription(WaveGearLevel before,WaveGearLevel after)
        {
            var values=new List<string>();
            void Change(int a,int b,string increase,string decrease){if(a!=b)values.Add(b>a?increase:decrease);}
            Change(before.Damage,after.Damage,"더 강하게 타격","피해 감소");
            Change(before.Range,after.Range,"더 넓게 공격","더 좁게 공격");
            Change(before.Count,after.Count,"한 번에 더 많이 타격","한 번에 적게 타격");
            Change(before.CooldownTicks,after.CooldownTicks,"공격 사이의 대기 증가","더 자주 공격");
            Change(before.Speed,after.Speed,"공격이 더 빠르게 이동","공격이 더 느리게 이동");
            Change(before.Knockback,after.Knockback,"더 세게 밀치기","밀치는 힘 감소");
            Change(before.LifetimeTicks,after.LifetimeTicks,"공격이 더 오래 유지","공격 유지 시간 감소");
            return values.Count==0?"성장표 상한":"다음 성장 · "+string.Join(" · ",values);
        }

        public static string WaveEvolutionClues(OfferedCardDetail card,FirstPlayableFrame frame,WaveRuntimeDefinition definition,IReadOnlyDictionary<string,ContentDisplay> displays,bool readable=false)
        {
            return string.Join("\n",frame.Evolutions.Where(e=>card.EvolutionIds.Contains(e.Id)).Select(e=>
            {
                var gate=definition.Evolutions[e.Id].Kind switch {WaveEvolutionKind.PlantingArc=>"밭 위의 적을 검으로 처치",WaveEvolutionKind.RepairOrbit=>"건물 수리 완료",WaveEvolutionKind.ShelteredPlot=>"건물 곁에서 수확",_=>throw new InvalidOperationException("Unknown evolution gate")};
                var separator=readable?"\n":" · ";
                return "진화 · "+displays[e.Id].DisplayName+" · "+(e.Activated?"완료":e.Available?"선택 가능":"조건")+separator+string.Join(" + ",e.Requirements.Select(r=>displays[r.EquipmentId].DisplayName))+separator+gate;
            }));
        }
        static string EvolutionClues(OfferedCardDetail card,FirstPlayableFrame frame,IReadOnlyDictionary<string,ContentDisplay> displays)
        {
            return string.Join("\n",frame.Evolutions.Where(e=>card.EvolutionIds.Contains(e.Id)).Select(e=>
                "진화 · "+displays[e.Id].DisplayName+" · "+(e.Activated?"완료":e.Available?"가능":"조건")+" · "+
                string.Join(" + ",e.Requirements.Select(r=>displays[r.EquipmentId].DisplayName+" "+r.MinimumLevel))+
                (e.GrowthRequirement==null?"":" · "+Growth(e.GrowthRequirement.Target)+" "+e.GrowthRequirement.Minimum)));
        }
        static string Growth(string target)=>target switch {"ripe"=>"익은 밭","harvests"=>"수확","buildings"=>"건물","people"=>"백성",_=>target};
        static string Rarity(string rarity)=>rarity switch {"common"=>"일반","rare"=>"희귀","epic"=>"영웅",_=>throw new InvalidOperationException("Unknown offered rarity: "+rarity)};
        public static string Ratio(long value,double total)=>total>0?(value/total*100).ToString("0",CultureInfo.InvariantCulture)+"%":"0%";
        public static void Summary(UiShell ui,RunSummary summary,RunFrame terminalFrame,FirstPlayableFrame terminalFirstPlayable,IReadOnlyDictionary<string,ContentDisplay> displays,Action retry,Action exit,bool abandoned=false,bool wave=false)
        {
            var panel=ui.Panel("Summary");ui.Label(panel,abandoned?"출정 포기":summary.Survived?"한 해 완료":"사망",UiTokens.Display,72);
            var survivedSeconds=summary.Tick/terminalFrame.TickRate;
            ui.Label(panel,$"생존 {survivedSeconds/60:00}:{survivedSeconds%60:00}",UiTokens.Body,40);
            ui.Label(panel,$"레벨 {summary.Level} · 처치 {terminalFirstPlayable.Kills} · 수확 {terminalFirstPlayable.Harvests}회",UiTokens.Heading,48);
            ui.Label(panel,terminalFirstPlayable.BossDefeated?"보스 격파":"보스 미격파",UiTokens.Small,36);
            double xp=summary.KillExperience+summary.HarvestExperience+summary.TaxExperience;
            ui.Label(panel,$"경험치 출처\n사냥 {Ratio(summary.KillExperience,xp)} · 수확 {Ratio(summary.HarvestExperience,xp)} · 세금 {Ratio(summary.TaxExperience,xp)}",UiTokens.Body,72);
            if(!wave)
            {
            var tool=summary.ToolActivationDamage+summary.ToolGrowthDamage;double damage=summary.WeaponDamage+tool+summary.AllyDamage;
            ui.Label(panel,$"피해 비중\n무기 {Ratio(summary.WeaponDamage,damage)} · 도구 {Ratio(tool,damage)} · 아군 {Ratio(summary.AllyDamage,damage)}",UiTokens.Body,72);
            }
            var actions=UiShell.Rect("Summary actions",panel);var buttons=actions.gameObject.AddComponent<HorizontalLayoutGroup>();buttons.spacing=16;buttons.childControlWidth=true;buttons.childForceExpandWidth=true;buttons.childControlHeight=true;buttons.childForceExpandHeight=false;
            ui.Button(actions,"다시 하기",retry);ui.Button(actions,"나가기",exit);
            ui.Label(panel,"이번 판 빌드",UiTokens.Heading,48);
            var build=UiShell.Rect("Build icons",panel);var grid=build.gameObject.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(88,104);grid.spacing=Vector2.one*8;grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=7;
            var ids=terminalFrame.Equipment.Select(e=>e.Id).Concat(terminalFirstPlayable.Charters).Concat(terminalFirstPlayable.Items.Keys).Concat(terminalFirstPlayable.Evolutions.Where(e=>e.Activated).Select(e=>e.Id)).Distinct().ToArray();
            build.gameObject.AddComponent<LayoutElement>().preferredHeight=Mathf.Ceil(ids.Length/7f)*112;
            foreach(var id in ids)
            {
                var slot=UiShell.Rect("Build "+id,build);ui.Surface(slot,"ui.slot");var image=ui.Icon(slot,id,64);image.rectTransform.anchoredPosition=new Vector2(0,12);
                var rank=terminalFrame.Equipment.FirstOrDefault(e=>e.Id==id)?.Level;
                if(rank.HasValue){var text=ui.Label(slot,rank.Value.ToString(CultureInfo.InvariantCulture),UiTokens.Caption,24);text.alignment=TextAnchor.MiddleCenter;text.rectTransform.anchorMin=new Vector2(0,0);text.rectTransform.anchorMax=new Vector2(1,0);text.rectTransform.pivot=new Vector2(.5f,0);text.rectTransform.anchoredPosition=new Vector2(0,4);text.rectTransform.sizeDelta=new Vector2(0,24);}
            }

        }
    }
}
