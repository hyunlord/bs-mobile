using System;
using System.Globalization;
using System.Linq;
using Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        public const string AccountDomainGlyphs = "가각간강같개건결경고공과관광교구군귀근기길김까뀌남내농능니다답도돌동되득량련로록료류률를먼면모목미바반밭버범별보복본부분비사상새서석선세소속수순습시실아않았엄었에연열영완요용운웅원유은을음응의이인일입자작잠잡장재저적전정제조족존주중지직진집천청초최취측침카탐태택터패폐풀필하한합했행허현협호화확환회획횟효훈희";
        public static string AccountHeroName(string id) => id=="slg:field_guide"?"농로 길잡이":id=="slg:river_warden"?"강가 수호자":id=="slg:ruin_seeker"?"폐허 탐구자":id;
        static string AccountRarity(string rarity) => rarity=="common"?"일반":rarity=="rare"?"희귀":rarity=="epic"?"서사":rarity;
        public static string AccountOdds(double probability) => (probability*100).ToString("0.####",CultureInfo.InvariantCulture)+"%";
        public static string AccountResearchCost(ResearchAccountNode node) => string.Join(" · ",node.cost.Select(x=>MaterialName(x.Key)+" "+x.Value));
        public static string AccountResearchStatus(string value) => value=="locked"?"잠김":value=="active"?"진행 중":value=="completed"?"완료":value=="available"?"시작 가능":value;
        public static void AccountConnection(UiShell ui,string status,bool busy,Action reconnect,Action back)
        {
            var root=Page(ui,"AccountDomainConnection","");ui.ApplySurface(root.GetComponent<Image>(),"ui.panel");
            var w=Width(ui);Caption(ui,root,"서버 계정 연결",20,40,w-40,48,22,true);
            Caption(ui,root,AccountCommandStatus(status),20,100,w-40,96,16,true);
            ActionButton(ui,root,"AccountDomain.Connect","다시 연결",reconnect,20,212,w-40,!busy);
            ActionButton(ui,root,"AccountDomain.Back","돌아가기",back,20,Height(ui)-60,w-40,!busy);
        }
        static RectTransform AccountHeader(UiShell ui,string name,string title,BarracksServerClient client,Action back)
        {
            var root=Page(ui,name,"");ui.ApplySurface(root.GetComponent<Image>(),"ui.panel");var w=Width(ui);
            var bar=ColorPlate(root,name+".Header",BarracksDark,0,0,w,52);
            Caption(ui,bar,title,16,8,w-80,36,22).fontStyle=FontStyle.Bold;
            ActionButton(ui,bar,name+".Back","‹",back,w-56,2,48,!client.Busy);
            BarracksWallet(ui,root,client.Latest.resources,w);
            var status=Caption(ui,root,AccountCommandStatus(client.Status),20,98,w-40,52,14,true);status.name="Account.Status";return root;
        }
        public static void RecruitAccount(UiShell ui,BarracksServerClient client,bool odds,bool confirm,Action showOdds,Action ask,Action submit,Action cancel,Action refresh,Action back)
        {
            var state=client.RecruitView;var root=AccountHeader(ui,"Recruit","영웅 모집",client,back);var w=Width(ui);var inner=w-40;
            var body=Body(ui,root,158,78);float y=0;
            if(state==null){Caption(ui,body,"모집 서버 응답 확인 필요",0,0,inner,60,16,true);End(body,80);return;}
            var pity=AccountUiArt.Place(body,"recruit","recruit-pity-panel",0,y,inner,88);
            Caption(ui,pity.transform,"연속 "+AccountRarity(state.guaranteedRarity)+" 미획득 "+state.pity,14,8,inner-28,28,17,true);
            Caption(ui,pity.transform,"보장까지 "+state.drawsUntilGuarantee+"회 · 중복도 초기화",14,40,inner-28,36,14,true);y+=100;
            if(odds)
            {
                Caption(ui,body,"영웅별 모집 확률 · 기본 / 다음",0,y,inner,32,18,true);y+=40;
                foreach(var entry in state.baseOdds)
                {
                    var next=state.next.FirstOrDefault(x=>x.heroId==entry.heroId);
                    Caption(ui,body,AccountHeroName(entry.heroId)+" · "+AccountRarity(entry.rarity),0,y,inner,26,16,true);y+=28;
                    Caption(ui,body,AccountOdds(entry.probability)+" / "+(next==null?"—":AccountOdds(next.probability)),0,y,inner,26,16,true);y+=32;
                }
                foreach(var duplicate in state.duplicateShards)
                {
                    Caption(ui,body,AccountRarity(duplicate.Key)+" 중복 · 조각 "+duplicate.Value,0,y,inner,28,14,true);y+=32;
                }
                Caption(ui,body,AccountRarity(state.guaranteedRarity)+" 중복도 천장 카운터를 초기화합니다.",0,y,inner,44,14,true);y+=48;
                Caption(ui,body,"풀 "+state.poolVersion,0,y,inner,36,12,true);y+=40;
            }
            else
            {
                foreach(var entry in state.baseOdds)
                {
                    var card=AccountUiArt.Place(body,"recruit","hero-list-card",0,y,inner,118);
                    var portrait=AccountUiArt.Place(card.transform,"recruit",entry.heroId+".portrait.approved-native",8,6,88,104);portrait.preserveAspect=true;
                    Caption(ui,card.transform,AccountHeroName(entry.heroId),108,10,inner-120,30,18,true);
                    var owned=state.heroes.TryGetValue(entry.heroId,out var shards);
                    Caption(ui,card.transform,AccountRarity(entry.rarity)+" · "+(owned?"보유":"미보유"),108,44,inner-120,26,15,true);
                    Caption(ui,card.transform,owned?"조각 "+shards:"",108,76,inner-120,24,15,true);y+=128;
                }
            }
            var probability=ActionButton(ui,body,"Recruit.Odds",odds?"영웅 목록":"확률 공개",showOdds,0,y,inner,!client.Busy,48);
            AccountUiArt.Apply(probability.image,"recruit","recruit-probability-button-"+(client.Busy?"disabled":"active"));y+=58;
            var canSend=!client.Busy&&!client.Uncertain&&state.gems>=state.costGems;
            var recruit=ActionButton(ui,body,"Recruit.Submit",client.Uncertain?"같은 요청 결과 확인":confirm?"확인 · 보석 "+state.costGems:"모집 · 보석 "+state.costGems,client.Uncertain?refresh:confirm?submit:ask,0,y,inner,!client.Busy&&(client.Uncertain||canSend),52);
            AccountUiArt.Apply(recruit.image,"recruit","recruit-once-button-"+(!client.Busy&&(client.Uncertain||canSend)?"active":"disabled"));y+=62;
            if(confirm){ActionButton(ui,body,"Recruit.Cancel","취소",cancel,0,y,inner,!client.Busy);y+=58;}
            if(state.last!=null)
            {
                var result=state.last;var card=AccountUiArt.Place(body,"recruit","recruit-result-frame-"+result.rarity,0,y,inner,220);
                Caption(ui,card.transform,"최근 확정 결과",14,8,inner-28,28,17,true);
                var portrait=AccountUiArt.Place(card.transform,"recruit",result.heroId+".portrait.approved-native",14,44,112,132);portrait.preserveAspect=true;
                Caption(ui,card.transform,AccountHeroName(result.heroId),140,50,inner-154,54,18,true);
                Caption(ui,card.transform,result.outcome=="shards"?"조각 +"+result.shards:"새 영웅",140,110,inner-154,40,17,true);
                Caption(ui,card.transform,AccountRarity(result.rarity)+" · 확정 모집 "+result.drawIndex,14,182,inner-28,28,14,true);y+=232;
            }
            End(body,y);ActionButton(ui,root,"Recruit.Refresh","서버 상태 새로고침",refresh,20,Height(ui)-60,inner,!client.Busy);
        }
        public static void ResearchAccount(UiShell ui,BarracksServerClient client,string selected,bool confirm,Action<string> select,Action ask,Action submit,Action cancel,Action complete,Action refresh,Action back)
        {
            var state=client.ResearchView;var root=AccountHeader(ui,"Research","연구",client,back);var w=Width(ui);var inner=w-40;
            var body=Body(ui,root,158,78);float y=0;
            if(state==null){Caption(ui,body,"연구 서버 응답 확인 필요",0,0,inner,60,16,true);End(body,80);return;}
            foreach(var group in state.nodes.GroupBy(n=>n.category))
            {
                Caption(ui,body,group.Key=="economy"?"경제":group.Key=="military"?"군사":group.Key=="estate"?"영지":group.Key,0,y,inner,30,20,true);y+=34;
                foreach(var node in group)
                {
                    var button=ActionButton(ui,body,"Research.Node:"+node.id,"",()=>select(node.id),0,y,inner,!client.Busy&&!client.Uncertain,64,node.id==selected?"ui.button.primary":"ui.button.secondary");
                    AccountUiArt.Place(button.transform,"research",node.id,8,8,48,48).preserveAspect=true;
                    Caption(ui,button.transform,ResearchName(node.id)+" · "+AccountResearchStatus(node.status),64,4,inner-76,32,15,true);
                    Caption(ui,button.transform,"Lv "+node.level+" / "+node.maxLevel,64,34,inner-76,24,13,true);y+=72;
                }
                y+=8;
            }
            var chosen=state.nodes.FirstOrDefault(n=>n.id==selected);
            if(chosen!=null)
            {
                Caption(ui,body,ResearchName(chosen.id),0,y,inner,32,20,true);y+=36;
                Caption(ui,body,"비용 · "+AccountResearchCost(chosen),0,y,inner,48,15,true);y+=52;
                Caption(ui,body,"연구 시간 "+Duration(chosen.seconds),0,y,inner,28,15,true);y+=32;
                Caption(ui,body,"효과 · "+chosen.domain+" +"+chosen.value,0,y,inner,44,15,true);y+=48;
                foreach(var reason in chosen.lockReasons){Caption(ui,body,"필요 · "+reason,0,y,inner,44,14,true);y+=48;}
                var active=state.active.FirstOrDefault(a=>a.nodeId==chosen.id);
                if(active!=null)
                {
                    Caption(ui,body,"남은 시간 "+Duration(active.remainingSeconds)+" · 완료 "+BarracksCompletionTime(active.completesAtMs,TimeZoneInfo.Local),0,y,inner,44,15,true);y+=48;
                    ActionButton(ui,body,"Research.Complete","완료 확인",complete,0,y,inner,!client.Busy&&!client.Uncertain);y+=58;
                }
                else
                {
                    var enabled=chosen.status=="available"&&!client.Busy&&!client.Uncertain;
                    ActionButton(ui,body,"Research.Start",confirm?"확인 · 연구 시작":"연구 시작",confirm?submit:ask,0,y,inner,enabled,48,"ui.button.primary");y+=58;
                    if(confirm){ActionButton(ui,body,"Research.Cancel","취소",cancel,0,y,inner,!client.Busy);y+=58;}
                }
            }
            foreach(var effect in state.effects){Caption(ui,body,effect.domain+" · 현재 "+effect.raw+" / 상한 "+effect.cap+" · 적용 "+effect.applied,0,y,inner,48,14,true);y+=52;}
            End(body,y);
            ActionButton(ui,root,"Research.Refresh",client.Uncertain?"같은 요청 결과 확인":"서버 상태 새로고침",refresh,20,Height(ui)-60,inner,!client.Busy);
        }
        static string ResearchName(string id)
        {
            switch(id)
            {
                case "slg:research-field-yield":return "밭 순환";case "slg:research-wood-yield":return "목재 선별";case "slg:research-iron-yield":return "광석 분류";
                case "slg:research-training":return "훈련 교범";case "slg:research-formation":return "전열 협동";case "slg:research-cover":return "엄호 교범";
                case "slg:research-building":return "공사 도면";case "slg:research-storage":return "보관 기록";case "slg:research-road-survey":return "행군 측량";
                case "slg:research-hero-drill":return "영주 교련";default:return id;
            }
        }
        public static string AccountCommandStatus(string status)
        {
            switch(status)
            {
                case "reply-reader-required":return "이전 요청의 화면에서 결과를 먼저 확인하세요";
                case "pending-storage-error":return "요청 저장 실패 · 새 요청을 보내지 않았습니다";
                case "pool-changed":return "모집 확률이 바뀌었습니다. 다시 확인하세요";
                case "insufficient-gems":return "보석이 부족합니다";
                case "recruited":return "모집 결과가 확정되었습니다";
                case "started":return "연구를 시작했습니다";
                case "completed":return "연구 완료를 확인했습니다";
                case "not-finished":return "연구가 아직 진행 중입니다";
                default:return BarracksStatus(status);
            }
        }
    }
}
