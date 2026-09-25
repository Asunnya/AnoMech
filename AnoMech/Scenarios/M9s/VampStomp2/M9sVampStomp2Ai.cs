using System;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.M9s.VampStomp2;

public sealed class M9sVampStomp2Ai : IScenarioAi<M9sVampStomp2State>
{
    public string Name => "Toxic / Hector";

    private static readonly float[] RingArenaRadii = [2f, 3.5f, 5f, 8.5f, 10f, 11f, 12f, 14.5f, 17f, 19f];
    private const float RingReach = M9sConstants.Geometry.RingArenaRadius - 1f;
    private const float PulseClearance = 6.5f;
    private const float StompClearRadius = 11f;
    private const float PulseWaitReach = 18f;
    private const float HalfMoonSideOffset = 5f;

    private M9sVampStomp2State state = null!;

    public void Run(M9sVampStomp2State stateParam, SimWorld world)
    {
        state = stateParam;
        var dodge = new M9sBatStompDodge(world, state.Bats, IsInsideRingArena, RingArenaRadii);
        var ai = new AiManager(world);
        ai.Move(0.5f, Uptime);
        ai.Move(12.5f, TanksSplitForHardcore);
        ai.Move(21.3f, WaitNearMarkersClearOfPulsesAndStomp);
        ai.Move(25.3f, () => AiMove.Create(dodge.MarkerSpots()).NaturalOrder());
        world.Events.Add(30.3f, () => dodge.DodgeBatRing(0, 30.3f, 34.6f));
        world.Events.Add(34.6f, () => dodge.DodgeBatRing(1, 34.6f, 38.1f));
        world.Events.Add(38.1f, () => dodge.DodgeBatRing(2, 38.1f, 41.6f));
        ai.Move(41.6f, () => AiMove.All(OutsideCleave(state.HalfMoonShortRotation)));
        ai.Move(46.6f, () => AiMove.All(OutsideCleave(state.HalfMoonLongRotation)));
        ai.Move(49.6f, () => AiMove.All(new Vector2(0f, -2f)));
    }

    private static IAiMove Uptime() => AiMove.Create(
        new(0f, -5f),
        new(-3f, -4.5f),
        new(-3f, 7f),
        new(3f, 7f),
        new(-5f, 0f),
        new(5f, 0f),
        new(-5f, 6f),
        new(5f, 6f)
    ).NaturalOrder();

    private static IAiMove TanksSplitForHardcore() => AiMove.Create(
        new(-5f, -6f),
        new(5f, -6f),
        new(-3f, 7f),
        new(3f, 7f),
        new(-4.5f, 2f),
        new(4.5f, 2f),
        new(-5f, 6f),
        new(5f, 6f)
    ).NaturalOrder();

    private IAiMove WaitNearMarkersClearOfPulsesAndStomp()
    {
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++)
        {
            var marker = M9sBatPattern.AtBearing(M9sBatStompDodge.ToxicMarkerBearings[slot], M9sBatStompDodge.MarkerRadius);
            spots[slot] = NearestSpotClearOfPulses(new Vector2(marker.X, marker.Z));
        }
        return AiMove.Create(spots).NaturalOrder();
    }

    private Vector2 NearestSpotClearOfPulses(Vector2 home)
    {
        var best = home;
        var bestDistance = float.MaxValue;
        for (var x = -PulseWaitReach; x <= PulseWaitReach; x += 1f)
            for (var z = -PulseWaitReach; z <= PulseWaitReach; z += 1f)
            {
                var spot = new Vector2(x, z);
                var radius = spot.Length();
                if (radius > PulseWaitReach || radius < StompClearRadius || !IsClearOfPulses(spot)) continue;
                var distance = Vector2.Distance(spot, home);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = spot;
            }
        return best;
    }

    private bool IsClearOfPulses(Vector2 spot)
    {
        foreach (var pulse in state.PulpingPulses)
            if (Vector2.Distance(spot, new Vector2(pulse.X, pulse.Z)) < PulseClearance) return false;
        return true;
    }

    private Vector2 OutsideCleave(float cleaveRotation)
    {
        var origin = M9sHalfMoon.CleaveOrigin(M9sVampStomp2State.BossTanked, cleaveRotation, state.HalfMoonIsMore).Position;
        var away = -new Vector2(MathF.Sin(cleaveRotation), MathF.Cos(cleaveRotation)) * HalfMoonSideOffset;
        return new Vector2(origin.X, -2f) + away;
    }

    private static bool IsInsideRingArena(Vector3 spot) => new Vector2(spot.X, spot.Z).Length() <= RingReach;
}
