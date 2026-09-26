using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P2Forsaken.Ai;

// Party-member movement choreography for UMAD P2 Forsaken. Reads the shared
// UmadP2ForsakenState so movement stays in sync with the randomized layout, and
// schedules moves through the AiManager. See TopP5DeltaAi for the canonical shape.
//
// This is NOT a selectable strat (it does not implement IScenarioAi). It is a
// reusable helper composed by strat classes such as UmadP2ForsakenKroxyRinonAi.
// The single plug point is `reorderActive`: after each Odd/EvenTower the active
// 4-role group (alpha or beta) is handed to the strat for an optional in-place
// reorder, so consecutive towers of the same group can rotate assignments. The
// default no-op keeps standalone use behaviour-identical to the old strat.
public sealed class UmadP2ForsakenRinonAiHelper
{
    private readonly Vector2?[] oddCoords;
    private readonly Vector2?[] evenCoords;
    private readonly Action<UmadP2ForsakenRinonAiHelper, int, IList<PartyRole>> reorderActive;

    private UmadP2ForsakenState state = null!;

    private IReadOnlyList<PartyRole> alphaInitial = [];
    private IReadOnlyList<PartyRole> betaInitial = [];
    private List<PartyRole> alpha = [];
    private List<PartyRole> beta = [];

    public UmadP2ForsakenRinonAiHelper(
        Vector2?[] oddCoords,
        Vector2?[] evenCoords,
        Action<UmadP2ForsakenRinonAiHelper, int, IList<PartyRole>> reorderActive)
    {
        this.oddCoords = oddCoords;
        this.evenCoords = evenCoords;
        this.reorderActive = reorderActive;
    }

    public void Run(UmadP2ForsakenState s, SimWorld world)
    {
        state = s;
        var ai = new AiManager(world);
        Init();

        ai.Move(1f, InitialLineup);
        ai.Move(10.16f, TowerPositions(0), jitter: .0f, sprint: true);
        ai.Move(25.17f, TowerPositions(1), jitter: .0f, sprint: true);
        ai.Move(33f, AllThingsEndsBait(0, 2), sprint: true);
        ai.Move(39.21f, TowerPositions(2), jitter: .0f, sprint: true);
        ai.Move(47.22f, TowerPositions(3), jitter: .0f, sprint: true);
        ai.Move(54f, AllThingsEndsBait(1, 4), sprint: true);
        ai.Move(59.26f, TowerPositions(4), jitter: .0f, sprint: true);
        ai.Move(65.27f, TowerPositions(5), jitter: .0f, sprint: true);
        ai.Move(75f, AllThingsEndsBait(2, 6), sprint: true);
        ai.Move(81.31f, TowerPositions(6), jitter: .0f, sprint: true);
        ai.Move(90.32f, TowerPositions(7), jitter: .0f, sprint: true);
        // Occurrence 3 has no upcoming tower to bisect against and nothing moves the party after
        // it, so it gets the real two-step: gather between the last towers, then relocate once the
        // castbar starts. The boss's facing locks at its Face() call, so moving during the cast is
        // what makes Future's End safe; Past's End's second move is a same-spot no-op.
        ai.Move(95.83f, BetweenLastTowers(), sprint: true);
        ai.Move(101.16f, AllThingsEndsBait(3, 7), sprint: true);
    }

    // Future's End sits opposite the upcoming towers, Past's End between them, both on the
    // NewNorthAt(2*i+2) bisector the tower pair straddles. Future's following tower transition
    // is infeasible at any distance, so its spot is pushed out for range as well as angle (the
    // cone tracks party.Player, who is in this stack); Past keeps the tighter margin to help its
    // own tight transition. Both casters sit at the origin.
    private const float PastMeleeFromCenter = 9.7f;
    private const float FutureMeleeFromCenter = 11f;

