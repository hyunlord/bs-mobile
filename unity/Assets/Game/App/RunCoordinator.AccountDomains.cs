using System;
using System.Collections;
using System.Linq;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        bool recruitOdds, recruitConfirm, researchConfirm;
        string researchNode;

        bool accountDomainCredentialsSubscribed, accountDomainConnecting;
        Action accountDomainConnected;
        bool UsesDeviceAccount => !string.IsNullOrWhiteSpace(AccountEndpoint());
        bool EnsureDeviceAccountConnection(Action onConnected = null)
        {
            if (onConnected != null) accountDomainConnected = onConnected;
            InitializeAccountAccess();
            if (!accountDomainCredentialsSubscribed)
            {
                AccountCredentialsChanged += ClearAccountDomainCredentials;
                accountDomainCredentialsSubscribed = true;
            }
            if (string.IsNullOrEmpty(AccountAccessToken))
            {
                barracksServer.ClearAuthentication(); accountDomainConnected = null; ShowAccountSettings(); return false;
            }
            if (!barracksServer.MatchesCredentials(AccountAccessToken, AuthenticatedAccountId))
            {
                if (!barracksServer.Busy) StartCoroutine(ConnectDeviceAccountDomains());
                else ShowDeviceAccountConnection();
                return false;
            }
            if (!barracksServer.AccountReady) { ShowDeviceAccountConnection(); return false; }
            accountDomainConnected = null; return true;
        }
        void ClearAccountDomainCredentials()
        {
            barracksServer?.ClearAuthentication();
            accountDomainConnected = null;
            recruitConfirm = false; researchConfirm = false; barracksConfirm = false; barracksCancelConfirm = false;
            researchNode = null; barracksUnit = null;
            if (screen == UiScreen.Barracks || screen == UiScreen.Recruit || screen == UiScreen.Research) ShowBarracksConnection();
        }
        void ShowDeviceAccountConnection() => MetaPage(UiScreen.Barracks, () => PortraitMetaPanels.AccountConnection(Ui,
            barracksServer.Status, barracksServer.Busy, () => StartCoroutine(ConnectDeviceAccountDomains()), () => { accountDomainConnected = null; RestoreBarracksSettings(); }));
        IEnumerator ConnectDeviceAccountDomains()
        {
            if (string.IsNullOrEmpty(AccountAccessToken)) { barracksServer.ClearAuthentication(); ShowAccountSettings(); yield break; }
            if (accountDomainConnecting) yield break;
            accountDomainConnecting = true;
            try
            {
                var pending = StartCoroutine(barracksServer.ConnectAuthenticated(AccountAccessToken, AuthenticatedAccountId));
                ShowDeviceAccountConnection();
                yield return pending;
            }
            finally { accountDomainConnecting = false; }
            if (screen == UiScreen.Barracks)
            {
                if (barracksServer.AccountReady)
                {
                    var next = accountDomainConnected; accountDomainConnected = null;
                    if (next != null) next(); else ShowBarracks();
                }
                else ShowDeviceAccountConnection();
            }
        }
        void RedrawAccountScreen()
        {
            if (screen == UiScreen.Recruit) ShowRecruit();
            else if (screen == UiScreen.Research) ShowResearch();
            else if (screen == UiScreen.Barracks) ShowBarracksConnection();
        }
        void RefreshAccountDomain()
        {
            recruitConfirm = false; researchConfirm = false;
            StartCoroutine(BarracksRequest(barracksServer.Uncertain ? barracksServer.Retry() : barracksServer.Refresh()));
        }
        void ShowRecruit()
        {
            if (barracksServer?.AccountReady != true) { ShowBarracksConnection(); return; }
            MetaPage(UiScreen.Recruit, () => PortraitMetaPanels.RecruitAccount(Ui, barracksServer, recruitOdds, recruitConfirm,
                () => { recruitOdds = !recruitOdds; recruitConfirm = false; ShowRecruit(); },
                () => { recruitOdds = true; recruitConfirm = true; ShowRecruit(); },
                () => { recruitConfirm = false; StartCoroutine(BarracksRequest(barracksServer.Recruit(barracksServer.RecruitView.poolVersion))); },
                () => { recruitConfirm = false; ShowRecruit(); }, RefreshAccountDomain, ShowBarracks));
        }
        void ShowResearch()
        {
            if (barracksServer?.AccountReady != true) { ShowBarracksConnection(); return; }
            var nodes = barracksServer.ResearchView?.nodes;
            if (nodes != null && !nodes.Any(n => n.id == researchNode)) researchNode = nodes.FirstOrDefault()?.id;
            MetaPage(UiScreen.Research, () => PortraitMetaPanels.ResearchAccount(Ui, barracksServer, researchNode, researchConfirm,
                id => { researchNode = id; researchConfirm = false; ShowResearch(); },
                () => { researchConfirm = true; ShowResearch(); },
                () => { researchConfirm = false; StartCoroutine(BarracksRequest(barracksServer.StartResearch(researchNode))); },
                () => { researchConfirm = false; ShowResearch(); },
                () => StartCoroutine(BarracksRequest(barracksServer.CompleteResearch(researchNode))), RefreshAccountDomain, ShowBarracks));
        }
    }
}
