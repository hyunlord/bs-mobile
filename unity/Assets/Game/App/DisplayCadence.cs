using System;
using UnityEngine;

namespace Game.App
{
    public sealed class DisplayCadence : MonoBehaviour
    {
        double lastRefresh = -1;
        bool explicitBenchmark;

        public static int SyncInterval(double refreshHz, bool mobile)
        {
            if (mobile || double.IsNaN(refreshHz) || double.IsInfinity(refreshHz) || refreshHz <= 0) return 0;
            var interval = (int)Math.Round(refreshHz / 60d, MidpointRounding.AwayFromZero);
            return interval >= 1 && interval <= 4 && Math.Abs(refreshHz / interval - 60d) < .5d ? interval : 0;
        }

        public static void Apply()
        {
            var interval = SyncInterval(Screen.currentResolution.refreshRateRatio.value, Application.isMobilePlatform);
            QualitySettings.vSyncCount = interval;
            Application.targetFrameRate = interval == 0 ? 60 : -1;
        }

        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            explicitBenchmark = Array.IndexOf(args, "--wave-benchmark") >= 0 && Array.IndexOf(args, "--crowd-replay-benchmark") < 0;
            Refresh();
        }

        void Update()
        {
            if (!explicitBenchmark && AutoplayCapture.Active == null && Time.captureFramerate == 0) Refresh();
        }

        void Refresh()
        {
            var refresh = Screen.currentResolution.refreshRateRatio.value;
            if (refresh == lastRefresh) return;
            lastRefresh = refresh;
            Apply();
        }
    }
}
