using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public enum WeaponDisplayStage { Normal, Evolved }

    // View-only table (Resources/WeaponDisplayStages.json) of which gear definitions and runtime evolutions show their
    // evolution-only presentation element (storm sky, flag bearer, giant sword, arrow rain, ...). Gear is keyed by its
    // definition designRef because the review Lv1/max/evolved stages share one gear id; runtime evolutions by evolution id.
    // Continuous size/count scaling stays on gear numbers. Not under data/, so Core content and replay hashes are unchanged.
    public sealed class WeaponDisplayStages
    {
        public const string ResourceName = "WeaponDisplayStages";
        readonly Dictionary<string, WeaponDisplayStage> gear = new Dictionary<string, WeaponDisplayStage>(StringComparer.Ordinal);
        readonly Dictionary<string, WeaponDisplayStage> evolutions = new Dictionary<string, WeaponDisplayStage>(StringComparer.Ordinal);
        readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        static WeaponDisplayStages loaded;

        public static WeaponDisplayStages Load()
        {
            if (loaded != null) return loaded;
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) throw new InvalidOperationException("Weapon display stage table is missing.");
            return loaded = Parse(asset.text);
        }

        public static WeaponDisplayStages Parse(string json)
        {
            if (!json.Contains("\"weapon-display-stage-v1\"")) throw new InvalidOperationException("Unsupported weapon display stage table.");
            var table = new WeaponDisplayStages();
            Read(json, "gear", table.gear);
            Read(json, "evolutions", table.evolutions);
            return table;
        }

        static void Read(string json, string section, Dictionary<string, WeaponDisplayStage> into)
        {
            var key = json.IndexOf("\"" + section + "\"", StringComparison.Ordinal);
            if (key < 0) throw new InvalidOperationException("Weapon display stage table lacks " + section + ".");
            var start = json.IndexOf('{', key); var end = json.IndexOf('}', start);
            foreach (Match entry in Regex.Matches(json.Substring(start, end - start), "\"([^\"]+)\"\\s*:\\s*\"(normal|evolved)\""))
                into.Add(entry.Groups[1].Value, entry.Groups[2].Value == "evolved" ? WeaponDisplayStage.Evolved : WeaponDisplayStage.Normal);
        }

        public bool TryGetGear(string designRef, out WeaponDisplayStage stage) => gear.TryGetValue(designRef ?? "", out stage);
        public bool TryGetEvolution(string evolutionId, out WeaponDisplayStage stage) => evolutions.TryGetValue(evolutionId ?? "", out stage);

        // The stage a weapon is drawn at: an active runtime evolution decides when there is one, else the gear definition.
        // An id missing from the table is drawn normal and logged once.
        public WeaponDisplayStage Of(WaveGearDefinition definition, string activeRuntimeEvolution)
        {
            if (!string.IsNullOrEmpty(activeRuntimeEvolution))
                return evolutions.TryGetValue(activeRuntimeEvolution, out var evolved) ? evolved : Unlisted("evolution " + activeRuntimeEvolution);
            if (definition == null) return WeaponDisplayStage.Normal;
            return gear.TryGetValue(definition.DesignRef ?? "", out var stage) ? stage : Unlisted("gear " + definition.Id + " (" + definition.DesignRef + ")");
        }

        public bool Evolved(WaveGearDefinition definition, string activeRuntimeEvolution) => Of(definition, activeRuntimeEvolution) == WeaponDisplayStage.Evolved;

        WeaponDisplayStage Unlisted(string what)
        {
            if (warned.Add(what)) UnityEngine.Debug.LogWarning("[weapon-display-stage] " + what + " is not in " + ResourceName + ".json; drawn as normal.");
            return WeaponDisplayStage.Normal;
        }
    }
}
