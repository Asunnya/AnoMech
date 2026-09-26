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
    private const float TightSpread = 0.3f;
    private const float ReactionDelay = 0.3f;

    private static readonly Vector2 TitansLeftSide = new(12f, -5.8f);
    private static readonly Vector2 TitansRightSide = new(12f, 5.8f);

    private UwuP3TitanState state = null!;
    private Vector2? groupTarget;
    private (float SecondHitAt, Vector2 Spot)? wedgeSpot;
    private SimWorld world = null!;

    public void Run(UwuP3TitanState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        groupTarget = null;
        wedgeSpot = null;
        world = worldParam;
        var ai = new AiManager(world);

        ai.Move(0.5f, () => Group(new Vector2(0f, 16.5f)));
        ai.Move(6.0f, () => TankAndParty(new Vector2(0f, 5.5f), new Vector2(0f, -5f)));
        ai.Move(24.5f, () => TankAndParty(new Vector2(-7f, 0f), new Vector2(7f, 0f)));
        ai.Move(27.5f, () => TankAndParty(new Vector2(0f, 5.5f), new Vector2(0f, -5f)));

        ai.Move(30.8f, () => Group(OppositeFirstJump(13.5f)));
        ai.Move(36.0f, () => Group(state.FromJumpFrame(new Vector2(14f - UpheavalStandOff, 0f)), 0.1f), jitter: 0f);
        ai.Move(40.1f, () => Group(UpheavalStandingSpot(), 0.1f), jitter: 0f);
        ai.Move(46.8f, JailedBesideTheirGaolSpotsOutOfTheLandslide, jitter: 0f);
        ai.Move(46.8f, () => PartyTo(new Vector2(-0.8f, -6.1f), withGaolTargets: false), jitter: 0f);
        ai.Move(48.6f, () => HolderTo(new Vector2(-11f, -5f * state.SafeSide)));
        ai.Move(50.40f, JailedIntoTheChain, jitter: 0f, sprint: true);
        ai.Move(50.75f, () => PartyTo(new Vector2(9.5f, -10.2f), withGaolTargets: false), jitter: 0f);
        ai.Move(50.8f, () => HolderTo(new Vector2(-11f, 0f)));
        ai.Move(53.05f, () => HolderTo(new Vector2(-8f, -6f * state.SafeSide)));
        ai.Move(57.0f, () => HolderTo(new Vector2(8f, 0f)), jitter: 0f);
        ai.Move(57.8f, () => PartyTo(TitansLeftSide, withGaolTargets: true), jitter: 0f);

        ai.Move(70.3f, () => PartyTo(TitansRightSide, withGaolTargets: true), jitter: 0f);
        ai.Move(73.3f, () => PartyTo(new Vector2(4f, 8.5f), withGaolTargets: true), jitter: 0f);
        ai.Move(73.3f, () => HolderTo(new Vector2(1f, 0f)), jitter: 0f);
        PlanEvery(ai, 76.1f, 80.3f, _ => []);

        ai.Move(84.9f, () => Group(OppositeSecondJump(10f)));
        ai.Move(90.1f, () => Group(OppositeSecondJump(8.5f)));
        ai.Move(92.5f, PartyInFrontHolderBehindTitan, jitter: 0f);
        PlanEvery(ai, 104.9f, 109.4f, _ => []);
        ai.Move(109.5f, PartyInFrontHolderBehindTitan, jitter: 0f);
        ai.Move(116.0f, OffTankBehindTitanForTheBuster, jitter: 0f);
        ai.Move(126.0f, PartyBehindTitanRangedInFront, jitter: 0f);
        PlanEvery(ai, 128.3f, 141.0f, _ => []);
        PlanEvery(ai, 141.0f, 147.9f, _ => [(int)PartyRole.MainTank]);
        ai.Move(141.0f, () => TankOppositeTheParty(PartyRole.MainTank));
        ai.Move(148.1f, () => Group(Vector2.Zero));
    }

    private IAiMove PartyInFrontHolderBehindTitan()
    {
        groupTarget = null;
        var spots = new Vector2?[8];
        var front = FromSecondJumpFrame(new Vector2(-4f, 0f));
        for (var slot = 0; slot < 8; slot++) spots[slot] = front + SpreadOffset(slot, TightSpread);
        spots[(int)PartyRole.CasterDps] = FromSecondJumpFrame(new Vector2(-10f, 0f));
        if (!state.Jailed.Contains(state.Holder)) spots[(int)state.Holder] = FromSecondJumpFrame(new Vector2(5f, 0f));
        foreach (var jailed in state.Jailed) spots[(int)jailed] = null;
        return AiMove.Create(spots).NaturalOrder();
    }

    private IAiMove OffTankBehindTitanForTheBuster()
    {
        var spots = new Vector2?[8];
        spots[(int)PartyRole.OffTank] = FromSecondJumpFrame(new Vector2(5.5f, 0f));
        spots[(int)PartyRole.MainTank] = FromSecondJumpFrame(new Vector2(-3f, 0f));
        return AiMove.Create(spots).NaturalOrder();
    }

    private IAiMove PartyBehindTitanRangedInFront()
    {
        groupTarget = null;
        var spots = new Vector2?[8];
        var behind = FromSecondJumpFrame(new Vector2(10f, 0f));
        for (var slot = 0; slot < 8; slot++) spots[slot] = behind + SpreadOffset(slot, TightSpread);
        spots[(int)PartyRole.CasterDps] = FromSecondJumpFrame(new Vector2(-10f, 0f));
        spots[(int)PartyRole.RegenHealer] = FromSecondJumpFrame(new Vector2(-10f, 1f));
        return AiMove.Create(spots).NaturalOrder();
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
        groupTarget = null;
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

    private IAiMove TankAndParty(Vector2 tank, Vector2 party)
    {
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++) spots[slot] = party + SpreadOffset(slot, GroupSpread);
        spots[(int)PartyRole.MainTank] = tank;
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

    private IAiMove PartyTo(Vector2 spot, bool withGaolTargets, bool jumpFrame = true)
    {
        groupTarget = null;
        var anchor = jumpFrame ? state.FromJumpFrame(spot) : spot;
        var holderMovesAlone = !state.GaolTargets.Contains(state.Holder);
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++)
        {
            var role = (PartyRole)slot;
            if (holderMovesAlone && role == state.Holder) continue;
            if (!withGaolTargets && state.GaolTargets.Contains(role)) continue;
            spots[slot] = anchor + SpreadOffset(slot, TightSpread);
        }
        return AiMove.Create(spots).NaturalOrder();
    }

    private Vector2 FromSecondJumpFrame(Vector2 eastFrame)
    {
        var (sin, cos) = MathF.SinCos((state.SecondJumpBearing - 90f) * MathF.PI / 180f);
        return new Vector2(eastFrame.X * cos - eastFrame.Y * sin, eastFrame.X * sin + eastFrame.Y * cos);
    }

    private IAiMove HolderAt(Vector2 spot) =>
        state.GaolTargets.Contains(state.Holder) && state.Jailed.Contains(state.Holder) ? Nobody() : Only(state.Holder, spot);

    private IAiMove HolderTo(Vector2 jumpFrameSpot) =>
        state.GaolTargets.Contains(state.Holder) ? Nobody() : Only(state.Holder, state.FromJumpFrame(jumpFrameSpot));

    private Vector2 BesideGaolSpot(int order) => state.FromJumpFrame(order switch
    {
        0 => new Vector2(5.5f, 3.5f * state.SafeSide),
        1 => new Vector2(0f, 3.8f * state.SafeSide),
        _ => new Vector2(-6.7f, 3.8f * state.SafeSide),
    });

    private IAiMove JailedBesideTheirGaolSpotsOutOfTheLandslide()
    {
        var spots = new Vector2?[8];
        for (var i = 0; i < state.GaolTargets.Count; i++) spots[(int)state.GaolTargets[i]] = BesideGaolSpot(i);
        if (!state.GaolTargets.Contains(state.Holder)) spots[(int)state.Holder] = state.FromJumpFrame(new Vector2(-11f, 0f));
        return AiMove.Create(spots).NaturalOrder();
    }

    private IAiMove JailedIntoTheChain()
    {
        var spots = new Vector2?[8];
        for (var i = 0; i < state.GaolTargets.Count; i++) spots[(int)state.GaolTargets[i]] = state.GaolSpot(i);
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
        if (state.AwakenedLandslide is { } landslide && now < landslide.SecondHitAt)
            return EveryoneIntoTheWedgeSafeFromBothHits(landslide, now, excluded);
        var holder = (int)state.Holder;
        var members = Enumerable.Range(0, 8)
            .Where(slot => slot != holder && !excluded.Contains(slot) && !state.Jailed.Contains((PartyRole)slot))
            .Select(slot => (slot, member: world.Party.Get(slot)))
            .Where(x => x.member is { } m && m.IsAlive())
            .ToList();
        var spots = new Vector2?[8];
        if (members.Count == 0) return AiMove.Create(spots).NaturalOrder();
        var anchor = members.Aggregate(Vector2.Zero, (sum, x) => sum + Flat(x.member!.Position)) / members.Count;
        var upcoming = state.Hazards.Where(h => h.At > now).ToList();
        var target = groupTarget is { } kept && kept.Length() <= ArenaRadiusAt(now) - 1f - GroupSpread
            && ClearOfHazardsOnTheWay(anchor, kept, now, upcoming, GroupSpread + 0.2f)
            ? kept
            : NearestSpotClearOfUpcomingHazards(anchor, now, Margin + GroupSpread);
        groupTarget = target;
        foreach (var (slot, _) in members) spots[slot] = target + SpreadOffset(slot, GroupSpread);
        if (!excluded.Contains(holder) && !state.Jailed.Contains(state.Holder) && world.Party.Get(holder) is { } tank && tank.IsAlive())
            spots[holder] = state.Hazards.Any(h => h.At > now)
                ? NearestSpotClearOfUpcomingHazards(Flat(tank.Position), now, Margin)
                : BesideTitanAwayFrom(target, now);
        return AiMove.Create(spots).NaturalOrder();
    }

    private IAiMove EveryoneIntoTheWedgeSafeFromBothHits(UwuP3TitanState.AwakenedLandslideCast landslide, float now, int[] excluded)
    {
        var members = Enumerable.Range(0, 8)
            .Where(slot => !excluded.Contains(slot) && !state.Jailed.Contains((PartyRole)slot))
            .Select(slot => (slot, member: world.Party.Get(slot)))
            .Where(x => x.member is { } m && m.IsAlive())
            .ToList();
        var spots = new Vector2?[8];
        if (members.Count == 0) return AiMove.Create(spots).NaturalOrder();
        if (wedgeSpot is not { } kept || kept.SecondHitAt != landslide.SecondHitAt)
        {
            var anchor = members.Aggregate(Vector2.Zero, (sum, x) => sum + Flat(x.member!.Position)) / members.Count;
            var spot = NearestWedgeSpot(landslide, anchor, now, checkPath: true)
                ?? NearestWedgeSpot(landslide, anchor, now, checkPath: false)
                ?? NearestSpotClearOfUpcomingHazards(anchor, now, Margin + TightSpread);
            wedgeSpot = kept = (landslide.SecondHitAt, spot);
        }
        groupTarget = null;
        var upcoming = state.Hazards.Where(h => h.At > now).ToList();
        foreach (var (slot, member) in members)
        {
            var at = Flat(member!.Position);
            var spot = kept.Spot + SpreadOffset(slot, TightSpread);
            spots[slot] = ClearOfHazardsOnTheWay(at, spot, now, upcoming, TightSpread)
                ? spot
                : NearestSpotClearOfUpcomingHazards(at, now, Margin);
        }
        return AiMove.Create(spots).NaturalOrder();
    }

    // Both hits leave the four wedges at 67.5 degrees either side of the first lines untouched, far enough out.
    private Vector2? NearestWedgeSpot(UwuP3TitanState.AwakenedLandslideCast landslide, Vector2 from, float now, bool checkPath)
    {
        var upcoming = state.Hazards.Where(h => h.At > now).ToList();
        var reach = ArenaRadiusAt(now) - 1f - TightSpread;
        Vector2? best = null;
        var bestDistance = float.MaxValue;
        foreach (var degrees in new[] { 67.5f, -67.5f, 112.5f, -112.5f })
        {
            var rotation = landslide.Rotation + degrees * MathF.PI / 180f;
            var direction = new Vector2(MathF.Sin(rotation), MathF.Cos(rotation));
            for (var radius = 8.5f; radius <= 14f; radius += 0.5f)
            {
                var spot = landslide.Origin + direction * radius;
                if (spot.Length() > reach) break;
                var distance = Vector2.Distance(from, spot);
                if (distance >= bestDistance || !ClearOfHazardsOnTheWay(checkPath ? from : spot, spot, now, upcoming, TightSpread + 0.3f)) continue;
                best = spot;
                bestDistance = distance;
                break;
            }
        }
        return best;
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
        foreach (var tighter in new[] { margin, GroupSpread + 0.25f })
            if (NearestClearSpot(from, now, upcoming, tighter, reach) is { } clearOfAll)
                return clearOfAll;
        for (var count = upcoming.Count - 1; count >= 0; count--)
            if (NearestClearSpot(from, now, upcoming.Take(count).ToList(), margin, reach) is { } found)
                return found;
        return from;
    }

    private static Vector2? NearestClearSpot(Vector2 from, float now, List<UwuP3TitanState.Hazard> hazards, float margin, float reach)
    {
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
        return best;
    }

    private static bool ClearOfHazardsOnTheWay(Vector2 from, Vector2 to, float now, IEnumerable<UwuP3TitanState.Hazard> hazards, float margin)
    {
        var distance = Vector2.Distance(from, to);
        var direction = distance > 0.01f ? (to - from) / distance : Vector2.Zero;
        foreach (var hazard in hazards)
        {
            var whenItHits = from + direction * MathF.Min(distance, RunSpeed * MathF.Max(0f, hazard.At - now - ReactionDelay));
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
        now < 32.6f ? 19.4f : now < 87f ? UwuP3TitanState.FirstShrinkRadius : UwuP3TitanState.SecondShrinkRadius;

    private static Vector2 ClampToArena(Vector2 spot, float limit)
    {
        return spot.Length() > limit ? Vector2.Normalize(spot) * limit : spot;
    }

    private static Vector2 Flat(Vector3 p) => new(p.X, p.Z);
}
