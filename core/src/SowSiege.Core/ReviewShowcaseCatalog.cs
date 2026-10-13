using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{
    public sealed record WaveShowcaseArena(string EnemyId, int SpawnPeriodTicks, int EnemyCap, int EnemySpeed, int EnemyHealth, int DurationTicks, int EnemyDamage, int SuppressedUntilTick, int BaseExperience, int SpawnRadius = 0, int RecycleRadius = 0, int EnemyBodyWidth = 0);
    public sealed record WaveShowcaseCase(string Id, string DesignRef, string ProposedContract, string Stage, string Classification, WaveGearDefinition Gear, WaveShowcaseArena Arena);
    public sealed record WaveCombinedShowcaseCase(WaveShowcaseCase[] Cases, WaveShowcaseArena Arena, int LordHealth, int WeaponSlots, int ToolSlots, int CaptureStartTick, string? SecondaryEnemyId = null, int SecondaryEnemyHealth = 0, int SecondaryEnemyFirstSpawnTick = 0);
    public static class ReviewShowcaseCatalog
    {
        public static string Hash(ContentCatalog catalog) => PortableStateCodec.HashCatalog(catalog);
        public static ContentCatalog CreateCombined(ContentCatalog baseline, WaveCombinedShowcaseCase review)
        {
            if (review.Cases is null || review.Cases.Length == 0 || review.LordHealth <= 0 ||
                review.WeaponSlots < 1 || review.ToolSlots < 1 || review.CaptureStartTick < 0 ||
                review.Arena.DurationTicks <= review.CaptureStartTick || review.Arena.EnemyCap < 1)
            { throw new ArgumentException("Combined review requires sources, positive slots and health, and a nonempty dense window."); }
            var gear = new Dictionary<string, WaveGearDefinition>(StringComparer.Ordinal);
            var weapons = new Dictionary<string, WeaponDefinition>(StringComparer.Ordinal);
            var tools = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
            string? startingWeapon = null;
            string? startingTool = null;
            ContentCatalog? result = null;
            foreach (var entry in review.Cases)
            {
                if (entry.Stage != "evolved" || gear.ContainsKey(entry.Id))
                { throw new ArgumentException("Combined sources must be unique evolved review cases."); }
                var isolated = Create(baseline, entry with { Arena = review.Arena });
                result ??= isolated;
                gear.Add(entry.Id, isolated.WaveRuntime!.Gear[entry.Id]);
                if (baseline.Tools.TryGetValue(entry.Id, out var tool))
                { tools.Add(entry.Id, tool); startingTool ??= entry.Id; }
                else
                { weapons.Add(entry.Id, isolated.Weapons[entry.Id]); startingWeapon ??= entry.Id; }
            }
            if (weapons.Count != review.WeaponSlots || tools.Count != review.ToolSlots)
            { throw new ArgumentException("Combined source categories do not match declared review slots."); }
            var catalog = result!;
            if (review.SecondaryEnemyId is not null)
            {
                if (review.Arena.EnemyBodyWidth < 1 || review.SecondaryEnemyId == review.Arena.EnemyId || review.SecondaryEnemyHealth < 1 || review.SecondaryEnemyFirstSpawnTick < 0 ||
                    review.SecondaryEnemyFirstSpawnTick >= review.Arena.DurationTicks || !catalog.WaveRuntime!.Enemies.ContainsKey(review.SecondaryEnemyId))
                { throw new ArgumentException("Invalid secondary review enemy."); }
                var enemies = catalog.Enemies.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
                enemies[review.SecondaryEnemyId] = enemies[review.SecondaryEnemyId] with { Health = review.SecondaryEnemyHealth, Damage = review.Arena.EnemyDamage };
                var rules = catalog.WaveRuntime!.Enemies.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
                rules[review.SecondaryEnemyId] = rules[review.SecondaryEnemyId] with { FirstSpawnTick = review.SecondaryEnemyFirstSpawnTick };
                var widths = catalog.WaveRuntime.EnemySeparation!.BodyWidths.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
                widths[review.SecondaryEnemyId] = review.Arena.EnemyBodyWidth;
                catalog = catalog with { Enemies = enemies, WaveRuntime = catalog.WaveRuntime with { Enemies = rules, EnemySeparation = new(1, widths) } };
            }
            var world = catalog.Tuning.World;
            var heroes = catalog.Heroes.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
            heroes[catalog.Tuning.DefaultHero] = heroes[catalog.Tuning.DefaultHero] with { StartingTool = startingTool! };
            return catalog with
            {
                Tuning = catalog.Tuning with
                {
                    World = world with
                    {
                        Map = world.Map with { LordHealth = review.LordHealth },
                        Progression = world.Progression with
                        {
                            StartingWeapon = startingWeapon!,
                            WeaponSlots = review.WeaponSlots,
                            ToolSlots = review.ToolSlots,
                            CardCount = review.Cases.Length
                        }
                    }
                },
                Heroes = heroes,
                Weapons = weapons,
                Tools = tools,
                Runtime = catalog.Runtime is null ? null : catalog.Runtime with
                { Tuning = catalog.Runtime.Tuning with { WeaponSlots = review.WeaponSlots, ToolSlots = review.ToolSlots }, Charters = new Dictionary<string, CharterDefinition>() },
                WaveRuntime = catalog.WaveRuntime! with { Revision = "m1-review-combined-v1", Gear = gear }
            };
        }
        public static ContentCatalog Create(ContentCatalog baseline, WaveShowcaseCase review)
        {
            var wave = baseline.WaveRuntime ?? throw new ArgumentException("A wave baseline is required.");
            if (review.ProposedContract != "m1-review-composition-v1" || review.Classification != "hypothesis" ||
                review.Stage is not ("lv1" or "max" or "evolved") || review.Id != review.Gear.Id || review.Gear.DesignRef != review.DesignRef ||
                !review.DesignRef.StartsWith("data/review/m1-weapon-combat-cases.json#", StringComparison.Ordinal) ||
                review.Gear.Damage < 1 || review.Gear.Range < 1 || review.Gear.Count < 1 || review.Gear.Count > WaveAttackComposition.MaximumImpacts ||
                review.Gear.CooldownTicks < 1 || review.Arena.SpawnPeriodTicks < 1 || review.Arena.EnemyCap < 1 ||
                review.Arena.EnemyHealth < 1 || review.Arena.EnemyDamage < 1 || review.Arena.EnemySpeed < 1 || review.Arena.DurationTicks < 1 || review.Arena.SuppressedUntilTick <= review.Arena.DurationTicks || review.Arena.BaseExperience < 1 || !wave.Enemies.ContainsKey(review.Arena.EnemyId))
            { throw new ArgumentException("Invalid isolated review composition case."); }
            var gear = new Dictionary<string, WaveGearDefinition>(StringComparer.Ordinal);
            foreach (var entry in wave.Gear)
            {
                gear.Add(entry.Key, entry.Value with { Damage = 0, Range = 0, Count = 0, Capacity = 0, CooldownTicks = review.Arena.SuppressedUntilTick, Levels = null, Aim = null, Composition = null });
            }

            gear[review.Id] = review.Gear with { Levels = null };
            var tools = baseline.Tools.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
            var weapons = baseline.Weapons.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
            var hero = baseline.Heroes[baseline.Tuning.DefaultHero];
            var isTool = tools.ContainsKey(review.Id);
            var startWeapon = isTool ? baseline.Tuning.World.Progression.StartingWeapon : review.Id;
            var startTool = isTool ? review.Id : hero.StartingTool;
            if (!isTool)

            {
                weapons[review.Id] = new(review.Id, new[] { "review-only" }, new(review.Gear.Damage, review.Gear.Range, review.Gear.CooldownTicks, "melee", review.Gear.Knockback));
            }

            var heroes = baseline.Heroes.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
            heroes[hero.Id] = hero with { StartingTool = startTool };
            var rules = wave.Enemies.ToDictionary(e => e.Key, e => e.Value with { FirstSpawnTick = e.Key == review.Arena.EnemyId ? 0 : review.Arena.SuppressedUntilTick }, StringComparer.Ordinal);
            var enemies = baseline.Enemies.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
            enemies[review.Arena.EnemyId] = enemies[review.Arena.EnemyId] with { Health = review.Arena.EnemyHealth, Speed = review.Arena.EnemySpeed, Damage = review.Arena.EnemyDamage };
            var world = baseline.Tuning.World;
            return baseline with
            {
                Tuning = baseline.Tuning with
                {
                    DurationTicks = review.Arena.DurationTicks,
                    World = world with
                    {
                        Progression = world.Progression with { StartingWeapon = startWeapon, BaseExperience = review.Arena.BaseExperience },
                        Threat = world.Threat with { SpawnPeriodTicks = review.Arena.SpawnPeriodTicks, EnemyCap = review.Arena.EnemyCap }
                    }
                },
                Weapons = weapons,
                Tools = tools,
                Heroes = heroes,
                Enemies = enemies,
                WaveRuntime = wave with
                {
                    Revision = review.ProposedContract + ":" + review.Stage,
                    Gear = gear,
                    Enemies = rules,
                    Programs = null,
                    EnemySeparation = review.Arena.EnemyBodyWidth > 0 ? new(1, new Dictionary<string, int> { [review.Arena.EnemyId] = review.Arena.EnemyBodyWidth }) : wave.EnemySeparation,
                    Items = new Dictionary<string, WaveItemDefinition>(),
                    Evolutions = new Dictionary<string, WaveEvolutionDefinition>(),
                    MaterialTargets = new Dictionary<string, string>(),
                    BossSpawnTick = review.Arena.SuppressedUntilTick
                }
            };
        }
    }
}
