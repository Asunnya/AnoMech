namespace AnoMech.Scenarios.M9s.Aetherletting;

// User-controlled overrides for M9sAetherlettingState. Spin pins which way the cone pairs rotate;
// null keeps it random. The first cone axis, spread pairs, cross shapes and Pulping Pulse layout are
// always randomized.
public sealed class M9sAetherlettingStateOverrides
{
    public ConeSpin? Spin { get; set; }
}
