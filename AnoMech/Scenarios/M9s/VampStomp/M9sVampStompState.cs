using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.M9s.VampStomp;

public enum BatSpin
{
    Clockwise = 1,
    CounterClockwise = -1,
}

// One ring of bats: Count bats evenly spaced from BaseBearing, all circling the arena centre the
// same way and exploding together once they have covered TravelDegrees.
public sealed record BatRing(int Count, float Radius, float BaseBearing, BatSpin Spin, float TravelDegrees, float CastAt)
{
    public float StartBearing(int index) => BaseBearing + index * 360f / Count;
    public float EndBearing(int index) => StartBearing(index) + (int)Spin * TravelDegrees;
}

// Per-run randomization for the Vamp Stomp opener.
//
// Measured from four pulls of one log (the clear plus three wipes): the rings always hold 2/3/5
// bats at 6/13/20y, start moving at 29.93s and cast Blast Beat at 33.51/36.99/40.48s. What
// varies is each ring's spin, picked independently (one pull had the inner ring clockwise and
// the other two counter-clockwise), and where the ring starts, which only ever lands on a fixed
// step: 45° inner, 30° middle, 18° outer. Travel is BossMod's (88° inner, 90° otherwise), which
// the log agrees with to within a degree.
public sealed class M9sVampStompState
{
    public const float BatMoveStartAt = 29.93f;
    public const float BlastRadius = 8f;
    public const float BatRingStartAt = 30.47f;
    public const float BatRingSpeed = 2f;

    public IReadOnlyList<BatRing> Rings { get; }
    public PartyRole BrutalRainTarget { get; }

    private readonly Rng rng = new();

    public M9sVampStompState(M9sVampStompStateOverrides overrides)
    {
        Rings =
        [
            NewRing(2, 6f, 45f, 88f, 33.51f, overrides.Spin),
            NewRing(3, 13f, 30f, 90f, 36.99f, overrides.Spin),
            NewRing(5, 20f, 18f, 90f, 40.48f, overrides.Spin),
        ];
        BrutalRainTarget = overrides.BrutalRainTarget ?? (PartyRole)rng.NextInt(8);
    }

    private BatRing NewRing(int count, float radius, float step, float travel, float castAt, BatSpin? spin)
    {
        var steps = (int)MathF.Round(360f / count / step);
        return new BatRing(count, radius, rng.NextInt(steps) * step,
            spin ?? rng.NextObj(BatSpin.Clockwise, BatSpin.CounterClockwise), travel, castAt);
    }

    public Placement BatPlacement(BatRing ring, int index, float time)
    {
        var progress = Math.Clamp((time - BatMoveStartAt) / (ring.CastAt - BatMoveStartAt), 0f, 1f);
        var bearing = ring.StartBearing(index) + (int)ring.Spin * ring.TravelDegrees * progress;
        return new Placement(AtBearing(bearing, ring.Radius), FacingAlongOrbit(bearing, ring.Spin));
    }

    public IReadOnlyList<Vector3> BlastPositions(int ringIndex)
    {
        var ring = Rings[ringIndex];
        return Enumerable.Range(0, ring.Count).Select(i => AtBearing(ring.EndBearing(i), ring.Radius)).ToList();
    }

    public static float BatRingRadiusAt(float time) => MathF.Max(0f, (time - BatRingStartAt) * BatRingSpeed);

    // Bearings are compass degrees: 0 = north (-Z), 90 = east (+X).
    public static Vector3 AtBearing(float bearingDegrees, float radius)
    {
        var rad = bearingDegrees * MathF.PI / 180f;
        return new Vector3(radius * MathF.Sin(rad), 0f, -radius * MathF.Cos(rad));
    }

    // Placement rotation 0 faces +Z; the orbit tangent for a compass bearing b is ±90° - b.
    private static float FacingAlongOrbit(float bearingDegrees, BatSpin spin)
    {
        var rad = bearingDegrees * MathF.PI / 180f;
        return (int)spin * MathF.PI / 2f - rad;
    }
}
