namespace AnoMech.Scenarios.M9s.HellInACell;

// User-controlled overrides for M9sHellInACellState. Layout pins one of the four first tower sets
// seen in the log (0-3); SpreadFirst pins both Ultrasonic pairs to Spread-then-Amp (true) or
// Amp-then-Spread (false). Null leaves either random. Pulping Pulse layouts are always random.
public sealed class M9sHellInACellStateOverrides
{
    public int? Layout { get; set; }
    public bool? SpreadFirst { get; set; }
}
