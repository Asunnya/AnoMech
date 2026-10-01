using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Dsr.P1Knights;

public sealed class DsrP1KnightsAi : IScenarioAi<DsrP1KnightsState>
{
    public string Name => "Toolbox (PF)";

    private const float CardinalChainStart = 1.5f;
    private const float DiagonalChainStart = 2.5f;
    private const float SafeQuadrantRadius = 14.1f;
    private const float ExecutionBaitRadius = 21.9f;
    private const float FullDimensionRadius = 9f;

    private static readonly WaymarkSlot[] NorthPreyMarkers = [WaymarkSlot.Two, WaymarkSlot.Three, WaymarkSlot.One, WaymarkSlot.Four];
    private static readonly WaymarkSlot[] SouthPreyMarkers = [WaymarkSlot.B, WaymarkSlot.C, WaymarkSlot.A, WaymarkSlot.D];
    private static readonly float[] BrightwingBaits = [128.16f, 133.20f, 138.23f, 143.27f];

    private static readonly Vector2 PrisonStackOffset = new(0f, 1f);
    private static readonly Vector2 PrisonEastDropOffset = new(6f, 0f);
    private static readonly Vector2 PrisonWestDropOffset = new(-5.5f, 2.5f);
    private static readonly Vector2[] BrightwingBaitOffsets = [new(-1.2f, 7f), new(1.2f, 7f)];

    private DsrP1KnightsState state = null!;
    private SimWorld world = null!;

    public void Run(DsrP1KnightsState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        var ai = new AiManager(world);

        ai.Move(0.3f, () => AiMove.Create(TanksNorthPartySouth()).NaturalOrder());
        world.Events.Add(14.5f, OffTankWalksThroughShieldBashTether);
        ai.Move(16.4f, () => AiMove.Create(OffTankNorthPartyStacksSouth()).NaturalOrder());
        ai.Move(25.0f, () => AiMove.Create(TanksNorthPartySouth()).NaturalOrder());

        ai.Move(35.0f, () => AiMove.Create(PreySpreadAndStack(state.FirstSlashTargets, NorthPreyMarkers, new Vector2(0f, 4.5f))).NaturalOrder(), jitter: 0f);
        ai.Move(41.2f, () => AiMove.Create(PreySpreadAndStack(state.SecondSlashTargets, SouthPreyMarkers, new Vector2(0f, -4.5f))).NaturalOrder(), jitter: 0f);
        ai.Move(48.6f, () => AiMove.Create(TanksNorthPartySouth()).NaturalOrder());
        ai.Move(50.9f, () => AiMove.Create(GroupOppositeAdelphel()).NaturalOrder());
        ai.Move(59.2f, () => AiMove.Create(GroupInSafeQuadrantMainTankPastThem()).NaturalOrder());
        ai.GiveInvuln(63.8f, PartyRole.MainTank, 6f);
        ai.Move(66.0f, () => AiMove.Create(TanksNorthPartySouth()).NaturalOrder());

        ai.Move(77.2f, () => AiMove.Create(ChainStartsAroundGrinnaux()).NaturalOrder(), jitter: 0f);
        ai.Move(85.6f, () => AiMove.Create(TanksNorthPartySouth()).NaturalOrder());
        ai.Move(95.4f, () => AiMove.Create(state.SecondDimension == Dimension.Empty ? TanksNorthPartySouth() : MaxMeleeAroundGrinnaux()).NaturalOrder());
        ai.Move(100.4f, () => AiMove.Create(TanksNorthPartySouth()).NaturalOrder());

        ai.Move(109.5f, () => AiMove.Create(StackInPrison()).NaturalOrder());
        for (var pair = 0; pair < BrightwingBaits.Length; pair++)
        {
            var bait = BrightwingBaits[pair];
            var roles = DsrP1KnightsState.BrightwingPairs[pair];
            var drop = pair % 2 == 0 ? PrisonEastDropOffset : PrisonWestDropOffset;
            ai.Move(bait - 3f, () => AiMove.Create(PairBaitsBrightwing(roles)).NaturalOrder(), jitter: 0.1f);
            ai.Move(bait + 0.4f, () => AiMove.Create(PairCarriesSkyblindOut(roles, drop)).NaturalOrder(), jitter: 0.1f);
            ai.Move(bait + 5.7f, () => AiMove.Create(PairReturnsBehindCharibert(roles)).NaturalOrder(), jitter: 0.1f);
        }
        ai.Move(149.0f, () => AiMove.Create(MainTankNorthForThordan()).NaturalOrder());
    }

    private void OffTankWalksThroughShieldBashTether()
    {
        if (world.Party.Get(PartyRole.OffTank) is not { } offTank || !world.Party.IsBotDriven(offTank)) return;
        if (state.ShieldBashTether?.B is not { } holder || holder == offTank) return;
        offTank.MoveTo(new Vector3(holder.Position.X * 0.5f, 0f, holder.Position.Z * 0.5f), 6.5f);
    }

