using System;
using System.Collections.Generic;
namespace SowSiege.Core
{
    internal sealed class ActiveAttackState
    {
        public int Id;
        public int ActivationId;
        public string Source = "";
        public string Form = "";
        public Position Origin;
        public Position Position;
        public Position Previous;
        public Position Direction;
        public int Age;
        public int Level;
        public int RemainingHits;
        public int Phase;
        public SortedDictionary<int, int> HitTicks = new();
    }
    internal sealed class AttackActivationState
    {
        public string Source = "";
        public string Form = "";
        public int Remaining;
        public bool HadHit;
    }
    internal sealed class MapEventState
    {
        public int Id;
        public string Definition = "";
        public Position Position;
        public int Health;
        public int ExpiresTick;
    }
    internal sealed class FirstPlayableState
    {
        public List<ActiveAttackState> Attacks = new();
        public SortedDictionary<int, AttackActivationState> Activations = new();
        public List<MapEventState> MapEvents = new();
        public SortedDictionary<string, int> NextEnemySpawn = new(StringComparer.Ordinal);
        public SortedDictionary<string, int> NextEventSpawn = new(StringComparer.Ordinal);
        public SortedDictionary<int, int> BuildingWork = new();
        public SortedDictionary<string, RarityDefinition> OfferedRarities = new(StringComparer.Ordinal);
        public SortedDictionary<int, string> PersonActivities = new();
        public SortedDictionary<string, int> BossEntities = new(StringComparer.Ordinal);
        public SortedSet<string> DefeatedBosses = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> Coverage = new(StringComparer.Ordinal);
        public long Kills;
        public void Count(string key) => Coverage[key] = Coverage.GetValueOrDefault(key) + 1;
    }
}
