using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
namespace Game.App
{
    // Adapts authoritative wave state to shared card, HUD and result widgets.
    public static class WavePresentation
    {
        public static FirstPlayableFrame Envelope(ContentCatalog catalog, RunFrame frame, CardOfferView offers, WaveRuntimeFrame wave)
        {
            if (wave == null || catalog.WaveRuntime == null) throw new InvalidOperationException("Authoritative wave view is required.");
            var cards = offers.Cards.Select(id =>
            {
                var level = frame.Equipment.FirstOrDefault(e => e.Id == id)?.Level ?? 0;
                return new OfferedCardDetail(id, catalog.WaveRuntime.Evolutions.ContainsKey(id) ? "epic" : "common", 1, level, level + 1, catalog.WaveRuntime.Evolutions.Values.Where(e => e.Id == id || e.InputIds.Contains(id)).Select(e => e.Id).ToArray());
            }).ToArray();
            var evolutions = catalog.WaveRuntime.Evolutions.Values.Select(e => new EvolutionClueView(e.Id, wave.Evolutions.Contains(e.Id), offers.Cards.Contains(e.Id), e.InputIds.Select(id => new EvolutionRequirement(id, 1)).ToArray(), null)).ToArray();
            var bosses = frame.Enemies.Where(e => e.DefinitionId == catalog.WaveRuntime.BossId).Select(e => new BossView(e.DefinitionId, e.Id, "boss", "active", e.Health, catalog.Enemies[e.DefinitionId].Health)).ToArray();
            return new FirstPlayableFrame(Array.Empty<ActiveAttackView>(), Array.Empty<MapEventView>(), Array.Empty<BuildingProgressView>(), cards, evolutions,
                Array.Empty<string>(), wave.Items.ToDictionary(id => id, _ => 1), Count(wave,"enemy-killed"), Count(wave,"reward-collected"), catalog.Tuning.DefaultHero,
                Array.Empty<PersonActivityView>(), bosses, wave.BossDefeated);
        }
        static long Count(WaveRuntimeFrame frame, string key) => frame.Counters.Where(pair => pair.Key.StartsWith(key+":",StringComparison.Ordinal)).Sum(pair => pair.Value);
    }
}
