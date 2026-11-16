// Shared with the test project by a source link; no dependency on the executable's Program type.
internal sealed class LoadRamp
{
    private readonly int accounts;
    private readonly int perSecond;
    public TimeSpan RampDuration { get; }
    public TimeSpan PlateauDuration { get; }
    public TimeSpan TotalDuration => RampDuration + PlateauDuration;

    public LoadRamp(int accounts, int perSecond, int plateauSeconds)
    {
        if (accounts <= 0) { throw new ArgumentOutOfRangeException(nameof(accounts)); }
        if (perSecond is < 0 or > 10000) { throw new ArgumentOutOfRangeException(nameof(perSecond)); }
        if (plateauSeconds <= 0) { throw new ArgumentOutOfRangeException(nameof(plateauSeconds)); }
        this.accounts = accounts;
        this.perSecond = perSecond;
        RampDuration = Offset(accounts);
        PlateauDuration = TimeSpan.FromSeconds(plateauSeconds);
    }

    private TimeSpan Offset(int index) => perSecond == 0 ? TimeSpan.Zero
        : TimeSpan.FromTicks(checked(((long)index * TimeSpan.TicksPerSecond + perSecond - 1) / perSecond));

    public TimeSpan AdmissionAt(int index)
    {
        if (index < 0 || index >= accounts) { throw new ArgumentOutOfRangeException(nameof(index)); }
        return Offset(index);
    }

    public TimeSpan NextAdmissionAfter(TimeSpan actualAdmission) => actualAdmission + Offset(1);

    public TimeSpan DelayUntilAdmission(int index, TimeSpan elapsed) => AdmissionAt(index) > elapsed ? AdmissionAt(index) - elapsed : TimeSpan.Zero;
    public string Phase(TimeSpan elapsed) => elapsed < RampDuration ? "ramp" : elapsed < TotalDuration ? "plateau" : "drain";
    public TimeSpan MeasuredPlateau(TimeSpan elapsed) => TimeSpan.FromTicks(Math.Clamp((elapsed - RampDuration).Ticks, 0, PlateauDuration.Ticks));
}
