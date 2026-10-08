using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class WorldAnnouncementsTests
    {
        [Test]
        public void BossTakesPriorityThenDeferredSeasonExpiresAndAvoidsUpperHero()
        {
            var owner = new GameObject("Announcement test camera"); var camera = owner.AddComponent<Camera>();
            using (var notices = new WorldAnnouncements(camera, ArtCatalog.Load()))
            {
                Assert.That(notices.Visible, Is.False, "Initial snapshot must not invent a season transition.");
                var catalog = CanonicalContent.CreateCatalog();
                var bossId = ""; foreach (var enemy in catalog.FirstPlayable.Enemies) if (enemy.Value.Rank == "boss") bossId = enemy.Key;
                notices.ShowSeason(1); notices.ShowBoss(bossId); notices.ShowSeason(3);
                notices.Present(0.1f, new Rect(0, 0, 900, 1600), new Vector2(450, 1500));
                Assert.That(notices.Message, Is.EqualTo("보스 출현")); Assert.That(notices.AtBottom, Is.True);
                notices.Present(1.8f, new Rect(0, 0, 900, 1600), new Vector2(450, 300));
                Assert.That(notices.Message, Is.EqualTo("겨울")); Assert.That(notices.Visible, Is.True); Assert.That(notices.AtBottom, Is.False);
                notices.Present(1.8f, new Rect(0, 0, 900, 1600), Vector2.zero);
                Assert.That(notices.Visible, Is.False);
            }
            Object.DestroyImmediate(owner);
        }
    }
}
