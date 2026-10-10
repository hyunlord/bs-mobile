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
        private readonly Dictionary<string, WaveEnemyOperations> enemyOperations = new(StringComparer.Ordinal);
        internal IReadOnlyDictionary<string, WavePrimitiveProgram> Programs { get; private set; }
        internal WavePrimitiveModules(WaveRuntimeDefinition definition)
        {
            this.definition = definition;
            Programs = definition.Programs ?? WaveLegacyCompiler.Compile(definition);
            foreach (var program in Programs.Values) { WavePrimitiveSupport.Validate(program); }
        }
        internal WavePrimitiveProgram this[string id]
        {
            get
            {
                if (!Programs.ContainsKey(id) && definition.Programs is null) { Programs = WaveLegacyCompiler.Compile(definition); }
                return Programs[id];
            }
        }
        internal WaveEnemyOperations Enemy(string id)
        {
            if (!enemyOperations.TryGetValue(id, out var operations)) { operations = new(this[id]); enemyOperations.Add(id, operations); }
            return operations;
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
