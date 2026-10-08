using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SowSiege.Sim;

public sealed record UnityExportFile(string RelativePath, long ByteLength, string Sha256);
public sealed record UnityExportSnapshot(string DataHash, UnityExportFile[] Files)
{
    public static UnityExportSnapshot Capture(string directory)
    {
        var root = Path.GetFullPath(directory);
        var files = new List<UnityExportFile>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Enumerate(root).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var bytes = File.ReadAllBytes(file);
            hash.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
            hash.AppendData(bytes); hash.AppendData(new byte[] { 0 });
            files.Add(new(relative, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        return new(Convert.ToHexString(hash.GetHashAndReset()), files.ToArray());

        IEnumerable<string> Enumerate(string current)
        {
            RequireSafeEntry(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (current == root && Path.GetFileName(entry) == "test") { continue; }
                RequireSafeEntry(entry);
                if (Directory.Exists(entry))
                {
                    foreach (var nested in Enumerate(entry)) { yield return nested; }
                }
                else if (Path.GetExtension(entry) == ".json") { yield return entry; }
            }
        }
    }

    public void AssertUnchanged(string directory)
    {
        var current = Capture(directory);
        if (current.DataHash != DataHash || !current.Files.SequenceEqual(Files))
        {
            throw new InvalidDataException("Canonical source changed during Unity export.");
        }
    }

    public static void RequireSafeEntry(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Symlink is forbidden in Unity export: {path}");
        }
        if (!Regex.IsMatch(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), @"\A[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException($"Unsafe Unity bundle path: {path}");
        }
    }
}
