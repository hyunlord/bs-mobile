using System;
using System.Linq;
using Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public sealed class WorldMarchActions
    {
        public string Hero, Unit, Recall;
        public int Count;
        public bool Confirm;
        public Action<string> SelectHero, SelectUnit, RecallMarch;
        public Action<int> ChangeCount;
        public Action Gather, Retry, Refresh, Shield, Training;
    }
    public static partial class PortraitMetaPanels
    {
        public static string MarchStateName(string state) => state == "outbound" ? "이동 중" : state == "gathering" ? "채집 중" : state == "returning" ? "귀환 중" : state == "returned" ? "귀환 확정" : "상태 확인 필요";
        internal static string MarchStateArt(string state) => state == "outbound" ? "march-moving" : state == "gathering" ? "march-gathering" : state == "returning" || state == "returned" ? "march-returning" : null;
        internal static string MarchProgressLabel(string state) => state == "returned" ? "귀환 완료" : "진행률 미확인";
        internal static string MarchTroopArt(string unit) => unit == "slg:levy" ? "march-troop-row-levy" : unit == "slg:guard" ? "march-troop-row-guard" : null;
        public static string MarchDue(long milliseconds)
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime().ToString("MM-dd HH:mm:ss"); }
            catch (ArgumentOutOfRangeException) { return "시각 확인 필요"; }
        }
        static Image MarchArt(Transform parent,string id,float x,float y,float width,float height)
        {
            var image=PortraitWidgets.Place("MarchArt."+id,parent,x,y,width,height).gameObject.AddComponent<Image>();
            ChatMarchArt.Surface(image,id);return image;
        }
        internal static float WorldTargetPanel(UiShell ui, RectTransform body, WorldMapChunk chunk, int selectedX, int selectedY, float width, float y)
        {
            var selected = chunk.Tile(selectedX, selectedY);
            if (selected != null)
            {
                Caption(ui, body, WorldMapResponse.Coordinate(selectedX, selectedY) + " · " + WorldTerrainName(selected.Terrain) + " · " + (selected.Passable ? "통과 가능" : "통과 불가"), 0, y, width, 48, 16); y += 52;
                var target = chunk.Target(selectedX, selectedY);
                if (target != null)
                {
                    var card = MarchArt(body, "target-card", 0, y, width, 164);
                    var header = target.Kind == "resource" ? "target-header-resource" : target.Kind == "monster" ? "target-header-monster" : target.Kind == "stronghold" ? "target-header-stronghold" : null;
                    var headerHeight = width * 116 / 700;
                    if (header != null) MarchArt(card.transform, header, 0, 0, width, headerHeight);
                    Caption(ui, card.transform, WorldTargetName(target), 16, headerHeight + 4, width - 32, 32, 18, true);
                    var detail = target.Kind == "resource" ? "재고 미확인 · 도착 시 다시 확인" : string.IsNullOrEmpty(target.AllianceName) ? "대상 정보 확인" : "연맹 " + target.AllianceName;
                    Caption(ui, card.transform, detail, 16, headerHeight + 40, width - 32, 64, 15, true);
                    card.rectTransform.sizeDelta = new Vector2(width, headerHeight + 116);
                    y += headerHeight + 128;
                }
            }
            return y;
        }
        static float WorldMarchPanel(UiShell ui,RectTransform body,BarracksServerClient client,WorldMapTarget target,WorldMarchActions actions,float width,float y)
        {
            if(target?.Kind=="resource")
            {
                var form=MarchArt(body,"march-formation-panel",0,y,width,100);float fy=16;var inner=width-32;
                Caption(ui,form.transform,"채집 편성",16,fy,inner,32,18,true);fy+=40;
                var heroes=client.RecruitView?.heroes;
                if(heroes==null||heroes.Count==0){Caption(ui,form.transform,"출정 가능한 영웅 정보를 확인하지 못했습니다.",16,fy,inner,64,16,true);fy+=72;}
                else foreach(var pair in heroes)
                {
                    var id=pair.Key;var busy=client.WorldMarches?.Any(m=>m.HeroId==id&&m.State!="returned")==true;
                    MarchArt(form.transform,"march-hero-slot",16,fy,72,84);
                    if (id == "slg:field_guide" || id == "slg:river_warden" || id == "slg:ruin_seeker")
                    {
                        var portrait = AccountUiArt.Place(form.transform,"recruit",id+".portrait.approved-native",22,fy+6,60,70);
                        portrait.preserveAspect = true;
                    }
                    Caption(ui,form.transform,AccountHeroName(id)+(busy?" · 행군 중":""),100,fy,inner-84,32,15,true);
                    ActionButton(ui,form.transform,"Gather.Hero:"+id,id==actions.Hero?"선택됨":"영웅 선택",()=>actions.SelectHero(id),100,fy+36,inner-84,!client.Busy&&!busy,48,id==actions.Hero?"ui.button.primary":"ui.button.secondary");fy+=96;
                }
                var units=client.Latest?.Training?.units;
                if(units!=null)foreach(var unit in units)
                {
                    var id=unit.unitId;
                    var troopArt = MarchTroopArt(id);
                    if (troopArt != null)
                    {
                        var rowHeight = inner * 128 / 700;
                        var row = MarchArt(form.transform,troopArt,16,fy,inner,rowHeight);
                        var chosen = id == actions.Unit;
                        Caption(ui,row.transform,unit.name,48,0,inner*.39f,rowHeight,15,true,TextAnchor.MiddleLeft);
                        Caption(ui,row.transform,chosen?actions.Count.ToString():"",inner*.70f,0,inner*.12f,rowHeight,15,true,TextAnchor.MiddleCenter);
                        Hit(row.transform,"Gather.Less:"+id,()=>actions.ChangeCount(-1),inner*.56f,0,48,Math.Max(48,rowHeight),chosen&&!client.Busy&&actions.Count>1);
                        Hit(row.transform,"Gather.More:"+id,()=>actions.ChangeCount(1),inner*.83f,0,48,Math.Max(48,rowHeight),chosen&&!client.Busy&&actions.Count<unit.ready);
                        fy += Math.Max(48,rowHeight) + 4;
                    }
                    ActionButton(ui,form.transform,"Gather.Unit:"+id,unit.name+" · 준비 "+unit.ready,()=>actions.SelectUnit(id),16,fy,inner,!client.Busy&&unit.ready>0,48,id==actions.Unit?"ui.button.primary":"ui.button.secondary");fy+=56;
                }
                var selected=units?.FirstOrDefault(u=>u.unitId==actions.Unit);
                if (selected != null && MarchTroopArt(selected.unitId) == null)
                {
                    ActionButton(ui,form.transform,"Gather.Less","−",()=>actions.ChangeCount(-1),16,fy,48,!client.Busy&&actions.Count>1);
                    Caption(ui,form.transform,"편성 "+actions.Count,72,fy,inner-112,48,18,true,TextAnchor.MiddleCenter);
                    ActionButton(ui,form.transform,"Gather.More","+",()=>actions.ChangeCount(1),width-64,fy,48,!client.Busy&&actions.Count<selected.ready);fy+=56;
                }
                Caption(ui,form.transform,"이동 시간·편성 한도·적재량은 아직 확인할 수 없습니다. 도착 시 사용 가능 여부를 다시 확인합니다.",16,fy,inner,88,15,true);fy+=96;
                var valid=!client.Busy&&!client.Uncertain&&!client.MarchUncertain&&client.AccountReady&&actions.Hero!=null&&selected!=null&&selected.ready>=actions.Count
                    &&client.WorldMarches!=null&&!client.WorldMarches.Any(m=>m.HeroId==actions.Hero&&m.State!="returned");
                ActionButton(ui,form.transform,"Gather.Submit",actions.Confirm?"확인 · 채집 행군 발령":"채집 행군 준비",actions.Gather,16,fy,inner,valid,48,"ui.button.primary");fy+=64;
                form.rectTransform.sizeDelta=new Vector2(width,fy);y+=fy+12;
            }
            if(client.MarchUncertain)
            {
                Caption(ui,body,"행군 요청 결과가 확인되지 않았습니다. 같은 요청으로 확인합니다.",0,y,width,64,16);y+=72;
                ActionButton(ui,body,"March.Retry","같은 요청 결과 확인",actions.Retry,0,y,width,!client.Busy);y+=56;
            }
            if(client.WorldMarchResult!=null)
            {
                Caption(ui,body,client.WorldMarchResult.Done?"행군 명령이 확정되었습니다.":"행군 명령이 거절되었습니다.",0,y,width,48,16);y+=56;
            }
            Caption(ui,body,"내 행군",0,y,width,32,18);y+=40;
            if(client.WorldMarches==null){Caption(ui,body,client.WorldMarchStatus == "not-loaded" ? "행군 정보를 확인해 주세요." : "행군 조회 미확인 · 서버 지원과 연결 상태를 확인하세요.",0,y,width,64,16);y+=72;}
            else if(client.WorldMarches.Length==0){Caption(ui,body,"진행한 행군이 없습니다.",0,y,width,40,16);y+=48;}
            else foreach(var march in client.WorldMarches)
            {
                var height=width*264/700;
                var card=MarchArt(body,"march-list-card",0,y,width,height);
                var icon=MarchStateArt(march.State);
                if (icon != null) MarchArt(card.transform,icon,12,12,32,32);
                Caption(ui,card.transform,MarchStateName(march.State),52,8,width-68,32,18,true);
                Caption(ui,card.transform,AccountHeroName(march.HeroId)+" · "+string.Join(" / ",march.Troops.Select(t=>(client.Latest?.Training?.units?.FirstOrDefault(u=>u.unitId==t.unitId)?.name??"병력")+" "+t.count)),16,44,width-32,32,15,true);
                y+=height+4;
                var timeHeight = width * 88 / 700;
                var timeRow = MarchArt(body,"march-time-row",0,y,width,timeHeight);
                Caption(ui,timeRow.transform,(march.State=="returned"?"귀환 시각 ":"다음 전환 시각 ")+MarchDue(march.DueAtMs),48,0,width-64,timeHeight,14,true,TextAnchor.MiddleLeft);y+=timeHeight+4;
                var progressWidth = width - 48; var progressHeight = progressWidth * 20 / 600;
                MarchArt(body,"march-progress-track",24,y,progressWidth,progressHeight);
                // #704: a returned server state confirms completion; other stages lack a phase start and server observation time.
                if (march.State == "returned") MarchArt(body,"march-progress-fill",24,y,progressWidth,progressHeight);
                y += progressHeight + 4;
                Caption(ui,body,MarchProgressLabel(march.State),0,y,width,32,14);y+=36;
                if(march.State=="gathering"||march.State=="returning")
                {Caption(ui,body,"운반량 정보는 아직 제공되지 않습니다.",0,y,width,40,14);y+=48;}
                if(march.State=="outbound"||march.State=="gathering")
                {
                    var id=march.Id;
                    ActionButton(ui,body,"March.Recall:"+id,actions.Recall==id?"확인 · 회군 요청":"회군 요청",()=>actions.RecallMarch(id),0,y,width,!client.Busy&&!client.MarchUncertain&&!client.Uncertain);y+=56;
                }
            }
            ActionButton(ui,body,"March.Refresh","행군·병력·자원 새로 고침",actions.Refresh,0,y,width,!client.Busy);y+=56;
            y = WorldWalletSummary(ui, body, client, width, y);
            ActionButton(ui,body,"World.Training","병영 · 병력 확인",actions.Training,0,y,width,!client.Busy);y+=56;
            ActionButton(ui,body,"World.Shield","보호막 상태 확인",actions.Shield,0,y,width,!client.Busy);y+=56;
            if(client.WorldShield!=null)
            {
                var shield=client.WorldShield;
                Caption(ui,body,shield.ShieldUntilMs>shield.UtcHighWatermark?"보호막 종료 "+MarchDue(shield.ShieldUntilMs):"확인 시점에 보호막 없음",0,y,width,48,16);y+=56;
                Caption(ui,body,"서버 확인 "+MarchDue(shield.UtcHighWatermark),0,y,width,36,14);y+=44;
            }
            else if(client.WorldShieldStatus!="not-loaded")
            {Caption(ui,body,"보호막 상태를 확인하지 못했습니다.",0,y,width,48,16);y+=56;}
            return y;
        }
    }
}
