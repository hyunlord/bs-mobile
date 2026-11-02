using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record ArmyAutoFixtureExpected(string Terminal, int Rounds, int AttackerK, int DefenderK, int FirstAttackerHealth);
public sealed record ArmyAutoFixture(string Name, ArmyAutoInput Input, ArmyAutoFixtureExpected Expected);
public sealed record ArmyAutoFixtureDocument(string Id, string Kind, int SchemaVersion, string Status, string NumericStatus,
    string Notes, IReadOnlyList<ArmyAutoFixture> Fixtures);
public static class ArmyAutoFixtureCli
{
    public static ArmyAutoFixtureDocument Load(string path)
    {
        var options = HostJson.CreateOptions(camelCase: true);
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false));
        return JsonSerializer.Deserialize<ArmyAutoFixtureDocument>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("Missing automatic combat fixtures.");
    }
    public static int Run(string[] args)
    {
        if (args.Length != 3) { throw new ArgumentException("army-auto-fixtures INPUT OUTPUT"); }
        var document = Load(args[1]);
        var observations = document.Fixtures.Select(f =>
        {
            var result = ArmyAuto.Resolve(f.Input);
            var expected = f.Expected;
            if (result.Combat.Terminal.ToString() != expected.Terminal || result.Combat.TickOrRound != expected.Rounds ||
                result.Combat.Troops.Sum(t => t.K) != expected.AttackerK || result.DefenderTroops.Sum(t => t.K) != expected.DefenderK ||
                (result.AttackerSlots.FirstOrDefault()?.Health ?? 0) != expected.FirstAttackerHealth)
            { throw new InvalidDataException($"Hand calculation differs: {f.Name}"); }
            return new { f.Name, bytes = Convert.ToBase64String(ArmyAuto.EncodeResult(result)), result.Combat.StateHash, result.InputHash };
        }).ToArray();
        var packet = new { schemaVersion = 1, coreAssembly = CoreAssemblyMetadata.VerifyHostBinding(), observations };
        File.WriteAllText(args[2], JsonSerializer.Serialize(packet, HostJson.CreateOptions(camelCase: true, indented: true)) + Environment.NewLine);
        return 0;
    }
}
