using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

internal sealed record WorldAccountContext(StoredAccount Account, IReadOnlyDictionary<string, JsonElement> States,
    long UtcMilliseconds, bool HasHeldMaterials);
internal sealed record WorldDomainUpdate(string Domain, JsonElement State, DomainEvent[] Events);
internal sealed record WorldAccountChanges(string Source, WorldDomainUpdate[] Domains, DomainCredit[] Credits,
    DomainCredit[]? Debits = null);

public sealed partial class AccountStore
{
    // Caller holds world before accounts (UUID order), owns the completion receipt and commits this transaction.
    // This trusted hook has no HTTP route; callers cannot bypass the common domain/credit/event/outbox writer.
    internal Task<string> ExecuteWorldAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id,
        Guid operationId, long now, Func<WorldAccountContext, CancellationToken, Task<WorldAccountChanges>> mutate,
        CancellationToken cancellation)
    {
        if (operationId == Guid.Empty) { throw new ArgumentException("World operation identity required.", nameof(operationId)); }
        return ExecuteInTransactionAsync(connection, transaction, id, null, null, true, cancellation,
            new(operationId, now, mutate));
    }

    private sealed record WorldMutation(Guid Id, long Now,
        Func<WorldAccountContext, CancellationToken, Task<WorldAccountChanges>> Apply);
}
