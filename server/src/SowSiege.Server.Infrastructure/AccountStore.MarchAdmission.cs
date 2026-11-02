using Npgsql;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed partial class AccountStore
{
    // The common account lock serializes free expedition issue with dispatch; no world lock is acquired here.
    private static async Task GuardMarchHeroAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid account, CancellationToken cancellation)
    {
        await using (var exists = new NpgsqlCommand("SELECT to_regclass('world_marches') IS NOT NULL", c, t))
        {
            if (await exists.ExecuteScalarAsync(cancellation) is not true) { return; }
        }
        await using var query = new NpgsqlCommand("""
            SELECT 1 FROM world_marches
            WHERE account_id=$1 AND state<>'returned' AND hero_id=payload->'snapshot'->>'basicHero' LIMIT 1
            """, c, t);
        query.Parameters.AddWithValue(account);
        if (await query.ExecuteScalarAsync(cancellation) is not null) { throw new CommandConflictException("hero-busy"); }
    }
}
