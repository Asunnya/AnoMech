using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.M9s.VampStomp2;

// Per-run randomization for the second Vamp Stomp.
//
// Measured from six pulls of one log. The bats follow M9sBatPattern (their timings match the first
// stomp to within 0.05s). Half Moon cleaves from wherever the boss is tanked, left or right first
// at random; the boss is held north here. Brutal Rain always marked a healer (SGE five times, AST
// once). Pulping Pulse drops one of six layouts, all seen in this phase of the log. The boss comes
// in with the four Satisfied stacks the first Crowd Kill gives.
public sealed class M9sVampStomp2State
{
    public static readonly Placement BossTanked = new(new Vector3(0f, 0f, -8f), MathF.PI);

    public static readonly IReadOnlyList<IReadOnlyList<Vector3>> PulpingLayouts =
    [
        [new(-15.7f, 0f, -6.5f), new(-12f, 0f, 12f), new(-5f, 0f, 0f), new(7.8f, 0f, 7.8f), new(-6.5f, 0f, 15.7f), new(7.8f, 0f, -7.8f)],
        [new(-11f, 0f, 0f), new(-7.8f, 0f, 7.8f), new(15.7f, 0f, 6.5f), new(5f, 0f, 0f), new(0f, 0f, -17f), new(0f, 0f, -5f)],
        [new(-7.8f, 0f, 7.8f), new(6.5f, 0f, -15.7f), new(17f, 0f, 0f), new(-11f, 0f, 0f), new(0f, 0f, -5f), new(5f, 0f, 0f)],
        [new(-15.7f, 0f, 6.5f), new(-7.8f, 0f, -7.8f), new(-6.5f, 0f, 15.7f), new(0f, 0f, 5f), new(11f, 0f, 0f), new(0f, 0f, -11f)],
        [new(-11f, 0f, 0f), new(-7.8f, 0f, 7.8f), new(17f, 0f, 0f), new(5f, 0f, 0f), new(0f, 0f, -17f), new(0f, 0f, -5f)],
        [new(-15.7f, 0f, -6.5f), new(-5f, 0f, 0f), new(0f, 0f, 17f), new(-12f, 0f, 12f), new(7.8f, 0f, 7.8f), new(7.8f, 0f, -7.8f)],
    ];

    public M9sBatPattern Bats { get; }
    public CleaveOrder HalfMoon { get; }
    public PartyRole BrutalRainTarget { get; }
    public IReadOnlyList<Vector3> PulpingPulses { get; }
    public M9sSatisfied Satisfied { get; } = new(4);

    public float HalfMoonShortRotation => M9sHalfMoon.ShortRotation(BossTanked.Rotation, HalfMoon);
    public float HalfMoonLongRotation => M9sHalfMoon.LongRotation(BossTanked.Rotation, HalfMoon);
    public bool HalfMoonIsMore { get; set; }

    public M9sVampStomp2State(Rng rng, M9sVampStomp2StateOverrides overrides)
    {
        Bats = new M9sBatPattern(overrides.Spin, rng);
        HalfMoon = overrides.Order ?? rng.NextObj(CleaveOrder.LeftFirst, CleaveOrder.RightFirst);
        BrutalRainTarget = rng.NextObj(PartyRole.RegenHealer, PartyRole.ShieldHealer);
        PulpingPulses = PulpingLayouts[rng.NextInt(PulpingLayouts.Count)];
    }
}
