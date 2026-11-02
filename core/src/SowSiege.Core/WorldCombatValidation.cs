using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public static partial class WorldCombatCodec
    {
        public static void Validate(WorldCombatOptions options)
        {
            try { ValidateSnapshot(options); }
            catch (OverflowException exception) { throw new ArgumentException("World combat arithmetic overflow.", nameof(options), exception); }
        }

        private static void ValidateSnapshot(WorldCombatOptions options)
        {
            if (options?.Snapshot is null) { throw new ArgumentNullException(nameof(options)); }
            var s = options.Snapshot;
            Require(Enum.IsDefined(typeof(WorldCombatMode), s.Mode), "Unknown world combat mode.");
            CanonicalHash(s.ContractHash);
            var p = s.Provenance ?? throw new ArgumentException("Missing world provenance.");
            Identifier(p.ProfileId); CanonicalHash(p.ProfileHash); CanonicalHash(p.RulesHash); CanonicalHash(p.ContentHash);
            Require(p.RulesId == (s.Mode == WorldCombatMode.DirectPve ? "world-combat-v1" : "army-auto-v1") &&
                p.RulesVersion == 1 && p.ArithmeticVersion == 1 && p.RemainderVersion == 1 && p.OrderVersion == 1 &&
                p.TickRate > 0 && p.RngAlgorithm == PortableRandom.Algorithm, "Unsupported world combat provenance.");
            var b = s.Battlefield ?? throw new ArgumentException("Missing battlefield.");
            Require(b.MinX >= 0 && b.MinY >= 0 && b.MaxX > b.MinX && b.MaxY > b.MinY, "Invalid battlefield.");
            long width = (long)b.MaxX - b.MinX, height = (long)b.MaxY - b.MinY;
            _ = checked(width * width + height * height);
            var hero = s.Hero ?? throw new ArgumentException("Missing hero provenance.");
            Identifier(hero.Id); CanonicalHash(hero.ContentHash); Ordered(hero.Skills, x => x.Id);
            foreach (var skill in hero.Skills) { Require(skill.Version > 0, "Invalid skill version."); CanonicalHash(skill.ContentHash); }
            var input = s.Input ?? throw new ArgumentException("Missing input policy.");
            Require(input.InputGraceMilliseconds > 0 && input.MaximumChunkCommands > 0 && input.MaximumChunkCommands <= ReplayCodec.MaximumCommands &&
                input.MaximumChunkBytes > 0 && input.MaximumChunkBytes <= MaximumBytes, "Invalid input policy.");
            Ordered(s.Objectives, x => x.Id); Ordered(s.Troops, x => x.Id); Ordered(s.Cohorts, x => x.Id); Ordered(s.Markers, x => x.Id);
            var limits = s.Objectives.Where(x => x.Kind == WorldObjectiveKind.TimeLimit).ToArray();
            Require(limits.Length == 1 && limits[0].LimitTicks > 0, "Exactly one positive time limit is required.");
            int limit = limits[0].LimitTicks;
            Require(s.Mode != WorldCombatMode.DirectPve || s.Objectives.Count > 1, "Direct combat requires a completion objective.");
            int total = 0;
            foreach (var troop in s.Troops)
            {
                Require(troop.N >= 0 && troop.Slots is not null && troop.Slots.Count == troop.N && troop.N <= MaximumEntries, "Invalid troop count.");
                total = checked(total + troop.N); Require(total <= MaximumEntries, "Too many standing slots."); Stats(troop.Stats);
                for (int i = 0; i < troop.N; i++)
                {
                    var slot = troop.Slots![i]; Require(slot is not null && slot.Ordinal == i, "Duplicate or unordered standing slot.");
                    Point(b, slot!.X, slot.Y);
                }
            }
            total = 0;
            foreach (var cohort in s.Cohorts)
            {
                Identifier(cohort.EnemyId); Require(cohort.Count > 0 && cohort.Count <= MaximumEntries && cohort.SpawnTick >= 0 && cohort.SpawnTick < limit, "Invalid spawn cohort.");
                total = checked(total + cohort.Count); Require(total <= MaximumEntries, "Too many designated enemies.");
                Point(b, cohort.X, cohort.Y); Stats(cohort.Stats);
            }
            foreach (var marker in s.Markers)
            {
                Point(b, marker.X, marker.Y); Require(marker.Radius >= 0 && marker.Health > 0, "Invalid marker.");
            }
            foreach (var objective in s.Objectives)
            {
                Require(Enum.IsDefined(typeof(WorldObjectiveKind), objective.Kind), "Unknown objective.");
                switch (objective.Kind)
                {
                    case WorldObjectiveKind.DefeatDesignated:
                        Identifier(objective.TargetId);
                        var cohort = s.Cohorts.FirstOrDefault(x => x.Id == objective.TargetId);
                        Require(cohort is not null && objective.RequiredCount > 0 && objective.RequiredCount <= cohort.Count &&
                            objective.HoldTicks == 0 && objective.LimitTicks == 0, "Unachievable defeat objective.");
                        break;
                    case WorldObjectiveKind.HoldMarker:
                        Identifier(objective.TargetId);
                        Require(s.Markers.Any(x => x.Id == objective.TargetId) && objective.HoldTicks > 0 && objective.HoldTicks <= limit &&
                            objective.RequiredCount == 0 && objective.LimitTicks == 0, "Invalid hold objective.");
                        break;
                    case WorldObjectiveKind.TimeLimit:
                        Require(objective.TargetId == "" && objective.RequiredCount == 0 && objective.HoldTicks == 0, "Unexpected time-limit payload.");
                        break;
                }
            }
            var abilities = (MetaAbility[])Enum.GetValues(typeof(MetaAbility));
            Require(s.Effects is not null && s.Effects.Count == abilities.Length, "Invalid world effect count.");
            for (int i = 0; i < abilities.Length; i++)
            {
                var effect = s.Effects![i];
                Require(effect is not null && effect.Ability == abilities[i] && effect.Cap >= 0 && effect.Sources is not null && effect.Sources.Count <= MaximumEntries, "Invalid or unordered world effect.");
                long raw = 0; WorldRawEffect? previous = null;
                foreach (var source in effect!.Sources!)
                {
                    Require(source is not null && Enum.IsDefined(typeof(WorldEffectSourceKind), source.Kind) && source.Raw >= 0, "Invalid effect source.");
                    Identifier(source!.SourceId); CanonicalHash(source.ContentHash);
                    if (source.Kind == WorldEffectSourceKind.Hero)
                    {
                        Require(source.SourceId == hero.Id && source.ContentHash == hero.ContentHash ||
                            hero.Skills.Any(x => x.Id == source.SourceId && x.ContentHash == source.ContentHash), "Unknown hero effect provenance.");
                    }
                    Require(previous is null || (int)previous.Kind < (int)source.Kind || previous.Kind == source.Kind && string.CompareOrdinal(previous.SourceId, source.SourceId) < 0, "Duplicate or unordered effect source.");
                    raw = checked(raw + source.Raw); previous = source;
                }
                Require(raw <= int.MaxValue && effect.Raw == raw && effect.Bonus == Math.Min(raw, effect.Cap), "World raw sum or cap mismatch.");
            }
        }

        public static void ValidateVassals(WorldCombatOptions options, VassalRunOptions? vassals)
        {
            Validate(options);
            if (vassals is not null) { VassalRun.Validate(vassals); }
            foreach (var effect in options.Snapshot.Effects)
            {
                var sources = effect.Sources.Where(x => x.Kind == WorldEffectSourceKind.Vassal).ToArray();
                if (vassals is null) { Require(sources.Length == 0, "Vassal source requires its snapshot."); continue; }
                var expected = vassals.Snapshot.Effects[(int)effect.Ability];
                Require(sources.Length == 1 && sources[0].SourceId == vassals.Snapshot.RulesId &&
                    sources[0].ContentHash == VassalRunCodec.Hash(vassals) && sources[0].Raw == expected.Raw,
                    "World vassal provenance or raw value mismatch.");
            }
        }

        internal static void CanonicalHash(string value)
        {
            Require(value is not null && value.Length == Sha256HexCharacters && value.All(c => c >= '0' && c <= '9' || c >= 'A' && c <= 'F'), "Expected canonical uppercase SHA-256.");
        }
        private static void Identifier(string value)
        {
            Require(value is not null && value.Length > 0 && value.Length <= MaximumIdentifierCharacters &&
                value.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == ':' || c == '/' || c == '.' || c == '-'), "Noncanonical world identifier.");
        }
        private static void Ordered<T>(IReadOnlyList<T> values, Func<T, string> id) where T : class
        {
            Require(values is not null && values.Count <= MaximumEntries, "Invalid world collection.");
            string? previous = null;
            foreach (var value in values!)
            {
                Require(value is not null, "Null world collection entry."); var key = id(value!); Identifier(key);
                Require(previous is null || string.CompareOrdinal(previous, key) < 0, "Duplicate or unordered world identifier."); previous = key;
            }
        }
        private static void Point(WorldBattlefield b, int x, int y) => Require(x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY, "Position outside battlefield.");
        private static void Stats(WorldCombatStats s) => Require(s is not null && s.Health > 0 && s.Attack >= 0 && s.Defense >= 0 && s.Speed >= 0 && s.Range >= 0 && s.CooldownTicks > 0, "Invalid combat values.");
        private static void Require(bool condition, string message) { if (!condition) { throw new ArgumentException(message); } }
    }
}
