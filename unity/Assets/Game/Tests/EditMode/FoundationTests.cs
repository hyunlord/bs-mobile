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
        public void FirstPlayableBridgeConstructsCanonicalFifteenMinutes()
        {
            var catalog = CanonicalContent.CreateCatalog();
            Assert.That(CanonicalContent.ProfileName, Is.EqualTo("first-playable"));
            Assert.That(catalog.Tuning.DurationTicks, Is.EqualTo(27000));
            Assert.That(catalog.Tuning.World.Seasons.Length, Is.EqualTo(4));
            Assert.That(catalog.WeaponCombat, Is.Not.Null);
            Assert.That(catalog.Tuning.TickRate, Is.EqualTo(30));
            Assert.That(catalog.FirstPlayable, Is.Not.Null);
            Assert.That(catalog.Weapons.Count, Is.EqualTo(10));
            Assert.That(catalog.Tools.Count, Is.EqualTo(8));
            Assert.That(catalog.Enemies.Count, Is.EqualTo(13));
            Assert.That(catalog.Runtime.Charters.Count, Is.EqualTo(8));
            Assert.That(catalog.Runtime.Items.Count, Is.EqualTo(30));
            Assert.That(catalog.Runtime.Evolutions.Count, Is.EqualTo(8));
            FoundationBuild.VerifyProfile();
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
