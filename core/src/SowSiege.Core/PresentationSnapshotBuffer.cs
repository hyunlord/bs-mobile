using System;
using System.Collections;
using System.Collections.Generic;

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
    /// <summary>Caller-owned wave enemy storage. Captured lists remain valid until this buffer is captured or prepared again. Capacity is never grown implicitly during capture.</summary>
    public sealed class WaveSnapshotBuffer
    {
        internal SnapshotList<WaveEnemyView> Enemies { get; }
        public int EnemyCapacity => Enemies.Capacity;
        public WaveSnapshotBuffer(int enemyCapacity) { Enemies = new(enemyCapacity); }
        /// <summary>Explicit allocation outside the capture loop; invalidates previously borrowed lists.</summary>
        public void PrepareEnemyCapacity(int enemyCapacity) => Enemies.Prepare(enemyCapacity);
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
