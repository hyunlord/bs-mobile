using System.Text.Json.Serialization;

namespace SowSiege.Server.Contracts;

public sealed record ResearchCommand([property: JsonRequired] Guid CommandId, [property: JsonRequired] long ExpectedVersion, [property: JsonRequired] string Type, string? NodeId);
public sealed record ResearchNodeView(string Id, string Category, int Level, int MaxLevel, string Status, string[] LockReasons,
    IReadOnlyDictionary<string, int> Cost, long Seconds, string Domain, int Value);
public sealed record ResearchJobView(string NodeId, int Level, DateTimeOffset StartedAt, DateTimeOffset CompletesAt, long RemainingSeconds);
public sealed record ResearchEffect(string Domain, int Raw, int Cap, int Applied);
public sealed record ResearchView(long AccountVersion, DateTimeOffset ServerTime, IReadOnlyDictionary<string, int> Wallet, ResearchNodeView[] Nodes,
    ResearchJobView[] Active, ResearchEffect[] Effects, string[] Settled);
public sealed record ResearchReply(bool Done, string Reason, ResearchView Research);
