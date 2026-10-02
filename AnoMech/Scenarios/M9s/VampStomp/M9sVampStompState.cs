using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.M9s.VampStomp;

// Per-run randomization for the Vamp Stomp opener: the bat rings (see M9sBatPattern) and who gets
// Brutal Rain, which in every pull of the log marked a healer (AST five times, SGE twice).
public sealed class M9sVampStompState
{
    public M9sBatPattern Bats { get; }
    public PartyRole BrutalRainTarget { get; }
    public M9sSatisfied Satisfied { get; } = new(0);

    public M9sVampStompState(Rng rng, M9sVampStompStateOverrides overrides)
    {
        Bats = new M9sBatPattern(overrides.Spin, rng);
        BrutalRainTarget = overrides.BrutalRainTarget ?? rng.NextObj(PartyRole.RegenHealer, PartyRole.ShieldHealer);
    }
}
