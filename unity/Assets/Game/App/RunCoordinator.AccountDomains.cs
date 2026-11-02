using System.Linq;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        bool recruitOdds, recruitConfirm, researchConfirm;
        string researchNode;

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
            if (barracksServer?.Latest == null) { ShowBarracksConnection(); return; }
            MetaPage(UiScreen.Recruit, () => PortraitMetaPanels.RecruitAccount(Ui, barracksServer, recruitOdds, recruitConfirm,
                () => { recruitOdds = !recruitOdds; recruitConfirm = false; ShowRecruit(); },
                () => { recruitOdds = true; recruitConfirm = true; ShowRecruit(); },
                () => { recruitConfirm = false; StartCoroutine(BarracksRequest(barracksServer.Recruit(barracksServer.RecruitView.poolVersion))); },
                () => { recruitConfirm = false; ShowRecruit(); }, RefreshAccountDomain, ShowBarracks));
        }
        void ShowResearch()
        {
            if (barracksServer?.Latest == null) { ShowBarracksConnection(); return; }
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
