using ArchitectureGuard;

if (args.Length == 1 && args[0] == "--self-test")
{
    return SelfTests.Run();
}

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: ArchitectureGuard <repository-root> | --self-test");
    return 2;
}
try
{
    var diagnostics = Guard.CheckRepository(Path.GetFullPath(args[0]));
    foreach (var diagnostic in diagnostics)
    {
        Console.Error.WriteLine(diagnostic);
    }

    Console.WriteLine($"ArchitectureGuard: {diagnostics.Count} violation(s).");
    return diagnostics.Count == 0 ? 0 : 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Xml.XmlException)
{
    Console.Error.WriteLine($"AG000: Guard could not complete: {exception.Message}");
    return 2;
}
