using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    // M3a-1 account estate engine (design #360 section 6, issue #361). Core never reads a clock: every command takes the host's
    // monotonic and wall seconds and first applies the verified interval with the same defence as MetaEngine.AdvanceIdle (the first
    // observation only initialises; going back or a monotonic/wall gap beyond the tolerance credits nothing and re-bases). A failed
    // check holds construction as well as production, so a wall-clock path cannot finish work the verified interval did not cover.
    public static class AccountEstate
    {
        public const string NewOrigin = "new-prototype", MigratedOrigin = "migrated-v5";
        // Production rates are per minute and multipliers per mille: the remainder denominator is seconds x per-mille scale.
        private const long SecondsPerMinute = 60;
        private const long ProductionDenominator = SecondsPerMinute * PlayerInput.Scale;

        public static MetaState NewPrototype(MetaCatalog catalog, AccountEstateConfig config, long monotonicSeconds, long wallSeconds)
        {
            Validate(catalog, config);
            var state = MetaEngine.NewGame(catalog);
            var wallet = new Dictionary<string, int>(state.Wallet);
            foreach (var pair in config.InitialWallet) { wallet[pair.Key] = Math.Min(pair.Value, catalog.Materials.Single(x => x.Id == pair.Key).WalletCap); }
            var estate = Create(config, NewOrigin, kind => kind.StartsComplete ? 1 : 0, config.Queue.AutoStartDefault, monotonicSeconds, wallSeconds);
            return state with { SchemaVersion = MetaSaveCodec.EstateVersion, Wallet = wallet, Estate = estate };
        }

        // 2A manor save migration (design section 6, steps 2-6). The host has already read the old save by its own version (step 1)
        // and settled any pending run under the old rules (step 2), so a pending run is refused here. The four manor levels become
        // completed estate buildings at the same level (capped at the prototype's last level) with no cost or time; wallet, vassals,
        // fragments, priority, claims, receipts and the idle remainder are kept as they are; nothing is re-awarded or back-filled.
        public static MetaState Migrate(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds)
        {
            Validate(catalog, config);
            if (state.Estate != null) { throw new InvalidOperationException("The account estate already exists."); }
            if (state.PendingRun != null) { throw new InvalidOperationException("Settle the pending run under the old rules before the estate migration."); }
            // Auto start needs the player's explicit consent after a migration (step 4).
            var estate = Create(config, MigratedOrigin, kind => kind.StartsComplete ? 1 : state.ManorLevels.TryGetValue(kind.Kind, out int level) ? Math.Min(level, kind.MaxLevel()) : 0,
                false, monotonicSeconds, wallSeconds);
            return MetaEngine.CloneState(state) with { SchemaVersion = MetaSaveCodec.EstateVersion, Estate = estate };
        }

        public static AccountEstateResult Advance(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, _ => (true, ""));

        // Starts the next level now when nothing is under construction, or reserves it (no cost until it starts) while one is.
        public static AccountEstateResult StartConstruction(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, int buildingId, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, s => s.Start(buildingId));

        public static AccountEstateResult StartReserved(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, s => s.StartFirstReservation());

        public static AccountEstateResult CancelReservation(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, int buildingId, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, s => s.CancelReservation(buildingId));

        public static AccountEstateResult SetAutoStart(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, bool enabled, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, s => { s.AutoStart = enabled; if (enabled) { s.StartFirstReservation(); } return (true, ""); });

        public static AccountEstateResult Move(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, int buildingId, int x, int y, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, s => s.Move(buildingId, x, y));

        public static AccountEstateResult Specialize(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, int buildingId, string branch, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, s => s.Specialize(buildingId, branch));

        public static AccountEstateResult ClaimProduction(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, s => s.ClaimProduction());

        public static AccountEstateResult ClaimTask(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, string taskId, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, s => s.ClaimTask(taskId));

        public static AccountEstateResult StartZoneUnlock(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds, string zoneId, AccountEstateBonuses? bonuses = null) =>
            Run(catalog, config, state, monotonicSeconds, wallSeconds, bonuses, s => s.StartZoneUnlock(zoneId));

        // The part of the protected first production path (design section 6.1) that is not started yet: other work may not spend it.
        public static Dictionary<string, int> ProtectedReserve(AccountEstateConfig config, MetaState state) =>
            new Session(config, state).ProtectedReserve();

        public static int StockCap(AccountEstateConfig config, MetaState state, string resource) => new Session(config, state).StockCap(resource);
        public static long StorageSeconds(AccountEstateConfig config, MetaState state, AccountEstateBonuses? bonuses = null) =>
            new Session(config, state) { Bonuses = bonuses ?? AccountEstateBonuses.None }.StorageSeconds();

        private static AccountEstateResult Run(MetaCatalog catalog, AccountEstateConfig config, MetaState state, long monotonicSeconds, long wallSeconds,
            AccountEstateBonuses? bonuses, Func<Session, (bool Done, string Reason)> command)
        {
            var inputs = bonuses ?? AccountEstateBonuses.None;
            inputs.Validate(config);
            if (state.Estate == null) { throw new InvalidOperationException("No account estate."); }
            if (state.Estate.ConfigVersion != config.Version) { throw new InvalidOperationException("Estate config version mismatch."); }
            if (monotonicSeconds < 0 || wallSeconds < 0) { throw new ArgumentOutOfRangeException(nameof(monotonicSeconds)); }
            var estate = state.Estate;
            bool initial = estate.LastMonotonicSeconds < 0 || estate.LastWallSeconds < 0;
            long mono = monotonicSeconds - estate.LastMonotonicSeconds, wall = wallSeconds - estate.LastWallSeconds;
            bool valid = !initial && mono >= 0 && wall >= 0 && Math.Abs((decimal)mono - wall) <= catalog.Economy.ClockToleranceSeconds;
            long seconds = valid ? Math.Min(mono, config.MaximumIntervalSeconds) : 0;
            var session = new Session(config, MetaEngine.CloneState(state)) { LastMonotonic = monotonicSeconds, LastWall = wallSeconds, Bonuses = inputs };
            session.Progress(seconds);
            var (done, reason) = command(session);
            return new AccountEstateResult(session.ToState(), initial ? AccountEstateClock.Initialized : valid ? AccountEstateClock.Accepted : AccountEstateClock.ClockMismatch,
                seconds, done, reason, session.Moved);
        }

        private static MetaEstateState Create(AccountEstateConfig config, string origin, Func<AccountEstateBuildingKind, int> level, bool autoStart, long mono, long wall)
        {
            var buildings = config.Buildings.Select((kind, i) => new MetaEstateBuilding(i + 1, kind.Kind, kind.X, kind.Y, level(kind))).ToArray();
            return new MetaEstateState(config.Version, origin, mono, wall, 0, config.Zones.Where(x => x.OpenAtStart).Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                buildings, null, Array.Empty<MetaEstateReservation>(), null, autoStart, Array.Empty<string>(), Array.Empty<string>(), buildings.Length + 1, false, false);
        }

        public static void Validate(MetaCatalog catalog, AccountEstateConfig config)
        {
            void Check(bool ok, string what) { if (!ok) { throw new ArgumentException("Invalid estate config: " + what); } }
            var materials = catalog.Materials.ToDictionary(x => x.Id);
            bool Materials(Dictionary<string, int> values) => values.All(x => materials.ContainsKey(x.Key) && x.Value >= 0);
            Check(!string.IsNullOrWhiteSpace(config.Version) && config.MaximumIntervalSeconds > 0, "version and interval");
            Check(Materials(config.InitialWallet) && Materials(config.WalletCaps) && config.WalletCaps.All(x => x.Value <= materials[x.Key].WalletCap)
                && config.InitialWallet.All(x => config.WalletCaps.TryGetValue(x.Key, out int cap) && x.Value <= cap), "wallet");
            Check(config.Queue.Reservations >= 0, "queue");
            var kinds = config.Buildings.Select(x => x.Kind).ToArray();
            Check(kinds.Length > 0 && kinds.Distinct(StringComparer.Ordinal).Count() == kinds.Length, "unique kinds");
            foreach (var kind in config.Buildings)
            {
                Check(kind.Width > 0 && kind.Height > 0 && (kind.StartsComplete ? kind.Levels.Length == 0 : kind.Levels.Length > 0), kind.Kind + " shape and levels");
                Check(kind.Levels.All(x => x.Seconds > 0 && Materials(x.Cost)), kind.Kind + " level costs");
                Check(kind.Parent == null || config.Buildings.Any(x => x.Kind == kind.Parent && x.Parent == null && !x.StartsComplete), kind.Kind + " parent");
                Check(kind.Produces == null || (kind.Parent != null && config.Storage.BaseStock.ContainsKey(kind.Produces.Resource) && kind.Produces.PerMinute > 0), kind.Kind + " production");
                Check(kind.Branches == null || kind.Branches.Distinct(StringComparer.Ordinal).Count() == kind.Branches.Length, kind.Kind + " branches");
            }
            int maxLevel = config.Buildings.Max(x => x.MaxLevel());
            var storage = config.Buildings.SingleOrDefault(x => x.Kind == config.Storage.StorageKind);
            Check(storage != null && config.Storage.GranaryLevelSeconds.Length > storage.MaxLevel() && config.Storage.GranaryLevelStockPermille.Length > storage.MaxLevel()
                && config.Storage.GranaryLevelSeconds.All(x => x > 0 && x <= config.Storage.MaximumSeconds) && config.Storage.GranaryLevelStockPermille.All(x => x > 0 && x <= config.Storage.MaximumStockPermille)
                && Materials(config.Storage.BaseStock) && config.Storage.LongAwayBonusSeconds >= 0 && config.Storage.LongAwayStockPermille >= 0, "storage");
            Check(config.Production.ParentLevelPermille.Length > maxLevel && config.Production.ParentLevelPermille[0] == 0 && config.Production.ParentLevelPermille.All(x => x >= 0)
                && config.Production.BranchBonus.All(x => x.Permille >= 0 && config.Buildings.Any(k => k.Branches?.Contains(x.Branch) == true) && materials.ContainsKey(x.Resource)), "production");
            Check(config.Construction.ReductionPermille >= 0 && config.Construction.MaximumReductionPermille >= 0 && config.Construction.MaximumReductionPermille < PlayerInput.Scale, "construction");
            Check(config.ProtectedPath.All(x => kinds.Contains(x)) && config.ProtectedPath.Distinct().Count() == config.ProtectedPath.Length, "protected path");
            Check(config.Tasks.Select(x => x.Id).Distinct().Count() == config.Tasks.Length && config.Tasks.All(x => Materials(x.Reward) && KnownTrigger(x.Trigger)), "tasks");
            Check(config.Zones.Length > 0 && config.Zones.Select(x => x.Id).Distinct().Count() == config.Zones.Length && config.Zones.Any(x => x.OpenAtStart)
                && config.Zones.All(x => x.Width > 0 && x.Height > 0 && (x.OpenAtStart || x.Unlock != null) && (x.Unlock == null || (x.Unlock.Seconds > 0 && Materials(x.Unlock.Cost)))), "zones");
            for (int a = 0; a < config.Zones.Length; a++) { for (int b = a + 1; b < config.Zones.Length; b++) { Check(!Overlap(config.Zones[a].X, config.Zones[a].Y, config.Zones[a].Width, config.Zones[a].Height, config.Zones[b].X, config.Zones[b].Y, config.Zones[b].Width, config.Zones[b].Height), "zone overlap"); } }
            var start = config.Zones.Where(x => x.OpenAtStart).ToArray();
            for (int a = 0; a < config.Buildings.Length; a++)
            {
                var k = config.Buildings[a];
                Check(Inside(start, k.X, k.Y, k.Width, k.Height), k.Kind + " starts inside an open zone");
                for (int b = a + 1; b < config.Buildings.Length; b++) { var o = config.Buildings[b]; Check(!Overlap(k.X, k.Y, k.Width, k.Height, o.X, o.Y, o.Width, o.Height), k.Kind + " overlaps " + o.Kind); }
            }
        }

        private static bool KnownTrigger(string trigger) => trigger is "first-production-claim" or "first-building-move" ||
            (trigger.StartsWith("production-sites-complete-", StringComparison.Ordinal) && int.TryParse(trigger.Substring("production-sites-complete-".Length), out int n) && n > 0);
        private static bool Overlap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) => ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;
        private static bool Inside(IEnumerable<AccountEstateZone> zones, int x, int y, int w, int h)
        {
            var open = zones.ToArray();
            for (int cx = x; cx < x + w; cx++) { for (int cy = y; cy < y + h; cy++) { if (!open.Any(z => cx >= z.X && cx < z.X + z.Width && cy >= z.Y && cy < z.Y + z.Height)) { return false; } } }
            return true;
        }

        // A working copy: commands mutate it, then ToState builds fresh immutable arrays (nothing shared with the input is changed).
        private sealed class Session
        {
            private readonly AccountEstateConfig config;
            private readonly MetaState state;
            private readonly List<MetaEstateBuilding> buildings;
            private readonly List<MetaEstateReservation> reservations;
            private readonly List<string> openZones;
            private readonly SortedSet<string> achieved, claimed;
            private MetaEstateWork? active;
            private MetaEstateZoneWork? zoneWork;
            private long elapsed;
            private bool firstClaim, firstMove;
            public bool AutoStart;
            public AccountEstateBonuses Bonuses = AccountEstateBonuses.None;
            public long LastMonotonic, LastWall;
            public readonly Dictionary<string, int> Moved = new();

            public Session(AccountEstateConfig config, MetaState state)
            {
                this.config = config; this.state = state;
                var e = state.Estate!;
                buildings = e.Buildings.ToList(); reservations = e.Reservations.ToList(); openZones = e.OpenZones.ToList();
                achieved = new SortedSet<string>(e.AchievedTasks, StringComparer.Ordinal); claimed = new SortedSet<string>(e.ClaimedTasks, StringComparer.Ordinal);
                active = e.Active; zoneWork = e.ZoneWork; elapsed = e.ElapsedSeconds; firstClaim = e.FirstClaimDone; firstMove = e.FirstMoveDone; AutoStart = e.AutoStart;
                LastMonotonic = e.LastMonotonicSeconds; LastWall = e.LastWallSeconds;
            }

            public MetaState ToState()
            {
                var e = state.Estate!;
                return state with
                {
                    Estate = new MetaEstateState(e.ConfigVersion, e.Origin, LastMonotonic, LastWall, elapsed, openZones.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                        buildings.ToArray(), active, reservations.ToArray(), zoneWork, AutoStart, achieved.ToArray(), claimed.ToArray(), e.NextBuildingId, firstClaim, firstMove)
                };
            }

            private AccountEstateBuildingKind Kind(string kind) => config.Buildings.Single(x => x.Kind == kind);
            private int Index(int id) => buildings.FindIndex(x => x.Id == id);
            private MetaEstateBuilding? FirstOfKind(string kind) => buildings.Where(x => x.Kind == kind).OrderBy(x => x.Id).FirstOrDefault();
            private int LevelOf(string kind) => FirstOfKind(kind)?.Level ?? 0;
            private bool HasBranch(string branch) => buildings.Any(x => x.Branch == branch);
            private int ProductionSitesComplete => buildings.Count(x => Kind(x.Kind).Produces != null && x.Level >= 1);
            private bool InOpenZone(int x, int y, int w, int h) => Inside(config.Zones.Where(z => openZones.Contains(z.Id)), x, y, w, h);

            // Seconds of production a site may hold since its stock was last emptied, by the storage building's level and branch.
            public long StorageSeconds()
            {
                var granary = FirstOfKind(config.Storage.StorageKind);
                int level = granary?.Level ?? 0;
                long seconds = config.Storage.GranaryLevelSeconds[Math.Min(level, config.Storage.GranaryLevelSeconds.Length - 1)];
                if (granary?.Branch == config.Storage.LongAwayBranch) { seconds += config.Storage.LongAwayBonusSeconds; }
                // ADR0079: extra storage seconds still end at the configured maximum, which includes the base time.
                seconds = checked(seconds + Bonuses.StorageSeconds);
                return Math.Min(seconds, config.Storage.MaximumSeconds);
            }

            public int StockCap(string resource)
            {
                if (!config.Storage.BaseStock.TryGetValue(resource, out int baseStock)) { return 0; }
                var granary = FirstOfKind(config.Storage.StorageKind);
                int level = granary?.Level ?? 0;
                long permille = config.Storage.GranaryLevelStockPermille[Math.Min(level, config.Storage.GranaryLevelStockPermille.Length - 1)];
                if (granary?.Branch == config.Storage.LongAwayBranch) { permille += config.Storage.LongAwayStockPermille; }
                permille = Math.Min(permille, config.Storage.MaximumStockPermille);
                return checked((int)(baseStock * permille / PlayerInput.Scale));
            }

            public Dictionary<string, int> ProtectedReserve()
            {
                var reserve = new Dictionary<string, int>();
                foreach (string kindId in config.ProtectedPath)
                {
                    var kind = Kind(kindId);
                    foreach (var building in buildings.Where(x => x.Kind == kindId && x.Level == 0 && active?.BuildingId != x.Id))
                    {
                        foreach (var pair in kind.Levels[0].Cost) { reserve[pair.Key] = (reserve.TryGetValue(pair.Key, out int value) ? value : 0) + pair.Value; }
                    }
                }
                return reserve;
            }

            // Applies a verified interval in event order: production up to each construction or zone completion, then the completion.
            public void Progress(long seconds)
            {
                long left = seconds;
                while (left > 0)
                {
                    long step = left;
                    if (active != null) { step = Math.Min(step, active.RemainingSeconds); }
                    if (zoneWork != null) { step = Math.Min(step, zoneWork.RemainingSeconds); }
                    Produce(step); elapsed += step; left -= step;
                    if (zoneWork != null)
                    {
                        zoneWork = zoneWork with { RemainingSeconds = zoneWork.RemainingSeconds - step };
                        if (zoneWork.RemainingSeconds == 0) { openZones.Add(zoneWork.ZoneId); zoneWork = null; }
                    }
                    if (active != null)
                    {
                        active = active with { RemainingSeconds = active.RemainingSeconds - step };
                        if (active.RemainingSeconds == 0)
                        {
                            int index = Index(active.BuildingId);
                            buildings[index] = buildings[index] with { Level = active.TargetLevel };
                            active = null;
                            UpdateTasks();
                            if (AutoStart) { StartFirstReservation(); }
                        }
                    }
                }
                UpdateTasks();
            }

            private void Produce(long seconds)
            {
                if (seconds <= 0) { return; }
                long storage = StorageSeconds();
                for (int i = 0; i < buildings.Count; i++)
                {
                    var site = buildings[i]; var kind = Kind(site.Kind);
                    if (kind.Produces == null || site.Level < 1) { continue; }
                    var parent = FirstOfKind(kind.Parent!);
                    int parentLevel = parent?.Level ?? 0;
                    long permille = config.Production.ParentLevelPermille[Math.Min(parentLevel, config.Production.ParentLevelPermille.Length - 1)];
                    if (parentLevel > 0 && parent!.Branch is { } branch)
                    {
                        permille += config.Production.BranchBonus.Where(x => x.Branch == branch && x.Resource == kind.Produces.Resource).Sum(x => (long)x.Permille);
                    }
                    // ADR0079: the bonus is a share of the site's current rate; floor keeps the remainder unit unchanged.
                    if (Bonuses.ProductionPermille.TryGetValue(kind.Produces.Resource, out int bonus) && bonus > 0)
                    {
                        permille = checked(permille + permille * bonus / PlayerInput.Scale);
                    }
                    long allowed = Math.Max(0, Math.Min(seconds, storage - site.StoredSeconds));
                    if (permille <= 0 || allowed == 0) { continue; }
                    long numerator = checked(site.Remainder + allowed * kind.Produces.PerMinute * permille);
                    long units = numerator / ProductionDenominator, remainder = numerator % ProductionDenominator;
                    long room = StockCap(kind.Produces.Resource) - site.Stock;
                    // A full stock discards the overflow and keeps no hidden remainder (design section 6.1).
                    if (units >= room) { buildings[i] = site with { Stock = site.Stock + (int)Math.Max(0, room), Remainder = 0, StoredSeconds = site.StoredSeconds + allowed }; }
                    else { buildings[i] = site with { Stock = site.Stock + (int)units, Remainder = remainder, StoredSeconds = site.StoredSeconds + allowed }; }
                }
            }

            private (bool, string) CanStart(MetaEstateBuilding building, out AccountEstateBuildingKind kind)
            {
                kind = Kind(building.Kind);
                if (building.Level >= kind.MaxLevel()) { return (false, "max-level"); }
                var k = kind;
                if (!InOpenZone(building.X, building.Y, k.Width, k.Height)) { return (false, "zone-closed"); }
                if (kind.Parent != null && LevelOf(kind.Parent) < 1) { return (false, "parent-required"); }
                return (true, "");
            }

            private (bool, string) Pay(AccountEstateBuildingKind kind, int targetLevel, Dictionary<string, int> cost)
            {
                var reserve = ProtectedReserve();
                bool protectedStep = targetLevel == 1 && config.ProtectedPath.Contains(kind.Kind);
                foreach (var material in cost.Keys.Union(reserve.Keys))
                {
                    int have = state.Wallet.TryGetValue(material, out int w) ? w : 0, pay = cost.TryGetValue(material, out int c) ? c : 0;
                    int keep = (reserve.TryGetValue(material, out int r) ? r : 0) - (protectedStep ? pay : 0);
                    if (have < pay) { return (false, "insufficient"); }
                    if (have - pay < keep) { return (false, "protected-reserve"); }
                }
                foreach (var pair in cost) { state.Wallet[pair.Key] -= pair.Value; }
                return (true, "");
            }

            private (bool, string) Begin(MetaEstateBuilding building, int targetLevel)
            {
                var (ok, reason) = CanStart(building, out var kind);
                if (!ok) { return (false, reason); }
                var step = kind.Levels[targetLevel - 1];
                (ok, reason) = Pay(kind, targetLevel, step.Cost);
                if (!ok) { return (false, reason); }
                int reduction = HasBranch(config.Construction.ReductionBranch) ? Math.Min(config.Construction.ReductionPermille, config.Construction.MaximumReductionPermille) : 0;
                long seconds = Math.Max(1, (long)step.Seconds * (PlayerInput.Scale - reduction) / PlayerInput.Scale);
                if (Bonuses.ConstructionSpeedPermille > 0)
                {
                    // ADR0079 speed form: ceil(seconds * 1000 / (1000 + bonus)), fixed at start like the branch reduction.
                    long divisor = PlayerInput.Scale + Bonuses.ConstructionSpeedPermille;
                    seconds = Math.Max(1, (checked(seconds * PlayerInput.Scale) + divisor - 1) / divisor);
                }
                active = new MetaEstateWork(building.Id, targetLevel, seconds, seconds);
                return (true, "started");
            }

            public (bool, string) Start(int buildingId)
            {
                int index = Index(buildingId);
                if (index < 0) { return (false, "unknown-building"); }
                var building = buildings[index];
                if (active?.BuildingId == buildingId || reservations.Any(x => x.BuildingId == buildingId)) { return (false, "already-queued"); }
                if (active == null) { return Begin(building, building.Level + 1); }
                var (ok, reason) = CanStartLater(building);
                if (!ok) { return (false, reason); }
                if (reservations.Count >= config.Queue.Reservations) { return (false, "queue-full"); }
                reservations.Add(new MetaEstateReservation(buildingId, building.Level + 1));
                return (true, "reserved");
            }

            // A reservation may wait for its parent to finish (the parent can be the work in progress); other checks happen at start.
            private (bool, string) CanStartLater(MetaEstateBuilding building)
            {
                var kind = Kind(building.Kind);
                if (building.Level >= kind.MaxLevel()) { return (false, "max-level"); }
                if (!InOpenZone(building.X, building.Y, kind.Width, kind.Height)) { return (false, "zone-closed"); }
                return (true, "");
            }

            public (bool, string) StartFirstReservation()
            {
                if (active != null) { return (false, "busy"); }
                if (reservations.Count == 0) { return (false, "no-reservation"); }
                var first = reservations[0];
                int index = Index(first.BuildingId);
                if (buildings[index].Level + 1 != first.TargetLevel) { reservations.RemoveAt(0); return (false, "stale-reservation"); }
                var (ok, reason) = Begin(buildings[index], first.TargetLevel);
                if (ok) { reservations.RemoveAt(0); }
                return (ok, reason);
            }

            public (bool, string) CancelReservation(int buildingId)
            {
                int removed = reservations.RemoveAll(x => x.BuildingId == buildingId);
                return removed > 0 ? (true, "cancelled") : (false, "not-reserved");
            }

            public (bool, string) Move(int buildingId, int x, int y)
            {
                int index = Index(buildingId);
                if (index < 0) { return (false, "unknown-building"); }
                var building = buildings[index]; var kind = Kind(building.Kind);
                if (active?.BuildingId == buildingId) { return (false, "under-construction"); }
                if (!InOpenZone(x, y, kind.Width, kind.Height)) { return (false, "outside-open-zone"); }
                foreach (var other in buildings)
                {
                    if (other.Id == buildingId) { continue; }
                    var o = Kind(other.Kind);
                    if (Overlap(x, y, kind.Width, kind.Height, other.X, other.Y, o.Width, o.Height)) { return (false, "overlaps:" + other.Id); }
                }
                if (building.X == x && building.Y == y) { return (true, "unchanged"); }
                buildings[index] = building with { X = x, Y = y };
                firstMove = true; UpdateTasks();
                return (true, "moved");
            }

            public (bool, string) Specialize(int buildingId, string branch)
            {
                int index = Index(buildingId);
                if (index < 0) { return (false, "unknown-building"); }
                if (state.PendingRun != null) { return (false, "run-pending"); }
                var building = buildings[index]; var kind = Kind(building.Kind);
                if (kind.Branches == null || !kind.Branches.Contains(branch)) { return (false, "unknown-branch"); }
                if (building.Level < kind.MaxLevel()) { return (false, "level-required"); }
                buildings[index] = building with { Branch = branch };
                return (true, "specialized");
            }

            // Moves only what fits under the estate wallet caps; the rest stays in the site's stock (no unbounded mailbox).
            public (bool, string) ClaimProduction()
            {
                bool any = false;
                for (int i = 0; i < buildings.Count; i++)
                {
                    var site = buildings[i]; var kind = Kind(site.Kind);
                    if (kind.Produces == null || site.Stock == 0) { continue; }
                    string resource = kind.Produces.Resource;
                    int room = Math.Max(0, config.WalletCaps[resource] - (state.Wallet.TryGetValue(resource, out int have) ? have : 0));
                    int moved = Math.Min(room, site.Stock);
                    if (moved == 0) { continue; }
                    state.Wallet[resource] = have + moved;
                    Moved[resource] = (Moved.TryGetValue(resource, out int sum) ? sum : 0) + moved;
                    int stock = site.Stock - moved;
                    buildings[i] = site with { Stock = stock, StoredSeconds = stock == 0 ? 0 : site.StoredSeconds };
                    any = true;
                }
                if (any) { firstClaim = true; UpdateTasks(); }
                return any ? (true, "claimed") : (false, "nothing-to-claim");
            }

            // Each task pays once; a reward that does not fit the wallet caps waits instead of being cut.
            public (bool, string) ClaimTask(string taskId)
            {
                var task = config.Tasks.SingleOrDefault(x => x.Id == taskId);
                if (task == null) { return (false, "unknown-task"); }
                if (claimed.Contains(taskId)) { return (false, "already-claimed"); }
                if (!achieved.Contains(taskId)) { return (false, "not-achieved"); }
                foreach (var pair in task.Reward)
                {
                    int have = state.Wallet.TryGetValue(pair.Key, out int value) ? value : 0;
                    if (have + pair.Value > config.WalletCaps[pair.Key]) { return (false, "wallet-full"); }
                }
                foreach (var pair in task.Reward)
                {
                    state.Wallet[pair.Key] = (state.Wallet.TryGetValue(pair.Key, out int value) ? value : 0) + pair.Value;
                    Moved[pair.Key] = (Moved.TryGetValue(pair.Key, out int sum) ? sum : 0) + pair.Value;
                }
                claimed.Add(taskId);
                return (true, "claimed");
            }

            public (bool, string) StartZoneUnlock(string zoneId)
            {
                var zone = config.Zones.SingleOrDefault(x => x.Id == zoneId);
                if (zone?.Unlock == null) { return (false, "unknown-zone"); }
                if (openZones.Contains(zoneId)) { return (false, "already-open"); }
                if (zoneWork != null) { return (false, "busy"); }
                if (!config.Zones.Any(z => openZones.Contains(z.Id) && Adjacent(z, zone))) { return (false, "not-adjacent"); }
                if (ProductionSitesComplete < zone.Unlock.CompletedProductionSites) { return (false, "production-sites-required"); }
                var reserve = ProtectedReserve();
                foreach (var material in zone.Unlock.Cost.Keys.Union(reserve.Keys))
                {
                    int have = state.Wallet.TryGetValue(material, out int w) ? w : 0, pay = zone.Unlock.Cost.TryGetValue(material, out int c) ? c : 0;
                    if (have < pay) { return (false, "insufficient"); }
                    if (have - pay < (reserve.TryGetValue(material, out int r) ? r : 0)) { return (false, "protected-reserve"); }
                }
                foreach (var pair in zone.Unlock.Cost) { state.Wallet[pair.Key] -= pair.Value; }
                zoneWork = new MetaEstateZoneWork(zoneId, zone.Unlock.Seconds);
                return (true, "started");
            }

            private static bool Adjacent(AccountEstateZone a, AccountEstateZone b) =>
                (a.X + a.Width == b.X || b.X + b.Width == a.X) && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height ||
                (a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && a.X < b.X + b.Width && b.X < a.X + a.Width;

            private void UpdateTasks()
            {
                foreach (var task in config.Tasks)
                {
                    if (achieved.Contains(task.Id)) { continue; }
                    bool done = task.Trigger switch
                    {
                        "first-production-claim" => firstClaim,
                        "first-building-move" => firstMove,
                        _ => ProductionSitesComplete >= int.Parse(task.Trigger.Substring("production-sites-complete-".Length))
                    };
                    if (done) { achieved.Add(task.Id); }
                }
            }
        }
    }
}
