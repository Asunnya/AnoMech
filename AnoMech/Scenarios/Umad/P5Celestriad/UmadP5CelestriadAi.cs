using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Umad.P5Celestriad.UmadP5CelestriadConstants;

namespace AnoMech.Scenarios.Umad.P5Celestriad;

// First-pass bots for Celestriad: each doppel already "knows" its own permanent element (or
// free-pair role) and, within a doubled element's active pair, which tower is "theirs" (see
// PlaceSet), a strategy call this AI owns, not something UmadP5CelestriadState hands it. Runs
// to that set's matching tower, splitting the pair 1y either side of the tower's tangent so
// both fit inside the soak radius. On Catastrophic Choice sets (0 and 2), bots first stack in
// the tower's centre, then after ChoiceReadDelay step to the safe half: Aero (green) -> away
// from the boss, Earth (brown) -> toward the boss. This is simulated recognition time, not a
// read of the ground VFX (which only flashes at resolution, see
// UmadP5CelestriadScenario.SpawnChoiceOmen): bots already know their side from state directly.
public sealed class UmadP5CelestriadAi : IScenarioAi<UmadP5CelestriadState>
{
    public string Name => "Standard";

    private const float MoveSpeed = 6f;
    private const float PairOffset = 1f;
    private const float HalfOffset = 2f;
    // Wait until a tower is actually lit before sending anyone toward it. Moving early means a
    // bot is already standing on the spot when it activates, which reads as the tower spawning
    // under a player instead of on open ground.
    private const float MoveDelay = 1.5f;
    // How long after Catastrophic Choice's cast starts bots reposition to their safe half.
    private const float ChoiceReadDelay = 2f;

    public void Run(UmadP5CelestriadState state, SimWorld world)
    {
        for (var set = 0; set < 3; set++)
        {
            var s = set;
            world.Events.Add(CelestriadTiming.TowerStart[s] + MoveDelay, () => PlaceSet(world, state, s, half: 0f));
            if (CelestriadTiming.CcAt[s] is { } cc)
                world.Events.Add(cc + ChoiceReadDelay, () => PlaceSet(world, state, s, HalfFor(state, s)));
        }
    }

    private static float HalfFor(UmadP5CelestriadState state, int set) =>
        state.AeroVariant[set] is { } choice
            ? (choice == CatastrophicChoice.Aero ? -1f : 1f)
            : 0f;

    private static void PlaceSet(SimWorld world, UmadP5CelestriadState state, int set, float half)
    {
        var party = world.Party;
        var freeRoles = state.PlayerDebuffElement.Where(kv => kv.Value is null).Select(kv => kv.Key).ToArray();

        // Grouped by element to recover the doubled-pair ordering State guarantees (ascending
        // sub-index): within a doubled element's 2 active towers, the first always gets that
        // set's debuffed pair for this element and the second always gets the free pair. This
        // pairing is this AI's own strategy choice, not a fact State hands us.
        foreach (var group in state.SetActiveTowers[set]
                     .Select(towerIndex => state.AllTowers[towerIndex])
                     .GroupBy(tower => tower.Element))
        {
            var debuffedRoles = state.PlayerDebuffElement
                .Where(kv => kv.Value is not null && state.ElementForSet(kv.Key, set) == group.Key)
                .Select(kv => kv.Key)
                .ToArray();

            var towers = group.ToArray();
            for (var i = 0; i < towers.Length; i++)
                PlaceAtTower(party, towers[i], i == 0 ? debuffedRoles : freeRoles, half);
        }
    }

    private static void PlaceAtTower(SimParty party, CelestriadTower tower, IReadOnlyList<PartyRole> roles, float half)
    {
        var inward = Vector3.Normalize(-tower.Position);
        var lateral = new Vector3(-inward.Z, 0f, inward.X);

        for (var i = 0; i < roles.Count; i++)
        {
            var bot = party.Get(roles[i]);
            if (bot is null || !bot.IsAlive()) continue;

            var side = i == 0 ? -1f : 1f;
            var dest = tower.Position + lateral * (side * PairOffset) + inward * (half * HalfOffset);
            bot.MoveTo(dest, MoveSpeed);
        }
    }
}
