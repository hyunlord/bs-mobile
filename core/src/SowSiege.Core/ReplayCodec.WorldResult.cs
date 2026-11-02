using System;
using System.IO;

namespace SowSiege.Core
{
    public static partial class ReplayCodec
    {
        // These transport helpers validate claims, not execution or settlement authority.
        public static byte[] EncodeWorldEnd(ReplayHeader issuedHeader, WorldReplayEnd end)
        {
            ValidateWorldEnd(issuedHeader, end);
            using var output = new MemoryStream();
            WriteWorldEnd(output, end);
            return output.ToArray();
        }

        public static WorldReplayEnd DecodeWorldEnd(ReplayHeader issuedHeader, byte[] bytes)
        {
            if (bytes is null || bytes.Length < sizeof(int) * 2 || bytes.Length > MaximumWorldEndBytes + sizeof(int) * 2)
            { throw new InvalidDataException("Invalid world result size."); }
            try
            {
                using var input = new MemoryStream(bytes, false);
                using var reader = new BinaryReader(input, Utf8, true);
                if (reader.ReadInt32() != WorldEndTag) { throw new InvalidDataException("Invalid world result tag."); }
                var end = ReadWorldEnd(reader);
                ValidateWorldEnd(issuedHeader, end);
                if (input.Position != input.Length) { throw new InvalidDataException("Trailing world result bytes."); }
                return end;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is EndOfStreamException || exception is OverflowException)
            { throw new InvalidDataException("Invalid world result claim.", exception); }
        }
    }
}
