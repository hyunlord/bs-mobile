using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{
    public sealed record WaveShowcaseArena(string EnemyId, int SpawnPeriodTicks, int EnemyCap, int EnemySpeed, int EnemyHealth, int DurationTicks, int EnemyDamage, int SuppressedUntilTick, int BaseExperience, int SpawnRadius = 0, int RecycleRadius = 0, int EnemyBodyWidth = 0);
    public sealed record WaveShowcaseCase(string Id, string DesignRef, string ProposedContract, string Stage, string Classification, WaveGearDefinition Gear, WaveShowcaseArena Arena);
    public static class ReviewShowcaseCatalog
    {
        public static string Hash(ContentCatalog catalog) => PortableStateCodec.HashCatalog(catalog);
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
