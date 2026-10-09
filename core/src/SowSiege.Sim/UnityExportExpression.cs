using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class UnityExportExpression
{
    public static string Write(object? value) => WriteValue(value, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static string WriteValue(object? value, HashSet<object> active)
    {
        if (value is null) { return "null"; }
        if (value is string text) { return JsonSerializer.Serialize(text); }
        if (value is bool flag) { return flag ? "true" : "false"; }
        if (value is int number) { return number.ToString(CultureInfo.InvariantCulture); }
        if (value is long large) { return large.ToString(CultureInfo.InvariantCulture) + "L"; }
        var type = value.GetType();
        if (type.IsEnum && type.Assembly == typeof(ContentCatalog).Assembly && type.Namespace == "SowSiege.Core")
        {
            var name = Enum.GetName(type, value) ?? throw new InvalidDataException("Undefined Unity export enum value.");
            return TypeName(type) + "." + name;
        }
        if (!active.Add(value)) { throw new InvalidDataException("Cycle in Unity export graph."); }
        try
        {
            if (value is Array array && array.Rank == 1)
            {
                var items = string.Join(", ", array.Cast<object?>().Select(item => WriteValue(item, active)));
                return "new " + TypeName(type.GetElementType()!) + "[] { " + items + " }";
            }
            if (value is IDictionary dictionary && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var arguments = type.GetGenericArguments();
                if (arguments[0] != typeof(string)) { throw new InvalidDataException("Unity export dictionaries require string keys."); }
                var entries = new List<string>();
                foreach (DictionaryEntry entry in dictionary) { entries.Add("{ " + WriteValue(entry.Key, active) + ", " + WriteValue(entry.Value, active) + " }"); }
                // Preserve loader insertion order: Core iteration order is part of deterministic gameplay.
                return "new global::System.Collections.Generic.Dictionary<string, " + TypeName(arguments[1]) + ">(global::System.StringComparer.Ordinal) { " +
                    string.Join(", ", entries) + " }";
            }
            if (type.Assembly != typeof(ContentCatalog).Assembly || !type.IsSealed || type.Namespace != "SowSiege.Core")
            {
                throw new InvalidDataException($"Unsupported Unity export type: {type.FullName}");
            }
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (constructors.Length != 1) { throw new InvalidDataException($"Ambiguous Unity export constructor: {type.FullName}"); }
            var parameters = constructors[0].GetParameters();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            if (properties.Length != parameters.Length || type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length != 0)
            {
                throw new InvalidDataException($"Unity export would omit state: {type.FullName}");
            }
            var values = parameters.Select(parameter =>
            {
                var property = properties.SingleOrDefault(candidate => candidate.Name == parameter.Name && candidate.PropertyType == parameter.ParameterType);
                if (property is null || property.GetIndexParameters().Length != 0 || property.GetMethod is null)
                {
                    throw new InvalidDataException($"Unsupported Unity export property: {type.FullName}.{parameter.Name}");
                }
                return WriteValue(property.GetValue(value), active);
            });
            return "new " + TypeName(type) + "(" + string.Join(", ", values) + ")";
        }
        finally { active.Remove(value); }
    }

    private static string TypeName(Type type)
    {
        if (type == typeof(string)) { return "string"; }
        if (type == typeof(int)) { return "int"; }
        if (type == typeof(long)) { return "long"; }
        if (type == typeof(bool)) { return "bool"; }
        if (type.IsArray && type.GetArrayRank() == 1) { return TypeName(type.GetElementType()!) + "[]"; }
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) || type.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
        {
            return "global::System.Collections.Generic." + type.Name.Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
        }
        if (type.Assembly == typeof(ContentCatalog).Assembly && type.Namespace == "SowSiege.Core" && type.IsSealed)
        {
            return "global::" + type.FullName;
        }
        throw new InvalidDataException($"Unsupported Unity export type name: {type.FullName}");
    }
}
