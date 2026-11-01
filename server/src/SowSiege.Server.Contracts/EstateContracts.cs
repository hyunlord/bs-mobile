using System.Text.Json.Serialization;

namespace SowSiege.Server.Contracts;

public sealed record EstateCommand([property: JsonRequired] Guid CommandId, [property: JsonRequired] long ExpectedVersion, [property: JsonRequired] string Type, int? BuildingId);
public sealed record BuildingView(int Id, string Kind, int Level, int X, int Y, int Stock);
public sealed record WorkView(int BuildingId, int TargetLevel, long RemainingSeconds);
public sealed record EstateView(long Version, string ConfigVersion, IReadOnlyDictionary<string, int> Wallet,
    BuildingView[] Buildings, WorkView? Construction, long CreditedSeconds, string Clock);
public sealed record CommandReply(bool Done, string Reason, EstateView Estate);
public sealed record AccountCreated(string Token, DateTimeOffset ExpiresAt, EstateView Estate);
public sealed record ApiError(string Code);
