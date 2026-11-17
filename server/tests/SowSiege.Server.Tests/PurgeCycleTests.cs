using SowSiege.Server.Host;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class PurgeCycleTests
{
    [Fact]
    public async Task LockedPrefixCannotStarveTailAcrossBudgetedCyclesAndEndDoesNotWrap()
    {
        var first = new AuthStore.PurgeCandidate(Guid.NewGuid(),DateTimeOffset.UnixEpoch);
        var second = new AuthStore.PurgeCandidate(Guid.NewGuid(),DateTimeOffset.UnixEpoch.AddSeconds(1));
        var tail = new AuthStore.PurgeCandidate(Guid.NewGuid(),DateTimeOffset.UnixEpoch.AddSeconds(2));
        var queue = new[]{first,second,tail};
        var visited = new List<Guid>();
        Task<AuthStore.PurgePage> Read(int batch, AuthStore.PurgeCandidate? after, CancellationToken ct)
        {
            var next=queue.Where(c=>after is null || c.DeletedAt>after.DeletedAt).Take(batch).ToArray();
            visited.AddRange(next.Select(c=>c.AccountId));
            int success=next.Count(c=>c==tail);
            return Task.FromResult(new AuthStore.PurgePage(next.Length,success,0,next.Length-success,next.LastOrDefault()??after));
        }
        var cursor=await PurgeCycle.RunAsync(null,2,Read,CancellationToken.None);
        Assert.Equal(second,cursor);
        cursor=await PurgeCycle.RunAsync(cursor,2,Read,CancellationToken.None);
        Assert.Null(cursor); // tail then empty page; no wrap to the locked prefix in this cycle
        Assert.Equal(new[]{first.AccountId,second.AccountId,tail.AccountId},visited);
        cursor=await PurgeCycle.RunAsync(cursor,2,Read,CancellationToken.None);
        Assert.Equal(second,cursor);
        Assert.Equal(5,visited.Count);
    }

    [Fact]
    public async Task CancellationPropagatesBeforeReadingAnotherPage()
    {
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        int calls=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>PurgeCycle.RunAsync(null,2,
            (_,_,_)=> { calls++; return Task.FromResult(new AuthStore.PurgePage(0,0,0,0,null)); },cancellation.Token));
        Assert.Equal(0,calls);
    }
}
