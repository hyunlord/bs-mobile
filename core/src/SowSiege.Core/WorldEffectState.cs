using System;
using System.Collections.Generic;
using System.IO;

namespace SowSiege.Core
{
    internal enum WorldEffectTarget { Hero, Standing, Temporary }
    internal sealed class WorldEffectState
    {
        private readonly WorldCombatSnapshot snapshot;
        private readonly SortedDictionary<int, int> grain = new();
        private readonly SortedDictionary<string, int> experience = new(StringComparer.Ordinal);
        private readonly SortedDictionary<int, int> groups = new();
        internal WorldEffectState(WorldCombatSnapshot snapshot)
        {
            this.snapshot = snapshot;
            foreach (var category in VassalRunState.ExperienceCategories) { experience.Add(category, 0); }
        }
        internal int Bonus(MetaAbility ability, WorldEffectTarget target = WorldEffectTarget.Hero)
        {
            var effect = snapshot.Effects[(int)ability];
            var eligible = target == WorldEffectTarget.Hero ? effect.HeroEligible : target == WorldEffectTarget.Standing ? effect.StandingEligible : effect.TemporaryEligible;
            return eligible ? effect.Bonus : 0;
        }
        internal int Scale(int value, MetaAbility ability, WorldEffectTarget target = WorldEffectTarget.Hero)
        {
            if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            return checked((int)(checked((long)value * ((long)PlayerInput.Scale + Bonus(ability, target))) / PlayerInput.Scale));
        }
        internal Position AfterMove(Position origin, Position moved)
        {
            var multiplier = (long)PlayerInput.Scale + Bonus(MetaAbility.Movement);
            return new(Axis(origin.X, moved.X, multiplier), Axis(origin.Y, moved.Y, multiplier));
        }
        private static int Axis(int origin, int moved, long multiplier)
        {
            var delta = (long)moved - origin;
            return checked((int)(origin + Math.Sign(delta) * (checked(Math.Abs(delta) * multiplier) / PlayerInput.Scale)));
        }
        internal int AdvanceGrain(int id, int amount)
        {
            grain.TryGetValue(id, out var remainder);
            var result = Accumulate(amount, MetaAbility.Growth, remainder, out var next); grain[id] = next; return result;
        }
        internal int AwardExperience(string category, int amount)
        {
            if (!experience.TryGetValue(category, out var remainder)) { throw new ArgumentException("Unknown experience category."); }
            var result = Accumulate(amount, MetaAbility.Experience, remainder, out var next); experience[category] = next; return result;
        }
        private int Accumulate(int amount, MetaAbility ability, int remainder, out int next)
        {
            if (amount < 0) { throw new ArgumentOutOfRangeException(nameof(amount)); }
            var numerator = checked((long)amount * ((long)PlayerInput.Scale + Bonus(ability)) + remainder);
            next = (int)(numerator % PlayerInput.Scale); return checked((int)(numerator / PlayerInput.Scale));
        }
        internal int RegisterGroup(int id, int health)
        {
            var maximum = Scale(health, MetaAbility.Allies, WorldEffectTarget.Temporary); groups.Add(id, maximum); return maximum;
        }
        internal int GroupMaximumHealth(int id) => groups[id];
        internal void RemoveGrain(int id) => grain.Remove(id);
        internal void Write(BinaryWriter writer)
        {
            writer.Write(grain.Count); foreach (var entry in grain) { writer.Write(entry.Key); writer.Write(entry.Value); }
            writer.Write(experience.Count); foreach (var entry in experience) { writer.Write(entry.Key); writer.Write(entry.Value); }
            writer.Write(groups.Count); foreach (var entry in groups) { writer.Write(entry.Key); writer.Write(entry.Value); }
        }
    }
}
