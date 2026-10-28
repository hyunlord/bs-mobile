#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed partial class EstateMapFlowTests
    {
        [UnityTest, Timeout(180000)]
        public IEnumerator HistoricalR7SaveKeepsItsBytesAndGridThroughTheRealEstateEntry() => HistoricalSave("m3a-prototype-r7", 12);

        [UnityTest, Timeout(180000)]
        public IEnumerator R8HalfRestoredMapUsesNormalProductionAndUnlockCommands() => CaptureR8Progress(7, "half-7-of-13");

        [UnityTest, Timeout(180000)]
        public IEnumerator R8AllThirteenZonesOpenThroughNormalProductionAndUnlockCommands() => CaptureR8Progress(13, "full-13-of-13");

        IEnumerator CaptureR8Progress(int target, string label)
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig();
            Assert.That(config.Version, Is.EqualTo(EstateR8Layout.Version));
            long clock = 0; int claims = 0;
            var state = AccountEstate.NewPrototype(catalog, config, 0, 0);
            void Advance(long seconds)
            {
                clock += seconds;
                var advanced = AccountEstate.Advance(catalog, config, state, clock, clock);
                Assert.That(advanced.Clock, Is.EqualTo(AccountEstateClock.Accepted)); state = advanced.State;
            }
            foreach (var kind in new[] { "meta:granary", "m3a:field", "m3a:lumber_camp", "meta:forge", "m3a:ironworks" })
            {
                var building = state.Estate.Buildings.Single(b => b.Kind == kind);
                var build = AccountEstate.StartConstruction(catalog, config, state, clock, clock, building.Id);
                Assert.That(build.Done, Is.True, kind + ": " + build.Reason); state = build.State;
                Advance(state.Estate.Active.RemainingSeconds);
            }
            bool Adjacent(AccountEstateZone a, AccountEstateZone b) =>
                (a.X + a.Width == b.X || b.X + b.Width == a.X) && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height ||
                (a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && a.X < b.X + b.Width && b.X < a.X + a.Width;
            while (state.Estate.OpenZones.Length < target)
            {
                var zone = config.Zones.First(z => !state.Estate.OpenZones.Contains(z.Id) && config.Zones.Any(o => state.Estate.OpenZones.Contains(o.Id) && Adjacent(o, z)));
                while (state.Wallet["meta:timber"] < zone.Unlock.Cost["meta:timber"])
                {
                    Assert.That(clock, Is.LessThan(86400)); Advance(60);
                    var claim = AccountEstate.ClaimProduction(catalog, config, state, clock, clock);
                    Assert.That(claim.Done, Is.True, claim.Reason); state = claim.State; claims++;
                }
                var unlock = AccountEstate.StartZoneUnlock(catalog, config, state, clock, clock, zone.Id);
                Assert.That(unlock.Done, Is.True, zone.Id + ": " + unlock.Reason); state = unlock.State;
                Advance(zone.Unlock.Seconds);
            }
            Assert.That(claims, Is.GreaterThan(1)); Assert.That(state.Estate.Buildings.Length, Is.EqualTo(10));
            SaveFixture(catalog, state);
            PlayerPrefs.SetInt(prefix + "estatePrototype", 1); PlayerPrefs.Save();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("EstateMap");
            Assert.That(app.EstateMap.State.Estate.OpenZones.Length, Is.EqualTo(target));
            Assert.That(app.EstateMap.FromSave, Is.True);
            ZoomTo(app.EstateMap.Map.WholeMap);
            var openedArea = config.Zones.Where(z => state.Estate.OpenZones.Contains(z.Id)).Sum(z => z.Width * z.Height);
            Debug.Log($"r8 {label} area={openedArea}/1664: normal Core commands with simulated clock, elapsed={clock}s claims={claims}; 10 authored sites, not the concept's future developed building count.");
            yield return Capture("estate-r8-" + label);
        }
    }
}
#endif
