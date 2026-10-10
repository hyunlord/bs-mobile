using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public sealed record WavePrimitiveProgram(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Params)
    {
        public bool Has(string unit) => Params.ContainsKey(unit);
        public string Value(string unit, string parameter, string fallback = "") => Params.TryGetValue(unit, out var values) && values.TryGetValue(parameter, out var value) ? value : fallback;
        public bool Is(string unit, string parameter, string value) => Value(unit, parameter) == value;
    }

    internal sealed class WavePrimitiveModules
    {
        private readonly WaveRuntimeDefinition definition;
        private readonly Dictionary<string, WaveAttackOperations> attackOperations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WaveEnemyOperations> enemyOperations = new(StringComparer.Ordinal);
        internal IReadOnlyDictionary<string, WavePrimitiveProgram> Programs { get; private set; }
        internal WavePrimitiveModules(WaveRuntimeDefinition definition)
        {
            this.definition = definition;
            Programs = definition.Programs ?? WaveLegacyCompiler.Compile(definition);
            foreach (var program in Programs.Values) { WavePrimitiveSupport.Validate(program); }
            CompileOperations();
        }
        internal WavePrimitiveProgram this[string id]
        {
            get
            {
                if (!Programs.ContainsKey(id) && definition.Programs is null) { Programs = WaveLegacyCompiler.Compile(definition); CompileOperations(); }
                return Programs[id];
            }
        }
        private void CompileOperations()
        {
            enemyOperations.Clear(); attackOperations.Clear();
            foreach (var entry in Programs)
            {
                enemyOperations.Add(entry.Key, new(entry.Value));
                attackOperations.Add(entry.Key, new(entry.Value));
            }
        }
        internal WaveAttackOperations Attack(string id)
        {
            _ = this[id];
            return attackOperations[id];
        }
        internal WaveEnemyOperations Enemy(string id)
        {
            _ = this[id];
            return enemyOperations[id];
        }
        internal bool Is(string id, string unit, string parameter, string value) => this[id].Is(unit, parameter, value);
        internal bool Has(string id, string unit) => this[id].Has(unit);
        internal static bool Equivalent(WaveRuntimeDefinition definition)
        {
            if (definition.Programs is null) { return true; }
            var historical = WaveLegacyCompiler.Compile(definition);
            return historical.Count == definition.Programs.Count && historical.All(record => definition.Programs.TryGetValue(record.Key, out var current) && Same(record.Value, current));
        }
        private static bool Same(WavePrimitiveProgram a, WavePrimitiveProgram b) => a.Params.Count == b.Params.Count && a.Params.All(unit => b.Params.TryGetValue(unit.Key, out var values) && unit.Value.Count == values.Count && unit.Value.All(parameter => values.TryGetValue(parameter.Key, out var value) && value == parameter.Value));
    }
    internal sealed class WaveAttackOperations
    {
        internal string Shape { get; }
        internal bool DryVariant { get; }
        internal bool WetPriority { get; }
        internal bool BriefStop { get; }
        internal bool Harvest { get; }
        internal bool OwnedHarvestSource { get; }
        internal string[] HarvestSources { get; }
        internal WaveAttackOperations(WavePrimitiveProgram program)
        {
            Shape = program.Value("unit:attack-shape", "shape");
            DryVariant = program.Is("unit:attack-variant", "condition", "water-empty");
            WetPriority = program.Is("unit:attack-variant", "condition", "wet-target");
            BriefStop = program.Is("unit:status-apply", "status", "brief-stop");
            Harvest = program.Has("unit:harvest-contact");
            OwnedHarvestSource = program.Is("unit:harvest-contact", "sourceFilter", "owned-source");
            HarvestSources = program.Value("unit:harvest-contact", "sourceIds").Split('|');
        }
    }
    internal sealed class WaveEnemyOperations
    {
        internal string Target { get; }
        internal string Movement { get; }
        internal string Action { get; }
        internal bool Boss { get; }
        internal WaveEnemyOperations(WavePrimitiveProgram program)
        {
            Target = program.Value("unit:enemy-pressure", "target");
            Movement = program.Value("unit:enemy-pressure", "movement");
            Action = program.Value("unit:enemy-pressure", "action");
            Boss = program.Has("unit:boss-phases");
        }
    }

}
