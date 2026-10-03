using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Ucob.P5Exaflares;

public sealed class UcobP5ExaflaresAi : IScenarioAi<UcobP5ExaflaresState>
{
    public string Name => "Dodge";

    private const float BlastClearance = 2f;
    private const float DodgeSpeed = 7f;
    private const float DecisionInterval = 0.75f;
    private const float ArenaReach = 19f;
    private const float MeleeRange = 6f;
    private const float BotComfort = 2f;
    private const float CrowdPenalty = 0.8f;
    private const float MeleeWeight = 1f;
    private const float TravelWeight = 0.35f;
    private const float ImproveMargin = 4f;
    private const float ArrivalEpsilon = 0.4f;
    private const float BerthTravelPenalty = 0.05f;
    private const float ClearanceTolerance = 0.01f;
    private const float RequiredClearance = UcobP5ExaflaresState.HitRadius + BlastClearance - ClearanceTolerance;
    private const float CalmHorizon = 8f;
    private const float MinimumSafeHorizon = 1.5f;

    private static readonly float[] StandOffAlongLane = [-12f, -6f, 0f, 6f, 12f];

    private UcobP5ExaflaresState state = null!;
    private SimWorld world = null!;
    private IReadOnlyList<Vector3> spots = [];
    private readonly Vector3?[] committed = new Vector3?[8];
    private readonly bool[] holding = new bool[8];

    public void Run(UcobP5ExaflaresState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        spots = BuildLaneSpots();
        Array.Clear(committed);
        Array.Clear(holding);

        var decisions = (int)MathF.Ceiling(
            (state.LastHitAt - UcobP5ExaflaresState.FirstTelegraphAt) / DecisionInterval) + 1;
        for (var i = 0; i < decisions; i++)
        {
            var at = UcobP5ExaflaresState.FirstTelegraphAt + i * DecisionInterval;
            state.Timeline.Add(at, () => PlaceBotsOnSafeLanes(at));
        }
    }

    private IReadOnlyList<Vector3> BuildLaneSpots()
    {
        var points = new List<Vector3>();
        foreach (var offset in UcobP5ExaflaresState.LaneOffsets)
            foreach (var along in StandOffAlongLane)
            {
                var spot = state.Lateral * offset + state.Travel * along;
                if (IsInsideArena(spot)) points.Add(spot);
            }
        return points;
    }

    private void PlaceBotsOnSafeLanes(float now)
    {
        var live = TelegraphedHitsWithin(now, CalmHorizon);
        if (live.Count == 0) return;
        var telegraphed = TelegraphedHitsWithin(now, float.PositiveInfinity);

        var taken = new List<Vector3>(8);
        foreach (var (slot, bot) in SlotsMostAtRiskFirst(now, live))
        {
            var from = bot.Position;

            if (committed[slot] is { } destination)
            {
                if (FlatDistance(from, destination) < ArrivalEpsilon) committed[slot] = null;
                else if (IsRouteSafe(from, destination, now, live)) { taken.Add(destination); continue; }
            }

            var holdSafeUntil = IsInsideArena(from)
                ? MathF.Min(SafeUntil(from, from, now, telegraphed), now + CalmHorizon)
                : float.MinValue;
            var best = BestLaneSpot(from, now, taken, telegraphed, out var bestCost, out var bestSafeUntil);
            if (best is null && holdSafeUntil < now + CalmHorizon)
            {
                best = WidestBerth(from, now);
                bestSafeUntil = now + MinimumSafeHorizon;
            }

            var holdOutlastsBest = holdSafeUntil > bestSafeUntil
                || (holdSafeUntil == bestSafeUntil && SpotCost(from, from, taken) <= bestCost + ImproveMargin);
            if (best is not { } target || holdOutlastsBest)
            {
                Hold(slot, bot, from, taken);
                continue;
            }

            committed[slot] = target;
            holding[slot] = false;
            taken.Add(target);
            bot.MoveTo(target, DodgeSpeed);
        }
    }

    private void Hold(int slot, SimCharacter bot, Vector3 at, List<Vector3> taken)
    {
        committed[slot] = null;
        taken.Add(at);
        if (holding[slot]) return;
        bot.MoveTo(at, DodgeSpeed);
        holding[slot] = true;
    }

