namespace ArchitectureGuard;

internal static class SelfTests
{
    internal static int Run()
    {
        (string Name, string Source, string? Rule)[] cases =
        [
            ("BCL algorithm", "class C { int Step(int n) => n + 1; }", null),
            ("comments are not code", "// UnityEngine hero:default 500\nclass C {}", null),
            ("engine unused import", "using UnityEngine; class C {}", "AG001"),
            ("inactive branch engine", "#if ENGINE\nusing UnityEngine;\n#endif\nclass C {}", "AG001"),
            ("inactive branch numeric", "class C {\n#if GAME\nint Attack => 42;\n#endif\n}", "AG003"),
            ("engine alias", "using E = Godot.Node; class C {}", "AG001"),
            ("content id literal", "class C { string Id = \"hero:default\"; }", "AG002"),
            ("content id escaped", "class C { string Id = \"hero\\u003adefault\"; }", "AG002"),
            ("content id concatenated", "class C { const string Id = \"hero\" + \":default\"; }", "AG002"),
            ("content id interpolated", "class C { const string Prefix = \"hero\"; const string Id = $\"{Prefix}:default\"; }", "AG002"),
            ("data id without namespace", "class C { string Id = \"alternate_knight\"; }", "AG002"),
            ("gameplay local", "class C { int Attack => 500; }", "AG003"),
            ("gameplay const", "class C { const int Attack = 500; }", "AG003"),
            ("gameplay hexadecimal", "class C { int Attack => 0xFF; }", "AG003"),
            ("gameplay negative", "class C { int Attack => -2; }", "AG003"),
            ("seeded random", "class C { System.Random R = new System.Random(1); }", null),
            ("unseeded random", "class C { System.Random R = new System.Random(); }", "AG004"),
            ("implicit new random", "class C { System.Random R = new(); }", "AG004"),
            ("shared random", "class C { object R => System.Random.Shared; }", "AG004"),
            ("clock alias", "using Clock = System.DateTime; class C { object Now => Clock.UtcNow; }", "AG004"),
            ("clock static import", "using static System.DateTime; class C { object N => Now; }", "AG004"),
            ("random guid", "class C { object G => System.Guid.NewGuid(); }", "AG004"),
            ("stopwatch", "class C { object S => System.Diagnostics.Stopwatch.StartNew(); }", "AG004"),
        ];
        var failures = 0;
        foreach (var item in cases)
        {
            var diagnostics = Guard.CheckSources(new Dictionary<string, string> { ["Fixture.cs"] = item.Source },
                new HashSet<string>(StringComparer.Ordinal) { "alternate_knight" }, []);
            var pass = item.Rule == null ? diagnostics.Count == 0 : diagnostics.Any(line => line.Contains(item.Rule, StringComparison.Ordinal));
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {item.Name}");
            if (!pass)
            {
                failures++;
            }
        }
        var algorithm = new[] { new AlgorithmConstant("C.Multiplier", "42", "Test fixture algorithm constant") };
        var allowed = Guard.CheckSources(new Dictionary<string, string> { ["Fixture.cs"] = "class C { const int Multiplier = 42; }" }, [], algorithm);
        var changed = Guard.CheckSources(new Dictionary<string, string> { ["Fixture.cs"] = "class C { const int Multiplier = 43; }" }, [], algorithm);
        var gameplay = Guard.CheckSources(new Dictionary<string, string> { ["Fixture.cs"] = "class C { const int Damage = 42; }" }, [], algorithm);
        var inline = Guard.CheckSources(new Dictionary<string, string> { ["Fixture.cs"] = "class C { const int Multiplier = 42; int Reward => 42; }" }, [], algorithm);
        var algorithmPass = allowed.Count == 0 && changed.Any(line => line.Contains("AG003", StringComparison.Ordinal))
            && gameplay.Any(line => line.Contains("AG003", StringComparison.Ordinal)) && inline.Any(line => line.Contains("AG003", StringComparison.Ordinal));
        Console.WriteLine($"{(algorithmPass ? "PASS" : "FAIL")} exact algorithm constant exemption");
        if (!algorithmPass)
        {
            failures++;
        }

        Console.WriteLine($"ArchitectureGuard self-tests: {cases.Length + 1 - failures}/{cases.Length + 1} passed.");
        failures += RepositoryTests.Run();
        return failures == 0 ? 0 : 1;
    }
}
