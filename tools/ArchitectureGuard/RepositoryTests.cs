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

        void Expect(string name, string rule)
        {
            var pass = Guard.CheckRepository(root).Any(diagnostic => diagnostic.Contains(rule, StringComparison.Ordinal));
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")} repository {name}");
            if (!pass)
            {
                failures++;
            }
        }
    }
}
