using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.M9s.VampStomp;

public sealed class M9sVampStompAi : IScenarioAi<M9sVampStompState>
{
    public string Name => "Toxic / Hector";

    private static readonly float[] MarkerBearings = [0f, 180f, 225f, 135f, 270f, 90f, 315f, 45f];
    private static readonly float[] BearingOffsets = [0f, -12f, 12f, -24f, 24f, -36f, 36f, -48f, 48f];
    private static readonly float[] SpreadRadii = [8.5f, 10f, 11f, 12f, 14.5f, 17f, 19.5f, 22f, 24.5f, 26.5f];

    private const float MarkerRadius = 12f;
    private const float BlastClearance = M9sVampStompState.BlastRadius + 1.5f;
    private const float SpreadClearance = M9sVampStompState.BlastRadius + 0.5f;
    private const float ArenaReach = M9sConstants.Geometry.ArenaHalfWidth - 1f;

    private M9sVampStompState state = null!;
    private SimWorld world = null!;

    public void Run(M9sVampStompState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        var ai = new AiManager(world);
        ai.Move(0.5f, Uptime);
        ai.Move(12.5f, TanksSplitForHardcore);
        ai.Move(20.3f, SpreadOnMarkersOutsideStomp);
        world.Events.Add(30.3f, () => DodgeBatRing(0));
        world.Events.Add(34.6f, () => DodgeBatRing(1));
        world.Events.Add(38.1f, () => DodgeBatRing(2));
        ai.Move(41.6f, StackForBrutalRain);
    }

    private IAiMove Uptime() => AiMove.Create(
        new(0f, -5f),
        new(-3f, -4.5f),
        new(-3f, 7f),
        new(3f, 7f),
        new(-5f, 0f),
        new(5f, 0f),
        new(-5f, 6f),
        new(5f, 6f)
    ).NaturalOrder();

    private IAiMove TanksSplitForHardcore() => AiMove.Create(
        new(-5f, -6f),
        new(5f, -6f),
        new(-3f, 7f),
        new(3f, 7f),
        new(-4.5f, 2f),
        new(4.5f, 2f),
        new(-5f, 6f),
        new(5f, 6f)
    ).NaturalOrder();

    private IAiMove SpreadOnMarkersOutsideStomp()
    {
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++)
        {
            var p = M9sVampStompState.AtBearing(MarkerBearings[slot], MarkerRadius);
            spots[slot] = new Vector2(p.X, p.Z);
        }
        return AiMove.Create(spots).NaturalOrder();
    }

    private IAiMove StackForBrutalRain() => AiMove.All(new Vector2(0f, -1.5f));

    private void DodgeBatRing(int ringIndex)
    {
        var blasts = state.BlastPositions(ringIndex);
        var taken = new List<Vector3>(8);
        var playerSlot = (int)world.Party.PlayerRole;
        for (var slot = 0; slot < 8; slot++)
        {
            if (slot == playerSlot) continue;
            if (world.Party.Get(slot) is not { } bot || !bot.IsAlive()) continue;
            var spot = SafeSpotNearMarker(MarkerBearings[slot], blasts, taken) ?? WidestBerthNearMarker(MarkerBearings[slot], blasts);
            taken.Add(spot);
            bot.MoveTo(spot);
        }
    }

    private static Vector3? SafeSpotNearMarker(float markerBearing, IReadOnlyList<Vector3> blasts, List<Vector3> taken)
    {
        Vector3? best = null;
        var bestCost = float.MaxValue;
        foreach (var offset in BearingOffsets)
            foreach (var radius in SpreadRadii)
            {
                var spot = M9sVampStompState.AtBearing(markerBearing + offset, radius);
                if (!IsInsideArena(spot) || NearestDistance(spot, blasts) < BlastClearance || NearestDistance(spot, taken) < SpreadClearance)
                    continue;
                var cost = MathF.Abs(offset) / 12f + MathF.Abs(radius - MarkerRadius) / 2.5f;
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = spot;
            }
        return best;
    }

    private static Vector3 WidestBerthNearMarker(float markerBearing, IReadOnlyList<Vector3> blasts)
    {
        var best = M9sVampStompState.AtBearing(markerBearing, MarkerRadius);
        var bestClearance = float.MinValue;
        foreach (var offset in BearingOffsets)
            foreach (var radius in SpreadRadii)
            {
                var spot = M9sVampStompState.AtBearing(markerBearing + offset, radius);
                if (!IsInsideArena(spot)) continue;
                var clearance = NearestDistance(spot, blasts);
                if (clearance <= bestClearance) continue;
                bestClearance = clearance;
                best = spot;
            }
        return best;
    }

    private static float NearestDistance(Vector3 from, IReadOnlyList<Vector3> points)
    {
        var nearest = float.MaxValue;
        foreach (var p in points)
            nearest = MathF.Min(nearest, Vector2.Distance(new Vector2(from.X, from.Z), new Vector2(p.X, p.Z)));
        return nearest;
    }

    private static bool IsInsideArena(Vector3 spot) => MathF.Abs(spot.X) <= ArenaReach && MathF.Abs(spot.Z) <= ArenaReach;
}
