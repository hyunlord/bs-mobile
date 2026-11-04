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
        static float WorldMarchPanel(UiShell ui,RectTransform body,BarracksServerClient client,WorldMapTarget target,WorldMarchActions actions,float width,float y)
        {
            if(target?.Kind=="resource")
            {
                var targetCard=MarchArt(body,"target-card",0,y,width,112);
                Caption(ui,targetCard.transform,WorldTargetName(target),16,8,width-32,32,18,true);
                Caption(ui,targetCard.transform,"재고 확인 불가 · 예약 보장 없음",16,44,width-32,52,15,true);y+=124;
                var form=MarchArt(body,"march-formation-panel",0,y,width,100);float fy=16;var inner=width-32;
                Caption(ui,form.transform,"채집 편성",16,fy,inner,32,18,true);fy+=40;
                var heroes=client.RecruitView?.heroes;
                if(heroes==null||heroes.Count==0){Caption(ui,form.transform,"출정 가능한 영웅 정보를 확인하지 못했습니다.",16,fy,inner,64,16,true);fy+=72;}
                else foreach(var pair in heroes)
                {
                    var id=pair.Key;var busy=client.WorldMarches?.Any(m=>m.HeroId==id&&m.State!="returned")==true;
                    ActionButton(ui,form.transform,"Gather.Hero:"+id,AccountHeroName(id)+(busy?" · 행군 중":""),()=>actions.SelectHero(id),16,fy,inner,!client.Busy&&!busy,48,id==actions.Hero?"ui.button.primary":"ui.button.secondary");fy+=56;
                }
                var units=client.Latest?.Training?.units;
                if(units!=null)foreach(var unit in units)
                {
                    var id=unit.unitId;
                    ActionButton(ui,form.transform,"Gather.Unit:"+id,unit.name+" · 준비 "+unit.ready,()=>actions.SelectUnit(id),16,fy,inner,!client.Busy&&unit.ready>0,48,id==actions.Unit?"ui.button.primary":"ui.button.secondary");fy+=56;
                }
                var selected=units?.FirstOrDefault(u=>u.unitId==actions.Unit);
                ActionButton(ui,form.transform,"Gather.Less","−",()=>actions.ChangeCount(-1),16,fy,48,!client.Busy&&actions.Count>1);
                Caption(ui,form.transform,"편성 "+actions.Count,72,fy,inner-112,48,18,true,TextAnchor.MiddleCenter);
                ActionButton(ui,form.transform,"Gather.More","+",()=>actions.ChangeCount(1),width-64,fy,48,!client.Busy&&selected!=null&&actions.Count<selected.ready);fy+=56;
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
                var icon=march.State=="gathering"?"march-gathering":march.State=="outbound"?"march-moving":"march-returning";
                MarchArt(card.transform,icon,12,12,32,32);
                Caption(ui,card.transform,MarchStateName(march.State),52,8,width-68,32,18,true);
                Caption(ui,card.transform,AccountHeroName(march.HeroId)+" · "+string.Join(" / ",march.Troops.Select(t=>(client.Latest?.Training?.units?.FirstOrDefault(u=>u.unitId==t.unitId)?.name??"병력")+" "+t.count)),16,44,width-32,32,15,true);
                Caption(ui,card.transform,(march.State=="returned"?"귀환 시각 ":"다음 전환 시각 ")+MarchDue(march.DueAtMs),16,80,width-32,40,14,true);y+=height+8;
                if(march.State=="gathering"||march.State=="returning")
                {Caption(ui,body,"운반량 정보는 아직 제공되지 않습니다.",0,y,width,40,14);y+=48;}
                if(march.State=="outbound"||march.State=="gathering")
                {
                    var id=march.Id;
                    ActionButton(ui,body,"March.Recall:"+id,actions.Recall==id?"확인 · 회군 요청":"회군 요청",()=>actions.RecallMarch(id),0,y,width,!client.Busy&&!client.MarchUncertain&&!client.Uncertain);y+=56;
                }
            }
            ActionButton(ui,body,"March.Refresh","행군·병력·자원 새로 고침",actions.Refresh,0,y,width,!client.Busy);y+=56;
            if(client.Latest?.resources!=null)
            {
                Caption(ui,body,string.Join(" · ",client.Latest.resources.Select(r=>MaterialName(r.id)+" "+r.amount)),0,y,width,72,15);y+=80;
            }
            if(client.Latest?.held!=null&&client.Latest.held.Length>0)
            {Caption(ui,body,"보관함 · "+string.Join(" · ",client.Latest.held.Select(r=>MaterialName(r.id)+" "+r.amount)),0,y,width,64,15);y+=72;}
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
