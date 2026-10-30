using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Server.Application;

/// <summary>Held: read-only per-material amounts currently waiting in the account held queue that this domain credited (set for Execute).</summary>
public sealed record DomainContext(StoredAccount Account, JsonElement State, long UtcMilliseconds, Guid? CommandId = null, byte[]? Seed = null,
    IReadOnlyDictionary<string, int>? Held = null);
public sealed record DomainResource(string Id, int Amount);
public sealed record DomainEvent(string Kind, JsonElement Data);
/// <summary>A material the account should receive. The shared writer credits it in arrival order, holding what the wallet cannot take.</summary>
public sealed record DomainCredit(string Material, int Amount);
public sealed record DomainResult(MetaState AccountState, JsonElement State, JsonElement View,
    bool Done, string Reason, DomainEvent[] Events, DomainCredit[]? Credits = null);

// Handlers are pure account transformations: no database, clock, network, or random I/O.
public interface IDomainHandler
{
    string Domain { get; }
    int StateVersion { get; }
    string ContentHash { get; }
    JsonElement InitialState { get; }
    JsonElement View(DomainContext context);
    DomainResult Advance(DomainContext context);
    DomainResult Execute(DomainContext context, string operation, JsonElement payload);
    DomainResult AfterCommand(DomainContext context) =>
        new(context.Account.State, context.State, View(context), true, "", []);
    // Optional: material for a server-keyed HMAC seed that the store computes and passes in DomainContext.Seed.
    string? SeedMaterial(DomainContext context, string operation, JsonElement payload) => null;
    // Optional: account currencies held in domain state, exposed in the reply resources array.
    DomainResource[] Resources(JsonElement state) => [];
}

/// <summary>Server-keyed deterministic seed: HMAC-SHA256(key, account, material). Computed outside handlers.</summary>
public static class DomainSeed
{
    public static byte[] Compute(byte[] key, Guid account, string material) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(account.ToString("D") + "\n" + material));
}

public sealed class DomainRegistry
{
    public IReadOnlyList<IDomainHandler> Handlers { get; }
    public byte[]? SeedKey { get; }
    public DomainRegistry(IEnumerable<IDomainHandler> handlers) : this(handlers, null) { }
    public DomainRegistry(IEnumerable<IDomainHandler> handlers, byte[]? seedKey)
    {
        SeedKey = seedKey;
        var ordered = handlers.OrderBy(h => h.Domain, StringComparer.Ordinal).ToArray();
        if (ordered.Any(h => string.IsNullOrWhiteSpace(h.Domain) || h.StateVersion < 1 || string.IsNullOrWhiteSpace(h.ContentHash)) ||
            ordered.Select(h => h.Domain).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
        {
            throw new InvalidOperationException("Invalid or duplicate domain registration.");
        }
        Handlers = ordered;
    }

    public IDomainHandler Resolve(string domain) => Handlers.SingleOrDefault(h => h.Domain == domain)
        ?? throw new CommandConflictException("unsupported-domain");
}
