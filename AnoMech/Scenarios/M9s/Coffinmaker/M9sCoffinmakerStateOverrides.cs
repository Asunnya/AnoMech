namespace AnoMech.Scenarios.M9s.Coffinmaker;

// User-controlled overrides for M9sCoffinmakerState. FirstSide is the side wall the boss cleaves
// from first; Order pins every cycle's cleave order; SawKill picks when the saw dies. Null leaves
// each random (SawKill weighted like the log). Coffinfiller columns are always randomized.
public sealed class M9sCoffinmakerStateOverrides
{
    public BossSide? FirstSide { get; set; }
    public CleaveOrder? Order { get; set; }
    public SawKill? SawKill { get; set; }
}
