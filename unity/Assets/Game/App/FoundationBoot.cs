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

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60;
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
            try
            {
                VerifiedDataHash = BundleVerifier.Verify(files);
                Catalog = CanonicalContent.CreateCatalog();
                Complete = true;
                status.Message = "준비 완료";
            }
            catch (Exception exception) { Fail(status, exception.Message); }
            if (Complete)
            {
                yield return SceneManager.LoadSceneAsync("Meta");
                Destroy(status);
                gameObject.AddComponent<RunCoordinator>().Initialize();
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
