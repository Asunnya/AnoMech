using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Uwu.P3Titan;

public sealed class UwuP3TitanAi : IScenarioAi<UwuP3TitanState>
{
    public string Name => "Zeal / The Balance";

    private const float RunSpeed = 6f;
    private const float Margin = 1f;
    private const float GroupSpread = 0.5f;
    private const float SearchStep = 0.6f;
    private const float LandslideLength = 40f;
    private const float UpheavalStandOff = 3.8f;
    private const float TankStandOff = 2.5f;
    private const float PlanStep = 0.3f;

    private UwuP3TitanState state = null!;
    private SimWorld world = null!;

    public void Run(UwuP3TitanState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        var ai = new AiManager(world);

        ai.Move(0.5f, () => Group(new Vector2(0f, 16.5f)));
        ai.Move(6.0f, TankNorthOfTitanPartySouth);
        ai.Move(23.0f, () => Group(new Vector2(0f, 5.5f)));
        PlanEvery(ai, 24.4f, 30.4f, _ => []);

        ai.Move(30.8f, () => Group(OppositeFirstJump(13.5f)));
        ai.Move(36.0f, () => Group(state.FromJumpFrame(new Vector2(14f - UpheavalStandOff, 0f)), 0.1f), jitter: 0f);
        ai.Move(40.1f, () => Group(UpheavalStandingSpot(), 0.1f), jitter: 0f);
        ai.Move(46.8f, SpreadForLandslidesJailedTowardTheSixthBomb);
        ai.Move(48.6f, () => HolderTo(new Vector2(-8f, -5f * state.SafeSide)));
        ai.Move(50.75f, JailedIntoTheChain);
        ai.Move(50.8f, () => HolderTo(new Vector2(-8f, 0f)));
        ai.Move(53.05f, () => HolderTo(new Vector2(-8f, -5f * state.SafeSide)));
        PlanEvery(ai, 46.9f, 55.2f, _ => [(int)state.Holder, .. state.GaolTargets.Select(r => (int)r)]);
        PlanEvery(ai, 55.3f, 84.6f, _ => []);

        ai.Move(84.9f, () => Group(OppositeSecondJump(13f)));
        ai.Move(90.1f, () => Group(OppositeSecondJump(8.5f)));
        PlanEvery(ai, 92.5f, 116.0f, _ => []);
        PlanEvery(ai, 116.0f, 124.5f, _ => [(int)PartyRole.OffTank]);
        ai.Move(116.0f, () => TankOppositeTheParty(PartyRole.OffTank));
        PlanEvery(ai, 124.5f, 141.0f, _ => []);
        PlanEvery(ai, 141.0f, 147.9f, _ => [(int)PartyRole.MainTank]);
        ai.Move(141.0f, () => TankOppositeTheParty(PartyRole.MainTank));
    }

    private void PlanEvery(AiManager ai, float from, float to, Func<float, int[]> excluded)
    {
        for (var t = from; t < to; t += PlanStep)
        {
            var at = t;
            ai.Move(at, () => GroupDodgesKnownHazards(at, excluded(at)), jitter: 0f);
        }
    }

    private static IAiMove Nobody() => AiMove.Create(new Vector2?[8]).NaturalOrder();

