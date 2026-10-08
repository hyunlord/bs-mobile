using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SowSiege.Tests")]
namespace SowSiege.Core
{

    internal readonly struct Position : IEquatable<Position>
    {
        private const int RecordHashMultiplier = 1521134295;
        public int X { get; }
        public int Y { get; }
        public Position(int x, int y) { X = x; Y = y; }
        public void Deconstruct(out int x, out int y) { x = X; y = Y; }
        public override string ToString() => $"Position {{ X = {X}, Y = {Y} }}";
        public bool Equals(Position other) => X == other.X && Y == other.Y;
        public override bool Equals(object? other) => other is Position position && Equals(position);
        public override int GetHashCode() => unchecked(Y.GetHashCode() - X.GetHashCode() * RecordHashMultiplier);
        public static bool operator ==(Position left, Position right) => left.Equals(right);
        public static bool operator !=(Position left, Position right) => !left.Equals(right);
        public long DistanceSquared(Position other) => (long)(X - other.X) * (X - other.X) + (long)(Y - other.Y) * (Y - other.Y);
        public Position MoveToward(Position target, int speed)
        {
            var distance = Math.Max(Math.Abs(target.X - X), Math.Abs(target.Y - Y));
            if (distance <= speed) { return target; }
            return new(X + (int)((long)(target.X - X) * speed / distance), Y + (int)((long)(target.Y - Y) * speed / distance));
        }
    }

    internal sealed class EnemyState
    {
        public int Id;
        public string Definition = "";
        public Position Position;
        public int Health;
        public int AttackTick;
        public string LastTarget = "";
        public Position TargetPosition;
        public int TargetRefreshTick;
        public int TargetId;
    }
    internal sealed class FarmState
    {
        public int Id;
        public Position Position;
        public string Source = "";
        public int Stage;
        public int Progress;
        public int Fertility;
    }
    internal sealed class BuildingState
    {
        public int Id;
        public Position Position;
        public string Source = "";
        public bool Built;
        public int Health;
        public int AttackTick;
    }
    internal sealed class PersonState
    {
        public int Id;
        public Position Position;
        public string Role = "peasant";
        public int Members = 1;
        public Position Destination;
        public string Source = "";
        public int DutyUntil;
        public int AttackTick;
        public int Health;
    }
    internal sealed class EquipmentState
    {
        public string Id = "";
        public int Level = 1;
        public int ReadyTick;
    }
    internal sealed class ToolLedger
    {
        public long ActivationDamage;
        public long GrowthProduced;
        public long GrowthDamage;
        public long Activations;
    }
    internal sealed class TrackedRandom
    {
        public TrackedRandom(int seed, bool portable = false)
        {
            random = new Random(seed);
            if (portable) { Portable = new PortableRandom(seed); }
        }

        internal PortableRandom? Portable { get; }

        private readonly Random random;
        public long Draws { get; private set; }
        public int Next(int limit) { Draws++; return Portable is null ? random.Next(limit) : Portable.Next(limit); }
    }
    internal sealed class WorldState
    {
        [OmitWhenNull]
        public RuntimeState? Runtime;
        [OmitWhenNull]
        public ExperimentState? Experiment;
        [OmitWhenNull]
        public RemainsState? Remains;
        [OmitWhenNull]
        public WeaponCombatState? WeaponCombat;
        public int Tick;
        public int Season;
        public int NextId;
        public Position Lord;
        public Position Destination;
        public Position Estate;
        public int LordHealth;
        public int Food;
        public int Level = 1;
        public long Experience;
        public long KillExperience;
        public long HarvestExperience;
        public long TaxExperience;
        public long WeaponDamage;
        public long AllyDamage;
        public string[] PendingCards = Array.Empty<string>();
        public string? LockedCard;
        public SortedSet<string> BannedCards = new(StringComparer.Ordinal);
        public int Rerolls;
        public int Bans;
        public int Locks;
        public long EstateTicks;
        public long SpawnedEnemies;
        public int Harvests;
        public int Ruins;
        public int Rebuilds;
        public string DeathCause = "";
        public List<EnemyState> Enemies = new();
        public List<FarmState> Farms = new();
        public List<BuildingState> Buildings = new();
        public List<PersonState> People = new();
        public List<EquipmentState> Equipment = new();
        public SortedDictionary<string, ToolLedger> Tools = new(StringComparer.Ordinal);
        public List<TimeSample> Timeline = new();
        public List<CardChoice> Cards = new();
        public int AllocateId() => NextId++;
    }

}
