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
        public IRuntimeDiagnostics? Diagnostics { get => Simulation.World.Diagnostics; set => Simulation.World.Diagnostics = value; }
        public ILordDamageObserver? LordDamageObserver { get => Simulation.World.LordDamageObserver; set => Simulation.World.LordDamageObserver = value; }
        public double LocomotionSpeedPerTick
        {
            get
            {
                var speed = Simulation.Wave?.MovementSpeed(Catalog.Tuning.World.Map.LordSpeed) ?? Catalog.Tuning.World.Map.LordSpeed;
                return Options.Feel?.FasterLord == true ? (double)speed * FeelExperimentState.SpeedNumerator / FeelExperimentState.SpeedDenominator : speed;
            }
        }
        public InteractiveSession(ContentCatalog catalog, InteractiveOptions options)
        {
            if (catalog is null || options is null) { throw new ArgumentNullException(nameof(catalog)); }
            if (!options.Run.ManualCards || options.Run.Scenario != "normal" || options.Run.Movement is not null || !Enum.IsDefined(typeof(AimMode), options.InitialAimMode)) { throw new ArgumentException("Interactive run requires manual cards, normal scenario and manual movement."); }
            ReplayCodec.ValidateHash(options.DataHash);
            options = options with { Feel = FeelExperimentOptions.Normalize(options.Feel) };
            if (options.Feel is not null && catalog.WaveRuntime is null) { throw new ArgumentException("Feel experiments require a wave runtime."); }
            if (options.Field is not null && catalog.WaveRuntime is null) { throw new ArgumentException("Fields require a wave runtime."); }
            if (options.Feel?.ImmediateContact == true)
            {
                var definition = catalog.WaveRuntime!;
                var modules = new WavePrimitiveModules(definition);
                foreach (var rule in definition.Enemies.Values)
                {
                    if (!WaveEnemySystem.ContactEligible(catalog, rule, modules.Enemy(rule.Id))) { continue; }
                    if (definition.EnemySeparation is not { Version: 1 } body || !body.BodyWidths.TryGetValue(rule.Id, out var width) || width <= 0)
                    { throw new ArgumentException("Immediate contact requires a positive body width for every eligible enemy."); }
                }
            }
            options.HostRewards?.Validate();
            if (options.MeleeAim is not null && (options.MeleeAim.Version != 1 || catalog.WaveRuntime is null))
            { throw new ArgumentException("Unsupported melee aim contract."); }
            if (catalog.WaveRuntime is { } aimWave)
            {
                foreach (var gear in aimWave.Gear.Values)
                { if (gear.Aim is not null && gear.Aim != "nearest-enemy") { throw new ArgumentException("Unsupported gear aim."); } }
            }
            if (options.HostRewards?.Revive is { } revive &&
                (revive.RestoreHealth > (options.Feel is { ReadableHealth: true } ? FeelExperimentState.Health : catalog.Tuning.World.Map.LordHealth) || revive.InvulnerabilityTicks > int.MaxValue - catalog.Tuning.DurationTicks))
            { throw new ArgumentException("Host revive rules exceed run limits.", nameof(options)); }
            var initialCatalogHash = PortableStateCodec.HashCatalog(catalog);
            if (options.Estate is { } estate)
            {
                WaveEstateLight.Validate(estate);
                if (catalog.WaveRuntime?.EstateLight is not { } light) { throw new ArgumentException("Estate runs require an estate light definition.", nameof(options)); }
                WaveEstateLight.Validate(light);
                if (!light.Loadouts.ContainsKey(estate.LoadoutId)) { throw new ArgumentException("Unknown estate loadout.", nameof(options)); }
                // The recorded catalog hash pins the loadout contents; the data tree hash cannot tell two loadouts of one tree apart (ADR0064 section 4).
                if (!string.Equals(estate.LoadoutCatalogHash, initialCatalogHash, StringComparison.Ordinal)) { throw new ArgumentException("Estate loadout catalog hash mismatch.", nameof(options)); }
            }
            Catalog = catalog; Options = options; InitialCatalogHash = initialCatalogHash; State = new() { Aim = options.InitialAimMode };
            if (options.Estate is not null) { State.EstateLoadout = options.Estate.LoadoutId; State.EstateSeed = options.Run.Seed; }
            if (options.Feel is not null) { State.Feel = new(options.Feel); }
            if (options.HostRewards is not null) { State.HostRewards = new(); }
            State.MeleeAim = options.MeleeAim;
            if (options.Field is not null) { State.Field = new(options.Field, options.Run.Seed); }
            Simulation = new(catalog, options.Run, new InteractiveHasher(), null, State);
            View = new SessionView(this);
        }
        private sealed class InteractiveHasher : IStateHasher
        {
            public string Compute(object snapshot) => throw new InvalidOperationException("Interactive sessions use the explicit state codec.");
        }
        internal RunStatus Status => Simulation.IsComplete ? RunStatus.Completed : Simulation.World.PendingCards.Length > 0 ? RunStatus.AwaitingCard : RunStatus.Running;
        public int HostRerollsGranted => State.HostRewards?.RerollGrants ?? 0;
        public bool HostRevived => State.HostRewards?.Revived ?? false;
        public bool CanHostRevive => Options.HostRewards?.Revive is not null && State.HostRewards is { Revived: false }
            && Simulation.World.LordHealth <= 0 && Simulation.World.Tick < Catalog.Tuning.DurationTicks;
        public void Apply(ReplayCommand command)
        {
            ReplayCodec.ValidateCommand(command);
            if (command.Sequence != NextSequence || command.Tick != Simulation.World.Tick) { throw new ArgumentException("Command sequence or tick mismatch."); }
            if (Status == RunStatus.Completed && command.Kind != ReplayCommandKind.HostReviveV1) { throw new InvalidOperationException("Run is complete."); }
            var cards = Simulation.World.PendingCards;
            if (command.Kind == ReplayCommandKind.Advance && Status != RunStatus.Running) { throw new InvalidOperationException("Choose a pending card first."); }
            if (command.Kind == ReplayCommandKind.GrantLevel && Status != RunStatus.Running) { throw new InvalidOperationException("A card is already pending."); }
            if (command.Kind is ReplayCommandKind.ChooseCard or ReplayCommandKind.BanCard or ReplayCommandKind.LockCard)
            {
                if (!cards.Contains(command.CardId, StringComparer.Ordinal)) { throw new ArgumentException("Card is not offered."); }
                if (command.Kind == ReplayCommandKind.BanCard && Simulation.World.Bans <= 0 || command.Kind == ReplayCommandKind.LockCard && Simulation.World.Locks <= 0) { throw new InvalidOperationException("Card budget exhausted."); }
            }
            if (command.Kind == ReplayCommandKind.RerollCards && (cards.Length == 0 || Simulation.World.Rerolls <= 0)) { throw new InvalidOperationException("No offer or reroll budget."); }
            if (command.Kind == ReplayCommandKind.HostReviveV1 && !CanHostRevive) { throw new InvalidOperationException("Revive is not available."); }
            if (command.Kind == ReplayCommandKind.HostGrantRerollV1 &&
                (Options.HostRewards?.AllowRerollGrant != true || Status != RunStatus.AwaitingCard || Simulation.World.Rerolls != 0 || State.HostRewards!.RerollGrants >= HostRewardRunOptions.MaximumRerollGrants))
            { throw new InvalidOperationException("Host reroll grant is not available."); }
            switch (command.Kind)
            {
                case ReplayCommandKind.Advance: State.Events.Clear(); AdvanceWithHostImmunity(command.Input); break;
                case ReplayCommandKind.ChooseCard: Simulation.ChooseCard(command.CardId!); break;
                case ReplayCommandKind.RerollCards: Simulation.RerollCards(); break;
                case ReplayCommandKind.BanCard: Simulation.BanCard(command.CardId!); break;
                case ReplayCommandKind.LockCard: Simulation.LockCard(command.CardId!); break;
                case ReplayCommandKind.SetAimMode: State.Aim = (AimMode)command.Value; break;
                case ReplayCommandKind.SetInvulnerable: State.Invulnerable = command.Value != 0; break;
                case ReplayCommandKind.SetSpawnPermille: State.SpawnPermille = command.Value; break;
                case ReplayCommandKind.GrantLevel: Simulation.GrantLevel(); break;
                case ReplayCommandKind.HostGrantRerollV1: Simulation.World.Rerolls = 1; State.HostRewards!.RerollGrants++; break;
                case ReplayCommandKind.HostReviveV1:
                    var revive = Options.HostRewards!.Revive!;
                    Simulation.World.LordHealth = revive.RestoreHealth;
                    State.HostRewards!.Revived = true;
                    State.HostRewards.InvulnerableUntil = Simulation.World.Tick + revive.InvulnerabilityTicks;
                    break;
                default: throw new ArgumentException("Unknown command.");
            }
            NextSequence++;
        }
        private void AdvanceWithHostImmunity(PlayerInput input)
        {
            if (State.HostRewards is not { } rewards || Simulation.World.Tick >= rewards.InvulnerableUntil)
            { Simulation.Advance(input); return; }
            bool original = State.Invulnerable;
            State.Invulnerable = true;
            try { Simulation.Advance(input); }
            finally { State.Invulnerable = original; }
        }
        public string ComputeStateHash() => PortableStateCodec.Hash(this);
        public RunSummary GetSummary()
        {
            var w = Simulation.World;
            return new(Options.Run.Seed, w.Tick, w.LordHealth > 0, w.LordHealth <= 0 ? "death" : Simulation.IsComplete ? "duration" : "running", w.Level,
                w.KillExperience, w.HarvestExperience, w.TaxExperience, w.WeaponDamage, w.Tools.Values.Sum(t => t.ActivationDamage), w.Tools.Values.Sum(t => t.GrowthDamage), w.AllyDamage, ComputeStateHash(), w.WaveRuntime is not null ? w.WaveRuntime.BossDefeated : Catalog.FirstPlayable is null ? null : FirstPlayableView.BossDefeated(Catalog, w), State.HostRewards?.Revived);
        }
        private sealed class SessionView : IBufferedRunView, IBufferedWaveRunView
        {
            private readonly InteractiveSession session;
            internal SessionView(InteractiveSession session) { this.session = session; }
            public RunStatus Status => session.Status;
            public WaveRuntimeFrame? CaptureWaveRuntime() => session.Catalog.WaveRuntime is { } definition ? session.Simulation.World.WaveRuntime!.Capture(definition, session.Simulation.World.Tick) : null;
            public WaveRuntimeFrame? CaptureWaveRuntime(WaveSnapshotBuffer buffer)
            {
                if (buffer is null) { throw new ArgumentNullException(nameof(buffer)); }
                return session.Catalog.WaveRuntime is { } definition ? session.Simulation.World.WaveRuntime!.Capture(definition, session.Simulation.World.Tick, buffer) : null;
            }
            public FirstPlayableFrame? CaptureFirstPlayable() => FirstPlayableView.Capture(session.Catalog, session.Simulation.World, session.Options.Run.HeroId);
            public CardOfferView CaptureCards()
            {
                var w = session.Simulation.World;
                return new(Array.AsReadOnly(w.PendingCards.ToArray()), w.LockedCard, w.Rerolls, w.Bans, w.Locks);
            }
            public RunFrame CaptureFrame() => CaptureFrameCore(null);
            public RunFrame CaptureFrame(RunSnapshotBuffer buffer)
            {
                if (buffer is null) { throw new ArgumentNullException(nameof(buffer)); }
                return CaptureFrameCore(buffer);
            }
            private RunFrame CaptureFrameCore(RunSnapshotBuffer? buffer)
            {
                var w = session.Simulation.World; var c = session.Catalog; var map = c.Tuning.World.Map;
                var living = 0;
                for (var i = 0; i < w.Enemies.Count; i++) { if (w.Enemies[i].Health > 0) { living++; } }
                buffer?.Enemies.Begin(living);
                var enemies = buffer?.Enemies.Storage ?? new EnemyView[living];
                var enemyIndex = 0;
                for (var i = 0; i < w.Enemies.Count; i++)
                {
                    var e = w.Enemies[i];
                    if (e.Health > 0) { enemies[enemyIndex++] = new(e.Id, e.Definition, InteractiveState.Point(e.Position), e.Health, c.Enemies[e.Definition].Health); }
                }
                var farms = new FarmView[w.Farms.Count];
                for (var i = 0; i < farms.Length; i++) { var f = w.Farms[i]; farms[i] = new(f.Id, f.Source, InteractiveState.Point(f.Position), f.Stage, f.Stage == c.Tuning.World.Farms.StageTicks.Length - 1); }
                var buildings = new BuildingView[w.Buildings.Count];
                for (var i = 0; i < buildings.Length; i++) { var b = w.Buildings[i]; buildings[i] = new(b.Id, b.Source, InteractiveState.Point(b.Position), b.Built, b.Health); }
                var people = new PersonView[w.People.Count];
                for (var i = 0; i < people.Length; i++) { var person = w.People[i]; people[i] = new(person.Id, person.Source, InteractiveState.Point(person.Position), person.Role, person.Members, person.Health); }
                var loot = w.Runtime?.GroundLoot.Select(l => new GroundLootView(l.Id, l.Item, InteractiveState.Point(l.Position))).ToArray() ?? Array.Empty<GroundLootView>();
                var remains = w.Remains?.Live.Select(r => new RemainView(r.Id, InteractiveState.Point(r.Position))).ToArray() ?? Array.Empty<RemainView>();
                var extent = map.EstateRadius;
                for (var i = 0; i < w.Farms.Count; i++) { var p = w.Farms[i].Position; extent = Math.Max(extent, Math.Max(Math.Abs(p.X - w.Estate.X), Math.Abs(p.Y - w.Estate.Y))); }
                for (var i = 0; i < w.Buildings.Count; i++) { var p = w.Buildings[i].Position; extent = Math.Max(extent, Math.Max(Math.Abs(p.X - w.Estate.X), Math.Abs(p.Y - w.Estate.Y))); }
                return new(w.Tick, Status, w.Season, Math.Max(0, c.Tuning.World.Seasons.Take(w.Season + 1).Sum(s => s.DurationTicks) - w.Tick), c.Tuning.DurationTicks, c.Tuning.TickRate, map.Width, map.Height,
                    new(InteractiveState.Point(w.Lord), InteractiveState.Point(w.WaveRuntime?.Facing ?? w.WeaponCombat?.Facing ?? new Position(1, 0)), w.LordHealth, session.State.Feel?.MaximumHealth(map.LordHealth) ?? map.LordHealth), InteractiveState.Point(w.Estate), w.Experience, session.Simulation.RequiredExperience, w.Level, extent,
                    new(living, people.Length, farms.Length, buildings.Length, session.State.Events.Count(e => e.Kind == PresentationKind.Attack && (e.Shape == "projectile" || e.Shape == "rays"))),
                    buffer is null ? Array.AsReadOnly(enemies) : buffer.Enemies, Array.AsReadOnly(farms), Array.AsReadOnly(buildings), Array.AsReadOnly(people), Array.AsReadOnly(loot), Array.AsReadOnly(remains), Array.AsReadOnly(w.Equipment.Select(e => new EquipmentView(e.Id, e.Level)).ToArray()), Array.AsReadOnly(session.State.Events.ToArray()), session.State.Field?.Frame(w.Estate));
            }
        }
    }
}
