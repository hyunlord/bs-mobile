using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{
    public sealed class InteractiveSession
    {
        public const int MaximumSpawnPermille = 10000;
        internal Simulation Simulation { get; }
        internal ContentCatalog Catalog { get; }
        internal InteractiveOptions Options { get; }
        internal string InitialCatalogHash { get; }
        internal InteractiveState State { get; }
        public long NextSequence { get; private set; }
        public IReadOnlyRunView View { get; }
        public InteractiveSession(ContentCatalog catalog, InteractiveOptions options)
        {
            if (catalog is null || options is null) { throw new ArgumentNullException(nameof(catalog)); }
            if (!options.Run.ManualCards || options.Run.Scenario != "normal" || options.Run.Movement is not null || !Enum.IsDefined(typeof(AimMode), options.InitialAimMode)) { throw new ArgumentException("Interactive run requires manual cards, normal scenario and manual movement."); }
            ReplayCodec.ValidateHash(options.DataHash);
            Catalog = catalog; Options = options; InitialCatalogHash = PortableStateCodec.HashCatalog(catalog); State = new() { Aim = options.InitialAimMode };
            Simulation = new(catalog, options.Run, new InteractiveHasher(), null, State);
            View = new SessionView(this);
        }
        private sealed class InteractiveHasher : IStateHasher
        {
            public string Compute(object snapshot) => throw new InvalidOperationException("Interactive sessions use the explicit state codec.");
        }
        internal RunStatus Status => Simulation.IsComplete ? RunStatus.Completed : Simulation.World.PendingCards.Length > 0 ? RunStatus.AwaitingCard : RunStatus.Running;
        public void Apply(ReplayCommand command)
        {
            ReplayCodec.ValidateCommand(command);
            if (command.Sequence != NextSequence || command.Tick != Simulation.World.Tick) { throw new ArgumentException("Command sequence or tick mismatch."); }
            if (Status == RunStatus.Completed) { throw new InvalidOperationException("Run is complete."); }
            var cards = Simulation.World.PendingCards;
            if (command.Kind == ReplayCommandKind.Advance && Status != RunStatus.Running) { throw new InvalidOperationException("Choose a pending card first."); }
            if (command.Kind == ReplayCommandKind.GrantLevel && Status != RunStatus.Running) { throw new InvalidOperationException("A card is already pending."); }
            if (command.Kind is ReplayCommandKind.ChooseCard or ReplayCommandKind.BanCard or ReplayCommandKind.LockCard)
            {
                if (!cards.Contains(command.CardId, StringComparer.Ordinal)) { throw new ArgumentException("Card is not offered."); }
                if (command.Kind == ReplayCommandKind.BanCard && Simulation.World.Bans <= 0 || command.Kind == ReplayCommandKind.LockCard && Simulation.World.Locks <= 0) { throw new InvalidOperationException("Card budget exhausted."); }
            }
            if (command.Kind == ReplayCommandKind.RerollCards && (cards.Length == 0 || Simulation.World.Rerolls <= 0)) { throw new InvalidOperationException("No offer or reroll budget."); }
            switch (command.Kind)
            {
                case ReplayCommandKind.Advance: State.Events.Clear(); Simulation.Advance(command.Input); break;
                case ReplayCommandKind.ChooseCard: Simulation.ChooseCard(command.CardId!); break;
                case ReplayCommandKind.RerollCards: Simulation.RerollCards(); break;
                case ReplayCommandKind.BanCard: Simulation.BanCard(command.CardId!); break;
                case ReplayCommandKind.LockCard: Simulation.LockCard(command.CardId!); break;
                case ReplayCommandKind.SetAimMode: State.Aim = (AimMode)command.Value; break;
                case ReplayCommandKind.SetInvulnerable: State.Invulnerable = command.Value != 0; break;
                case ReplayCommandKind.SetSpawnPermille: State.SpawnPermille = command.Value; break;
                case ReplayCommandKind.GrantLevel: Simulation.GrantLevel(); break;
                default: throw new ArgumentException("Unknown command.");
            }
            NextSequence++;
        }
        public string ComputeStateHash() => PortableStateCodec.Hash(this);
        public RunSummary GetSummary()
        {
            var w = Simulation.World;
            return new(Options.Run.Seed, w.Tick, w.LordHealth > 0, w.LordHealth <= 0 ? "death" : Simulation.IsComplete ? "duration" : "running", w.Level,
                w.KillExperience, w.HarvestExperience, w.TaxExperience, w.WeaponDamage, w.Tools.Values.Sum(t => t.ActivationDamage), w.Tools.Values.Sum(t => t.GrowthDamage), w.AllyDamage, ComputeStateHash());
        }
        private sealed class SessionView : IReadOnlyRunView
        {
            private readonly InteractiveSession session;
            internal SessionView(InteractiveSession session) { this.session = session; }
            public RunStatus Status => session.Status;
            public CardOfferView CaptureCards()
            {
                var w = session.Simulation.World;
                return new(Array.AsReadOnly(w.PendingCards.ToArray()), w.LockedCard, w.Rerolls, w.Bans, w.Locks);
            }
            public RunFrame CaptureFrame()
            {
                var w = session.Simulation.World; var c = session.Catalog; var map = c.Tuning.World.Map;
                var enemies = w.Enemies.Where(e => e.Health > 0).Select(e => new EnemyView(e.Id, e.Definition, InteractiveState.Point(e.Position), e.Health, c.Enemies[e.Definition].Health)).ToArray();
                var farms = w.Farms.Select(f => new FarmView(f.Id, f.Source, InteractiveState.Point(f.Position), f.Stage, f.Stage == c.Tuning.World.Farms.StageTicks.Length - 1)).ToArray();
                var buildings = w.Buildings.Select(b => new BuildingView(b.Id, b.Source, InteractiveState.Point(b.Position), b.Built, b.Health)).ToArray();
                var people = w.People.Select(p => new PersonView(p.Id, p.Source, InteractiveState.Point(p.Position), p.Role, p.Members, p.Health)).ToArray();
                var loot = w.Runtime?.GroundLoot.Select(l => new GroundLootView(l.Id, l.Item, InteractiveState.Point(l.Position))).ToArray() ?? Array.Empty<GroundLootView>();
                var remains = w.Remains?.Live.Select(r => new RemainView(r.Id, InteractiveState.Point(r.Position))).ToArray() ?? Array.Empty<RemainView>();
                var extent = Math.Max(map.EstateRadius, w.Farms.Select(f => Math.Max(Math.Abs(f.Position.X - w.Estate.X), Math.Abs(f.Position.Y - w.Estate.Y))).Concat(w.Buildings.Select(b => Math.Max(Math.Abs(b.Position.X - w.Estate.X), Math.Abs(b.Position.Y - w.Estate.Y)))).DefaultIfEmpty(0).Max());
                return new(w.Tick, Status, w.Season, Math.Max(0, c.Tuning.World.Seasons.Take(w.Season + 1).Sum(s => s.DurationTicks) - w.Tick), c.Tuning.DurationTicks, c.Tuning.TickRate, map.Width, map.Height,
                    new(InteractiveState.Point(w.Lord), InteractiveState.Point(w.WeaponCombat?.Facing ?? new Position(1, 0)), w.LordHealth, map.LordHealth), InteractiveState.Point(w.Estate), w.Experience, session.Simulation.RequiredExperience, w.Level, extent,
                    new(enemies.Length, people.Length, farms.Length, buildings.Length, session.State.Events.Count(e => e.Kind == PresentationKind.Attack && (e.Shape == "projectile" || e.Shape == "rays"))),
                    Array.AsReadOnly(enemies), Array.AsReadOnly(farms), Array.AsReadOnly(buildings), Array.AsReadOnly(people), Array.AsReadOnly(loot), Array.AsReadOnly(remains), Array.AsReadOnly(w.Equipment.Select(e => new EquipmentView(e.Id, e.Level)).ToArray()), Array.AsReadOnly(session.State.Events.ToArray()));
            }
        }
    }
}
