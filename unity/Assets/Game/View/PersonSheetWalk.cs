using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    // Plays a person's multi-frame "move" sheet by distance travelled (#331). The Soldier B walk covers
    // GameVisualTokens.PersonWalkCyclePixels of ground per cycle at its own pixel scale, so the feet stay planted at any speed.
    // Core never gives militia or guard the "move" state, so the View shows the walk whenever they move and their state's own
    // standing frame when they do not. A single-frame "move" role (peasant, returning, vassal) keeps the time-based path.
    public sealed class PersonSheetWalk
    {
        readonly Dictionary<(string kind,int id),(Vector2 last,float phase,int generation)> phases=new Dictionary<(string,int),(Vector2,float,int)>(256);
        readonly List<(string kind,int id)> retired=new List<(string,int)>(256);
        public int Count=>phases.Count;
        public static bool Applies(ArtVisual move)=>move.UvRects.Count>1;
        // One walk cycle of ground in Unity units: the cycle's source pixels at the role's own pixel scale.
        public static float Stride(ArtVisual move)=>GameVisualTokens.PersonWalkCyclePixels*move.WorldSize.y/move.CellPixels.y;
        public static float Advance(float phase,float distance,float stride)=>Mathf.Repeat(phase+distance/stride,1);
        public static int FrameAt(float phase,int frames)=>Mathf.Min(frames-1,Mathf.FloorToInt(phase*frames));
        // The sheet frame to draw, or -1 when the actor did not move between the last two Core ticks (draw its standing role).
        public int Frame(string kind,int id,Vector2 rendered,bool moving,ArtVisual move,int generation)
        {
            var key=(kind,id);
            (Vector2 last,float phase,int generation) state=phases.TryGetValue(key,out var known)?known:(rendered,0f,generation);
            state.phase=Advance(state.phase,Vector2.Distance(rendered,state.last),Stride(move));
            state.last=rendered;state.generation=generation;phases[key]=state;
            return moving?FrameAt(state.phase,move.UvRects.Count):-1;
        }
        // Forgets actors not drawn in this generation, as the renderer does for facing.
        public void Retire(int generation)
        {
            retired.Clear();
            foreach(var pair in phases)if(pair.Value.generation!=generation)retired.Add(pair.Key);
            for(var i=0;i<retired.Count;i++)phases.Remove(retired[i]);
        }
    }
}
