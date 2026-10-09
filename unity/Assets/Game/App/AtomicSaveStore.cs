#nullable enable
#pragma warning disable IDE0161 // Unity C# 9 requires a block-scoped namespace.
using System;
using System.IO;

namespace Game.App
{
    public enum AtomicSaveValidation { Valid, Corrupt, UnsupportedVersion }

    public enum AtomicSaveLoadStatus { Missing, Primary, RecoveredBackup }

    public sealed class AtomicSaveLoadResult
    {
        public byte[]? Payload { get; }
        public AtomicSaveLoadStatus Status { get; }

        internal AtomicSaveLoadResult(byte[]? payload, AtomicSaveLoadStatus status)
        {
            Payload = payload;
            Status = status;
        }
    }

    /// <summary>Single-writer host storage; the caller owns payload schema and version validation.</summary>
    public sealed class AtomicSaveStore
    {
        private readonly string primary;
        private readonly string backup;
        private readonly string temporary;
        private readonly int maxPayloadBytes;
        private readonly object sync = new object();

        public AtomicSaveStore(string directory, string fileName, int maxPayloadBytes = 1048576)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A save directory is required.", nameof(directory));
            }

            if (string.IsNullOrWhiteSpace(fileName) || fileName == "." || fileName == "..")
            {
                throw new ArgumentException("A safe file name is required.", nameof(fileName));
            }

            foreach (var character in fileName)
            {
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') && character != '.' && character != '_' && character != '-')
                {
                    throw new ArgumentException("Save names permit only ASCII letters, digits, dots, underscores and hyphens.", nameof(fileName));
                }
            }

            if (maxPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
            }

            primary = Path.Combine(Path.GetFullPath(directory), fileName);
            backup = primary + ".bak";
            temporary = primary + ".tmp";
            this.maxPayloadBytes = maxPayloadBytes;
        }

        public AtomicSaveLoadResult Load(Func<byte[], AtomicSaveValidation> validate)
        {
            if (validate == null)
            {
                throw new ArgumentNullException(nameof(validate));
            }

            lock (sync)
            {
                var primaryBytes = ReadValidated(primary, validate);
                if (primaryBytes != null)
                {
                    return new AtomicSaveLoadResult(primaryBytes, AtomicSaveLoadStatus.Primary);
                }

                var backupBytes = ReadValidated(backup, validate);
                if (backupBytes != null)
                {
                    return new AtomicSaveLoadResult(backupBytes, AtomicSaveLoadStatus.RecoveredBackup);
                }

                if (File.Exists(primary) || File.Exists(backup))
                {
                    throw new InvalidDataException("Existing save files were rejected; automatic reset is forbidden.");
                }

                return new AtomicSaveLoadResult(null, AtomicSaveLoadStatus.Missing);
            }
        }

        public void Save(byte[] payload, Func<byte[], AtomicSaveValidation> validate)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (validate == null)
            {
                throw new ArgumentNullException(nameof(validate));
            }

            lock (sync)
            {
                var snapshot = (byte[])payload.Clone();
                if (snapshot.Length == 0 || snapshot.Length > maxPayloadBytes || validate(snapshot) != AtomicSaveValidation.Valid)
                {
                    throw new InvalidDataException("Save payload was rejected.");
                }

                var hasPrimary = File.Exists(primary);
                var primaryBytes = ReadValidated(primary, validate);
                var backupBytes = ReadValidated(backup, validate);
                if (hasPrimary && primaryBytes == null && backupBytes == null)
                {
                    throw new InvalidDataException("No valid save generation remains; automatic reset is forbidden.");
                }

                if (!hasPrimary && File.Exists(backup) && backupBytes == null)
                {
                    throw new InvalidDataException("Rejected backup is preserved; automatic reset is forbidden.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(primary)!);
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(snapshot, 0, snapshot.Length);
                    stream.Flush(true);
                }
                if (hasPrimary)
                {
                    File.Replace(temporary, primary, primaryBytes != null ? backup : null);
                }
                else
                {
                    File.Move(temporary, primary);
                }
            }
        }

        private byte[]? ReadValidated(string path, Func<byte[], AtomicSaveValidation> validate)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length == 0 || stream.Length > maxPayloadBytes)
                {
                    return null;
                }

                var bytes = new byte[(int)stream.Length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var count = stream.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0)
                    {
                        throw new EndOfStreamException("Save changed while reading.");
                    }

                    offset += count;
                }
                var validation = validate(bytes);
                if (validation == AtomicSaveValidation.UnsupportedVersion)
                {
                    throw new InvalidDataException("Save uses an unsupported version; loading and overwriting are forbidden.");
                }

                return validation == AtomicSaveValidation.Valid ? bytes : null;
            }
        }
    }
}
