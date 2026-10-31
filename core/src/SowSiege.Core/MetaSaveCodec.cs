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
        public const int CurrentVersion = RewardVersion;
        // Version 6 adds the M3a account estate (#361). It is written only for an account that has an estate, so every save
        // without one keeps its version 5 bytes; a version 6 file is never downgraded.
        public const int EstateVersion = 6;
        public const int LatestVersion = EstateVersion;
        private const int ClockVersion = 2;
        private const int CompletedRunsVersion = 3;
        private const int ClaimsVersion = 4;
        private const int RewardVersion = 5;
        private const int EnvelopeBytes = 44;
        private const int ChecksumBytes = 32;
        internal const int MaximumCollectionEntries = 4096;
        internal const int MaximumStringBytes = 1024;
        private const int Magic = 0x534D4554;
        private const int MaximumBytes = 1048576;
        public static byte[] Encode(MetaState state) => EncodeVersion(state, state.Estate != null ? EstateVersion : CurrentVersion);

        // Previous schemas remain writable for migration fixtures and interoperability tests.
        public static byte[] EncodeVersion(MetaState state, int version)
        {
            if (version < 1 || version > LatestVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            Validate(state);
            if ((state.Estate != null) != (version >= EstateVersion)) { throw new InvalidDataException("An account estate is written in version 6 only, and version 6 needs one."); }
            if (version < ClaimsVersion && state.PendingClaims.Length != 0)
            {
                throw new InvalidDataException("Pending claims cannot be written to a historical schema.");
            }
            if (version < RewardVersion && state.RewardReceipts.Length != 0) { throw new InvalidDataException("Reward receipts cannot be downgraded."); }
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
                if (version >= CompletedRunsVersion)
                {
                    writer.Write(state.CompletedRuns);
                }
                if (version >= ClaimsVersion)
                {
                    writer.Write(state.NextClaimSequence);
                    writer.Write(state.PendingClaims.Length);
                    foreach (var claim in state.PendingClaims.OrderBy(x => x.Sequence))
                    {
                        writer.Write(claim.Sequence); writer.Write((int)claim.Kind); WriteString(writer, claim.SourceId);
                        WriteInts(writer, claim.Rewards); writer.Write(claim.IdleSeconds);
                    }
                }
                if (version >= RewardVersion)
                {
                    writer.Write(state.RewardReceipts.Length);
                    foreach (var receipt in state.RewardReceipts.OrderBy(x => x.ReceiptId, StringComparer.Ordinal))
                    { WriteString(writer, receipt.ReceiptId); writer.Write((int)receipt.Kind); writer.Write(receipt.SourceSequence); }
                    writer.Write(state.ResultRewardBasis != null);
                    if (state.ResultRewardBasis != null) { writer.Write(state.ResultRewardBasis.RunSequence); WriteInts(writer, state.ResultRewardBasis.Awarded); }
                }
                if (version >= EstateVersion) { WriteEstate(writer, state.Estate!); }
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
                if (version < 1 || version > LatestVersion)
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
                    version >= ClockVersion ? reader.ReadInt64() : -1, version >= ClockVersion ? reader.ReadInt64() : -1, version >= ClockVersion ? reader.ReadInt64() : 0, version >= CompletedRunsVersion ? reader.ReadInt64() : 0);
                if (version >= ClaimsVersion)
                {
                    long nextClaim = reader.ReadInt64();
                    var claims = new MetaPendingClaim[Count(reader)];
                    for (int i = 0; i < claims.Length; i++)
                    {
                        claims[i] = new MetaPendingClaim(reader.ReadInt64(), (MetaClaimKind)reader.ReadInt32(), ReadString(reader), ReadInts(reader), reader.ReadInt32());
                    }
                    state = state with { NextClaimSequence = nextClaim, PendingClaims = claims };
                }
                if (version >= RewardVersion)
                {
                    var receipts = new MetaRewardReceipt[Count(reader)];
                    for (int i = 0; i < receipts.Length; i++) { receipts[i] = new MetaRewardReceipt(ReadString(reader), (MetaBonusKind)reader.ReadInt32(), reader.ReadInt64()); }
                    MetaResultRewardBasis? basis = ReadBoolean(reader) ? new MetaResultRewardBasis(reader.ReadInt64(), ReadInts(reader)) : null;
                    state = state with { RewardReceipts = receipts, ResultRewardBasis = basis };
                }
                if (version >= EstateVersion) { state = state with { Estate = ReadEstate(reader) }; }
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

                if (state.SchemaVersion == CompletedRunsVersion || state.SchemaVersion == ClaimsVersion)
                {
                    state = state with { SchemaVersion = CurrentVersion };
                }
                Validate(state); return new MetaDecodeResult(true, state, "");
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is ArgumentException || exception is OverflowException)
            { return new MetaDecodeResult(false, null, "corrupt"); }
        }
        private static MetaState MigrateOneToTwo(MetaState state) => state with { SchemaVersion = ClockVersion, LastMonotonicSeconds = -1, LastWallSeconds = -1, IdleRemainderSeconds = 0 };
        private static MetaState MigrateTwoToThree(MetaState state) => state with { SchemaVersion = CompletedRunsVersion, CompletedRuns = state.NextRunSequence - 1 - (state.PendingRun == null ? 0 : 1) };
        private static void Validate(MetaState state)
        {
            if (state.RewardReceipts.Length > MaximumCollectionEntries || state.RewardReceipts.Select(x => x.ReceiptId).Distinct().Count() != state.RewardReceipts.Length ||
                state.RewardReceipts.Select(x => (x.Kind, x.SourceSequence)).Distinct().Count() != state.RewardReceipts.Length ||
                state.RewardReceipts.Any(x => string.IsNullOrWhiteSpace(x.ReceiptId) || !Enum.IsDefined(typeof(MetaBonusKind), x.Kind) || x.SourceSequence < 1 ||
                    (x.Kind == MetaBonusKind.Result ? x.SourceSequence >= state.NextRunSequence : x.SourceSequence >= state.NextClaimSequence)) ||
                (state.ResultRewardBasis != null && (state.ResultRewardBasis.RunSequence < 1 || state.ResultRewardBasis.RunSequence >= state.NextRunSequence || state.ResultRewardBasis.Awarded.Values.Any(x => x < 0))))
            { throw new InvalidDataException("Invalid reward receipt ledger."); }
            if (state.NextClaimSequence < 1 || state.PendingClaims.Length > MaximumCollectionEntries ||
                state.PendingClaims.Select(x => x.Sequence).Distinct().Count() != state.PendingClaims.Length ||
                state.PendingClaims.Count(x => x.Kind == MetaClaimKind.Idle) > 1 ||
                state.PendingClaims.Where(x => x.Kind == MetaClaimKind.Challenge).Select(x => x.SourceId).Distinct().Count() != state.PendingClaims.Count(x => x.Kind == MetaClaimKind.Challenge) ||
                state.PendingClaims.Any(x => x.Sequence < 1 || x.Sequence >= state.NextClaimSequence ||
                    !Enum.IsDefined(typeof(MetaClaimKind), x.Kind) || string.IsNullOrWhiteSpace(x.SourceId) ||
                    x.Rewards.Values.Any(value => value < 0) || x.IdleSeconds < 0 ||
                    (x.Kind == MetaClaimKind.Idle && (x.IdleSeconds == 0 || x.SourceId != "idle")) ||
                    (x.Kind == MetaClaimKind.Challenge && (x.IdleSeconds != 0 || state.CompletedChallenges.Contains(x.SourceId)))))
            {
                throw new InvalidDataException("Invalid claim ledger.");
            }
            if (state.Estate != null) { ValidateEstate(state.Estate); }
            if (state.SchemaVersion < 1 || state.SchemaVersion > LatestVersion || (state.SchemaVersion == EstateVersion) != (state.Estate != null) || state.HighestClearedChapter < 0 || state.NextRunSequence < 1 || state.CompletedRuns < 0 || state.LastMonotonicSeconds < -1 || state.LastWallSeconds < -1 || state.IdleRemainderSeconds < 0)
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
        private static void WriteEstate(BinaryWriter writer, MetaEstateState e)
        {
            WriteString(writer, e.ConfigVersion); WriteString(writer, e.Origin);
            writer.Write(e.LastMonotonicSeconds); writer.Write(e.LastWallSeconds); writer.Write(e.ElapsedSeconds);
            WriteStrings(writer, e.OpenZones);
            writer.Write(e.Buildings.Length);
            foreach (var b in e.Buildings.OrderBy(x => x.Id))
            {
                writer.Write(b.Id); WriteString(writer, b.Kind); writer.Write(b.X); writer.Write(b.Y); writer.Write(b.Level);
                writer.Write(b.Branch != null); if (b.Branch != null) { WriteString(writer, b.Branch); }
                writer.Write(b.Stock); writer.Write(b.Remainder); writer.Write(b.StoredSeconds);
            }
            writer.Write(e.Active != null);
            if (e.Active != null) { writer.Write(e.Active.BuildingId); writer.Write(e.Active.TargetLevel); writer.Write(e.Active.TotalSeconds); writer.Write(e.Active.RemainingSeconds); }
            writer.Write(e.Reservations.Length);
            foreach (var r in e.Reservations) { writer.Write(r.BuildingId); writer.Write(r.TargetLevel); }
            writer.Write(e.ZoneWork != null);
            if (e.ZoneWork != null) { WriteString(writer, e.ZoneWork.ZoneId); writer.Write(e.ZoneWork.RemainingSeconds); }
            writer.Write(e.AutoStart); WriteStrings(writer, e.AchievedTasks); WriteStrings(writer, e.ClaimedTasks);
            writer.Write(e.NextBuildingId); writer.Write(e.FirstClaimDone); writer.Write(e.FirstMoveDone);
        }
        private static MetaEstateState ReadEstate(BinaryReader reader)
        {
            string version = ReadString(reader), origin = ReadString(reader);
            long mono = reader.ReadInt64(), wall = reader.ReadInt64(), elapsed = reader.ReadInt64();
            string[] zones = ReadStrings(reader);
            var buildings = new MetaEstateBuilding[Count(reader)];
            for (int i = 0; i < buildings.Length; i++)
            {
                int id = reader.ReadInt32(); string kind = ReadString(reader); int x = reader.ReadInt32(), y = reader.ReadInt32(), level = reader.ReadInt32();
                string? branch = ReadBoolean(reader) ? ReadString(reader) : null;
                buildings[i] = new MetaEstateBuilding(id, kind, x, y, level, branch, reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt64());
            }
            MetaEstateWork? active = ReadBoolean(reader) ? new MetaEstateWork(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt64()) : null;
            var reservations = new MetaEstateReservation[Count(reader)];
            for (int i = 0; i < reservations.Length; i++) { reservations[i] = new MetaEstateReservation(reader.ReadInt32(), reader.ReadInt32()); }
            MetaEstateZoneWork? zoneWork = ReadBoolean(reader) ? new MetaEstateZoneWork(ReadString(reader), reader.ReadInt64()) : null;
            bool autoStart = ReadBoolean(reader); string[] achieved = ReadStrings(reader), claimed = ReadStrings(reader);
            int next = reader.ReadInt32(); bool firstClaim = ReadBoolean(reader), firstMove = ReadBoolean(reader);
            return new MetaEstateState(version, origin, mono, wall, elapsed, zones, buildings, active, reservations, zoneWork, autoStart, achieved, claimed, next, firstClaim, firstMove);
        }
        // Structure only; the rules that need the estate config are checked by AccountEstate when it runs.
        private static void ValidateEstate(MetaEstateState e)
        {
            var ids = e.Buildings.Select(x => x.Id).ToArray();
            if (e.LastMonotonicSeconds < -1 || e.LastWallSeconds < -1 || e.ElapsedSeconds < 0 || e.Buildings.Length == 0 || e.Buildings.Length > MaximumCollectionEntries ||
                ids.Distinct().Count() != ids.Length || ids.Any(x => x < 1 || x >= e.NextBuildingId) ||
                e.Buildings.Any(x => x.Level < 0 || x.Stock < 0 || x.Remainder < 0 || x.StoredSeconds < 0 || string.IsNullOrWhiteSpace(x.Kind)) ||
                e.OpenZones.Length == 0 || e.OpenZones.Distinct().Count() != e.OpenZones.Length ||
                (e.Active != null && (!ids.Contains(e.Active.BuildingId) || e.Active.TargetLevel < 1 || e.Active.RemainingSeconds < 1 || e.Active.RemainingSeconds > e.Active.TotalSeconds)) ||
                e.Reservations.Any(x => !ids.Contains(x.BuildingId) || x.TargetLevel < 1 || x.BuildingId == e.Active?.BuildingId) || e.Reservations.Select(x => x.BuildingId).Distinct().Count() != e.Reservations.Length ||
                (e.ZoneWork != null && (e.ZoneWork.RemainingSeconds < 1 || e.OpenZones.Contains(e.ZoneWork.ZoneId))) ||
                e.AchievedTasks.Distinct().Count() != e.AchievedTasks.Length || e.ClaimedTasks.Distinct().Count() != e.ClaimedTasks.Length || e.ClaimedTasks.Any(x => !e.AchievedTasks.Contains(x)))
            { throw new InvalidDataException("Invalid account estate."); }
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
