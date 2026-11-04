using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

public static class PurgeCycle
{
    // A returned cursor survives timer ticks. Only an observed queue end resets it; never wrap in the same cycle.
    public static async Task<AuthStore.PurgeCandidate?> RunAsync(AuthStore.PurgeCandidate? cursor, int budget,
        Func<int, AuthStore.PurgeCandidate?, CancellationToken, Task<AuthStore.PurgePage>> page, CancellationToken ct)
    {
        if (budget < 1) { throw new ArgumentOutOfRangeException(nameof(budget)); }
        int selected = 0;
        while (selected < budget)
        {
            ct.ThrowIfCancellationRequested();
            var result = await page(Math.Min(50, budget - selected), cursor, ct);
            if (result.Selected == 0) { return null; }
            selected += result.Selected;
            cursor = result.Cursor;
        }
        return cursor;
    }
}
