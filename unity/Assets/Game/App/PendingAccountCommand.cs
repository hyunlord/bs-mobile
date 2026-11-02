using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace Game.App
{
    // This journal contains only a command and a one-way account scope digest, never credentials.
    public sealed class PendingAccountCommand
    {
        [Serializable] sealed class Journal
        {
            public int version;
            public string scope;
            public string body;
        }
        [Serializable] sealed class Envelope { public string commandId, domain, operation; public long expectedVersion; }
        readonly string path, scopeHash;
        public PendingAccountCommand(string root, string accountScope)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(accountScope)) throw new ArgumentException("Pending command needs an account scope.");
            scopeHash = Fingerprint(accountScope); path = Path.Combine(root, scopeHash + ".json");
        }
        public string Read()
        {
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 65536) throw new FormatException("Pending command journal is oversized.");
            var root = AccountJson.Parse(new UTF8Encoding(false, true).GetString(bytes));
            AccountJson.Field(root, "version", "number"); AccountJson.Field(root, "scope", "string");
            var savedBody = AccountJson.Field(root, "body");
            if ((string)savedBody.Attribute("type") != "null") AccountJson.RequireType(savedBody, "string");
            var journal = JsonUtility.FromJson<Journal>(new UTF8Encoding(false, true).GetString(bytes));
            if (journal == null || journal.version != 1 || journal.scope != scopeHash) throw new FormatException("Invalid pending command journal.");
            if (!string.IsNullOrEmpty(journal.body)) Domain(journal.body);
            return string.IsNullOrEmpty(journal.body) ? null : journal.body;
        }
        public void Write(string body)
        {
            if (body != null) Domain(body);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Journal { version = 1, scope = scopeHash, body = body }));
            var temporary = path + ".writing";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        public static string Domain(string body)
        {
            if (string.IsNullOrWhiteSpace(body) || body.Length > 32768) throw new FormatException("Invalid pending command.");
            var root = AccountJson.Parse(body);
            var required = new[] { "commandId", "expectedVersion", "domain", "operation", "payload" };
            if ((string)root.Attribute("type") != "object" || required.Any(name => root.Elements(name).Count() != 1)
                || (string)root.Element("payload").Attribute("type") != "object"
                || (string)root.Element("expectedVersion").Attribute("type") != "number"
                || !long.TryParse(root.Element("expectedVersion").Value, out _))
                throw new FormatException("Incomplete pending command envelope.");
            var value = JsonUtility.FromJson<Envelope>(body);
            if (value == null || !Guid.TryParse(value.commandId, out var id) || id == Guid.Empty || value.expectedVersion < 0
                || string.IsNullOrEmpty(value.operation) || (value.domain != "training" && value.domain != "recruit" && value.domain != "research" && value.domain != "alliance"))
                throw new FormatException("Invalid pending command envelope.");
            return value.domain;
        }
        public static string Fingerprint(string value)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}
