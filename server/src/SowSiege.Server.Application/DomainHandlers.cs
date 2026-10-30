using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Server.Application;

public sealed record DomainContext(StoredAccount Account, JsonElement State, long UtcMilliseconds);
public sealed record DomainEvent(string Kind, JsonElement Data);
public sealed record DomainResult(MetaState AccountState, JsonElement State, JsonElement View,
    bool Done, string Reason, DomainEvent[] Events);

// Handlers are pure account transformations: no database, clock, network, or random I/O.
public interface IDomainHandler
{
    string Domain { get; }
    int StateVersion { get; }
    string ContentHash { get; }
    JsonElement InitialState { get; }
    DomainResult Advance(DomainContext context);
    DomainResult Execute(DomainContext context, string operation, JsonElement payload);
}

public sealed class DomainRegistry
{
    public IReadOnlyList<IDomainHandler> Handlers { get; }
    public DomainRegistry(IEnumerable<IDomainHandler> handlers)
    {
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
