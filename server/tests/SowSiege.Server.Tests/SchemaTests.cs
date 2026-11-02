using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class SchemaTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentStartupIsSerializedAndFutureSchemaIsRejected()
    {
        string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.");
        string schema = "stage2_" + Guid.NewGuid().ToString("N");
        await using var admin = NpgsqlDataSource.Create(connection);
        await using (var create = admin.CreateCommand("CREATE SCHEMA " + schema))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema };
            await using var isolated = NpgsqlDataSource.Create(settings.ConnectionString);
            var application = new EstateApplication(EstateContent.Load(ContentEstateTests.Root()));
            var first = new AccountStore(isolated, application, TimeProvider.System);
            var second = new AccountStore(isolated, application, TimeProvider.System);
            await Task.WhenAll(first.InitializeAsync(CancellationToken.None), second.InitializeAsync(CancellationToken.None));
            await using var future = isolated.CreateCommand("INSERT INTO server_schema VALUES(4)");
            await future.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => first.InitializeAsync(CancellationToken.None));
            await using var version = isolated.CreateCommand("SELECT max(version) FROM server_schema");
            Assert.Equal(4, await version.ExecuteScalarAsync());
        }
        finally
        {
            // This random test-owned schema contains no other test or account data.
            await using var cleanup = admin.CreateCommand("DROP SCHEMA " + schema + " CASCADE");
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
