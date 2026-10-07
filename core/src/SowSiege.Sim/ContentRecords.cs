using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record LoopLink(string EstateId, string Stage, string Reason);
public sealed record ProposedAbility(string Trigger, string Effect, string Cost);
public sealed record AntiSynergyNote(string OtherId, string Reason);
public sealed record LoopStage(string Id, string Action);
public sealed record EstateLoop(string Name, string Summary, LoopStage[] Stages);
public sealed record EnemyBehavior(string Trigger, string Pressure, string Counterplay);

public abstract record ContentRecord(string Id, string Name, string Concept, string[] Tags,
    string DesignStatus, string ImplementationNote, LoopLink[] LoopLinks);

public sealed record ActivationContent(int Damage, int Range, int CooldownTicks, string Shape, int Knockback,
    string Form, decimal DamageCoefficient)
{
    // The coefficient describes a design proposal; only S2's unchanged integer tuning is executed.
    public Activation ToCore() => new(Damage, Range, CooldownTicks, Shape, Knockback);
}

public sealed record GrowthContent(string Target, int Yield, string Output, string PrimaryRoute)
{
    public Growth ToCore() => new(Target, Yield);
}

public sealed record ToolContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, ActivationContent Activation, GrowthContent Growth,
    string FloorRationale, string[] AntiSynergy, AntiSynergyNote[] AntiSynergyNotes)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks)
{
    public ToolDefinition ToCore() => new(Id, Tags, Activation.ToCore(), Growth.ToCore(), FloorRationale, AntiSynergy);
}

public sealed record WeaponContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, ActivationContent Activation)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks)
{
    public WeaponDefinition ToCore() => new(Id, Tags, Activation.ToCore());
}

public sealed record HeroContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, string StartingTool, int DamageMultiplier,
    string[] AffinityEstateIds, ProposedAbility Ability)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks)
{
    public HeroDefinition ToCore() => new(Id, StartingTool, DamageMultiplier);
}

public sealed record EstateContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, int GrowthMultiplier, EstateLoop UniqueLoop)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks)
{
    public EstateDefinition ToCore() => new(Id, GrowthMultiplier);
}

public sealed record EnemyContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, string Target, int Health, int Speed, int Damage,
    int Range, int AttackCooldownTicks, int Experience, EnemyBehavior Behavior)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks)
{
    public EnemyDefinition ToCore() => new(Id, Target, Health, Speed, Damage, Range, AttackCooldownTicks, Experience);
}

public sealed record ContentSelection(string[] Weapons, string[] Tools, string[] Enemies, string[] Heroes, string[] Estates)
{
    public ContentSelection Add(ContentSelection other) => new([.. Weapons, .. other.Weapons],
        [.. Tools, .. other.Tools], [.. Enemies, .. other.Enemies], [.. Heroes, .. other.Heroes], [.. Estates, .. other.Estates]);
}

public sealed record RuntimeProfile(string Id, string Name, ContentSelection Selection, ContentSelection TestSelection)
{
    public ContentSelection Select(bool includeTest) => includeTest ? Selection.Add(TestSelection) : Selection;
}
