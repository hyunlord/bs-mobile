namespace SowSiege.Sim;

public static class UnityExportCli
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("--export-unity" or "--verify-unity-export")) { return false; }
        if (args.Length is not (3 or 4)) { throw new ArgumentException("Usage: --export-unity|--verify-unity-export <data-directory> <bridge-file> [profile]"); }
        if (args[0] == "--export-unity") { UnityExport.Write(args[1], args[2], args.Length == 4 ? args[3] : UnityExport.ProfileName); }
        else { UnityExport.Verify(args[1], args[2], args.Length == 4 ? args[3] : UnityExport.ProfileName); }
        Console.WriteLine($"Unity canonical bridge {args[0]}: {Path.GetFullPath(args[2])}");
        return true;
    }
}
