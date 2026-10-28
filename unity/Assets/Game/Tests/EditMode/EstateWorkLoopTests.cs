using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.EditMode
{
    public sealed class EstateWorkLoopTests
    {
        [Test] public void GranaryArrivalPlaysExactlyOneSackCycleThenResumesWalking()
        {
            var route = new EstateCarryRoute(new[] { Vector2.zero, Vector2.right });
            var roundTrip = 2.0 * route.Length / EstateCarryRoute.PixelsPerSecond;
            Assert.That(route.SampleWork(roundTrip - .01).Working, Is.False);
            Assert.That(route.SampleWork(roundTrip).Working, Is.True);
            Assert.That(route.SampleWork(roundTrip).Grid, Is.EqualTo(Vector2.zero));
            Assert.That(route.SampleWork(roundTrip + 2.39).Frame, Is.EqualTo(7));
            Assert.That(route.SampleWork(roundTrip + 2.4 + .01).Working, Is.False);
            Assert.That(route.SampleWork(roundTrip + 1.0), Is.EqualTo(new EstateCarryRoute(route.Points).SampleWork(roundTrip + 1.0)), "Rebuilding must not restart sack arrival.");
        }

        [Test] public void AllResidentKindsShareOneStableSixWorkerBudget()
        {
            var jobs = Enumerable.Range(0, 10).Reverse().Select(i => new EstateResidentJob(i, i % 2 == 0 ? EstateResidentKind.Hoe : EstateResidentKind.Hammer, Vector2.one)).ToList();
            jobs.Add(new EstateResidentJob(0, EstateResidentKind.Carry, Vector2.zero));
            var selected = EstateResidentJob.Select(jobs);
            Assert.That(selected.Length, Is.EqualTo(6));
            Assert.That(selected.Any(j => j.Kind == EstateResidentKind.Carry), Is.True);
            Assert.That(selected.Select(j => j.Key), Is.EqualTo(EstateResidentJob.Select(jobs.AsEnumerable().Reverse()).Select(j => j.Key)));
            var plan = new EstatePlan(); plan.Residents.AddRange(jobs);
            plan.Items.Add(new EstateDrawItem { Source = "peasant", Key = "idle-granary" });
            plan.Items.Add(new EstateDrawItem { Source = "peasant", Key = "fallback-carry" });
            plan.Items.Add(new EstateDrawItem { Source = "peasant", Key = "placed-worker" });
            EstateResidentJob.ApplyBudget(plan);
            Assert.That(plan.Residents.Count + plan.Items.Count(i => i.Source == "peasant"), Is.EqualTo(6));
        }

        [Test] public void NativeLoopCanvasesPlaceEveryFootAtTheRequestedAnchor()
        {
            foreach (var kind in new[] { EstateResidentKind.Hoe, EstateResidentKind.Hammer, EstateResidentKind.Sack })
            {
                var size = EstateResidentJob.Canvas(kind);
                Assert.That(size.x, Is.GreaterThan(0)); Assert.That(size.y, Is.GreaterThan(0));
            }
            Assert.That(EstateResidentJob.Canvas(EstateResidentKind.Hammer), Is.EqualTo(new Vector2(64, 76)));
            Assert.That(EstateResidentJob.Canvas(EstateResidentKind.Hoe), Is.EqualTo(new Vector2(96, 96)));
        }

        [Test] public void CompletedFieldAndConstructionReplaceStaticWorkingResidents()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var state = AccountEstate.NewPrototype(catalog, config, 0, 0);
            var field = state.Estate.Buildings.Single(b => b.Kind == "m3a:field");
            var granary = state.Estate.Buildings.Single(b => b.Kind == "meta:granary");
            var fresh = EstatePlanner.Plan(EstateDecorRules.Load(), EstateArt.Load(), config, state.Estate);
            Assert.That(fresh.Residents.Any(j => j.Kind == EstateResidentKind.Hoe), Is.False);
            var fixture = state.Estate with { Buildings = state.Estate.Buildings.Select(b => b.Id == field.Id || b.Id == granary.Id ? b with { Level = 1 } : b).ToArray(), Active = new MetaEstateWork(granary.Id, 2, 100, 50) };
            var plan = EstatePlanner.Plan(EstateDecorRules.Load(), EstateArt.Load(), config, fixture);
            Assert.That(plan.Residents.Count(j => j.Kind == EstateResidentKind.Hoe), Is.EqualTo(1));
            Assert.That(plan.Residents.Count(j => j.Kind == EstateResidentKind.Hammer), Is.EqualTo(1));
            Assert.That(plan.Items.Any(i => i.Role == "construction-worker-hammer" || i.Role == "peasant-farm"), Is.False);
            Assert.That(plan.Residents.Count + plan.Items.Count(i => i.Source == "peasant"), Is.LessThanOrEqualTo(6));
        }

        [Test] public void CarrierAndSackSpriteChangesKeepTheirAuthoredFootAtRoutePoint()
        {
            var owner = new GameObject("worker-foot", typeof(RectTransform));
            try
            {
                var model = new EstateMapModel(20, 20); model.SetFrame(new Vector2(390, 844), 0, 0);
                var points = new[] { Vector2.zero, new Vector2(2, 0) }; var route = new EstateCarryRoute(points);
                var view = owner.AddComponent<EstateCarryView>();
                view.Configure(EstateArt.Load(), model, points, new System.Collections.Generic.List<(RectTransform Rect, float Y)>());
                var roundTrip = 2.0 * route.Length / EstateCarryRoute.PixelsPerSecond;
                foreach (var time in new[] { 0.1, roundTrip * .75, roundTrip + .01, roundTrip + 2.39, roundTrip + 2.41 })
                {
                    view.RefreshAt(time); var rect = (RectTransform)owner.transform; var sprite = owner.GetComponent<Image>().sprite;
                    var pivot = sprite.pivot / sprite.rect.size;
                    var foot = new Vector2(rect.anchoredPosition.x + pivot.x * rect.sizeDelta.x, -rect.anchoredPosition.y + (1-pivot.y) * rect.sizeDelta.y);
                    Assert.That(Vector2.Distance(foot, model.GridToViewport(route.SampleWork(time).Grid)), Is.LessThan(.001f));
                }
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}