    private Func<IAiMove> AllThingsEndsBait(int i, int northIndex)
    {
        var distance = state.EndAttacks[i] == EndAttack.PastsEnd ? -PastMeleeFromCenter : FutureMeleeFromCenter;
        return () => AiMove.All(new(0, distance))
                           .ApplyPositions(state.NewNorthAt(northIndex).Apply);
    }

    // Occurrence 3's first leg: both variants start between the towers.
    private Func<IAiMove> BetweenLastTowers()
    {
        return () => AiMove.All(new(0, -PastMeleeFromCenter))
                           .ApplyPositions(state.NewNorthAt(7).Apply);
    }

    private void Init()
    {
        var stacks = state.Lockons.Where(pair => pair.Value == LockonId.ForsakenStack).Select(pair => pair.Key)
                          .ToList();
        var supportPair = (PartyRole)(((int)stacks[0] + 2) % 4);
        var dpsPair = (PartyRole)((((int)stacks[1] - 2) % 4) + 4);
        var list = new List<PartyRole>([stacks[0], stacks[1], supportPair, dpsPair]);
        list.Sort();
        (list[0], list[1]) = (list[1], list[0]); // swap tank and healer
        alpha = list;
        alphaInitial = new List<PartyRole>(alpha);
        list = Enum.GetValues<PartyRole>()
                   .Where(role => !list.Contains(role))
                   .ToList();
        (list[0], list[1]) = (list[1], list[0]); // swap tank and healer
        beta = list;
        betaInitial = new List<PartyRole>(beta);
    }


    public PartyRole ActiveRole(uint mechanic, int towerId, int order)
    {
        var array = towerId is < 3 or 7 ? alpha : beta;
        try
        {
            return array
                   .Where(role => state.Lockons[role] == mechanic)
                   .Skip(order)
                   .First();
        }
        catch (InvalidOperationException)
        {
            DiagnosticLog.Warn($"Lockons {string.Join(",", state.Lockons)}");
            DiagnosticLog.Warn($"Can't find {mechanic}.{order}, for {towerId}, for {string.Join(",", array)}");
            throw;
        }
    }

    private PartyRole PassiveRole(int towerId, int order)
    {
        var array = towerId is < 3 or 7 ? betaInitial : alphaInitial;
        return array[order];
    }

    private IAiMove InitialLineup()
    {
        return AiMove.Create(
            new(-2.5f, -2.5f),
            new(-2.5f, 2.5f),
            new(-7.5f, -2.5f),
            new(-7.5f, 2.5f),
            new(2.5f, -2.5f),
            new(2.5f, 2.5f),
            new(7.5f, -2.5f),
            new(7.5f, 2.5f)
        );
    }

    private Func<IAiMove> TowerPositions(int i)
    {
        return () =>
        {
            var move = i % 2 == 1 ? EvenTower(i) : OddTower(i); // odd/even flipped because 0 indexing teehee
            reorderActive(this, i, i is < 3 or 7 ? alpha : beta); // plug point: same active-group rule as ActiveRole
            return move;
        };
    }

    private IAiMove OddTower(int i)
    {
        return AiMove.Create(oddCoords)
                     .Assignments([
                         ActiveRole(LockonId.ForsakenStack, i, 0),
                         ActiveRole(LockonId.ForsakenCone, i, 0),
                         ActiveRole(LockonId.ForsakenStack, i, 1),
                         ActiveRole(LockonId.ForsakenChariot, i, 0),
                         PassiveRole(i, 0),
                         PassiveRole(i, 1),
                         PassiveRole(i, 2),
                         PassiveRole(i, 3)
                     ])
                     .ApplyPositions(state.NewNorthAt(i).Apply);
    }

