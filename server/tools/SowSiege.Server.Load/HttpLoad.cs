using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
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
        int rampPerSecond = options.TryGetValue("ramp-per-second", out var rampText)
            ? int.Parse(rampText, System.Globalization.CultureInfo.InvariantCulture) : 0;
        if (rampText is not null && (rampPerSecond is < 1 or > 10000)) { throw new ArgumentOutOfRangeException(nameof(options)); }
        var ramp = new LoadRamp(concurrency, rampPerSecond, seconds);
        if (ramp.TotalDuration >= TimeSpan.FromMinutes(30)) { throw new ArgumentException("Ramp and plateau exceed the existing run lifetime."); }
        string connectionMode = options.GetValueOrDefault("connection-mode", "shared");
        if (connectionMode is not ("shared" or "per-account")) { throw new ArgumentException("Unknown connection mode."); }
        string? serverRevision = options.GetValueOrDefault("server-revision");
        if (serverRevision is not null && !Regex.IsMatch(serverRevision, "^[a-fA-F0-9]{40}$"))
        { throw new ArgumentException("Server revision must be a full SHA."); }
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
                connectionMode, serverRevision, serverRevisionSource = serverRevision is null ? "UNKNOWN" : "operator-supplied",
                rampPerSecond, plannedRampSeconds = ramp.RampDuration.TotalSeconds, plannedPlateauSeconds = ramp.PlateauDuration.TotalSeconds,
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
            var errors = new ConcurrentDictionary<ErrorClassification, long>();
            void RecordError(ErrorClassification error)
            {
                Interlocked.Increment(ref stats.Errors);
                errors.AddOrUpdate(error, 1, static (_, count) => count + 1);
            }
            using var connections = new HttpConnections(stats);
            using var clients = new HttpClients(server, concurrency, connectionMode == "per-account");
            async Task<HttpResponseMessage> Send(int i, EstateCommand command, CancellationToken ct)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/estate/commands")
                { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact, Content = JsonContent.Create(command, options: Json) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[i]);
                return await clients.ForAccount(i).SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
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
            var gates = Enumerable.Range(0, concurrency).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            var entered = Enumerable.Range(0, concurrency).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            int ready = 0, admitted = 0, activeWorkers = 0;
            long epoch = 0, deadline = long.MaxValue, plateauStarted = 0, maximumAdmissionLagTicks = 0;
            var workers = Enumerable.Range(0, concurrency).Select(async i =>
            {
                Interlocked.Increment(ref ready); await gates[i].Task.WaitAsync(stop.Token);
                Interlocked.Increment(ref admitted);
                long lag = Math.Max(0, (Stopwatch.GetElapsedTime(epoch) - ramp.AdmissionAt(i)).Ticks);
                long previousLag;
                do { previousLag = Volatile.Read(ref maximumAdmissionLagTicks); if (lag <= previousLag) { break; } }
                while (Interlocked.CompareExchange(ref maximumAdmissionLagTicks, lag, previousLag) != previousLag);
                Interlocked.Increment(ref activeWorkers);
                entered[i].SetResult();
                try
                {
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
                                if (body != original) { RecordError(new("ReplayMismatch")); break; }
                                Interlocked.Increment(ref stats.Replays); stats.Replayed[i] = true; replay = null; original = null;
                                continue;
                            }
                            var reply = JsonSerializer.Deserialize<CommandReply>(body, Json) ?? throw new InvalidDataException("Missing command reply.");
                            if (reply.Estate.Version != versions[i] + 1) { RecordError(new("VersionMismatch")); break; }
                            versions[i] = reply.Estate.Version; Interlocked.Increment(ref stats.Committed);
                            if (reply.Done) { Interlocked.Increment(ref stats.Done); } else { Interlocked.Increment(ref stats.Refused); }
                            if (!stats.Replayed[i]) { replay = command; original = body; }
                        }
                        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException)
                        { RecordError(Classify(error, stop.IsCancellationRequested)); break; }
                        finally { Interlocked.Decrement(ref stats.InFlight); }
                    }
                }
                finally { Interlocked.Decrement(ref activeWorkers); }
            }).ToArray();
            if (ready != concurrency) { throw new InvalidOperationException("HTTP worker barrier incomplete."); }
            long plateauSamples = 0, plateauConnectionTargetSamples = 0;
            int minimumPlateauConnections = int.MaxValue, minimumPlateauActiveWorkers = int.MaxValue;
            object WorkloadSample()
            {
                var at = Stopwatch.GetElapsedTime(epoch);
                long plateau = Volatile.Read(ref plateauStarted);
                string phase = plateau == 0 ? "ramp" : Stopwatch.GetTimestamp() < Volatile.Read(ref deadline) ? "plateau" : "drain";
                int currentConnections = Volatile.Read(ref stats.Connections), currentWorkers = Volatile.Read(ref activeWorkers);
                if (phase == "plateau")
                {
                    plateauSamples++;
                    if (currentConnections >= concurrency) { plateauConnectionTargetSamples++; }
                    minimumPlateauConnections = Math.Min(minimumPlateauConnections, currentConnections);
                    minimumPlateauActiveWorkers = Math.Min(minimumPlateauActiveWorkers, currentWorkers);
                }
                return new { phase, elapsedSeconds = at.TotalSeconds, admittedAccounts = Volatile.Read(ref admitted), activeWorkers = currentWorkers,
                    plannedRampSeconds = ramp.RampDuration.TotalSeconds, plannedPlateauSeconds = ramp.PlateauDuration.TotalSeconds };
            }
            var elapsed = Stopwatch.StartNew();
            epoch = Stopwatch.GetTimestamp();
            async Task AdmitAsync()
            {
                if (rampPerSecond == 0)
                {
                    Volatile.Write(ref deadline, epoch + seconds * Stopwatch.Frequency);
                    Volatile.Write(ref plateauStarted, epoch);
                    foreach (var gate in gates) { gate.SetResult(); }
                    return;
                }
                TimeSpan next = TimeSpan.Zero;
                for (int i = 0; i < concurrency; i++)
                {
                    TimeSpan delay;
                    while ((delay = next - Stopwatch.GetElapsedTime(epoch)) > TimeSpan.Zero)
                    { await Task.Delay(delay, stop.Token); }
                    gates[i].SetResult();
                    await entered[i].Task.WaitAsync(stop.Token);
                    next = ramp.NextAdmissionAfter(Stopwatch.GetElapsedTime(epoch));
                }
                long began = Stopwatch.GetTimestamp();
                Volatile.Write(ref deadline, began + checked((long)Math.Ceiling(ramp.PlateauDuration.TotalSeconds * Stopwatch.Frequency)));
                Volatile.Write(ref plateauStarted, began);
            }
            Task observing = Load.Observe(observer, application, stats, samples, observationStop.Token, WorkloadSample);
            Task admission = AdmitAsync();
            try
            {
                await Task.WhenAll(workers.Append(admission));
                // Retry-After can retire all workers early; retain the entire declared plateau observation.
                if (rampPerSecond > 0)
                {
                    double remaining = (Volatile.Read(ref deadline) - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency;
                    if (remaining > 0) { await Task.Delay(TimeSpan.FromSeconds(remaining), stop.Token); }
                }
            }
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
                while (await rows.ReadAsync(stop.Token))
                {
                    found++;
                    if (rows.GetInt64(1) != expected[rows.GetGuid(0)]) { RecordError(new("FinalVersionMismatch")); }
                }
            }
            bool correct = found == concurrency && stats.Errors == 0 && stats.Http401 == 0 && stats.Http5xx == 0 && stats.HttpOther == 0 && deadlocks == 0;
            bool target = admitted == concurrency && stats.MaximumConnections >= concurrency && plateauSamples > 0;
            string status = !correct ? "FAILED" : !target ? "PARTIAL_CONCURRENCY_TARGET_UNMET" : "HTTP_POLICY_SATURATION_OBSERVED";
            var summary = new
            {
                status, mode = "http-current-policy", concurrency, ready, peakInFlightRequests = stats.Maximum,
                connectionMode, clientRevision = options["revision"], serverRevision,
                rampPerSecond, admittedAccounts = admitted, plannedRampSeconds = ramp.RampDuration.TotalSeconds,
                plannedPlateauSeconds = ramp.PlateauDuration.TotalSeconds,
                actualRampSeconds = plateauStarted == 0 ? (double?)null : Stopwatch.GetElapsedTime(epoch, plateauStarted).TotalSeconds,
                measuredPlateauSeconds = plateauStarted == 0 ? 0 : Math.Clamp(elapsed.Elapsed.TotalSeconds - Stopwatch.GetElapsedTime(epoch, plateauStarted).TotalSeconds, 0, ramp.PlateauDuration.TotalSeconds),
                maximumAdmissionLagSeconds = TimeSpan.FromTicks(maximumAdmissionLagTicks).TotalSeconds,
                plateauSamples, plateauConnectionTargetSamples,
                plateauConnectionTargetSampleFraction = plateauSamples == 0 ? (double?)null : plateauConnectionTargetSamples / (double)plateauSamples,
                minimumPlateauConnections = plateauSamples == 0 ? (int?)null : minimumPlateauConnections,
                minimumPlateauActiveWorkers = plateauSamples == 0 ? (int?)null : minimumPlateauActiveWorkers,
                serverRevisionSource = serverRevision is null ? "UNKNOWN" : "operator-supplied",
                peakObservedHttpConnections = stats.MaximumConnections, connectionTelemetry = "System.Net.Http connection lifecycle events",
                stats.Committed, stats.Done, stats.Refused, stats.Replays, stats.Http429, stats.Http401, stats.Http5xx, stats.HttpOther, stats.Errors, deadlocks,
                preparationSeconds = preparation.Elapsed.TotalSeconds, measuredAndDrainSeconds = elapsed.Elapsed.TotalSeconds,
                acceptedNewCommandsPerSecond = stats.Committed / elapsed.Elapsed.TotalSeconds,
                allHttpLatencyBucketUpperMs = new { p50 = stats.Percentile(.5), p95 = stats.Percentile(.95), p99 = stats.Percentile(.99) },
                replayCoverageAccounts = stats.Replayed.Count(x => x), allAccountReplayVerified = stats.Replays == concurrency,
                capacityClaim = "current HTTP policy only; not unrestricted server capacity", preflightAcceptedCommands = 1,
                fallback429BackoffMilliseconds = 100, retryAfterRespected = true, inFlightIncludesClientQueue = true,
                actualTcpConcurrency = stats.MaximumConnections > 0 ? "HTTP1 connection events observed; not simultaneous command execution" : "UNKNOWN",
                errorCounts = errors.OrderBy(p => p.Key.Kind, StringComparer.Ordinal)
                    .ThenBy(p => p.Key.Type, StringComparer.Ordinal).ThenBy(p => p.Key.HttpRequestError, StringComparer.Ordinal)
                    .ThenBy(p => p.Key.SocketErrorCode, StringComparer.Ordinal).Select(p => new { error = p.Key, count = p.Value }).ToArray(),
            };
            await using var output = new FileStream(Path.Combine(options["output"], "summary.json"), FileMode.CreateNew);
            await JsonSerializer.SerializeAsync(output, summary, Json, stop.Token);
            Console.WriteLine(JsonSerializer.Serialize(summary, Json));
            return !correct ? 1 : target ? 0 : 2;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    // Only fixed categories and BCL enum/type names: no exception message, request, response, address or account identifier.
    internal sealed record ErrorClassification(string Kind, string? Type = null, string? HttpRequestError = null, string? SocketErrorCode = null);

    internal static ErrorClassification Classify(Exception error, bool operationCancelled)
    {
        SocketException? socket = null;
        bool timeout = false;
        Exception? current = error;
        for (int depth = 0; depth < 8 && current is not null; depth++, current = current.InnerException)
        {
            socket ??= current as SocketException;
            timeout |= current is TimeoutException;
        }
        string? socketCode = socket is null ? null : Enum.IsDefined(socket.SocketErrorCode) ? socket.SocketErrorCode.ToString() : "Unknown";
        if (error is HttpRequestException http)
        {
            string httpError = Enum.IsDefined(http.HttpRequestError) ? http.HttpRequestError.ToString() : "Unknown";
            return new("Transport", nameof(HttpRequestException), httpError, socketCode);
        }
        if (error is OperationCanceledException)
        {
            string kind = operationCancelled ? "Cancellation" : timeout ? "Timeout" : "CancellationUnknownOrigin";
            return new(kind, error is TaskCanceledException ? nameof(TaskCanceledException) : nameof(OperationCanceledException), SocketErrorCode: socketCode);
        }
        if (error is TimeoutException) { return new("Timeout", nameof(TimeoutException), SocketErrorCode: socketCode); }
        if (error is SocketException) { return new("Socket", nameof(SocketException), SocketErrorCode: socketCode); }
        if (error is JsonException) { return new("Json", nameof(JsonException)); }
        if (error is InvalidDataException) { return new("InvalidResponse", nameof(InvalidDataException)); }
        return new("Other", nameof(Exception));
    }

    private sealed class HttpClients : IDisposable
    {
        private readonly List<HttpClient> clients = [];
        private readonly bool perAccount;
        public HttpClients(Uri server, int concurrency, bool perAccount)
        {
            this.perAccount = perAccount;
            try
            {
                for (int i = 0; i < (perAccount ? concurrency : 1); i++)
                {
                    var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                        MaxConnectionsPerServer = perAccount ? 1 : concurrency };
                    HttpClient client;
                    try { client = new HttpClient(handler, disposeHandler: true); }
                    catch { handler.Dispose(); throw; }
                    clients.Add(client);
                    client.BaseAddress = server;
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.MaxResponseContentBufferSize = 1024 * 1024;
                }
            }
            catch { Dispose(); throw; }
        }
        public HttpClient ForAccount(int index) => clients[perAccount ? index : 0];
        public void Dispose()
        {
            foreach (var client in clients) { client.Dispose(); }
            clients.Clear();
        }
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
