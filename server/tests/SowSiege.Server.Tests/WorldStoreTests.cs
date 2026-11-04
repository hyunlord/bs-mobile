using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

/// <summary>Each test owns one PostgreSQL schema; command and concurrency deadlines expose lock cycles.</summary>
internal sealed class WorldTestDatabase(NpgsqlDataSource source, NpgsqlDataSource admin, string schema, string connectionString) : IAsyncDisposable
{
    public NpgsqlDataSource Source => source;
    public string ConnectionString => connectionString;

    public static async Task<WorldTestDatabase> CreateAsync(int? maximumPoolSize = null, string? applicationName = null)
    {
        string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required; these tests never substitute an in-memory store.");
        string schema = "world_test_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(connection);
        settings.ApplicationName = (applicationName ?? schema) + "-admin";
        settings.MaxPoolSize = 1;
        var admin = NpgsqlDataSource.Create(settings.ConnectionString);
        bool schemaCreated = false;
        try
        {
            await using var create = admin.CreateCommand("CREATE SCHEMA " + schema);
            await create.ExecuteNonQueryAsync();
            schemaCreated = true;
            settings.SearchPath = schema;
            settings.CommandTimeout = 10;
            // Independent parallel fixtures share PostgreSQL's original 100-connection budget.
            // Keep concurrent requests inside each fixture, but bound its retained idle pool too.
            settings.MaxPoolSize = maximumPoolSize ?? 4;
            settings.ApplicationName = applicationName ?? schema;
            string isolated = settings.ConnectionString;
            return new(NpgsqlDataSource.Create(isolated), admin, schema, isolated);
        }
        catch
        {
            try
            {
                if (schemaCreated)
                {
                    await using var drop = admin.CreateCommand("DROP SCHEMA " + schema + " CASCADE");
                    await drop.ExecuteNonQueryAsync();
                }
            }
            finally { await admin.DisposeAsync(); }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await source.DisposeAsync();
            // This exact randomly named schema is the sole resource owned by this fixture.
            await using var drop = admin.CreateCommand("DROP SCHEMA " + schema + " CASCADE");
            await drop.ExecuteNonQueryAsync();
        }
        finally { await admin.DisposeAsync(); }
    }
}

