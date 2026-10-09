using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public static class MetaRunAdapter
    {
        public static ContentCatalog ProjectCatalog(ContentCatalog source, MetaCatalog? meta, MetaState? state, MetaRunPlan? plan)
        {
            if (meta is null || state is null || plan is null) { return source; }
            var chapter = meta.Chapters.Single(c => c.Id == plan.Run.ChapterId);
            var modifiers = MetaEngine.Modifiers(meta, state);
            var unlocked = new HashSet<string>(meta.InitialContentIds.Concat(state.UnlockedContentIds), StringComparer.Ordinal);
            var weapons = source.Weapons.Where(p => unlocked.Contains(p.Key)).ToDictionary(p => p.Key,
                p => p.Value with { Activation = p.Value.Activation with { Damage = Scale(p.Value.Activation.Damage, modifiers.AttackPermille) } }, StringComparer.Ordinal);
            var tools = source.Tools.Where(p => unlocked.Contains(p.Key)).ToDictionary(p => p.Key,
                p => p.Value with { Activation = p.Value.Activation with { Damage = Scale(p.Value.Activation.Damage, modifiers.AttackPermille) } }, StringComparer.Ordinal);
            if (!weapons.ContainsKey(source.Tuning.World.Progression.StartingWeapon) || source.Heroes.Values.Any(h => !tools.ContainsKey(h.StartingTool)))
            { throw new ArgumentException("Meta unlocks must include every starting weapon and tool.", nameof(meta)); }
            var enemyIds = new HashSet<string>(chapter.EnemyIds, StringComparer.Ordinal) { chapter.BossId };
            var enemies = source.Enemies.Where(p => enemyIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value with
            {
                Health = Scale(p.Value.Health, chapter.EnemyHealthPermille),
                Damage = Scale(p.Value.Damage, chapter.EnemyDamagePermille),
                Experience = Scale(p.Value.Experience, modifiers.ExperiencePermille)
            }, StringComparer.Ordinal);
            var world = source.Tuning.World;
            var projectedWorld = world with
            {
                Map = world.Map with { Width = Scale(world.Map.Width, chapter.WidthPermille), Height = Scale(world.Map.Height, chapter.HeightPermille), LordHealth = Scale(world.Map.LordHealth, modifiers.HealthPermille), LordSpeed = Scale(world.Map.LordSpeed, modifiers.MovementPermille) },
                Farms = world.Farms with { Capacity = chapter.FarmCapacity, StageTicks = world.Farms.StageTicks.Select(t => Duration(t, modifiers.GrowthPermille)).ToArray(), ExperiencePerHarvest = Scale(world.Farms.ExperiencePerHarvest, modifiers.ExperiencePermille) },
                Buildings = world.Buildings with { SiteCount = chapter.SiteCount, Spacing = chapter.SiteSpacing, TaxExperience = Scale(world.Buildings.TaxExperience, modifiers.ExperiencePermille) },
                People = world.People with { Damage = Scale(world.People.Damage, modifiers.AllyPermille), VassalHealth = Scale(world.People.VassalHealth, modifiers.AllyPermille), RecruitPeriodTicks = Duration(world.People.RecruitPeriodTicks, modifiers.GrowthPermille) },
                Threat = world.Threat with { SpawnPeriodTicks = Duration(world.Threat.SpawnPeriodTicks, chapter.ThreatPermille) }
            };
            var runtime = source.Runtime;
            if (runtime is not null)
            {
                runtime = runtime with
                {
                    Equipment = runtime.Equipment.Where(p => weapons.ContainsKey(p.Key) || tools.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                    Items = runtime.Items.Where(p => unlocked.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                    Evolutions = runtime.Evolutions.Where(p => p.Value.InputIds.All(id => weapons.ContainsKey(id) || tools.ContainsKey(id)) && (weapons.ContainsKey(p.Value.BaseId) || tools.ContainsKey(p.Value.BaseId))).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
                };
            }
            var combat = source.WeaponCombat;
            if (combat is not null)
            {
                combat = combat with
                {
                    Weapons = combat.Weapons.Where(p => weapons.ContainsKey(p.Key)).ToDictionary(p => p.Key,
                    p => p.Value with { Levels = p.Value.Levels.Select(l => l with { Damage = Scale(l.Damage, modifiers.AttackPermille) }).ToArray() }, StringComparer.Ordinal)
                };
            }
            var firstPlayable = source.FirstPlayable;
            if (firstPlayable is not null)
            {
                if (!firstPlayable.Enemies.TryGetValue(chapter.BossId, out var boss) || boss.Rank != "boss" || enemies.Count != enemyIds.Count)
                { throw new ArgumentException("Chapter requires a defined boss and enemy roster.", nameof(meta)); }
                firstPlayable = firstPlayable with
                {
                    Weapons = firstPlayable.Weapons.Where(p => weapons.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                    Enemies = firstPlayable.Enemies.Where(p => enemyIds.Contains(p.Key) && (p.Value.Rank != "boss" || p.Key == chapter.BossId)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                    MapEvents = firstPlayable.MapEvents.Select(e => e with { Experience = Scale(e.Experience, modifiers.ExperiencePermille), ItemIds = e.ItemIds.Where(id => runtime?.Items.ContainsKey(id) == true).ToArray(), RewardCount = e.ItemIds.Length > 0 && !e.ItemIds.Any(id => runtime?.Items.ContainsKey(id) == true) ? 0 : e.RewardCount }).ToArray(),
                    EvolutionRequirements = firstPlayable.EvolutionRequirements.Where(p => runtime?.Evolutions.ContainsKey(p.Key) == true).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                    EvolutionGrowthRequirements = firstPlayable.EvolutionGrowthRequirements?.Where(p => runtime?.Evolutions.ContainsKey(p.Key) == true).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                    BuildingWorkRequired = Duration(firstPlayable.BuildingWorkRequired, modifiers.GrowthPermille)
                };
            }
            return source with { Tuning = source.Tuning with { World = projectedWorld }, Weapons = weapons, Tools = tools, Enemies = enemies, Runtime = runtime, WeaponCombat = combat, FirstPlayable = firstPlayable };
        }

        public static MetaRunFacts FromInteractive(RunSummary summary, RunFrame frame, FirstPlayableFrame firstPlayable, bool abandoned) =>
            new(summary.Tick, frame.DurationTicks, summary.Survived, firstPlayable.BossDefeated, abandoned, frame.Level,
                frame.Farms.Count, frame.Buildings.Count(b => b.Built && b.Health > 0), frame.People.Sum(p => p.Members), firstPlayable.Kills, firstPlayable.Harvests);

        // Round upward so small data-defined bonuses affect integral simulation units; zero remains zero.
        private static int Scale(int value, int permille) => checked((int)(((long)value * permille + PlayerInput.Scale - 1) / PlayerInput.Scale));
        private static int Duration(int value, int permille) => Math.Max(1, checked((int)((long)value * PlayerInput.Scale / permille)));
    }
}
