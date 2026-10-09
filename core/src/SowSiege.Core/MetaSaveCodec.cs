using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SowSiege.Core
{
    public static class MetaSaveCodec
    {
        public const int CurrentVersion = 3;
        private const int ClockVersion = 2;
        private const int EnvelopeBytes = 44;
        private const int ChecksumBytes = 32;
        private const int MaximumCollectionEntries = 4096;
        private const int MaximumStringBytes = 1024;
        private const int Magic = 0x534D4554;
        private const int MaximumBytes = 1048576;
        public static byte[] Encode(MetaState state) => EncodeVersion(state, CurrentVersion);

        // Previous schemas remain writable for migration fixtures and interoperability tests.
        public static byte[] EncodeVersion(MetaState state, int version)
        {
            if (version < 1 || version > CurrentVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            Validate(state);
            using var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
            {
                writer.Write(state.HighestClearedChapter); WriteString(writer, state.ManorPriority);
                WriteInts(writer, state.Wallet); WriteInts(writer, state.ManorLevels);
                writer.Write(state.Vassals.Count);
                foreach (var entry in state.Vassals.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    WriteString(writer, entry.Key); writer.Write(entry.Value.Level); writer.Write(entry.Value.Rank); writer.Write(entry.Value.Fragments); writer.Write(entry.Value.Unlocked);
                }
                writer.Write(state.Metrics.Count);
                foreach (var entry in state.Metrics.OrderBy(x => x.Key, StringComparer.Ordinal)) { WriteString(writer, entry.Key); writer.Write(entry.Value); }
                WriteStrings(writer, state.CompletedChallenges); WriteStrings(writer, state.UnlockedContentIds); WriteStrings(writer, state.ActiveVassalIds);
                writer.Write(state.NextRunSequence); writer.Write(state.PendingRun != null);
                if (state.PendingRun != null) { writer.Write(state.PendingRun.Sequence); WriteString(writer, state.PendingRun.ChapterId); writer.Write(state.PendingRun.Seed); }
                if (version >= ClockVersion) { writer.Write(state.LastMonotonicSeconds); writer.Write(state.LastWallSeconds); writer.Write(state.IdleRemainderSeconds); }
                if (version >= CurrentVersion)
                {
                    writer.Write(state.CompletedRuns);
                }
            }
            byte[] bytes = payload.ToArray();
            if (bytes.Length > MaximumBytes - EnvelopeBytes)
            {
                throw new InvalidDataException("Save exceeds size limit.");
            }

            using var envelope = new MemoryStream();
            using (var writer = new BinaryWriter(envelope, Encoding.UTF8, true)) { writer.Write(Magic); writer.Write(version); writer.Write(bytes.Length); writer.Write(bytes); }
            using var sha = SHA256.Create(); byte[] hash = sha.ComputeHash(envelope.ToArray()); envelope.Write(hash, 0, hash.Length);
            return envelope.ToArray();
        }

        public static MetaDecodeResult Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < EnvelopeBytes || bytes.Length > MaximumBytes)
            {
                return new MetaDecodeResult(false, null, "corrupt");
            }

            try
            {
                using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                if (reader.ReadInt32() != Magic)
                {
                    throw new InvalidDataException();
                }

                int version = reader.ReadInt32();
                if (version < 1 || version > CurrentVersion)
                {
                    return new MetaDecodeResult(false, null, "unsupported-version");
                }

                int length = reader.ReadInt32(); if (length < 0 || length != bytes.Length - EnvelopeBytes)
                {
                    throw new InvalidDataException();
                }

                using var sha = SHA256.Create(); byte[] hash = sha.ComputeHash(bytes, 0, bytes.Length - ChecksumBytes);
                for (int i = 0; i < hash.Length; i++)
                {
                    if (hash[i] != bytes[bytes.Length - ChecksumBytes + i])
                    {
                        throw new InvalidDataException();
                    }
                }

                int highest = reader.ReadInt32(); string priority = ReadString(reader); var wallet = ReadInts(reader); var manor = ReadInts(reader);
                var vassals = new Dictionary<string, MetaVassalState>();
                int count = Count(reader);
                for (int i = 0; i < count; i++)
                {
                    vassals.Add(ReadString(reader), new MetaVassalState(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), ReadBoolean(reader)));
                }

                var metrics = new Dictionary<string, long>(); count = Count(reader);
                for (int i = 0; i < count; i++)
                {
                    metrics.Add(ReadString(reader), reader.ReadInt64());
                }

                string[] completed = ReadStrings(reader), unlocked = ReadStrings(reader), active = ReadStrings(reader);
                long sequence = reader.ReadInt64();
                MetaPendingRun? pending = ReadBoolean(reader) ? new MetaPendingRun(reader.ReadInt64(), ReadString(reader), reader.ReadInt32()) : null;
                var state = new MetaState(version, highest, priority, wallet, manor, vassals, metrics, completed, unlocked, active, sequence, pending,
                    version >= ClockVersion ? reader.ReadInt64() : -1, version >= ClockVersion ? reader.ReadInt64() : -1, version >= ClockVersion ? reader.ReadInt64() : 0, version >= CurrentVersion ? reader.ReadInt64() : 0);
                if (stream.Position != bytes.Length - ChecksumBytes)
                {
                    throw new InvalidDataException();
                }

                if (state.SchemaVersion == 1)
                {
                    state = MigrateOneToTwo(state);
                }

                if (state.SchemaVersion == ClockVersion)
                {
                    state = MigrateTwoToThree(state);
                }

                Validate(state); return new MetaDecodeResult(true, state, "");
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is ArgumentException || exception is OverflowException)
            { return new MetaDecodeResult(false, null, "corrupt"); }
        }
        private static MetaState MigrateOneToTwo(MetaState state) => state with { SchemaVersion = ClockVersion, LastMonotonicSeconds = -1, LastWallSeconds = -1, IdleRemainderSeconds = 0 };
        private static MetaState MigrateTwoToThree(MetaState state) => state with { SchemaVersion = CurrentVersion, CompletedRuns = state.NextRunSequence - 1 - (state.PendingRun == null ? 0 : 1) };
        private static void Validate(MetaState state)
        {
            if (state.SchemaVersion < 1 || state.SchemaVersion > CurrentVersion || state.HighestClearedChapter < 0 || state.NextRunSequence < 1 || state.CompletedRuns < 0 || state.LastMonotonicSeconds < -1 || state.LastWallSeconds < -1 || state.IdleRemainderSeconds < 0)
            {
                throw new InvalidDataException("Invalid meta state.");
            }

            if (state.Wallet.Values.Any(x => x < 0) || state.ManorLevels.Values.Any(x => x < 0) || state.Metrics.Values.Any(x => x < 0 || x > MetaValidation.MaximumMetricValue) || state.Vassals.Values.Any(x => x.Level < 1 || x.Rank < 0 || x.Fragments < 0))
            {
                throw new InvalidDataException("Invalid meta quantity.");
            }

            if (!state.ManorLevels.ContainsKey(state.ManorPriority) || state.ActiveVassalIds.Distinct().Count() != state.ActiveVassalIds.Length || state.CompletedChallenges.Distinct().Count() != state.CompletedChallenges.Length || state.UnlockedContentIds.Distinct().Count() != state.UnlockedContentIds.Length || state.ActiveVassalIds.Any(x => !state.Vassals.TryGetValue(x, out var v) || !v.Unlocked))
            {
                throw new InvalidDataException("Invalid meta references.");
            }

            if (state.PendingRun != null && (state.PendingRun.Sequence < 1 || state.PendingRun.Sequence != state.NextRunSequence - 1 || string.IsNullOrWhiteSpace(state.PendingRun.ChapterId)))
            {
                throw new InvalidDataException("Invalid pending run.");
            }
        }
        private static bool ReadBoolean(BinaryReader reader) { byte value = reader.ReadByte(); if (value > 1) { throw new InvalidDataException(); } return value == 1; }
        private static int Count(BinaryReader reader) { int count = reader.ReadInt32(); if (count < 0 || count > MaximumCollectionEntries) { throw new InvalidDataException(); } return count; }
        private static void WriteString(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); if (bytes.Length == 0 || bytes.Length > MaximumStringBytes) { throw new InvalidDataException(); } writer.Write(bytes.Length); writer.Write(bytes); }
        private static string ReadString(BinaryReader reader) { int count = reader.ReadInt32(); if (count < 1 || count > MaximumStringBytes) { throw new InvalidDataException(); } byte[] bytes = reader.ReadBytes(count); if (bytes.Length != count) { throw new EndOfStreamException(); } return new UTF8Encoding(false, true).GetString(bytes); }
        private static void WriteInts(BinaryWriter writer, Dictionary<string, int> values) { writer.Write(values.Count); foreach (var entry in values.OrderBy(x => x.Key, StringComparer.Ordinal)) { WriteString(writer, entry.Key); writer.Write(entry.Value); } }
        private static Dictionary<string, int> ReadInts(BinaryReader reader) { int count = Count(reader); var result = new Dictionary<string, int>(); for (int i = 0; i < count; i++) { result.Add(ReadString(reader), reader.ReadInt32()); } return result; }
        private static void WriteStrings(BinaryWriter writer, string[] values)
        {
            writer.Write(values.Length); foreach (string value in values.OrderBy(x => x, StringComparer.Ordinal))
            {
                WriteString(writer, value);
            }
        }
        private static string[] ReadStrings(BinaryReader reader) { int count = Count(reader); var result = new string[count]; for (int i = 0; i < count; i++) { result[i] = ReadString(reader); } return result; }
    }
}
