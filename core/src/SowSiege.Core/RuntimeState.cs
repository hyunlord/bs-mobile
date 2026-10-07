namespace SowSiege.Core;

internal sealed class EffectCounter
{
    public long Count;
    public long Total;
    public int? First;
    public int? Last;
}
internal sealed class RuntimeEntityState
{
    public int Shield;
    public int ShieldUntil;
    public int PauseUntil;
    public int HoldUntil;
    public int RestSince;
    public bool HasReturned;
    public bool ArrivalGuardUsed;
    public Position? Waypoint;
    public Position Facing;
}
internal sealed record GroundLoot(int Id, string Source, string Item, Position Position);
internal sealed record ExperienceEvent(int Tick, string Category, long Amount);
internal sealed class RuntimeState
{
    public SortedDictionary<string, int> Charters = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Items = new(StringComparer.Ordinal);
    public SortedSet<string> Evolutions = new(StringComparer.Ordinal);
    public SortedDictionary<string, EffectCounter> Effects = new(StringComparer.Ordinal);
    public SortedDictionary<int, RuntimeEntityState> Entities = [];
    public SortedDictionary<string, SortedDictionary<string, long>> Growth = new(StringComparer.Ordinal);
    public List<GroundLoot> GroundLoot = [];
    public List<LootAcquisition> Loot = [];
    public List<EvolutionActivation> EvolutionEvents = [];
    public List<ExperienceEvent> Experience = [];
}
internal sealed record EffectContext(Position Origin, string Equipment = "", EnemyState? Enemy = null, FarmState? Farm = null, BuildingState? Building = null, PersonState? Person = null, bool WasRuined = false, bool WasNew = false, bool ShieldConsumed = false);
internal sealed record OwnedEffect(string Source, string Kind, int Stacks, RuntimeEffectDefinition Definition);
