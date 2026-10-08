using System.Collections;
using System.IO;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class WorldAnnouncementsRenderTests
    {
        [UnityTest]
        public IEnumerator AuthoredSeasonBannerRendersIntoCameraTargetAtBothAspects()
        {
            foreach (var dimensions in new[] { new Vector2Int(360, 640), new Vector2Int(640, 640) })
            {
                var owner = new GameObject("Announcement capture camera"); var camera = owner.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.transform.position = new Vector3(0, 0, -10);
                var target = new RenderTexture(dimensions.x, dimensions.y, 24); camera.targetTexture = target;
                using (var notices = new WorldAnnouncements(camera, ArtCatalog.Load()))
                {
                    notices.ShowSeason(2);
                    notices.Present(0, new Rect(0, 0, dimensions.x, dimensions.y), new Vector2(dimensions.x / 2, dimensions.y - 20));
                    Canvas.ForceUpdateCanvases(); yield return null; yield return null; yield return null;
                    var previous = RenderTexture.active; RenderTexture.active = target;
                    var capture = new Texture2D(dimensions.x, dimensions.y, TextureFormat.RGB24, false);
                    capture.ReadPixels(new Rect(0, 0, dimensions.x, dimensions.y), 0, 0); capture.Apply(); RenderTexture.active = previous;
                    var visible = 0; var upper = 0;
                    for (var y = 0; y < dimensions.y; y++)
                        for (var x = 0; x < dimensions.x; x++)
                        {
                            var pixel = capture.GetPixel(x, y); if (pixel.maxColorComponent < 0.1f) continue;
                            if (y < dimensions.y / 3) visible++; else if (y > dimensions.y * 2 / 3) upper++;
                        }
                    Assert.That(visible, Is.GreaterThan(100), "Authored banner must render into targetTexture, not only an uncaptured overlay canvas.");
                    Assert.That(upper, Is.Zero, "Upper hero region must remain clear.");
                    var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/phase1b/m2-runtime")); Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, "announcement-bottom-" + dimensions.x + "x" + dimensions.y + ".png"), capture.EncodeToPNG());
                    Object.Destroy(capture);
                }
                camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(owner); yield return null;
            }
        }
    }
}
