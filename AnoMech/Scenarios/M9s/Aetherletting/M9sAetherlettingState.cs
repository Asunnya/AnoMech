using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.M9s.Aetherletting;

public enum ConeSpin
{
    Clockwise = 45,
    CounterClockwise = -45,
}

// Per-run randomization for the Aetherletting phase.
//
// Measured from six pulls of one log. Four pairs of opposite 45° cones fire from the centre 2s
// apart, each pair 45° on from the last: the first pair's axis (one of four) and the spin are
// random. Spreads land on random pairs of players 2s apart; each leaves a puddle where its target
// stands that becomes a 10y-wide cross ("+" or "X", random per puddle) 14.5s later. Pulping Pulse
// always drops one of five fixed layouts (all five seen in the log), picked here at random.
//
// Toxic's static spots sit on the seam between two cone sectors at the arena edge, 22.5° off each
// cardinal, which keeps the centre clear of every cross.
public sealed class M9sAetherlettingState
{
    public static readonly float[] ToxicBearings = [337.5f, 157.5f, 247.5f, 67.5f, 202.5f, 112.5f, 292.5f, 22.5f];

    public static readonly float[] ConeResolveAt = [50.68f, 52.68f, 54.68f, 56.68f];
    public static readonly float[] SpreadResolveAt = [52.68f, 54.68f, 56.68f, 58.68f];

    public static readonly IReadOnlyList<IReadOnlyList<Vector3>> PulpingLayouts =
    [
        [new(-7.8f, 0f, -7.8f), new(-7.8f, 0f, 7.8f), new(17f, 0f, 0f), new(5f, 0f, 0f), new(6.5f, 0f, -15.7f), new(0f, 0f, -17f)],
        [new(-15.7f, 0f, 6.5f), new(-7.8f, 0f, -7.8f), new(0f, 0f, 17f), new(0f, 0f, 5f), new(11f, 0f, 0f), new(0f, 0f, -11f)],
        [new(-15.7f, 0f, 6.5f), new(0f, 0f, 5f), new(-7.8f, 0f, -7.8f), new(11f, 0f, 0f), new(-6.5f, 0f, 15.7f), new(0f, 0f, -11f)],
        [new(-15.7f, 0f, -6.5f), new(-5f, 0f, 0f), new(-6.5f, 0f, 15.7f), new(0f, 0f, 17f), new(7.8f, 0f, 7.8f), new(7.8f, 0f, -7.8f)],
        [new(-15.7f, 0f, -6.5f), new(-5f, 0f, 0f), new(0f, 0f, 17f), new(0f, 0f, 5f), new(11f, 0f, 0f), new(7.8f, 0f, -7.8f)],
    ];

    public float FirstConeBearing { get; }
    public ConeSpin Spin { get; }
    public IReadOnlyList<PartyRole[]> SpreadPairs { get; }
    public IReadOnlyList<Vector3> PulpingPulses { get; }
    public M9sSatisfied Satisfied { get; } = new(0);
    public Vector3?[] Puddles { get; } = new Vector3?[8];
    private readonly bool[] diagonalCross = new bool[8];

    private readonly Rng rng = new();

    public M9sAetherlettingState(M9sAetherlettingStateOverrides overrides)
    {
        FirstConeBearing = rng.NextInt(4) * 45f;
        Spin = overrides.Spin ?? rng.NextObj(ConeSpin.Clockwise, ConeSpin.CounterClockwise);

        var roles = rng.Shuffle(Enum.GetValues<PartyRole>()).ToList();
        SpreadPairs = Enumerable.Range(0, 4).Select(i => new[] { roles[2 * i], roles[2 * i + 1] }).ToList();

        for (var i = 0; i < 8; i++) diagonalCross[i] = rng.NextBool();
        PulpingPulses = PulpingLayouts[rng.NextInt(PulpingLayouts.Count)];
    }

    // Compass bearing of one cone of the pair; its partner points the opposite way.
    public float ConeBearing(int pair) => ((FirstConeBearing + (int)Spin * pair) % 360f + 360f) % 360f;

    // Which pair (0-3) fires through the 45° sector centred on `bearing`.
    public int PairFiringThrough(float sectorBearing)
    {
        for (var pair = 0; pair < 4; pair++)
        {
            var delta = MathF.Abs(BearingDelta(ConeBearing(pair), sectorBearing));
            if (delta < 1f || delta > 179f) return pair;
        }
        return 0;
    }

    public int SpreadPairOf(PartyRole role)
    {
        for (var pair = 0; pair < SpreadPairs.Count; pair++)
            if (SpreadPairs[pair].Contains(role)) return pair;
        return 0;
    }

    public bool IsDiagonalCross(PartyRole role) => diagonalCross[(int)role];

    public const float CrossHalfWidth = 5f;

    // Distance from `point` to the nearer arm of the cross a puddle at `puddle` leaves; the arm is
    // lethal within CrossHalfWidth.
    public float DistanceToCross(Vector3 puddle, PartyRole role, Vector3 point)
    {
        var rotation = IsDiagonalCross(role) ? MathF.PI / 4f : 0f;
        var dx = point.X - puddle.X;
        var dz = point.Z - puddle.Z;
        var along = MathF.Abs(dx * MathF.Sin(rotation) + dz * MathF.Cos(rotation));
        var across = MathF.Abs(dx * MathF.Cos(rotation) - dz * MathF.Sin(rotation));
        return MathF.Min(along, across);
    }

    // Bearings are compass degrees: 0 = north (-Z), 90 = east (+X). Placement rotation 0 faces +Z.
    public static Vector3 AtBearing(float bearingDegrees, float radius)
    {
        var rad = bearingDegrees * MathF.PI / 180f;
        return new Vector3(radius * MathF.Sin(rad), 0f, -radius * MathF.Cos(rad));
    }

    public static float RotationFacing(float bearingDegrees) => MathF.PI - bearingDegrees * MathF.PI / 180f;

    public static float BearingDelta(float a, float b) => ((a - b) % 360f + 540f) % 360f - 180f;
}
