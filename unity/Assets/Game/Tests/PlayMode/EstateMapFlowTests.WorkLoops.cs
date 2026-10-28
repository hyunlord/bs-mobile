#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed partial class EstateMapFlowTests
    {
        [UnityTest, Timeout(600000)]
        public IEnumerator ApprovedWorkingResidentsUseOneBudgetAndAnExplicitOfflineReviewClock()
        {
            var catalog = CanonicalContent.CreateMetaCatalog();
            var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var fixture = AccountEstate.Migrate(catalog, config, MetaEngine.NewGame(catalog), 0, 0);
            var construction = fixture.Estate.Buildings.Single(b => b.Kind == "meta:forge");
            fixture = fixture with { Estate = fixture.Estate with {
                Buildings = fixture.Estate.Buildings.Select(b => b.Kind == "meta:granary" || b.Kind == "m3a:field" ? b with { Level = 1, Stock = 1 } : b).ToArray(),
                Active = new MetaEstateWork(construction.Id, 1, 100000, 100000)
            } };
            SaveFixture(catalog, fixture);
            PlayerPrefs.SetInt(prefix + "estatePrototype", 1); PlayerPrefs.Save();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("EstateMap");
            var account = MetaSaveCodec.Encode(app.Progression.State);
            var residents = app.Ui.GetComponentsInChildren<EstateCarryView>();
            Assert.That(residents.Length, Is.EqualTo(3));
            Assert.That(residents.Length, Is.LessThanOrEqualTo(EstateResidentJob.VisibleLimit));
            Assert.That(residents.Count(v => v.name == "EstateMap.CarryWorker"), Is.EqualTo(1));
            yield return RecordEstateWorkLoopBatches(residents);
            yield return Capture("estate-work-loops-editor-fixture");
            var output = Environment.GetEnvironmentVariable("ESTATE_WORK_LOOPS_VIDEO");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                var planned = app.EstateMap.Plan(EstateDecorRules.Load(), EstateArt.Load());
                var route = new EstateCarryRoute(planned.CarryRoute);
                var reviewStart = Math.Max(0, 2.0 * route.Length / EstateCarryRoute.PixelsPerSecond - 8);
                var previousRate = Time.captureFramerate;
                Time.captureFramerate = 60;
                foreach (var resident in residents) resident.enabled = false;
                try
                {
                    yield return PlayModeCaptureGuard.WaitForShaders("estate-work-loops-offline");
                    // Editor fixture only. Freeze the review clock explicitly; no capture or production time claim.
                    for (var frame = 0; frame < 1800; frame++)
                    {
                        foreach (var resident in residents) resident.RefreshAt(reviewStart + frame / 60.0);
                        Canvas.ForceUpdateCanvases(); yield return null;
                        var probe = PlayModeCaptureGuard.Begin("estate-work-loops-" + frame);
                        var image = ScreenCapture.CaptureScreenshotAsTexture();
                        try {
                            var path = Path.Combine(output, frame.ToString("D5") + ".png");
                            PlayModeCaptureGuard.Check(image, probe, path, ShaderUtil.anythingCompiling);
                            File.WriteAllBytes(path, image.EncodeToPNG());
                        } finally { Object.Destroy(image); }
                    }
                    File.WriteAllText(Path.Combine(output, "provenance.txt"), $"Editor saved-state fixture; completed field/granary plus long-running forge construction; 1800 frames at 60fps; explicit View clock start={reviewStart}; eight seconds before first granary arrival; no native normal-progression or performance claim.\n");
                }
                finally { Time.captureFramerate = previousRate; foreach (var resident in residents) if (resident != null) resident.enabled = true; }
            }
            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(account));
        }

        IEnumerator RecordEstateWorkLoopBatches(EstateCarryView[] residents)
        {
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/work-loops-466"));
            Directory.CreateDirectory(output);
            var csv = new System.Text.StringBuilder("seconds,materialRuns,batches,drawCalls,setPass\n");
            foreach (var resident in residents) resident.enabled = false;
            try
            {
                for (var seconds = 0; seconds <= 30; seconds += 5)
                {
                    foreach (var resident in residents) resident.RefreshAt(seconds);
                    Canvas.ForceUpdateCanvases();
                    var counts = new int[3]; yield return Batches(counts);
                    csv.AppendLine($"{seconds},{EstateMapView.MaterialBreaks(View().transform)},{counts[0]},{counts[1]},{counts[2]}");
                }
                File.WriteAllText(Path.Combine(output, "active-fixture-batches.csv"), csv.ToString());
            }
            finally { foreach (var resident in residents) if (resident != null) resident.enabled = true; }
        }
    }
}
#endif
