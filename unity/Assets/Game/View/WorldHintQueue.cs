using System.Globalization;

namespace Game.View
{
    public enum WorldHintKind { Ripe, HarvestExperience, CompletionExperience, Repair }

    public sealed class WorldHintQueue
    {
        public const int Capacity = 5;
        struct Hint { public WorldHintKind Kind; public long Amount; }
        readonly Hint[] pending = new Hint[Capacity];
        int head, count;
        float until;
        bool showing;
        string message;
        public int Count => count;

        public void Enqueue(WorldHintKind kind, long amount = 0)
        {
            var experience = kind == WorldHintKind.HarvestExperience || kind == WorldHintKind.CompletionExperience;
            for (var i = showing && experience ? 1 : 0; i < count; i++)
            {
                var index = (head + i) % Capacity;
                if (pending[index].Kind != kind) continue;
                if (experience) pending[index].Amount += amount;
                return;
            }
            if (count == Capacity) throw new System.InvalidOperationException("Four hint kinds must fit beside the active caption.");
            pending[(head + count) % Capacity] = new Hint { Kind = kind, Amount = amount };
            count++;
        }

        public string Present(float now, float duration)
        {
            if (showing && now >= until)
            { head = (head + 1) % Capacity; count--; showing = false; message = null; }
            if (count == 0) return null;
            if (!showing)
            {
                showing = true; until = now + duration;
                var hint = pending[head];
                message = hint.Kind == WorldHintKind.Ripe ? "익음" : hint.Kind == WorldHintKind.Repair ? "수리 중" :
                    (hint.Kind == WorldHintKind.HarvestExperience ? "수확 +" : "완료 +") + hint.Amount.ToString(CultureInfo.InvariantCulture) + " 경험치";
            }
            return message;
        }

        public void Clear() { head = count = 0; showing = false; until = 0; message = null; }
    }
}
