using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s;

public sealed class M9sBatStompDodge(SimWorld world, M9sBatPattern pattern, Func<Vector3, bool> isInsideArena, IReadOnlyList<float> radii)
{
    public static readonly float[] ToxicMarkerBearings = [0f, 180f, 225f, 135f, 270f, 90f, 315f, 45f];

    public const float MarkerRadius = 12f;

    private static readonly float[] BearingOffsets = BuildBearingOffsets();
    private const float BlastClearance = M9sBatPattern.BlastRadius + 1.5f;
    private const float PopHitReach = M9sBatPattern.BlastRadius + 0.75f;
    private const float VulnSeconds = 1.96f;
    private const float VulnMargin = 0.3f;
    private const float BlastResolveDelay = 0.98f;
    private const float ArrivalMargin = 0.15f;
    private const float RunSpeed = 6f;
    private const float PlanStep = 0.05f;
    private const float PlanTail = 2f;

    public Vector2?[] MarkerSpots()
    {
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++)
        {
            var p = M9sBatPattern.AtBearing(ToxicMarkerBearings[slot], MarkerRadius);
            spots[slot] = new Vector2(p.X, p.Z);
        }
        return spots;
    }

    public void DodgeBatRing(int ringIndex, float decidedAt, float nextDecisionAt)
    {
        var blasts = pattern.BlastPositions(ringIndex);
        var blastAt = pattern.Rings[ringIndex].CastAt + BlastResolveDelay;
        var steps = (int)((nextDecisionAt + PlanTail - decidedAt) / PlanStep);
        var playerSlot = (int)world.Party.PlayerRole;
        var vulnUntil = new float[8];
        var planned = new Dictionary<int, Trajectory>();
        for (var slot = 0; slot < 8; slot++)
        {
            if (world.Party.Get(slot) is not { } member || !member.IsAlive()) continue;
            vulnUntil[slot] = decidedAt + (member.FindStatus(StatusId.MagicVulnerabilityUp)?.Remaining ?? float.NegativeInfinity);
            if (slot == playerSlot)
                planned[slot] = new Trajectory(pattern, decidedAt, Flat(member.Position), Flat(member.Position), IsCursed(member), steps);
        }

        for (var slot = 0; slot < 8; slot++)
        {
            if (slot == playerSlot) continue;
            if (world.Party.Get(slot) is not { } bot || !bot.IsAlive()) continue;
            var from = Flat(bot.Position);
            var cursed = IsCursed(bot);
            var choice = SafestSpotNearMarker(slot, from, cursed, blasts, blastAt, decidedAt, steps, planned, vulnUntil, playerSlot);
            var spot = choice?.Spot ?? WidestBerthNearMarker(ToxicMarkerBearings[slot], blasts);
            planned[slot] = choice?.Path ?? new Trajectory(pattern, decidedAt, from, Flat(spot), cursed, steps);
            bot.MoveTo(spot);
        }
    }

    private (Vector3 Spot, Trajectory Path)? SafestSpotNearMarker(
        int slot, Vector2 from, bool cursed, IReadOnlyList<Vector3> blasts, float blastAt, float decidedAt, int steps,
        Dictionary<int, Trajectory> planned, float[] vulnUntil, int playerSlot)
    {
        (Vector3, Trajectory)? best = null;
        var bestScore = (Bots: int.MaxValue, Player: int.MaxValue, Cost: float.MaxValue);
        foreach (var offset in BearingOffsets)
            foreach (var radius in radii)
            {
                var cost = MathF.Abs(offset) / 12f + MathF.Abs(radius - MarkerRadius) / 2.5f;
                if (bestScore is { Bots: 0, Player: 0 } && cost >= bestScore.Cost) continue;
                var spot = M9sBatPattern.AtBearing(ToxicMarkerBearings[slot] + offset, radius);
                if (!isInsideArena(spot) || NearestDistance(spot, blasts) < BlastClearance) continue;
                if (Vector2.Distance(from, Flat(spot)) / RunSpeed > blastAt - decidedAt - ArrivalMargin) continue;
                var path = new Trajectory(pattern, decidedAt, from, Flat(spot), cursed, steps);
                planned[slot] = path;
                var dying = DyingToPops(planned, vulnUntil, decidedAt);
                planned.Remove(slot);
                var playerDies = dying.Remove(playerSlot) ? 1 : 0;
                var score = (Bots: dying.Count, Player: playerDies, Cost: cost);
                if (score.CompareTo(bestScore) >= 0) continue;
                bestScore = score;
                best = (spot, path);
            }
        return best;
    }

    private static HashSet<int> DyingToPops(Dictionary<int, Trajectory> paths, float[] vulnUntil, float decidedAt)
    {
        var hits = new Dictionary<int, List<float>>();
        foreach (var slot in paths.Keys) hits[slot] = [];
        foreach (var (popper, path) in paths)
        {
            if (path.PopStep is not { } step) continue;
            var at = path.Points[step];
            var time = decidedAt + step * PlanStep;
            foreach (var (other, otherPath) in paths)
                if (other == popper || Vector2.Distance(at, otherPath.Points[step]) <= PopHitReach)
                    hits[other].Add(time);
        }

        var dying = new HashSet<int>();
        foreach (var (slot, times) in hits)
        {
            if (times.Count == 0) continue;
            times.Sort();
            if (times[0] < vulnUntil[slot] + VulnMargin) dying.Add(slot);
            for (var i = 1; i < times.Count; i++)
                if (times[i] - times[i - 1] < VulnSeconds + VulnMargin) dying.Add(slot);
        }
        return dying;
    }

    private static bool IsCursed(SimCharacter member) => member.HasStatus(StatusId.CurseOfTheBombpyre);

    private sealed class Trajectory
    {
        public Vector2[] Points { get; }
        public int? PopStep { get; }

        public Trajectory(M9sBatPattern pattern, float start, Vector2 from, Vector2 to, bool cursed, int steps)
        {
            Points = new Vector2[steps];
            var length = Vector2.Distance(from, to);
            var direction = length > 1e-4f ? (to - from) / length : Vector2.Zero;
            for (var k = 0; k < steps; k++)
            {
                var time = start + k * PlanStep;
                Points[k] = from + direction * MathF.Min(RunSpeed * k * PlanStep, length);
                if (cursed && PopStep == null && Points[k].Length() <= pattern.BatRingRadiusAt(time)) PopStep = k;
            }
        }
    }

    private Vector3 WidestBerthNearMarker(float markerBearing, IReadOnlyList<Vector3> blasts)
    {
        var best = M9sBatPattern.AtBearing(markerBearing, MarkerRadius);
        var bestClearance = float.MinValue;
        foreach (var offset in BearingOffsets)
            foreach (var radius in radii)
            {
                var spot = M9sBatPattern.AtBearing(markerBearing + offset, radius);
                if (!isInsideArena(spot)) continue;
                var clearance = NearestDistance(spot, blasts);
                if (clearance <= bestClearance) continue;
                bestClearance = clearance;
                best = spot;
            }
        return best;
    }

    private static float[] BuildBearingOffsets()
    {
        var offsets = new List<float> { 0f };
        for (var step = 12f; step <= 180f; step += 12f)
        {
            offsets.Add(-step);
            offsets.Add(step);
        }
        return offsets.ToArray();
    }

    private static Vector2 Flat(Vector3 p) => new(p.X, p.Z);

    private static float NearestDistance(Vector3 from, IReadOnlyList<Vector3> points)
    {
        var nearest = float.MaxValue;
        foreach (var p in points)
            nearest = MathF.Min(nearest, Vector2.Distance(new Vector2(from.X, from.Z), new Vector2(p.X, p.Z)));
        return nearest;
    }
}
