namespace SowSiege.Sim;

public static class UnityExportCli
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 3 && args[0] == "--export-wave-benchmark")
        {
            var snapshot = UnityExportSnapshot.Capture(args[1]);
            var catalog = ContentLoader.Load(args[1], false, "wave-1a");
            var source = "// Generated frozen benchmark fixture; do not edit.\nnamespace Game.App { public sealed partial class RunCoordinator {\n" +
                "static partial void LoadFrozenWaveBenchmark(ref global::SowSiege.Core.ContentCatalog catalog, ref string dataHash) { catalog = " +
                UnityExportExpression.Write(catalog) + "; dataHash = \"" + snapshot.DataHash + "\"; }\n} }\n";
            snapshot.AssertUnchanged(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
            File.WriteAllText(args[2], source);
            return true;
        }
        if (args.Length == 0 || args[0] is not ("--export-unity" or "--verify-unity-export")) { return false; }
        if (args.Length is not (3 or 4)) { throw new ArgumentException("Usage: --export-unity|--verify-unity-export <data-directory> <bridge-file> [profile]"); }
        if (args[0] == "--export-unity") { UnityExport.Write(args[1], args[2], args.Length == 4 ? args[3] : UnityExport.ProfileName); }
        else { UnityExport.Verify(args[1], args[2], args.Length == 4 ? args[3] : UnityExport.ProfileName); }
        Console.WriteLine($"Unity canonical bridge {args[0]}: {Path.GetFullPath(args[2])}");
        return true;
    }
}
