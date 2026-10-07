using System.Security.Cryptography;
using System.Text.Json;

namespace SowSiege.Core;

internal static class StateHash
{
    public static string Compute(object state)
    {
        var element = JsonSerializer.SerializeToElement(state, new JsonSerializerOptions { IncludeFields = true });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { WriteCanonical(writer, element); }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in element.EnumerateArray()) { WriteCanonical(writer, value); }
                writer.WriteEndArray();
                break;
            default: element.WriteTo(writer); break;
        }
    }
}
