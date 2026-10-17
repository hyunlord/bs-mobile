using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.App.Generated;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Game.App
{
    public sealed class FoundationBoot : MonoBehaviour
    {
        public static ContentCatalog Catalog { get; private set; }
        public static string VerifiedDataHash { get; private set; }
        public bool Complete { get; private set; }
        public string Error { get; private set; }

        public static string ToRequestUri(string path) => Path.IsPathRooted(path) ? new Uri(path).AbsoluteUri : path;

        public static bool IsOfflineReviewBoot(string[] args)
            => Array.IndexOf(args, "--offline-replay-capture") >= 0
                && Array.IndexOf(args, "--crowd-replay-benchmark") >= 0;

        private IEnumerator Start()
        {
            // Only the explicit offline review may progress without foreground ownership.
            // Set before the first asynchronous load, not after RunCoordinator exists.
            var offlineReview = IsOfflineReviewBoot(Environment.GetCommandLineArgs());
            if (offlineReview)
            {
                Application.runInBackground = true;
                UnityEngine.Debug.Log("OFFLINE_REVIEW_BOOT begin background-enabled");
            }
            gameObject.AddComponent<DisplayCadence>();
            DontDestroyOnLoad(gameObject);
            var status = gameObject.AddComponent<FoundationStatus>();
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in CanonicalContent.Files)
            {
                var path = Application.isEditor
                    ? new Uri(Path.GetFullPath(Path.Combine(Application.dataPath, "../../data", file.RelativePath))).AbsoluteUri
                    : Application.streamingAssetsPath + "/data/" + file.RelativePath;
                using (var request = UnityWebRequest.Get(ToRequestUri(path)))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Fail(status, "Cannot read canonical data: " + file.RelativePath);
                        yield break;
                    }
                    files.Add(file.RelativePath, request.downloadHandler.data);
                }
            }
            if (offlineReview) UnityEngine.Debug.Log("OFFLINE_REVIEW_BOOT files-loaded");
            try
            {
                VerifiedDataHash = BundleVerifier.Verify(files);
                Catalog = CanonicalContent.CreateCatalog();
                ArtCatalog.ProfileName = CanonicalContent.ProfileName;
                Complete = true;
                status.Message = "준비 완료";
            }
            catch (Exception exception) { Fail(status, exception.Message); }
            if (Complete)
            {
                if (offlineReview) UnityEngine.Debug.Log("OFFLINE_REVIEW_BOOT catalog-verified loading-meta");
                yield return SceneManager.LoadSceneAsync("Meta");
                if (offlineReview) UnityEngine.Debug.Log("OFFLINE_REVIEW_BOOT meta-loaded initializing-run");
                Destroy(status);
                gameObject.AddComponent<RunCoordinator>().Initialize();
                if (offlineReview) UnityEngine.Debug.Log("OFFLINE_REVIEW_BOOT run-initialized");
            }
        }

        private void Fail(FoundationStatus status, string reason)
        {
            Catalog = null;
            VerifiedDataHash = null;
            Error = reason;
            status.Failed = true;
            status.Message = "게임 데이터를 확인할 수 없습니다.\n" + reason;
            UnityEngine.Debug.LogError(reason);
        }
    }
}
