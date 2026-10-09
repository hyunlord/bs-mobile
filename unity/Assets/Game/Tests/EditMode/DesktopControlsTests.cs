using Game.Input;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class DesktopControlsTests
    {
        [Test]
        public void KeyboardDiagonalDoesNotMoveFasterThanCardinal()
        {
            var cardinal = DesktopControls.ResolveMovement(default, false, Vector2.right, false);
            var diagonal = DesktopControls.ResolveMovement(default, false, Vector2.one, false);
            Assert.That(cardinal.X, Is.EqualTo(PlayerInput.Scale));
            Assert.That(cardinal.Y, Is.Zero);
            Assert.That(diagonal.X, Is.EqualTo(diagonal.Y));
            Assert.That(Mathf.Sqrt((float)diagonal.X * diagonal.X + (float)diagonal.Y * diagonal.Y),
                Is.EqualTo(PlayerInput.Scale).Within(1));
        }

        [Test]
        public void PointerOwnsMovementUntilReleasedAndPauseBlocksBothDevices()
        {
            var pointer = new PlayerInput(-300, 400);
            Assert.That(DesktopControls.ResolveMovement(pointer, true, Vector2.right, false), Is.EqualTo(pointer));
            Assert.That(DesktopControls.ResolveMovement(default, true, Vector2.right, false), Is.EqualTo(default(PlayerInput)));
            Assert.That(DesktopControls.ResolveMovement(pointer, true, Vector2.right, true), Is.EqualTo(default(PlayerInput)));
            Assert.That(DesktopControls.ResolveMovement(default, false, Vector2.right, true), Is.EqualTo(default(PlayerInput)));
        }

        [Test]
        public void HeldKeyboardMustBeReleasedAfterAModalOrFocusPause()
        {
            var controls = new DesktopControls();
            Assert.That(controls.ResolveInput(default, false, Vector2.right, false).X, Is.EqualTo(PlayerInput.Scale));
            Assert.That(controls.ResolveInput(default, false, Vector2.right, true), Is.EqualTo(default(PlayerInput)));
            Assert.That(controls.ResolveInput(default, false, Vector2.right, false), Is.EqualTo(default(PlayerInput)));
            controls.ResolveInput(default, false, Vector2.zero, false);
            Assert.That(controls.ResolveInput(default, false, Vector2.right, false).X, Is.EqualTo(PlayerInput.Scale));
            controls.Reset();
            Assert.That(controls.ResolveInput(default, false, Vector2.right, false), Is.EqualTo(default(PlayerInput)),
                "Focus callbacks must block held keys even when Unity did not Update while unfocused.");
        }

        [TestCase(900, 1950, 1512, 982)]
        [TestCase(1080, 2520, 1512, 982)]
        [TestCase(2184, 1968, 1512, 982)]
        [TestCase(900, 1950, 800, 600)]
        public void PresetsFitDesktopAndKeepTheirAspect(int width, int height, int desktopWidth, int desktopHeight)
        {
            var actual = DesktopControls.FitWindow(new Vector2Int(width, height), new Vector2Int(desktopWidth, desktopHeight));
            Assert.That(actual.x, Is.InRange(1, desktopWidth - 80));
            Assert.That(actual.y, Is.InRange(1, desktopHeight - 120));
            Assert.That((double)actual.x / actual.y, Is.EqualTo((double)width / height).Within(0.002));
        }
    }
}
