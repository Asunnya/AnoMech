using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.M9s.Flails;

public sealed class M9sFlailsAi : IScenarioAi<M9sFlailsState>
{
    public string Name => "Toxic / Hector";

    private const float PlanFrom = 12f;
    private const float PlanUntil = 78f;
    private const float DecisionStep = 0.25f;
    private const float Horizon = 2.5f;
    private const float RunSpeed = 6f;
    private const float HazardMargin = 1f;
    private const float TowerHoldRadius = 2.2f;
    private const float TowerHoldLead = 2.2f;
    private const float DoornailStandoff = 2f;

    private static readonly Vector3[] Candidates = BuildCandidates();

    private M9sFlailsState state = null!;
    private SimWorld world = null!;

    public void Run(M9sFlailsState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        new AiManager(world).Move(0.5f, () => AiMove.All(new Vector2(0f, 2f)));
        for (var t = PlanFrom; t < PlanUntil; t += DecisionStep)
        {
            var now = t;
            world.Events.Add(now, () => MoveEveryBotToSafety(now));
        }
    }

    private void MoveEveryBotToSafety(float now)
    {
        var saws = M9sFlailsState.SawHitsBetween(now - 0.2f, now + Horizon + 0.2f);
        var checkTimes = CheckTimes(now, saws);
        var playerSlot = (int)world.Party.PlayerRole;
        for (var slot = 0; slot < 8; slot++)
        {
            if (slot == playerSlot) continue;
            if (world.Party.Get(slot) is not { } bot || !bot.IsAlive()) continue;
            if (SafestSpotNearHome(slot, bot.Position, now, checkTimes, saws) is { } spot)
                bot.MoveTo(spot, RunSpeed);
        }
    }

    private static List<float> CheckTimes(float now, IReadOnlyList<SawHit> saws)
    {
        var times = saws.Where(h => h.At > now && h.At <= now + Horizon).Select(h => h.At).ToList();
        times.AddRange(M9sFlailsState.RoundResolveAt.Where(t => t > now && t <= now + Horizon));
        for (var t = now + DecisionStep; t <= now + Horizon; t += DecisionStep) times.Add(t);
        times.Sort();
        return times;
    }

    private Vector3? SafestSpotNearHome(int slot, Vector3 from, float now, List<float> checkTimes, IReadOnlyList<SawHit> saws)
    {
        var isTank = slot < 2;
        var home = Home(slot, now);
        var tower = TowerToHold(slot, now);
        foreach (var spot in Candidates.OrderBy(c => M9sFlailsState.FlatDistance(c, home) + 0.3f * M9sFlailsState.FlatDistance(c, from)))
        {
            if (M9sFlailsState.FlatDistance(spot, from) > RunSpeed * Horizon) continue;
            if (tower is { } t && M9sFlailsState.FlatDistance(spot, t) > TowerHoldRadius) continue;
            if (IsRouteSafe(from, spot, now, checkTimes, isTank, saws)) return spot;
        }
        return null;
    }

    private bool IsRouteSafe(Vector3 from, Vector3 to, float now, List<float> checkTimes, bool isTank, IReadOnlyList<SawHit> saws)
    {
        var distance = M9sFlailsState.FlatDistance(from, to);
        foreach (var at in checkTimes)
        {
            var covered = distance < 1e-3f ? 1f : MathF.Min(1f, (at - now) * RunSpeed / distance);
            if (state.IsLethal(Vector3.Lerp(from, to, covered), at, isTank, HazardMargin, saws)) return false;
        }
        return true;
    }

    private Vector3 Home(int slot, float now)
    {
        if (slot < 2)
        {
            if (M9sFlailsState.LatestRoundCastBy(now) is not { } round)
                return new Vector3(0f, 0f, slot == 0 ? -4f : 4f);
            return slot == 0 ? state.Rounds[round].NorthTower : state.Rounds[round].SouthTower;
        }
        for (var round = 0; round < 3; round++)
            if (state.PuddleRadius(round, now) is { } radius)
            {
                var nail = state.Rounds[round].Doornail;
                var towardCentre = nail.X > 0 ? -1f : 1f;
                return nail with { X = nail.X + towardCentre * (radius + DoornailStandoff) };
            }
        return Vector3.Zero;
    }

    private Vector3? TowerToHold(int slot, float now)
    {
        if (slot >= 2 || M9sFlailsState.TowerRoundAt(now) is not { } round) return null;
        if (now < M9sFlailsState.RoundResolveAt[round] - TowerHoldLead) return null;
        return slot == 0 ? state.Rounds[round].NorthTower : state.Rounds[round].SouthTower;
    }

    private static Vector3[] BuildCandidates()
    {
        var candidates = new List<Vector3>();
        for (var x = -9; x <= 9; x++)
            for (var z = -19; z <= 19; z++)
                candidates.Add(new Vector3(x, 0f, z));
        return candidates.ToArray();
    }
}
