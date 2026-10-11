using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SowSiege.Core
{
    public interface IBufferedRunView : IReadOnlyRunView
    {
        RunFrame CaptureFrame(RunSnapshotBuffer buffer);
    }
    public interface IBufferedWaveRunView : IWaveRunView
    {
        WaveRuntimeFrame? CaptureWaveRuntime(WaveSnapshotBuffer buffer);
    }
    /// <summary>Caller-owned enemy storage. Captured lists remain valid until this buffer is captured or prepared again. Use distinct buffers for retained previous/current frames. The detached capture API never borrows this storage.</summary>
    public sealed class RunSnapshotBuffer
    {
        internal SnapshotList<EnemyView> Enemies { get; }
        public int EnemyCapacity => Enemies.Capacity;
        public RunSnapshotBuffer(int enemyCapacity) { Enemies = new(enemyCapacity); }
        /// <summary>Explicit allocation outside the capture loop; invalidates previously borrowed lists.</summary>
        public void PrepareEnemyCapacity(int enemyCapacity) => Enemies.Prepare(enemyCapacity);
    }
    /// <summary>Caller-owned wave enemy and counter storage. Borrowed values remain valid until this buffer is captured or prepared again. Enemy capacity is explicit; counter storage grows only when its key count exceeds capacity.</summary>
    public sealed class WaveSnapshotBuffer
    {
        internal SnapshotList<WaveEnemyView> Enemies { get; }
        internal SnapshotCounters Counters { get; } = new();
        public int EnemyCapacity => Enemies.Capacity;
        public WaveSnapshotBuffer(int enemyCapacity) { Enemies = new(enemyCapacity); }
        /// <summary>Copies every borrowed field so a public export can outlive buffer reuse. Other frame fields are already detached immutable snapshots.</summary>
        public static WaveRuntimeFrame Detach(WaveRuntimeFrame frame)
        {
            if (frame is null) { throw new ArgumentNullException(nameof(frame)); }
            var enemies = new WaveEnemyView[frame.Enemies.Count];
            for (var i = 0; i < enemies.Length; i++) { enemies[i] = frame.Enemies[i]; }
            var counters = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in frame.Counters) { counters.Add(pair.Key, pair.Value); }
            return frame with { Enemies = Array.AsReadOnly(enemies), Counters = new ReadOnlyDictionary<string, long>(counters) };
        }
        /// <summary>Explicit allocation outside the capture loop; invalidates previously borrowed lists.</summary>
        public void PrepareEnemyCapacity(int enemyCapacity) => Enemies.Prepare(enemyCapacity);
    }
    internal sealed class SnapshotCounters : IReadOnlyDictionary<string, long>
    {
        private KeyValuePair<string, long>[] entries = Array.Empty<KeyValuePair<string, long>>();
        public int Count { get; private set; }
        internal void Capture(SortedDictionary<string, long> source)
        {
            if (source.Count > entries.Length) { Array.Resize(ref entries, Math.Max(source.Count, entries.Length << 1)); }
            if (Count > source.Count) { Array.Clear(entries, source.Count, Count - source.Count); }
            var index = 0;
            foreach (var pair in source) { entries[index++] = pair; }
            Count = source.Count;
        }
        public bool TryGetValue(string key, out long value)
        {
            if (key is null) { throw new ArgumentNullException(nameof(key)); }
            var low = 0; var high = Count - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                var comparison = StringComparer.Ordinal.Compare(entries[middle].Key, key);
                if (comparison == 0) { value = entries[middle].Value; return true; }
                if (comparison < 0) { low = middle + 1; } else { high = middle - 1; }
            }
            value = 0; return false;
        }
        public long this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
        public bool ContainsKey(string key) => TryGetValue(key, out _);
        public IEnumerable<string> Keys { get { for (var i = 0; i < Count; i++) { yield return entries[i].Key; } } }
        public IEnumerable<long> Values { get { for (var i = 0; i < Count; i++) { yield return entries[i].Value; } } }
        public IEnumerator<KeyValuePair<string, long>> GetEnumerator()
        { for (var i = 0; i < Count; i++) { yield return entries[i]; } }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    internal sealed class SnapshotList<T> : IReadOnlyList<T> where T : struct
    {
        internal T[] Storage { get; private set; }
        public int Count { get; private set; }
        internal int Capacity => Storage.Length;
        internal SnapshotList(int capacity)
        {
            if (capacity < 0) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
            Storage = new T[capacity];
        }
        internal void Prepare(int capacity)
        {
            if (capacity < 0) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
            if (capacity > Storage.Length) { Storage = new T[capacity]; }
            else { Array.Clear(Storage, 0, Count); }
            Count = 0;
        }
        internal void Begin(int count)
        {
            if (count > Capacity) { throw new InvalidOperationException("Snapshot enemy capacity exceeded; prepare capacity before capture."); }
            if (Count > count) { Array.Clear(Storage, count, Count - count); }
            Count = count;
        }
        public T this[int index] => index >= 0 && index < Count ? Storage[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<T> GetEnumerator()
        {
            for (var i = 0; i < Count; i++) { yield return Storage[i]; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
