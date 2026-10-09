using System;
using System.IO;
using System.Text;

namespace SowSiege.Core
{
    public sealed record MetaReplayProjection(ContentCatalog Catalog, MetaRunPlan Plan);

    public static class MetaReplayContext
    {
        private const int MinimumBytes = 140;
        private const int HashBytes = 64;
        private const int Magic = 0x4D52504C;
        private const int MaximumBytes = 1049000;

        public static byte[] Encode(MetaRunPlan plan, string baseDataHash, ContentCatalog projected)
        {
            ReplayCodec.ValidateHash(baseDataHash);
            if (plan.State.PendingRun != plan.Run) { throw new ArgumentException("Replay requires the exact pending run snapshot."); }
            byte[] state = MetaSaveCodec.Encode(plan.State);
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output, Encoding.UTF8, true);
            writer.Write(Magic); writer.Write(1);
            writer.Write(Encoding.ASCII.GetBytes(baseDataHash));
            writer.Write(Encoding.ASCII.GetBytes(PortableStateCodec.HashCatalog(projected)));
            writer.Write(state.Length); writer.Write(state);
            return output.ToArray();
        }

        public static MetaReplayProjection Restore(byte[] bytes, ContentCatalog source, MetaCatalog meta, string actualDataHash)
        {
            ReplayCodec.ValidateHash(actualDataHash);
            if (bytes == null || bytes.Length < MinimumBytes || bytes.Length > MaximumBytes) { throw new InvalidDataException("Invalid meta replay size."); }
            using var input = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(input, Encoding.UTF8, true);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != 1) { throw new InvalidDataException("Unsupported meta replay context."); }
            string baseHash = Encoding.ASCII.GetString(reader.ReadBytes(HashBytes));
            string projectedHash = Encoding.ASCII.GetString(reader.ReadBytes(HashBytes));
            if (!string.Equals(baseHash, actualDataHash, StringComparison.Ordinal)) { throw new InvalidDataException("Meta replay base data mismatch."); }
            int length = reader.ReadInt32();
            if (length <= 0 || length != bytes.Length - input.Position) { throw new InvalidDataException("Invalid meta snapshot length."); }
            var decoded = MetaSaveCodec.Decode(reader.ReadBytes(length));
            if (!decoded.Valid || decoded.State == null || decoded.State.PendingRun == null || MetaValidation.ValidateState(meta, decoded.State).Length != 0)
            { throw new InvalidDataException("Invalid meta replay state."); }
            var plan = new MetaRunPlan(decoded.State, decoded.State.PendingRun);
            var projected = MetaRunAdapter.ProjectCatalog(source, meta, plan.State, plan);
            if (PortableStateCodec.HashCatalog(projected) != projectedHash) { throw new InvalidDataException("Meta replay projected catalog mismatch."); }
            return new MetaReplayProjection(projected, plan);
        }

        public static ReplayVerification Verify(byte[] context, ContentCatalog source, MetaCatalog meta, string actualDataHash, ReplayDocument replay)
        {
            var projection = Restore(context, source, meta, actualDataHash);
            if (projection.Plan.Run.Seed != replay.Header.Options.Run.Seed) { throw new InvalidDataException("Meta replay seed mismatch."); }
            return ReplayRunner.Verify(projection.Catalog, actualDataHash, replay);
        }
    }
}
