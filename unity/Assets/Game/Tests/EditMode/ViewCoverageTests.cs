using System;
using System.Collections.Generic;
using System.IO;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // Resolves the Core-ID ↔ View requirements computed by tools/view-coverage.mjs through the real Resources registries.
    // CI checks the manifests and keeps the fixture current; this proves the same roles load through ArtCatalog and EnemyRigAtlas.
    public sealed class ViewCoverageTests
    {
        [Serializable] sealed class Fixture { public int schemaVersion; public Profile[] profiles; }
        [Serializable] sealed class Profile { public string name; public string[] roles; public string[] bindings; public string[] knownGapRoles; public string[] knownGapBindings; }

        static Fixture Load()
        {
            var file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "tools", "fixtures", "view-coverage.expected.json"));
            Assert.That(File.Exists(file), Is.True, "Missing " + file + "; run node tools/view-coverage.mjs --write.");
            var fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(file));
            Assert.That(fixture.schemaVersion, Is.EqualTo(1));
            return fixture;
        }

        static void Resolve(ArtCatalog catalog, string type, string key)
        {
            if (type == "role") { catalog.ResolveRole(key); return; }
            var parts = key.Split('|');
            if (parts.Length != 3) throw new InvalidOperationException("Malformed binding key: " + key);
            catalog.Resolve(parts[0], parts[1], parts[2]);
        }

        [Test] public void EveryProfileResolvesCoreContentAndViewRolesThroughItsRegistry()
        {
            var fixture = Load();
            var previous = ArtCatalog.ProfileName;
            var names = new List<string>();
            try
            {
                foreach (var profile in fixture.profiles)
                {
                    Assert.That(profile.name, Is.EqualTo("first-playable").Or.EqualTo("wave-1a"), "ArtCatalog.Load only knows these registries.");
                    names.Add(profile.name);
                    ArtCatalog.ProfileName = profile.name;
                    ArtCatalog.InvalidateCache();
                    var catalog = ArtCatalog.Load();
                    var missing = new List<string>();
                    foreach (var role in profile.roles) try { Resolve(catalog, "role", role); } catch (InvalidOperationException e) { missing.Add(e.Message); }
                    foreach (var binding in profile.bindings) try { Resolve(catalog, "binding", binding); } catch (InvalidOperationException e) { missing.Add(e.Message); }
                    Assert.That(missing, Is.Empty, profile.name + " registry lacks required art.");
                    foreach (var role in profile.knownGapRoles) Assert.Throws<InvalidOperationException>(() => Resolve(catalog, "role", role), profile.name + " known gap now resolves: " + role);
                    foreach (var binding in profile.knownGapBindings) Assert.Throws<InvalidOperationException>(() => Resolve(catalog, "binding", binding), profile.name + " known gap now resolves: " + binding);
                    if (profile.name == "wave-1a") Assert.DoesNotThrow(() => new EnemyRigAtlas(catalog));
                }
                Assert.That(names, Is.EquivalentTo(new[] { "first-playable", "wave-1a" }));
            }
            finally
            {
                ArtCatalog.ProfileName = previous;
                ArtCatalog.InvalidateCache();
            }
        }
    }
}
