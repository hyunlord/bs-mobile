using System;
using System.Collections.Generic;
using System.IO;
using Game.App;
using Game.App.Generated;
using Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Tests.EditMode
{
    public sealed class FoundationTests
    {
        [Test]
        public void ProductionBridgeConstructsCanonicalYear()
        {
            var catalog = CanonicalContent.CreateCatalog();
            Assert.That(CanonicalContent.ProfileName, Is.EqualTo("production"));
            Assert.That(catalog.Tuning.DurationTicks, Is.EqualTo(21600));
            Assert.That(catalog.Tuning.World.Seasons.Length, Is.EqualTo(4));
            Assert.That(catalog.WeaponCombat, Is.Not.Null);
        }

        [Test]
        public void CanonicalBytesVerifyAndTamperingFailsClosed()
        {
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in CanonicalContent.Files)
                bytes.Add(file.RelativePath, File.ReadAllBytes(Path.Combine(FoundationBuild.RepoRoot, "data", file.RelativePath)));
            Assert.That(BundleVerifier.Verify(bytes), Is.EqualTo(CanonicalContent.DataHash).IgnoreCase);
            var first = CanonicalContent.Files[0];
            bytes[first.RelativePath][0] ^= 1;
            Assert.Throws<InvalidDataException>(() => BundleVerifier.Verify(bytes));
            bytes.Remove(first.RelativePath);
            Assert.Throws<InvalidDataException>(() => BundleVerifier.Verify(bytes));
        }

        [Test]
        public void TemplateUsesActualUniversal2DRendererAndTextSerialization()
        {
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.TypeOf<UniversalRenderPipelineAsset>());
            Assert.That(AssetDatabase.LoadAssetAtPath<Renderer2DData>("Assets/Settings/Renderer2D.asset"), Is.Not.Null);
            var serialized = new SerializedObject(GraphicsSettings.defaultRenderPipeline);
            Assert.That(serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue, Is.TypeOf<Renderer2DData>());
            Assert.That(EditorSettings.serializationMode, Is.EqualTo(SerializationMode.ForceText));
            Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(3));
        }
    }
}
