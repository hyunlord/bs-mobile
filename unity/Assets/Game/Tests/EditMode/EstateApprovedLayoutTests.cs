using System;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class EstateApprovedLayoutTests
    {
        static string Json => Resources.Load<TextAsset>("EstateAuthoredLayout").text;
        static EstateAuthoredLayout.Document Parse(string json) => EstateAuthoredLayout.Parse(json, CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r8"));
        [Test] public void AddingTheAuthoredGraphicSuppliesItsRequiredCanvasRenderer()
        {
            var owner = new GameObject("authored-terrain-contract", typeof(RectTransform));
            try
            {
                var graphic = owner.AddComponent<EstateAuthoredTerrainGraphic>();
                Assert.That(owner.GetComponent<CanvasRenderer>(), Is.Not.Null);
                Assert.That(graphic.canvasRenderer, Is.SameAs(owner.GetComponent<CanvasRenderer>()));
                owner.SetActive(false);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
        [Test] public void ApprovedProtectedExportContainsAllInstancesAndNoReviewBuildings()
        {
            var doc = Parse(Json);
            Assert.That(doc.instances.Length, Is.EqualTo(2055));
            Assert.That(doc.zones.Length, Is.EqualTo(13));
            Assert.That(doc.instances.Any(i => i.category == "building"), Is.False);
            Assert.That(doc.instances.Any(i => i.zoneId == "exterior"), Is.True);
            Assert.That(doc.instances.Select(i => i.drawOrder).Distinct().Count(), Is.EqualTo(2055));
            Assert.That(EstateAuthoredLayout.Load(CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7")), Is.Null);
        }
        [Test] public void InvalidCoordinatesAssetsDuplicateAndEmptyInputsFailClosed()
        {
            foreach (var change in new Action<EstateAuthoredLayout.Document>[] {
                d => d.instances = Array.Empty<EstateAuthoredLayout.Instance>(),
                d => d.instances[1].id = d.instances[0].id,
                d => d.instances[0].assetId = "unknown",
                d => d.instances[0].scale[0] = 0,
                d => d.instances[0].zoneId = "missing",
                d => d.instances[0].opacity = 2,
                d => d.instances.First(i => i.zoneId != "exterior").grid[0] = -999 })
            {
                var doc = Parse(Json); change(doc);
                Assert.Throws<InvalidOperationException>(() => Parse(JsonUtility.ToJson(doc)));
            }
        }
        [Test] public void RotationPreservesCanvasCentreAndSignedOrderIsNotReplacedByY()
        {
            var asset = new EstateAuthoredLayout.Asset { size = new float[] { 100, 40 }, anchor = new float[] { 10, 30 } };
            var item = new EstateAuthoredLayout.Instance { grid = new float[] { 2,3 }, scale = new float[] { 2,1 }, rotationRadians = Mathf.PI * .5f, drawOrder = -20 };
            var corners = EstateAuthoredLayout.Corners(asset,item);
            var centre = corners.Aggregate(Vector2.zero,(a,b) => a+b) / 4;
            Assert.That(Vector2.Distance(centre, EstateMapModel.Project(new Vector2(2,3)) + new Vector2(80,-10)), Is.LessThan(.001f));
            Assert.That(Vector2.Distance(corners[1]-corners[0], new Vector2(0,200)), Is.LessThan(.001f));
            var doc = new EstateAuthoredLayout.Document { instances = new[] { new EstateAuthoredLayout.Instance { drawOrder = 10 }, item, new EstateAuthoredLayout.Instance { drawOrder = 0 } } };
            Assert.That(EstateAuthoredLayout.Ordered(doc).Select(i => i.drawOrder), Is.EqualTo(new[] { -20,0,10 }));
        }
    }
}
