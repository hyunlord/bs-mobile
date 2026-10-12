using System;
using SowSiege.Core;

namespace Game.View
{
    public sealed class BossPresentationTimeline
    {
        public const int HistoryLength = 32;
        public const float HitCooldownSeconds = .18f;
        public const float SlowSpeed = .25f;
        public const float RecoverySpeed = 1.75f;

        public sealed class CaptureSlot
        {
            public readonly RunSnapshotBuffer RunBuffer;
            public readonly WaveSnapshotBuffer WaveBuffer;
            public RunFrame Frame { get; internal set; }
            public WaveRuntimeFrame Wave { get; internal set; }
            public FirstPlayableFrame FirstPlayable { get; internal set; }
            internal long Sequence = -1;
            internal CaptureSlot(int capacity)
            {
                RunBuffer = new RunSnapshotBuffer(capacity);
                WaveBuffer = new WaveSnapshotBuffer(capacity);
            }
        }

        public readonly struct Sample
        {
            public readonly RunFrame Previous, Current;
            public readonly WaveRuntimeFrame Wave;
            public readonly FirstPlayableFrame FirstPlayable;
            public readonly float Alpha, VisualDeltaSeconds;
            public readonly bool Delayed;
            internal Sample(CaptureSlot previous, CaptureSlot current, float alpha, float delta, bool delayed)
            {
                Previous = previous.Frame; Current = current.Frame; Wave = current.Wave;
                FirstPlayable = current.FirstPlayable; Alpha = alpha;
                VisualDeltaSeconds = delta; Delayed = delayed;
            }
        }

        readonly CaptureSlot[] history = new CaptureSlot[HistoryLength];
        readonly Action<CaptureSlot> deliver;
        long nextSequence, deliveredSequence = -1;
        double visualTick;
        float hold, slow, cooldown;
        bool positioned;
        public int OverflowRecoveries { get; private set; }
        public bool Active => hold > 0 || slow > 0;

        public BossPresentationTimeline(int enemyCapacity, Action<CaptureSlot> deliver)
        {
            if (enemyCapacity < 0) throw new ArgumentOutOfRangeException(nameof(enemyCapacity));
            this.deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
            for (var i = 0; i < history.Length; i++) history[i] = new CaptureSlot(enemyCapacity);
        }

        public void WarmStorage(IBufferedRunView run, IBufferedWaveRunView wave)
        {
            if (nextSequence != 0) throw new InvalidOperationException("Warm history before the first captured frame.");
            for (var i = 0; i < history.Length; i++)
            {
                run.CaptureFrame(history[i].RunBuffer);
                wave.CaptureWaveRuntime(history[i].WaveBuffer);
            }
        }

        public CaptureSlot PrepareCapture()
        {
            var slot = history[(int)(nextSequence % HistoryLength)];
            if (slot.Sequence >= 0 && (slot.Sequence > deliveredSequence ||
                positioned && slot.Frame.Tick >= Math.Floor(visualTick) - 1))
            {
                DeliverThrough(nextSequence - 1);
                hold = slow = 0;
                positioned = false;
                OverflowRecoveries++;
            }
            return slot;
        }

        public void Commit(CaptureSlot slot, RunFrame frame, WaveRuntimeFrame wave, FirstPlayableFrame firstPlayable)
        {
            if (!ReferenceEquals(slot, history[(int)(nextSequence % HistoryLength)]))
                throw new InvalidOperationException("Commit must use the prepared presentation history slot.");
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (nextSequence > 0 && frame.Tick < Latest.Frame.Tick)
                throw new InvalidOperationException("Start a new presentation timeline when the run changes.");
            slot.Frame = frame; slot.Wave = wave; slot.FirstPlayable = firstPlayable;
            slot.Sequence = nextSequence++;
        }

        public void RequestHitStop(float seconds)
        {
            if (cooldown > 0 || slow > 0) return;
            hold = Math.Min(.08f, Math.Max(.04f, seconds));
            cooldown = HitCooldownSeconds;
        }

        public void RequestDeathSlow(float seconds)
        {
            slow = Math.Min(.3f, Math.Max(0, seconds));
        }