public sealed class WorldStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);
    private const long NowMs = 1_793_520_000_000;

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(NowMs);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Setup(WorldTestDatabase Database, WorldMapContent Content, AccountStore Accounts,
        WorldStore World, AllianceStore Alliances, AuthStore Auth, ManualClock Clock) : IAsyncDisposable
    {
        public NpgsqlDataSource Source => Database.Source;
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    internal static byte[] MapBytes(int columns = 4, int rows = 4) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        id = "slg:world_map_test", kind = "slg-world-map", schemaVersion = 1,
        status = "hypothesis", numericStatus = "[가설]", sources = new[] { "docs/design/gdd/18-slg-world-map-proposal.md" },
        worldId = "slg:test_world", width = 8, height = 8, chunkSize = 4, registeredAccountTarget = columns * rows,
        defaultTerrain = "plain", terrainPatches = Array.Empty<object>(),
        startRegion = new { x = 0, y = 0, width = columns, height = rows }, startAnchor = new { x = 0, y = 0 },
        initialTargets = new[] { new { targetId = "slg:test_grain", kind = "resource", contentId = "meta:grain", x = 7, y = 7, generation = 1 } },
        notes = "Small connected map used only by PostgreSQL boundary tests."
    });

    private static async Task<Setup> CreateAsync(int columns = 4, int rows = 4)
    {
        var database = await WorldTestDatabase.CreateAsync();
        var content = WorldMapContent.Parse(MapBytes(columns, rows));
        var world = new WorldStore(database.Source, content);
        var clock = new ManualClock();
        var accounts = new AccountStore(database.Source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock, world: world);
        await accounts.InitializeAsync(None);
        var alliances = new AllianceStore(database.Source, AllianceContent.Load(ContentEstateTests.Root()), clock);
        await alliances.InitializeAsync(None);
        var auth = new AuthStore(database.Source, accounts,
            new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromMinutes(1), AuthTests.ShippedLimits(), true,
                Convert.FromHexString(RecruitTests.TestKeyHex)), clock);
        await auth.InitializeAsync(None);
        await world.InitializeAsync(None);
        return new(database, content, accounts, world, alliances, auth, clock);
    }

    private static async Task<Guid> AccountAsync(AccountStore accounts)
    {
        var created = await accounts.CreateAsync(None);
        return (await accounts.AuthenticateAsync(created.Token, None))!.Value;
    }

    private static async Task<long> CountAsync(NpgsqlDataSource source, string table)
    {
        await using var query = source.CreateCommand("SELECT count(*) FROM " + table);
        return (long)(await query.ExecuteScalarAsync())!;
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentNewAccountsReceiveDistinctConnectedCitiesInOneTransaction()
    {
        await using var setup = await CreateAsync();
        Guid[] accounts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => AccountAsync(setup.Accounts))).WaitAsync(Deadline);
        var cities = await Task.WhenAll(accounts.Select(id => setup.World.FindCityAsync(id, None)));
        Assert.All(cities, city => Assert.NotNull(city));
        Assert.Equal(accounts.Length, cities.Select(c => c!.TargetId).Distinct().Count());
        Assert.Equal(accounts.Length, cities.Select(c => (c!.X, c.Y)).Distinct().Count());
        foreach (var city in cities)
        {
            Assert.Equal("city", city!.Kind);
            Assert.Equal(1L, city.Generation);
            Assert.Contains(new WorldCoordinate(city.X, city.Y), setup.Content.CitySlots);
            Assert.NotNull(setup.Content.ShortestPath(setup.Content.StartAnchor, new(city.X, city.Y)));
        }
        Assert.Equal(12L, await CountAsync(setup.Source, "accounts"));
        Assert.Equal(12L, await CountAsync(setup.Source, "sessions"));
        Assert.Equal(13L, await CountAsync(setup.Source, "world_targets"));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RepeatedLegacyAllocationDoesNotRewriteAccountPayloadVersionOrReceipts()
    {
        await using var setup = await CreateAsync();
        var legacy = new AccountStore(setup.Source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), new ManualClock());
        Guid account = await AccountAsync(legacy);
        Assert.Null(await setup.World.FindCityAsync(account, None));
        await using var payload = setup.Source.CreateCommand("SELECT payload FROM accounts WHERE id=$1");
        payload.Parameters.AddWithValue(account);
        byte[] before = (byte[])(await payload.ExecuteScalarAsync())!;
        var cities = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => setup.World.EnsureCityAsync(account, NowMs + i, None))).WaitAsync(Deadline);
        Assert.Single(cities.Select(c => c.TargetId).Distinct());
        Assert.Equal(before, (byte[])(await payload.ExecuteScalarAsync())!);
        await using var version = setup.Source.CreateCommand("SELECT version FROM accounts WHERE id=$1");
        version.Parameters.AddWithValue(account);
        Assert.Equal(0L, await version.ExecuteScalarAsync());
        Assert.Equal(0L, await CountAsync(setup.Source, "commands"));
        Assert.Equal(2L, await CountAsync(setup.Source, "world_targets"));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task FullWorldRollsBackAccountAndSessionButExistingCityRemainsIdempotent()
    {
        await using var setup = await CreateAsync(2, 1);
        Guid first = await AccountAsync(setup.Accounts);
        await AccountAsync(setup.Accounts);
        var city = await setup.World.FindCityAsync(first, None);
        await Assert.ThrowsAsync<WorldCapacityException>(() => setup.Accounts.CreateAsync(None));
        Assert.Equal(2L, await CountAsync(setup.Source, "accounts"));
        Assert.Equal(2L, await CountAsync(setup.Source, "sessions"));
        Assert.Equal(3L, await CountAsync(setup.Source, "world_targets"));
        Assert.Equal(city, await setup.World.EnsureCityAsync(first, NowMs + 1000, None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task FailedCityInsertRollsBackAccountSessionAndAllocationClock()
    {
        await using var setup = await CreateAsync();
        await using var clock = setup.Source.CreateCommand("SELECT utc_highwater FROM world_instances WHERE world_id=$1");
        clock.Parameters.AddWithValue(setup.Content.WorldId);
        object? before = await clock.ExecuteScalarAsync();
        await using var failpoint = setup.Source.CreateCommand("""
            CREATE FUNCTION reject_test_city() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.kind='city' THEN RAISE EXCEPTION 'world-test-insert-failure'; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER reject_test_city BEFORE INSERT ON world_targets FOR EACH ROW EXECUTE FUNCTION reject_test_city();
            """);
        await failpoint.ExecuteNonQueryAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => setup.Accounts.CreateAsync(None));
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal(0L, await CountAsync(setup.Source, "accounts"));
        Assert.Equal(0L, await CountAsync(setup.Source, "sessions"));
        Assert.Equal(1L, await CountAsync(setup.Source, "world_targets"));
        Assert.Equal(before, await clock.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RestartPreservesCityTargetIdentityAndDoesNotReseedOccupiedLocations()
    {
        await using var setup = await CreateAsync();
        Guid account = await AccountAsync(setup.Accounts);
        var before = await setup.World.FindCityAsync(account, None);
        var restarted = new WorldStore(setup.Source, WorldMapContent.Parse(MapBytes()));
        await restarted.InitializeAsync(None);
        await restarted.InitializeAsync(None);
        Assert.Equal(before, await restarted.FindCityAsync(account, None));
        Assert.Equal(before, await restarted.EnsureCityAsync(account, NowMs - 60_000, None));
        await using var clock = setup.Source.CreateCommand("SELECT utc_highwater FROM world_instances WHERE world_id=$1");
        clock.Parameters.AddWithValue(setup.Content.WorldId);
        Assert.Equal(NowMs, await clock.ExecuteScalarAsync());
        Assert.Equal(2L, await CountAsync(setup.Source, "world_targets"));
        var chunk = await restarted.ChunkAsync(1, 1, None);
        var target = Assert.Single(chunk.Targets);
        Assert.Equal("slg:test_grain", target.TargetId);
        Assert.Equal(1L, target.Generation);
        Assert.Equal(16, chunk.Tiles.Length);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ChangedContentFailsClosedWithoutRewritingPersistedMap()
    {
        await using var setup = await CreateAsync();
        Guid account = await AccountAsync(setup.Accounts);
        var city = await setup.World.FindCityAsync(account, None);
        byte[] changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(MapBytes()) + "\n");
        var replacement = new WorldStore(setup.Source, WorldMapContent.Parse(changed));
        await Assert.ThrowsAsync<InvalidDataException>(() => replacement.InitializeAsync(None));
        Assert.Equal(city, await setup.World.FindCityAsync(account, None));
        await using var hash = setup.Source.CreateCommand("SELECT content_hash FROM world_instances WHERE world_id=$1");
        hash.Parameters.AddWithValue(setup.Content.WorldId);
        Assert.Equal(setup.Content.Hash, await hash.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task FutureWorldSchemaIsNotDowngradedOrPartiallyReinitialised()
    {
        await using var setup = await CreateAsync();
        Guid account = await AccountAsync(setup.Accounts);
        var city = await setup.World.FindCityAsync(account, None);
        await using var future = setup.Source.CreateCommand("INSERT INTO world_schema VALUES(2)");
        await future.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new WorldStore(setup.Source, setup.Content).InitializeAsync(None));
        await using var version = setup.Source.CreateCommand("SELECT max(version) FROM world_schema");
        Assert.Equal(2, await version.ExecuteScalarAsync());
        Assert.Equal(city, await setup.World.FindCityAsync(account, None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DatabaseRejectsTwoInteractionTargetsAtTheSameTile()
    {
        await using var setup = await CreateAsync();
        Guid first = await AccountAsync(setup.Accounts);
        Guid second = await AccountAsync(setup.Accounts);
        var occupied = (await setup.World.FindCityAsync(first, None))!;
        await using var collide = setup.Source.CreateCommand("UPDATE world_targets SET x=$1,y=$2 WHERE account_id=$3");
        collide.Parameters.AddWithValue(occupied.X); collide.Parameters.AddWithValue(occupied.Y); collide.Parameters.AddWithValue(second);
        var error = await Assert.ThrowsAsync<PostgresException>(() => collide.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal(3L, await CountAsync(setup.Source, "world_targets"));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AllianceMutationAndRepeatedWorldAllocationFinishWithoutALockCycle()
    {
        await using var setup = await CreateAsync();
        Guid account = await AccountAsync(setup.Accounts);
        var create = new AccountCommand(Guid.NewGuid(), 0, AllianceStore.Domain, "create",
            JsonSerializer.SerializeToElement(new { name = "World Guard", mode = "open" }));
        var alliance = setup.Alliances.ExecuteAsync(account, create, None);
        var allocations = Task.WhenAll(Enumerable.Range(0, 16).Select(_ => setup.World.EnsureCityAsync(account, NowMs, None)));
        await Task.WhenAll(alliance, allocations).WaitAsync(Deadline);
        var reply = JsonSerializer.Deserialize<AllianceReply>(await alliance, AccountStore.Json)!;
        Assert.True(reply.Done);
        Assert.Single((await allocations).Select(c => c.TargetId).Distinct());
        var city = (await setup.World.FindCityAsync(account, None))!;
        var chunk = await setup.World.ChunkAsync(city.X / setup.Content.ChunkSize, city.Y / setup.Content.ChunkSize, None);
        Assert.Equal("World Guard", Assert.Single(chunk.Targets, t => t.AccountId == account).AllianceName);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DeletedAccountsDisappearFromPublicAoiWithoutReassigningTheirReservedTile()
    {
        await using var setup = await CreateAsync();
        Guid account = await AccountAsync(setup.Accounts);
        var old = (await setup.World.FindCityAsync(account, None))!;
        await setup.Auth.DeleteAccountAsync(account, None);
        var chunk = await setup.World.ChunkAsync(old.X / setup.Content.ChunkSize, old.Y / setup.Content.ChunkSize, None);
        Assert.DoesNotContain(chunk.Targets, t => t.AccountId == account);
        Guid replacement = await AccountAsync(setup.Accounts);
        var next = (await setup.World.FindCityAsync(replacement, None))!;
        Assert.NotEqual((old.X, old.Y), (next.X, next.Y));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task UnknownAndDeletedAccountsCannotAllocateAndRejectionRollsBackTheClock()
    {
        await using var setup = await CreateAsync();
        Guid account = await AccountAsync(setup.Accounts);
        await setup.Auth.DeleteAccountAsync(account, None);
        var deleted = await Assert.ThrowsAsync<CommandConflictException>(() => setup.World.EnsureCityAsync(account, NowMs + 60_000, None));
        Assert.Equal("account-deleted", deleted.Message);
        var missing = await Assert.ThrowsAsync<CommandConflictException>(() => setup.World.EnsureCityAsync(Guid.NewGuid(), NowMs + 120_000, None));
        Assert.Equal("unknown-account", missing.Message);
        Assert.Equal(2L, await CountAsync(setup.Source, "world_targets"));
        await using var clock = setup.Source.CreateCommand("SELECT utc_highwater FROM world_instances WHERE world_id=$1");
        clock.Parameters.AddWithValue(setup.Content.WorldId);
        Assert.Equal(NowMs, await clock.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task PermanentPurgeReleasesCityOnlyAtGraceDeadlineAndCanReuseTheSlot()
    {
        await using var setup = await CreateAsync(1, 1);
        Guid account = await AccountAsync(setup.Accounts);
        var city = (await setup.World.FindCityAsync(account, None))!;
        var deadline = await setup.Auth.DeleteAccountAsync(account, None);
        setup.Clock.Now = deadline.AddMilliseconds(-1);
        Assert.Equal(0, await setup.Auth.PurgeExpiredAsync(10, None));
        Assert.Equal(city, await setup.World.FindCityAsync(account, None));
        await Assert.ThrowsAsync<WorldCapacityException>(() => setup.Accounts.CreateAsync(None));

        setup.Clock.Now = deadline;
        Assert.Equal(1, await setup.Auth.PurgeExpiredAsync(10, None));
        Assert.Null(await setup.World.FindCityAsync(account, None));
        Assert.Equal(0L, await CountAsync(setup.Source, "accounts"));
        Assert.Equal(1L, await CountAsync(setup.Source, "world_targets"));
        Assert.Equal(0, await setup.Auth.PurgeExpiredAsync(10, None));
        Guid replacement = await AccountAsync(setup.Accounts);
        var next = (await setup.World.FindCityAsync(replacement, None))!;
        Assert.Equal((city.X, city.Y), (next.X, next.Y));
        Assert.NotEqual(city.TargetId, next.TargetId);
        Assert.Equal(1L, next.Generation);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DeviceRestorePreservesCityIdentityAndPreventsLaterPurge()
    {
        await using var setup = await CreateAsync(1, 1);
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), "10.0.0.1", None);
        var city = (await setup.World.FindCityAsync(guest.AccountId, None))!;
        var deadline = await setup.Auth.DeleteAccountAsync(guest.AccountId, None);
        setup.Clock.Now = deadline.AddMilliseconds(-1);
        var challenge = await setup.Auth.ChallengeAsync(guest.KeyId, "10.0.0.1", None);
        var signature = device.SignData(AuthStore.SignedBytes(guest.KeyId, challenge.Nonce), HashAlgorithmName.SHA256);
        var restored = await setup.Auth.SignInAsync(guest.KeyId, challenge.Nonce, signature, None);
        Assert.True(restored.Restored);
        Assert.Equal(city, await setup.World.EnsureCityAsync(guest.AccountId, setup.Clock.Now.ToUnixTimeMilliseconds(), None));
        setup.Clock.Now = deadline.AddDays(1);
        Assert.Equal(0, await setup.Auth.PurgeExpiredAsync(10, None));
        var chunk = await setup.World.ChunkAsync(city.X / setup.Content.ChunkSize, city.Y / setup.Content.ChunkSize, None);
        Assert.Equal(city, Assert.Single(chunk.Targets, target => target.AccountId == guest.AccountId));
    }
}
