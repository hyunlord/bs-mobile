using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Game.App.Generated;

namespace Game.App
{
    public static class BundleVerifier
    {
        public static void VerifyFile(BundleFile file, byte[] bytes)
        {
            if (bytes.LongLength != file.ByteLength || !string.Equals(Hash(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Canonical data mismatch: " + file.RelativePath);
        }

        public static string Verify(IReadOnlyDictionary<string, byte[]> files)
        {
            using (var stream = new MemoryStream())
            {
                foreach (var file in CanonicalContent.Files)
                {
                    if (!files.TryGetValue(file.RelativePath, out var bytes))
                        throw new InvalidDataException("Missing canonical data: " + file.RelativePath);
                    VerifyFile(file, bytes);
                    var path = Encoding.UTF8.GetBytes(file.RelativePath + "\0");
                    stream.Write(path, 0, path.Length);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.WriteByte(0);
                }
                var hash = Hash(stream.ToArray());
                if (!string.Equals(hash, CanonicalContent.DataHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Canonical bundle identity mismatch.");
                return hash;
            }
        }

        public static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "");
        }
    }
}
