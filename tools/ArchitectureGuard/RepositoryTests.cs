namespace ArchitectureGuard;

internal static class RepositoryTests
{
    internal static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "architecture-guard-" + Guid.NewGuid().ToString("N"));
        var core = Path.Combine(root, "core/src/SowSiege.Core");
        Directory.CreateDirectory(core);
        Directory.CreateDirectory(Path.Combine(root, "tools/ArchitectureGuard"));
        Directory.CreateDirectory(Path.Combine(root, "data/heroes"));
        File.WriteAllText(Path.Combine(root, "tools/ArchitectureGuard/algorithm-constants.json"), "[]");
        File.WriteAllText(Path.Combine(root, "data/heroes/alternate.json"), "{\"id\":\"arbitrary_knight\"}");
        var project = Path.Combine(core, "SowSiege.Core.csproj");
        var source = Path.Combine(core, "Fixture.cs");
        var failures = 0;
        try
        {
            foreach (var kind in new[] { "PackageReference", "ProjectReference", "Reference", "FrameworkReference" })
            {
                File.WriteAllText(project, $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><{kind} Include=\"External\" /></ItemGroup></Project>");
                File.WriteAllText(source, "class C {} ");
                Expect(kind, "AG001");
            }
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(source, "class C { string Id => \"arbitrary_knight\"; }");
            Expect("JSON-discovered content ID", "AG002");
            Directory.CreateDirectory(Path.Combine(root, "data/estates"));
            File.WriteAllText(Path.Combine(root, "data/estates/alternate.json"),
                "{\"id\":\"arbitrary_meadow\",\"uniqueLoop\":{\"stages\":[{\"id\":\"people\"}]}}");
            File.WriteAllText(source, "class C { string Stage => \"people\"; }");
            Expect("nested loop stage is not a content identity", "AG002", false);
            File.WriteAllText(source, "class C { string Id => \"arbitrary_meadow\"; }");
            Expect("top-level estate identity remains protected", "AG002");
            File.WriteAllText(Path.Combine(root, "data/references.json"),
                "{\"settings\":{\"heroId\":\"referenced_knight\"},\"hero\":{\"id\":\"nested_knight\"}}");
            File.WriteAllText(source, "class C { string Id => \"referenced_knight\"; }");
            Expect("explicit nested hero reference remains protected", "AG002");
            File.WriteAllText(source, "class C { string Id => \"nested_knight\"; }");
            Expect("explicit nested hero object remains protected", "AG002");
            File.WriteAllText(Path.Combine(root, "data/heroes/generic.json"), "{\"id\":\"people\"}");
            File.WriteAllText(source, "class C { string Id => \"people\"; }");
            Expect("generic word used as actual hero identity remains protected", "AG002");
            File.WriteAllText(Path.Combine(root, "data/system-design-v1.json"), "{\"primitiveContract\":{\"units\":[{\"id\":\"unit:attack-shape\",\"paramSchema\":{\"type\":\"object\"}}]}}");
            File.WriteAllText(source, "class C { string Unit => \"unit:attack-shape\"; }");
            Expect("declared mechanic grammar unit", "AG002", false);
            File.WriteAllText(source, "class C { string Unit => \"unit\\u003aattack-shape\"; }");
            Expect("escaped declared mechanic grammar unit", "AG002", false);
            File.WriteAllText(source, "class C { const string Unit = \"unit\" + \":attack-shape\"; }");
            Expect("constant folded declared mechanic grammar unit", "AG002", false);
            File.WriteAllText(source, "class C { string Unit => \"unit:unknown\"; }");
            Expect("unknown unit namespace is not blanket exempt", "AG002");
            File.WriteAllText(source, "class C { string Unit => \"unit\\u003aunknown\"; }");
            Expect("escaped unknown unit", "AG002");
            File.WriteAllText(source, "class C { const string Unit = \"unit\" + \":unknown\"; }");
            Expect("concatenated unknown unit", "AG002");
            File.WriteAllText(source, "class C { string Id => \"unit:attack-shape core:seed_bag\"; }");
            Expect("known grammar cannot hide concrete content", "AG002");
            File.WriteAllText(Path.Combine(root, "data/items.json"), "{\"items\":[{\"id\":\"unit:attack-shape\"}]}");
            File.WriteAllText(source, "class C { string Id => \"unit:attack-shape\"; }");
            Expect("actual item identity overrides grammar exemption", "AG002");
            File.Delete(Path.Combine(root, "data/items.json"));
            File.WriteAllText(Path.Combine(root, "data/heroes/collision.json"), "{\"id\":\"unit:attack-shape\"}");
            File.WriteAllText(source, "class C { string Id => \"unit:attack-shape\"; }");
            Expect("actual hero identity overrides grammar exemption", "AG002");
            var registry = Path.Combine(root, "data/system-design-v1.json");
            var validRegistry = File.ReadAllText(registry);
            File.WriteAllText(registry, validRegistry.Replace("unit:attack-shape", "core:seed_bag", StringComparison.Ordinal));
            var rejectedRegistry = false;
            try { Guard.CheckRepository(root); }
            catch (IOException) { rejectedRegistry = true; }
            Console.WriteLine($"{(rejectedRegistry ? "PASS" : "FAIL")} repository concrete content cannot register as grammar");
            if (!rejectedRegistry) { failures++; }
            File.WriteAllText(registry, validRegistry);
            File.WriteAllText(source, "class C {} ");
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"),
                "<Project><ItemGroup><PackageReference Include=\"Engine\" /></ItemGroup></Project>");
            Expect("inherited package reference", "AG001");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return failures;

        void Expect(string name, string rule, bool expected = true)
        {
            var pass = Guard.CheckRepository(root).Any(diagnostic => diagnostic.Contains(rule, StringComparison.Ordinal)) == expected;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")} repository {name}");
            if (!pass)
            {
                failures++;
            }
        }
    }
}
