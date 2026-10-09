using System;
using System.Collections.Generic;
namespace SowSiege.Core
{
    internal sealed class WaveActivation
    {
        public int Id, PendingChildren, ExpiresTick;
        public string Source = "";
        public long RequestedDamage, HitDamage;
        public HashSet<int> HitTargets = new();
    }
    internal sealed partial class WaveRuntimeState
    {
        public int NextActivation;
        public Dictionary<int, WaveActivation> Activations = new();
        private void Metric(string name, string source, long amount)
        {
            Counters[name] = Counters.GetValueOrDefault(name) + amount;
            var key = name + ":" + source;
            Counters[key] = Counters.GetValueOrDefault(key) + amount;
        }
        public int BeginActivation(string source, int pending, int expires)
        {
            var id = NextActivation++;
            Activations.Add(id, new() { Id = id, Source = source, PendingChildren = pending, ExpiresTick = expires });
            Metric("activation-started", source, 1);
            Metric("activation-pending", source, 1);
            return id;
        }
        public void RecordDamage(int id, int requested, int dealt)
        {
            if (!Activations.TryGetValue(id, out var activation)) { throw new InvalidOperationException("Damage requires an active activation."); }
            if (dealt < 0 || requested < dealt) { throw new ArgumentOutOfRangeException(nameof(dealt)); }
            activation.RequestedDamage += requested;
            activation.HitDamage += dealt;
            Metric("damage-requested", activation.Source, requested);
            Metric("damage-dealt", activation.Source, dealt);
            Metric("overkill-waste", activation.Source, requested - dealt);
        }
        public void ResolveActivation(int id)
        {
            if (!Activations.TryGetValue(id, out var activation)) { return; }
            Metric("activation-pending", activation.Source, -1);
            Metric("activation-resolved", activation.Source, 1);
            Metric(activation.HitDamage > 0 ? "successful-activation" : "empty-activation", activation.Source, 1);
            Activations.Remove(id);
        }
        public void ResolveChild(int id)
        {
            if (!Activations.TryGetValue(id, out var activation)) { return; }
            if (activation.PendingChildren <= 0) { throw new InvalidOperationException("Activation child resolved twice."); }
            activation.PendingChildren--;
            if (activation.PendingChildren == 0) { ResolveActivation(id); }
        }
    }
}