    private IAiMove EvenTower(int i)
    {
        return AiMove.Create(evenCoords)
                     .Assignments([
                         ActiveRole(LockonId.ForsakenCone, i, 0),
                         ActiveRole(LockonId.ForsakenChariot, i, 0),
                         ActiveRole(LockonId.ForsakenCone, i, 1),
                         ActiveRole(LockonId.ForsakenChariot, i, 1),
                         PassiveRole(i, 0),
                         PassiveRole(i, 1),
                         PassiveRole(i, 2),
                         PassiveRole(i, 3),
                     ])
                     .ApplyPositions(state.NewNorthAt(i).Apply);
    }

    // --- Coordinate sets ---
    // 16 scenario-local XZ coords per set: 8 for odd-index towers, 8 for even-index
    // towers (active 4 + passive 4 each). Passed into the constructor by the strats.
    // AiMove copies these on Create, so its per-move rotation never mutates the set.

    // Standard layout — the values the helper used to hardcode. Shared by the
    // "South Flex 341" and "Kroxy-Rinon 341" strats.
    public static readonly Vector2?[] StandardOdd =
    [
        // active group
        new(4.8f, -4.8f),  // stack
        new(8f, -8f),      // cone
        new(-5.4f, -2.4f), // stack
        new(-5.4f, -8.5f), // chariot
        // passive group
        new(9.6f, -9.6f),  // outer cone bait
        new(2.5f, -2.5f),  // inner cone bait
        new(-3.5f, -1.9f), // in stack
        new(-3.5f, -1.9f)  // in stack
    ];

    public static readonly Vector2?[] OldEven =
    [
        // active group
        new(3.2f, -3.2f),  // cone1
        new(8, -8),        // chariot1
        new(-3.2f, -3.2f), // cone2
        new(-8, -8),       // chariot2
        // passive group
        new(8.8f, -2.4f),  // cone bait1
        new(3.8f, 4.2f),   // clone bait1
        new(-3.8f, 4.2f),  // clone bait2
        new(-8.8f, -2.4f)  // cone bait2
    ];

    public static readonly Vector2?[] DiamonMarkersEven =
    [
        // active group
        new(4.8f, -2.2f),  // cone1
        new(6.3f, -9.2f),        // chariot1
        new(-4.8f, -2.2f), // cone2
        new(-6.3f, -9.2f),       // chariot2
        // passive group
        new(10.5f, 0f),  // cone bait1
        new(3.5f, 4.9f),   // clone bait1
        new(-3.5f, 4.9f),  // clone bait2
        new(-10.5f, 0f)  // cone bait2
    ];

    // --- Reorder behaviors ---
    // The strats' plug point, shared so the set-1 and set-2 variants stay in sync.
    // Invoked from TowerPositions with the helper instance + the active 4-role group.

    // South Flex: rotate the active group's assignment per tower.
    public static void SouthFlexReorder(UmadP2ForsakenRinonAiHelper helper, int index, IList<PartyRole> active)
    {
        if (index % 2 == 0) // odd tower
        {
            List<PartyRole> reordered =
            [
                helper.ActiveRole(LockonId.ForsakenStack, index, 0),
                helper.ActiveRole(LockonId.ForsakenCone, index, 0),
                helper.ActiveRole(LockonId.ForsakenChariot, index, 0),
                helper.ActiveRole(LockonId.ForsakenStack, index, 1)
            ];
            active.Clear();
            reordered.ForEach(active.Add);
        }
        else // even tower
        {
            List<PartyRole> reordered =
            [
                helper.ActiveRole(LockonId.ForsakenCone, index, 0),
                helper.ActiveRole(LockonId.ForsakenChariot, index, 0),
                helper.ActiveRole(LockonId.ForsakenChariot, index, 1),
                helper.ActiveRole(LockonId.ForsakenCone, index, 1)
            ];
            active.Clear();
            reordered.ForEach(active.Add);
        }
    }

    // Kroxy: no reorder — standalone behaviour-identical to the original helper.
    public static void KroxyReorder(UmadP2ForsakenRinonAiHelper helper, int index, IList<PartyRole> active)
    {
    }
}
