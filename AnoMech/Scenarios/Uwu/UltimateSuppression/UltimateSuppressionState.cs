using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Uwu.UltimateSuppression;

public class UltimateSuppressionState
{
    public readonly Rng Rng = new();

    public Placement LightPillarPlacement { get; set; } = new();

    public SimCharacter? PlayerLightPillar = null!;
    public SimCharacter?[] PlayerMistralSongs = new SimCharacter?[2];
    public SimCharacter?[] PlayerEruptions = new SimCharacter?[2];
    public SimCharacter? PlayerGaol = null!;
    public SimCharacter? PlayerFlamingCrush = null!;

    public SimTether? MesohighTether = null;

    public const int SuppressionSpots = 6;
    // Which spread spot each non-tank takes, resolved here so a peer's replay can't re-roll it.
    public int[] SuppressionSpotOrder { get; private init; } = [];

    // LightPillarPlacement is resolved mid-run on the host and never read by the Ai, so it stays
    // at its default here.
    public static UltimateSuppressionState? FromNetworkReplay(
        SimParty party, PartyRole lightPillar, IReadOnlyList<PartyRole> mistralSongs,
        IReadOnlyList<PartyRole> eruptions, PartyRole gaol, PartyRole flamingCrush, int[] suppressionSpotOrder)
    {
        if (mistralSongs.Count != 2 || eruptions.Count != 2 || suppressionSpotOrder is not { Length: SuppressionSpots }) return null;
        return new UltimateSuppressionState
        {
            PlayerLightPillar = party.Get(lightPillar),
            PlayerMistralSongs = [party.Get(mistralSongs[0]), party.Get(mistralSongs[1])],
            PlayerEruptions = [party.Get(eruptions[0]), party.Get(eruptions[1])],
            PlayerGaol = party.Get(gaol),
            PlayerFlamingCrush = party.Get(flamingCrush),
            SuppressionSpotOrder = suppressionSpotOrder,
        };
    }

    private UltimateSuppressionState() { }

    public UltimateSuppressionState(SimParty party, UltimateSuppressionStateOverrides overrides)
    {
        RoleList roles;
        var doOverride = !party.PlayerRole.IsTank() && overrides.Assignment != UltimateSuppressionAssignment.Auto;

        if (doOverride)
        {
            roles = RoleList.AllExcept(party, [PartyRole.MainTank, PartyRole.OffTank, party.PlayerRole]);
        }
        else
        {
            roles = RoleList.AllExcept(party, [PartyRole.MainTank, PartyRole.OffTank]);
        }

        var index = 0;

        PlayerLightPillar = (doOverride && overrides.Assignment == UltimateSuppressionAssignment.LightPillar) ? party.Player : roles.Get(index++);
        PlayerMistralSongs[0] = (doOverride && overrides.Assignment == UltimateSuppressionAssignment.MistralSong) ? party.Player : roles.Get(index++);
        PlayerMistralSongs[1] = roles.Get(index++);
        PlayerEruptions[0] = (doOverride && overrides.Assignment == UltimateSuppressionAssignment.Eruption) ? party.Player : roles.Get(index++);
        PlayerEruptions[1] = roles.Get(index++);
        PlayerGaol = (doOverride && overrides.Assignment == UltimateSuppressionAssignment.Gaol) ? party.Player : roles.Get(index++);
        PlayerFlamingCrush = party.Get(Rng.NextDpsRole());
        SuppressionSpotOrder = Rng.Shuffle(Enumerable.Range(0, SuppressionSpots).ToArray()).ToArray();
    }
}
