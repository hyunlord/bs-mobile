#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.App
{
    public static class DeviceParity
    {
        public static IEnumerator Run(ContentCatalog catalog, string dataHash, string[] fixtureUris, string outputDirectory, Action<string> report)
        {
            if (fixtureUris.Length != 5) throw new ArgumentException("Exactly five canonical fixtures required.");
            Directory.CreateDirectory(outputDirectory);
            var device = DeviceFacts.Capture();
            var results = new ParityBatch { commit = Generated.BuildIdentity.Commit, sourceHash = device.sourceHash, sourceDirty = device.sourceDirty, unity = device.unityVersion, backend = device.backend, model = device.model, os = device.os, dataHash = dataHash, results = new FixtureResult[fixtureUris.Length] };
            report("기기 재생 검증 0/5");
            for (var i = 0; i < fixtureUris.Length; i++)
            {
                byte[] bytes;
                using (var request = UnityWebRequest.Get(fixtureUris[i]))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        results.results[i] = new FixtureResult { seed = 30000 + i, error = "fixture-read-failed" };
                        Save(); report("기기 재생 읽기 실패"); yield break;
                    }
                    bytes = request.downloadHandler.data;
                }
                var expectedSeed = 30000 + i;
                var task = Task.Run(() => Verify(catalog, dataHash, bytes, expectedSeed));
                while (!task.IsCompleted) yield return null;
                var result = task.IsFaulted ? new FixtureResult { seed = expectedSeed, error = "verification-worker-failed" } : task.Result;
                results.results[i] = result; Save();
                if (!result.passed) { report("기기 재생 검증 실패: " + result.error); yield break; }
                report($"기기 재생 검증 {i + 1}/5");
            }
            results.passed = true; Save(); report("기기 재생 검증 5/5 통과");
            void Save() => File.WriteAllText(Path.Combine(outputDirectory, "device-parity.json"), JsonUtility.ToJson(results, true));
        }

        public static FixtureResult Verify(ContentCatalog catalog, string dataHash, byte[] bytes, int expectedSeed)
        {
            var result = new FixtureResult { seed = expectedSeed };
            using (var algorithm = SHA256.Create()) result.inputSha256 = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "");
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                {
                    var replay = ReplayCodec.Read(stream);
                    if (replay.Header.Options.Run.Seed != expectedSeed) throw new InvalidDataException("fixture-seed-mismatch");
                    var verified = ReplayRunner.Verify(catalog, dataHash, replay);
                    if (verified.EndKind != ReplayEndKind.Duration || verified.Tick != catalog.Tuning.DurationTicks) throw new InvalidDataException("fixture-duration-mismatch");
                    result.tick = verified.Tick; result.stateHash = verified.StateHash; result.passed = true;
                }
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidDataException || error is InvalidOperationException || error is OverflowException)
            {
                result.error = error.GetType().Name;
            }
            return result;
        }
        [Serializable]
        public sealed class FixtureResult { public int seed, tick; public bool passed; public string inputSha256, stateHash, error; }
        [Serializable]
        sealed class ParityBatch { public string commit, sourceHash; public bool sourceDirty; public bool passed; public string unity, backend, model, os, dataHash; public FixtureResult[] results; }
    }
}
#endif
