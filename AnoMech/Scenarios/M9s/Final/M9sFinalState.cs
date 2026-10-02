using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;

namespace AnoMech.Scenarios.M9s.Final;

// Per-run randomization for the final stretch.
//
// Measured from the two pulls of one log that got here. The third Vamp Stomp keeps the first one's
// bat timings (see M9sBatPattern), 18.58s earlier on this scenario's clock. The boss arrives with the
// clear's nine Satisfied stacks, so Half Moon and Hardcore come in their larger versions: one pull
// cleaved left first, the other right. Pulping Pulse dropped the same ten puddles in both. Sanguine
// Scratch opened on a different cone set in each. The clear killed the boss during Crowd Kill;
// otherwise Final Finale Fatale wipes the party.
public sealed class M9sFinalState
{
    public const float StompShift = -18.58f;

    public static readonly Placement BossTanked = new(new Vector3(0f, 0f, -7.5f), MathF.PI);

    public static readonly IReadOnlyList<Vector3> PulpingPulses =
    [
        new(-17f, 0f, 0f), new(-5f, 0f, 0f), new(-15.7f, 0f, -6.5f), new(0f, 0f, 17f), new(-6.5f, 0f, 15.7f),
        new(12f, 0f, 12f), new(0f, 0f, 5f), new(-6.5f, 0f, -15.7f), new(11f, 0f, 0f), new(7.8f, 0f, -7.8f),
    ];

    public static readonly float[] ScratchWaveAt = [50.36f, 52.78f, 55.18f, 57.56f, 59.98f];

    public M9sBatPattern Bats { get; }
    public CleaveOrder HalfMoon { get; }
    public float ScratchFirstOffset { get; }
    public bool Enrage { get; }
    public M9sSatisfied Satisfied { get; } = new(9);

    public float HalfMoonShortRotation => M9sHalfMoon.ShortRotation(BossTanked.Rotation, HalfMoon);
    public float HalfMoonLongRotation => M9sHalfMoon.LongRotation(BossTanked.Rotation, HalfMoon);

    public M9sFinalState(Rng rng, M9sFinalStateOverrides overrides)
    {
        Bats = new M9sBatPattern(overrides.Spin, rng, StompShift);
        HalfMoon = overrides.Order ?? rng.NextObj(CleaveOrder.LeftFirst, CleaveOrder.RightFirst);
        ScratchFirstOffset = rng.NextObj(0f, 22.5f);
        Enrage = overrides.Enrage;
    }
}
