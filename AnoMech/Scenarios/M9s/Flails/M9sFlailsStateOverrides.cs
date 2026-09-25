namespace AnoMech.Scenarios.M9s.Flails;

// User-controlled overrides for M9sFlailsState. Layout pins one of the four tower/doornail
// sequences seen in the log (0-3); null picks one with the log's weights.
public sealed class M9sFlailsStateOverrides
{
    public int? Layout { get; set; }
}
