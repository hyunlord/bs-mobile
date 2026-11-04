using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Infrastructure;

public sealed record AllianceMemberView(Guid AccountId, string Role, long JoinedAtMs);
/// <summary>Author is null when the writer has left the alliance or deleted the account; the client shows "삭제된 사용자" (ADR0080 §5).</summary>
public sealed record AllianceMessageView(Guid MessageId, Guid? Author, string Text, long AtMs);
public sealed record AllianceHelpView(string JobKey, string NodeId, Guid Requester, long DurationMs, long GivenMs, int Grants, long CompletesAtMs, string Status);
public sealed record AllianceApplicationView(Guid AccountId, long CreatedAtMs, long ExpiresAtMs);
public sealed record AllianceInfo(Guid Id, string Name, string Mode, string Notice, string Status, Guid Leader, int MemberCount, int MemberLimit);
/// <summary>Applications is filled only for leaders and officers (the roles that may approve or reject).</summary>
public sealed record AllianceView(string Role, AllianceInfo? Alliance, AllianceMemberView[] Members, AllianceMessageView[] Chat, AllianceHelpView[] Help,
    Guid? PendingApplication, long? RestrictedUntilMs, Guid[] Blocked, AllianceApplicationView[] Applications);
public sealed record AllianceReply(bool Done, string Reason, long AccountVersion, AllianceView Alliance, Guid? CreatedId = null, Guid? ReportId = null, long? ShortenedMs = null);

/// <summary>
/// GDD19 alliance commands, routed from /v1/account/commands (domain "alliance"). Alliance state spans accounts, so it is not an
/// ADR0078 per-account handler; it shares the command envelope, receipts, account version and outbox with the account writer.
/// Lock order (deadlock free): the alliance row first (FOR UPDATE), then every involved account row in one statement
/// ordered by id (PostgreSQL uuid order). The account writer locks one account row and never an alliance row, so no cycle forms.
/// Membership grants no combat, production or research bonus: this store never writes accounts.payload or domain state.
/// </summary>
public sealed class AllianceStore(NpgsqlDataSource dataSource, AllianceContent content, TimeProvider time, WorldRecoveryGate? recoveryGate = null)
{
    public const string Domain = "alliance";
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal const string Sql = """
        CREATE TABLE IF NOT EXISTS alliance_schema(version integer PRIMARY KEY);
        INSERT INTO alliance_schema VALUES (1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS alliances(
          id uuid PRIMARY KEY, name text NOT NULL, name_key text NOT NULL, mode text NOT NULL CHECK (mode IN ('open','approval')),
          notice text NOT NULL, status text NOT NULL CHECK (status IN ('active','dissolved')), leader uuid NOT NULL,
          member_count integer NOT NULL CHECK (member_count >= 0), created_ms bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS alliance_members(
          account_id uuid PRIMARY KEY REFERENCES accounts(id), alliance_id uuid NOT NULL REFERENCES alliances(id),
          role text NOT NULL CHECK (role IN ('leader','officer','member')), joined_ms bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS alliance_applications(
          account_id uuid PRIMARY KEY REFERENCES accounts(id), alliance_id uuid NOT NULL REFERENCES alliances(id),
          created_ms bigint NOT NULL, expires_ms bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS alliance_restrictions(account_id uuid PRIMARY KEY REFERENCES accounts(id), until_ms bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS alliance_messages(
          message_id uuid PRIMARY KEY, alliance_id uuid NOT NULL REFERENCES alliances(id), author uuid NOT NULL REFERENCES accounts(id),
          body text NOT NULL, at_ms bigint NOT NULL, seq bigserial UNIQUE);
        CREATE TABLE IF NOT EXISTS alliance_reports(
          id uuid PRIMARY KEY, reporter uuid NOT NULL REFERENCES accounts(id), message_id uuid NOT NULL, alliance_id uuid NOT NULL,
          author uuid NOT NULL, body_snapshot text NOT NULL, message_at_ms bigint NOT NULL, reason text NOT NULL, note text NOT NULL,
          created_ms bigint NOT NULL, UNIQUE(reporter,message_id,reason));
        CREATE TABLE IF NOT EXISTS alliance_blocks(
          account_id uuid NOT NULL REFERENCES accounts(id), blocked uuid NOT NULL REFERENCES accounts(id), PRIMARY KEY(account_id,blocked));
        CREATE TABLE IF NOT EXISTS alliance_help_requests(
          requester uuid NOT NULL REFERENCES accounts(id), job_key text NOT NULL, alliance_id uuid NOT NULL REFERENCES alliances(id),
          node_id text NOT NULL, duration_ms bigint NOT NULL, completes_ms bigint NOT NULL, given_ms bigint NOT NULL, grants integer NOT NULL,
          status text NOT NULL CHECK (status IN ('open','closed')), PRIMARY KEY(requester,job_key));
        CREATE TABLE IF NOT EXISTS alliance_help_grants(
          requester uuid NOT NULL, job_key text NOT NULL, giver uuid NOT NULL REFERENCES accounts(id), shortened_ms bigint NOT NULL,
          at_ms bigint NOT NULL, PRIMARY KEY(requester,job_key,giver), FOREIGN KEY(requester,job_key) REFERENCES alliance_help_requests(requester,job_key));
        """;

