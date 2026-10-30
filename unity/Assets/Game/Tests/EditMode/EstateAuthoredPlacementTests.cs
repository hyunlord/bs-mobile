using System;
using System.Collections.Generic;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class EstateAuthoredPlacementTests
    {
        static EstateAuthoredPlacements.Document Fixture() => new EstateAuthoredPlacements.Document
        {
            version = "estate-authored-placements-draft-v1", config = "m3a-prototype-r8",
            entries = new[] { new EstateAuthoredPlacements.Entry { id = "fixture-only:one", zoneId = "zone:start",
                propId = "fixture-bright", darkPropId = "fixture-dark", sizeVariant = "M", grid = new[] { 25.25f, 27.5f }, width = 96 } }
        };

        static EstateAuthoredPlacements.Document Parse(EstateAuthoredPlacements.Document doc) =>
            EstateAuthoredPlacements.Parse(JsonUtility.ToJson(doc), CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r8"),
                id => id == "fixture-bright" || id == "fixture-dark");

        [Test] public void AuthoredCoordinatesWidthAndIdentityAreNotAdjustedByView()
        {
            var doc = Parse(Fixture());
            var plan = new EstatePlan();
            EstateAuthoredPlacements.Apply(plan, doc, new HashSet<string> { "zone:start" });
            var item = plan.Items.Single();
            Assert.That(item.Grid, Is.EqualTo(new Vector2(25.25f, 27.5f)));
            Assert.That(item.Width, Is.EqualTo(96));
            Assert.That(item.Key, Is.EqualTo("fixture-only:one"));
            Assert.That(item.Role, Is.EqualTo("fixture-bright"));
            var dark = new EstatePlan();
            EstateAuthoredPlacements.Apply(dark, doc, new HashSet<string>());
            Assert.That(dark.Items.Single().Role, Is.EqualTo("fixture-dark"));
            Assert.That(dark.Items.Single().Grid, Is.EqualTo(item.Grid));
            Assert.That(dark.Items.Single().Width, Is.EqualTo(item.Width));
        }

        [Test] public void DuplicateUnknownAndOutOfZoneInputsFailInsteadOfInventingPlacements()
        {
            var duplicate = Fixture(); duplicate.entries = new[] { duplicate.entries[0], duplicate.entries[0] };
            Assert.Throws<InvalidOperationException>(() => Parse(duplicate));
            var unknown = Fixture(); unknown.entries[0].propId = "missing";
            Assert.Throws<InvalidOperationException>(() => Parse(unknown));
            var outside = Fixture(); outside.entries[0].grid[0] = 999;
            Assert.Throws<InvalidOperationException>(() => Parse(outside));
            var wrongConfig = Fixture(); wrongConfig.config = "m3a-prototype-r7";
            Assert.Throws<InvalidOperationException>(() => Parse(wrongConfig));
            var zero = Fixture(); zero.entries[0].width = 0;
            Assert.Throws<InvalidOperationException>(() => Parse(zero));
            var missing = Fixture(); missing.entries = null;
            Assert.Throws<InvalidOperationException>(() => Parse(missing));
        }
    }
}
