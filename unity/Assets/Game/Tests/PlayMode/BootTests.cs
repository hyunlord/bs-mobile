using System.Collections;
using Game.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class BootTests
    {
        [UnityTest]
        public IEnumerator BootVerifiesActualBytesBeforeEnteringMeta()
        {
            yield return SceneManager.LoadSceneAsync("Boot");
            var deadline = Time.realtimeSinceStartup + 30;
            while (SceneManager.GetActiveScene().name != "Meta" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Meta"));
            Assert.That(FoundationBoot.Catalog, Is.Not.Null);
            Assert.That(FoundationBoot.VerifiedDataHash, Has.Length.EqualTo(64));
            foreach (var boot in Object.FindObjectsByType<FoundationBoot>()) Object.Destroy(boot.gameObject);
        }
    }
}
