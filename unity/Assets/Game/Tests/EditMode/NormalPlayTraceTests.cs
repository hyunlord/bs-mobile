using Game.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Tests.EditMode
{
    public sealed class NormalPlayTraceTests
    {
        [Test]
        public void ScriptedUiFilterExcludesPhysicalPointerBindingsAndRestoresOriginalAsset()
        {
            var owner = new GameObject("Isolated trace UI", typeof(EventSystem));
            var physical = InputSystem.AddDevice<Mouse>("Regression physical mouse");
            var synthetic = InputSystem.AddDevice<Mouse>("Regression trace mouse");
            NormalPlayTrace.ScriptedUiDeviceFilter filter = null;
            InputActionAsset original = null;
            UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>? priorDevices = null;
            try
            {
                var module = owner.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
                original = module.actionsAsset;
                priorDevices = original.devices;
                original.devices = new InputDevice[] { physical };
                filter = new NormalPlayTrace.ScriptedUiDeviceFilter(module, synthetic);
                Assert.That(module.actionsAsset, Is.Not.SameAs(original));
                Assert.That(original.devices.Value.Count, Is.EqualTo(1));
                Assert.That(original.devices.Value[0], Is.SameAs(physical));
                foreach (var action in new[] { module.point.action, module.leftClick.action })
                {
                    Assert.That(action.controls.Count, Is.GreaterThan(0));
                    foreach (var control in action.controls)
                        Assert.That(control.device, Is.SameAs(synthetic), "Physical mouse events must not resolve into the module's pointer or held-button actions.");
                }
                filter.Dispose(); filter.Dispose();
                Assert.That(module.actionsAsset, Is.SameAs(original));
                Assert.That(original.devices.Value[0], Is.SameAs(physical));
                foreach (var control in module.point.action.controls) Assert.That(control.device, Is.SameAs(physical));
            }
            finally
            {
                filter?.Dispose();
                if (original != null) original.devices = priorDevices;
                Object.DestroyImmediate(owner);
                InputSystem.RemoveDevice(synthetic);
                InputSystem.RemoveDevice(physical);
            }
        }

        [Test]
        public void ScriptedUiFilterRejectsMissingInitializedModule()
            => Assert.Throws<System.InvalidOperationException>(() => new NormalPlayTrace.ScriptedUiDeviceFilter(null, null));

        [Test]
        public void OptionalSeedUsesOnlyExplicitNormalTraceConfiguration()
        {
            Assert.That(NormalPlayTrace.ParseRequestedSeed(new[] { "app" }), Is.Null);
            Assert.That(NormalPlayTrace.ParseRequestedSeed(new[] { "app", "--smoothness-trace", "--smoothness-seed", "1078312934" }), Is.EqualTo(1078312934));
            Assert.Throws<System.ArgumentException>(() => NormalPlayTrace.ParseRequestedSeed(new[] { "--smoothness-seed", "1" }));
            Assert.Throws<System.ArgumentException>(() => NormalPlayTrace.ParseRequestedSeed(new[] { "--smoothness-trace", "--smoothness-seed" }));
            Assert.Throws<System.ArgumentException>(() => NormalPlayTrace.ParseRequestedSeed(new[] { "--smoothness-trace", "--smoothness-seed", "2147483648" }));
        }

        [TestCase(false, 1f, 0f)]
        [TestCase(true, .8f, .6f)]
        public void ScriptDirectionPreservesMagnitudeAcrossCardinalAndObliqueProtocols(bool oblique, float x, float y)
        {
            var direction = NormalPlayTrace.ScriptDirection(oblique);
            Assert.That(direction.x, Is.EqualTo(x));
            Assert.That(direction.y, Is.EqualTo(y));
            Assert.That(direction.magnitude, Is.EqualTo(1).Within(.000001));
            Assert.That((direction * .25f).magnitude, Is.EqualTo(.25f).Within(.000001));
        }

        [TestCase(false, -1f, 0f)]
        [TestCase(true, -.8f, -.6f)]
        public void LeftOptionReversesDirectionWithoutChangingMagnitude(bool oblique,float x,float y)
        {
            var direction=NormalPlayTrace.ScriptDirection(oblique,true);
            Assert.That(direction.x,Is.EqualTo(x));
            Assert.That(direction.y,Is.EqualTo(y));
            Assert.That(direction.magnitude,Is.EqualTo(1).Within(.000001));
            Assert.That(direction,Is.EqualTo(-NormalPlayTrace.ScriptDirection(oblique)));
        }

        [Test]
        public void StartupLoggingAcceptsADestroyedSelectedUiObject()
        {
            var selection = new UnityEngine.GameObject("destroyed selection");
            Assert.That(NormalPlayTrace.ObservedObjectName(selection), Is.EqualTo("destroyed selection"));
            UnityEngine.Object.DestroyImmediate(selection);
            Assert.That(NormalPlayTrace.ObservedObjectName(selection), Is.EqualTo("none"));
            Assert.That(NormalPlayTrace.ObservedObjectName(null), Is.EqualTo("none"));
        }

        [TestCase(-1, -1)]
        [TestCase(0, 0)]
        [TestCase(3.999, 0)]
        [TestCase(4, 1)]
        [TestCase(8, 2)]
        [TestCase(12, 3)]
        [TestCase(16, 4)]
        [TestCase(18, 4)]
        public void ScriptedSegmentsHaveExplicitFourSecondMovementAndReleaseWindows(double seconds, int phase)
            => Assert.That(NormalPlayTrace.MovementPhase(seconds), Is.EqualTo(phase));
    }
}
