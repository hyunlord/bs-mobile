using System.Text.Json.Nodes;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WeaponCombatContentTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("test-weapon")]
    [InlineData("missing-test-definition")]
    [InlineData("invalid-test-definition")]
    [InlineData("explicit-null")]
    [InlineData("unsafe-path")]
    [InlineData("unknown-field")]
    [InlineData("missing-weapon")]
    [InlineData("extra-weapon")]
    [InlineData("missing-level")]
    [InlineData("duplicate-level")]
    [InlineData("invalid-model")]
    [InlineData("overflow")]
    [InlineData("count-limit")]
    [InlineData("zero-damage")]
    [InlineData("shape-mismatch")]
    [InlineData("damage-regression")]
    [InlineData("cooldown-regression")]
    [InlineData("duplicate-json-key")]
    [InlineData("symlink")]
    [InlineData("null-levels")]
    [InlineData("missing-member")]
    [InlineData("damage-only")]
    [InlineData("sector-pierce")]
    [InlineData("rays-zero-width")]
    [InlineData("version")]
    public void WeaponCombatBoundaryRejectsInvalidDefinitions(string mutation)
    {
        var root = Path.Combine(Path.GetTempPath(), "weapon-combat-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "data");
            foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            var profile = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "profiles/s4b-02.json")))!;
            profile["weaponCombat"] = JsonNode.Parse("{\"contractVersion\":1,\"definitionsFile\":\"weapon-growth-test.json\"}");
            foreach (var weaponPath in Directory.GetFiles(Path.Combine(root, "weapons"), "*.json"))
            {
                var weapon = JsonNode.Parse(File.ReadAllText(weaponPath))!;
                if (weapon["growth"] is not { } growth) { continue; }
                foreach (var key in new[] { "damage", "range", "cooldownTicks", "knockback" }) { weapon["activation"]![key] = growth["levels"]![0]![key]!.DeepClone(); }
                weapon.AsObject().Remove("growth");
                File.WriteAllText(weaponPath, weapon.ToJsonString());
            }
            var weapons = new JsonObject();
            foreach (var id in profile["selection"]!["weapons"]!.AsArray())
            {
                weapons[id!.GetValue<string>()] = new JsonObject
                {
                    ["attackModel"] = "sector90",
                    ["beamHalfWidth"] = 0,
                    ["levels"] = new JsonArray(Enumerable.Range(1, 12).Select(level => (JsonNode)new JsonObject { ["level"] = level, ["damage"] = level, ["range"] = 100 + level, ["cooldownTicks"] = 30, ["count"] = 1, ["pierce"] = 0, ["knockback"] = 0 }).ToArray())
                };
            }
            if (mutation is "test-weapon" or "missing-test-definition" or "invalid-test-definition")
            {
                const string testId = "test:host_weapon";
                var primaryId = weapons.First().Key;
                var weaponFile = Directory.GetFiles(Path.Combine(root, "weapons"), "*.json").First(file => JsonNode.Parse(File.ReadAllText(file))!["id"]!.GetValue<string>() == primaryId);
                var dummy = JsonNode.Parse(File.ReadAllText(weaponFile))!;
                dummy["id"] = testId;
                Directory.CreateDirectory(Path.Combine(root, "test/weapons"));
                File.WriteAllText(Path.Combine(root, "test/weapons/host-weapon.json"), dummy.ToJsonString());
                profile["testSelection"]!["weapons"]!.AsArray().Add(testId);
                if (mutation != "missing-test-definition") { weapons[testId] = weapons.First().Value!.DeepClone(); }
                if (mutation == "invalid-test-definition") { weapons[testId]!["levels"]![0]!["count"] = 65; }
            }
            var config = new JsonObject { ["contractVersion"] = 1, ["weapons"] = weapons };
            var first = weapons.First(); var levels = first.Value!["levels"]!.AsArray();
            switch (mutation)
            {
                case "null-levels": first.Value!["levels"] = null; break;
                case "missing-member": levels[0]!.AsObject().Remove("knockback"); break;
                case "damage-only": levels[1]!["range"] = levels[0]!["range"]!.GetValue<int>(); break;
                case "sector-pierce": levels[0]!["pierce"] = 1; break;
                case "rays-zero-width": first.Value!["attackModel"] = "rays"; break;
                case "version": config["contractVersion"] = 2; break;
                case "explicit-null": profile["weaponCombat"] = null; break;
                case "unsafe-path": profile["weaponCombat"]!["definitionsFile"] = "../weapon-growth-test.json"; break;
                case "unknown-field": levels[0]!["ignored"] = 1; break;
                case "missing-weapon": weapons.Remove(first.Key); break;
                case "extra-weapon": weapons["test:unknown"] = first.Value.DeepClone(); break;
                case "missing-level": levels.RemoveAt(11); break;
                case "duplicate-level": levels[1]!["level"] = 1; break;
                case "invalid-model": first.Value["attackModel"] = "unknown"; break;
                case "overflow": levels[0]!["damage"] = 2147483648L; break;
                case "count-limit": levels[0]!["count"] = 65; break;
                case "zero-damage": levels[0]!["damage"] = 0; break;
                case "shape-mismatch": first.Value["beamHalfWidth"] = 1; break;
                case "damage-regression": levels[2]!["damage"] = 1; break;
                case "cooldown-regression": levels[2]!["cooldownTicks"] = 31; break;
            }
            File.WriteAllText(Path.Combine(root, "profiles/weapon-host-test.json"), profile.ToJsonString());
            var filePath = Path.Combine(root, "weapon-growth-test.json");
            var json = config.ToJsonString();
            if (mutation == "duplicate-json-key") { json = json.Replace("\"weapons\":{", "\"weapons\":{" + System.Text.Json.JsonSerializer.Serialize(first.Key) + ":" + first.Value!.ToJsonString() + ",", StringComparison.Ordinal); }
            File.WriteAllText(filePath, json);
            if (mutation == "symlink") { File.Move(filePath, filePath + ".original"); File.CreateSymbolicLink(filePath, filePath + ".original"); }
            if (mutation is "valid" or "test-weapon")
            {
                var loaded = ContentLoader.Load(root, false, "weapon-host-test");
                Assert.NotNull(loaded.WeaponCombat);
                Assert.Equal(loaded.Weapons.Count, loaded.WeaponCombat.Weapons.Count);
                Assert.All(loaded.WeaponCombat.Weapons.Values, weapon => Assert.Equal(12, weapon.Levels.Length));
                Assert.Null(ContentLoader.Load(root, false, "s4b-02").WeaponCombat);
                Assert.Equal(profile["selection"]!["weapons"]!.AsArray().Select(id => id!.GetValue<string>()), loaded.WeaponCombat.Weapons.Keys);
                if (mutation == "test-weapon")
                {
                    const string testId = "test:host_weapon";
                    Assert.False(loaded.WeaponCombat.Weapons.ContainsKey(testId));
                    Assert.False(loaded.Weapons.ContainsKey(testId));
                    var withTest = ContentLoader.Load(root, true, "weapon-host-test");
                    Assert.Equal(loaded.Weapons.Count + 1, withTest.WeaponCombat!.Weapons.Count);
                    Assert.True(withTest.Weapons.ContainsKey(testId));
                    var options = new RunOptions(9100, withTest.Tuning.DefaultHero, withTest.Tuning.DefaultEstate, "weapon");
                    var sim = SimulationFactory.Create(withTest, options);
                    sim.World.Enemies.Clear();
                    sim.World.Lord = new(1000, 1000);
                    sim.World.WeaponCombat!.Facing = new(0, 1);
                    var enemy = new EnemyState { Id = sim.World.AllocateId(), Position = new(1000, 1050), Health = 100 };
                    sim.World.Enemies.Add(enemy);
                    var spatial = new SpatialHash(withTest.Tuning.World.Map.CellSize);
                    spatial.Rebuild(sim.World.Enemies);
                    var combat = new CombatSystem(withTest, options, sim.World, new TrackedRandom(9100), spatial);
                    combat.Activate(new EquipmentState { Id = testId, Level = 1 }, withTest.Weapons[testId].Activation, null);
                    Assert.True(enemy.Health < 100);
                    Assert.Equal(100 - enemy.Health, sim.World.WeaponDamage);
                }
                var originalHash = ContentLoader.Hash(root, false);
                File.AppendAllText(filePath, " ");
                Assert.NotEqual(originalHash, ContentLoader.Hash(root, false));
            }
            else { Assert.ThrowsAny<Exception>(() => ContentLoader.Load(root, false, "weapon-host-test")); }
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}
