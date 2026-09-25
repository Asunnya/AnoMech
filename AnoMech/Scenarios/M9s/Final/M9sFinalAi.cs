using System;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.M9s.Final;

public sealed class M9sFinalAi : IScenarioAi<M9sFinalState>
{
    public string Name => "Toxic / Hector";

    private static readonly float[] RingArenaRadii = [2f, 3.5f, 5f, 8.5f, 10f, 11f, 12f, 14.5f, 17f, 19f];
    private const float RingReach = M9sConstants.Geometry.RingArenaRadius - 1f;
    private const float PulseClearance = 6.5f;
    private const float PulseWaitReach = 18f;
    private const float HalfMoonSideOffset = 5f;
    private const float ScratchDodgeRadius = 8f;

    private M9sFinalState state = null!;
    private float scratchBearing = 180f;

    public void Run(M9sFinalState stateParam, SimWorld world)
    {
        state = stateParam;
        var dodge = new M9sBatStompDodge(world, state.Bats, IsInsideRingArena, RingArenaRadii);
        var ai = new AiManager(world);
        ai.Move(0.5f, Uptime);
        ai.Move(1.72f, () => AiMove.Create(dodge.MarkerSpots()).NaturalOrder());
        world.Events.Add(11.72f, () => dodge.DodgeBatRing(0, 11.72f, 16.02f));
        world.Events.Add(16.02f, () => dodge.DodgeBatRing(1, 16.02f, 19.52f));
        world.Events.Add(19.52f, () => dodge.DodgeBatRing(2, 19.52f, 23.02f));

        ai.Move(23.02f, () => AiMove.All(OutsideCleave(state.HalfMoonShortRotation)));
        ai.Move(28.02f, () => AiMove.All(OutsideCleave(state.HalfMoonLongRotation)));

        ai.Move(31.0f, TanksNorthPartySouthForBigHardcore);
        ai.Move(38.2f, () => AiMove.All(NearestSpotClearOfPulses(new Vector2(0f, 5f))));

        ai.Move(47.5f, () => AiMove.All(GapNearest(0, 180f)));
        ai.Move(50.46f, () => AiMove.All(GapNearest(1, scratchBearing)));
        ai.Move(52.88f, () => AiMove.All(GapNearest(2, scratchBearing)));
        ai.Move(55.28f, () => AiMove.All(GapNearest(3, scratchBearing)));
        ai.Move(57.66f, () => AiMove.All(GapNearest(4, scratchBearing)));
        ai.Move(60.10f, () => AiMove.All(new Vector2(0f, 3f)));
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

    private static IAiMove TanksNorthPartySouthForBigHardcore() => AiMove.Create(
        new(-3f, -11f),
        new(3f, -11f),
        new(-2f, 12f),
        new(2f, 12f),
        new(-2f, 12f),
        new(2f, 12f),
        new(-2f, 12f),
        new(2f, 12f)
    ).NaturalOrder();

    private Vector2 OutsideCleave(float cleaveRotation)
    {
        var origin = M9sHalfMoon.CleaveOrigin(M9sFinalState.BossTanked, cleaveRotation, state.Satisfied.IsMore).Position;
        var away = -new Vector2(MathF.Sin(cleaveRotation), MathF.Cos(cleaveRotation)) * HalfMoonSideOffset;
        return new Vector2(origin.X, -2f) + away;
    }

    private Vector2 GapNearest(int wave, float bearing)
    {
        var best = M9sSanguineScratch.GapCentre(state.ScratchFirstOffset, wave, 0);
        var bestDelta = float.MaxValue;
        for (var i = 0; i < 8; i++)
        {
            var gap = M9sSanguineScratch.GapCentre(state.ScratchFirstOffset, wave, i);
            var delta = MathF.Abs(((gap - bearing) % 360f + 540f) % 360f - 180f);
            if (delta >= bestDelta) continue;
            bestDelta = delta;
            best = gap;
        }
        scratchBearing = best;
        var p = M9sBatPattern.AtBearing(best, ScratchDodgeRadius);
        return new Vector2(p.X, p.Z);
    }

    private static Vector2 NearestSpotClearOfPulses(Vector2 home)
    {
        var best = home;
        var bestDistance = float.MaxValue;
        for (var x = -PulseWaitReach; x <= PulseWaitReach; x += 1f)
            for (var z = -PulseWaitReach; z <= PulseWaitReach; z += 1f)
            {
                var spot = new Vector2(x, z);
                if (spot.Length() > PulseWaitReach || !IsClearOfPulses(spot)) continue;
                var distance = Vector2.Distance(spot, home);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = spot;
            }
        return best;
    }

    private static bool IsClearOfPulses(Vector2 spot)
    {
        foreach (var pulse in M9sFinalState.PulpingPulses)
            if (Vector2.Distance(spot, new Vector2(pulse.X, pulse.Z)) < PulseClearance) return false;
        return true;
    }

    private static bool IsInsideRingArena(Vector3 spot) => new Vector2(spot.X, spot.Z).Length() <= RingReach;
}
