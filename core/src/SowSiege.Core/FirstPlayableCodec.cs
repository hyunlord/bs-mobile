namespace SowSiege.Core
{
    internal sealed partial class PortableStateCodec
    {
        private void WriteFirstPlayable(FirstPlayableDefinition value)
        {
            Write("SowSiege-first-playable-v1"); Write(value.ContractVersion);
            WriteMap(value.Weapons, v => { Write(v.Form); Write(v.Speed); Write(v.Radius); Write(v.LifetimeTicks); Write(v.HitIntervalTicks); Write(v.ChainRange); Write(v.SpreadPermille); Write(v.BurstIntervalTicks); });
            WriteMap(value.Enemies, v => { Write(v.Rank); Write(v.FirstSpawnTick); Write(v.RepeatTicks); Write(v.Weight); });
            WriteList(value.MapEvents, v => { Write(v.Id); Write(v.Kind); Write(v.FirstSpawnTick); Write(v.RepeatTicks); Write(v.LifetimeTicks); Write(v.SpawnRadius); Write(v.InteractRadius); Write(v.FoodCost); Write(v.RewardCount); Write(v.HealAmount); Write(v.Experience); Write(v.Health); WriteList(v.ItemIds, Write); });
            WriteMap(value.EvolutionRequirements, v => WriteList(v, r => { Write(r.EquipmentId); Write(r.MinimumLevel); }));
            Write(value.BuildingWorkRequired); Write(value.BuildingWorkPerActivation); Write(value.MaxActiveAttacks);
            Write(value.EvolutionGrowthRequirements is not null);
            if (value.EvolutionGrowthRequirements is not null)
            {
                WriteMap(value.EvolutionGrowthRequirements, v => { Write(v.Target); Write(v.Minimum); });
            }
        }
        private void WriteFirstPlayable(FirstPlayableState value)
        {
            Write("SowSiege-first-playable-state-v1");
            WriteList(value.Attacks, v => { Write(v.Id); Write(v.ActivationId); Write(v.Source); Write(v.Form); Write(v.Origin); Write(v.Position); Write(v.Previous); Write(v.Direction); Write(v.Age); Write(v.Level); Write(v.RemainingHits); Write(v.Phase); WriteMap(v.HitTicks, Write); });
            WriteMap(value.Activations, v => { Write(v.Source); Write(v.Form); Write(v.Remaining); Write(v.HadHit); });
            WriteList(value.MapEvents, v => { Write(v.Id); Write(v.Definition); Write(v.Position); Write(v.Health); Write(v.ExpiresTick); });
            WriteMap(value.NextEnemySpawn, Write); WriteMap(value.NextEventSpawn, Write); WriteMap(value.BuildingWork, Write);
            WriteMap(value.OfferedRarities, Write); WriteMap(value.PersonActivities, Write); WriteMap(value.BossEntities, Write);
            WriteList(value.DefeatedBosses, Write); WriteMap(value.Coverage, Write); Write(value.Kills);
        }
    }
}
