using System;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.M9s.Aetherletting;

public sealed class M9sAetherlettingAi : IScenarioAi<M9sAetherlettingState>
{
    public string Name => "Toxic / Hector";

    private const float EdgeRadius = 18f;
    private const float SeamOffset = 3f;
    private const float PulseClearance = 6.5f;
    private const float ConesDoneAt = 56.8f;
    private const float CrossClearance = M9sAetherlettingState.CrossHalfWidth + 0.6f;
    private const float StackSearchReach = 12f;
    private const float StackSearchStep = 0.5f;
    private static readonly float[] CrossesKnownAt = [53.3f, 55.3f, 57.3f, 59.3f];

    private M9sAetherlettingState state = null!;

    public void Run(M9sAetherlettingState stateParam, SimWorld world)
    {
        state = stateParam;
        var ai = new AiManager(world);
        ai.Move(0.5f, () => AiMove.All(new Vector2(0f, 3f)));
        ai.Move(32.6f, WaitNearStaticSpotsClearOfPulses);
        ai.Move(36.6f, () => SeamSpots(beforeFirstCone: true));

        for (var slot = 0; slot < 8; slot++)
        {
            var role = (PartyRole)slot;
            var bearing = M9sAetherlettingState.ToxicBearings[slot];
            var switchAt = M9sAetherlettingState.ConeResolveAt[EarlierSectorPair(bearing)] + 0.1f;
            ai.Move(switchAt, () => AiMove.Single(role, SeamSpot(bearing, beforeFirstCone: false)));

            var stackFrom = MathF.Max(ConesDoneAt, M9sAetherlettingState.SpreadResolveAt[state.SpreadPairOf(role)] + 0.1f);
            ai.Move(stackFrom, () => AiMove.Single(role, StackClearOfCrosses()));
            foreach (var knownAt in CrossesKnownAt)
                if (knownAt > stackFrom)
                    ai.Move(knownAt, () => AiMove.Single(role, StackClearOfCrosses()));
        }
    }

    private Vector2 StackClearOfCrosses()
    {
        Vector2? best = null;
        var bestDistance = float.MaxValue;
        var leastExposed = Vector2.Zero;
        var leastExposedClearance = float.MinValue;
        for (var x = -StackSearchReach; x <= StackSearchReach; x += StackSearchStep)
            for (var z = -StackSearchReach; z <= StackSearchReach; z += StackSearchStep)
            {
                var spot = new Vector2(x, z);
                var clearance = ClearanceFromKnownCrosses(new Vector3(x, 0f, z));
                if (clearance > leastExposedClearance)
                {
                    leastExposedClearance = clearance;
                    leastExposed = spot;
                }
                var distance = spot.Length();
                if (clearance < CrossClearance || distance >= bestDistance) continue;
                bestDistance = distance;
                best = spot;
            }
        return best ?? leastExposed;
    }

    private float ClearanceFromKnownCrosses(Vector3 spot)
    {
        var clearance = float.MaxValue;
        for (var slot = 0; slot < 8; slot++)
            if (state.Puddles[slot] is { } puddle)
                clearance = MathF.Min(clearance, state.DistanceToCross(puddle, (PartyRole)slot, spot));
        return clearance;
    }

    private IAiMove WaitNearStaticSpotsClearOfPulses()
    {
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++)
            spots[slot] = NearestPulseSafeSpot(M9sAetherlettingState.AtBearing(M9sAetherlettingState.ToxicBearings[slot], EdgeRadius));
        return AiMove.Create(spots).NaturalOrder();
    }

    private Vector2 NearestPulseSafeSpot(Vector3 home)
    {
        var best = new Vector2(home.X, home.Z);
        var bestDistance = float.MaxValue;
        for (var x = -EdgeRadius; x <= EdgeRadius; x += 1f)
            for (var z = -EdgeRadius; z <= EdgeRadius; z += 1f)
            {
                var spot = new Vector2(x, z);
                if (spot.Length() > EdgeRadius || !IsClearOfPulses(spot)) continue;
                var distance = Vector2.Distance(spot, new Vector2(home.X, home.Z));
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

    private IAiMove SeamSpots(bool beforeFirstCone)
    {
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++)
            spots[slot] = SeamSpot(M9sAetherlettingState.ToxicBearings[slot], beforeFirstCone);
        return AiMove.Create(spots).NaturalOrder();
    }

    private Vector2 SeamSpot(float bearing, bool beforeFirstCone)
    {
        var towardClockwise = LaterSectorIsClockwise(bearing) == beforeFirstCone;
        var p = M9sAetherlettingState.AtBearing(bearing + (towardClockwise ? SeamOffset : -SeamOffset), EdgeRadius);
        return new Vector2(p.X, p.Z);
    }

    private bool LaterSectorIsClockwise(float bearing) =>
        state.PairFiringThrough(bearing + 22.5f) > state.PairFiringThrough(bearing - 22.5f);

    private int EarlierSectorPair(float bearing) =>
        Math.Min(state.PairFiringThrough(bearing + 22.5f), state.PairFiringThrough(bearing - 22.5f));
}
