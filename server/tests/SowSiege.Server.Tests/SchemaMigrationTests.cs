using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class SchemaMigrationTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task VersionOneMigrationPreservesPayloadAndRollsBackFailedDdl()
    {
        string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES") ?? throw new InvalidOperationException("Postgres required.");
        string schema = "migration_" + Guid.NewGuid().ToString("N");
        await using var admin = NpgsqlDataSource.Create(connection);
        await using (var create = admin.CreateCommand("CREATE SCHEMA " + schema))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString);
            var store = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), TimeProvider.System);
            await store.InitializeAsync(CancellationToken.None);
            var created = await store.CreateAsync(CancellationToken.None);
            Guid id = (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
            var command = new EstateCommand(Guid.NewGuid(), 0, "claim-production", null);
            string originalReceipt = await store.ExecuteAsync(id, command, CancellationToken.None);
            await using var payload = source.CreateCommand("SELECT payload FROM accounts WHERE id=$1");
            payload.Parameters.AddWithValue(id);
            var before = (byte[])(await payload.ExecuteScalarAsync())!;
            await using var restoreV1 = source.CreateCommand("DROP TABLE account_held_materials,account_domains,account_job_clocks,domain_events; DELETE FROM server_schema; INSERT INTO server_schema VALUES(1); ALTER TABLE server_schema ADD CONSTRAINT no_two CHECK(version=1)");
            await restoreV1.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<PostgresException>(() => store.InitializeAsync(CancellationToken.None));
            await using var version = source.CreateCommand("SELECT max(version) FROM server_schema");
            Assert.Equal(1, await version.ExecuteScalarAsync());
            Assert.Equal(before, (byte[])(await payload.ExecuteScalarAsync())!);
            await using var repair = source.CreateCommand("ALTER TABLE server_schema DROP CONSTRAINT no_two");
            await repair.ExecuteNonQueryAsync();
            await store.InitializeAsync(CancellationToken.None);
            Assert.Equal(3, await version.ExecuteScalarAsync());
            Assert.Equal(originalReceipt, await store.ExecuteAsync(id, command, CancellationToken.None));
            Assert.Equal(before, (byte[])(await payload.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var cleanup = admin.CreateCommand("DROP SCHEMA " + schema + " CASCADE");
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
