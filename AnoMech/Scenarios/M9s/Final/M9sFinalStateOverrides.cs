namespace AnoMech.Scenarios.M9s.Final;

// User-controlled overrides for M9sFinalState. Spin pins the Vamp Stomp bat rings and Order pins Half
// Moon's first side (null leaves either random). Enrage plays out Final Finale Fatale instead of the
// boss dying on the clear's schedule.
public sealed class M9sFinalStateOverrides
{
    public BatSpin? Spin { get; set; }
    public CleaveOrder? Order { get; set; }
    public bool Enrage { get; set; }
}
