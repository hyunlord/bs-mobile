#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Game.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Tests.PlayMode
{
    // Opt-in experiment only. Registry references are restored in finally; no asset is marked dirty or saved.
    internal sealed class AstcTextureTrial : IDisposable
    {
        static readonly string[] Targets = { "hero-knight", "fallow-enemy-rigs", "feel-hero-b-layered" };
        readonly List<Texture2D> owned = new List<Texture2D>();
        ArtRegistry registry;
        Texture2D[] originals;
        [Serializable] sealed class Row
        {
            public string atlas, mode, format, graphicsDevice, graphicsApi, evidence;
            public int width, height, block, mipCount;
            public long rawBytes, expectedBytes;
            public bool gpuSupported;
        }
        [Serializable] sealed class InventoryRow
        {
            public string path, format;
            public int width, height, mipCount;
            public bool readable;
            public long runtimeBytes;
        }
        [Serializable] sealed class Inventory
        {
            public List<InventoryRow> textures = new List<InventoryRow>();
        }
        public static AstcTextureTrial OpenFromEnvironment()
        {
            var mode = Environment.GetEnvironmentVariable("ASTC_TRIAL_MODE");
            if (string.IsNullOrEmpty(mode)) return null;
            Assert.That(mode == "rgba" || mode == "4" || mode == "6", Is.True, "ASTC_TRIAL_MODE=rgba|4|6");
            Assert.That(ArtCatalog.UsesWaveArt, Is.True, "Trial targets require wave-1a registry.");
            var directory = Environment.GetEnvironmentVariable("ASTC_TRIAL_OUTPUT");
            Assert.That(!string.IsNullOrWhiteSpace(directory), Is.True, "Explicit local trial output required.");
            Directory.CreateDirectory(directory);
            Assert.That(Directory.GetFileSystemEntries(directory), Is.Empty, "Keep each trial in a fresh directory.");
            var trial = new AstcTextureTrial();
            try { trial.Prepare(mode, directory); return trial; }
            catch { trial.Dispose(); throw; }
        }
        void Prepare(string mode, string directory)
        {
            registry = Resources.Load<ArtRegistry>("Wave1aArt");
            Assert.That(registry, Is.Not.Null);
            originals = registry.atlases;
            var replacement = (Texture2D[])originals.Clone();
            var manifest = ArtCatalog.Parse(registry.manifest.text);
            var block = mode == "rgba" ? 0 : int.Parse(mode);
            var format = block == 4 ? TextureFormat.ASTC_4x4 : block == 6 ? TextureFormat.ASTC_6x6 : TextureFormat.RGBA32;
            var supported = SystemInfo.SupportsTextureFormat(format);
            File.WriteAllText(Path.Combine(directory, "environment.txt"), $"unity={Application.unityVersion}\nmode={mode}\nformat={format}\ngpuSupported={supported}\ndevice={SystemInfo.graphicsDeviceName}\napi={SystemInfo.graphicsDeviceType}\nclassification=Editor actual compression quality trial; not mobile memory measurement\n");
            Assert.That(supported, Is.True, "No supported GPU decode path; do not substitute original pixels.");
            var inventoryPath = Environment.GetEnvironmentVariable("ASTC_TRIAL_INVENTORY_PATHS");
            if (!string.IsNullOrEmpty(inventoryPath))
            {
                var inventory = new Inventory();
                foreach (var assetPath in File.ReadAllLines(inventoryPath))
                {
                    if (string.IsNullOrWhiteSpace(assetPath)) continue;
                    Assert.That(assetPath.StartsWith("Assets/", StringComparison.Ordinal) && assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !assetPath.Contains("..") && File.Exists(assetPath), Is.True);
                    var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    Assert.That(asset, Is.Not.Null, assetPath);
                    inventory.textures.Add(new InventoryRow { path=assetPath, format=asset.format.ToString(), width=asset.width, height=asset.height, mipCount=asset.mipmapCount, readable=asset.isReadable, runtimeBytes=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(asset) });
                }
                File.WriteAllText(Path.Combine(directory, "inventory.json"), JsonUtility.ToJson(inventory, true));
            }
            foreach (var id in Targets)
            {
                var index = Array.FindIndex(manifest.atlases, item => item.id == id);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), id);
                var source = originals[index];
                var copy = Readback(source); owned.Add(copy);
                copy.name = id + "-trial-" + mode;
                copy.filterMode = source.filterMode; copy.wrapMode = source.wrapMode;
                var originalPixels = copy.GetRawTextureData<byte>().ToArray();
                File.WriteAllBytes(Path.Combine(directory, id + "-source.png"), copy.EncodeToPNG());
                if (block != 0) EditorUtility.CompressTexture(copy, format, TextureCompressionQuality.Best);
                Assert.That(copy.format, Is.EqualTo(format), "Compressor may silently leave input unchanged: " + id);
                Assert.That(copy.mipmapCount, Is.EqualTo(1));
                var bytes = copy.GetRawTextureData<byte>();
                var expected = block == 0 ? (long)copy.width * copy.height * 4 : (long)((copy.width + block - 1) / block) * ((copy.height + block - 1) / block) * 16;
                Assert.That((long)bytes.Length, Is.EqualTo(expected), "Actual ASTC block payload: " + id);
                File.WriteAllBytes(Path.Combine(directory, id + ".blocks"), bytes.ToArray());
                var rawByteCount = bytes.Length;
                copy.Apply(false, false);
                var decoded = Readback(copy);
                try
                {
                    File.WriteAllBytes(Path.Combine(directory, id + "-decoded.png"), decoded.EncodeToPNG());
                    if (block == 0) CollectionAssert.AreEqual(originalPixels, decoded.GetRawTextureData<byte>().ToArray(), "RGBA GPU roundtrip must preserve every channel before ASTC comparisons: " + id);
                }
                finally { UnityEngine.Object.DestroyImmediate(decoded); }
                var row = new Row { atlas=id, mode=mode, format=copy.format.ToString(), width=copy.width, height=copy.height, block=block, mipCount=copy.mipmapCount, rawBytes=rawByteCount, expectedBytes=expected, gpuSupported=supported, graphicsDevice=SystemInfo.graphicsDeviceName, graphicsApi=SystemInfo.graphicsDeviceType.ToString(), evidence="Actual compressed texture GPU readback; decoded PNG is RGBA again, not memory saving evidence." };
                File.WriteAllText(Path.Combine(directory, id + ".json"), JsonUtility.ToJson(row, true));
                replacement[index] = copy;
            }
            registry.atlases = replacement;
            ArtCatalog.InvalidateCache();
        }
        static Texture2D Readback(Texture texture)
        {
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prior = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                image.Apply(false, false);
                return image;
            }
            catch { UnityEngine.Object.DestroyImmediate(image); throw; }
            finally { RenderTexture.active = prior; RenderTexture.ReleaseTemporary(target); }
        }
        public void Dispose()
        {
            if (registry != null && originals != null) registry.atlases = originals;
            ArtCatalog.InvalidateCache();
            foreach (var texture in owned) UnityEngine.Object.DestroyImmediate(texture);
            owned.Clear();
        }
    }
}
#endif
