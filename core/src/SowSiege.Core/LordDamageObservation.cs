namespace SowSiege.Core
{
    public enum LordDamageKind { Strike, Contact, Charge, Water, Projectile }

    // Diagnostic metadata only: never read by rules or written into replay/state codecs.
    public readonly struct LordDamageObservation
    {
        public int Tick { get; }
        public int AttackerId { get; }
        public int ProjectileId { get; }
        public string DefinitionId { get; }
        public LordDamageKind Kind { get; }
        public int Damage { get; }
        public WorldPoint Origin { get; }
        public WorldPoint Lord { get; }
        public LordDamageObservation(int tick, int attackerId, int projectileId, string definitionId, LordDamageKind kind, int damage, WorldPoint origin, WorldPoint lord)
        { Tick = tick; AttackerId = attackerId; ProjectileId = projectileId; DefinitionId = definitionId; Kind = kind; Damage = damage; Origin = origin; Lord = lord; }
    }

    // Hosts attach before replaying inputs; callbacks must be nonthrowing and observational.
    public interface ILordDamageObserver
    {
        void OnLordDamage(in LordDamageObservation observation);
    }
}
