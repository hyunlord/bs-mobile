using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

// Independent direct-writer and current-policy HTTP measurements; never combine their results.
return await Load.RunAsync(args);

internal static class Load
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = Parse(args);
            return options["mode"] switch
            {
                "direct" => await ExecuteAsync(options),
                "http" => await HttpLoad.RunAsync(options),
                _ => throw new ArgumentException("Unknown mode.")
            };
        }
        catch (Exception error)
        {
            // Exception messages/connection strings can contain credentials. Emit only a type and safe SQLSTATE.
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "FAILED", error = error.GetType().Name,
                sqlState = (error as PostgresException)?.SqlState }, Json));
            return 1;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        string[] required = ["mode", "database", "concurrency", "seconds", "pool", "seed-parallel", "content-root", "output", "revision"];
        string[] allowed = [.. required, "server-uri", "schema", "server-application"];
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0) { throw new ArgumentException("Expected option/value pairs."); }
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(args[i][2..])
                || !result.TryAdd(args[i][2..], args[i + 1])) { throw new ArgumentException("Unknown or duplicate option."); }
        }
        foreach (string name in required) { if (!result.ContainsKey(name)) { throw new ArgumentException("Missing option."); } }
        if (result["mode"] == "direct" && result.Keys.Any(k => !required.Contains(k))) { throw new ArgumentException("HTTP options supplied to direct mode."); }
        return result;
    }

    private static async Task<int> ExecuteAsync(Dictionary<string, string> options)
    {
        int concurrency = int.Parse(options["concurrency"], System.Globalization.CultureInfo.InvariantCulture);
        int seconds = int.Parse(options["seconds"], System.Globalization.CultureInfo.InvariantCulture);
        int pool = int.Parse(options["pool"], System.Globalization.CultureInfo.InvariantCulture);
        int seedParallel = int.Parse(options["seed-parallel"], System.Globalization.CultureInfo.InvariantCulture);
        if (concurrency is not (1000 or 10000) || seconds is < 10 or > 600 || pool is < 1 or > 256
            || seedParallel is < 1 or > 32 || seedParallel > pool) { throw new ArgumentOutOfRangeException(nameof(options)); }
        string database = options["database"];
        if (!Regex.IsMatch(database, "^sow_load_[a-z0-9_]{1,40}$")) { throw new ArgumentException("Dedicated load database required."); }
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SOW_LOAD_POSTGRES")
            ?? throw new InvalidOperationException("SOW_LOAD_POSTGRES required."));
        if (settings.Host is not ("localhost" or "127.0.0.1" or "::1") || settings.Port != 55441
            || settings.Database != database) { throw new InvalidOperationException("Local isolated database mismatch."); }
        string schema = "load_" + Guid.NewGuid().ToString("N");
        string application = schema;
        settings.SearchPath = schema;
        settings.ApplicationName = application;
        settings.MaxPoolSize = pool;
        settings.MinPoolSize = 0;
        settings.Multiplexing = false;
        settings.IncludeErrorDetail = false;
        settings.Timeout = 15;
        settings.CommandTimeout = 30;
        // Exclusive output creation refuses overwriting previous evidence. No deletion or DB creation commands exist here.
        Directory.CreateDirectory(options["output"]);
        await using var manifestFile = new FileStream(Path.Combine(options["output"], "manifest.json"), FileMode.CreateNew);
        await using var sampleFile = new FileStream(Path.Combine(options["output"], "samples.jsonl"), FileMode.CreateNew);
        await using var samples = new StreamWriter(sampleFile);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
            await using (var create = source.CreateCommand("CREATE SCHEMA " + schema)) { await create.ExecuteNonQueryAsync(stop.Token); }
            var content = EstateContent.Load(options["content-root"]);
            var store = new AccountStore(source, new EstateApplication(content), TimeProvider.System);
            await store.InitializeAsync(stop.Token);
            await JsonSerializer.SerializeAsync(manifestFile, new
            {
                status = "PREPARING", revision = options["revision"], schema, database, concurrency, seconds, pool, seedParallel,
                mode = "direct-estate-writer", operation = "claim-production", http = false, authentication = false,
                registeredDomains = false, world = false, runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
                startedUtc = DateTimeOffset.UtcNow, contentHash = content.Hash, contentRoot = Path.GetFullPath(options["content-root"]),
            }, Json, stop.Token);
            await manifestFile.FlushAsync(stop.Token);
            var preparation = Stopwatch.StartNew();
            var ids = new Guid[concurrency];
            var versions = new long[concurrency];
            await Parallel.ForEachAsync(Enumerable.Range(0, concurrency), new ParallelOptions { MaxDegreeOfParallelism = seedParallel, CancellationToken = stop.Token },
                async (index, token) =>
                {
                    var created = await store.CreateAsync(token);
                    ids[index] = await store.AuthenticateAsync(created.Token, token) ?? throw new InvalidDataException("Seed authentication failed.");
                    versions[index] = created.Estate.Version;
                    var warm = new EstateCommand(Guid.NewGuid(), versions[index], "claim-production", null);
                    string first = await store.ExecuteAsync(ids[index], warm, token);
                    if (first != await store.ExecuteAsync(ids[index], warm, token)) { throw new InvalidDataException("Warm replay mismatch."); }
                    versions[index] = JsonSerializer.Deserialize<CommandReply>(first, Json)!.Estate.Version;
                });
            preparation.Stop();
            var observerSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { ApplicationName = application + "-observer", MaxPoolSize = 1 };
            await using var observerSource = NpgsqlDataSource.Create(observerSettings.ConnectionString);
            await using var observer = await observerSource.OpenConnectionAsync(stop.Token);
            long deadlocksBefore = await Deadlocks(observer, stop.Token);
            var stats = new Stats();
            using var observeStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            long deadline = 0;
            int ready = 0;
            Task[] workers = Enumerable.Range(0, concurrency).Select(async index =>
            {
                Interlocked.Increment(ref ready);
                await start.Task.WaitAsync(stop.Token);
                while (Stopwatch.GetTimestamp() < Volatile.Read(ref deadline))
                {
                    var command = new EstateCommand(Guid.NewGuid(), versions[index], "claim-production", null);
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    request.CancelAfter(TimeSpan.FromSeconds(30));
                    long began = Stopwatch.GetTimestamp();
                    int pending = Interlocked.Increment(ref stats.InFlight);
                    stats.Peak(pending);
                    try
                    {
                        string response = await store.ExecuteAsync(ids[index], command, request.Token);
                        stats.Record(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
                        var reply = JsonSerializer.Deserialize<CommandReply>(response, Json) ?? throw new InvalidDataException("Missing response.");
                        if (reply.Estate.Version != versions[index] + 1) { throw new InvalidDataException("Version mismatch."); }
                        versions[index] = reply.Estate.Version;
                        Interlocked.Increment(ref stats.Committed);
                        if (reply.Done) { Interlocked.Increment(ref stats.Done); }
                        else { Interlocked.Increment(ref stats.Refused); }
                        // One replay per worker during measurement, separately counted from new commands.
                        if (!stats.Replayed[index])
                        {
                            if (response != await store.ExecuteAsync(ids[index], command, request.Token)) { throw new InvalidDataException("Replay mismatch."); }
                            stats.Replayed[index] = true;
                            Interlocked.Increment(ref stats.Replays);
                        }
                    }
                    catch { Interlocked.Increment(ref stats.Errors); stop.Cancel(); throw; }
                    finally { Interlocked.Decrement(ref stats.InFlight); }
                }
            }).ToArray();
            if (ready != concurrency) { throw new InvalidOperationException("Worker barrier incomplete."); }
            var elapsed = Stopwatch.StartNew();
            deadline = Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency;
            Task observing = Observe(observer, application, stats, samples, observeStop.Token);
            start.SetResult();
            try { await Task.WhenAll(workers); }
            finally
            {
                observeStop.Cancel();
                try { await observing; } catch (OperationCanceledException) when (observeStop.IsCancellationRequested) { }
                await samples.FlushAsync();
            }
            elapsed.Stop();
            long deadlocks = await Deadlocks(observer, stop.Token) - deadlocksBefore;
            // Verify DB version against each worker after replay: replay must not add another version.
            await using var verify = source.CreateCommand("SELECT id,version FROM accounts WHERE id=ANY($1)");
            verify.Parameters.AddWithValue(ids);
            var expected = ids.Select((id, index) => (id, versions[index])).ToDictionary(x => x.id, x => x.Item2);
            await using var rows = await verify.ExecuteReaderAsync(stop.Token);
            int found = 0;
            while (await rows.ReadAsync(stop.Token))
            {
                if (expected[rows.GetGuid(0)] != rows.GetInt64(1)) { throw new InvalidDataException("Final version mismatch."); }
                found++;
            }
            bool pass = found == concurrency && stats.Replays == concurrency && deadlocks == 0 && stats.Errors == 0 && stats.Maximum == concurrency;
            var summary = new
            {
                status = pass ? "CORRECTNESS_PASS_CAPACITY_UNRATED" : "FAILED", concurrency, ready, peakInFlight = stats.Maximum,
                preparationSeconds = preparation.Elapsed.TotalSeconds, measuredAndDrainSeconds = elapsed.Elapsed.TotalSeconds,
                stats.Committed, stats.Done, stats.Refused, stats.Replays, stats.Errors, deadlocks,
                committedPerSecond = stats.Committed / elapsed.Elapsed.TotalSeconds,
                latencyBucketUpperMs = new { p50 = stats.Percentile(.50), p95 = stats.Percentile(.95), p99 = stats.Percentile(.99) },
                simultaneousDatabaseExecutionNotClaimed = true,
            };
            await using var summaryFile = new FileStream(Path.Combine(options["output"], "summary.json"), FileMode.CreateNew);
            await JsonSerializer.SerializeAsync(summaryFile, summary, Json, stop.Token);
            Console.WriteLine(JsonSerializer.Serialize(summary, Json));
            return pass ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    internal static async Task<long> Deadlocks(NpgsqlConnection connection, CancellationToken token)
    {
        await using var query = new NpgsqlCommand("SELECT deadlocks FROM pg_stat_database WHERE datname=current_database()", connection);
        return Convert.ToInt64(await query.ExecuteScalarAsync(token));
    }

    internal static async Task Observe(NpgsqlConnection connection, string application, Stats stats, StreamWriter output, CancellationToken token)
    {
        using var process = Process.GetCurrentProcess();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        do
        {
            await using var query = new NpgsqlCommand("""
                SELECT count(*),count(*) FILTER(WHERE state='active'),count(*) FILTER(WHERE wait_event_type='Lock'),
                  count(*) FILTER(WHERE cardinality(pg_blocking_pids(pid))>0),
                  coalesce(max(extract(epoch FROM clock_timestamp()-xact_start)),0)::double precision
                FROM pg_stat_activity WHERE datname=current_database() AND application_name=$1
                """, connection);
            query.Parameters.AddWithValue(application);
            await using var reader = await query.ExecuteReaderAsync(token);
            await reader.ReadAsync(token);
            process.Refresh();
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow, inFlight = Volatile.Read(ref stats.InFlight), committed = Interlocked.Read(ref stats.Committed),
                http429 = Interlocked.Read(ref stats.Http429), http401 = Interlocked.Read(ref stats.Http401), http5xx = Interlocked.Read(ref stats.Http5xx),
                httpOther = Interlocked.Read(ref stats.HttpOther), openHttpConnections = Volatile.Read(ref stats.Connections),
                connections = reader.GetInt64(0), active = reader.GetInt64(1), lockWaiting = reader.GetInt64(2),
                blocked = reader.GetInt64(3), oldestTransactionSeconds = reader.GetDouble(4),
                processCpuSeconds = process.TotalProcessorTime.TotalSeconds, workingSetBytes = process.WorkingSet64,
                managedBytes = GC.GetTotalMemory(false),
            }, Json));
        } while (await timer.WaitForNextTickAsync(token));
    }

    internal sealed class Stats
    {
        public long Committed, Done, Refused, Replays, Errors;
        public long Http429, Http401, Http5xx, HttpOther;
        public int Connections, MaximumConnections;
        public int InFlight, Maximum;
        public readonly bool[] Replayed = new bool[10000];
        private readonly long[] histogram = new long[32];
        public void Peak(int value)
        {
            int old;
            do { old = Volatile.Read(ref Maximum); if (value <= old) { return; } }
            while (Interlocked.CompareExchange(ref Maximum, value, old) != old);
        }
        public void Record(double ms) => Interlocked.Increment(ref histogram[Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(1, ms))), 0, 31)]);
        public double Percentile(double percentile)
        {
            long target = (long)Math.Ceiling(histogram.Sum() * percentile), cumulative = 0;
            for (int i = 0; i < histogram.Length; i++) { cumulative += histogram[i]; if (cumulative >= target) { return Math.Pow(2, i); } }
            return 0;
        }
    }
}
