using Npgsql;

namespace SowSiege.Server.Infrastructure;

/// <summary>Drain due world events, then retain the world lock through the supplied account/security transaction.</summary>
public interface IWorldRecoveryBarrier
{
    Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, long, CancellationToken, Task<T>> action, CancellationToken ct);
}

/// <summary>Late attachment avoids a constructor cycle between account, security and march stores.</summary>
public sealed class WorldRecoveryGate(NpgsqlDataSource source, TimeProvider time)
{
    private IWorldRecoveryBarrier? barrier;
    public bool IsAttached => Volatile.Read(ref barrier) is not null;

    public void Attach(IWorldRecoveryBarrier value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var previous = Interlocked.CompareExchange(ref barrier, value, null);
        if (previous is not null && !ReferenceEquals(previous, value))
        {
            throw new InvalidOperationException("A different world recovery barrier is already attached.");
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, long, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var attached = Volatile.Read(ref barrier);
        if (attached is not null) { return await attached.ExecuteAsync(action, ct); }
        await using var c = await source.OpenConnectionAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        await using var check = new NpgsqlCommand("SELECT to_regclass('world_marches') IS NOT NULL", c, t);
        if ((bool)(await check.ExecuteScalarAsync(ct))!)
        {
            throw new InvalidOperationException("Persisted marches require an attached world recovery barrier.");
        }
        T result = await action(c, t, time.GetUtcNow().ToUnixTimeMilliseconds(), ct);
        await t.CommitAsync(ct);
        return result;
    }
}