    private Vector3? BestLaneSpot(Vector3 from, float now, List<Vector3> taken, IReadOnlyList<ExaflareHit> threats,
        out float bestCost, out float bestSafeUntil)
    {
        bestCost = float.MaxValue;
        bestSafeUntil = float.MinValue;
        var calmUntil = now + CalmHorizon;
        Vector3? best = null;
        foreach (var spot in spots)
        {
            var safeUntil = SafeUntil(from, spot, now, threats);
            if (safeUntil <= now + MinimumSafeHorizon) continue;
            var cappedSafeUntil = MathF.Min(safeUntil, calmUntil);
            var cost = SpotCost(from, spot, taken);
            if (cappedSafeUntil < bestSafeUntil || (cappedSafeUntil == bestSafeUntil && cost >= bestCost)) continue;
            bestSafeUntil = cappedSafeUntil;
            bestCost = cost;
            best = spot;
        }
        return best;
    }

    private Vector3? WidestBerth(Vector3 from, float now)
    {
        var threats = TelegraphedHitsWithin(now, MinimumSafeHorizon);
        Vector3? best = null;
        var bestScore = float.MinValue;
        foreach (var spot in spots)
        {
            var score = RouteClearance(from, spot, now, threats) - BerthTravelPenalty * FlatDistance(from, spot);
            if (score <= bestScore) continue;
            bestScore = score;
            best = spot;
        }
        return best;
    }

    private static float SpotCost(Vector3 from, Vector3 to, List<Vector3> taken)
    {
        var cost = MeleeWeight * MathF.Max(FlatDistance(Vector3.Zero, to), MeleeRange)
                   + TravelWeight * FlatDistance(from, to);
        foreach (var other in taken)
        {
            var gap = FlatDistance(other, to);
            if (gap < BotComfort) cost += (BotComfort - gap) * CrowdPenalty;
        }
        return cost;
    }

    private IReadOnlyList<ExaflareHit> TelegraphedHitsWithin(float now, float horizon) =>
        state.Hits.Where(h => h.KnownAt <= now && h.Time > now && h.Time <= now + horizon).ToList();

    private IEnumerable<(int Slot, SimCharacter Bot)> SlotsMostAtRiskFirst(float now, IReadOnlyList<ExaflareHit> threats)
    {
        var slots = new List<(int, SimCharacter)>(8);
        for (var slot = 0; slot < 8; slot++)
            if (world.Party.Get(slot) is { } member && member.IsAlive()) slots.Add((slot, member));
        return slots.OrderBy(b => RouteClearance(b.Item2.Position, b.Item2.Position, now, threats)).ToList();
    }

    private static bool IsRouteSafe(Vector3 from, Vector3 to, float now, IReadOnlyList<ExaflareHit> threats) =>
        RouteClearance(from, to, now, threats) >= RequiredClearance;

    private static float SafeUntil(Vector3 from, Vector3 to, float now, IReadOnlyList<ExaflareHit> threats)
    {
        var until = float.PositiveInfinity;
        foreach (var hit in threats)
            if (hit.Time < until && FlatDistance(PositionAt(from, to, now, hit.Time), hit.Position) < RequiredClearance)
                until = hit.Time;
        return until;
    }

    private static float RouteClearance(Vector3 from, Vector3 to, float now, IReadOnlyList<ExaflareHit> threats)
    {
        var clearance = float.MaxValue;
        foreach (var hit in threats)
            clearance = MathF.Min(clearance, FlatDistance(PositionAt(from, to, now, hit.Time), hit.Position));
        return clearance;
    }

    private static Vector3 PositionAt(Vector3 from, Vector3 to, float now, float when)
    {
        var travel = FlatDistance(from, to);
        if (travel < 1e-3f) return from;
        var covered = MathF.Max(0f, when - now) * DodgeSpeed;
        return Vector3.Lerp(from, to, Math.Clamp(covered / travel, 0f, 1f));
    }

    private static bool IsInsideArena(Vector3 position) => FlatDistance(Vector3.Zero, position) <= ArenaReach;

    private static float FlatDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
