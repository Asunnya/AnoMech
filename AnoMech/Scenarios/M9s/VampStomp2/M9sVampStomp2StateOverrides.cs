namespace AnoMech.Scenarios.M9s.VampStomp2;

// User-controlled overrides for M9sVampStomp2State. Spin pins all three bat rings to one direction
// and Order pins Half Moon's first side; null leaves either random. Ring start angles, the Brutal
// Rain healer and the Pulping Pulse layout are always randomized.
public sealed class M9sVampStomp2StateOverrides
{
    public BatSpin? Spin { get; set; }
    public CleaveOrder? Order { get; set; }
}
