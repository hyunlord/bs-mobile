using System.Collections.Generic;

namespace Game.View
{
    // The common enemy hit reaction in one place (user spec 2026-10-19, after the six weapon videos were rejected): every value the
    // hit flash, knockback, death and big-hit stop use. View only; Core, its events and the replay hashes are untouched.
    public static class HitReaction
    {
        public const float Frame = 1f / 60;
        // White flash on every hit, drawn the existing way (flash toward white, outline kept). Two 60Hz frames.
        public const int FlashFrames = 2;
        // Crowd flash (user decision 2026-10-20, every weapon): of the enemies whose flash starts on one frame, the CrowdFullFlash
        // nearest the hero flash fully and the rest at CrowdDimFlash, so a wide hit (the horn hits every enemy on screen) does
        // not turn the screen white. Death bursts and knockback are unchanged.
        public const int CrowdFullFlash = 8;
        public const float CrowdDimFlash = .25f;
        // The struck enemy is pushed along the hit direction by this many screen pixels (spec 2-4) and eases back.
        public const float KnockbackPixels = 3;
        public const float KnockbackRecoverSeconds = .18f;
        // Death: the flash frames, then the body swells by DeathPopScale and fades out; gone after DeathFrames (spec 6-8).
        public const int DeathFrames = 7;
        public const float DeathPopScale = .3f;
        // Display-only hitstop for big hits, evolution attacks only (Core has no critical hits); spec 30-50 ms.
        public const float BigHitStopSeconds = .04f;
        // At most one big-hit stop per this interval, so a stream of evolved hits (orbit, patch pulses) does not stutter the screen.
        public const float BigHitStopMinIntervalSeconds = .5f;
        public const float FlashSeconds = FlashFrames * Frame;
        public const float DeathSeconds = DeathFrames * Frame;
    }

    // Ranks the enemies whose hit flash starts on the same presented frame by distance to the hero (ties by id).
    public sealed class CrowdFlash
    {
        struct Entry { public int Id, Frame; public float Distance, Time; }
        sealed class ByFrameThenDistance : IComparer<Entry>
        {
            public int Compare(Entry a, Entry b) => a.Frame != b.Frame ? a.Frame.CompareTo(b.Frame)
                : a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Id.CompareTo(b.Id);
        }
        static readonly ByFrameThenDistance Order = new ByFrameThenDistance();
        readonly Dictionary<int, Entry> members = new Dictionary<int, Entry>(256);
        readonly Dictionary<int, int> ranks = new Dictionary<int, int>(256);
        readonly List<Entry> sorted = new List<Entry>(256);
        readonly List<int> stale = new List<int>(64);
        bool dirty;

        public void Note(int id, int frame, float time, float distanceSquared)
        {
            members[id] = new Entry { Id = id, Frame = frame, Time = time, Distance = distanceSquared };
            dirty = true;
        }

        // Full strength for an enemy whose flash was not noted (no crowd known).
        public float Strength(int id)
        {
            if (!members.ContainsKey(id)) return 1;
            if (dirty) Rank();
            return ranks[id] < HitReaction.CrowdFullFlash ? 1 : HitReaction.CrowdDimFlash;
        }

        // Drops flashes noted before time (they have ended).
        public void Forget(float time)
        {
            stale.Clear();
            foreach (var pair in members) if (pair.Value.Time < time) stale.Add(pair.Key);
            for (var i = 0; i < stale.Count; i++) members.Remove(stale[i]);
            if (stale.Count > 0) dirty = true;
        }

        public void Clear() { members.Clear(); ranks.Clear(); dirty = false; }

        void Rank()
        {
            dirty = false; ranks.Clear(); sorted.Clear();
            foreach (var pair in members) sorted.Add(pair.Value);
            sorted.Sort(Order);
            var frame = int.MinValue; var rank = 0;
            for (var i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Frame != frame) { frame = sorted[i].Frame; rank = 0; }
                ranks[sorted[i].Id] = rank++;
            }
        }
    }
}
