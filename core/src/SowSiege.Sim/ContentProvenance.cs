using System.Security.Cryptography;
using System.Text;

namespace SowSiege.Sim;

public static class ContentProvenance
{
    public static string? SourceHash(string? root)
    {
        if (root is null || !Directory.Exists(Path.Combine(root, "core"))) { return null; }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var paths = Directory.EnumerateFiles(Path.Combine(root, "core"), "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(file => Path.GetExtension(file) is ".cs" or ".csproj" or ".props" or ".targets")
            .Concat(new[] { "global.json", "Directory.Build.props", "Directory.Build.targets" }.Select(file => Path.Combine(root, file)).Where(File.Exists))
            .Order(StringComparer.Ordinal);
        foreach (var file in paths)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/') + "\0"));
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

}