    /// <summary>v2 (ADR0080 §5): a purged account's ID in retained chat and reports is replaced by a keyed pseudonym (*_ref). Run once.</summary>
    private const string MigrateV2 = """
        ALTER TABLE alliance_messages ALTER COLUMN author DROP NOT NULL, ADD COLUMN IF NOT EXISTS author_ref text;
        ALTER TABLE alliance_reports ALTER COLUMN reporter DROP NOT NULL, ALTER COLUMN author DROP NOT NULL,
          ADD COLUMN IF NOT EXISTS reporter_ref text, ADD COLUMN IF NOT EXISTS author_ref text;
        ALTER TABLE alliances ALTER COLUMN leader DROP NOT NULL;
        INSERT INTO alliance_schema VALUES (2) ON CONFLICT DO NOTHING;
        """;

    private sealed record NamePayload([property: JsonRequired] string Name, [property: JsonRequired] string Mode);
    private sealed record AlliancePayload([property: JsonRequired] Guid AllianceId);
    private sealed record AccountPayload([property: JsonRequired] Guid AccountId);
    private sealed record TextPayload([property: JsonRequired] string Text);
    private sealed record ChatPayload([property: JsonRequired] Guid MessageId, [property: JsonRequired] string Text);
    private sealed record ReportPayload([property: JsonRequired] Guid MessageId, [property: JsonRequired] string Reason, string? Note);
    private sealed record HelpRequestPayload([property: JsonRequired] string NodeId);
    private sealed record HelpGivePayload([property: JsonRequired] Guid Requester, [property: JsonRequired] string JobKey);

