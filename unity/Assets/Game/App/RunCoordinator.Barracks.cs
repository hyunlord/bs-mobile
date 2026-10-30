using System;
using System.Collections;
using System.Linq;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        BarracksServerClient barracksServer;
        string barracksUnit;
        int barracksCount = 1;
        bool barracksConfirm;
        UnityEngine.Coroutine barracksPoll;

        Action BarracksSettingsAction
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return IsWave && (Session == null || finished) && BarracksServerEndpoint() != null ? ShowBarracksConnection : (Action)null;
#else
                return null;
#endif
            }
        }
        static string BarracksServerEndpoint()
        {
            var args = Environment.GetCommandLineArgs(); var at = Array.IndexOf(args, "--server-url");
            return at >= 0 && at + 1 < args.Length && BarracksServerClient.ValidEndpoint(args[at + 1]) ? args[at + 1] : null;
        }
        void ShowBarracksConnection()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var endpoint = BarracksServerEndpoint(); if (endpoint == null) return;
            if (barracksServer == null) barracksServer = new BarracksServerClient(endpoint);
            if (barracksPoll == null) barracksPoll = StartCoroutine(PollBarracks());
            if (barracksServer.Latest != null) { ShowBarracks(); return; }
            MetaPage(UiScreen.Barracks, () => PortraitMetaPanels.BarracksConnect(Ui, endpoint, barracksServer.Status,
                barracksServer.Busy, (token, create) => StartCoroutine(BarracksRequest(barracksServer.Connect(token, create))), RestoreBarracksSettings));
#endif
        }
        void ShowBarracks()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (barracksServer?.Latest?.Training == null) { ShowBarracksConnection(); return; }
            var units = barracksServer.Latest.Training.units;
            if (!units.Any(u => u.unitId == barracksUnit)) barracksUnit = units.FirstOrDefault()?.unitId;
            MetaPage(UiScreen.Barracks, () => PortraitMetaPanels.Barracks(Ui, barracksServer, barracksUnit, barracksCount, barracksConfirm,
                id => { barracksUnit = id; barracksConfirm = false; ShowBarracks(); },
                count => { barracksCount = count; barracksConfirm = false; ShowBarracks(); },
                () => { barracksConfirm = true; ShowBarracks(); },
                () => { barracksConfirm = false; StartCoroutine(BarracksRequest(barracksServer.Start(barracksUnit, barracksCount))); },
                () => StartCoroutine(BarracksRequest(barracksServer.CancelWaiting())),
                () => StartCoroutine(BarracksRequest(barracksServer.Uncertain ? barracksServer.Retry() : barracksServer.Refresh())),
                () => ShowSettings(false)));
#endif
        }
        void RestoreBarracksSettings() { screen = UiScreen.Settings; ShowSettings(false); }
        IEnumerator PollBarracks()
        {
            while (true)
            {
                yield return new UnityEngine.WaitForSecondsRealtime(5);
                if (screen == UiScreen.Barracks && barracksServer?.Latest != null && !barracksServer.Busy && !barracksServer.Uncertain && !barracksConfirm)
                    yield return BarracksRequest(barracksServer.Refresh());
            }
        }
        IEnumerator BarracksRequest(IEnumerator operation)
        {
            // Start the coroutine before rebuilding so all spending buttons reflect Busy immediately.
            var pending = StartCoroutine(operation); ShowBarracksConnection();
            yield return pending;
            if (screen == UiScreen.Barracks) ShowBarracksConnection();
        }
    }
}
