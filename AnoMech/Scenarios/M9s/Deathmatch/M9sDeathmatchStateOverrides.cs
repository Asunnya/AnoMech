namespace AnoMech.Scenarios.M9s.Deathmatch;

// User-controlled overrides for M9sDeathmatchState. NorthSouth pins the tower axis (false = west/east);
// null keeps it random (north/south four times in five in the log). Cone sets, bat sweeps and bat
// shapes are always randomized.
public sealed class M9sDeathmatchStateOverrides
{
    public bool? NorthSouth { get; set; }
}
