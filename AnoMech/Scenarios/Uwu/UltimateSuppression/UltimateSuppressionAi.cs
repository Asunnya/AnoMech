using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Uwu.UwuConstants;

namespace AnoMech.Scenarios.Uwu.UltimateSuppression;

public class UltimateSuppressionAi : IScenarioAi<UltimateSuppressionState>
{
    public string Name => "NAUR";

    private static readonly Placement West = new(new(-18.5f, 0, 0), Geometry.LookAtCenterRotation[DirectionEnum.W]);
    private UltimateSuppressionState state = null!;

    public void Run(UltimateSuppressionState state, SimWorld world)
    {
        this.state = state;

        var ai = new AiManager(world);

        ai.Move(5f, SuppressionStart);
        ai.Move(15f, Eruptions1, 1);
        ai.Move(18f, Eruptions2);
        ai.Move(18.5f, Eruptions3);
        ai.Move(21f, Eruptions4);
        ai.Move(23.25f, FeatherRain1);
        ai.Move(25.25f, FeatherRain2);
        ai.Move(26.75f, () => AiMove.Single(((ISimPartyMember)state.PlayerLightPillar!).Role, new(6, -5)));
        ai.Move(29f, () => AiMove.Single(((ISimPartyMember)state.PlayerLightPillar!).Role, new(6.25f, 0)));
        ai.Move(31f, () => AiMove.All(new(6.7f, 0)));
        ai.Move(33f, () => AiMove.Single(PartyRole.MainTank, new(-4, -7)));
        ai.Move(33.25f, Landslide1);
        ai.Move(35.75f, Landslide2);
        ai.Move(42f, () => AiMove.All(new(7, 5)));
    }

    private IAiMove SuppressionStart()
    {
        var fraction = 90 / (UltimateSuppressionState.SuppressionSpots - 1);
        var spots = state.SuppressionSpotOrder
            .Select(x => RotateSuppresionSpot(fraction * x))
            .ToArray();

        return AiMove.Create(
            new(-6, 8),  // MT
            new(-6, 8), // OT
            spots[0], // H1
            spots[1], // H2
            spots[2], // M1
            spots[3], // M2
            spots[4], // R1
            spots[5] // R2
            ).NaturalOrder();
    }

    private IAiMove Eruptions1()
    {
        return AiMove.Create(
            null, null, // Tanks
            new(0, 0), // H1
            new(0, 0), // H2
            new(0, 0), // M1
            new(0, 0), // M2
            new(0, 0), // R1
            new(0, 0) // R2
            ).NaturalOrder();
    }

    private IAiMove Eruptions2()
    {
        return AiMove.Create(
            null, null, // Tanks
            new(2, 2), // Light Pillar
            new(2, 2), // Mistral Song
            new(2, 2), // Mistral Song
            new(2, 2), // Eruption
            new(2, 2), // Eruption
            new(2, 2) // Gaol
            )
            .Assignments([
                PartyRole.MainTank,
                PartyRole.OffTank,
                ((ISimPartyMember)state.PlayerLightPillar!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[0]!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[1]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[0]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[1]!).Role,
                ((ISimPartyMember)state.PlayerGaol!).Role,
            ]);
    }

    private IAiMove Eruptions3()
    {
        return AiMove.Create(
            null, null, // Tanks
            new(-8, 10), // Light Pillar
            new(-8, 10), // Mistral Song
            new(-8, 10), // Mistral Song
            new(8, 8), // Eruption
            new(8, 8), // Eruption
            new(8, 8) // Gaol
            )
            .Assignments([
                PartyRole.MainTank,
                PartyRole.OffTank,
                ((ISimPartyMember)state.PlayerLightPillar!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[0]!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[1]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[0]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[1]!).Role,
                ((ISimPartyMember)state.PlayerGaol!).Role,
            ]);
    }

    private IAiMove Eruptions4()
    {
        return AiMove.Create(
            null, null, null, null, null,
            new(12, -2), new(12, -2), // Eruption
            null
            )
            .Assignments([
                PartyRole.MainTank,
                PartyRole.OffTank,
                ((ISimPartyMember)state.PlayerLightPillar!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[0]!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[1]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[0]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[1]!).Role,
                ((ISimPartyMember)state.PlayerGaol!).Role,
            ]);
    }

    private IAiMove FeatherRain1()
    {
        var gaol = state.PlayerGaol!;
        var gaolPos = new Vector2(gaol.Position.X, gaol.Position.Z);

        return AiMove.Create(
            gaolPos,  // MT
            gaolPos, // OT
            new(-12, -2), // Light Pillar
            gaolPos, // Mistral Song
            gaolPos, // Mistral Song
            new(6, -5), // Eruption
            new(6, -5), // Eruption
            null // Gaol
            )
            .Assignments([
                PartyRole.MainTank,
                PartyRole.OffTank,
                ((ISimPartyMember)state.PlayerLightPillar!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[0]!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[1]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[0]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[1]!).Role,
                ((ISimPartyMember)gaol).Role,
            ]);
    }

    private IAiMove FeatherRain2()
    {
        return AiMove.Create(
            new(6, 4),  // MT
            new(6, 4), // OT
            new(-4, -7), // Light Pillar
            new(6, 4), // Mistral Song
            new(6, 4), // Mistral Song
            new(6, 4), // Eruption
            new(6, 4), // Eruption
            null // Gaol
            )
            .Assignments([
                PartyRole.MainTank,
                PartyRole.OffTank,
                ((ISimPartyMember)state.PlayerLightPillar!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[0]!).Role,
                ((ISimPartyMember)state.PlayerMistralSongs[1]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[0]!).Role,
                ((ISimPartyMember)state.PlayerEruptions[1]!).Role,
                ((ISimPartyMember)state.PlayerGaol!).Role,
            ]);
    }

    private IAiMove Landslide1()
    {
        return AiMove.Create(
            null, // MT
            new(7, 5),// OT
            new(7, 5), // H1
            new(7, 5), // H2
            new(7, 5), // M1
            new(7, 5), // M2
            new(7, 5), // R1
            new(7, 5) // R2
            ).NaturalOrder();
    }

    private IAiMove Landslide2()
    {
        return AiMove.Create(
            null, // MT
            new(6.7f, 0), // OT
            new(6.7f, 0), // H1
            new(6.7f, 0), // H2
            new(6.7f, 0), // M1
            new(6.7f, 0), // M2
            new(6.7f, 0), // R1
            new(6.7f, 0) // R2
            ).NaturalOrder();
    }

    private static Vector2 RotateSuppresionSpot(float rotationDegrees)
    {
        var rot = float.DegreesToRadians(rotationDegrees);
        var placement = West.RotateAroundOrigin(rot);

        return placement.Position2;
    }
}
