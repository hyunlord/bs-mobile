using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    // ADR0058: the single wave evolution unlock evaluator. Data chooses the inputs, their levels and how often a named
    // Core event must have happened; what each event means stays in the systems that record it.
    public static class WaveEvolutionUnlocks
    {
        internal const string AttackKillNearGrowth = "attack-kill-near-growth";
        internal const string RepairCompleted = "repair-completed";
        internal const string HarvestNearBuilding = "harvest-near-building";
        public static readonly IReadOnlyList<string> Events = Array.AsReadOnly(new[] { AttackKillNearGrowth, RepairCompleted, HarvestNearBuilding });

        // Without explicit data the historical rule applies: every input owned at any level, the program's event gate once.
        internal static WaveEvolutionUnlock Effective(WaveEvolutionDefinition evolution, WavePrimitiveProgram program)
            => evolution.Unlock ?? new(evolution.InputIds.Select(id => new WaveUnlockInput(id)).ToArray(), new[] { new WaveUnlockEvent(program.Value("unit:event-gate", "condition"), 1) });

        internal static bool IsDefault(WaveEvolutionDefinition evolution, WavePrimitiveProgram program)
        {
            if (evolution.Unlock is not { } unlock) { return true; }
            return unlock.Inputs.Length == evolution.InputIds.Length && unlock.Inputs.All(input => evolution.InputIds.Contains(input.Id, StringComparer.Ordinal) && (input.MinLevel ?? 1) <= 1 && input.RequireMaxLevel != true)
                && unlock.Events.Length == 1 && unlock.Events[0].MinCount == 1 && unlock.Events[0].Event == program.Value("unit:event-gate", "condition");
        }

        // Event counts are state only when some evolution needs more than one occurrence, so existing hashes stay unchanged.
        internal static bool CountsEvents(WaveRuntimeDefinition definition) => definition.Evolutions.Values.Any(e => e.Unlock?.Events.Any(x => x.MinCount > 1) == true);

        public static int MaxLevel(WaveGearDefinition gear) => gear.Levels?.Length ?? 1;

        internal static bool Eligible(WaveEvolutionUnlock unlock, WaveRuntimeDefinition definition, List<EquipmentState> equipment, WaveRuntimeState state)
        {
            foreach (var input in unlock.Inputs)
            {
                var owned = equipment.Find(x => x.Id == input.Id);
                if (owned is null || (input.MinLevel is { } minimum && owned.Level < minimum)) { return false; }
                if (input.RequireMaxLevel == true && owned.Level < MaxLevel(definition.Gear[input.Id])) { return false; }
            }
            foreach (var required in unlock.Events)
            {
                if (required.MinCount <= 1 ? !Latched(state, required.Event) : Count(state, required.Event) < required.MinCount) { return false; }
            }
            return true;
        }

        internal static void Record(WaveRuntimeState state, string name)
        {
            switch (name)
            {
                case AttackKillNearGrowth: state.BladePlotKill = true; break;
                case RepairCompleted: state.RepairCompleted = true; break;
                case HarvestNearBuilding: state.HarvestNearBuilding = true; break;
                default: throw new ArgumentException("Unsupported evolution history event.");
            }
            if (state.CountUnlockEvents) { state.UnlockEvents[name] = Count(state, name) + 1; }
        }

        private static bool Latched(WaveRuntimeState state, string name) => name switch
        {
            AttackKillNearGrowth => state.BladePlotKill,
            RepairCompleted => state.RepairCompleted,
            HarvestNearBuilding => state.HarvestNearBuilding,
            _ => throw new ArgumentException("Unsupported evolution history gate.")
        };

        private static int Count(WaveRuntimeState state, string name) => state.UnlockEvents.TryGetValue(name, out var count) ? count : 0;
    }
}
