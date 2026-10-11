using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Game.View
{
    public sealed class ShortHaptics : IDisposable
    {
        bool disposed;
#if UNITY_ANDROID && !UNITY_EDITOR
        readonly object gate = new object();
        readonly AndroidJavaObject activity, view;
        readonly object[] feedbackArguments, dispatchArguments;
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SowSiegePrepareShortHaptic();
        [DllImport("__Internal")] static extern void SowSiegePlayShortHaptic();
#endif

        public ShortHaptics()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var window = activity.Call<AndroidJavaObject>("getWindow");
            view = window.Call<AndroidJavaObject>("getDecorView");
            using var constants = new AndroidJavaClass("android.view.HapticFeedbackConstants");
            feedbackArguments = new object[] { constants.GetStatic<int>("CONTEXT_CLICK") };
            dispatchArguments = new object[] { new AndroidJavaRunnable(PlayOnAndroidUiThread) };
#elif UNITY_IOS && !UNITY_EDITOR
            SowSiegePrepareShortHaptic();
#endif
        }

        public void Play()
        {
            if (disposed) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (gate)
            {
                if (!disposed) activity.Call("runOnUiThread", dispatchArguments);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            if (!disposed) SowSiegePlayShortHaptic();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void PlayOnAndroidUiThread()
        {
            lock (gate)
            {
                if (!disposed) view.Call<bool>("performHapticFeedback", feedbackArguments);
            }
        }
#endif

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                view.Dispose(); activity.Dispose();
            }
#else
            disposed = true;
#endif
        }
    }
}
