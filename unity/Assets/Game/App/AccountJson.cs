using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Game.App
{
    // Explicit server projection: no reflection-based DTO activation or package dependency.
    internal static class AccountJson
    {
        internal static XElement Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 1048576) throw new FormatException("Invalid account JSON size.");
            var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 32, MaxStringContentLength = 1048576, MaxArrayLength = 16384, MaxBytesPerRead = 4096, MaxNameTableCharCount = 65536 };
            using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json), quotas);
            var root = XElement.Load(reader); RequireType(root, "object");
            foreach (var item in root.DescendantsAndSelf())
                if ((string)item.Attribute("type") == "object")
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var child in item.Elements())
                        if (!names.Add((string)child.Attribute("item") ?? child.Name.LocalName)) throw new FormatException("Duplicate account JSON field.");
                }
            return root;
        }
        internal static void RequireType(XElement node, string type)
        {
            if (node == null || (string)node.Attribute("type") != type) throw new FormatException("Unexpected account JSON type.");
        }
        internal static XElement Field(XElement node, string name, string type = null)
        {
            RequireType(node, "object");
            var values = node.Elements(name).ToArray();
            if (values.Length != 1) throw new FormatException("Missing or duplicate account JSON field: " + name);
            if (type != null) RequireType(values[0], type);
            return values[0];
        }
        internal static string StringValue(XElement node) { RequireType(node, "string"); return node.Value; }
        internal static long LongValue(XElement node)
        {
            RequireType(node, "number");
            if (!long.TryParse(node.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)) throw new FormatException("Invalid account integer.");
            return value;
        }
        internal static double DoubleValue(XElement node)
        {
            RequireType(node, "number");
            if (!double.TryParse(node.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("Invalid account number.");
            return value;
        }
        internal static T[] Array<T>(XElement node, string key, Func<XElement,T> read) => Field(node,key,"array").Elements().Select(read).ToArray();
        internal static Dictionary<string,int> Map(XElement node, string key)
        {
            var result = new Dictionary<string,int>(StringComparer.Ordinal);
            foreach (var item in Field(node,key,"object").Elements())
            {
                var id = (string)item.Attribute("item") ?? item.Name.LocalName;
                if (result.ContainsKey(id)) throw new FormatException("Duplicate account map entry.");
                result.Add(id,checked((int)LongValue(item)));
            }
            return result;
        }
    }
}
