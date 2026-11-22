using System;
using System.Collections.Generic;
using System.IO;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor
{
    // Explicit offline Editor review; this does not select or enable an SLG hero in gameplay.
    public static class SlgHeroPlaybackCapture
    {
        public static void Run()
        {
            var output = Environment.GetEnvironmentVariable("SLG_HERO_CAPTURE_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("SLG_HERO_CAPTURE_OUTPUT is required.");
            Directory.CreateDirectory(output);
            var scene = EditorSceneManager.NewPreviewScene();
            var previousProfile = ArtCatalog.ProfileName;
            var previousActive = RenderTexture.active;
            var previousAsync = ShaderUtil.allowAsyncCompilation;
            var sprites = new List<Sprite>();
            var target = new RenderTexture(1600, 780, 24);
            var image = new Texture2D(1600, 780, TextureFormat.RGB24, false);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                ArtCatalog.ProfileName = Environment.GetEnvironmentVariable("UNITY_CONTENT_PROFILE") ?? "first-playable";
                ArtCatalog.InvalidateCache();
                var catalog = ArtCatalog.Load();
                var heroes = new[] { "field_guide", "river_warden", "ruin_seeker" };
                var states = new[] { "idle", "walk", "hit", "death", "attack" };
                var renderers = new List<SpriteRenderer>();
                var visuals = new List<ArtVisual>();
                var clips = new List<Sprite[]>();
                var rows = new List<string> { "profile,hero,state,frames,frameMs" };
                for (var row = 0; row < heroes.Length; row++)
                for (var column = 0; column < states.Length; column++)
                {
                    if (row == 2 && column == 4)
                    {
                        if (catalog.TryResolveRole("hero.slg.ruin_seeker.attack", out _)) throw new InvalidOperationException("Unexpected ruin seeker attack.");
                        continue;
                    }
                    var visual = catalog.Resolve("hero", "slg:" + heroes[row], states[column]);
                    var owner = new GameObject(visual.RoleId);
                    SceneManager.MoveGameObjectToScene(owner, scene);
                    owner.transform.position = new Vector3(column * 3.2f - 6.4f, 2.6f - row * 2.6f - .7f, 0);
                    var renderer = owner.AddComponent<SpriteRenderer>();
                    var frames = new Sprite[visual.UvRects.Count];
                    for (var i = 0; i < frames.Length; i++)
                    {
                        var uv = visual.UvRects[i];
                        frames[i] = Sprite.Create(visual.Texture, new Rect(uv.x * visual.Texture.width, uv.y * visual.Texture.height, uv.width * visual.Texture.width, uv.height * visual.Texture.height), visual.Pivot, 100, 0, SpriteMeshType.FullRect);
                        sprites.Add(frames[i]);
                    }
                    renderers.Add(renderer); visuals.Add(visual); clips.Add(frames);
                    rows.Add(ArtCatalog.ProfileName + "," + heroes[row] + "," + states[column] + "," + frames.Length + "," + visual.FrameMs);
                }
                var cameraOwner = new GameObject("Hero playback review camera");
                SceneManager.MoveGameObjectToScene(cameraOwner, scene);
                var camera = cameraOwner.AddComponent<Camera>();
                camera.scene = scene;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true; camera.orthographicSize = 3.9f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(40, 45, 48, 255);
                camera.targetTexture = target;
                for (var frame = 0; frame < 180; frame++)
                {
                    for (var i = 0; i < renderers.Count; i++)
                        renderers[i].sprite = clips[i][(frame * 1000 / 60 / visuals[i].FrameMs) % clips[i].Length];
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                    File.WriteAllBytes(Path.Combine(output, "frame-" + frame.ToString("D4") + ".png"), image.EncodeToPNG());
                }
                File.WriteAllLines(Path.Combine(output, "roles.csv"), rows);
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: 14 runtime bindings, 180 offline Editor frames at 60 samples/s. Rows: field_guide, river_warden, ruin_seeker. Columns: idle, walk, hit, death, attack (ruin_seeker empty). Not live gameplay or C05 evidence.");
            }
            finally
            {
                ArtCatalog.ProfileName = previousProfile;
                RenderTexture.active = previousActive;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                foreach (var sprite in sprites) UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(image); target.Release(); UnityEngine.Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
