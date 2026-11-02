using System;
using System.Collections;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        string allianceMessage;
        bool allianceReportReason;

        bool TryShowPortraitAllianceChat()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (barracksServer == null || !barracksServer.Connected) return false;
            barracksServer.PrepareAlliance();
            RenderPortraitAllianceChat();
            if (barracksServer.AllianceLatest == null && !barracksServer.Busy && !barracksServer.Uncertain)
                StartCoroutine(AllianceRequest(barracksServer.RefreshAlliance()));
            return true;
#else
            return false;
#endif
        }
        void RenderPortraitAllianceChat()
        {
            MetaPage(UiScreen.Alliance, () => PortraitMetaPanels.AllianceChat(Ui, barracksServer, allianceMessage, allianceReportReason,
                new AllianceChatActions
                {
                    Back = ShowPortraitEstate,
                    Refresh = () => StartCoroutine(AllianceRequest(RefreshAllianceWithPending())),
                    SelectMessage = id => { allianceMessage = id; allianceReportReason = false; RenderPortraitAllianceChat(); },
                    BeginReport = () => { allianceReportReason = true; RenderPortraitAllianceChat(); },
                    CloseMenu = () => { allianceMessage = null; allianceReportReason = false; RenderPortraitAllianceChat(); },
                    Report = reason =>
                    {
                        var id = allianceMessage; allianceMessage = null; allianceReportReason = false;
                        StartCoroutine(AllianceRequest(barracksServer.ReportAllianceMessage(id, reason, "")));
                    },
                    Block = () =>
                    {
                        var message = Array.Find(barracksServer.AllianceLatest.chat, value => value.messageId == allianceMessage);
                        if (message == null || string.IsNullOrEmpty(message.author)) return;
                        allianceMessage = null; allianceReportReason = false;
                        StartCoroutine(AllianceRequest(barracksServer.BlockAllianceAuthor(message.author, true)));
                    },
                    Unblock = id => StartCoroutine(AllianceRequest(barracksServer.BlockAllianceAuthor(id, false)))
                }));
        }
        IEnumerator RefreshAllianceWithPending()
        {
            barracksServer.PrepareAlliance();
            if (barracksServer.Uncertain) yield return barracksServer.Retry();
            if (!barracksServer.Uncertain) yield return barracksServer.RefreshAlliance();
        }
        IEnumerator AllianceRequest(IEnumerator operation)
        {
            var pending = StartCoroutine(operation);
            RenderPortraitAllianceChat();
            yield return pending;
            if (screen == UiScreen.Alliance) RenderPortraitAllianceChat();
        }
    }
}