    private static Vector2?[] TanksNorthPartySouth() =>
    [
        new(-0.8f, -4f),
        new(0.8f, -4f),
        new(-1f, 4f),
        new(1f, 4f),
        new(-1.5f, 3.2f),
        new(1.5f, 3.2f),
        new(-1f, 5f),
        new(1f, 5f),
    ];

    private static Vector2?[] OffTankNorthPartyStacksSouth()
    {
        var spots = Enumerable.Repeat<Vector2?>(new Vector2(0f, 3.5f), 8).ToArray();
        spots[(int)PartyRole.OffTank] = new Vector2(0f, -4f);
        spots[(int)PartyRole.MainTank] = new Vector2(-3f, -4f);
        return spots;
    }

    private static Vector2?[] PreySpreadAndStack(IReadOnlyList<PartyRole> prey, WaymarkSlot[] markers, Vector2 stack)
    {
        var spots = Enumerable.Repeat<Vector2?>(stack, 8).ToArray();
        for (var i = 0; i < prey.Count; i++)
            spots[(int)prey[i]] = Flat(DsrConstants.Phase1Waymark(markers[i]));
        return spots;
    }

    private Vector2?[] GroupOppositeAdelphel()
    {
        var opposite = Flat(DsrConstants.AtBearing(state.AdelphelLandingBearing + 180f, 5f));
        return Enumerable.Repeat<Vector2?>(opposite, 8).ToArray();
    }

    private Vector2?[] GroupInSafeQuadrantMainTankPastThem()
    {
        var bearing = state.SafeQuadrantBearing();
        var spots = Enumerable.Repeat<Vector2?>(Flat(DsrConstants.AtBearing(bearing, SafeQuadrantRadius)), 8).ToArray();
        spots[(int)PartyRole.MainTank] = Flat(DsrConstants.AtBearing(bearing, ExecutionBaitRadius));
        return spots;
    }

    private Vector2?[] ChainStartsAroundGrinnaux()
    {
        var spots = new Vector2?[8];
        foreach (var role in Enum.GetValues<PartyRole>())
        {
            var bearing = state.ChainBearing(role);
            var radius = bearing % 90f == 0f ? CardinalChainStart : DiagonalChainStart;
            spots[(int)role] = Flat(DsrConstants.AtBearing(bearing, radius));
        }
        return spots;
    }

    private static Vector2?[] MaxMeleeAroundGrinnaux()
    {
        var spots = new Vector2?[8];
        spots[(int)PartyRole.MainTank] = new Vector2(-1f, -FullDimensionRadius);
        spots[(int)PartyRole.OffTank] = new Vector2(1f, -FullDimensionRadius);
        float[] southBearings = [165f, 195f, 150f, 210f, 175f, 185f];
        PartyRole[] rest = [PartyRole.RegenHealer, PartyRole.ShieldHealer, PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps];
        for (var i = 0; i < rest.Length; i++)
            spots[(int)rest[i]] = Flat(DsrConstants.AtBearing(southBearings[i], FullDimensionRadius));
        return spots;
    }

    private static Vector2 InPrison(Vector2 offset) => Flat(DsrP1KnightsState.PrisonCentre) + offset;

    private static Vector2?[] StackInPrison() => Enumerable.Repeat<Vector2?>(InPrison(PrisonStackOffset), 8).ToArray();

    private static Vector2?[] PairReturnsBehindCharibert(PartyRole[] pair)
    {
        var spots = new Vector2?[8];
        foreach (var role in pair) spots[(int)role] = InPrison(PrisonStackOffset);
        return spots;
    }

    private static Vector2?[] PairBaitsBrightwing(PartyRole[] pair)
    {
        var spots = new Vector2?[8];
        spots[(int)pair[0]] = InPrison(BrightwingBaitOffsets[0]);
        spots[(int)pair[1]] = InPrison(BrightwingBaitOffsets[1]);
        return spots;
    }

    private static Vector2?[] PairCarriesSkyblindOut(PartyRole[] pair, Vector2 drop)
    {
        var spots = new Vector2?[8];
        spots[(int)pair[0]] = InPrison(drop + new Vector2(0f, -0.8f));
        spots[(int)pair[1]] = InPrison(drop + new Vector2(0f, 0.8f));
        return spots;
    }

    private static Vector2?[] MainTankNorthForThordan()
    {
        var spots = Enumerable.Repeat<Vector2?>(new Vector2(0f, 5f), 8).ToArray();
        spots[(int)PartyRole.MainTank] = new Vector2(0f, -6f);
        spots[(int)PartyRole.OffTank] = new Vector2(1.5f, -5f);
        return spots;
    }

    private static Vector2 Flat(Vector3 p) => new(p.X, p.Z);
}
