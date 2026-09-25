using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.Coffinmaker;

public enum BossSide
{
    East = 1,
    West = -1,
}

// How fast the party burns the saw down. The log's six pulls killed it at ~114s (three pulls: the
// fourth cycle fires no Coffinfiller), ~116s (two: one wave) or ~125s (one: both waves).
public enum SawKill
{
    Fast,
    Average,
    Slow,
}

// One Half Moon (see M9sHalfMoon) + Coffinfiller set. Coffinfiller fires the corridor's four 5y
// columns in two waves of two, one per side of the centre line, each wave lining up with one cleave.
public sealed record SawCycle(
    float CastAt,
    Placement Boss,
    CleaveOrder Order,
    uint FillerActionId,
    float FillerStartZ,
    IReadOnlyList<float> FirstWave,
    IReadOnlyList<float> SecondWave)
{
    public float ShortRotation => M9sHalfMoon.ShortRotation(Boss.Rotation, Order);
    public float LongRotation => M9sHalfMoon.LongRotation(Boss.Rotation, Order);

    public bool IsInsideCleave(Vector3 point, float rotation, float margin = 0f, bool more = false) =>
        M9sHalfMoon.IsInsideCleave(Boss, rotation, point, margin, more);
}

// Per-run randomization for the Coffinmaker (saws) phase.
//
// Measured from six pulls of one log. Fixed: the corridor (x within ±10) shrinks from the north by
// 10y per Dead Wake, three times; Coffinfiller runs from the current north edge to the south wall,
// so the three filler actions (32/22/12y) only differ in length; the boss cleaves from one side
// wall (x = ±12, z = 0), then the opposite one (x = ∓12, z = 10), then twice from the south wall.
// Random: which side wall comes first (5 of 6 pulls east), each cycle's cleave order, and which
// column on each side of the centre line fires in the first wave.
public sealed class M9sCoffinmakerState
{
    public static readonly float[] EastColumns = [2.5f, 7.5f];
    public static readonly float[] WestColumns = [-2.5f, -7.5f];

    public BossSide FirstSide { get; }
    public IReadOnlyList<SawCycle> Cycles { get; }
    public SawKill SawKill { get; }
    public M9sSatisfied Satisfied { get; } = new(0);
    public bool[] CycleIsMore { get; } = new bool[4];

    private readonly Rng rng = new();

    public M9sCoffinmakerState(M9sCoffinmakerStateOverrides overrides)
    {
        FirstSide = overrides.FirstSide ?? rng.NextObj(BossSide.East, BossSide.West);
        SawKill = overrides.SawKill ?? rng.NextObj(SawKill.Fast, SawKill.Fast, SawKill.Fast, SawKill.Average, SawKill.Average, SawKill.Slow);
        var s = (int)FirstSide;
        Cycles =
        [
            NewCycle(16.19f, new Placement(new Vector3(12f * s, 0f, 0f), -s * MathF.PI / 2f), ActionId.CoffinfillerLong, -10f, overrides.Order),
            NewCycle(33.60f, new Placement(new Vector3(-12f * s, 0f, 10f), s * MathF.PI / 2f), ActionId.CoffinfillerMedium, 0f, overrides.Order),
            NewCycle(51.00f, new Placement(new Vector3(0f, 0f, 21.8f), MathF.PI), ActionId.CoffinfillerShort, 10f, overrides.Order),
            NewCycle(61.19f, new Placement(new Vector3(0f, 0f, 21.8f), MathF.PI), ActionId.CoffinfillerShort, 10f, overrides.Order),
        ];
    }

    private SawCycle NewCycle(float castAt, Placement boss, uint fillerId, float fillerStartZ, CleaveOrder? order)
    {
        var east = rng.NextInt(2);
        var west = rng.NextInt(2);
        return new SawCycle(castAt, boss,
            order ?? rng.NextObj(CleaveOrder.LeftFirst, CleaveOrder.RightFirst),
            fillerId, fillerStartZ,
            [EastColumns[east], WestColumns[west]],
            [EastColumns[1 - east], WestColumns[1 - west]]);
    }

    // Dead Wake resolves at 13.94 / 31.36 / 48.76s, each time burning the next 10y strip.
    public static float NorthEdgeAt(float time) => time switch
    {
        < 13.94f => -20f,
        < 31.36f => -10f,
        < 48.76f => 0f,
        _ => 10f,
    };
}
