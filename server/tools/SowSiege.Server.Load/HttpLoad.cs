using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

internal static class HttpLoad
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(Dictionary<string, string> options)
    {
        int Number(string key) => int.Parse(options[key], System.Globalization.CultureInfo.InvariantCulture);
        int concurrency = Number("concurrency"), seconds = Number("seconds"), pool = Number("pool"), seedParallel = Number("seed-parallel");
        if (concurrency is not (1000 or 10000) || seconds is < 10 or > 600 || pool is < 1 or > 256 || seedParallel is < 1 or > 32 || seedParallel > pool)
        { throw new ArgumentOutOfRangeException(nameof(options)); }
        string schema = options["schema"], application = options["server-application"], database = options["database"];
        if (!Regex.IsMatch(schema, "^load_[a-z0-9_]{1,40}$") || !Regex.IsMatch(application, "^sow-load-host-[a-z0-9-]{1,30}$")
            || !Regex.IsMatch(database, "^sow_load_[a-z0-9_]{1,40}$")) { throw new ArgumentException("Isolated fixture names required."); }
        var server = new Uri(options["server-uri"], UriKind.Absolute);
        if (server.Scheme != "http" || server.Host is not ("localhost" or "127.0.0.1" or "[::1]" or "::1") || server.IsDefaultPort
            || server.AbsolutePath != "/" || server.Query.Length != 0 || server.Fragment.Length != 0 || server.UserInfo.Length != 0)
        { throw new ArgumentException("Explicit loopback HTTP origin required."); }
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SOW_LOAD_POSTGRES")
            ?? throw new InvalidOperationException("SOW_LOAD_POSTGRES required."));
        if (settings.Host is not ("localhost" or "127.0.0.1" or "::1") || settings.Port != 55441 || settings.Database != database
            || settings.SearchPath != schema) { throw new InvalidOperationException("Isolated database/schema mismatch."); }
        settings.ApplicationName = schema + "-seed";
        settings.MaxPoolSize = pool; settings.MinPoolSize = 0; settings.Multiplexing = false;
        settings.IncludeErrorDetail = false; settings.Timeout = 15; settings.CommandTimeout = 30;
        Directory.CreateDirectory(options["output"]);
        await using var manifestFile = new FileStream(Path.Combine(options["output"], "manifest.json"), FileMode.CreateNew);
        await using var sampleFile = new FileStream(Path.Combine(options["output"], "samples.jsonl"), FileMode.CreateNew);
        await using var samples = new StreamWriter(sampleFile);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
            // Host owner must create and initialize this private schema before running this mode.
            await using (var check = source.CreateCommand("SELECT current_schema()=$1 AND to_regclass('accounts') IS NOT NULL AND to_regclass('sessions') IS NOT NULL"))
            { check.Parameters.AddWithValue(schema); if (await check.ExecuteScalarAsync(stop.Token) is not true) { throw new InvalidOperationException("Host schema not initialized."); } }
            var content = EstateContent.Load(options["content-root"]);
            var store = new AccountStore(source, new EstateApplication(content), TimeProvider.System);
            await JsonSerializer.SerializeAsync(manifestFile, new
            {
                status = "PREPARING", mode = "http-current-policy", schema, database, application, serverOrigin = server.GetLeftPart(UriPartial.Authority),
                concurrency, seconds, seedParallel, seedPool = pool, revision = options["revision"], contentHash = content.Hash,
                seededEstateOnly = true, worldAndCityFixture = false, accountCreationHttpMeasured = false,
                serverPolicyModified = false, processMetricsOwner = "load-client", httpVersion = "1.1",
                startedUtc = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(),
            }, Json, stop.Token);
            await manifestFile.FlushAsync(stop.Token);
            var preparation = Stopwatch.StartNew();
            var ids = new Guid[concurrency]; var tokens = new string[concurrency]; var versions = new long[concurrency];
            await Parallel.ForEachAsync(Enumerable.Range(0, concurrency), new ParallelOptions { MaxDegreeOfParallelism = seedParallel, CancellationToken = stop.Token }, async (i, ct) =>
            {
                var created = await store.CreateAsync(ct);
                tokens[i] = created.Token;
                ids[i] = await store.AuthenticateAsync(created.Token, ct) ?? throw new InvalidDataException("Seed authentication failed.");
                versions[i] = created.Estate.Version;
            });
            var stats = new Load.Stats();
            using var connections = new HttpConnections(stats);
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false, MaxConnectionsPerServer = concurrency };
            using var http = new HttpClient(handler) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 1024 * 1024 };
            async Task<HttpResponseMessage> Send(int i, EstateCommand command, CancellationToken ct)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/estate/commands")
                { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact, Content = JsonContent.Create(command, options: Json) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[i]);
                return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            }
            // One real accepted HTTP command must be visible in the explicitly selected schema.
            // A 429 here is a preflight blocker, never a reason to alter/reset the limiter.
            var probe = new EstateCommand(Guid.NewGuid(), versions[0], "claim-production", null);
            using (var response = await Send(0, probe, stop.Token))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { status = "PREFLIGHT_INCOMPLETE", httpStatus = (int)response.StatusCode, measured = false }, Json));
                    return response.StatusCode == HttpStatusCode.TooManyRequests ? 2 : 1;
                }
                var reply = await response.Content.ReadFromJsonAsync<CommandReply>(Json, stop.Token) ?? throw new InvalidDataException("Probe response absent.");
                if (reply.Estate.Version != versions[0] + 1) { throw new InvalidDataException("Probe version mismatch."); }
                versions[0] = reply.Estate.Version;
            }
            await using (var proof = source.CreateCommand("SELECT count(*) FROM commands c JOIN accounts a ON a.id=c.account_id WHERE c.account_id=$1 AND c.command_id=$2 AND a.version=$3"))
            {
                proof.Parameters.AddWithValue(ids[0]); proof.Parameters.AddWithValue(probe.CommandId); proof.Parameters.AddWithValue(versions[0]);
                if ((long)(await proof.ExecuteScalarAsync(stop.Token))! != 1) { throw new InvalidDataException("HTTP target did not mutate the selected schema."); }
            }
            await using (var host = source.CreateCommand("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND application_name=$1"))
            { host.Parameters.AddWithValue(application); if ((long)(await host.ExecuteScalarAsync(stop.Token))! == 0) { throw new InvalidDataException("Host observer identity missing."); } }
            preparation.Stop();
            var observerSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { ApplicationName = schema + "-observer", MaxPoolSize = 1 };
            await using var observerSource = NpgsqlDataSource.Create(observerSettings.ConnectionString);
            await using var observer = await observerSource.OpenConnectionAsync(stop.Token);
            long beforeDeadlocks = await Load.Deadlocks(observer, stop.Token);
            using var observationStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int ready = 0; long deadline = 0;
            var workers = Enumerable.Range(0, concurrency).Select(async i =>
            {
                Interlocked.Increment(ref ready); await start.Task.WaitAsync(stop.Token);
                EstateCommand? replay = null; string? original = null;
                TimeSpan backoff = TimeSpan.Zero;
                while (Stopwatch.GetTimestamp() < Volatile.Read(ref deadline))
                {
                    // Backoff is deliberately outside InFlight; do not report sleeping workers as HTTP requests.
                    if (backoff > TimeSpan.Zero)
                    {
                        double remaining = (Volatile.Read(ref deadline) - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency;
                        if (backoff.TotalSeconds >= remaining) { break; }
                        await Task.Delay(backoff, stop.Token);
                        backoff = TimeSpan.Zero;
                    }
                    var command = replay ?? new EstateCommand(Guid.NewGuid(), versions[i], "claim-production", null);
                    long began = Stopwatch.GetTimestamp(); stats.Peak(Interlocked.Increment(ref stats.InFlight));
                    try
                    {
                        using var response = await Send(i, command, stop.Token);
                        stats.Record(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
                        if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            Interlocked.Increment(ref stats.Http429);
                            var retryAfter = response.Headers.RetryAfter;
                            backoff = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(100));
                            if (backoff < TimeSpan.FromMilliseconds(100)) { backoff = TimeSpan.FromMilliseconds(100); }
                            continue;
                        }
                        if (response.StatusCode == HttpStatusCode.Unauthorized) { Interlocked.Increment(ref stats.Http401); break; }
                        if ((int)response.StatusCode >= 500) { Interlocked.Increment(ref stats.Http5xx); break; }
                        if (response.StatusCode != HttpStatusCode.OK) { Interlocked.Increment(ref stats.HttpOther); break; }
                        string body = await response.Content.ReadAsStringAsync(stop.Token);
                        if (replay is not null)
                        {
                            if (body != original) { Interlocked.Increment(ref stats.Errors); break; }
                            Interlocked.Increment(ref stats.Replays); stats.Replayed[i] = true; replay = null; original = null;
                            continue;
                        }
                        var reply = JsonSerializer.Deserialize<CommandReply>(body, Json) ?? throw new InvalidDataException("Missing command reply.");
                        if (reply.Estate.Version != versions[i] + 1) { Interlocked.Increment(ref stats.Errors); break; }
                        versions[i] = reply.Estate.Version; Interlocked.Increment(ref stats.Committed);
                        if (reply.Done) { Interlocked.Increment(ref stats.Done); } else { Interlocked.Increment(ref stats.Refused); }
                        if (!stats.Replayed[i]) { replay = command; original = body; }
                    }
                    catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException)
                    { Interlocked.Increment(ref stats.Errors); break; }
                    finally { Interlocked.Decrement(ref stats.InFlight); }
                }
            }).ToArray();
            if (ready != concurrency) { throw new InvalidOperationException("HTTP worker barrier incomplete."); }
            Task observing = Load.Observe(observer, application, stats, samples, observationStop.Token);
            var elapsed = Stopwatch.StartNew(); deadline = Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency;
            start.SetResult();
            try { await Task.WhenAll(workers); }
            finally
            {
                observationStop.Cancel();
                try { await observing; } catch (OperationCanceledException) when (observationStop.IsCancellationRequested) { }
                await samples.FlushAsync();
            }
            elapsed.Stop();
            long deadlocks = await Load.Deadlocks(observer, stop.Token) - beforeDeadlocks;
            int found = 0;
            await using (var verify = source.CreateCommand("SELECT id,version FROM accounts WHERE id=ANY($1)"))
            {
                verify.Parameters.AddWithValue(ids);
                var expected = ids.Select((id, i) => (id, versions[i])).ToDictionary(x => x.id, x => x.Item2);
                await using var rows = await verify.ExecuteReaderAsync(stop.Token);
                while (await rows.ReadAsync(stop.Token)) { found++; if (rows.GetInt64(1) != expected[rows.GetGuid(0)]) { stats.Errors++; } }
            }
            bool correct = found == concurrency && stats.Errors == 0 && stats.Http401 == 0 && stats.Http5xx == 0 && stats.HttpOther == 0 && deadlocks == 0;
            bool target = stats.Maximum == concurrency && stats.MaximumConnections >= concurrency;
            string status = !correct ? "FAILED" : !target ? "PARTIAL_CONCURRENCY_TARGET_UNMET" : "HTTP_POLICY_SATURATION_OBSERVED";
            var summary = new
            {
                status, mode = "http-current-policy", concurrency, ready, peakInFlightRequests = stats.Maximum,
                peakObservedHttpConnections = stats.MaximumConnections, connectionTelemetry = "System.Net.Http connection lifecycle events",
                stats.Committed, stats.Done, stats.Refused, stats.Replays, stats.Http429, stats.Http401, stats.Http5xx, stats.HttpOther, stats.Errors, deadlocks,
                preparationSeconds = preparation.Elapsed.TotalSeconds, measuredAndDrainSeconds = elapsed.Elapsed.TotalSeconds,
                acceptedNewCommandsPerSecond = stats.Committed / elapsed.Elapsed.TotalSeconds,
                allHttpLatencyBucketUpperMs = new { p50 = stats.Percentile(.5), p95 = stats.Percentile(.95), p99 = stats.Percentile(.99) },
                replayCoverageAccounts = stats.Replayed.Count(x => x), allAccountReplayVerified = stats.Replays == concurrency,
                capacityClaim = "current HTTP policy only; not unrestricted server capacity", preflightAcceptedCommands = 1,
                fallback429BackoffMilliseconds = 100, retryAfterRespected = true, inFlightIncludesClientQueue = true,
                actualTcpConcurrency = stats.MaximumConnections > 0 ? "HTTP1 connection events observed; not simultaneous command execution" : "UNKNOWN",
            };
            await using var output = new FileStream(Path.Combine(options["output"], "summary.json"), FileMode.CreateNew);
            await JsonSerializer.SerializeAsync(output, summary, Json, stop.Token);
            Console.WriteLine(JsonSerializer.Serialize(summary, Json));
            return !correct ? 1 : target ? 0 : 2;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private sealed class HttpConnections : EventListener
    {
        private readonly Load.Stats stats;
        private readonly HashSet<long> open = [];
        public HttpConnections(Load.Stats stats)
        {
            this.stats = stats;
            foreach (var source in EventSource.GetSources().Where(s => s.Name == "System.Net.Http")) { EnableEvents(source, EventLevel.Informational); }
        }
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Net.Http") { EnableEvents(source, EventLevel.Informational); }
        }
        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (stats is null || e.PayloadNames is null || e.Payload is null) { return; }
            int index = e.PayloadNames.IndexOf("connectionId");
            if (index < 0 || e.Payload[index] is not long id) { return; }
            lock (open)
            {
                if (e.EventName == "ConnectionEstablished") { open.Add(id); }
                else if (e.EventName == "ConnectionClosed") { open.Remove(id); }
                else { return; }
                Volatile.Write(ref stats.Connections, open.Count);
                stats.MaximumConnections = Math.Max(stats.MaximumConnections, open.Count);
            }
        }
    }
}
