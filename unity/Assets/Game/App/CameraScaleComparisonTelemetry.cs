using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.App
{
    // Opt-in comparison observer. It never supplies inputs or mutates simulation state.
    public sealed class CameraScaleComparisonTelemetry
    {
        struct EnemyHistory
        {
            public int tick, continuousSince, enteredAt;
            public bool visible, touching;
        }
        public struct TickSample
        {
            public int tick, enemies, visible, contacts, recentEntrantContacts, unknownHistoryContacts, damageEvents;
            public bool foreground;
        }
        readonly Dictionary<int, EnemyHistory> history;
        readonly TickSample[] samples;
        readonly int enemyCapacity, ticksPerSecond;
        Rect viewport;
        Vector2 lord;
        float lordRadius;
        TickSample current;
        bool observing, measured;
        int lastTick = -1;
        public int Count { get; private set; }
        public int CapacityFailures { get; private set; }
        public int DiscontinuousTicks { get; private set; }
        public string ContactThresholdSource { get; set; } = "caller-supplied enemy radius + lord radius; caller must record units and source";
        public CameraScaleComparisonTelemetry(int enemyCapacity = 131072, int sampleCapacity = 30000, int ticksPerSecond = 30)
        {
            if (enemyCapacity < 1 || sampleCapacity < 1 || ticksPerSecond < 1) throw new ArgumentOutOfRangeException();
            this.enemyCapacity = enemyCapacity; this.ticksPerSecond = ticksPerSecond;
            history = new Dictionary<int, EnemyHistory>(enemyCapacity);
            samples = new TickSample[sampleCapacity];
        }
        public TickSample Sample(int index) => index >= 0 && index < Count ? samples[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public bool TryScreenHistory(int id, int tick, out bool visible, out bool enteredLastSecond)
        {
            visible = false; enteredLastSecond = false;
            if (!history.TryGetValue(id, out var value) || value.tick != tick) return false;
            visible = value.visible;
            enteredLastSecond = value.enteredAt >= 0 && tick - value.enteredAt <= ticksPerSecond;
            return tick - value.continuousSince >= ticksPerSecond || value.enteredAt >= value.continuousSince;
        }
        public bool BeginTick(int tick, Rect viewportWorld, Vector2 lordWorld, float lordBodyRadius, bool measuredWindow, bool foregroundEligible)
        {
            if (observing) throw new InvalidOperationException("End the previous tick before observing another.");
            if (tick <= lastTick) return false;
            if (lastTick >= 0 && tick != lastTick + 1) DiscontinuousTicks++;
            lastTick = tick; viewport = viewportWorld; lord = lordWorld; lordRadius = lordBodyRadius;
            measured = measuredWindow; observing = true;
            current = new TickSample { tick = tick, foreground = foregroundEligible };
            return true;
        }
        public void ObserveEnemy(int id, Rect renderedWorldBounds, Vector2 center, float bodyRadius)
        {
            if (!observing) throw new InvalidOperationException("BeginTick must precede enemy observations.");
            var known = history.TryGetValue(id, out var prior);
            // A duplicate snapshot item must not count twice or change entry history.
            if (known && prior.tick == current.tick) return;
            current.enemies++;
            var visible = renderedWorldBounds.xMax >= viewport.xMin && renderedWorldBounds.xMin <= viewport.xMax &&
                renderedWorldBounds.yMax >= viewport.yMin && renderedWorldBounds.yMin <= viewport.yMax;
            if (visible) current.visible++;
            var distance = center - lord; var radius = bodyRadius + lordRadius;
            var touching = distance.sqrMagnitude <= radius * radius;
            var continuous = known && prior.tick == current.tick - 1;
            var since = continuous ? prior.continuousSince : current.tick;
            var entry = continuous ? prior.enteredAt : -1;
            if (continuous && !prior.visible && visible) entry = current.tick;
            if (touching && (!continuous || !prior.touching))
            {
                // Initial inside observations cannot prove where this enemy was in the last second.
                var completeHistory = continuous && (current.tick - since >= ticksPerSecond || entry >= since);
                if (!completeHistory) current.unknownHistoryContacts++;
                else
                {
                    current.contacts++;
                    if (entry >= 0 && current.tick - entry <= ticksPerSecond) current.recentEntrantContacts++;
                }
            }
            if (!known && history.Count == enemyCapacity) { CapacityFailures++; return; }
            history[id] = new EnemyHistory { tick = current.tick, continuousSince = since, enteredAt = entry, visible = visible, touching = touching };
        }
        public void EndTick(int actualLordDamageEvents = 0)
        {
            if (!observing) throw new InvalidOperationException("No tick is being observed.");
            current.damageEvents = actualLordDamageEvents; observing = false;
            if (!measured) return;
            if (Count == samples.Length) { CapacityFailures++; return; }
            samples[Count++] = current;
        }
        public void Save(string directory)
        {
            if (observing) throw new InvalidOperationException("Finish the tick before saving.");
            Directory.CreateDirectory(directory);
            using (var writer = new StreamWriter(Path.Combine(directory, "camera-scale-ticks.csv")))
            {
                writer.WriteLine("tick,enemies,visibleEnemies,derivedContactEntries,recentEntrantContactEntries,unknownHistoryContactEntries,actualLordDamageEvents,foreground");
                for (var i = 0; i < Count; i++)
                {
                    var s = samples[i];
                    writer.WriteLine(FormattableString.Invariant($"{s.tick},{s.enemies},{s.visible},{s.contacts},{s.recentEntrantContacts},{s.unknownHistoryContacts},{s.damageEvents},{(s.foreground ? 1 : 0)}"));
                }
            }
            File.WriteAllText(Path.Combine(directory, "camera-scale-ticks.json"), JsonUtility.ToJson(new Manifest {
                ticksPerSecond = ticksPerSecond, samples = Count, capacityFailures = CapacityFailures, discontinuousTicks = DiscontinuousTicks,
                contactThresholdSource = ContactThresholdSource,
                eligible = Count > 0 && CapacityFailures == 0 && DiscontinuousTicks == 0 }, true));
        }
        [Serializable] sealed class Manifest
        {
            public string contract = "camera-scale-observer-v1";
            public string damageAttribution = "unavailable: lord-hit carries definition ID, not attacker instance ID; no damaging-hit entrant rate can be inferred";
            public string visibility = "supplied rendered world bounds intersect the supplied camera viewport at each Core tick; count is not a screen pixel segmentation";
            public string contactCohort = "derived caller-supplied distance-threshold ENTRY per stable enemy ID, not damage or a verified physical collision; all eligible threshold entries are denominator, entries <= one second after observed outside-to-visible transition are numerator; unknown history is excluded and reported";
            public string contactThresholdSource;
            public string eligibility = "observe warmup and every Core tick, including unmeasured intervals; filter foreground rows; zero denominator is unavailable, not zero percent; caller separately verifies replay and exact viewport geometry; performance must be recorded with capture off";
            public int ticksPerSecond, samples, capacityFailures, discontinuousTicks;
            public bool eligible;
        }
    }
}
