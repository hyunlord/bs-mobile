using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.App
{
    public enum EstateResidentKind { Hoe, Hammer, Carry, Sack }

    // View-only jobs share one budget. IDs and ordering never depend on a frame clock or random generator.
    public sealed class EstateResidentJob
    {
        public const int VisibleLimit = 6;
        public readonly int BuildingId;
        public readonly EstateResidentKind Kind;
        public readonly Vector2 Grid;
        public string Key => BuildingId.ToString("D8") + ":" + Kind;
        public EstateResidentJob(int buildingId, EstateResidentKind kind, Vector2 grid)
        { BuildingId = buildingId; Kind = kind; Grid = grid; }
        public static EstateResidentJob[] Select(IEnumerable<EstateResidentJob> jobs) =>
            jobs.OrderBy(j => j.BuildingId).ThenBy(j => j.Kind).Take(VisibleLimit).ToArray();
        public static void ApplyBudget(EstatePlan plan)
        {
            var working = Select(plan.Residents);
            plan.Residents.Clear(); plan.Residents.AddRange(working);
            // Active jobs take priority; idle granary, fallback carry and placed residents share the remaining slots.
            var idle = plan.Items.Where(i => i.Source == "peasant").OrderBy(i => i.Key, StringComparer.Ordinal)
                .Take(VisibleLimit - working.Length).ToArray();
            plan.Items.RemoveAll(i => i.Source == "peasant" && !idle.Contains(i));
        }
        public static Vector2 Canvas(EstateResidentKind kind) => kind == EstateResidentKind.Hoe ? new Vector2(96, 96)
            : kind == EstateResidentKind.Hammer ? new Vector2(64, 76) : new Vector2(66, 66);
        public static double FrameSeconds(EstateResidentKind kind) => kind == EstateResidentKind.Hoe ? .2 : kind == EstateResidentKind.Hammer ? .15 : .3;
        public static string Role(EstateResidentKind kind) => "worker-" + kind.ToString().ToLowerInvariant() + "-";
    }
}
