using System.Collections.Generic;
namespace Game.View
{
    public sealed class WaveBossPhaseCue
    {
        readonly Dictionary<int,string> phases=new Dictionary<int,string>();
        public bool Accept(int entityId,string phase)
        {
            var entered=phases.TryGetValue(entityId,out var previous)&&previous=="charge"&&phase=="recovery";
            phases[entityId]=phase;
            return entered;
        }
        public void Reset()=>phases.Clear();
    }
}
