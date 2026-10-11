using Game.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.EditMode
{
    public sealed class PortraitCardActionsTests
    {
        [Test]
        public void InputGateRestoresAvailabilityWithoutGrantingSpentQuotas()
        {
            var owner = new GameObject("Card gate test");
            try
            {
                var choose = Create(owner.transform, "Choose");
                var spentBan = Create(owner.transform, "Ban");
                var pinned = Create(owner.transform, "Already pinned");
                var reroll = Create(owner.transform, "Reroll");
                var actions = new PortraitCardActions();
                actions.Add(choose, true); actions.Add(spentBan, false);
                actions.Add(pinned, false); actions.Add(reroll, true);
                actions.SetActionsEnabled(false);
                foreach (var button in actions.Buttons) Assert.That(button.interactable, Is.False);
                actions.SetActionsEnabled(true);
                Assert.That(choose.interactable, Is.True);
                Assert.That(reroll.interactable, Is.True);
                Assert.That(spentBan.interactable, Is.False);
                Assert.That(pinned.interactable, Is.False);
                actions.SetActionsEnabled(false); actions.SetActionsEnabled(true);
                Assert.That(spentBan.interactable, Is.False);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void ControlsAddedDuringGateStayDisabledAndDisposedViewsAreSafe()
        {
            var owner = new GameObject("Late card action");
            var actions = new PortraitCardActions();
            actions.SetActionsEnabled(false);
            var choose = Create(owner.transform, "Choose"); actions.Add(choose, true);
            Assert.That(choose.interactable, Is.False);
            Object.DestroyImmediate(owner);
            Assert.DoesNotThrow(() => actions.SetActionsEnabled(true));
        }

        static Button Create(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Button));
            child.transform.SetParent(parent, false); return child.GetComponent<Button>();
        }
    }
}
