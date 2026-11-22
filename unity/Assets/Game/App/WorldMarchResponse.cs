using System;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace Game.App
{
    [Serializable] public sealed class WorldMarchTroops { public string unitId; public int count; }
    [Serializable] public sealed class WorldMarchOrder
    {
        public string commandId, operation, targetId, heroId, marchId;
        public long expectedVersion, generation;
        public WorldMarchTroops[] troops;
    }
    public sealed class WorldMarchView
    {
        public string Id, TargetId, Operation, HeroId, State, Reason;
        public long Generation, StartedAtMs, DueAtMs;
        public WorldMarchTroops[] Troops;
        public Vector2Int[] Path;
    }
    public sealed class WorldMarchResult { public bool Done; public string Reason; public WorldMarchView March; }
    public sealed class WorldShieldView { public long UtcHighWatermark, ShieldUntilMs, NextFreeAtMs, HostileUntilMs; }
    public static class WorldMarchResponse
    {
        [Serializable] sealed class GatherOrder
        {
            public string commandId, operation, targetId, heroId;
            public long expectedVersion, generation;
            public WorldMarchTroops[] troops;
        }
        [Serializable] sealed class RecallOrder { public string commandId, operation, marchId; public long expectedVersion; }
        public static string OrderJson(WorldMarchOrder order)
        {
            var json = order.operation == "cancel"
                ? JsonUtility.ToJson(new RecallOrder { commandId=order.commandId,expectedVersion=order.expectedVersion,operation=order.operation,marchId=order.marchId })
                : JsonUtility.ToJson(new GatherOrder { commandId=order.commandId,expectedVersion=order.expectedVersion,operation=order.operation,targetId=order.targetId,generation=order.generation,heroId=order.heroId,troops=order.troops });
            ValidateOrder(json); return json;
        }
        public const string Endpoint = "/v1/world/marches/commands";
        static string Text(XElement node, string name) => AccountJson.StringValue(AccountJson.Field(node, name));
        static long Number(XElement node, string name)
        {
            var value = AccountJson.LongValue(AccountJson.Field(node, name));
            if (value < 0) throw new FormatException("Negative world march value."); return value;
        }
        static WorldMarchTroops Troop(XElement node)
        {
            var result = new WorldMarchTroops { unitId = Text(node, "unitId"), count = checked((int)Number(node, "count")) };
            if (string.IsNullOrWhiteSpace(result.unitId) || result.count < 1) throw new FormatException("Invalid march troop."); return result;
        }
        static WorldMarchView March(XElement node)
        {
            var result = new WorldMarchView { Id = Text(node, "marchId"), TargetId = Text(node, "targetId"), Generation = Number(node, "generation"), Operation = Text(node, "operation"),
                HeroId = Text(node, "heroId"), StartedAtMs = Number(node, "startedAtMs"), DueAtMs = Number(node, "dueAtMs"), State = Text(node, "state"), Reason = Text(node, "reason"),
                Troops = AccountJson.Array(node, "troops", Troop), Path = AccountJson.Array(node, "path", point => new Vector2Int(checked((int)Number(point,"x")), checked((int)Number(point,"y")))) };
            if (!Guid.TryParse(result.Id, out var id) || id == Guid.Empty || result.Generation < 1 || string.IsNullOrWhiteSpace(result.TargetId) || string.IsNullOrWhiteSpace(result.HeroId)
                || !new[] { "outbound", "gathering", "returning", "returned" }.Contains(result.State) || result.Troops.Length == 0 || result.Path.Length == 0
                || result.Troops.Select(t=>t.unitId).Distinct(StringComparer.Ordinal).Count()!=result.Troops.Length) throw new FormatException("Invalid march view.");
            return result;
        }
        public static WorldMarchView[] ReadList(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("[", StringComparison.Ordinal)) throw new FormatException("Expected march array.");
            var result = AccountJson.Array(AccountJson.Parse("{\"marches\":" + json + "}"), "marches", March);
            if (result.Select(m=>m.Id).Distinct(StringComparer.Ordinal).Count()!=result.Length) throw new FormatException("Duplicate march."); return result;
        }
        public static WorldMarchResult ReadReply(string json)
        {
            var node = AccountJson.Parse(json); var done = AccountJson.Field(node,"done","boolean");
            if (!bool.TryParse(done.Value, out var success)) throw new FormatException("Invalid march outcome.");
            var march = AccountJson.Field(node,"march");
            return new WorldMarchResult { Done = success, Reason = Text(node,"reason"), March = (string)march.Attribute("type") == "null" ? null : March(march) };
        }
        public static WorldShieldView ReadShield(string json)
        {
            var node = AccountJson.Parse(json);
            return new WorldShieldView { UtcHighWatermark=Number(node,"utcHighWatermark"), ShieldUntilMs=Number(node,"shieldUntilMs"), NextFreeAtMs=Number(node,"nextFreeAtMs"), HostileUntilMs=Number(node,"hostileUntilMs") };
        }
        public static string ValidateOrder(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 32768) throw new FormatException("Invalid march order size.");
            var node = AccountJson.Parse(json); var operation = Text(node,"operation");
            var allowed = new[] { "commandId", "expectedVersion", "operation", "targetId", "generation", "heroId", "troops", "marchId" };
            if (node.Elements().Any(e=>!allowed.Contains(e.Name.LocalName))) throw new FormatException("Unexpected march field.");
            if (!Guid.TryParse(Text(node,"commandId"), out var id) || id == Guid.Empty) throw new FormatException("Invalid march command identity.");
            Number(node,"expectedVersion");
            if (operation == "cancel")
            {
                if (!Guid.TryParse(Text(node,"marchId"), out id) || id == Guid.Empty) throw new FormatException("Invalid recalled march.");
            }
            else if (operation == "gather")
            {
                if (string.IsNullOrWhiteSpace(Text(node,"targetId")) || string.IsNullOrWhiteSpace(Text(node,"heroId")) || Number(node,"generation") < 1) throw new FormatException("Invalid gather target.");
                var troops = AccountJson.Array(node,"troops",Troop);
                if (troops.Length == 0 || troops.Select(t=>t.unitId).Distinct(StringComparer.Ordinal).Count()!=troops.Length) throw new FormatException("Invalid gather troops.");
            }
            else throw new FormatException("Unsupported march operation.");
            return Endpoint;
        }
    }
}
