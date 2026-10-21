using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;

if (args.Length != 1 || Directory.Exists(args[0])) { throw new ArgumentException("Run from repository root with one NEW output directory."); }
Directory.CreateDirectory(args[0]);
const int start = 13500, intendedEnd = 14400;
var catalog = ContentLoader.Load("artifacts/benchmarks/c05/data", profileName: "wave-1a");
var originalBytes = File.ReadAllBytes("benchmarks/wave-c05/run.ssreplay");
if (Convert.ToHexString(SHA256.HashData(originalBytes)) != "F8D65A26EFA2D7CAAD0EDEEC459418F1E269F7AED80980A8C7142CD7481CD9E5") { throw new InvalidDataException("C05 source bytes changed."); }
using var originalStream = new MemoryStream(originalBytes);
var original = ReplayCodec.Read(originalStream);
foreach (var candidate in new[] { 1, 2 })
{
    var name = "dense-approach-candidate" + candidate;
    var destination = Path.Combine(args[0], name + ".ssreplay");
    var session = new InteractiveSession(catalog, original.Header.Options);
    var observer = new Observer(); session.LordDamageObserver = observer;
    var ticks = new List<(int Tick, int Enemies, int Health, int X, int Y)>();
    string prefixHash;
    using (var output = File.Create(destination))
    {
        ReplayCodec.WriteHeader(output, original.Header);
        foreach (var command in original.Commands)
        {
            if (command.Tick >= start) { break; }
            session.Apply(command); ReplayCodec.WriteCommand(output, command);
        }
        var returnPoint = session.View.CaptureFrame().Lord.Position;
        prefixHash = session.ComputeStateHash();
        ReplayCodec.WriteCheckpoint(output, new(session.NextSequence, start, prefixHash));
        while (session.View.Status != RunStatus.Completed)
        {
            var frame = session.View.CaptureFrame();
            if (frame.Tick >= intendedEnd) { break; }
            ReplayCommand command;
            if (frame.Status == RunStatus.AwaitingCard)
            {
                var cards = session.View.CaptureCards();
                command = new(session.NextSequence, frame.Tick, ReplayCommandKind.ChooseCard, CardId: cards.Cards[0]);
            }
            else
            {
                long x = 0, y = 0; var count = 0;
                foreach (var enemy in frame.Enemies)
                {
                    if (enemy.Health <= 0) { continue; }
                    x += enemy.Position.X; y += enemy.Position.Y; count++;
                }
                var returning = candidate == 2 && frame.Tick >= start + 50;
                var dx = returning ? returnPoint.X - frame.Lord.Position.X : count == 0 ? 0 : x / count - frame.Lord.Position.X;
                var dy = returning ? returnPoint.Y - frame.Lord.Position.Y : count == 0 ? 0 : y / count - frame.Lord.Position.Y;
                var length = Math.Max(Math.Abs(dx), Math.Abs(dy));
                var move = length <= 200 ? default : new PlayerInput((short)(dx * 1000 / length), (short)(dy * 1000 / length));
                command = new(session.NextSequence, frame.Tick, ReplayCommandKind.Advance, move);
            }
            session.Apply(command); ReplayCodec.WriteCommand(output, command);
            if (command.Kind != ReplayCommandKind.Advance) { continue; }
            var next = session.View.CaptureFrame();
            ticks.Add((next.Tick, next.Enemies.Count, next.Lord.Health, next.Lord.Position.X, next.Lord.Position.Y));
        }
        var final = session.View.CaptureFrame();
        ReplayCodec.WriteEnd(output, new(session.NextSequence, final.Tick, final.Lord.Health <= 0 ? ReplayEndKind.Death : ReplayEndKind.Quit, session.ComputeStateHash()));
    }
    using var verificationStream = File.OpenRead(destination);
    var document = ReplayCodec.Read(verificationStream);
    var verified = ReplayRunner.Verify(catalog, original.Header.Options.DataHash, document);
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination)));
    var expected = candidate == 1 ? "4A40930B3D4FCEB80FBAEB06F7C5922F8716A2308BD750A4C79089874852E23F" : "07F211D9D8A1E7A22110D7438C29E69106E6BF619CCE681B2B89C28527B9CEC6";
    if (hash != expected) { throw new InvalidDataException("Candidate bytes changed: " + name + " " + hash); }
    var hits = observer.Hits.Where(hit => hit.Tick >= start).ToArray();
    File.WriteAllLines(Path.Combine(args[0], name + "-ticks.csv"), new[] { "tick,enemies,health,lordX,lordY" }.Concat(ticks.Select(t => FormattableString.Invariant($"{t.Tick},{t.Enemies},{t.Health},{t.X},{t.Y}"))));
    File.WriteAllLines(Path.Combine(args[0], name + "-hits.csv"), new[] { "tick,attackerId,projectileId,definitionId,kind,damage,originX,originY,lordX,lordY" }.Concat(hits.Select(h => FormattableString.Invariant($"{h.Tick},{h.AttackerId},{h.ProjectileId},{h.DefinitionId},{h.Kind},{h.Damage},{h.Origin.X},{h.Origin.Y},{h.Lord.X},{h.Lord.Y}"))));
    var summary = new { name, sha256 = hash, startTick = start, intendedEndTick = intendedEnd, prefixHash, verified, minEnemies = ticks.Min(t => t.Enemies), maxEnemies = ticks.Max(t => t.Enemies), positiveDamageEvents = hits.Length, damage = hits.Sum(h => h.Damage) };
    File.WriteAllText(Path.Combine(args[0], name + ".json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(summary));
}

internal sealed class Observer : ILordDamageObserver
{
    internal readonly List<LordDamageObservation> Hits = new();
    public void OnLordDamage(in LordDamageObservation observation) => Hits.Add(observation);
}