    private IAiMove Group(Vector2 anchor, float spread = GroupSpread)
    {
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++) spots[slot] = anchor + SpreadOffset(slot, spread);
        return AiMove.Create(spots).NaturalOrder();
    }

    private static Vector2 SpreadOffset(int slot, float spread)
    {
        var angle = slot * MathF.PI / 4f;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * spread;
    }

    private static IAiMove Only(PartyRole role, Vector2 spot)
    {
        var spots = new Vector2?[8];
        spots[(int)role] = spot;
        return AiMove.Create(spots).NaturalOrder();
    }

    private IAiMove TankNorthOfTitanPartySouth()
    {
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++) spots[slot] = new Vector2(0f, 5.5f) + SpreadOffset(slot, GroupSpread);
        spots[(int)PartyRole.MainTank] = new Vector2(0f, -3.5f);
        return AiMove.Create(spots).NaturalOrder();
    }

    private Vector2 OppositeFirstJump(float radius) => Flat(UwuP3TitanState.AtBearing(state.FirstJumpBearing + 180f, radius));

    private Vector2 OppositeSecondJump(float radius) => Flat(UwuP3TitanState.AtBearing(state.SecondJumpBearing + 180f, radius));

    private Vector2 UpheavalStandingSpot()
    {
        var titan = new Vector2(14f, 0f);
        var landing = new Vector2(-13.5f, 4f * state.SafeSide);
        return state.FromJumpFrame(titan + Vector2.Normalize(landing - titan) * UpheavalStandOff);
    }

    private IAiMove HolderTo(Vector2 jumpFrameSpot) =>
        state.GaolTargets.Contains(state.Holder) ? Nobody() : Only(state.Holder, state.FromJumpFrame(jumpFrameSpot));

    private Vector2 GaolSpot(int order) => state.FromJumpFrame(order switch
    {
        0 => new Vector2(-6.3f, 2.9f * state.SafeSide),
        1 => new Vector2(-0.8f, 5.3f * state.SafeSide),
        _ => new Vector2(-4.0f, 10.3f * state.SafeSide),
    });

    private IAiMove SpreadForLandslidesJailedTowardTheSixthBomb()
    {
        var spots = new Vector2?[8];
        for (var i = 0; i < state.GaolTargets.Count; i++)
            spots[(int)state.GaolTargets[i]] = i == 0 ? state.FromJumpFrame(new Vector2(-6.3f, 5.2f * state.SafeSide)) : GaolSpot(i);
        if (!state.GaolTargets.Contains(state.Holder)) spots[(int)state.Holder] = state.FromJumpFrame(new Vector2(-8f, 0f));
        return AiMove.Create(spots).NaturalOrder();
    }

    private IAiMove JailedIntoTheChain()
    {
        var spots = new Vector2?[8];
        for (var i = 0; i < state.GaolTargets.Count; i++) spots[(int)state.GaolTargets[i]] = GaolSpot(i);
        return AiMove.Create(spots).NaturalOrder();
    }

    private IAiMove TankOppositeTheParty(PartyRole tank)
    {
        var others = Enumerable.Range(0, 8).Where(slot => slot != (int)tank).Select(world.Party.Get).OfType<SimCharacter>().Where(m => m.IsAlive()).ToList();
        if (others.Count == 0) return Nobody();
        var titan = Flat(state.TitanPosition);
        var party = others.Aggregate(Vector2.Zero, (sum, m) => sum + Flat(m.Position)) / others.Count;
        var away = titan - party;
        var direction = away.Length() < 0.1f ? new Vector2(0f, -1f) : Vector2.Normalize(away);
        return Only(tank, ClampToArena(titan + direction * TankStandOff, UwuP3TitanState.SecondShrinkRadius - 1f));
    }

    private IAiMove GroupDodgesKnownHazards(float now, int[] excluded)
    {
        var holder = (int)state.Holder;
        var members = Enumerable.Range(0, 8)
            .Where(slot => slot != holder && !excluded.Contains(slot) && !state.Jailed.Contains((PartyRole)slot))
            .Select(slot => (slot, member: world.Party.Get(slot)))
            .Where(x => x.member is { } m && m.IsAlive())
            .ToList();
        var spots = new Vector2?[8];
        if (members.Count == 0) return AiMove.Create(spots).NaturalOrder();
        var anchor = members.Aggregate(Vector2.Zero, (sum, x) => sum + Flat(x.member!.Position)) / members.Count;
        var target = NearestSpotClearOfUpcomingHazards(anchor, now, Margin + GroupSpread);
        foreach (var (slot, _) in members) spots[slot] = target + SpreadOffset(slot, GroupSpread);
        if (!excluded.Contains(holder) && !state.Jailed.Contains(state.Holder) && world.Party.Get(holder) is { } tank && tank.IsAlive())
            spots[holder] = state.Hazards.Any(h => h.At > now)
                ? NearestSpotClearOfUpcomingHazards(Flat(tank.Position), now, Margin)
                : BesideTitanAwayFrom(target, now);
        return AiMove.Create(spots).NaturalOrder();
    }

    private Vector2 BesideTitanAwayFrom(Vector2 party, float now)
    {
        var titan = Flat(state.TitanPosition);
        var away = titan - party;
        var direction = away.Length() < 0.1f ? new Vector2(0f, -1f) : Vector2.Normalize(away);
        return ClampToArena(titan + direction * TankStandOff, ArenaRadiusAt(now) - 1f);
    }

    private Vector2 NearestSpotClearOfUpcomingHazards(Vector2 from, float now, float margin)
    {
        var upcoming = state.Hazards.Where(h => h.At > now).OrderBy(h => h.At).ToList();
        var reach = ArenaRadiusAt(now) - 1f - GroupSpread;
        for (var count = upcoming.Count; count >= 0; count--)
        {
            var hazards = upcoming.Take(count).ToList();
            Vector2? best = null;
            var bestDistance = float.MaxValue;
            for (var x = -reach; x <= reach; x += SearchStep)
                for (var z = -reach; z <= reach; z += SearchStep)
                {
                    var spot = new Vector2(x, z);
                    if (spot.Length() > reach) continue;
                    var distance = Vector2.Distance(from, spot);
                    if (distance >= bestDistance || !ClearOfHazardsOnTheWay(from, spot, now, hazards, margin)) continue;
                    best = spot;
                    bestDistance = distance;
                }
            if (best is { } found) return found;
        }
        return from;
    }

    private static bool ClearOfHazardsOnTheWay(Vector2 from, Vector2 to, float now, IEnumerable<UwuP3TitanState.Hazard> hazards, float margin)
    {
        var distance = Vector2.Distance(from, to);
        var direction = distance > 0.01f ? (to - from) / distance : Vector2.Zero;
        foreach (var hazard in hazards)
        {
            var whenItHits = from + direction * MathF.Min(distance, RunSpeed * (hazard.At - now));
            if (IsInside(whenItHits, hazard, margin)) return false;
        }
        return true;
    }

    private static bool IsInside(Vector2 point, UwuP3TitanState.Hazard hazard, float margin)
    {
        if (!hazard.IsLane) return Vector2.Distance(point, hazard.Origin) <= hazard.Size + margin;
        var rad = hazard.Bearing * MathF.PI / 180f;
        var forward = new Vector2(MathF.Sin(rad), -MathF.Cos(rad));
        var offset = point - hazard.Origin;
        var along = Vector2.Dot(offset, forward);
        if (along < -margin || along > LandslideLength) return false;
        return MathF.Abs(offset.X * forward.Y - offset.Y * forward.X) <= hazard.Size + margin;
    }

    private static float ArenaRadiusAt(float now) =>
        now < 36.7f ? 19.4f : now < 91f ? UwuP3TitanState.FirstShrinkRadius : UwuP3TitanState.SecondShrinkRadius;

    private static Vector2 ClampToArena(Vector2 spot, float limit)
    {
        return spot.Length() > limit ? Vector2.Normalize(spot) * limit : spot;
    }

    private static Vector2 Flat(Vector3 p) => new(p.X, p.Z);
}
