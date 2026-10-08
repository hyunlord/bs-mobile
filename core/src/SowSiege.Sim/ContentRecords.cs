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
    string FloorRationale, string[] AntiSynergy, AntiSynergyNote[] AntiSynergyNotes, EquipmentProjection? RuntimeProjection = null)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks)
{
    public ToolDefinition ToCore() => new(Id, Tags, Activation.ToCore(), Growth.ToCore(), FloorRationale, AntiSynergy);
}

public sealed record WeaponContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, WeaponActivationContent Activation, EquipmentProjection? RuntimeProjection = null,
    WeaponCombatWeaponDefinition? Growth = null)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks)
{
    public WeaponDefinition ToCore() => new(Id, Tags, Activation.ToCore(Growth));
}

public sealed record HeroContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, string StartingTool, int DamageMultiplier,
    string[] AffinityEstateIds, ProposedAbility Ability)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks)
{
    public HeroDefinition ToCore() => new(Id, StartingTool, DamageMultiplier);
}

public sealed record EstateContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, int GrowthMultiplier, EstateLoop UniqueLoop, RemainsLoopDefinition? RemainsLoop = null)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks)
{
    public EstateDefinition ToCore() => new(Id, GrowthMultiplier, RemainsLoop);
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

public sealed record RuntimeProfile(string Id, string Name, ContentSelection Selection, ContentSelection TestSelection, RuntimeProfileExtension? Runtime = null, ExperimentProfileExtension? Experiment = null, WeaponCombatProfileExtension? WeaponCombat = null,
    string? TuningFile = null, ProductionGameplay? Gameplay = null, FirstPlayableDefinition? FirstPlayable = null, RuntimeProjectionOverrides? RuntimeOverrides = null)
{
    public ContentSelection Select(bool includeTest) => includeTest ? Selection.Add(TestSelection) : Selection;
}

public sealed record EquipmentProjection(GrowthActionDefinition[] GrowthActions, RuntimeEffectDefinition[] Effects);
public sealed record CharterProjection(string PolicyCategory, RuntimeEffectDefinition[] Effects);
public sealed record ItemProjection(string[] RequiredTags, RuntimeEffectDefinition[] Effects);
public sealed record EvolutionProjection(RuntimeEffectDefinition[] Effects);
public sealed record RuntimeProfileExtension(int ContractVersion, string[] Charters, string[] Items, string[] Evolutions, RuntimeTuning Tuning);
public sealed record ProposedEffect(string Trigger, string Benefit, string Cost);
public sealed record ProposedCharterEffect(string Target, string Trigger, string Benefit, string Cost);
public sealed record EvolutionGrowthCondition(string Target, string State, int Minimum);
public sealed record ProposedEvolutionResult(string BaseId, string Name, string Effect, string Cost);
public sealed record CharterContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, ProposedCharterEffect Effect, string[] Requirements, CharterProjection? RuntimeProjection = null)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks);
public sealed record ItemContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, ProposedEffect Effect, string[] LinkedToolIds, ItemProjection? RuntimeProjection = null)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks);
public sealed record EvolutionContent(string Id, string Name, string Concept, string[] Tags, string DesignStatus,
    string ImplementationNote, LoopLink[] LoopLinks, string Kind, string[] InputIds, EvolutionGrowthCondition? GrowthCondition,
    ProposedEvolutionResult Result, EvolutionProjection? RuntimeProjection = null)
    : ContentRecord(Id, Name, Concept, Tags, DesignStatus, ImplementationNote, LoopLinks);

public sealed record WeaponCombatProfileExtension(int ContractVersion, string? DefinitionsFile = null);
public sealed record WeaponCombatFile(int ContractVersion, Dictionary<string, WeaponCombatWeaponDefinition> Weapons);
public sealed record WeaponCombatManifest(int ContractVersion, string[] Weapons);
public sealed record ProductionGameplay(EnemyOverride[] EnemyOverrides, ExperimentDefinition Experiment);

public sealed record WeaponActivationContent(string Shape, string Form, decimal DamageCoefficient,
    int? Damage = null, int? Range = null, int? CooldownTicks = null, int? Knockback = null)
{
    public Activation ToCore(WeaponCombatWeaponDefinition? growth)
    {
        if (growth is not null)
        {
            var first = growth.Levels[0];
            return new(first.Damage, first.Range, first.CooldownTicks, Shape, first.Knockback);
        }
        return new(Damage!.Value, Range!.Value, CooldownTicks!.Value, Shape, Knockback!.Value);
    }
}

public sealed record RuntimeProjectionOverrides(Dictionary<string, EquipmentProjection> Equipment, Dictionary<string, CharterProjection> Charters, Dictionary<string, ItemProjection> Items, Dictionary<string, EvolutionProjection> Evolutions);