    private sealed record Membership(Guid AllianceId, string Role, long JoinedMs);

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await Run(connection, transaction, "SELECT pg_advisory_xact_lock(19019)", cancellation);
        await Run(connection, transaction, Sql, cancellation);
        await using var version = new NpgsqlCommand("SELECT max(version) FROM alliance_schema", connection, transaction);
        if (await version.ExecuteScalarAsync(cancellation) is not int current || current is not (1 or 2))
        {
            throw new InvalidDataException("Unsupported alliance schema version.");
        }
        if (current == 1)
        {
            await Run(connection, transaction, MigrateV2, cancellation);
        }
        await transaction.CommitAsync(cancellation);
    }

    /// <summary>Periodic retention (ADR0080 §5): report snapshots older than ReportRetentionDays are removed even when no new report arrives.</summary>
    public async Task<int> PruneReportsAsync(CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var prune = new NpgsqlCommand("DELETE FROM alliance_reports WHERE created_ms < $1", connection);
        prune.Parameters.AddWithValue(time.GetUtcNow().ToUnixTimeMilliseconds() - content.ReportRetentionDays * 86_400_000L);
        return await prune.ExecuteNonQueryAsync(cancellation);
    }

    public async Task<AllianceView> ViewAsync(Guid id, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var view = await ViewAsync(connection, transaction, id, Now(), cancellation);
        await transaction.CommitAsync(cancellation);
        return view;
    }

    public async Task<string> ExecuteAsync(Guid caller, AccountCommand command, CancellationToken cancellation)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return recoveryGate is null
                    ? await ExecuteOnceAsync(caller, command, cancellation)
                    : await recoveryGate.ExecuteAsync((connection, transaction, now, ct) =>
                        ExecuteInTransactionAsync(connection, transaction, caller, command, now, ct), cancellation);
            }
            catch (RetryException)
            {
                // The involved alliance changed between discovery and locking; rediscover in a new transaction.
                if (attempt >= 2)
                {
                    throw new CommandConflictException("state-changed");
                }
            }
        }
    }

    private sealed class RetryException : Exception;

    private async Task<string> ExecuteOnceAsync(Guid caller, AccountCommand command, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        string response = await ExecuteInTransactionAsync(connection, transaction, caller, command, Now(), cancellation);
        await transaction.CommitAsync(cancellation);
        return response;
    }

    private async Task<string> ExecuteInTransactionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid caller, AccountCommand command, long now, CancellationToken cancellation)
    {
        // 1) Discover the alliance and accounts this command touches, without locks.
        var (allianceId, others) = await DiscoverAsync(connection, transaction, caller, command, cancellation);
        // 2) The optional recovery barrier already owns the world lock; then lock alliance and account UUIDs.
        if (allianceId is { } locked)
        {
            await using var lockAlliance = new NpgsqlCommand("SELECT 1 FROM alliances WHERE id=$1 FOR UPDATE", connection, transaction);
            lockAlliance.Parameters.AddWithValue(locked);
            await lockAlliance.ExecuteScalarAsync(cancellation);
        }
        var accounts = others.Append(caller).Distinct().ToArray();
        long version = 0;
        await using (var lockAccounts = new NpgsqlCommand("SELECT id,version FROM accounts WHERE id = ANY($1) ORDER BY id FOR UPDATE", connection, transaction))
        {
            lockAccounts.Parameters.AddWithValue(accounts);
            await using var reader = await lockAccounts.ExecuteReaderAsync(cancellation);
            int found = 0;
            while (await reader.ReadAsync(cancellation))
            {
                found++;
                if (reader.GetGuid(0) == caller)
                {
                    version = reader.GetInt64(1);
                }
            }
            if (found != accounts.Length)
            {
                throw new CommandConflictException("unknown-account");
            }
        }
        // 3) Receipts and version, like the account writer.
        string payloadHash = Hash("alliance-v1:" + JsonSerializer.Serialize(command, AccountStore.Json));
        await using (var receipt = new NpgsqlCommand("SELECT payload_hash,response FROM commands WHERE account_id=$1 AND command_id=$2", connection, transaction))
        {
            receipt.Parameters.AddWithValue(caller); receipt.Parameters.AddWithValue(command.CommandId);
            await using var reader = await receipt.ExecuteReaderAsync(cancellation);
            if (await reader.ReadAsync(cancellation))
            {
                if (reader.GetString(0) != payloadHash)
                {
                    throw new CommandConflictException("command-id-reused");
                }
                return reader.GetString(1);
            }
        }
        if (command.ExpectedVersion != version)
        {
            throw new CommandConflictException("version-conflict");
        }
        // 4) Rediscover under locks; a changed alliance means another command won the race.
        var (recheck, _) = await DiscoverAsync(connection, transaction, caller, command, cancellation);
        if (recheck != allianceId)
        {
            throw new RetryException();
        }
        var (done, reason, created, reported, shortened) = await ApplyAsync(connection, transaction, caller, command, now, cancellation);
        long next = checked(version + 1);
        await Run(connection, transaction, "UPDATE accounts SET version=$2 WHERE id=$1", cancellation, caller, next);
        await Run(connection, transaction, "INSERT INTO outbox VALUES($1,$2,'alliance-updated')", cancellation, caller, next);
        var reply = new AllianceReply(done, reason, next, await ViewAsync(connection, transaction, caller, now, cancellation), created, reported, shortened);
        string response = JsonSerializer.Serialize(reply, AccountStore.Json);
        await Run(connection, transaction, "INSERT INTO commands VALUES($1,$2,$3,$4)", cancellation, caller, command.CommandId, payloadHash, response);
        return response;
    }

    private T Payload<T>(AccountCommand command) => command.Payload.Deserialize<T>(Strict) ?? throw new JsonException("Empty payload.");

    private async Task<(Guid? Alliance, Guid[] Others)> DiscoverAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid caller, AccountCommand command, CancellationToken cancellation)
    {
        var mine = await MembershipAsync(connection, transaction, caller, cancellation);
        switch (command.Operation)
        {
            case "create":
                return (null, []);
            case "apply":
                return (Payload<AlliancePayload>(command).AllianceId, []);
            case "approve":
            case "reject":
                return (mine?.AllianceId, [Payload<AccountPayload>(command).AccountId]);
            case "cancel-apply":
                return (await ScalarAsync<Guid?>(connection, transaction, "SELECT alliance_id FROM alliance_applications WHERE account_id=$1", cancellation, caller), []);
            case "give-help":
                var give = Payload<HelpGivePayload>(command);
                return (mine?.AllianceId, [give.Requester]);
            case "leave" or "notice" or "chat" or "report" or "request-help":
                return (mine?.AllianceId, []);
            case "block" or "unblock":
                return (null, [Payload<AccountPayload>(command).AccountId]);
            default:
                throw new CommandConflictException("unsupported-operation");
        }
    }

    private async Task<(bool, string, Guid?, Guid?, long?)> ApplyAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid caller, AccountCommand command, long now, CancellationToken ct)
    {
        var mine = await MembershipAsync(c, t, caller, ct);
        bool restricted = await ScalarAsync<long?>(c, t, "SELECT until_ms FROM alliance_restrictions WHERE account_id=$1", ct, caller) is { } until && until > now;
        switch (command.Operation)
        {
            case "create":
            {
                var p = Payload<NamePayload>(command);
                string? name = AllianceRules.Clean(p.Name, content.NameMinChars, content.NameMaxChars);
                if (mine is not null) { return (false, "already-member", null, null, null); }
                if (restricted) { return (false, "rejoin-restricted", null, null, null); }
                if (name is null) { return (false, "invalid-name", null, null, null); }
                if (p.Mode is not ("open" or "approval")) { return (false, "invalid-mode", null, null, null); }
                var id = command.CommandId; // the command id is the alliance id, so a resend cannot create a second alliance
                await Run(c, t, "INSERT INTO alliances VALUES($1,$2,$3,$4,'','active',$5,1,$6)", ct, id, name, AllianceRules.NameKey(name), p.Mode, caller, now);
                await Run(c, t, "INSERT INTO alliance_members VALUES($1,$2,'leader',$3)", ct, caller, id, now);
                await Run(c, t, "DELETE FROM alliance_applications WHERE account_id=$1", ct, caller);
                return (true, "created", id, null, null);
            }
            case "apply":
            {
                var target = Payload<AlliancePayload>(command).AllianceId;
                var alliance = await AllianceAsync(c, t, target, ct);
                if (mine is not null) { return (false, "already-member", null, null, null); }
                if (restricted) { return (false, "rejoin-restricted", null, null, null); }
                if (alliance is not { Status: "active" }) { return (false, "unknown-alliance", null, null, null); }
                if (alliance.Mode == "open")
                {
                    if (alliance.MemberCount >= content.MemberLimit) { return (false, "alliance-full", null, null, null); }
                    await JoinAsync(c, t, target, caller, now, ct);
                    return (true, "joined", null, null, null);
                }
                var pending = await ScalarAsync<long?>(c, t, "SELECT expires_ms FROM alliance_applications WHERE account_id=$1", ct, caller);
                if (pending is { } expires && expires > now) { return (false, "application-pending", null, null, null); }
                await Run(c, t, "INSERT INTO alliance_applications VALUES($1,$2,$3,$4) ON CONFLICT(account_id) DO UPDATE SET alliance_id=$2,created_ms=$3,expires_ms=$4", ct,
                    caller, target, now, now + content.ApplicationHours * 3_600_000L);
                return (true, "applied", null, null, null);
            }
            case "cancel-apply":
            {
                int rows = await Run(c, t, "DELETE FROM alliance_applications WHERE account_id=$1", ct, caller);
                return rows == 0 ? (false, "no-application", null, null, null) : (true, "cancelled", null, null, null);
            }
            case "approve":
            case "reject":
            {
                var applicant = Payload<AccountPayload>(command).AccountId;
                if (mine is not { Role: "leader" or "officer" }) { return (false, "not-permitted", null, null, null); }
                var application = await ScalarAsync<long?>(c, t, "SELECT expires_ms FROM alliance_applications WHERE account_id=$1 AND alliance_id=$2", ct, applicant, mine.AllianceId);
                if (application is null || application <= now) { return (false, "no-application", null, null, null); }
                if (command.Operation == "reject")
                {
                    await Run(c, t, "DELETE FROM alliance_applications WHERE account_id=$1", ct, applicant);
                    return (true, "rejected", null, null, null);
                }
                // Recheck everything at approval: the applicant may have joined elsewhere, been restricted, or the last seat may be gone.
                if (await MembershipAsync(c, t, applicant, ct) is not null) { return (false, "already-member", null, null, null); }
                if (await ScalarAsync<long?>(c, t, "SELECT until_ms FROM alliance_restrictions WHERE account_id=$1", ct, applicant) is { } u && u > now) { return (false, "rejoin-restricted", null, null, null); }
                var alliance = await AllianceAsync(c, t, mine.AllianceId, ct);
                if (alliance!.MemberCount >= content.MemberLimit) { return (false, "alliance-full", null, null, null); }
                await JoinAsync(c, t, mine.AllianceId, applicant, now, ct);
                return (true, "approved", null, null, null);
            }
            case "leave":
            {
                if (mine is null) { return (false, "not-member", null, null, null); }
                var alliance = await AllianceAsync(c, t, mine.AllianceId, ct);
                if (mine.Role == "leader" && alliance!.MemberCount > 1) { return (false, "leader-must-delegate", null, null, null); }
                await Run(c, t, "DELETE FROM alliance_members WHERE account_id=$1", ct, caller);
                await Run(c, t, "UPDATE alliances SET member_count=member_count-1, status=CASE WHEN member_count-1=0 THEN 'dissolved' ELSE status END WHERE id=$1", ct, mine.AllianceId);
                await Run(c, t, "UPDATE alliance_help_requests SET status='closed' WHERE requester=$1 AND status='open'", ct, caller);
                await Run(c, t, "INSERT INTO alliance_restrictions VALUES($1,$2) ON CONFLICT(account_id) DO UPDATE SET until_ms=$2", ct, caller, now + content.RejoinHours * 3_600_000L);
                return (true, alliance!.MemberCount == 1 ? "dissolved" : "left", null, null, null);
            }
            case "notice":
            {
                if (mine is not { Role: "leader" or "officer" }) { return (false, "not-permitted", null, null, null); }
                string? text = AllianceRules.Clean(Payload<TextPayload>(command).Text, 0, content.NoticeMaxChars, allowEmpty: true);
                if (text is null) { return (false, "invalid-text", null, null, null); }
                await Run(c, t, "UPDATE alliances SET notice=$2 WHERE id=$1", ct, mine.AllianceId, text);
                return (true, "notice-set", null, null, null);
            }
            case "chat":
            {
                var p = Payload<ChatPayload>(command);
                if (mine is null) { return (false, "not-member", null, null, null); }
                string? text = AllianceRules.Clean(p.Text, 1, content.Chat.MaxChars);
                if (text is null) { return (false, "invalid-text", null, null, null); }
                var last = await ScalarAsync<long?>(c, t, "SELECT max(at_ms) FROM alliance_messages WHERE author=$1", ct, caller);
                if (last is { } previous && now - previous < content.Chat.IntervalSeconds * 1000L) { return (false, "too-fast", null, null, null); }
                if (await ScalarAsync<Guid?>(c, t, "SELECT message_id FROM alliance_messages WHERE message_id=$1", ct, p.MessageId) is not null) { return (false, "message-id-used", null, null, null); }
                await Run(c, t, "INSERT INTO alliance_messages(message_id,alliance_id,author,body,at_ms) VALUES($1,$2,$3,$4,$5)", ct, p.MessageId, mine.AllianceId, caller, text, now);
                // Keep only the most recent N messages per alliance; reported messages keep their snapshot in alliance_reports.
                await Run(c, t, "DELETE FROM alliance_messages WHERE alliance_id=$1 AND seq NOT IN (SELECT seq FROM alliance_messages WHERE alliance_id=$1 ORDER BY seq DESC LIMIT $2)", ct,
                    mine.AllianceId, content.Chat.RetainedPerAlliance);
                return (true, "sent", null, null, null);
            }
            case "report":
            {
                var p = Payload<ReportPayload>(command);
                if (!content.ReportReasons.Contains(p.Reason)) { return (false, "invalid-reason", null, null, null); }
                string? note = AllianceRules.Clean(p.Note ?? "", 0, 80, allowEmpty: true);
                if (note is null) { return (false, "invalid-text", null, null, null); }
                if (mine is null) { return (false, "not-member", null, null, null); }
                await using var read = new NpgsqlCommand("SELECT alliance_id,author,body,at_ms,author_ref FROM alliance_messages WHERE message_id=$1", c, t);
                read.Parameters.AddWithValue(p.MessageId);
                Guid alliance; object author, authorRef; string body; long at;
                await using (var reader = await read.ExecuteReaderAsync(ct))
                {
                    if (!await reader.ReadAsync(ct)) { return (false, "unknown-message", null, null, null); }
                    (alliance, author, body, at, authorRef) = (reader.GetGuid(0), reader.GetValue(1), reader.GetString(2), reader.GetInt64(3), reader.GetValue(4));
                }
                if (alliance != mine.AllianceId) { return (false, "not-visible", null, null, null); }
                // Evidence snapshots are kept for the data retention period (GDD19 §5, [가설]) and then removed.
                await Run(c, t, "DELETE FROM alliance_reports WHERE created_ms < $1", ct, now - content.ReportRetentionDays * 86_400_000L);
                var existing = await ScalarAsync<Guid?>(c, t, "SELECT id FROM alliance_reports WHERE reporter=$1 AND message_id=$2 AND reason=$3", ct, caller, p.MessageId, p.Reason);
                if (existing is { } same) { return (true, "already-reported", null, same, null); }
                var reportId = command.CommandId;
                await Run(c, t, "INSERT INTO alliance_reports VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,NULL,$11)", ct, reportId, caller, p.MessageId, alliance, author, body, at, p.Reason, note, now, authorRef);
                return (true, "reported", null, reportId, null);
            }
            case "block":
            case "unblock":
            {
                var other = Payload<AccountPayload>(command).AccountId;
                if (other == caller) { return (false, "invalid-target", null, null, null); }
                if (command.Operation == "block")
                {
                    await Run(c, t, "INSERT INTO alliance_blocks VALUES($1,$2) ON CONFLICT DO NOTHING", ct, caller, other);
                    return (true, "blocked", null, null, null);
                }
                await Run(c, t, "DELETE FROM alliance_blocks WHERE account_id=$1 AND blocked=$2", ct, caller, other);
                return (true, "unblocked", null, null, null);
            }
            case "request-help":
            {
                // Clients know the node of their active research job; one job per node can be active at a time.
                string node = Payload<HelpRequestPayload>(command).NodeId;
                if (mine is null) { return (false, "not-member", null, null, null); }
                var job = await ResearchJobAsync(c, t, caller, node, ct);
                if (job is null || job.Value.CompletesAtMs <= now) { return (false, "job-not-active", null, null, null); }
                // GDD19 §6: only jobs started while in this alliance; a job started before joining never becomes eligible.
                if (job.Value.StartedAtMs < mine.JoinedMs) { return (false, "job-started-outside-alliance", null, null, null); }
                string key = "research:" + job.Value.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (await ScalarAsync<string?>(c, t, "SELECT status FROM alliance_help_requests WHERE requester=$1 AND job_key=$2", ct, caller, key) is not null)
                {
                    return (false, "already-requested", null, null, null);
                }
                await Run(c, t, "INSERT INTO alliance_help_requests VALUES($1,$2,$3,$4,$5,$6,0,0,'open')", ct, caller, key, mine.AllianceId, node,
                    job.Value.CompletesAtMs - job.Value.StartedAtMs, job.Value.CompletesAtMs);
                return (true, "requested", null, null, null);
            }
            case "give-help":
            {
                var p = Payload<HelpGivePayload>(command);
                if (mine is null) { return (false, "not-member", null, null, null); }
                if (p.Requester == caller) { return (false, "own-request", null, null, null); }
                await using var read = new NpgsqlCommand("SELECT alliance_id,duration_ms,given_ms,grants,status,completes_ms FROM alliance_help_requests WHERE requester=$1 AND job_key=$2", c, t);
                read.Parameters.AddWithValue(p.Requester); read.Parameters.AddWithValue(p.JobKey);
                Guid alliance; long duration, given, completes; int grants; string status;
                await using (var reader = await read.ExecuteReaderAsync(ct))
                {
                    if (!await reader.ReadAsync(ct)) { return (false, "unknown-request", null, null, null); }
                    (alliance, duration, given, grants, status, completes) = (reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetString(4), reader.GetInt64(5));
                }
                var requesterMembership = await MembershipAsync(c, t, p.Requester, ct);
                if (status != "open" || alliance != mine.AllianceId || requesterMembership?.AllianceId != alliance) { return (false, "request-closed", null, null, null); }
                if (grants >= content.Help.PerJobMaxGrants) { return (false, "help-full", null, null, null); }
                if (await ScalarAsync<long?>(c, t, "SELECT shortened_ms FROM alliance_help_grants WHERE requester=$1 AND job_key=$2 AND giver=$3", ct, p.Requester, p.JobKey, caller) is not null)
                {
                    return (false, "already-helped", null, null, null);
                }
                // Single settlement against completion: the requester's account row is locked, so the job state read here is current.
                long seq = long.Parse(p.JobKey["research:".Length..], System.Globalization.CultureInfo.InvariantCulture);
                var job = await ResearchJobBySeqAsync(c, t, p.Requester, seq, ct);
                if (job is null || job.Value.CompletesAtMs <= now) { return (false, "job-not-active", null, null, null); }
                long remaining = job.Value.CompletesAtMs - now - given;
                long shortened = AllianceRules.HelpShortening(content.Help, duration, remaining, given);
                if (shortened == 0) { return (false, "no-time-to-shorten", null, null, null); }
                await Run(c, t, "INSERT INTO alliance_help_grants VALUES($1,$2,$3,$4,$5)", ct, p.Requester, p.JobKey, caller, shortened, now);
                await Run(c, t, "UPDATE alliance_help_requests SET given_ms=given_ms+$3, grants=grants+1 WHERE requester=$1 AND job_key=$2", ct, p.Requester, p.JobKey, shortened);
                return (true, "helped", null, null, shortened);
            }
            default:
                throw new CommandConflictException("unsupported-operation");
        }
    }

    private static async Task JoinAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid alliance, Guid account, long now, CancellationToken ct)
    {
        await Run(c, t, "INSERT INTO alliance_members VALUES($1,$2,'member',$3)", ct, account, alliance, now);
        await Run(c, t, "UPDATE alliances SET member_count=member_count+1 WHERE id=$1", ct, alliance);
        await Run(c, t, "DELETE FROM alliance_applications WHERE account_id=$1", ct, account);
    }

    /// <summary>Read-only look at the requester's active research jobs in stored domain state (the requester row is locked by the caller).</summary>
    private static async Task<(long Seq, string Node, long StartedAtMs, long CompletesAtMs)[]> ResearchJobsAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid account, CancellationToken ct)
    {
        var json = await ScalarAsync<string?>(c, t, "SELECT state_json::text FROM account_domains WHERE account_id=$1 AND domain='research'", ct, account);
        if (json is null)
        {
            return [];
        }
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("active").EnumerateArray().Select(job => (job.GetProperty("seq").GetInt64(), job.GetProperty("nodeId").GetString()!,
            job.GetProperty("startedAtMs").GetInt64(), job.GetProperty("completesAtMs").GetInt64())).ToArray();
    }

    private static async Task<(long Seq, string Node, long StartedAtMs, long CompletesAtMs)?> ResearchJobAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid account, string node, CancellationToken ct) =>
        (await ResearchJobsAsync(c, t, account, ct)).Where(j => j.Node == node).Select(j => ((long Seq, string Node, long StartedAtMs, long CompletesAtMs)?)j).FirstOrDefault();

    private static async Task<(long Seq, string Node, long StartedAtMs, long CompletesAtMs)?> ResearchJobBySeqAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid account, long seq, CancellationToken ct) =>
        (await ResearchJobsAsync(c, t, account, ct)).Where(j => j.Seq == seq).Select(j => ((long Seq, string Node, long StartedAtMs, long CompletesAtMs)?)j).FirstOrDefault();

    private static async Task<Membership?> MembershipAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid account, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT alliance_id,role,joined_ms FROM alliance_members WHERE account_id=$1", c, t);
        query.Parameters.AddWithValue(account);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2)) : null;
    }

    private async Task<AllianceInfo?> AllianceAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid id, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT id,name,mode,notice,status,leader,member_count FROM alliances WHERE id=$1", c, t);
        query.Parameters.AddWithValue(id);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetGuid(5), reader.GetInt32(6), content.MemberLimit)
            : null;
    }

    private async Task<AllianceView> ViewAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid id, long now, CancellationToken ct)
    {
        var mine = await MembershipAsync(c, t, id, ct);
        var blocked = new List<Guid>();
        await using (var query = new NpgsqlCommand("SELECT blocked FROM alliance_blocks WHERE account_id=$1 ORDER BY blocked", c, t))
        {
            query.Parameters.AddWithValue(id);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { blocked.Add(reader.GetGuid(0)); }
        }
        var pending = await ScalarAsync<Guid?>(c, t, "SELECT alliance_id FROM alliance_applications WHERE account_id=$1 AND expires_ms>$2", ct, id, now);
        var until = await ScalarAsync<long?>(c, t, "SELECT until_ms FROM alliance_restrictions WHERE account_id=$1 AND until_ms>$2", ct, id, now);
        if (mine is null)
        {
            return new("none", null, [], [], [], pending, until, blocked.ToArray(), []);
        }
        var members = new List<AllianceMemberView>();
        await using (var query = new NpgsqlCommand("SELECT account_id,role,joined_ms FROM alliance_members WHERE alliance_id=$1 ORDER BY joined_ms,account_id", c, t))
        {
            query.Parameters.AddWithValue(mine.AllianceId);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { members.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2))); }
        }
        // Personal block hides only the blocked author's chat, and only for the blocker; help and membership are unaffected.
        var chat = new List<AllianceMessageView>();
        await using (var query = new NpgsqlCommand("""
            SELECT g.message_id, m.account_id, g.body, g.at_ms FROM alliance_messages g
            LEFT JOIN alliance_members m ON m.account_id=g.author AND m.alliance_id=g.alliance_id
            WHERE g.alliance_id=$1 AND (g.author IS NULL OR NOT (g.author = ANY($2))) ORDER BY g.seq
            """, c, t))
        {
            query.Parameters.AddWithValue(mine.AllianceId); query.Parameters.AddWithValue(blocked.ToArray());
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { chat.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetInt64(3))); }
        }
        // Requests whose job has finished are closed here, so the open list never shows completed work.
        await Run(c, t, "UPDATE alliance_help_requests SET status='closed' WHERE alliance_id=$1 AND status='open' AND completes_ms<=$2", ct, mine.AllianceId, now);
        var help = new List<AllianceHelpView>();
        await using (var query = new NpgsqlCommand("SELECT job_key,node_id,requester,duration_ms,given_ms,grants,completes_ms,status FROM alliance_help_requests WHERE alliance_id=$1 AND status='open' ORDER BY requester,job_key", c, t))
        {
            query.Parameters.AddWithValue(mine.AllianceId);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                help.Add(new(reader.GetString(0), reader.GetString(1), reader.GetGuid(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt32(5), reader.GetInt64(6), reader.GetString(7)));
            }
        }
        var applications = new List<AllianceApplicationView>();
        if (mine.Role is "leader" or "officer")
        {
            await using var query = new NpgsqlCommand("SELECT account_id,created_ms,expires_ms FROM alliance_applications WHERE alliance_id=$1 AND expires_ms>$2 ORDER BY created_ms,account_id", c, t);
            query.Parameters.AddWithValue(mine.AllianceId); query.Parameters.AddWithValue(now);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { applications.Add(new(reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2))); }
        }
        return new(mine.Role, await AllianceAsync(c, t, mine.AllianceId, ct), members.ToArray(), chat.ToArray(), help.ToArray(), pending, until, blocked.ToArray(), applications.ToArray());
    }

    private long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection c, NpgsqlTransaction t, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, c, t);
        foreach (object value in values) { command.Parameters.AddWithValue(value); }
        object? result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? default : (T)result;
    }

    private static async Task<int> Run(NpgsqlConnection c, NpgsqlTransaction t, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, c, t);
        foreach (object value in values) { command.Parameters.AddWithValue(value); }
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
