using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class HostJson
{
    public static JsonSerializerOptions CreateOptions(bool includeFields = false, bool camelCase = false, bool indented = false)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            foreach (var property in type.Properties)
            {
                if (property.AttributeProvider?.IsDefined(typeof(OmitWhenNullAttribute), inherit: true) == true)
                {
                    property.ShouldSerialize = (_, value) => value is not null;
                }
            }
        });
        return new JsonSerializerOptions
        {
            IncludeFields = includeFields,
            PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null,
            WriteIndented = indented,
            TypeInfoResolver = resolver
        };
    }
}
