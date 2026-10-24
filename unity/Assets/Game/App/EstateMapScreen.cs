using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // M3a-2 estate map prototype (#366): a development-only screen over the M3a-1 Core estate. It works on an in-memory copy
    // (the account's estate when it has one, otherwise a 2A migration of the current manor) and never writes the save.
    // Every placement is checked by AccountEstate.Move at the copy's last observation, so the preview credits no time and
    // shows Core's own reason.
    public sealed class EstateMapScreen
    {
        // Temporary names for the M3a kinds that have no catalog building yet (design #360 5.1 reserved names).
        static readonly Dictionary<string, string> KindNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["m3a:home"] = "우리 집", ["m3a:watchtower"] = "망루", ["m3a:well_shrine"] = "우물",
            ["m3a:field"] = "경작지", ["m3a:lumber_camp"] = "벌목장", ["m3a:ironworks"] = "철공소",
        };
        // Existing manor sprites; the other kinds use labelled colour plates until Astra's estate art is approved.
        static readonly Dictionary<string, string> KindArt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["meta:forge"] = "b-forge", ["meta:scriptorium"] = "b-scriptorium", ["meta:granary"] = "b-granary", ["meta:barracks"] = "b-barracks",
        };

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

        // `map` keeps the player's zoom and position when the copy is rebuilt from a newer account state.
        public EstateMapScreen(MetaCatalog catalog, AccountEstateConfig config, MetaState account, EstateMapModel map = null)
        {
            Catalog = catalog; Config = config ?? throw new ArgumentNullException(nameof(config));
            FromSave = account.Estate != null;
            State = FromSave ? account
                : account.PendingRun == null ? AccountEstate.Migrate(catalog, config, account, 0, 0)
                : AccountEstate.NewPrototype(catalog, config, 0, 0);
            Map = map ?? new EstateMapModel(config.Zones.Max(z => z.X + z.Width), config.Zones.Max(z => z.Y + z.Height));
        }

        long Monotonic => Math.Max(0, State.Estate.LastMonotonicSeconds);
        long Wall => Math.Max(0, State.Estate.LastWallSeconds);
        public MetaEstateBuilding Building(int id) => State.Estate.Buildings.Single(b => b.Id == id);
        public AccountEstateBuildingKind Kind(string kind) => Config.Buildings.Single(k => k.Kind == kind);
        public bool Open(AccountEstateZone zone) => State.Estate.OpenZones.Contains(zone.Id);
        public bool UnderConstruction(int id) => State.Estate.Active?.BuildingId == id;
        public static string Art(string kind) => KindArt.TryGetValue(kind, out var art) ? art : null;

        public string Name(string kind)
        {
            if (KindNames.TryGetValue(kind, out var name)) return name;
            return Catalog.ManorBuildings.FirstOrDefault(b => b.Id == kind)?.Name ?? kind;
        }

        public Vector2 HomeCentre()
        {
            var home = State.Estate.Buildings.Select(b => (b, k: Kind(b.Kind))).FirstOrDefault(x => x.k.StartsComplete);
            return home.k == null ? new Vector2(Map.Columns, Map.Rows) * .5f : new Vector2(home.b.X + home.k.Width * .5f, home.b.Y + home.k.Height * .5f);
        }

        public void Select(int? id)
        {
            Selected = id; Moving = false; PreviewReason = ""; Notice = "";
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
            if (UnderConstruction(building.Id)) return "공사 중";
            if (building.Level == 0) return "폐허 · 아직 짓지 않음";
            var status = "완성";
            if (kind.Produces != null) status += " · 재고 " + MaterialName(kind.Produces.Resource) + " " + building.Stock;
            if (building.Branch != null) status += " · 전문화 " + building.Branch;
            return status;
        }

        public string MaterialName(string id) => Catalog.Materials.FirstOrDefault(m => m.Id == id)?.Name ?? id;
    }
}
