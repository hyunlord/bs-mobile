using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SowSiege.Core
{
    // Explicit schema: changes to state fields require a codec-version review and coverage test.
    internal sealed partial class PortableStateCodec
    {
        private readonly BinaryWriter writer;
        private PortableStateCodec(Stream stream) { writer = new BinaryWriter(stream, Encoding.UTF8, true); }
        internal static string Hash(InteractiveSession session)
        {
            using var stream = new MemoryStream();
            var codec = new PortableStateCodec(stream);
            codec.Write("SowSiege-full-state"); codec.Write(ReplayCodec.Version);
            codec.Write(session.Options); codec.Write(session.InitialCatalogHash); codec.Write(session.Catalog); codec.Write(session.NextSequence);
            codec.Write(session.Simulation.World); codec.Write(session.State);
            codec.Write(session.Simulation.RandomState.Draws); codec.Write(session.Simulation.RandomState.Portable);
            codec.writer.Flush();
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
        }
        internal static string HashCatalog(ContentCatalog catalog)
        {
            using var stream = new MemoryStream(); var codec = new PortableStateCodec(stream);
            codec.Write("SowSiege-initial-catalog"); codec.Write(ReplayCodec.Version); codec.Write(catalog); codec.writer.Flush();
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
        }
        private void Write(int value) => writer.Write(value);
        private void Write(short value) => writer.Write(value);
        private void Write(long value) => writer.Write(value);
        private void Write(bool value) => writer.Write(value);
        private void Write(string? value)
        {
            writer.Write(value is not null); if (value is null) { return; }
            var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
        }
        private void WriteList<T>(IEnumerable<T> values, Action<T> write)
        {
            var entries = values.ToArray(); Write(entries.Length); foreach (var entry in entries) { write(entry); }
        }
        private void WriteMap<T>(IEnumerable<KeyValuePair<string, T>> values, Action<T> write)
        {
            var entries = values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(); Write(entries.Length);
            foreach (var entry in entries) { Write(entry.Key); write(entry.Value); }
        }
        private void WriteCatalogMap<T>(IEnumerable<KeyValuePair<string, T>> values, Action<T> write)
        {
            var entries = values.ToArray();
            WriteList(entries.Select(entry => entry.Key), key => Write(key));
            WriteMap(entries, write);
        }
        private void WriteMap<T>(IEnumerable<KeyValuePair<int, T>> values, Action<T> write)
        {
            var entries = values.OrderBy(pair => pair.Key).ToArray(); Write(entries.Length);
            foreach (var entry in entries) { Write(entry.Key); write(entry.Value); }
        }
        private void Write(PolicyDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.DamageMultiplier);
            Write(value.GrowthMultiplier);
            WriteCatalogMap(value.CardWeights, mapValue => { Write(mapValue); });
        }
        private void Write(MapTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Width);
            Write(value.Height);
            Write(value.CellSize);
            Write(value.LordSpeed);
            Write(value.LordHealth);
            Write(value.EstateRadius);
            Write(value.WaypointPeriodTicks);
        }
        private void Write(FarmTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Capacity);
            Write(value.Spacing);
            WriteList(value.StageTicks, item => { Write(item); });
            Write(value.HarvestRange);
            Write(value.FoodPerHarvest);
            Write(value.ExperiencePerHarvest);
            Write(value.FertilityPerKill);
            Write(value.FertilityGrowthBonus);
        }
        private void Write(BuildingTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.SiteCount);
            Write(value.Spacing);
            Write(value.Health);
            Write(value.RepairAmount);
            Write(value.Damage);
            Write(value.Range);
            Write(value.AttackCooldownTicks);
            Write(value.TaxPeriodTicks);
            Write(value.TaxExperience);
        }
        private void Write(PeopleTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.InitialPeasants);
            Write(value.MaxPeople);
            Write(value.InitialFood);
            Write(value.FoodCapacity);
            Write(value.FoodPerPerson);
            Write(value.ConsumePeriodTicks);
            Write(value.RecruitPeriodTicks);
            Write(value.DraftDurationTicks);
            Write(value.ReturnSpeed);
            Write(value.WorkerGrowthBonus);
            Write(value.Damage);
            Write(value.Range);
            Write(value.AttackCooldownTicks);
            Write(value.VassalHealth);
            Write(value.SquadSize);
        }
        private void Write(RarityDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Name);
            Write(value.Weight);
            Write(value.UpgradeAmount);
        }
        private void Write(ProgressionTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.BaseExperience);
            Write(value.ExperiencePerLevel);
            Write(value.CardCount);
            Write(value.WeaponSlots);
            Write(value.ToolSlots);
            Write(value.StartingWeapon);
            WriteList(value.Rarities, item => { Write(item); });
            Write(value.Rerolls);
            Write(value.Bans);
            Write(value.Locks);
        }
        private void Write(ThreatTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.SpawnPeriodTicks);
            Write(value.BaseSpawnCount);
            Write(value.TimeRampTicks);
            Write(value.ProsperityDivisor);
            Write(value.EnemyCap);
            Write(value.SpawnInset);
            Write(value.ContactPeriodTicks);
        }
        private void Write(LoadTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Enemies);
            Write(value.Farms);
            Write(value.Buildings);
            Write(value.People);
        }
        private void Write(SeasonDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Name);
            Write(value.DurationTicks);
            Write(value.GrowthMultiplier);
            Write(value.SpawnMultiplier);
        }
        private void Write(WorldTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Map);
            Write(value.Farms);
            Write(value.Buildings);
            Write(value.People);
            Write(value.Progression);
            Write(value.Threat);
            Write(value.Load);
            WriteList(value.Seasons, item => { Write(item); });
            Write(value.TelemetryPeriodTicks);
            Write(value.DefaultPeopleRule);
        }
        private void Write(Tuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.TickRate);
            Write(value.DurationTicks);
            Write(value.DamageRollMax);
            Write(value.DefaultHero);
            Write(value.DefaultEstate);
            WriteCatalogMap(value.Policies, mapValue => { Write(mapValue); });
            Write(value.World);
        }
        private void Write(Activation? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Damage);
            Write(value.Range);
            Write(value.CooldownTicks);
            Write(value.Shape);
            Write(value.Knockback);
        }
        private void Write(Growth? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Target);
            Write(value.Yield);
        }
        private void Write(ToolDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            WriteList(value.Tags, item => { Write(item); });
            Write(value.Activation);
            Write(value.Growth);
            Write(value.FloorRationale);
            WriteList(value.AntiSynergy, item => { Write(item); });
        }
        private void Write(HeroDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.StartingTool);
            Write(value.DamageMultiplier);
        }
        private void Write(RemainsLoopDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Capacity);
            Write(value.LifetimeTicks);
            Write(value.AbsorptionRadius);
        }
        private void Write(EstateDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.GrowthMultiplier);
            Write(value.RemainsLoop);
        }
        private void Write(WeaponDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            WriteList(value.Tags, item => { Write(item); });
            Write(value.Activation);
        }
        private void Write(EnemyDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Target);
            Write(value.Health);
            Write(value.Speed);
            Write(value.Damage);
            Write(value.Range);
            Write(value.AttackCooldownTicks);
            Write(value.Experience);
        }
        private void Write(LootSourceDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Kind);
            Write(value.Weight);
            Write(value.FoodCost);
        }
        private void Write(RuntimeTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.WeaponSlots);
            Write(value.ToolSlots);
            Write(value.CharterSlots);
            Write(value.RecentExperienceWindowTicks);
            Write(value.ExperienceWeightDivisor);
            Write(value.LootPeriodTicks);
            Write(value.LootSpawnRadius);
            Write(value.LootPickupRadius);
            Write(value.MaxGroundLoot);
            Write(value.LootQuantity);
            WriteList(value.LootSources, item => { Write(item); });
        }
        private void Write(GrowthActionDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Target);
            Write(value.Operation);
            Write(value.Yield);
            Write(value.DurationTicks);
        }
        private void Write(RuntimeCondition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Kind);
            Write(value.Value);
            Write(value.Minimum);
        }
        private void Write(RuntimeEffectDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Trigger);
            Write(value.Operation);
            Write(value.Subject);
            Write(value.Amount);
            Write(value.Radius);
            Write(value.DurationTicks);
            Write(value.FoodCost);
            WriteList(value.Conditions, item => { Write(item); });
        }
        private void Write(EquipmentRuntimeDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            WriteList(value.GrowthActions, item => { Write(item); });
            WriteList(value.Effects, item => { Write(item); });
        }
        private void Write(CharterDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.PolicyCategory);
            WriteList(value.Effects, item => { Write(item); });
        }
        private void Write(ItemDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            WriteList(value.RequiredTags, item => { Write(item); });
            WriteList(value.Effects, item => { Write(item); });
        }
        private void Write(EvolutionDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Kind);
            WriteList(value.InputIds, item => { Write(item); });
            Write(value.BaseId);
            WriteList(value.Effects, item => { Write(item); });
        }
        private void Write(RuntimeCatalog? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Tuning);
            WriteCatalogMap(value.Equipment, mapValue => { Write(mapValue); });
            WriteCatalogMap(value.Charters, mapValue => { Write(mapValue); });
            WriteCatalogMap(value.Items, mapValue => { Write(mapValue); });
            WriteCatalogMap(value.Evolutions, mapValue => { Write(mapValue); });
        }
        private void Write(CircuitOffset? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.X);
            Write(value.Y);
        }
        private void Write(MovementTuning? value)
        {
            Write(value is not null); if (value is null) { return; }
            WriteList(value.CircuitOffsets, item => { Write(item); });
            Write(value.DecisionPeriodTicks);
            Write(value.EvadeRange);
            Write(value.EvadeStep);
        }
        private void Write(ExperienceCurve? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Base);
            Write(value.Linear);
            Write(value.Quadratic);
        }
        private void Write(ExperimentDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Movement);
            Write(value.Experience);
            WriteList(value.MixedCategoryOrder, item => { Write(item); });
        }
        private void Write(WeaponLevelDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Level);
            Write(value.Damage);
            Write(value.Range);
            Write(value.CooldownTicks);
            Write(value.Count);
            Write(value.Pierce);
            Write(value.Knockback);
        }
        private void Write(WeaponCombatWeaponDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.AttackModel);
            Write(value.BeamHalfWidth);
            WriteList(value.Levels, item => { Write(item); });
        }
        private void Write(WeaponCombatDefinition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.ContractVersion);
            WriteCatalogMap(value.Weapons, mapValue => { Write(mapValue); });
        }
        private void Write(ContentCatalog? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Tuning);
            WriteCatalogMap(value.Tools, mapValue => { Write(mapValue); });
            WriteCatalogMap(value.Heroes, mapValue => { Write(mapValue); });
            WriteCatalogMap(value.Estates, mapValue => { Write(mapValue); });
            WriteCatalogMap(value.Weapons, mapValue => { Write(mapValue); });
            WriteCatalogMap(value.Enemies, mapValue => { Write(mapValue); });
            Write(value.Runtime);
            Write(value.Experiment);
            Write(value.WeaponCombat);
            if (value.FirstPlayable is not null) { WriteFirstPlayable(value.FirstPlayable); }
        }
        private void Write(EffectCounter? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Count);
            Write(value.Total);
            Write(value.First.HasValue); if (value.First.HasValue) { Write(value.First.Value); }
            Write(value.Last.HasValue); if (value.Last.HasValue) { Write(value.Last.Value); }
        }
        private void Write(Position value)
        {
            Write(value.X);
            Write(value.Y);
        }
        private void Write(RuntimeEntityState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Shield);
            Write(value.ShieldUntil);
            Write(value.PauseUntil);
            Write(value.HoldUntil);
            Write(value.RestSince);
            Write(value.HasReturned);
            Write(value.ArrivalGuardUsed);
            Write(value.Waypoint.HasValue); if (value.Waypoint.HasValue) { Write(value.Waypoint.Value); }
            Write(value.Facing);
        }
        private void Write(GroundLoot? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Source);
            Write(value.Item);
            Write(value.Position);
        }
        private void Write(LootAcquisition? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Tick);
            Write(value.SourceKind);
            Write(value.SourceId);
            Write(value.SourceEntityId);
            Write(value.ItemId);
            Write(value.Quantity);
            Write(value.StackAfter);
            Write(value.FoodPaid);
        }
        private void Write(EvolutionActivation? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Tick);
            Write(value.EvolutionId);
            Write(value.BaseId);
        }
        private void Write(ExperienceEvent? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Tick);
            Write(value.Category);
            Write(value.Amount);
        }
        private void Write(RuntimeState? value)
        {
            Write(value is not null); if (value is null) { return; }
            WriteMap(value.Charters, mapValue => { Write(mapValue); });
            WriteMap(value.Items, mapValue => { Write(mapValue); });
            WriteList(value.Evolutions, item => { Write(item); });
            WriteMap(value.Effects, mapValue => { Write(mapValue); });
            WriteMap(value.Entities, mapValue => { Write(mapValue); });
            WriteMap(value.Growth, outer => { WriteMap(outer, inner => { Write(inner); }); });
            WriteList(value.GroundLoot, item => { Write(item); });
            WriteList(value.Loot, item => { Write(item); });
            WriteList(value.EvolutionEvents, item => { Write(item); });
            WriteList(value.Experience, item => { Write(item); });
        }
        private void Write(MovementSample? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Tick);
            Write(value.LordX);
            Write(value.LordY);
            Write(value.EstateX);
            Write(value.EstateY);
            Write(value.DistanceSquared);
        }
        private void Write(FarmWaitEvent? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.FarmId);
            Write(value.Episode);
            Write(value.RipeTick);
            Write(value.EndTick);
            Write(value.EndKind);
            Write(value.ObservedWaitTicks);
            Write(value.Censored);
        }
        private void Write(RipeEpisode? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Episode);
            Write(value.Tick);
        }
        private void Write(ExperimentState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.CircuitIndex);
            Write(value.FollowingCircuit);
            WriteList(value.MovementTrace, item => { Write(item); });
            WriteList(value.Samples, item => { Write(item); });
            WriteList(value.Waits, item => { Write(item); });
            WriteMap(value.OpenRipe, mapValue => { Write(mapValue); });
            WriteMap(value.Episodes, mapValue => { Write(mapValue); });
            Write(value.Ended);
        }
        private void Write(Remain? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.EnemyId);
            Write(value.Position);
            Write(value.CreatedTick);
            Write(value.ExpiresTick);
        }
        private void Write(RemainsSample? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Tick);
            Write(value.Active);
            Write(value.Created);
            Write(value.Absorbed);
            Write(value.Expired);
            Write(value.Dropped);
            Write(value.FertilityTransferred);
            Write(value.FertilityConsumed);
            Write(value.GrowthBonusApplied);
            Write(value.FertilizedHarvests);
        }
        private void Write(RemainsState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Definition);
            Write(value.NextId);
            WriteList(value.Live, item => { Write(item); });
            WriteMap(value.FarmCredits, mapValue => { Write(mapValue); });
            WriteList(value.FertilizedCycles, item => { Write(item); });
            Write(value.Created);
            Write(value.Absorbed);
            Write(value.Expired);
            Write(value.Dropped);
            Write(value.FertilityTransferred);
            Write(value.FertilityConsumed);
            Write(value.GrowthBonusApplied);
            Write(value.FertilizedHarvests);
            WriteList(value.Samples, item => { Write(item); });
        }
        private void Write(WeaponCombatState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Facing);
        }
        private void Write(EnemyState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Definition);
            Write(value.Position);
            Write(value.Health);
            Write(value.AttackTick);
            Write(value.LastTarget);
            Write(value.TargetPosition);
            Write(value.TargetRefreshTick);
            Write(value.TargetId);
        }
        private void Write(FarmState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Position);
            Write(value.Source);
            Write(value.Stage);
            Write(value.Progress);
            Write(value.Fertility);
        }
        private void Write(BuildingState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Position);
            Write(value.Source);
            Write(value.Built);
            Write(value.Health);
            Write(value.AttackTick);
        }
        private void Write(PersonState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Position);
            Write(value.Role);
            Write(value.Members);
            Write(value.Destination);
            Write(value.Source);
            Write(value.DutyUntil);
            Write(value.AttackTick);
            Write(value.Health);
        }
        private void Write(EquipmentState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Level);
            Write(value.ReadyTick);
        }
        private void Write(ToolLedger? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.ActivationDamage);
            Write(value.GrowthProduced);
            Write(value.GrowthDamage);
            Write(value.Activations);
        }
        private void Write(TimeSample? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Tick);
            Write(value.Season);
            Write(value.Level);
            Write(value.Enemies);
            Write(value.Farms);
            Write(value.Buildings);
            Write(value.People);
            Write(value.WeaponDamage);
            Write(value.ToolDamage);
            Write(value.GrowthDamage);
            Write(value.KillExperience);
            Write(value.HarvestExperience);
            Write(value.TaxExperience);
            Write(value.Food);
            Write(value.LordHealth);
            Write(value.AllyDamage);
        }
        private void Write(CardChoice? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Tick);
            WriteList(value.Offered, item => { Write(item); });
            Write(value.Chosen);
            Write(value.Rarity);
            Write(value.Level);
        }
        private void Write(WorldState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Runtime);
            Write(value.Experiment);
            Write(value.Remains);
            Write(value.WeaponCombat);
            Write(value.Tick);
            Write(value.Season);
            Write(value.NextId);
            Write(value.Lord);
            Write(value.Destination);
            Write(value.Estate);
            Write(value.LordHealth);
            Write(value.Food);
            Write(value.Level);
            Write(value.Experience);
            Write(value.KillExperience);
            Write(value.HarvestExperience);
            Write(value.TaxExperience);
            Write(value.WeaponDamage);
            Write(value.AllyDamage);
            WriteList(value.PendingCards, item => { Write(item); });
            Write(value.LockedCard);
            WriteList(value.BannedCards, item => { Write(item); });
            Write(value.Rerolls);
            Write(value.Bans);
            Write(value.Locks);
            Write(value.EstateTicks);
            Write(value.SpawnedEnemies);
            Write(value.Harvests);
            Write(value.Ruins);
            Write(value.Rebuilds);
            Write(value.DeathCause);
            WriteList(value.Enemies, item => { Write(item); });
            WriteList(value.Farms, item => { Write(item); });
            WriteList(value.Buildings, item => { Write(item); });
            WriteList(value.People, item => { Write(item); });
            WriteList(value.Equipment, item => { Write(item); });
            WriteMap(value.Tools, mapValue => { Write(mapValue); });
            WriteList(value.Timeline, item => { Write(item); });
            WriteList(value.Cards, item => { Write(item); });
            if (value.FirstPlayable is not null) { WriteFirstPlayable(value.FirstPlayable); }
        }
        private void Write(RunOptions? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Seed);
            Write(value.HeroId);
            Write(value.EstateId);
            Write(value.Policy);
            Write(value.PeopleRule);
            Write(value.Scenario);
            Write(value.ManualCards);
            Write(value.Movement);
        }
        private void Write(InteractiveOptions? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Run);
            Write((int)value.InitialAimMode);
            Write(value.DataHash);
        }
        private void Write(WorldPoint value)
        {
            Write(value.X);
            Write(value.Y);
        }
        private void Write(PresentationEvent? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.Id);
            Write(value.Tick);
            Write((int)value.Kind);
            Write(value.SourceId);
            Write(value.Origin);
            Write(value.Direction);
            Write(value.Shape);
            Write(value.Range);
            Write(value.Amount);
            WriteList(value.Endpoints, item => { Write(item); });
            WriteList(value.HitEntityIds, item => { Write(item); });
            Write(value.Geometry);
        }
        private void Write(AttackGeometry? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write(value.InnerRadius); Write(value.BeamHalfWidth); Write(value.Count); Write(value.Pierce);
            WriteList(value.RayEnds, item => { Write(item); });
        }
        private void Write(InteractiveState? value)
        {
            Write(value is not null); if (value is null) { return; }
            Write((int)value.Aim);
            Write(value.Invulnerable);
            Write(value.SpawnPermille);
            Write(value.NextEventId);
            WriteList(value.Events, item => { Write(item); });
        }
        private void Write(PortableRandom? value)
        {
            Write(value is not null); if (value is null) { return; }
            WriteList(value.Words, item => { Write(item); });
            Write(value.Index);
            Write(value.Partner);
            Write(value.Draws);
        }
    }
}