        public void CancelFeedback()
        {
            hold = slow = cooldown = 0;
        }

        public Sample Present(float liveAlpha, float realDeltaSeconds)
        {
            if (nextSequence == 0) throw new InvalidOperationException("Capture before presenting.");
            var current = Latest;
            var before = PreviousDistinct(current);
            var alpha = Math.Max(0, Math.Min(1, liveAlpha));
            var target = before.Frame.Tick + (current.Frame.Tick - before.Frame.Tick) * (double)alpha;
            if (positioned) target = Math.Max(visualTick, target);
            var seconds = Math.Max(0, realDeltaSeconds);
            var oldTick = visualTick;
            if (!positioned) { visualTick = target; oldTick = target; positioned = true; }
            cooldown = Math.Max(0, cooldown - seconds);
            var remaining = seconds;
            if (hold > 0)
            {
                var held = Math.Min(hold, remaining);
                hold -= held; remaining -= held;
            }
            var advance = 0f;
            if (slow > 0 && remaining > 0)
            {
                var slowed = Math.Min(slow, remaining);
                slow -= slowed; remaining -= slowed; advance += slowed * SlowSpeed;
            }
            if (remaining > 0) advance += remaining * (visualTick < target ? RecoverySpeed : 1);
            visualTick = Math.Min(target, visualTick + advance * current.Frame.TickRate);
            if (visualTick < target - .00001)
            {
                FindPair(visualTick, out before, out current);
            }
            // A reset live accumulator may move its requested alpha backwards. The
            // rendered sample must use the monotonic clock, not that stale alpha.
            var span = current.Frame.Tick - before.Frame.Tick;
            alpha = span > 0 ? (float)((visualTick - before.Frame.Tick) / span) : 1;
            DeliverThrough(current.Frame.Tick <= visualTick ? current.Sequence : before.Sequence);
            var delta = (float)Math.Max(0, (visualTick - oldTick) / current.Frame.TickRate);
            return new Sample(before, current, alpha, delta, visualTick < target - .00001 || Active);
        }

        CaptureSlot Latest => history[(int)((nextSequence - 1) % HistoryLength)];

        CaptureSlot PreviousDistinct(CaptureSlot current)
        {
            var oldest = Math.Max(0, nextSequence - HistoryLength);
            for (var sequence = current.Sequence - 1; sequence >= oldest; sequence--)
            {
                var candidate = history[(int)(sequence % HistoryLength)];
                if (candidate.Frame.Tick < current.Frame.Tick) return candidate;
            }
            return current;
        }

        void FindPair(double tick, out CaptureSlot previous, out CaptureSlot current)
        {
            var oldest = Math.Max(0, nextSequence - HistoryLength);
            previous = history[(int)(oldest % HistoryLength)]; current = previous;
            for (var sequence = oldest; sequence < nextSequence; sequence++)
            {
                var candidate = history[(int)(sequence % HistoryLength)];
                if (candidate.Frame.Tick <= tick)
                {
                    previous = candidate; current = candidate;
                    if (candidate.Frame.Tick == tick)
                    {
                        while (sequence + 1 < nextSequence &&
                            history[(int)((sequence + 1) % HistoryLength)].Frame.Tick == candidate.Frame.Tick)
                            current = history[(int)(++sequence % HistoryLength)];
                        previous = PreviousDistinct(current);
                        return;
                    }
                }
                else
                {
                    current = candidate;
                    while (sequence + 1 < nextSequence &&
                        history[(int)((sequence + 1) % HistoryLength)].Frame.Tick == current.Frame.Tick)
                        current = history[(int)(++sequence % HistoryLength)];
                    return;
                }
            }
        }

        void DeliverThrough(long sequence)
        {
            while (deliveredSequence < sequence)
            {
                var next = deliveredSequence + 1;
                var slot = history[(int)(next % HistoryLength)];
                if (slot.Sequence != next)
                    throw new InvalidOperationException("Presentation history was overwritten before its events were delivered.");
                deliveredSequence = next;
                deliver(slot);
            }
        }
    }
}
