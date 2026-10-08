using System;
using UnityEngine;

namespace Game.App
{
    [Serializable]
    public sealed class DeviceFacts
    {
        public string model;
        public string os;
        public string unityVersion;
        public string backend;
        public string applicationVersion;
        public string graphics;
        public string sourceHash;
        public bool sourceDirty;
        public string thermal = "unavailable";
        float nextThermalRead;

        public static DeviceFacts Capture()
        {
            var facts = new DeviceFacts
            {
                model = SystemInfo.deviceModel, os = SystemInfo.operatingSystem,
                unityVersion = Application.unityVersion, applicationVersion = Application.version,
                graphics = SystemInfo.graphicsDeviceType.ToString(),
                sourceHash = Generated.BuildIdentity.SourceHash, sourceDirty = Generated.BuildIdentity.SourceDirty,
#if ENABLE_IL2CPP
                backend = "IL2CPP"
#else
                backend = "Mono"
#endif
            };
            facts.RefreshThermal();
            return facts;
        }

        public void RefreshThermal()
        {
            if (Time.realtimeSinceStartup < nextThermalRead) return;
            nextThermalRead = Time.realtimeSinceStartup + 5;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (version.GetStatic<int>("SDK_INT") < 29) return;
                }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var power = activity.Call<AndroidJavaObject>("getSystemService", "power"))
                {
                    if (power == null) { thermal = "unavailable"; return; }
                    switch (power.Call<int>("getCurrentThermalStatus"))
                    {
                        case 0: thermal = "none"; break;
                        case 1: thermal = "light"; break;
                        case 2: thermal = "moderate"; break;
                        case 3: thermal = "severe"; break;
                        case 4: thermal = "critical"; break;
                        case 5: thermal = "emergency"; break;
                        case 6: thermal = "shutdown"; break;
                        default: thermal = "unavailable"; break;
                    }
                }
            }
            catch (AndroidJavaException) { thermal = "unavailable"; }
#endif
        }
    }
}
