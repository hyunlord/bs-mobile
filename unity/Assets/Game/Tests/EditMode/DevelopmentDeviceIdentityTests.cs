using System;
using System.Collections;
using Game.App;
using Game.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode
{
    public sealed class DevelopmentDeviceIdentityTests
    {
        [Test] public void EditorSelectsDevelopmentBackendBeforeAnyNativeAttempt()
        {
            Assert.That(DeviceIdentityVaultFactory.DevelopmentEnabled, Is.True);
            Assert.That(DeviceIdentityVaultFactory.Create("http://127.0.0.1:5082"), Is.TypeOf<DevelopmentDeviceIdentityVault>());
            Assert.Throws<ArgumentException>(() => DeviceIdentityVaultFactory.Create("https://example.invalid"));
        }
        [Test] public void DevelopmentMemoryScopesPersistWithinProcessAndStaySeparate()
        {
            var scope = Guid.NewGuid().ToString("N");
            var first = new DevelopmentDeviceIdentityVault("http://127.0.0.1:5082", scope);
            Assert.That(first.Load(), Is.Empty); first.Save("synthetic-only");
            Assert.That(new DevelopmentDeviceIdentityVault("http://127.0.0.1:5082", scope).Load(), Is.EqualTo("synthetic-only"));
            Assert.That(new DevelopmentDeviceIdentityVault("http://127.0.0.1:5083", scope).Load(), Is.Empty);
            Assert.That(new DevelopmentDeviceIdentityVault("http://127.0.0.1:5082", scope + "other").Load(), Is.Empty);
        }
        [UnityTest] public IEnumerator DevelopmentIdentitySignsAndOptionallyVerifiesLocalServerLifecycle()
        {
            var vault = new DevelopmentDeviceIdentityVault("http://127.0.0.1:5082", Guid.NewGuid().ToString("N"));
            Assert.That(vault.PublicKeySpki().Length, Is.EqualTo(91));
            Assert.That(vault.Sign(DeviceSignature.Message("11111111-1111-1111-1111-111111111111", "probe-nonce")).Length, Is.EqualTo(64));
            var endpoint = Environment.GetEnvironmentVariable("SOW_DEV_AUTH_PROBE_ENDPOINT");
            if (!string.IsNullOrEmpty(endpoint))
            {
                var id = Environment.GetEnvironmentVariable("SOW_DEV_AUTH_PROBE_ID");
                var output = Environment.GetEnvironmentVariable("SOW_DEV_AUTH_PROBE_OUTPUT");
                Assert.That(id, Is.Not.Null.And.Not.Empty); Assert.That(output, Is.Not.Null.And.Not.Empty);
                yield return RunEditor(DeviceAuthRuntimeProbe.DevelopmentFlow(endpoint, id, output));
            }
        }
        static IEnumerator RunEditor(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested) yield return RunEditor(nested);
                else if (routine.Current is AsyncOperation operation) while (!operation.isDone) yield return null;
                else yield return null;
            }
        }
    }
}
