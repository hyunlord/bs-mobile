using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // Estate map prototype (#366, #368, #370): a development-only screen over the M3a-1 Core estate. It works on an in-memory
    // copy and never writes the save: the account's estate when it has one, otherwise a new prototype estate (design 6.1 first
    // visit: home, ruin sites, the prototype starting wallet). Every command goes through AccountEstate at the copy's own clock,
    // which follows real time from the moment the copy was made plus any development time skip; Core verifies each interval.
    public sealed class EstateMapScreen
    {
        // Temporary names for the M3a kinds that have no catalog building yet (design #360 5.1 reserved names).
        static readonly Dictionary<string, string> KindNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["m3a:home"] = "우리 집", ["m3a:watchtower"] = "망루", ["m3a:well_shrine"] = "우물",
            ["m3a:field"] = "경작지", ["m3a:lumber_camp"] = "벌목장", ["m3a:ironworks"] = "철공소",
        };
        // Existing manor sprites; the other kinds use colour blocks until Astra's estate art is approved.
        static readonly Dictionary<string, string> KindArt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["meta:forge"] = "b-forge", ["meta:scriptorium"] = "b-scriptorium", ["meta:granary"] = "b-granary", ["meta:barracks"] = "b-barracks",
        };
        // Branch names and run-side summaries from design #360 section 7 and the r2 board 03 [가설]; estate-side effects are
        // computed from the config instead.
        static readonly Dictionary<string, (string Name, string Run)> Branches = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["granary:expedition-supply"] = ("원정 보급 창고", "출정 보급 식량 [가설] · 판 효과는 M3b에서 연결"),
            ["granary:long-away"] = ("장기 보관 창고", "오래 비워도 생산이 덜 멈춤"),
            ["forge:sword-workshop"] = ("검술 공방", "시작 무기 단계 [가설] · 판 효과는 M3b에서 연결"),
            ["forge:supply-workshop"] = ("보급 공방", "영지 철재 생산에 집중"),
            ["scriptorium:card-research"] = ("카드 연구실", "카드 다시 뽑기 [가설] · 판 효과는 M3b에서 연결"),
            ["scriptorium:city-records"] = ("도시 기록소", "영지 기록 [가설] · 효과는 후속 설계"),
            ["barracks:escort"] = ("호위 병영", "시작 병사 [가설] · 판 효과는 M3b에서 연결"),
            ["barracks:construction-support"] = ("건설 지원 병영", "이후 공사 시간을 줄임"),
        };

        readonly Func<double> realtime;
        readonly double startedAt;
        readonly long baseMonotonic, baseWall;
        long skipped;

        public MetaCatalog Catalog { get; }
        public AccountEstateConfig Config { get; }
        public MetaState State { get; private set; }
        public EstateMapModel Map { get; }
        public bool FromSave { get; }
        public int? Selected { get; private set; }
        public bool Moving { get; private set; }
        public Vector2Int Preview { get; private set; }
        public bool PreviewValid { get; private set; }
        public string PreviewReason { get; private set; } = "";
        public string Notice { get; private set; } = "";
        public bool GoalCollapsed { get; private set; }
        public bool Specializing { get; private set; }
        public string BranchChoice { get; private set; }
        public string ZoneChoice { get; private set; }
        public bool QueueOpen { get; private set; }

        // `map` keeps the player's zoom and position when the copy is rebuilt; `realtime` is seconds since an arbitrary origin
        // (Time.realtimeSinceStartupAsDouble in the app, a fixed value in tests).
        public EstateMapScreen(MetaCatalog catalog, AccountEstateConfig config, MetaState account, EstateMapModel map = null, Func<double> realtime = null)
        {
            Catalog = catalog; Config = config ?? throw new ArgumentNullException(nameof(config));
            this.realtime = realtime ?? (() => 0); startedAt = this.realtime();
            FromSave = account.Estate != null;
            State = FromSave ? account : AccountEstate.NewPrototype(catalog, config, 0, 0);
            baseMonotonic = Math.Max(0, State.Estate.LastMonotonicSeconds); baseWall = Math.Max(0, State.Estate.LastWallSeconds);
            Map = map ?? new EstateMapModel(config.Zones.Max(z => z.X + z.Width), config.Zones.Max(z => z.Y + z.Height));
        }

        // The copy's clock: whole seconds of real time since it was made plus the development skip, on both Core clocks.
        long Elapsed => (long)Math.Floor(Math.Max(0, realtime() - startedAt)) + skipped;
        long Monotonic => baseMonotonic + Elapsed;
        long Wall => baseWall + Elapsed;

        public MetaEstateBuilding Building(int id) => State.Estate.Buildings.Single(b => b.Id == id);
        public AccountEstateBuildingKind Kind(string kind) => Config.Buildings.Single(k => k.Kind == kind);
        public bool Open(AccountEstateZone zone) => State.Estate.OpenZones.Contains(zone.Id);
        public bool UnderConstruction(int id) => State.Estate.Active?.BuildingId == id;
        public bool Reserved(int id) => State.Estate.Reservations.Any(r => r.BuildingId == id);
        public static string Art(string kind) => KindArt.TryGetValue(kind, out var art) ? art : null;
        public string BranchName(string branch) => Branches.TryGetValue(branch, out var value) ? value.Name : branch;

        public string Name(string kind)
        {
            if (KindNames.TryGetValue(kind, out var name)) return name;
            return Catalog.ManorBuildings.FirstOrDefault(b => b.Id == kind)?.Name ?? kind;
        }

        // The building whose footprint contains the cell; the map's touch test is the isometric grid, not sprite rectangles.
        public int? BuildingAt(Vector2Int cell)
        {
            foreach (var building in State.Estate.Buildings)
            {
                var kind = Kind(building.Kind);
                if (cell.x >= building.X && cell.x < building.X + kind.Width && cell.y >= building.Y && cell.y < building.Y + kind.Height) return building.Id;
            }
            return null;
        }

        public AccountEstateZone ZoneAt(Vector2Int cell) =>
            Config.Zones.FirstOrDefault(z => cell.x >= z.X && cell.x < z.X + z.Width && cell.y >= z.Y && cell.y < z.Y + z.Height);

        public Vector2 Centre(int id)
        {
            var building = Building(id); var kind = Kind(building.Kind);
            return new Vector2(building.X + kind.Width * .5f, building.Y + kind.Height * .5f);
        }

        public Vector2 HomeCentre()
        {
            var home = State.Estate.Buildings.Select(b => (b, k: Kind(b.Kind))).FirstOrDefault(x => x.k.StartsComplete);
            return home.k == null ? new Vector2(Map.Columns, Map.Rows) * .5f : new Vector2(home.b.X + home.k.Width * .5f, home.b.Y + home.k.Height * .5f);
        }

        public void ToggleGoal() => GoalCollapsed = !GoalCollapsed;
        public void ToggleQueue() { var open = !QueueOpen; Select(null); QueueOpen = open; }

        // ---- Time ----

        // Applies the verified interval up to now. True when something the page draws changed (a level, the queue, a zone, a
        // task, or site stock appearing or running out, which adds or removes the claim button), so the caller redraws; the
        // remaining time and the stock amounts are refreshed in place by the caller instead.
        public bool Advance()
        {
            var before = Shape(State.Estate);
            State = AccountEstate.Advance(Catalog, Config, State, Monotonic, Wall).State;
            var changed = Shape(State.Estate) != before;
            if (changed && Completed(before) is { Length: > 0 } done) Notice = done;
            return changed;
        }

        // Development only: moves the copy's clock forward; Core still applies the interval through its own checks and caps.
        public bool SkipTime(long seconds) { skipped += Math.Max(0, seconds); return Advance(); }

        static string Shape(MetaEstateState estate) =>
            string.Join(",", estate.Buildings.Select(b => b.Level + (b.Branch ?? ""))) + "|" + estate.Active?.BuildingId + "|" +
            string.Join(",", estate.Reservations.Select(r => r.BuildingId)) + "|" + string.Join(",", estate.OpenZones) + "|" +
            estate.ZoneWork?.ZoneId + "|" + string.Join(",", estate.AchievedTasks) + "|" + (estate.Buildings.Any(b => b.Stock > 0) ? "stock" : "");

        string Completed(string before)
        {
            var levels = before.Split('|')[0].Split(',');
            var buildings = State.Estate.Buildings;
            for (var i = 0; i < buildings.Length && i < levels.Length; i++)
                if (!levels[i].StartsWith(buildings[i].Level.ToString(), StringComparison.Ordinal))
                    return Name(buildings[i].Kind) + " Lv " + buildings[i].Level + " 완공";
            return "";
        }

        // ---- Commands (each one runs Core at the copy's clock and keeps Core's reason) ----

        AccountEstateResult Apply(AccountEstateResult result, Func<string, string> notice)
        {
            State = result.State; Notice = notice(result.Reason); return result;
        }

        public bool StartWork(int id) => Apply(AccountEstate.StartConstruction(Catalog, Config, State, Monotonic, Wall, id),
            reason => reason == "started" ? Name(Building(id).Kind) + " 공사 시작" : reason == "reserved" ? Name(Building(id).Kind) + " 예약" : ReasonText(reason)).Done;

        public bool CancelReservation(int id) => Apply(AccountEstate.CancelReservation(Catalog, Config, State, Monotonic, Wall, id),
            reason => reason == "cancelled" ? "예약을 취소했습니다" : ReasonText(reason)).Done;

        public bool StartReserved() => Apply(AccountEstate.StartReserved(Catalog, Config, State, Monotonic, Wall),
            reason => reason == "started" ? "예약한 공사를 시작했습니다" : ReasonText(reason)).Done;

        public bool ToggleAutoStart()
        {
            var enable = !State.Estate.AutoStart;
            return Apply(AccountEstate.SetAutoStart(Catalog, Config, State, Monotonic, Wall, enable), _ => enable ? "자동 착공을 켰습니다" : "자동 착공을 껐습니다").Done;
        }

        public bool ClaimProduction()
        {
            var result = Apply(AccountEstate.ClaimProduction(Catalog, Config, State, Monotonic, Wall), ReasonText);
            if (result.Done) Notice = "받음 · " + Amounts(result.Moved);
            return result.Done;
        }

        public bool ClaimGoal()
        {
            var task = GoalTask(); if (task == null) return false;
            var result = Apply(AccountEstate.ClaimTask(Catalog, Config, State, Monotonic, Wall, task.Id), ReasonText);
            if (result.Done) Notice = "과제 보상 받음 · " + Amounts(result.Moved);
            return result.Done;
        }

        public bool OpenZone(string zoneId)
        {
            var done = Apply(AccountEstate.StartZoneUnlock(Catalog, Config, State, Monotonic, Wall, zoneId),
                reason => reason == "started" ? "구역 개방 준비 시작" : ReasonText(reason)).Done;
            if (done) ZoneChoice = null;
            return done;
        }

        public void BeginSpecialize()
        {
            if (Selected == null) return;
            Specializing = true; BranchChoice = Building(Selected.Value).Branch ?? Kind(Building(Selected.Value).Kind).Branches?.FirstOrDefault(); Notice = "";
        }

        public void ChooseBranch(string branch) => BranchChoice = branch;

        public bool ConfirmBranch()
        {
            if (!Specializing || Selected == null || BranchChoice == null) return false;
            var id = Selected.Value; var branch = BranchChoice;
            var done = Apply(AccountEstate.Specialize(Catalog, Config, State, Monotonic, Wall, id, branch),
                reason => reason == "specialized" ? Name(Building(id).Kind) + " · " + BranchName(branch) : ReasonText(reason)).Done;
            if (done) Specializing = false;
            return done;
        }

        public void CloseSpecialize() { Specializing = false; BranchChoice = null; }

        // ---- Selection and move (move previews never change the copy) ----

        public void Select(int? id)
        {
            Selected = id; Moving = false; Specializing = false; BranchChoice = null; ZoneChoice = null; QueueOpen = false; PreviewReason = ""; Notice = "";
        }

        public void SelectZone(string zoneId)
        {
            Select(null); ZoneChoice = zoneId;
        }

        public void BeginMove()
        {
            if (Selected == null) return;
            var building = Building(Selected.Value);
            Moving = true; Notice = ""; Evaluate(new Vector2Int(building.X, building.Y));
        }

        // A tap in move mode puts the footprint on the tapped cell (its top-left, or its centre for odd sizes).
        public void Place(Vector2Int tapped)
        {
            if (!Moving || Selected == null) return;
            var kind = Kind(Building(Selected.Value).Kind);
            Evaluate(Map.Anchor(tapped, kind.Width, kind.Height));
        }

        public bool Confirm()
        {
            if (!Moving || Selected == null || !PreviewValid) return false;
            var result = AccountEstate.Move(Catalog, Config, State, Monotonic, Wall, Selected.Value, Preview.x, Preview.y);
            if (!result.Done) { PreviewValid = false; PreviewReason = result.Reason; return false; }
            State = result.State; Moving = false; PreviewReason = "";
            Notice = result.Reason == "moved" ? "옮겼습니다 · 저장하지 않는 시제품입니다" : "";
            return true;
        }

        public void Cancel()
        {
            Moving = false; PreviewReason = ""; Notice = "";
        }

        void Evaluate(Vector2Int anchor)
        {
            Preview = anchor;
            var result = AccountEstate.Move(Catalog, Config, State, Monotonic, Wall, Selected.Value, anchor.x, anchor.y);
            PreviewValid = result.Done; PreviewReason = result.Reason;
        }

        // ---- Display ----

        public AccountEstateTask GoalTask() => Config.Tasks.FirstOrDefault(t => !State.Estate.ClaimedTasks.Contains(t.Id));
        public bool GoalReady => GoalTask() is { } task && State.Estate.AchievedTasks.Contains(task.Id);

        // The first life task not yet claimed (config order), with its reward.
        public string GoalText()
        {
            var task = GoalTask();
            if (task == null) return "목표 · 모든 보상을 받았습니다";
            var name = task.Trigger switch
            {
                "first-production-claim" => "생산 재고를 처음 받기",
                "first-building-move" => "건물 하나 옮기기",
                _ when task.Trigger.StartsWith("production-sites-complete-", StringComparison.Ordinal) => "생산 터 " + task.Trigger.Substring(26) + "곳 완성",
                _ => task.Id,
            };
            return "목표 · " + name + " · 보상 " + Amounts(task.Reward) + (GoalReady ? " · 달성" : "");
        }

        public string Amounts(IReadOnlyDictionary<string, int> amounts) =>
            string.Join(" ", Catalog.Materials.Where(m => amounts.TryGetValue(m.Id, out var value) && value > 0).Select(m => m.Name + " " + amounts[m.Id]));

        public static string Clock(long seconds) => seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";

        // Cost and time of the next level, or null at the last level. Time already includes the construction-support branch.
        public (Dictionary<string, int> Cost, long Seconds)? NextStep(int id)
        {
            var building = Building(id); var kind = Kind(building.Kind);
            if (building.Level >= kind.MaxLevel() || kind.Levels.Length == 0) return null;
            var step = kind.Levels[building.Level];
            var support = State.Estate.Buildings.Any(b => b.Branch == Config.Construction.ReductionBranch);
            var reduction = support ? Math.Min(Config.Construction.ReductionPermille, Config.Construction.MaximumReductionPermille) : 0;
            return (step.Cost, Math.Max(1, (long)step.Seconds * (PlayerInput.Scale - reduction) / PlayerInput.Scale));
        }

        public string WorkLine()
        {
            var estate = State.Estate; var parts = new List<string>();
            if (estate.Active is { } work) parts.Add("공사 " + Name(Building(work.BuildingId).Kind) + " Lv " + work.TargetLevel + " · " + Clock(work.RemainingSeconds));
            else parts.Add("공사 없음");
            if (estate.Reservations.Length > 0) parts.Add("예약 " + estate.Reservations.Length + "/" + Config.Queue.Reservations);
            if (estate.ZoneWork is { } zone) parts.Add("구역 " + Clock(zone.RemainingSeconds));
            return string.Join(" · ", parts);
        }

        public int StockTotal => State.Estate.Buildings.Sum(b => b.Stock);

        public string ReasonText(string reason)
        {
            if (reason.StartsWith("overlaps:", StringComparison.Ordinal) && int.TryParse(reason.Substring(9), out var other))
                return WithParticle(Name(Building(other).Kind)) + " 겹칩니다";
            switch (reason)
            {
                case "moved": case "": return "놓을 수 있습니다";
                case "unchanged": return "지금 자리입니다";
                case "outside-open-zone": return "열린 구역 밖입니다";
                case "under-construction": return "공사 중에는 옮길 수 없습니다";
                case "insufficient": return "재료가 모자랍니다";
                case "protected-reserve": return "첫 생산 경로 몫은 남겨 둡니다";
                case "parent-required": return "먼저 부모 시설을 지어야 합니다";
                case "zone-closed": return "닫힌 구역입니다";
                case "max-level": return "최고 레벨입니다";
                case "queue-full": return "예약이 가득 찼습니다";
                case "already-queued": return "이미 공사·예약 중입니다";
                case "busy": return "다른 작업이 진행 중입니다";
                case "no-reservation": return "예약이 없습니다";
                case "nothing-to-claim": return "받을 재고가 없습니다";
                case "wallet-full": return "지갑이 가득 찼습니다";
                case "not-achieved": return "아직 달성하지 않았습니다";
                case "level-required": return "최고 레벨에서 고를 수 있습니다";
                case "run-pending": return "출정 중에는 바꿀 수 없습니다";
                case "production-sites-required": return "생산 터를 더 완성해야 합니다";
                case "not-adjacent": return "열린 구역과 이어져 있지 않습니다";
                case "already-open": return "이미 열린 구역입니다";
                default: return reason;
            }
        }

        // 와/과 by the last syllable's final consonant ("우물과", "망루와").
        static string WithParticle(string name)
        {
            var last = name.Length > 0 ? name[name.Length - 1] : ' ';
            var final = last >= '가' && last <= '힣' && (last - '가') % 28 != 0;
            return name + (final ? "과" : "와");
        }

        public string Status(MetaEstateBuilding building)
        {
            var kind = Kind(building.Kind);
            if (UnderConstruction(building.Id)) return "공사 중 · 남은 " + Clock(State.Estate.Active.RemainingSeconds);
            var status = building.Level == 0 ? "폐허 · 아직 짓지 않음" : "완성";
            if (Reserved(building.Id)) status += " · 예약됨";
            if (kind.Produces != null && building.Level > 0)
                status += " · 재고 " + MaterialName(kind.Produces.Resource) + " " + building.Stock + "/" + AccountEstate.StockCap(Config, State, kind.Produces.Resource);
            if (building.Branch != null) status += " · " + BranchName(building.Branch);
            return status;
        }

        // Estate-side effect of a branch, read from the config; run-side effects come from the design table.
        public string BranchEffect(string branch)
        {
            if (branch == Config.Storage.LongAwayBranch)
                return "보관 +" + Config.Storage.LongAwayBonusSeconds / 3600 + "시간 · 재고 상한 +" + Config.Storage.LongAwayStockPermille / 10 + "%";
            var bonus = Config.Production.BranchBonus.FirstOrDefault(b => b.Branch == branch);
            if (bonus != null) return MaterialName(bonus.Resource) + " 생산 +" + bonus.Permille / 10 + "%p";
            if (branch == Config.Construction.ReductionBranch) return "공사 시간 −" + Config.Construction.ReductionPermille / 10 + "%";
            return Branches.TryGetValue(branch, out var value) ? value.Run : "";
        }

        public string BranchNote(string branch) => Branches.TryGetValue(branch, out var value) ? value.Run : "";

        public string MaterialName(string id) => Catalog.Materials.FirstOrDefault(m => m.Id == id)?.Name ?? id;
    }
}
