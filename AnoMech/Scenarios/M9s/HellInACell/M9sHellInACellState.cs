using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.M9s.HellInACell;

public enum UltrasonicKind
{
    Spread,
    Amp,
}

// Where the group that is not in cells stands for an Ultrasonic: the tank in the wide safe spot (the
// middle of two neighbouring cell-free bearings), the healer on the next free bearing clockwise, the
// two DPS together on the next free bearing counter-clockwise. For Amp all four stack on the tank.
public sealed record UltrasonicFormation(float TankBearing, float HealerBearing, float DpsBearing);

// Per-run randomization for Hell in a Cell.
//
// Measured from five pulls of one log. The four towers of the first set sit on 12y cardinals and
// intercardinals and always include north; the second set is the other four. The log holds four
// distinct first sets, picked with its weights. Each set's two Ultrasonics come Spread then Amp,
// except one pull whose second pair came Amp first, so each pair's order is random here.
//
// Toxic / Hector: G1 (MT, H1, M1, R1) soaks the first set and G2 the second, clockwise from north in
// T > H > M > R order; the other group takes the Ultrasonics (see UltrasonicFormation).
public sealed class M9sHellInACellState
{
    public const float TowerRadius = 4f;
    public const float TowerDistance = 12f;
    public const float FormationRadius = 6f;

    public static readonly PartyRole[][] Groups =
    [
        [PartyRole.MainTank, PartyRole.RegenHealer, PartyRole.MeleeDpsA, PartyRole.PhysRangedDps],
        [PartyRole.OffTank, PartyRole.ShieldHealer, PartyRole.MeleeDpsB, PartyRole.CasterDps],
    ];

    public static readonly float[] TowerCastAt = [14.88f, 37.25f];
    public static readonly float[] TowerResolveAt = [19.90f, 42.20f];
    public static readonly float[] TankCellDiesAt = [24.00f, 46.30f];
    public static readonly float[] OtherCellsDieAt = [32.90f, 55.20f];
    public static readonly float[][] UltrasonicCastAt = [[21.99f, 29.11f], [44.40f, 51.55f]];
    public const float UltrasonicDelay = 5.7f;

    private static readonly float[] AllBearings = [0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f];

    private static readonly float[][] FirstSets =
    [
        [0f, 135f, 225f, 270f],
        [0f, 45f, 180f, 270f],
        [0f, 90f, 180f, 225f],
        [0f, 135f, 225f, 315f],
    ];

    public static readonly IReadOnlyList<IReadOnlyList<Vector3>> PulpingLayouts =
    [
        Layout((-17, 0), (-6.5f, -15.7f), (-6.5f, 15.7f), (-5, 0), (0, 5), (7.8f, -7.8f), (11, 0), (12, 12)),
        Layout((-12, -12), (-11, 0), (-7.8f, 7.8f), (0, -17), (0, -5), (5, 0), (6.5f, 15.7f), (17, 0)),
        Layout((-12, -12), (-12, 12), (-11, 0), (0, -17), (0, -5), (0, 11), (7.8f, 7.8f), (17, 0)),
        Layout((-15.7f, 6.5f), (-7.8f, -7.8f), (-7.8f, 7.8f), (5, 0), (6.5f, -15.7f), (6.5f, 15.7f), (12, -12), (17, 0)),
        Layout((-15.7f, -6.5f), (-6.5f, -15.7f), (-6.5f, 15.7f), (-5, 0), (0, 5), (7.8f, -7.8f), (11, 0), (12, 12)),
        Layout((-15.7f, 6.5f), (-7.8f, -7.8f), (-7.8f, 7.8f), (0, -17), (5, 0), (6.5f, -15.7f), (6.5f, 15.7f), (15.7f, 6.5f)),
        Layout((-12, -12), (-12, 12), (-11, 0), (0, -17), (0, -5), (0, 11), (7.8f, 7.8f), (15.7f, -6.5f)),
    ];

    public IReadOnlyList<float>[] TowerBearings { get; }
    public UltrasonicKind[][] UltrasonicOrder { get; }
    public IReadOnlyList<Vector3>[] PulpingPulses { get; }
    public M9sSatisfied Satisfied { get; } = new(8);

    public M9sHellInACellState(Rng rng, M9sHellInACellStateOverrides overrides)
    {
        var first = FirstSets[overrides.Layout ?? rng.NextObj(0, 1, 1, 2, 3)];
        TowerBearings = [first, AllBearings.Except(first).ToList()];
        UltrasonicOrder =
        [
            overrides.SpreadFirst ?? rng.NextBool() ? [UltrasonicKind.Spread, UltrasonicKind.Amp] : [UltrasonicKind.Amp, UltrasonicKind.Spread],
            overrides.SpreadFirst ?? rng.NextBool() ? [UltrasonicKind.Spread, UltrasonicKind.Amp] : [UltrasonicKind.Amp, UltrasonicKind.Spread],
        ];
        PulpingPulses =
        [
            PulpingLayouts[rng.NextInt(PulpingLayouts.Count)],
            PulpingLayouts[rng.NextInt(PulpingLayouts.Count)],
            PulpingLayouts[rng.NextInt(PulpingLayouts.Count)],
        ];
    }

    public static int FirstSetCount => FirstSets.Length;

    private static IReadOnlyList<Vector3> Layout(params (float X, float Z)[] points) =>
        points.Select(p => new Vector3(p.X, 0f, p.Z)).ToList();

    // Clockwise from north, T > H > M > R of the soaking group.
    public IReadOnlyList<(PartyRole Role, Vector3 Tower)> TowerAssignments(int set)
    {
        var towers = TowerBearings[set].OrderBy(b => b).ToList();
        return Enumerable.Range(0, 4).Select(i => (Groups[set][i], AtBearing(towers[i], TowerDistance))).ToList();
    }

    public UltrasonicFormation Formation(int set)
    {
        var free = TowerBearings[1 - set].OrderBy(b => b).ToList();
        for (var i = 0; i < free.Count; i++)
        {
            var a = free[i];
            var b = free[(i + 1) % free.Count];
            if (Math.Abs(BearingDelta(b, a) - 45f) > 1f) continue;
            var rest = free.Where(f => f != a && f != b).ToList();
            var healer = rest.OrderBy(f => Wrap(f - b)).First();
            var dps = rest.First(f => f != healer);
            return new UltrasonicFormation(Wrap(a + 22.5f), healer, dps);
        }
        return new UltrasonicFormation(free[0], free[1], free[2]);
    }

    public static Vector3 AtBearing(float bearingDegrees, float radius)
    {
        var rad = bearingDegrees * MathF.PI / 180f;
        return new Vector3(radius * MathF.Sin(rad), 0f, -radius * MathF.Cos(rad));
    }

    public static float RotationFacing(Vector3 from, Vector3 to) => MathF.Atan2(to.X - from.X, to.Z - from.Z);

    public static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private static float Wrap(float degrees) => ((degrees % 360f) + 360f) % 360f;

    private static float BearingDelta(float a, float b) => Wrap(a - b);
}
