namespace SowSiege.Sim;

public static class UnityExportCli
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("--export-unity" or "--verify-unity-export")) { return false; }
        if (args.Length != 3) { throw new ArgumentException("Usage: --export-unity|--verify-unity-export <data-directory> <bridge-file>"); }
        if (args[0] == "--export-unity") { UnityExport.Write(args[1], args[2]); }
        else { UnityExport.Verify(args[1], args[2]); }
        Console.WriteLine($"Unity canonical bridge {args[0]}: {Path.GetFullPath(args[2])}");
        return true;
    }
}
