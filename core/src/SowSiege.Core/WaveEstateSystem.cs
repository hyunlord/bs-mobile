using System;
using System.Collections.Generic;
namespace SowSiege.Core
{
    internal sealed class WaveEstateBuilding
    {
        public int Id, Progress, Dwell;
        public string DefinitionId = "";
        public Position Position;
        public WaveEstatePhase Phase;
        public bool Paid;
    }

    // ADR0064 sections 1 and 3: pre-built sleeping buildings around the home, woken by staying (channel), paying (spend) or both.
    // Buildings are never attacked in M2, so lit zones only grow; the completion ledger keeps activation once per run.
    internal sealed class WaveEstateSystem
    {
        private readonly WorldState world;
        private readonly WaveEstateLightDefinition light;
        private WaveRuntimeState State => world.WaveRuntime!;

        private WaveEstateSystem(WorldState world, WaveEstateLightDefinition light) { this.world = world; this.light = light; }

        internal static WaveEstateSystem? Create(ContentCatalog catalog, WorldState world, InteractiveState? interactive)
        {
            if (interactive?.EstateLoadout is not { } loadout) { return null; }
            var definition = catalog.WaveRuntime!;
            var light = definition.EstateLight ?? throw new InvalidOperationException("Estate runs require an estate light definition.");
            var state = world.WaveRuntime!;
            state.EstateLoadout = loadout;
            state.Estate = new() { new() { Id = world.AllocateId(), DefinitionId = light.HomeId, Position = world.Estate, Phase = WaveEstatePhase.Active } };
            var slots = light.Loadouts[loadout];
            var chapter = ChapterHash(definition.ChapterId);
            var seed = interactive.EstateSeed;
            var turn = (int)(FieldState.Mix(seed, chapter, slots.Length) & (uint)(FieldState.DirectionCount - 1));
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                var direction = FieldState.Direction(turn + index * FieldState.DirectionCount / slots.Length);
                var radius = slot.MinRadius + (int)(FieldState.Mix(seed, chapter, index) % (uint)(slot.MaxRadius - slot.MinRadius + 1));
                var position = new Position(checked(world.Estate.X + (int)((long)direction.X * radius / PlayerInput.Scale)), checked(world.Estate.Y + (int)((long)direction.Y * radius / PlayerInput.Scale)));
                if (interactive.Field is null)
                {
                    var map = catalog.Tuning.World.Map;
                    position = new(Math.Clamp(position.X, 0, map.Width), Math.Clamp(position.Y, 0, map.Height));
                }
                state.Estate.Add(new() { Id = world.AllocateId(), DefinitionId = slot.BuildingId, Position = position, Phase = WaveEstatePhase.Asleep });
            }
            return new(world, light);
        }
        private static uint ChapterHash(string chapter)
        {
            uint hash = 0;
            foreach (var c in chapter) { hash = unchecked(hash * FieldState.SeedHashMultiplier ^ c); }
            return hash;
        }

        internal void Tick()
        {
            foreach (var building in State.Estate!)
            {
                if (building.Phase == WaveEstatePhase.Active) { continue; }
                var definition = light.Buildings[building.DefinitionId];
                var inside = building.Position.DistanceSquared(world.Lord) <= (long)definition.WakeRadius * definition.WakeRadius;
                if (definition.Wake != WaveEstateWake.Channel && !building.Paid)
                {
                    // Spending needs an uninterrupted stay so passing through never pays by accident.
                    if (!inside) { building.Dwell = 0; continue; }
                    if (building.Dwell < definition.ConfirmTicks) { building.Dwell++; }
                    if (building.Dwell < definition.ConfirmTicks || !Pay(definition)) { continue; }
                    building.Paid = true;
                    State.Emit(world.Tick, "estate-spend", definition.Id, building.Id, building.Position, building.Position, definition.Cost);
                    if (definition.Wake == WaveEstateWake.Spend) { Activate(building, definition); continue; }
                }
                // Leaving the circle pauses channel progress; it neither decays nor resets (user decision 2).
                if (!inside) { continue; }
                if (building.Phase == WaveEstatePhase.Asleep)
                {
                    building.Phase = WaveEstatePhase.Waking;
                    State.Emit(world.Tick, "estate-wake-start", definition.Id, building.Id, building.Position, building.Position, definition.ChannelTicks);
                }
                building.Progress++;
                if (building.Progress >= definition.ChannelTicks) { Activate(building, definition); }
            }
        }
        private bool Pay(WaveEstateBuildingDefinition definition)
        {
            if (definition.Resource == WaveEstateResource.Food)
            {
                if (world.Food < definition.Cost) { return false; }
                world.Food -= definition.Cost; return true;
            }
            if (State.Timber < definition.Cost) { return false; }
            State.Timber -= definition.Cost; return true;
        }
        private void Activate(WaveEstateBuilding building, WaveEstateBuildingDefinition definition)
        {
            if (!State.Completed.Add("estate-wake:" + building.Id)) { return; }
            building.Phase = WaveEstatePhase.Active;
            State.Emit(world.Tick, "estate-wake-complete", definition.Id, building.Id, building.Position, building.Position, definition.LitRadius);
        }

        internal static bool Lit(WaveRuntimeDefinition definition, List<WaveEstateBuilding> estate, Position position)
        {
            var light = definition.EstateLight!;
            foreach (var building in estate)
            {
                if (building.Phase != WaveEstatePhase.Active) { continue; }
                var radius = building.DefinitionId == light.HomeId ? light.HomeLitRadius : light.Buildings[building.DefinitionId].LitRadius;
                if (building.Position.DistanceSquared(position) <= (long)radius * radius) { return true; }
            }
            return false;
        }

        // T1 (ADR0064 section 2): multiply first, widen to long, round upward, so a small bonus still raises integral damage and zero stays zero.
        internal static int Darken(int damage, int permille) => checked((int)(((long)damage * (PlayerInput.Scale + permille) + PlayerInput.Scale - 1) / PlayerInput.Scale));

        internal static WaveEstateFrame Capture(WaveRuntimeDefinition definition, WaveRuntimeState state)
        {
            var light = definition.EstateLight!;
            var estate = state.Estate!;
            var views = new WaveEstateBuildingView[estate.Count];
            var sleeping = 0;
            for (var i = 0; i < views.Length; i++)
            {
                var b = estate[i];
                if (b.DefinitionId == light.HomeId)
                {
                    views[i] = new(b.Id, b.DefinitionId, new(b.Position.X, b.Position.Y), b.Phase, 0, 0, 0, 0, false, WaveEstateResource.Food, 0, 0, light.HomeLitRadius);
                    continue;
                }
                var d = light.Buildings[b.DefinitionId];
                if (b.Phase != WaveEstatePhase.Active) { sleeping++; }
                views[i] = new(b.Id, b.DefinitionId, new(b.Position.X, b.Position.Y), b.Phase, b.Progress, d.ChannelTicks, b.Dwell, d.ConfirmTicks, b.Paid, d.Resource, d.Cost, d.WakeRadius, d.LitRadius);
            }
            var bonus = (int)Math.Min(light.DarkFinishCapPermille, (long)sleeping * light.DarkFinishPermillePerSleeping);
            return new(light.Version, state.EstateLoadout!, light.SightRadius, Array.AsReadOnly(views), sleeping, bonus);
        }
    }
}
