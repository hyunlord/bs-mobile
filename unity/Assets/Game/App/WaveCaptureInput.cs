using System;
using System.Linq;
using SowSiege.Core;
namespace Game.App
{
    // Ordinary movement and card preferences only. Never changes the simulation state.
    public static class WaveCaptureInput
    {
        static readonly string[] Priority={"core:sowing_sworddance","core:warded_masonry","core:sheltered_sowing","core:seed_bag","core:rain_ladle","core:carpenter_hammer","core:muster_horn","core:iron_blade","core:ward_orbit","core:storm_fork","core:ember_wand","core:harvest_scythe"};
        static string[] preferred;
        public static void ConfigurePriority(string value) => preferred=string.IsNullOrWhiteSpace(value)?null:value.Split(',');
        public static int CardRank(string id) { var selection=preferred??Priority;var index=Array.IndexOf(selection,id);return index<0?100:index; }
        public static WorldPoint Target(RunFrame frame,WaveRuntimeFrame wave,WorldPoint fallback,ContentCatalog catalog=null)
        {
            var reward=wave.Rewards.OrderBy(r=>Distance(r.Position,frame.Lord.Position)).FirstOrDefault();if(reward!=null)return reward.Position;
            var ripe=wave.Work.Where(w=>w.Kind=="grain"&&w.Complete&&w.Health>0).OrderBy(w=>Distance(w.Position,frame.Lord.Position)).FirstOrDefault();if(ripe!=null)return ripe.Position;
            var carpenter=catalog?.WaveRuntime?.Gear.Values.FirstOrDefault(g=>g.Kind==WaveAttackKind.ConstructionSlam);
            if(wave.Timber>0&&carpenter!=null&&frame.Equipment.Any(e=>e.Id==carpenter.Id)&&!wave.Work.Any(w=>w.Kind=="building"&&w.Health>0&&Distance(w.Position,wave.TimberOrigin)<=(long)carpenter.WorkRadius*carpenter.WorkRadius))return wave.TimberOrigin;
            var building=wave.Work.Where(w=>w.Kind=="building"&&!w.Complete&&w.Health>0).OrderBy(w=>Distance(w.Position,frame.Lord.Position)).FirstOrDefault();
            if(building!=null)return new WorldPoint(building.Position.X+(int)(Math.Cos(frame.Tick/30d)*100),building.Position.Y+(int)(Math.Sin(frame.Tick/30d)*100));
            return fallback;
        }
        static long Distance(WorldPoint a,WorldPoint b){var x=(long)a.X-b.X;var y=(long)a.Y-b.Y;return x*x+y*y;}
    }
}
