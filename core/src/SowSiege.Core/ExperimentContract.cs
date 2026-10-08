using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{

    public sealed record CircuitOffset(int X, int Y);
    public sealed record MovementTuning(CircuitOffset[] CircuitOffsets, int DecisionPeriodTicks, int EvadeRange, int EvadeStep);
    public sealed record ExperienceCurve(int Base, int Linear, int Quadratic);
    public sealed record ExperimentDefinition(MovementTuning Movement, ExperienceCurve Experience, string[] MixedCategoryOrder);
    public sealed record MovementSample(int Tick, int LordX, int LordY, int EstateX, int EstateY, long DistanceSquared);
    public sealed record FarmWaitEvent(int FarmId, int Episode, int RipeTick, int EndTick, string EndKind, int ObservedWaitTicks, bool Censored);
    public sealed record ExperimentResult(string MovementMode, int? DeathTick, IReadOnlyList<MovementSample> MovementSamples, IReadOnlyList<FarmWaitEvent> FarmWaitEvents, int[] MovementTrace);

}
