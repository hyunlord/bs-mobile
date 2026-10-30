using System;
using System.Linq;
using Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // Approved #563: parchment panels, blue primary actions, dark resource/navigation bars.
    public static partial class PortraitMetaPanels
    {
        public const string BarracksGlyphs = "병영 훈련 서버 연결 개발 계정 새 만들기 기존 토큰 입력 닫기 연결 중 결과 확인 다시 보내기 조회 상비 병력 총 인원 상한 준비 출전 부상 예약 포함 징집 창병 방패 수비병 명 곡물 목재 철재 기본 시간 훈련 뒤 충분 부족 활성 대기 취소 확인 실제 서버 상태 로컬 저장과 별개 이번 실행 동안만 보관 안 됨 병영 필요 선택 자원 모자람 공간 없음 재접속 인증 만료 응답 오류 취소 환불 대기만 가능 서버 주소 실행 옵션 적용 기다려 주세요 연결 실패 새로고침 환불 대기 지갑 가득 차 일부 공간 생기면 돌려받습니다 즉시 계속 기초 생산시설 건설용 쓸 수 없습니다 견적 완료 예정 점유 이번 남은 진행 담지 못한 보관됩니다 공간 시 UTC";
        static readonly Color BarracksDark = new Color32(20, 26, 31, 255);
        public static void BarracksConnect(UiShell ui, string endpoint, string status, bool busy, Action<string, bool> connect, Action back)
        {
            var root = Page(ui, "BarracksConnect", ""); ui.ApplySurface(root.GetComponent<Image>(), "ui.panel");
            var w = Width(ui); var body = Body(ui, root, 124, 78);
            ActionButton(ui, root, "Barracks.Back", "‹", back, 8, 60, 48, !busy);
            Caption(ui, root, "개발 서버 · 병영", 68, 60, w - 76, 42, 22, true);
            Caption(ui, body, "로컬 저장과 별개인 서버 계정입니다.\n토큰은 이번 실행 동안만 보관합니다.", 0, 0, w - 36, 60, 15, true);
            Caption(ui, body, endpoint, 0, 66, w - 36, 44, 14, true);
            Caption(ui, body, "기존 계정 토큰", 0, 120, w - 36, 28, 16, true);
            var fieldRect = Plate(ui, body, "Barracks.Token", "ui.panel", 0, 154, w - 36, 52);
            var field = fieldRect.gameObject.AddComponent<InputField>();
            var label = Caption(ui, fieldRect, "", 10, 4, w - 56, 44, 16, true);
            field.textComponent = label; field.targetGraphic = fieldRect.GetComponent<Image>();
            field.contentType = InputField.ContentType.Password; field.characterLimit = 64; field.lineType = InputField.LineType.SingleLine;
            field.interactable = !busy;
            ActionButton(ui, body, "Barracks.Connect", "연결", () => connect(field.text, false), 0, 222, w - 36, !busy, 48, "ui.button.primary");
            ActionButton(ui, body, "Barracks.Create", "새 서버 계정 만들기", () => connect(null, true), 0, 286, w - 36, !busy);
            Caption(ui, body, BarracksStatus(status), 0, 352, w - 36, 72, 15, true); End(body, 440);
        }
        public static void Barracks(UiShell ui, BarracksServerClient client, string selected, int count, bool confirming, bool cancelConfirming,
            Action<string> select, Action<int> quantity, Action confirm, Action submit, Action cancel, Action keepWaiting, Action refresh, Action back)
        {
            var root=Page(ui,"Barracks","");ui.ApplySurface(root.GetComponent<Image>(),"ui.panel");
            float w=Width(ui),h=Height(ui),inner=w-40;
            var state=client.Latest.Training;
            var chosen=state.units.FirstOrDefault(u=>u.unitId==selected)??state.units.FirstOrDefault();
            var bar=ColorPlate(root,"Barracks.Header",BarracksDark,0,0,w,52);
            Caption(ui,bar,"병영 · 훈련",16,8,w-80,36,22).fontStyle=FontStyle.Bold;
            ActionButton(ui,bar,"Barracks.Back","‹",back,w-56,2,48,!client.Busy);
            if(chosen==null)return;
            var quote=chosen.quotes?.FirstOrDefault(q=>q.count==count);
            float unitWidth=(inner-8)/Math.Max(1,state.units.Length);
            for(var i=0;i<state.units.Length;i++)
            {
                var unit=state.units[i];ActionButton(ui,root,"Barracks.Unit:"+unit.unitId,unit.name,()=>select(unit.unitId),20+i*(unitWidth+8),58,unitWidth,!client.Busy&&!client.Uncertain,48,unit==chosen?"ui.button.primary":"ui.button.secondary");
            }
            TrainingImage(root,chosen.unitId=="slg:guard"?"troop-guard":"troop-levy",28,110,80,80).preserveAspect=true;
            Caption(ui,root,"훈련할 인원",124,113,w-144,26,18,true).fontStyle=FontStyle.Bold;
            Caption(ui,root,"준비 "+chosen.ready+" · 부상 "+chosen.wounded,124,144,w-144,26,16,true);
            Caption(ui,root,"병영 Lv "+state.barracksLevel+" · 서버 계정",124,174,w-144,24,14,true);
            Caption(ui,root,"훈련 수량",20,200,inner,26,18,true).fontStyle=FontStyle.Bold;
            TrainingQuantity(ui,root,"Barracks.CountLess","minus",()=>quantity(Math.Max(1,count-1)),32,230,count>1&&!client.Busy&&!client.Uncertain);
            var value=TrainingImage(root,"training-quantity-value",(w-166)/2,230,166,48);
            Caption(ui,value.transform,count+"명",0,0,166,48,22,true,TextAnchor.MiddleCenter).fontStyle=FontStyle.Bold;
            TrainingQuantity(ui,root,"Barracks.CountMore","plus",()=>quantity(count+1),w-80,230,count<state.batchLimit&&!client.Busy&&!client.Uncertain);
            Caption(ui,root,"총 비용",20,287,inner,26,18,true).fontStyle=FontStyle.Bold;
            var cost=TrainingImage(root,"training-total-cost-row",32,316,w-64,56);
            if(quote?.totalCost!=null)
            {
                var ids=new[]{"meta:grain","meta:timber","meta:iron"};
                for(var i=0;i<ids.Length;i++)
                {
                    float x=12+i*(w-64)/3;MaterialArt(ui,cost.transform,ids[i],"",x,16,24);
                    var amount=quote.totalCost.FirstOrDefault(c=>c.materialId==ids[i])?.amount??0;
                    Caption(ui,cost.transform,amount.ToString(),x+30,10,(w-64)/3-42,36,18,true).fontStyle=FontStyle.Bold;
                }
            }
            else Caption(ui,cost.transform,"서버 견적 확인 필요",14,10,w-92,36,16,true);
            Caption(ui,root,quote!=null&&quote.canStart?"완료 예정 "+DateTimeOffset.FromUnixTimeMilliseconds(quote.estimatedCompletesAtMilliseconds).ToString("HH:mm 'UTC'")+" · "+Duration(quote.durationMilliseconds/1000):"완료 예정 —",24,378,w-48,25,14,true);
            Caption(ui,root,"병력 점유 "+state.occupied+" / "+state.capacity+" (이번 훈련 +"+(quote?.count??0)+" 예약)",24,404,w-48,25,14,true);
            var reason=Caption(ui,root,quote==null?"서버 견적 확인 필요":!quote.canStart?BarracksStatus(quote.reason):BarracksStatus(client.Status),24,431,w-48,36,14,true);reason.name="Barracks.Status";
            var train=ActionButton(ui,root,"Barracks.Train",client.Uncertain?"같은 요청 결과 확인":confirming?"확인 · "+count+"명 훈련":count+"명 훈련",client.Uncertain?refresh:confirming?submit:confirm,32,472,w-64,!client.Busy&&(client.Uncertain||quote?.canStart==true),48,"ui.button.primary");
            var label=train.GetComponentInChildren<Text>();label.GetComponent<PortraitButtonLabel>().enabled=false;label.color=new Color32(247,238,213,255);
            var queue=Plate(ui,root,"Barracks.Queue","ui.panel",20,534,inner,144);
            Caption(ui,queue,state.HasActive?"진행 중 · "+state.active.count+"명":"활성 훈련 없음",14,10,inner-28,28,18,true).fontStyle=FontStyle.Bold;
            Caption(ui,queue,state.HasActive?"남은 시간 "+Duration(Math.Max(0,state.active.completesAtMilliseconds-state.serverUtcMilliseconds)/1000):"대기 0 / 1",14,42,inner-28,24,16,true);
            var track=TrainingImage(queue,"training-progress-track",14,81,148,18);
            if(state.HasActive)
            {
                var progress=(float)Math.Max(0,Math.Min(1,(double)(state.serverUtcMilliseconds-state.active.startedAtMilliseconds)/Math.Max(1,state.active.durationMilliseconds)));
                var fill=TrainingImage(track.transform,"training-progress-fill",8,5,132,8);fill.type=Image.Type.Filled;fill.fillMethod=Image.FillMethod.Horizontal;fill.fillAmount=progress;
            }
            var canCancel=state.HasWaiting&&!client.Busy&&!client.Uncertain;
            var cancelButton=ActionButton(ui,queue,"Barracks.CancelWaiting",state.HasWaiting?"대기 취소":"활성 취소 불가",cancel,inner-174,74,160,canCancel);
            TrainingSurface(cancelButton.image,"training-cancel-"+(canCancel?"active":"disabled"));
            Caption(ui,queue,state.HasWaiting?"대기 "+state.waiting.count+"명 · 앞 훈련 완료 후 시작":"대기 0 / 1",14,118,inner-28,22,13,true);
            var refund=TrainingImage(root,"training-refund-pending-panel",32,690,w-64,82);
            TrainingImage(refund.transform,"training-refund-pending-mark",14,14,20,20);
            var refundText=BarracksPendingRefundText(state);
            Caption(ui,refund.transform,string.IsNullOrEmpty(refundText)?"환불 대기 없음":refundText,44,10,w-120,26,16).fontStyle=FontStyle.Bold;
            Caption(ui,refund.transform,"지갑에 담지 못한 환불은 보관됩니다",14,46,w-92,26,14,true);
            ActionButton(ui,root,"Barracks.Refresh","서버 상태 새로고침",refresh,20,h-60,w-40,!client.Busy&&!client.Uncertain);
            if(cancelConfirming&&state.HasWaiting)
            {
                var shade=ColorPlate(root,"Barracks.ConfirmationShade",new Color(0,0,0,.75f),0,52,w,h-52);shade.GetComponent<Image>().raycastTarget=true;
                var panel=Plate(ui,root,"Barracks.CancelConfirmation","ui.panel",20,290,inner,280);
                Caption(ui,panel,"대기 훈련 취소",16,12,inner-32,32,20,true).fontStyle=FontStyle.Bold;
                Caption(ui,panel,BarracksRefundPreviewText(state),16,56,inner-32,140,16,true);
                var yes=ActionButton(ui,panel,"Barracks.ConfirmCancel","확인 · 대기 취소",cancel,14,214,(inner-36)/2,!client.Busy&&!client.Uncertain);
                TrainingSurface(yes.image,"training-cancel-active");
                ActionButton(ui,panel,"Barracks.KeepWaiting","계속 훈련",keepWaiting,22+(inner-36)/2,214,(inner-36)/2,!client.Busy);
            }
        }
        public static string BarracksPendingRefundText(BarracksView state) =>
            string.Join(" · ", (state.pendingRefunds ?? Array.Empty<BarracksCost>()).Where(c => c.amount > 0).Select(c => "환불 대기 " + MaterialName(c.materialId) + " " + c.amount));
        public static string BarracksRefundPreviewText(BarracksView state)
        {
            var rows = state.waitingCancellationRefunds ?? Array.Empty<BarracksRefundPreview>();
            var text = string.Join(" · ", rows.Select(c => MaterialName(c.materialId) + " 즉시 " + c.immediateAmount + " / 환불 대기 " + c.pendingAmount));
            return rows.Any(c => c.pendingAmount > 0) ? text + "\n지갑이 가득 차 일부는 공간이 생기면 돌려받습니다" : text + "\n대기 훈련을 취소하고 자원을 돌려받습니다";
        }
        static string MaterialName(string id) => id == "meta:grain" ? "곡물" : id == "meta:timber" ? "목재" : id == "meta:iron" ? "철재" : id;
        static string Duration(long seconds) => (seconds / 60) + ":" + (seconds % 60).ToString("00");
        public static string BarracksStatus(string reason)
        {
            switch (reason)
            {
                case "pending": return "서버 확인 중 · 기다려 주세요";
                case "outcome-unknown": case "invalid-response": return "결과를 확인하지 못했습니다. 같은 요청으로 다시 조회합니다.";
                case "connection-failed": return "서버 연결 실패 · 서버 주소와 실행 상태를 확인하세요";
                case "unauthorized": return "계정 인증이 만료됐습니다. 다시 연결하세요";
                case "version-conflict": return "서버 상태가 바뀌었습니다. 새로고침 후 다시 선택하세요";
                case "invalid-token": return "64자리 계정 토큰을 입력하세요";
                case "not-connected": return "새 계정 또는 기존 계정으로 연결하세요";
                case "barracks-required": case "unit-locked": return "병영 완공 레벨이 부족합니다";
                case "insufficient-resources": return "재료가 모자랍니다";
                case "protected-reserve": return "기초 생산시설 건설용 자원은 훈련에 쓸 수 없습니다";
                case "queue-full": return "대기열이 가득 찼습니다";
                case "capacity": return "병력 상한을 넘습니다";
                case "training-started": return "훈련을 시작했습니다";
                case "training-queued": return "대기 훈련을 예약했습니다";
                case "training-cancelled": return "대기 훈련을 취소했습니다";
                case "accepted": case "initialized": case "connected": return "서버 상태 확인됨";
                default: return string.IsNullOrEmpty(reason) ? "" : "서버 상태 · " + reason;
            }
        }
    }
}
