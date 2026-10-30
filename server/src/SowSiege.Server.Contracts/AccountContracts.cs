using System.Text.Json;
using System.Text.Json.Serialization;

namespace SowSiege.Server.Contracts;

public sealed record AccountCommand([property: JsonRequired] Guid CommandId, [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] string Domain, [property: JsonRequired] string Operation, [property: JsonRequired] JsonElement Payload);
public sealed record ResourceAmount(string Id, int Amount);
public sealed record AccountReply(bool Done, string Reason, EstateView Estate, IReadOnlyDictionary<string, JsonElement> Domains, ResourceAmount[] Resources);
