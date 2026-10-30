using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace SowSiege.Core
{
    internal sealed class VassalRunState
    {
        internal static readonly IReadOnlyList<string> ExperienceCategories = Array.AsReadOnly(new[] { "enemy", "growth", "irrigation", "shipment", "mission" });
        private readonly SortedDictionary<int, int> grain = new();
        private readonly Dictionary<string, int> experience = ExperienceCategories.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        private readonly SortedDictionary<int, int> groups = new();
        public VassalRunOptions Options { get; }
        public VassalRunSnapshot Snapshot => Options.Snapshot;
        public string SnapshotHash { get; }
        public IReadOnlyDictionary<int, int> GrainRemainders { get; }
        public IReadOnlyDictionary<string, int> ExperienceRemainders { get; }
        public IReadOnlyDictionary<int, int> GroupMaxHealth { get; }

        public VassalRunState(VassalRunOptions options)
        {
            Options = VassalRun.Freeze(options); SnapshotHash = VassalRunCodec.Hash(Options);
            GrainRemainders = new ReadOnlyDictionary<int, int>(grain);
            ExperienceRemainders = new ReadOnlyDictionary<string, int>(experience);
            GroupMaxHealth = new ReadOnlyDictionary<int, int>(groups);
        }
        public int Scale(int value, MetaAbility ability)
        {
            if (!Enum.IsDefined(typeof(MetaAbility), ability)) { throw new ArgumentException("Unknown vassal ability."); }
            if (value <= 0 && (ability == MetaAbility.Attack || ability == MetaAbility.Allies)) { return value; }
            if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            return checked((int)(checked((long)value * ((long)PlayerInput.Scale + Snapshot.Effects[(int)ability].Bonus)) / PlayerInput.Scale));
        }
        public int AdvanceGrain(int id, int amount)
        {
            if (id < 0) { throw new ArgumentOutOfRangeException(nameof(id)); }
            grain.TryGetValue(id, out int remainder);
            int result = Accumulate(amount, MetaAbility.Growth, remainder, out int next); grain[id] = next; return result;
        }
        public int AwardExperience(string category, int amount)
        {
            if (!experience.TryGetValue(category, out int remainder)) { throw new ArgumentException("Unknown XP category."); }
            int result = Accumulate(amount, MetaAbility.Experience, remainder, out int next); experience[category] = next; return result;
        }
        private int Accumulate(int amount, MetaAbility ability, int remainder, out int next)
        {
            if (amount < 0) { throw new ArgumentOutOfRangeException(nameof(amount)); }
            long numerator = checked((long)amount * ((long)PlayerInput.Scale + Snapshot.Effects[(int)ability].Bonus) + remainder);
            int result = checked((int)(numerator / PlayerInput.Scale)); next = (int)(numerator % PlayerInput.Scale); return result;
        }
        public int RegisterGroup(int id, int baseHealth)
        {
            if (id < 0 || baseHealth <= 0 || groups.ContainsKey(id)) { throw new ArgumentException("Invalid or repeated vassal group registration."); }
            int maximum = Scale(baseHealth, MetaAbility.Allies); groups.Add(id, maximum); return maximum;
        }
        public int GroupMaximumHealth(int id) => groups.TryGetValue(id, out int maximum) ? maximum : throw new ArgumentException("Unknown vassal group.");
        public void RemoveGrain(int id) => grain.Remove(id);
        public void RemoveGroup(int id) => groups.Remove(id);

        public void ValidateEntities(IEnumerable<int> liveGrainIds, IEnumerable<int> groupIds)
        {
            var live = new HashSet<int>(liveGrainIds); var present = new HashSet<int>(groupIds);
            if (grain.Keys.Any(id => !live.Contains(id)) || !present.SetEquals(groups.Keys)) { throw new ArgumentException("Orphan vassal runtime entity."); }
        }
        public void ValidateWorld(WaveRuntimeState state) => ValidateEntities(
            state.Work.Where(w => w.Kind == "grain" && w.Health > 0).Select(w => w.Id), state.Groups.Select(g => g.Id));
        public byte[] Encode()
        {
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.UTF8, true);
            w.Write("vassal-runtime-v1"); w.Write(1); w.Write(SnapshotHash);
            w.Write(grain.Count); foreach (var pair in grain) { w.Write(pair.Key); w.Write(pair.Value); }
            w.Write(ExperienceCategories.Count); foreach (string category in ExperienceCategories) { w.Write(category); w.Write(experience[category]); }
            w.Write(groups.Count); foreach (var pair in groups) { w.Write(pair.Key); w.Write(pair.Value); w.Write(1); }
            w.Flush(); return stream.ToArray();
        }
    }
}
