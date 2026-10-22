using System;
using System.IO;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed class ActualLordDamageTelemetry : ILordDamageObserver
    {
        struct Sample
        {
            public LordDamageObservation hit;
            public bool classified, knownHistory, visible, recentEntry, foreground;
        }
        readonly Sample[] samples;
        readonly int startTick, endTick;
        int count, pending;
        public int Count => count;
        public int Dropped { get; private set; }
        public ActualLordDamageTelemetry(int startTick, int endTick, int capacity = 16384)
        {
            if (startTick < 0 || endTick < startTick || capacity < 1) throw new ArgumentOutOfRangeException();
            this.startTick = startTick; this.endTick = endTick; samples = new Sample[capacity];
        }
        public void OnLordDamage(in LordDamageObservation observation)
        {
            if (observation.Damage <= 0 || (long)observation.Tick + 1 < startTick || (long)observation.Tick + 1 > endTick) return;
            if (count == samples.Length) { Dropped++; return; }
            samples[count++] = new Sample { hit = observation };
        }
        public void CompleteTick(int tick, CameraScaleComparisonTelemetry visibility, bool foreground)
        {
            while (pending < count && (long)samples[pending].hit.Tick + 1 <= tick)
            {
                ref var value = ref samples[pending++];
                value.classified = (long)value.hit.Tick + 1 == tick;
                value.foreground = foreground && value.classified;
                if (value.classified && value.hit.AttackerId >= 0)
                    value.knownHistory = visibility.TryScreenHistory(value.hit.AttackerId, tick, out value.visible, out value.recentEntry);
            }
        }
        public void Save(string directory)
        {
            Directory.CreateDirectory(directory);
            var known = 0; var recent = 0; var unknown = 0; var unknownOwner = 0; var unclassified = 0; var positiveDamage = 0; var foregroundEvents = 0;
            using (var writer = new StreamWriter(Path.Combine(directory, "actual-lord-damage.csv")))
            {
                writer.WriteLine("tick,attackerId,projectileId,definitionId,kind,damage,classified,knownHistory,visible,recentEntry,foreground,snapshotTick");
                for (var i = 0; i < count; i++)
                {
                    var s = samples[i]; var h = s.hit;
                    if (!s.classified) unclassified++;
                    writer.WriteLine(FormattableString.Invariant($"{h.Tick},{h.AttackerId},{h.ProjectileId},{h.DefinitionId},{h.Kind},{h.Damage},{(s.classified ? 1 : 0)},{(s.knownHistory ? 1 : 0)},{(s.visible ? 1 : 0)},{(s.recentEntry ? 1 : 0)},{(s.foreground ? 1 : 0)},{(long)h.Tick + 1}"));
                    if (!s.foreground) continue;
                    foregroundEvents++; positiveDamage += h.Damage;
                    if (h.AttackerId < 0) unknownOwner++;
                    if (!s.knownHistory) unknown++;
                    else { known++; if (s.recentEntry) recent++; }
                }
            }
            File.WriteAllText(Path.Combine(directory, "actual-lord-damage.json"), JsonUtility.ToJson(new Manifest {
                startTick = startTick, endTick = endTick, events = count, dropped = Dropped, unclassified = unclassified,
                foregroundEvents = foregroundEvents, positiveDamage = positiveDamage, knownHistoryEvents = known, recentEntryEvents = recent,
                unknownHistoryEvents = unknown, unknownOwnershipEvents = unknownOwner,
                observationsComplete = Dropped == 0 && unclassified == 0 }, true));
        }
        [Serializable] sealed class Manifest
        {
            public string contract = "actual-lord-damage-observer-v2";
            public string tickContract = "CSV tick is the original damage event tick T. snapshotTick=T+1 is the post-Advance frame used for visibility; startTick/endTick inclusively bound snapshotTick.";
            public string attribution = "Actual positive HP loss with emitting attacker instance ID. Projectile events retain shooter ID and projectile ID separately; absent diagnostic ownership is -1, never guessed.";
            public string cohort = "Foreground positive-damage events with complete attacker screen history are denominator; attacker entered the viewport <=1 Core second earlier is numerator. Unknown history/ownership excluded and counted separately. Zero denominator is unavailable. Visibility is first rendered sample after the damage tick, not sub-tick collision time.";
            public string causality = "Identical replay inputs must produce identical actual damage events across camera/warning variants. This measures changed visibility of danger, not behavioral benefit or avoided damage.";
            public int startTick, endTick, events, dropped, unclassified, foregroundEvents, positiveDamage, knownHistoryEvents, recentEntryEvents, unknownHistoryEvents, unknownOwnershipEvents;
            public bool observationsComplete;
        }
    }
}
