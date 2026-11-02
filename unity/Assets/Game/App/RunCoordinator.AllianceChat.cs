using System;
using System.Collections;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        string allianceMessage;
        bool allianceReportReason;
        bool allianceCredentialsSubscribed;

        void ShowPortraitAlliance()
        {
            if (!TryShowPortraitAllianceChat()) ShowPortraitUnconnectedWorld(PortraitMetaPage.Alliance);
        }

        void ClearAllianceSelection()
        {
            allianceMessage = null; allianceReportReason = false;
            if (screen == UiScreen.Alliance) ShowPortraitUnconnectedWorld(PortraitMetaPage.Alliance);
        }

        bool TryShowPortraitAllianceChat()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (UsesDeviceAccount)
            {
                var endpoint = BarracksServerEndpoint();
                if (endpoint == null) return false;
                if (barracksServer == null) barracksServer = new BarracksServerClient(endpoint);
                barracksServer.PrepareAlliance();
                if (!allianceCredentialsSubscribed)
                {
                    AccountCredentialsChanged += ClearAllianceSelection;
                    allianceCredentialsSubscribed = true;
                }
                if (!EnsureDeviceAccountConnection()) return true;
            }
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
            if (UsesDeviceAccount && !barracksServer.MatchesCredentials(AccountAccessToken, AuthenticatedAccountId))
            {
                barracksServer.ClearAuthentication(); ClearAllianceSelection(); yield break;
            }
            var pending = StartCoroutine(operation);
            RenderPortraitAllianceChat();
            yield return pending;
            if (screen == UiScreen.Alliance) RenderPortraitAllianceChat();
        }
    }
}
