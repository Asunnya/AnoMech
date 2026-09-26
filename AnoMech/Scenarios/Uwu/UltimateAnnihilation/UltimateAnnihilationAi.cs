using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Uwu.UltimateAnnihilation;

public class UltimateAnnihilationAi : IScenarioAi<UltimateAnnihilationState>
{
    public string Name => "NAUR";

    private static readonly Vector2 WestStack = new(-6f, -4.5f);
    private static readonly Vector2 EastOfStack = new(2.5f, -3.7f);
    private static readonly Vector2 NorthWestOfStack = new(-6.5f, -12.5f);
    private static readonly Vector2 NorthEdge = new(-0.8f, -17.5f);
    private static readonly Vector2 NorthWestOutOfLines = new(-9.5f, -12.5f);
    private static readonly Vector2 NorthWestStack = new(-6.5f, -7.5f);
    private static readonly Vector2 NorthOfNorthWestStack = new(-6f, -13f);
    private static readonly Vector2 MainTankOrb = new(1.6f, -3.4f);
    private static readonly Vector2 OffTankOrb = new(2.6f, -4.4f);

    private UltimateAnnihilationState state = null!;

    public void Run(UltimateAnnihilationState state, SimWorld world)
    {
        this.state = state;

        var ai = new AiManager(world);

        ai.Move(4f, () => Everyone(WestStack));
        ai.Move(15f, () => Everyone(EastOfStack));
        ai.Move(17.6f, () => Everyone(WestStack));
        ai.Move(20.6f, SplitForOrbsMesohighAndSearingWind);
        ai.Move(23f, () => Only(state.FirstMesohighTaker, new(12.5f, 1f)));
        ai.Move(24.5f, () => Only(state.SearingWindTarget, new(-3f, 14f)));
        ai.Move(26f, DodgeFirstFeatherRain);
        ai.Move(27.3f, TanksReturnToOrbs);
        ai.Move(28.8f, TanksJoinNorthEdge);
        ai.Move(29.2f, () => Only(state.FirstMesohighTaker, NorthEdge));
        ai.Move(31.8f, DodgeCrimsonCrossAndLandslides);
        ai.Move(35.3f, TanksPopLastOrbsWhileGroupStacksNorthWest);
        ai.Move(40.3f, () => Only(PartyRole.MainTank, new(-2.5f, -6f)));
        ai.Move(43.4f, DodgeSecondFeatherRain);
        ai.Move(46f, SetUpForHomingLasers);
        ai.Move(54.6f, () => Only(state.SearingWindTarget, NorthWestStack));
    }

    private IAiMove SplitForOrbsMesohighAndSearingWind() =>
        Group(NorthWestOfStack, MainTankOrb, OffTankOrb, searingWind: new(-11f, 3f), firstMesohigh: new(-1f, 6.5f));

    private IAiMove DodgeFirstFeatherRain() =>
        Group(NorthEdge, new(4.5f, -8f), new(5.5f, -7f), searingWind: new(0f, 17.5f), firstMesohigh: new(11.5f, -11f));

    private IAiMove TanksReturnToOrbs() =>
        Group(null, MainTankOrb, OffTankOrb);

    private IAiMove TanksJoinNorthEdge() =>
        Group(null, new(-1.5f, -15.5f), new(1f, -16.5f));

    private IAiMove DodgeCrimsonCrossAndLandslides() =>
        Group(NorthWestOutOfLines, new(-8.5f, -10f), new(7.5f, -11f), searingWind: new(9f, 11f));

    private IAiMove TanksPopLastOrbsWhileGroupStacksNorthWest() =>
        Group(NorthWestStack, MainTankOrb, OffTankOrb, searingWind: new(0f, 10.5f));

    private IAiMove DodgeSecondFeatherRain() =>
        Group(NorthOfNorthWestStack, new(-0.5f, -14.5f), new(6f, -8f), searingWind: new(3.5f, 9.5f));

    private IAiMove SetUpForHomingLasers() =>
        Group(NorthWestStack, new(0f, -7.7f), new(7.3f, -3.7f), searingWind: new(3.5f, 9.5f));

    private static IAiMove Everyone(Vector2 spot) => AiMove.All(spot);

    private static IAiMove Only(PartyRole role, Vector2 spot) => AiMove.Single(role, spot);

    private IAiMove Group(Vector2? group, Vector2? mainTank, Vector2? offTank, Vector2? searingWind = null, Vector2? firstMesohigh = null)
    {
        var spots = new Vector2?[8];
        for (var i = 0; i < spots.Length; i++)
            spots[i] = group;
        spots[(int)PartyRole.MainTank] = mainTank;
        spots[(int)PartyRole.OffTank] = offTank;
        spots[(int)state.SearingWindTarget] = searingWind ?? group;
        spots[(int)state.FirstMesohighTaker] = firstMesohigh ?? group;
        return AiMove.Create(spots).NaturalOrder();
    }
}
