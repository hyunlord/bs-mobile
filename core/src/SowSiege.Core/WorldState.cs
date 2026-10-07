using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

[assembly: InternalsVisibleTo("SowSiege.Tests")]
namespace SowSiege.Core;

internal readonly record struct Position(int X, int Y)
{
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
internal sealed class TrackedRandom(int seed)
{
    private readonly Random random = new(seed);
    public long Draws { get; private set; }
    public int Next(int limit) { Draws++; return random.Next(limit); }
}
internal sealed class WorldState
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeState? Runtime;
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
    public string[] PendingCards = [];
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
    public List<EnemyState> Enemies = [];
    public List<FarmState> Farms = [];
    public List<BuildingState> Buildings = [];
    public List<PersonState> People = [];
    public List<EquipmentState> Equipment = [];
    public SortedDictionary<string, ToolLedger> Tools = new(StringComparer.Ordinal);
    public List<TimeSample> Timeline = [];
    public List<CardChoice> Cards = [];
    public int AllocateId() => NextId++;
}
