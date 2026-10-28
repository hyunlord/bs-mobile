using System;
using Game.Editor;
using Game.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class MobileTextureImportTests
    {
        [TestCase("Assets/Art/hero-knight.png")]
        [TestCase("Assets/Art/fallow-enemy-rigs.png")]
        [TestCase("Assets/Resources/EstateArt/estate-decor.png")]
        public void OnlyMobileOverridesUseApprovedAstc4(string path)
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.textureCompression,Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.GetPlatformTextureSettings("Standalone").overridden,Is.False);
            foreach(var platform in new[]{"Android","iPhone"})
            {
                var settings=importer.GetPlatformTextureSettings(platform);
                Assert.That(settings.overridden,Is.True,platform);
                Assert.That(settings.format,Is.EqualTo(TextureImporterFormat.ASTC_4x4),platform);
                Assert.That(settings.maxTextureSize,Is.EqualTo(4096));
                Assert.That(settings.compressionQuality,Is.EqualTo(100),"Match approved trial Best quality request");
            }
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(path).format,Is.EqualTo(TextureFormat.RGBA32),"Mac editor stays RGBA");
        }
        [Test] public void SourcePaddingStillRejectsOneAlphaAndAcceptsZero()
        {
            var image=new Texture2D(3,3,TextureFormat.RGBA32,false);
            try
            {
                var pixels=new Color32[9];pixels[4]=new Color32(255,255,255,255);image.SetPixels32(pixels);image.Apply();
                var manifest=JsonUtility.FromJson<ArtManifest>("{\"version\":\"test\",\"atlases\":[{\"id\":\"test\",\"width\":3,\"height\":3,\"padding\":1}],\"roles\":[{\"id\":\"test\",\"atlas\":\"test\",\"frames\":[{\"x\":1,\"y\":1,\"width\":1,\"height\":1}]}]}");
                Assert.DoesNotThrow(()=>ArtPreparation.ValidateSourceAlphaPadding(manifest,manifest.atlases[0],image.EncodeToPNG()));
                pixels[0]=new Color32(0,0,0,1);image.SetPixels32(pixels);image.Apply();
                Assert.Throws<InvalidOperationException>(()=>ArtPreparation.ValidateSourceAlphaPadding(manifest,manifest.atlases[0],image.EncodeToPNG()));
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
