using UnityEngine;

namespace Game.View
{
    // Presentation only: authoritative health is observed, never modified.
    public sealed class DamageTrailState
    {
        public float HealthFraction { get; private set; }
        public float TrailFraction { get; private set; }
        public float EdgeOpacity { get; private set; }
        bool initialized;
        int priorHealth, priorMaximum, priorTick;
        float hold, flashRemaining, flashPeak;
        const float HoldSeconds=.10f, DecaySeconds=.24f, FlashSeconds=.28f;

        public void Present(int health,int maximum,int tick,float elapsed)
        {
            var fraction=maximum>0?Mathf.Clamp01((float)health/maximum):0;
            if(!initialized||tick<priorTick||maximum!=priorMaximum)
            {
                initialized=true;HealthFraction=TrailFraction=fraction;hold=flashRemaining=flashPeak=EdgeOpacity=0;
            }
            else
            {
                var dt=Mathf.Max(0,elapsed);
                if(health<priorHealth)
                {
                    TrailFraction=Mathf.Max(TrailFraction,HealthFraction);
                    hold=HoldSeconds;flashRemaining=FlashSeconds;
                    flashPeak=Mathf.Clamp(.14f+(HealthFraction-fraction)*.7f,.14f,.42f);
                    dt=0; // The newly observed hit starts now, not at the previous presentation sample.
                }
                HealthFraction=fraction;
                TrailFraction=Mathf.Max(TrailFraction,fraction);
                var usedHold=Mathf.Min(hold,dt);hold-=usedHold;dt-=usedHold;
                TrailFraction=fraction+(TrailFraction-fraction)*Mathf.Exp(-dt/DecaySeconds);
                if(TrailFraction-fraction<.0001f)TrailFraction=fraction;
                flashRemaining=Mathf.Max(0,flashRemaining-(health<priorHealth?0:Mathf.Max(0,elapsed)));
                var progress=flashRemaining/FlashSeconds;
                EdgeOpacity=flashPeak*progress*progress;
            }
            priorHealth=health;priorMaximum=maximum;priorTick=tick;
        }
    }
}
