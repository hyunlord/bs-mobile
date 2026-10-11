using System;
using System.Collections.Generic;
using SowSiege.Core;
namespace Game.App
{
    // Adapts authoritative wave state to shared card, HUD and result widgets.
    public static class WavePresentation
    {
        public static FirstPlayableFrame Envelope(ContentCatalog catalog, RunFrame frame, CardOfferView offers, WaveRuntimeFrame wave)
        {
            if (wave == null || catalog.WaveRuntime == null) throw new InvalidOperationException("Authoritative wave view is required.");
            var definitions = catalog.WaveRuntime.Evolutions;
            var cards = offers.Cards.Count == 0 ? Array.Empty<OfferedCardDetail>() : new OfferedCardDetail[offers.Cards.Count];
            for (var cardIndex = 0; cardIndex < cards.Length; cardIndex++)
            {
                var id = offers.Cards[cardIndex];
                var level = 0;
                for (var equipmentIndex = 0; equipmentIndex < frame.Equipment.Count; equipmentIndex++)
                    if (frame.Equipment[equipmentIndex].Id == id) { level = frame.Equipment[equipmentIndex].Level; break; }
                var matching = 0;
                foreach (var evolution in definitions.Values)
                    if (evolution.Id == id || Contains(evolution.InputIds, id)) matching++;
                var links = matching == 0 ? Array.Empty<string>() : new string[matching];
                var linkIndex = 0;
                foreach (var evolution in definitions.Values)
                    if (evolution.Id == id || Contains(evolution.InputIds, id)) links[linkIndex++] = evolution.Id;
                cards[cardIndex] = new OfferedCardDetail(id, definitions.ContainsKey(id) ? "epic" : "common", 1, level, level + 1, links);
            }
            var evolutions = definitions.Count == 0 ? Array.Empty<EvolutionClueView>() : new EvolutionClueView[definitions.Count];
            var evolutionIndex = 0;
            foreach (var evolution in definitions.Values)
            {
                var requirements = evolution.InputIds.Length == 0 ? Array.Empty<EvolutionRequirement>() : new EvolutionRequirement[evolution.InputIds.Length];
                for (var inputIndex = 0; inputIndex < requirements.Length; inputIndex++) requirements[inputIndex] = new EvolutionRequirement(evolution.InputIds[inputIndex], 1);
                evolutions[evolutionIndex++] = new EvolutionClueView(evolution.Id, Contains(wave.Evolutions, evolution.Id), Contains(offers.Cards, evolution.Id), requirements, null);
            }
            var bossCount = 0;
            for (var index = 0; index < frame.Enemies.Count; index++)
                if (frame.Enemies[index].DefinitionId == catalog.WaveRuntime.BossId) bossCount++;
            var bosses = bossCount == 0 ? Array.Empty<BossView>() : new BossView[bossCount];
            var bossIndex = 0;
            for (var index = 0; index < frame.Enemies.Count; index++)
            {
                var enemy = frame.Enemies[index];
                if (enemy.DefinitionId == catalog.WaveRuntime.BossId)
                    bosses[bossIndex++] = new BossView(enemy.DefinitionId, enemy.Id, "boss", "active", enemy.Health, catalog.Enemies[enemy.DefinitionId].Health);
            }
            var items = new Dictionary<string, int>(wave.Items.Count);
            for (var index = 0; index < wave.Items.Count; index++) items.Add(wave.Items[index], 1);
            return new FirstPlayableFrame(Array.Empty<ActiveAttackView>(), Array.Empty<MapEventView>(), Array.Empty<BuildingProgressView>(), cards, evolutions,
                Array.Empty<string>(), items, Count(wave.Counters,"enemy-killed:"), Count(wave.Counters,"reward-collected:"), catalog.Tuning.DefaultHero,
                Array.Empty<PersonActivityView>(), bosses, wave.BossDefeated);
        }
        static bool Contains(IReadOnlyList<string> values, string id)
        {
            for (var index = 0; index < values.Count; index++) if (values[index] == id) return true;
            return false;
        }
        static long Count(IReadOnlyDictionary<string, long> counters, string prefix)
        {
            long total = 0;
            foreach (var pair in counters)
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) total = checked(total + pair.Value);
            return total;
        }
#if UNITY_EDITOR
        public static long EditorCountCounters(IReadOnlyDictionary<string, long> counters, string prefix) => Count(counters, prefix);
#endif
    }
}
