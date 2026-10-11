using System;
using UnityEngine;

namespace Game.App
{
    public sealed class DisplayCadence : MonoBehaviour
    {
        double lastRefresh = -1;
        bool explicitBenchmark, crowdTarget60Experiment;

        public static bool ParseCrowdTarget60Experiment(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            var option = Array.IndexOf(args, "--crowd-target60-experiment");
            if (option < 0) return false;
            var replay = Array.IndexOf(args, "--wave-benchmark");
            if (option != Array.LastIndexOf(args, "--crowd-target60-experiment") || replay < 0 ||
                replay != Array.LastIndexOf(args, "--wave-benchmark") || replay + 1 >= args.Length ||
                string.IsNullOrWhiteSpace(args[replay + 1]) || args[replay + 1].StartsWith("--", StringComparison.Ordinal) ||
                Array.IndexOf(args, "--crowd-replay-benchmark") < 0 ||
                Array.IndexOf(args, "--offline-replay-capture") >= 0 || Array.IndexOf(args, "--autoplay-capture") >= 0 ||
                Array.IndexOf(args, "--wave-benchmark-capture") >= 0 || Array.IndexOf(args, "--wave-benchmark-mode") >= 0)
                throw new ArgumentException("--crowd-target60-experiment requires one crowd replay benchmark without capture or an alternate benchmark mode.");
            return true;
        }
        public static void ApplyCrowdTarget60Experiment()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
        }

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
            crowdTarget60Experiment = ParseCrowdTarget60Experiment(args);
            explicitBenchmark = crowdTarget60Experiment || (Array.IndexOf(args, "--wave-benchmark") >= 0 && Array.IndexOf(args, "--crowd-replay-benchmark") < 0);
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
            if (crowdTarget60Experiment) ApplyCrowdTarget60Experiment(); else Apply();
        }
    }
}
