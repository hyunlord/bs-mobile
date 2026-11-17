using System.Security.Cryptography;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class PurgeOperationsTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed record Fixture(NpgsqlDataSource Source, AuthStore Auth, Clock Time) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Source.DisposeAsync();
        public async Task<Guid> Guest()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return (await Auth.CreateGuestAsync(key.ExportSubjectPublicKeyInfo(), Guid.NewGuid().ToString(), CancellationToken.None)).AccountId;
        }
    }
    private static async Task<Fixture> Create()
    {
        string cs = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES") ?? throw new InvalidOperationException("Postgres required");
        string schema = "purge_ops_" + Guid.NewGuid().ToString("N");
        await using (var admin = NpgsqlDataSource.Create(cs))
        await using (var command = admin.CreateCommand("CREATE SCHEMA " + schema)) { await command.ExecuteNonQueryAsync(); }
        var source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs) { SearchPath = schema }.ConnectionString);
        var clock = new Clock();
        var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock);
        await accounts.InitializeAsync(CancellationToken.None);
        var auth = new AuthStore(source, accounts, new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromSeconds(60),
            AuthTests.ShippedLimits(), true, Convert.FromHexString(RecruitTests.TestKeyHex)), clock);
        await auth.InitializeAsync(CancellationToken.None);
        return new(source, auth, clock);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task FailedAccountRollsBackAndDoesNotStarveLaterAccountThenRecovers()
    {
        await using var f = await Create();
        Guid bad = await f.Guest(), good = await f.Guest();
        await f.Auth.DeleteAccountAsync(bad, CancellationToken.None);
        f.Time.Now = f.Time.Now.AddSeconds(1);
        await f.Auth.DeleteAccountAsync(good, CancellationToken.None);
        await using (var create = f.Source.CreateCommand("CREATE TABLE fixture_blocker(account_id uuid REFERENCES accounts(id))")) { await create.ExecuteNonQueryAsync(); }
        await using (var block = f.Source.CreateCommand("INSERT INTO fixture_blocker VALUES($1)"))
        { block.Parameters.AddWithValue(bad); await block.ExecuteNonQueryAsync(); }
        f.Time.Now = f.Time.Now.AddDays(31);
        var page = await f.Auth.PurgePageAsync(2, null, TimeSpan.FromMinutes(10), CancellationToken.None);
        Assert.Equal((2,1,1), (page.Selected,page.Succeeded,page.Failed));
        var state = await f.Auth.ReadPurgeOperationsAsync(CancellationToken.None);
        Assert.Equal((0L,1L,1L,0L,1L,1L), (state.GracePending,state.DuePending,state.RetryWaiting,state.RetryReady,state.Succeeded,state.Failed));
        Assert.Equal(0, (await f.Auth.PurgePageAsync(2,null,TimeSpan.FromMinutes(10),CancellationToken.None)).Selected);
        Assert.Equal(0L,state.RetryFinished);
        f.Time.Now = f.Time.Now.AddMinutes(10);
        Assert.Equal(1,(await f.Auth.PurgePageAsync(2,null,TimeSpan.FromMinutes(10),CancellationToken.None)).Failed);
        state = await f.Auth.ReadPurgeOperationsAsync(CancellationToken.None);
        Assert.Equal((2L,1L,0L),(state.Failed,state.RetryFinished,state.Recovered));
        await using (var unblock = f.Source.CreateCommand("DELETE FROM fixture_blocker")) { await unblock.ExecuteNonQueryAsync(); }
        f.Time.Now = f.Time.Now.AddMinutes(10);
        Assert.Equal(1L,(await f.Auth.ReadPurgeOperationsAsync(CancellationToken.None)).RetryReady);
        Assert.Equal(1, await f.Auth.PurgeExpiredAsync(2,CancellationToken.None));
        state = await f.Auth.ReadPurgeOperationsAsync(CancellationToken.None);
        Assert.Equal((0L,2L,1L,2L),(state.DuePending,state.Succeeded,state.Recovered,state.RetryFinished));
        await using var remaining = f.Source.CreateCommand("SELECT count(*) FROM auth_purge_retries");
        Assert.Equal(0L,await remaining.ExecuteScalarAsync());
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task RestorationCascadeAndNewDeletionGenerationDoNotInheritOldFailure()
    {
        await using var f = await Create();
        Guid account = await f.Guest();
        await f.Auth.DeleteAccountAsync(account,CancellationToken.None);
        var oldCandidate = new AuthStore.PurgeCandidate(account, f.Time.Now);
        await using (var seed = f.Source.CreateCommand("INSERT INTO auth_purge_retries SELECT account_id,deleted_at,1,$2,$2,'storage' FROM auth_deleted_accounts WHERE account_id=$1"))
        { seed.Parameters.AddWithValue(account); seed.Parameters.AddWithValue(f.Time.Now); await seed.ExecuteNonQueryAsync(); }
        // Mirrors the existing restoration mutation; FK cleanup is the new contract under test.
        await using (var restore = f.Source.CreateCommand("DELETE FROM auth_deleted_accounts WHERE account_id=$1"))
        { restore.Parameters.AddWithValue(account); await restore.ExecuteNonQueryAsync(); }
        await using (var count = f.Source.CreateCommand("SELECT count(*) FROM auth_purge_retries")) { Assert.Equal(0L,await count.ExecuteScalarAsync()); }
        f.Time.Now = f.Time.Now.AddSeconds(1);
        await f.Auth.DeleteAccountAsync(account,CancellationToken.None);
        // Exercise delayed old-generation failure bookkeeping after a new deletion has committed.
        var record = typeof(AuthStore).GetMethod("RecordPurgeFailureAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)record.Invoke(f.Auth, new object[]{oldCandidate,new InvalidOperationException("fixture"),false,TimeSpan.FromMinutes(10),CancellationToken.None})!;
        var view=await f.Auth.ReadPurgeOperationsAsync(CancellationToken.None);
        Assert.Equal((1L,0L,0L,0L),(view.GracePending,view.RetryReady,view.Failed,view.RetryFinished));
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task TwoInitialFailuresRecordedAfterBothAttemptsDoNotBecomeRetries()
    {
        await using var f=await Create();
        Guid account=await f.Guest();
        await f.Auth.DeleteAccountAsync(account,CancellationToken.None);
        var candidate=new AuthStore.PurgeCandidate(account,f.Time.Now);
        var record=typeof(AuthStore).GetMethod("RecordPurgeFailureAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var bothAttemptsFinished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRecorded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int finished=0;
        async Task CompleteFailedAttempt(bool second)
        {
            // Both simulated actual attempts observed no retry row under their account lock.
            bool wasRetryAtAttempt=false;
            if(Interlocked.Increment(ref finished)==2) { bothAttemptsFinished.SetResult(); }
            await bothAttemptsFinished.Task;
            if(second) { await firstRecorded.Task; }
            await (Task)record.Invoke(f.Auth,new object[]{candidate,new InvalidOperationException("fixture"),wasRetryAtAttempt,TimeSpan.FromMinutes(10),CancellationToken.None})!;
            if(!second) { firstRecorded.SetResult(); }
        }
        await Task.WhenAll(CompleteFailedAttempt(false),CompleteFailedAttempt(true)).WaitAsync(TimeSpan.FromSeconds(15));
        var state=await f.Auth.ReadPurgeOperationsAsync(CancellationToken.None);
        Assert.Equal((2L,0L,0L),(state.Failed,state.RetryFinished,state.Recovered));
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task CancellationDoesNotCountAsFailure()
    {
        await using var f=await Create();
        using var cancel=new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Auth.PurgeExpiredAsync(2,cancel.Token));
        var state=await f.Auth.ReadPurgeOperationsAsync(CancellationToken.None);
        Assert.Equal((0L,0L),(state.Failed,state.RetryFinished));
    }
}
