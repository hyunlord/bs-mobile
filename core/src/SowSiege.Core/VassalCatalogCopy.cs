using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    // ADR0075: used only for the opt-in run. The historical path keeps its original catalog ownership.
    internal static class VassalCatalogCopy
    {
        internal static ContentCatalog Freeze(ContentCatalog source)
        {
            if (source is null) { throw new ArgumentNullException(nameof(source)); }
            return source with
            {
                Tuning = source.Tuning with
                {
                    Policies = Map(source.Tuning.Policies, value => value with { CardWeights = Map(value.CardWeights, Same) }),
                    World = source.Tuning.World with
                    {
                        Farms = source.Tuning.World.Farms with { StageTicks = Copy(source.Tuning.World.Farms.StageTicks) },
                        Progression = source.Tuning.World.Progression with { Rarities = Copy(source.Tuning.World.Progression.Rarities) },
                        Seasons = Copy(source.Tuning.World.Seasons)
                    }
                },
                Tools = Map(source.Tools, value => value with { Tags = Copy(value.Tags), AntiSynergy = Copy(value.AntiSynergy) }),
                Heroes = Map(source.Heroes, Same),
                Estates = Map(source.Estates, Same),
                Weapons = Map(source.Weapons, value => value with { Tags = Copy(value.Tags) }),
                Enemies = Map(source.Enemies, Same),
                Runtime = source.Runtime is { } runtime ? Freeze(runtime) : null,
                Experiment = source.Experiment is { } experiment ? experiment with
                {
                    Movement = experiment.Movement with { CircuitOffsets = Copy(experiment.Movement.CircuitOffsets) },
                    MixedCategoryOrder = Copy(experiment.MixedCategoryOrder)
                } : null,
                WeaponCombat = source.WeaponCombat is { } combat ? combat with
                {
                    Weapons = Map(combat.Weapons, value => value with { Levels = Copy(value.Levels) })
                } : null,
                FirstPlayable = source.FirstPlayable is { } firstPlayable ? Freeze(firstPlayable) : null,
                WaveRuntime = source.WaveRuntime is { } wave ? Freeze(wave) : null
            };
        }

        private static RuntimeCatalog Freeze(RuntimeCatalog source) => source with
        {
            Tuning = source.Tuning with { LootSources = Copy(source.Tuning.LootSources) },
            Equipment = Map(source.Equipment, value => value with { GrowthActions = Copy(value.GrowthActions), Effects = Effects(value.Effects) }),
            Charters = Map(source.Charters, value => value with { Effects = Effects(value.Effects) }),
            Items = Map(source.Items, value => value with { RequiredTags = Copy(value.RequiredTags), Effects = Effects(value.Effects) }),
            Evolutions = Map(source.Evolutions, value => value with { InputIds = Copy(value.InputIds), Effects = Effects(value.Effects) })
        };

        private static FirstPlayableDefinition Freeze(FirstPlayableDefinition source) => source with
        {
            Weapons = Map(source.Weapons, Same),
            Enemies = Map(source.Enemies, Same),
            MapEvents = source.MapEvents.Select(value => value with { ItemIds = Copy(value.ItemIds) }).ToArray(),
            EvolutionRequirements = Map(source.EvolutionRequirements, Copy),
            EvolutionGrowthRequirements = source.EvolutionGrowthRequirements is { } growth ? Map(growth, Same) : null
        };

        private static WaveRuntimeDefinition Freeze(WaveRuntimeDefinition source) => source with
        {
            Gear = Map(source.Gear, value => value with { Levels = value.Levels is { } levels ? Copy(levels) : null }),
            Items = Map(source.Items, value => value with { EquipmentIds = Copy(value.EquipmentIds) }),
            Evolutions = Map(source.Evolutions, value => value with
            {
                InputIds = Copy(value.InputIds),
                Unlock = value.Unlock is { } unlock ? unlock with { Inputs = Copy(unlock.Inputs), Events = Copy(unlock.Events) } : null
            }),
            Enemies = Map(source.Enemies, Same),
            MaterialTargets = source.MaterialTargets is { } targets ? Map(targets, Same) : null,
            Programs = source.Programs is { } programs ? Map(programs, value => value with
            {
                Params = Map<string, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>(value.Params, parameters => Map(parameters, Same))
            }) : null,
            EnemySeparation = source.EnemySeparation is { } separation ? separation with { BodyWidths = Map(separation.BodyWidths, Same) } : null,
            CardOffer = source.CardOffer is { } cards ? cards with { Cards = cards.Cards is { } entries ? Map(entries, Same) : null } : null,
            EstateLight = source.EstateLight is { } estate ? estate with
            {
                Buildings = Map(estate.Buildings, Same),
                Loadouts = Map(estate.Loadouts, Copy)
            } : null
        };

        private static RuntimeEffectDefinition[] Effects(RuntimeEffectDefinition[] values) => values.Select(value => value with { Conditions = Copy(value.Conditions) }).ToArray();
        private static T Same<T>(T value) => value;
        private static T[] Copy<T>(T[] values) => (T[])values.Clone();
        // Preserve enumeration order because catalog hashing records it separately from sorted values.
        private static Dictionary<TKey, TResult> Map<TKey, TValue, TResult>(IReadOnlyDictionary<TKey, TValue> values, Func<TValue, TResult> copy) where TKey : notnull
        {
            var comparer = values is Dictionary<TKey, TValue> dictionary ? dictionary.Comparer : EqualityComparer<TKey>.Default;
            var result = new Dictionary<TKey, TResult>(values.Count, comparer);
            foreach (var value in values) { result.Add(value.Key, copy(value.Value)); }
            return result;
        }
    }
}
