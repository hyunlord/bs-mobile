using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class CommandReferenceIndexTests
{
    // Frozen v2 fixture; do not generate this from the current production schema at test runtime.
    private const string AuthV1 = """
        CREATE TABLE IF NOT EXISTS auth_schema(version integer PRIMARY KEY);
        INSERT INTO auth_schema VALUES (1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS auth_device_keys(
          key_id uuid PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id), public_key bytea NOT NULL,
          created_at timestamptz NOT NULL, revoked_at timestamptz);
        CREATE TABLE IF NOT EXISTS auth_challenges(
          nonce_hash text PRIMARY KEY, key_id uuid NOT NULL REFERENCES auth_device_keys(key_id), expires_at timestamptz NOT NULL, used boolean NOT NULL);
        CREATE TABLE IF NOT EXISTS auth_refresh_tokens(
          token_hash text PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id), key_id uuid NOT NULL REFERENCES auth_device_keys(key_id),
          family uuid NOT NULL, expires_at timestamptz NOT NULL, used_at timestamptz, revoked_at timestamptz);
        CREATE TABLE IF NOT EXISTS auth_deleted_accounts(account_id uuid PRIMARY KEY REFERENCES accounts(id), deleted_at timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS auth_rate(kind text NOT NULL, subject text NOT NULL, window_start bigint NOT NULL, count integer NOT NULL,
          PRIMARY KEY(kind,subject,window_start));
        CREATE INDEX IF NOT EXISTS auth_challenges_expires ON auth_challenges(expires_at);
        CREATE INDEX IF NOT EXISTS auth_refresh_expires ON auth_refresh_tokens(expires_at);
        """;
    private const string AuthV2 = """
        CREATE TABLE IF NOT EXISTS auth_social_challenges(
          id uuid PRIMARY KEY, nonce_hash text NOT NULL, provider text NOT NULL, client_id text NOT NULL, purpose text NOT NULL,
          account_id uuid REFERENCES accounts(id), public_key bytea, expires_at timestamptz NOT NULL, used boolean NOT NULL DEFAULT false);
        CREATE INDEX IF NOT EXISTS auth_social_challenges_expiry ON auth_social_challenges(expires_at);
        CREATE TABLE IF NOT EXISTS auth_social_identities(
          provider text NOT NULL, subject text NOT NULL, account_id uuid NOT NULL REFERENCES accounts(id),
          PRIMARY KEY(provider,subject));
        CREATE INDEX IF NOT EXISTS auth_social_identity_account ON auth_social_identities(account_id);
        CREATE TABLE IF NOT EXISTS auth_social_credentials(
          id uuid PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id), subject_ref text NOT NULL,
          client_id text NOT NULL, credential bytea NOT NULL);
        CREATE INDEX IF NOT EXISTS auth_social_credential_account ON auth_social_credentials(account_id);
        CREATE TABLE IF NOT EXISTS auth_apple_revocations(
          id uuid PRIMARY KEY, subject_ref text NOT NULL, client_id text NOT NULL, credential bytea NOT NULL,
          retry_at timestamptz NOT NULL, attempts integer NOT NULL DEFAULT 0);
        CREATE INDEX IF NOT EXISTS auth_apple_revocations_due ON auth_apple_revocations(retry_at);
        CREATE INDEX IF NOT EXISTS auth_apple_revocations_subject ON auth_apple_revocations(subject_ref);
        INSERT INTO auth_schema VALUES (2) ON CONFLICT DO NOTHING;
        """;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Fixture(NpgsqlDataSource Source, AccountStore Accounts, AuthStore Auth, Clock Clock) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }

    private static async Task<Fixture> CreateAsync(bool legacy = false)
    {
        string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES") ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES required.");
        string schema = "command_refs_" + Guid.NewGuid().ToString("N");
        await using (var admin = NpgsqlDataSource.Create(connection))
        {
            await using var create = admin.CreateCommand("CREATE SCHEMA " + schema);
            await create.ExecuteNonQueryAsync();
        }
        var source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString);
        var clock = new Clock();
        var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock);
        await accounts.InitializeAsync(CancellationToken.None);
        var auth = new AuthStore(source, accounts, new(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromMinutes(1), AuthTests.ShippedLimits(), true,
            Convert.FromHexString(RecruitTests.TestKeyHex)), clock);
        if (legacy)
        {
            await using var initialize = source.CreateCommand(AuthV1 + AuthV2);
            await initialize.ExecuteNonQueryAsync();
        }
        else { await auth.InitializeAsync(CancellationToken.None); }
        return new(source, accounts, auth, clock);
    }

    private static async Task<Guid> AccountAsync(Fixture fixture)
    {
        var account = await fixture.Accounts.CreateAsync(CancellationToken.None);
        return (await fixture.Accounts.AuthenticateAsync(account.Token, CancellationToken.None))!.Value;
    }

    private static async Task<Guid> InsertAsync(Fixture fixture, Guid owner, string response)
    {
        Guid id = Guid.NewGuid();
        await using var insert = fixture.Source.CreateCommand("INSERT INTO commands VALUES($1,$2,'fixture',$3)");
        insert.Parameters.AddWithValue(owner); insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(response);
        await insert.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<Guid[]> MatchesAsync(Fixture fixture, string id, bool indexed)
    {
        string predicate = indexed ? "auth_command_response_ids_v1(response) @> ARRAY[$1]::text[]" : "strpos(response,$1)>0";
        await using var query = fixture.Source.CreateCommand("SELECT command_id FROM commands WHERE " + predicate + " ORDER BY command_id");
        query.Parameters.AddWithValue(id);
        var results = new List<Guid>();
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync()) { results.Add(reader.GetGuid(0)); }
        return results.ToArray();
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ExtractedReferencesMatchLowercaseSubstringSemanticsIncludingOverlaps()
    {
        await using var fixture = await CreateAsync();
        Guid owner = await AccountAsync(fixture);
        const string target = "abcddcba-1234-5678-9abc-abcdef012345";
        const string first = "11111111-2222-3333-4444-555566667777";
        const string overlapping = "66667777-8888-9999-aaaa-bbbbbbbbbbbb";
        string[] matching = ["{\"" + target + "\":1}", "{\"value\":\"" + target + "\"}", "prefix" + target + "suffix",
            "not JSON: " + target, "한글🌾" + target + "끝", target + " / " + target];
        foreach (string response in matching) { await InsertAsync(fixture, owner, response); }
        Guid uppercase = await InsertAsync(fixture, owner, target.ToUpperInvariant());
        await InsertAsync(fixture, owner, "no identifiers");
        Guid overlap = await InsertAsync(fixture, owner, first + "-8888-9999-aaaa-bbbbbbbbbbbb");
        Assert.Equal(matching.Length, (await MatchesAsync(fixture, target, true)).Length);
        Assert.DoesNotContain(uppercase, await MatchesAsync(fixture, target, true));
        foreach (string id in new[] { target, first, overlapping, Guid.NewGuid().ToString("D") })
        {
            Assert.Equal(await MatchesAsync(fixture, id, false), await MatchesAsync(fixture, id, true));
        }
        Assert.Equal(new[] { overlap }, await MatchesAsync(fixture, overlapping, true));
        await using var extract = fixture.Source.CreateCommand("SELECT auth_command_response_ids_v1($1)");
        extract.Parameters.AddWithValue(first + "-8888-9999-aaaa-bbbbbbbbbbbb");
        string[] ids = (string[])(await extract.ExecuteScalarAsync())!;
        Assert.Contains(first, ids); Assert.Contains(overlapping, ids);
        await using var empty = fixture.Source.CreateCommand("SELECT auth_command_response_ids_v1('')");
        Assert.Empty((string[])(await empty.ExecuteScalarAsync())!);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task V2MigrationIndexesExistingRowsAndHasValidGinExpressionContract()
    {
        await using var fixture = await CreateAsync(legacy: true);
        Guid owner = await AccountAsync(fixture);
        string target = Guid.NewGuid().ToString("D");
        Guid existing = await InsertAsync(fixture, owner, "legacy " + target);
        await using (var absent = fixture.Source.CreateCommand("SELECT to_regprocedure('auth_command_response_ids_v1(text)')::text"))
        {
            Assert.Equal(DBNull.Value, await absent.ExecuteScalarAsync());
        }
        await fixture.Auth.InitializeAsync(CancellationToken.None);
        await fixture.Auth.InitializeAsync(CancellationToken.None);
        Assert.Equal(new[] { existing }, await MatchesAsync(fixture, target, true));
        await using (var version = fixture.Source.CreateCommand("SELECT array_agg(version ORDER BY version) FROM auth_schema"))
        {
            Assert.Equal(new[] { 1, 2, 3, 4 }, (int[])(await version.ExecuteScalarAsync())!);
        }
        await using var index = fixture.Source.CreateCommand("""
            SELECT i.indisvalid,i.indisready,am.amname,pg_get_expr(i.indexprs,i.indrelid)
            FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_am am ON am.oid=c.relam
            WHERE c.oid=to_regclass('commands_response_account_ids')
            """);
        await using (var reader = await index.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0)); Assert.True(reader.GetBoolean(1)); Assert.Equal("gin", reader.GetString(2));
            Assert.Contains("auth_command_response_ids_v1(response)", reader.GetString(3));
            Assert.False(await reader.ReadAsync());
        }
        await using var function = fixture.Source.CreateCommand("""
            SELECT p.provolatile::text,p.proisstrict,p.proparallel::text,l.lanname
            FROM pg_proc p JOIN pg_language l ON l.oid=p.prolang WHERE p.oid=to_regprocedure('auth_command_response_ids_v1(text)')
            """);
        await using var attributes = await function.ExecuteReaderAsync();
        Assert.True(await attributes.ReadAsync());
        Assert.Equal("i", attributes.GetString(0)); Assert.True(attributes.GetBoolean(1));
        Assert.Equal("s", attributes.GetString(2)); Assert.Equal("plpgsql", attributes.GetString(3));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task IndexTracksInsertedChangedAndRemovedResponses()
    {
        await using var fixture = await CreateAsync();
        Guid owner = await AccountAsync(fixture);
        string first = Guid.NewGuid().ToString("D"), second = Guid.NewGuid().ToString("D");
        Guid row = await InsertAsync(fixture, owner, first);
        Assert.Equal(new[] { row }, await MatchesAsync(fixture, first, true));
        await using (var update = fixture.Source.CreateCommand("UPDATE commands SET response=$2 WHERE command_id=$1"))
        {
            update.Parameters.AddWithValue(row); update.Parameters.AddWithValue(second); await update.ExecuteNonQueryAsync();
        }
        Assert.Empty(await MatchesAsync(fixture, first, true));
        Assert.Equal(new[] { row }, await MatchesAsync(fixture, second, true));
        await using (var remove = fixture.Source.CreateCommand("DELETE FROM commands WHERE command_id=$1"))
        {
            remove.Parameters.AddWithValue(row); await remove.ExecuteNonQueryAsync();
        }
        Assert.Empty(await MatchesAsync(fixture, second, true));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task IndexedPurgeRewritesOnlyOtherAccountsMatchingResponses()
    {
        await using var fixture = await CreateAsync();
        Guid deleted = await AccountAsync(fixture), retained = await AccountAsync(fixture);
        string id = deleted.ToString("D"), unrelated = "keep exact bytes 🌾 " + Guid.NewGuid().ToString("D");
        Guid ownReceipt = await InsertAsync(fixture, deleted, "own " + id);
        Guid repeated = await InsertAsync(fixture, retained, "before " + id + " twice " + id + " after");
        Guid unchanged = await InsertAsync(fixture, retained, unrelated);
        Guid upper = await InsertAsync(fixture, retained, id.ToUpperInvariant());
        await fixture.Auth.DeleteAccountAsync(deleted, CancellationToken.None);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(31);
        Assert.Equal(1, await fixture.Auth.PurgeExpiredAsync(100, CancellationToken.None));
        await using var read = fixture.Source.CreateCommand("SELECT command_id,response FROM commands WHERE account_id=$1");
        read.Parameters.AddWithValue(retained);
        var rows = new Dictionary<Guid, string>();
        await using (var reader = await read.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) { rows.Add(reader.GetGuid(0), reader.GetString(1)); }
        }
        Assert.Equal(3, rows.Count); Assert.Equal(unrelated, rows[unchanged]);
        Assert.Equal(id.ToUpperInvariant().Replace(id, fixture.Auth.Pseudonym(deleted), StringComparison.Ordinal), rows[upper]);
        Assert.DoesNotContain(id, rows[repeated]); Assert.StartsWith("before ", rows[repeated]); Assert.EndsWith(" after", rows[repeated]);
        Assert.Empty(await MatchesAsync(fixture, id, true));
        await using var own = fixture.Source.CreateCommand("SELECT count(*) FROM commands WHERE command_id=$1");
        own.Parameters.AddWithValue(ownReceipt); Assert.Equal(0L, await own.ExecuteScalarAsync());
    }
}
