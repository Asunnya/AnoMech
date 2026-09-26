namespace AnoMech.Scenarios.M9s.Coffinmaker;

// Debug overrides; null keeps the random pick.
public sealed class M9sCoffinmakerStateOverrides
{
    public BossSide? FirstSide { get; set; }
    public CleaveOrder? Order { get; set; }
    public SawKill? SawKill { get; set; }
}
