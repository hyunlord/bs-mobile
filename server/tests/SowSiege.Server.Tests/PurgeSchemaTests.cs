using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class PurgeSchemaTests
{
    // Frozen auth v3 DDL from 28e9e4ea; no current production schema reflection at runtime.
    private const string AuthV3 = """
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
        CREATE FUNCTION auth_command_response_ids_v1(response text) RETURNS text[]
        LANGUAGE plpgsql IMMUTABLE STRICT PARALLEL SAFE
        SET search_path = pg_catalog
        AS $function$
        DECLARE
          ids text[] := ARRAY[]::text[];
          search_from integer := 1;
          hit integer;
        BEGIN
          LOOP
            hit := regexp_instr(response COLLATE "C",
              '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', search_from);
            EXIT WHEN hit = 0;
            ids := array_append(ids, substr(response, hit, 36));
            -- Advance one character, not one match: UUID substrings can overlap.
            search_from := hit + 1;
          END LOOP;
          RETURN ids;
        END;
        $function$;
        CREATE INDEX commands_response_account_ids ON commands USING gin (auth_command_response_ids_v1(response));
        INSERT INTO auth_schema VALUES (3);
        """;

    private sealed record Fixture(NpgsqlDataSource Source, AccountStore Accounts, AuthStore Auth) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }

    private static async Task<Fixture> CreateAsync()
    {
        string cs = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES") ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES required.");
        string schema = "purge_schema_" + Guid.NewGuid().ToString("N");
        await using (var admin = NpgsqlDataSource.Create(cs))
        await using (var create = admin.CreateCommand("CREATE SCHEMA " + schema)) { await create.ExecuteNonQueryAsync(); }
        var source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs) { SearchPath = schema }.ConnectionString);
        var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), TimeProvider.System);
        await accounts.InitializeAsync(CancellationToken.None);
        await using (var legacy = source.CreateCommand(AuthV3)) { await legacy.ExecuteNonQueryAsync(); }
        var auth = new AuthStore(source, accounts, new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromMinutes(1),
            AuthTests.ShippedLimits(), true, Convert.FromHexString(RecruitTests.TestKeyHex)), TimeProvider.System);
        return new(source, accounts, auth);
    }

    private static async Task<Guid> AccountAsync(Fixture f)
    {
        var account = await f.Accounts.CreateAsync(CancellationToken.None);
        return (await f.Accounts.AuthenticateAsync(account.Token, CancellationToken.None))!.Value;
    }

    private static async Task<object?> ScalarAsync(Fixture f, string sql)
    {
        await using var query = f.Source.CreateCommand(sql);
        return await query.ExecuteScalarAsync();
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task FrozenV3UpgradePreservesRowsIndexAndFunctionAndRepeatedInitializationPreservesStats()
    {
        await using var f = await CreateAsync();
        Guid account = await AccountAsync(f), command = Guid.NewGuid();
        var deletedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        string response = "embedded:" + account.ToString("D") + ":suffix";
        await using (var insert = f.Source.CreateCommand("INSERT INTO auth_deleted_accounts VALUES($1,$2)"))
        { insert.Parameters.AddWithValue(account); insert.Parameters.AddWithValue(deletedAt); await insert.ExecuteNonQueryAsync(); }
        await using (var insert = f.Source.CreateCommand("INSERT INTO commands VALUES($1,$2,'fixture',$3)"))
        { insert.Parameters.AddWithValue(account); insert.Parameters.AddWithValue(command); insert.Parameters.AddWithValue(response); await insert.ExecuteNonQueryAsync(); }
        var indexOid = await ScalarAsync(f, "SELECT 'commands_response_account_ids'::regclass::oid::bigint");
        var functionOid = await ScalarAsync(f, "SELECT 'auth_command_response_ids_v1(text)'::regprocedure::oid::bigint");
        var functionBody = await ScalarAsync(f, "SELECT pg_get_functiondef('auth_command_response_ids_v1(text)'::regprocedure)");
        await f.Auth.InitializeAsync(CancellationToken.None);
        await using (var update = f.Source.CreateCommand("UPDATE auth_purge_stats SET succeeded=7,failed=5,recovered=2,retry_finished=3 WHERE id"))
        { await update.ExecuteNonQueryAsync(); }
        await f.Auth.InitializeAsync(CancellationToken.None);
        Assert.Equal(4, await ScalarAsync(f, "SELECT max(version) FROM auth_schema"));
        Assert.Equal(indexOid, await ScalarAsync(f, "SELECT 'commands_response_account_ids'::regclass::oid::bigint"));
        Assert.Equal(functionOid, await ScalarAsync(f, "SELECT 'auth_command_response_ids_v1(text)'::regprocedure::oid::bigint"));
        Assert.Equal(functionBody, await ScalarAsync(f, "SELECT pg_get_functiondef('auth_command_response_ids_v1(text)'::regprocedure)"));
        Assert.Equal(true, await ScalarAsync(f, "SELECT indisvalid FROM pg_index WHERE indexrelid='commands_response_account_ids'::regclass"));
        await using (var saved = f.Source.CreateCommand("SELECT response FROM commands WHERE command_id=$1 AND auth_command_response_ids_v1(response) @> ARRAY[$2]::text[]"))
        { saved.Parameters.AddWithValue(command); saved.Parameters.AddWithValue(account.ToString("D")); Assert.Equal(response, await saved.ExecuteScalarAsync()); }
        await using (var mark = f.Source.CreateCommand("SELECT deleted_at FROM auth_deleted_accounts WHERE account_id=$1"))
        {
            mark.Parameters.AddWithValue(account);
            await using var reader = await mark.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.Equal(deletedAt, reader.GetFieldValue<DateTimeOffset>(0));
        }
        var stats = await f.Auth.ReadPurgeOperationsAsync(CancellationToken.None);
        Assert.Equal((7L,5L,2L,3L), (stats.Succeeded,stats.Failed,stats.Recovered,stats.RetryFinished));
        Assert.Equal(1L, await ScalarAsync(f, "SELECT count(*) FROM auth_purge_stats"));
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task FutureVersionRejectsWithoutCreatingOperationsTablesOrChangingLegacyObjects()
    {
        await using var f = await CreateAsync();
        var functionBody = await ScalarAsync(f, "SELECT pg_get_functiondef('auth_command_response_ids_v1(text)'::regprocedure)");
        await using (var future = f.Source.CreateCommand("INSERT INTO auth_schema VALUES(99)")) { await future.ExecuteNonQueryAsync(); }
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Auth.InitializeAsync(CancellationToken.None));
        Assert.Equal(99, await ScalarAsync(f, "SELECT max(version) FROM auth_schema"));
        Assert.Equal(0L, await ScalarAsync(f, "SELECT count(*) FROM auth_schema WHERE version=4"));
        Assert.Equal(0L, await ScalarAsync(f, "SELECT count(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name IN ('auth_purge_retries','auth_purge_stats')"));
        Assert.Equal(functionBody, await ScalarAsync(f, "SELECT pg_get_functiondef('auth_command_response_ids_v1(text)'::regprocedure)"));
        Assert.Equal(true, await ScalarAsync(f, "SELECT indisvalid FROM pg_index WHERE indexrelid='commands_response_account_ids'::regclass"));
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task RetryForeignKeyCascadesWhenDeletionIsCancelledAndRejectsOrphans()
    {
        await using var f = await CreateAsync();
        await f.Auth.InitializeAsync(CancellationToken.None);
        Guid account = await AccountAsync(f);
        await using (var mark = f.Source.CreateCommand("INSERT INTO auth_deleted_accounts VALUES($1,now())"))
        { mark.Parameters.AddWithValue(account); await mark.ExecuteNonQueryAsync(); }
        await using (var retry = f.Source.CreateCommand("INSERT INTO auth_purge_retries SELECT account_id,deleted_at,1,now(),now(),'storage' FROM auth_deleted_accounts WHERE account_id=$1"))
        { retry.Parameters.AddWithValue(account); await retry.ExecuteNonQueryAsync(); }
        Assert.Equal(1L, await ScalarAsync(f, "SELECT count(*) FROM auth_purge_retries"));
        await using (var restore = f.Source.CreateCommand("DELETE FROM auth_deleted_accounts WHERE account_id=$1"))
        { restore.Parameters.AddWithValue(account); await restore.ExecuteNonQueryAsync(); }
        Assert.Equal(0L, await ScalarAsync(f, "SELECT count(*) FROM auth_purge_retries"));
        Assert.Equal(1L, await ScalarAsync(f, "SELECT count(*) FROM accounts"));
        await using var orphan = f.Source.CreateCommand("INSERT INTO auth_purge_retries VALUES($1,now(),1,now(),now(),'storage')");
        orphan.Parameters.AddWithValue(account);
        var error = await Assert.ThrowsAsync<PostgresException>(() => orphan.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }
}
