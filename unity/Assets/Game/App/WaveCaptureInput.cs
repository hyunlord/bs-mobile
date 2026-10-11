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
        static bool evasive;
        public static void ConfigureMovement(string value)
        {
            if(!string.IsNullOrEmpty(value)&&value!="default"&&value!="evasive")throw new ArgumentException("Capture movement must be default or evasive.",nameof(value));
            evasive=value=="evasive";
        }
        public static void ConfigurePriority(string value) => preferred=string.IsNullOrWhiteSpace(value)?null:value.Split(',');
        public static int CardRank(string id) { var selection=preferred??Priority;var index=Array.IndexOf(selection,id);return index<0?100:index; }
        public static string ChooseCard(CardOfferView offers,RunFrame frame,ContentCatalog catalog,string targetMaterial=null,int evolutionAfterTick=0)
        {
            if(evolutionAfterTick<0)throw new ArgumentOutOfRangeException(nameof(evolutionAfterTick),"Evolution preference tick must be nonnegative.");
            if(offers==null||offers.Cards.Count==0)throw new ArgumentException("At least one actual offered card is required.",nameof(offers));
            var definition=catalog?.WaveRuntime??throw new ArgumentException("Wave capture requires a wave catalog.",nameof(catalog));
            var cards=offers.Cards;
            if(frame.Tick<evolutionAfterTick)
            {
                var ordinary=cards.Where(id=>!definition.Evolutions.ContainsKey(id)).ToArray();
                if(ordinary.Length>0)cards=ordinary;
            }
            var owned=frame.Equipment.Select(e=>e.Id).ToHashSet(StringComparer.Ordinal);
            if(frame.Level==2&&targetMaterial!=null&&definition.MaterialTargets!=null&&definition.MaterialTargets.TryGetValue(targetMaterial,out var target)&&!owned.Contains(target)&&cards.Contains(target))return target;
            var selection=preferred??Priority;
            var evolution=selection.FirstOrDefault(id=>definition.Evolutions.ContainsKey(id)&&cards.Contains(id));
            if(evolution!=null)return evolution;
            var newGear=selection.FirstOrDefault(id=>definition.Gear.ContainsKey(id)&&!owned.Contains(id)&&cards.Contains(id));
            if(newGear!=null)return newGear;
            return cards.OrderBy(CardRank).First();
        }
        public static WorldPoint Target(RunFrame frame,WaveRuntimeFrame wave,WorldPoint fallback,ContentCatalog catalog=null,bool goldenMinute=false)
        {
            var target=GrowthTarget(frame,wave,fallback,catalog);
            if(goldenMinute&&frame.Tick>=52*frame.TickRate&&frame.Tick<60*frame.TickRate)
            {
                long x=0,y=0;var count=0;
                foreach(var work in wave.Work)
                {
                    if(work.Kind!="building"||!work.Complete||work.Health<=0)continue;
                    x+=work.Position.X;y+=work.Position.Y;count++;
                }
                // Positive world Y is screen-up. Walk northeast of actual completed buildings;
                // do not move the estate, camera, rewards, or simulation state for the comparison.
                if(count>0)target=new WorldPoint((int)Math.Max(0,Math.Min(frame.MapWidth,x/count+1800)),(int)Math.Max(0,Math.Min(frame.MapHeight,y/count+1600)));
            }
            return evasive?EvasiveTarget(frame,wave,target,catalog):target;
        }
        static WorldPoint GrowthTarget(RunFrame frame,WaveRuntimeFrame wave,WorldPoint fallback,ContentCatalog catalog)
        {
            var reward=wave.Rewards.OrderBy(r=>Distance(r.Position,frame.Lord.Position)).FirstOrDefault();if(reward!=null)return reward.Position;
            var ripe=wave.Work.Where(w=>w.Kind=="grain"&&w.Complete&&w.Health>0).OrderBy(w=>Distance(w.Position,frame.Lord.Position)).FirstOrDefault();if(ripe!=null)return ripe.Position;
            var carpenter=catalog?.WaveRuntime?.Gear.Values.FirstOrDefault(g=>g.Kind==WaveAttackKind.ConstructionSlam);
            if(wave.Timber>0&&carpenter!=null&&frame.Equipment.Any(e=>e.Id==carpenter.Id)&&!wave.Work.Any(w=>w.Kind=="building"&&w.Health>0&&Distance(w.Position,wave.TimberOrigin)<=(long)carpenter.WorkRadius*carpenter.WorkRadius))return wave.TimberOrigin;
            var building=wave.Work.Where(w=>w.Kind=="building"&&!w.Complete&&w.Health>0).OrderBy(w=>Distance(w.Position,frame.Lord.Position)).FirstOrDefault();
            if(building!=null)return new WorldPoint(building.Position.X+(int)(Math.Cos(frame.Tick/30d)*100),building.Position.Y+(int)(Math.Sin(frame.Tick/30d)*100));
            return fallback;
        }
        static WorldPoint EvasiveTarget(RunFrame frame,WaveRuntimeFrame wave,WorldPoint target,ContentCatalog catalog)
        {
            const int step=450, observationRadius=1800, clearance=200;
            var origin=frame.Lord.Position;
            var nearby=frame.Enemies.Where(e=>e.Health>0&&Distance(e.Position,origin)<=(long)observationRadius*observationRadius).ToArray();
            WorldPoint Clamp(WorldPoint point)=>new WorldPoint(Math.Max(0,Math.Min(frame.MapWidth,point.X)),Math.Max(0,Math.Min(frame.MapHeight,point.Y)));
            WorldPoint Toward(WorldPoint point)
            {
                var length=Math.Sqrt(Distance(origin,point));
                return length<=step?Clamp(point):Clamp(new WorldPoint(origin.X+(int)((point.X-origin.X)*step/length),origin.Y+(int)((point.Y-origin.Y)*step/length)));
            }
            double Risk(WorldPoint point)
            {
                double risk=0;
                foreach(var enemy in nearby)
                {
                    var radius=clearance+(catalog!=null&&catalog.Enemies.TryGetValue(enemy.DefinitionId,out var rule)?rule.Range:0);
                    risk+=Math.Max(0,1-Math.Sqrt(Distance(point,enemy.Position))/Math.Max(1,radius));
                    var tell=wave.Enemies.FirstOrDefault(e=>e.Id==enemy.Id);
                    if(tell==null||(tell.Phase!="tell-charge"&&tell.Phase!="charge"&&tell.Phase!="tell-water"&&tell.Phase!="water"))continue;
                    var dx=(double)tell.Target.X-tell.Origin.X;var dy=(double)tell.Target.Y-tell.Origin.Y;
                    var denominator=dx*dx+dy*dy;
                    var t=denominator==0?0:Math.Max(0,Math.Min(1,((point.X-tell.Origin.X)*dx+(point.Y-tell.Origin.Y)*dy)/denominator));
                    var x=point.X-(tell.Origin.X+t*dx);var y=point.Y-(tell.Origin.Y+t*dy);
                    risk+=2*Math.Max(0,1-Math.Sqrt(x*x+y*y)/Math.Max(1,radius));
                }
                return risk;
            }
            double RouteRisk(WorldPoint point)=>Risk(point)*2+Risk(new WorldPoint((origin.X+point.X)/2,(origin.Y+point.Y)/2));
            var direct=Toward(target);var best=direct;var bestRisk=RouteRisk(direct);
            if(bestRisk==0)return target;
            for(var i=0;i<8;i++)
            {
                var angle=i*Math.PI/4;var candidate=Clamp(new WorldPoint(origin.X+(int)(Math.Cos(angle)*step),origin.Y+(int)(Math.Sin(angle)*step)));
                var risk=RouteRisk(candidate);
                if(risk<bestRisk||(risk==bestRisk&&Distance(candidate,target)<Distance(best,target))){best=candidate;bestRisk=risk;}
            }
            return best;
        }
        static long Distance(WorldPoint a,WorldPoint b){var x=(long)a.X-b.X;var y=(long)a.Y-b.Y;return x*x+y*y;}
    }
}
