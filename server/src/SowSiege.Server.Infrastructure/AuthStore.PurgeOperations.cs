using Npgsql;

namespace SowSiege.Server.Infrastructure;

public sealed record PurgeOperationsView(long GracePending, long DuePending, long RetryWaiting, long RetryReady,
    long Succeeded, long Failed, long Recovered, long RetryFinished, DateTimeOffset? LastSucceededAt);

public sealed partial class AuthStore
{
    // #684: apply after auth schema 3; no account-identifying operational history survives restoration or purge.
    private const string PurgeOperationsSql = """
        CREATE TABLE auth_purge_retries(
          account_id uuid PRIMARY KEY REFERENCES auth_deleted_accounts(account_id) ON DELETE CASCADE,
          deletion_started_at timestamptz NOT NULL, attempts bigint NOT NULL CHECK(attempts>0),
          last_failed_at timestamptz NOT NULL, next_attempt_at timestamptz NOT NULL,
          failure_code text NOT NULL CHECK(failure_code IN ('storage','unexpected')));
        CREATE INDEX auth_purge_retries_ready ON auth_purge_retries(next_attempt_at);
        CREATE TABLE auth_purge_stats(
          id boolean PRIMARY KEY CHECK(id), succeeded bigint NOT NULL DEFAULT 0,
          failed bigint NOT NULL DEFAULT 0, recovered bigint NOT NULL DEFAULT 0, retry_finished bigint NOT NULL DEFAULT 0,
          last_succeeded_at timestamptz);
        INSERT INTO auth_purge_stats(id) VALUES(true);
        INSERT INTO auth_schema VALUES(4);
        """;

    public sealed record PurgeCandidate(Guid AccountId, DateTimeOffset DeletedAt);

    // Cursor is the last SELECTED candidate, including locked/failed/skipped candidates; reset only after reaching the end of the queue.
    internal async Task<PurgeCandidate[]> ReadPurgeCandidatesAsync(int batch, PurgeCandidate? after, CancellationToken ct)
    {
        if (batch < 1) { throw new ArgumentOutOfRangeException(nameof(batch)); }
        var now = time.GetUtcNow();
        await using var query = dataSource.CreateCommand("""
            SELECT d.account_id,d.deleted_at FROM auth_deleted_accounts d
            LEFT JOIN auth_purge_retries r ON r.account_id=d.account_id
            WHERE d.deleted_at<=$1 AND (r.next_attempt_at IS NULL OR r.next_attempt_at<=$2)
              AND ($3::timestamptz IS NULL OR (d.deleted_at,d.account_id)>($3,$4::uuid))
            ORDER BY d.deleted_at,d.account_id LIMIT $5
            """);
        query.Parameters.AddWithValue(now.Subtract(options.DeletionGrace)); query.Parameters.AddWithValue(now);
        query.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)after?.DeletedAt ?? DBNull.Value);
        query.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)after?.AccountId ?? DBNull.Value);
        query.Parameters.AddWithValue(batch);
        var result = new List<PurgeCandidate>();
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { result.Add(new(reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1))); }
        return result.ToArray();
    }

    // Call only after the failed purge transaction and connection have been disposed. Cancellation is never a failure.
    internal async Task RecordPurgeFailureAsync(PurgeCandidate candidate, Exception failure, bool wasRetry, TimeSpan retryDelay, CancellationToken ct)
    {
        if (failure is OperationCanceledException) { return; }
        ct.ThrowIfCancellationRequested();
        if (retryDelay <= TimeSpan.Zero || retryDelay > TimeSpan.FromDays(1)) { throw new ArgumentOutOfRangeException(nameof(retryDelay)); }
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var locked = new NpgsqlCommand("SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE", connection, transaction))
        {
            locked.Parameters.AddWithValue(candidate.AccountId);
            if (await locked.ExecuteScalarAsync(ct) is null) { return; }
        }
        // wasRetry was captured under the account lock at actual attempt time, never inferred from later bookkeeping.
        var now = time.GetUtcNow();
        // Deletion generation check prevents a failure from the old request contaminating a later request.
        int written = await Run(connection, transaction, """
            INSERT INTO auth_purge_retries(account_id,deletion_started_at,attempts,last_failed_at,next_attempt_at,failure_code)
              SELECT account_id,deleted_at,1,$3,$4,$5 FROM auth_deleted_accounts
              WHERE account_id=$1 AND deleted_at=$2
            ON CONFLICT(account_id) DO UPDATE SET attempts=auth_purge_retries.attempts+1,
              last_failed_at=EXCLUDED.last_failed_at,next_attempt_at=EXCLUDED.next_attempt_at,failure_code=EXCLUDED.failure_code
            """, ct, candidate.AccountId, candidate.DeletedAt, now, now.Add(retryDelay), failure is NpgsqlException ? "storage" : "unexpected");
        if (written != 0)
        {
            // Singleton is acquired last; never acquire any other row lock after this update.
            await Run(connection, transaction, "UPDATE auth_purge_stats SET failed=failed+1,retry_finished=retry_finished+$1 WHERE id", ct, wasRetry ? 1L : 0L);
        }
        await transaction.CommitAsync(ct);
    }

    // Caller captures wasRetry under its account lock, before deleting auth_deleted_accounts (CASCADE removes retry UUID).
    // Call after all purge mutations, immediately before COMMIT. A rollback must also roll back this count.
    private Task RecordPurgeSuccessAsync(NpgsqlConnection c, NpgsqlTransaction t, bool wasRetry, CancellationToken ct) =>
        Run(c, t, "UPDATE auth_purge_stats SET succeeded=succeeded+1,recovered=recovered+$1,retry_finished=retry_finished+$1,last_succeeded_at=$2 WHERE id",
            ct, wasRetry ? 1L : 0L, time.GetUtcNow());

    public async Task<PurgeOperationsView> ReadPurgeOperationsAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var query = dataSource.CreateCommand("""
            SELECT count(*) FILTER(WHERE d.deleted_at>$1), count(*) FILTER(WHERE d.deleted_at<=$1),
              count(*) FILTER(WHERE d.deleted_at<=$1 AND r.next_attempt_at>$2),
              count(*) FILTER(WHERE d.deleted_at<=$1 AND r.next_attempt_at<=$2),
              s.succeeded,s.failed,s.recovered,s.retry_finished,s.last_succeeded_at
            FROM auth_purge_stats s LEFT JOIN auth_deleted_accounts d ON true
            LEFT JOIN auth_purge_retries r ON r.account_id=d.account_id
            WHERE s.id GROUP BY s.succeeded,s.failed,s.recovered,s.retry_finished,s.last_succeeded_at
            """);
        query.Parameters.AddWithValue(now.Subtract(options.DeletionGrace)); query.Parameters.AddWithValue(now);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) { throw new InvalidOperationException("Purge operations schema is not initialized."); }
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4),
            reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8));
    }
}
