using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Host;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldMeasurementAttribute : FactAttribute
{
    public WorldMeasurementAttribute()
    {
        if (Environment.GetEnvironmentVariable("WORLD_OPERATIONS_MEASURE") != "1")
        {
            Skip = "Explicit opt-in: WORLD_OPERATIONS_MEASURE=1";
        }
    }
}

public sealed partial class WorldOperationsMeasurements
{
    private static readonly CancellationToken None = CancellationToken.None;
    private sealed class Clock : TimeProvider
    {
        public long Now { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
    private sealed class Factory(string postgres, Clock clock, int batch = 128) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prototype:Enabled"] = "true", ["GameContentRoot"] = ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"] = postgres, ["ConnectionStrings:Redis"] = "127.0.0.1:1",
                ["Domains:SeedKey"] = RecruitTests.TestKeyHex, ["Auth:PseudonymKey"] = RecruitTests.TestKeyHex,
                ["World:RecoveryBatchSize"] = batch.ToString(CultureInfo.InvariantCulture)
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType == typeof(WorldRecoveryWorker)).ToArray())
                {
                    services.Remove(descriptor);
                }
            });
        }
    }
    private static int Count(string name, int maximum)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable(name), out int value) || value < 1 || value > maximum)
        {
            throw new InvalidOperationException($"Set {name} explicitly to 1..{maximum}.");
        }
        return value;
    }
    private sealed record Sample(string Path, string Case, int Index, double Milliseconds, long Bytes, int Events = 0,
        int Processed = 0, int CombatAttempts = 0, long? OldestDueAgeMs = null);
    private static string Output()
    {
        string path = Path.GetFullPath(Environment.GetEnvironmentVariable("WORLD_MEASURE_OUTPUT") ?? throw new InvalidOperationException("WORLD_MEASURE_OUTPUT is required."));
        Directory.CreateDirectory(path);
        return path;
    }
    private static async Task Save(string name, List<Sample> samples, NpgsqlDataSource source, WorldMapContent map, int population, double setup)
    {
        string output = Output();
        static string Cell(object? value) => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("\"", "\"\"") + "\"";
        await File.WriteAllLinesAsync(Path.Combine(output, name + ".csv"),
            new[] { "path,case,index,milliseconds,response_bytes,committed_events,processed_events,combat_attempts,oldest_due_age_ms" }.Concat(samples.Select(s =>
                string.Join(",", Cell(s.Path), Cell(s.Case), Cell(s.Index), Cell(s.Milliseconds), Cell(s.Bytes), Cell(s.Events), Cell(s.Processed), Cell(s.CombatAttempts), Cell(s.OldestDueAgeMs)))));
        var summaries = samples.GroupBy(s => (s.Path, s.Case)).Select(g =>
        {
            double[] values = g.Select(s => s.Milliseconds).Order().ToArray();
            double P(double p) => values[Math.Max(0, (int)Math.Ceiling(values.Length * p) - 1)];
            return string.Join(",", Cell(g.Key.Path), Cell(g.Key.Case), Cell(values.Length), Cell(P(.5)), Cell(P(.95)), Cell(P(.99)), Cell(values.Sum()), Cell(g.Sum(s => s.Events)), Cell(g.Sum(s => s.Processed)), Cell(g.Sum(s => s.CombatAttempts)), Cell(g.Max(s => s.OldestDueAgeMs)));
        });
        await File.WriteAllLinesAsync(Path.Combine(output, name + "-summary.csv"), new[] { "path,case,samples,p50_ms,p95_ms,p99_ms,total_ms,committed_events,processed_events,combat_attempts,max_oldest_due_age_ms" }.Concat(summaries));
        await using var command = source.CreateCommand("SELECT current_setting('max_connections'),version(),(SELECT count(*) FROM pg_stat_activity WHERE datname=current_database())");
        await using var rows = await command.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());
        var start = new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = ContentEstateTests.Root(), RedirectStandardOutput = true, UseShellExecute = false };
        using var git = Process.Start(start)!;
        string sha = (await git.StandardOutput.ReadToEndAsync()).Trim();
        await git.WaitForExitAsync();
        Assert.Equal(0, git.ExitCode);
        using var status = Process.Start(new ProcessStartInfo("git", "status --porcelain")
        { WorkingDirectory = ContentEstateTests.Root(), RedirectStandardOutput = true, UseShellExecute = false })!;
        string changes = await status.StandardOutput.ReadToEndAsync();
        await status.WaitForExitAsync();
        Assert.Equal(0, status.ExitCode);
        await File.WriteAllTextAsync(Path.Combine(output, name + "-metadata.json"), JsonSerializer.Serialize(new
        {
            sha, dirty = changes.Length != 0, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription, processors = Environment.ProcessorCount, cpu = await CpuModel(),
            map.Hash, map.Width, map.Height, map.ChunkSize, population, setupMilliseconds = setup,
            poolMaximum = 4, databaseMaximumConnections = rows.GetString(0), postgres = rows.GetString(1), observedDatabaseConnections = rows.GetInt64(2),
            transport = "ASP.NET TestServer; excludes TCP/TLS; production rate limiter enabled", utc = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> CpuModel()
    {
        if (OperatingSystem.IsMacOS())
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/sbin/sysctl", "-n machdep.cpu.brand_string")
            { RedirectStandardOutput = true, UseShellExecute = false })!;
            string value = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            return value;
        }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString();
    }

    [WorldMeasurement, Trait("Category", "Postgres")]
    public async Task Aoi()
    {
        int count = Count("WORLD_MEASURE_ACCOUNTS", 10000);
        Output();
        var samples = new List<Sample>();
        var setup = Stopwatch.StartNew();
        await using var db = await WorldTestDatabase.CreateAsync(4);
        using var factory = new Factory(db.ConnectionString, new Clock());
        using var client = factory.CreateClient();
        var accounts = factory.Services.GetRequiredService<AccountStore>();
        var world = factory.Services.GetRequiredService<WorldStore>();
        var map = factory.Services.GetRequiredService<WorldMapContent>();
        Assert.Equal(512, map.Width);
        Assert.Equal(512, map.Height);
        string? token = null;
        var occupied = new HashSet<WorldCoordinate>();
        var valid = map.CitySlots.ToHashSet();
        var density = new Dictionary<(int X, int Y), int>();
        for (int i = 0; i < count; i++)
        {
            var created = await accounts.CreateAsync(None);
            token ??= created.Token;
            Guid id = (await accounts.AuthenticateAsync(created.Token, None))!.Value;
            var city = (await world.FindCityAsync(id, None))!;
            Assert.True(occupied.Add(new(city.X, city.Y)));
            Assert.Contains(new WorldCoordinate(city.X, city.Y), valid);
            Assert.True(map.IsPassable(city.X, city.Y));
            var chunk = (city.X / map.ChunkSize, city.Y / map.ChunkSize);
            density[chunk] = density.GetValueOrDefault(chunk) + 1;
        }
        Assert.Equal(count, occupied.Count);
        setup.Stop();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var coordinates = (from x in Enumerable.Range(0, map.Width / map.ChunkSize)
                           from y in Enumerable.Range(0, map.Height / map.ChunkSize) select (X: x, Y: y)).ToArray();
        var dense = density.MaxBy(p => p.Value).Key;
        var empty = coordinates.First(p => !density.ContainsKey(p) && !map.InitialTargets.Any(t => t.X / map.ChunkSize == p.X && t.Y / map.ChunkSize == p.Y));
        var cases = new[] { (Name: "dense", Point: dense), (Name: "empty", Point: empty), (Name: "boundary", Point: coordinates[^1]) };
        var expected = new Dictionary<(int, int), WorldChunkView>();
        int repetitions = count <= 16 ? 4 : 25;
        foreach (var item in cases)
        {
            for (int i = 0; i <= repetitions; i++)
            {
                var timer = Stopwatch.StartNew();
                var view = await world.ChunkAsync(item.Point.X, item.Point.Y, None);
                timer.Stop();
                expected[item.Point] = view;
                samples.Add(new("store", item.Name + (i == 0 ? "-first" : "-warm"), i, timer.Elapsed.TotalMilliseconds, JsonSerializer.SerializeToUtf8Bytes(view).Length));
            }
            await Explain(db.Source, map, item.Point, item.Name);
        }
        int index = 0;
        foreach (var point in coordinates)
        {
            var timer = Stopwatch.StartNew();
            var view = await world.ChunkAsync(point.X, point.Y, None);
            timer.Stop();
            expected[point] = view;
            samples.Add(new("store", "sweep", index++, timer.Elapsed.TotalMilliseconds, JsonSerializer.SerializeToUtf8Bytes(view).Length));
        }
        var window = Stopwatch.StartNew();
        int requests = 0;
        async Task Pace(int upcoming)
        {
            if (requests + upcoming > 48)
            {
                var timer = Stopwatch.StartNew();
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, 61000 - window.Elapsed.TotalMilliseconds)));
                samples.Add(new("limiter-wait", "http", requests, timer.Elapsed.TotalMilliseconds, 0));
                requests = 0;
                window.Restart();
            }
            requests += upcoming;
        }
        async Task<Sample> Http((int X, int Y) point, string label, int i)
        {
            var timer = Stopwatch.StartNew();
            using var response = await client.GetAsync($"/v1/world/chunks/{point.X}/{point.Y}");
            var bytes = await response.Content.ReadAsByteArrayAsync();
            timer.Stop();
            response.EnsureSuccessStatusCode();
            var actual = JsonSerializer.Deserialize<WorldChunkView>(bytes, AccountStore.Json)!;
            Assert.Equal(JsonSerializer.Serialize(expected[point]), JsonSerializer.Serialize(actual));
            return new("http-testserver", label, i, timer.Elapsed.TotalMilliseconds, bytes.Length);
        }
        foreach (var item in cases)
        {
            for (int i = 0; i <= repetitions; i++)
            {
                await Pace(1);
                samples.Add(await Http(item.Point, item.Name + (i == 0 ? "-first" : "-warm"), i));
            }
        }
        index = 0;
        foreach (var point in count <= 16 ? coordinates.Take(4) : coordinates)
        {
            await Pace(1);
            samples.Add(await Http(point, "sweep", index++));
        }
        for (int i = 0; i < (count <= 16 ? 1 : 8); i++)
        {
            await Pace(4);
            samples.AddRange(await Task.WhenAll(Enumerable.Range(0, 4).Select(j => Http(dense, "concurrent4", i * 4 + j))));
        }
        await Save("aoi", samples, db.Source, map, count, setup.Elapsed.TotalMilliseconds);
    }

    private static async Task Explain(NpgsqlDataSource source, WorldMapContent map, (int X, int Y) point, string label)
    {
        await using var command = source.CreateCommand("""
            EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
            SELECT t.target_id,t.generation,t.kind,t.content_id,t.x,t.y,t.account_id,a.name
            FROM world_targets t LEFT JOIN alliance_members m ON m.account_id=t.account_id
            LEFT JOIN alliances a ON a.id=m.alliance_id AND a.status='active'
            WHERE t.world_id=$1 AND t.x >= $2 AND t.x < $3 AND t.y >= $4 AND t.y < $5
              AND NOT EXISTS(SELECT 1 FROM auth_deleted_accounts d WHERE d.account_id=t.account_id)
            ORDER BY t.x,t.y,t.target_id
            """);
        command.Parameters.AddWithValue(map.WorldId);
        command.Parameters.AddWithValue(point.X * map.ChunkSize);
        command.Parameters.AddWithValue((point.X + 1) * map.ChunkSize);
        command.Parameters.AddWithValue(point.Y * map.ChunkSize);
        command.Parameters.AddWithValue((point.Y + 1) * map.ChunkSize);
        string result = (string)(await command.ExecuteScalarAsync())!;
        await File.WriteAllTextAsync(Path.Combine(Output(), "aoi-sql-" + label + ".json"), result);
    }
}
