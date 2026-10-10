using System;
using SowSiege.Core;

namespace Game.App
{
    public static class WeakCapturePolicy
    {
        public static int Preference(bool item, bool evolution, bool weapon, bool owned, int weaponCount)
            => evolution || weapon && !owned && weaponCount >= 2 ? int.MaxValue : item ? 0 : weapon && !owned && weaponCount == 1 ? 5 : owned ? 10 : weapon ? 30 : 20;

        public static string Choose(CardOfferView offer, RunFrame frame, ContentCatalog catalog)
        {
            var weaponCount = 0;
            foreach (var equipment in frame.Equipment) if (catalog.Weapons.ContainsKey(equipment.Id)) weaponCount++;
            string best = null; var bestRank = int.MaxValue;
            foreach (var id in offer.Cards)
            {
                var owned = false;
                foreach (var equipment in frame.Equipment) if (equipment.Id == id) { owned = true; break; }
                var rank = Preference(catalog.WaveRuntime.Items.ContainsKey(id), catalog.WaveRuntime.Evolutions.ContainsKey(id), catalog.Weapons.ContainsKey(id), owned, weaponCount);
                if (rank < bestRank) { best = id; bestRank = rank; }
            }
            return best;
        }

        public static WorldPoint Target(RunFrame frame, WaveRuntimeFrame wave)
        {
            var angle = frame.Tick / (frame.TickRate * 9d);
            var desired = new WorldPoint((int)(frame.MapWidth * (.5 + .34 * Math.Cos(angle))), (int)(frame.MapHeight * (.5 + .34 * Math.Sin(angle))));
            var origin = frame.Lord.Position;
            var dx = (double)desired.X - origin.X; var dy = (double)desired.Y - origin.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var direct = length <= 450 ? desired : new WorldPoint(origin.X + (int)(dx * 450 / length), origin.Y + (int)(dy * 450 / length));
            var best = direct; var bestRisk = Risk(direct, frame, wave);
            if (bestRisk == 0) return desired;
            for (var i = 0; i < 16; i++)
            {
                var a = i * Math.PI / 8;
                var candidate = new WorldPoint(Math.Max(250, Math.Min(frame.MapWidth - 250, origin.X + (int)(Math.Cos(a) * 450))), Math.Max(250, Math.Min(frame.MapHeight - 250, origin.Y + (int)(Math.Sin(a) * 450))));
                var risk = Risk(candidate, frame, wave);
                if (risk < bestRisk || risk == bestRisk && Distance(candidate, desired) < Distance(best, desired)) { best = candidate; bestRisk = risk; }
            }
            return best;
        }

        static double Risk(WorldPoint point, RunFrame frame, WaveRuntimeFrame wave)
        {
            double risk = 0;
            foreach (var enemy in frame.Enemies)
            {
                if (enemy.Health <= 0) continue;
                var distance = Math.Sqrt(Distance(point, enemy.Position));
                if (distance < 650) risk += (650 - distance) / 650;
            }
            foreach (var enemy in wave.Enemies)
            {
                if (enemy.Phase != "tell-charge" && enemy.Phase != "charge" && enemy.Phase != "tell-water" && enemy.Phase != "water") continue;
                var dx = (double)enemy.Target.X - enemy.Origin.X; var dy = (double)enemy.Target.Y - enemy.Origin.Y;
                var length = dx * dx + dy * dy;
                var t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((point.X - enemy.Origin.X) * dx + (point.Y - enemy.Origin.Y) * dy) / length));
                var x = point.X - enemy.Origin.X - t * dx; var y = point.Y - enemy.Origin.Y - t * dy;
                risk += 3 * Math.Max(0, 1 - Math.Sqrt(x * x + y * y) / 450);
            }
            return risk;
        }

        static long Distance(WorldPoint a, WorldPoint b) { var x = (long)a.X - b.X; var y = (long)a.Y - b.Y; return x * x + y * y; }
    }
}
