using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace AnoMech.Core.UserActions.Jobs;

// Monk's Beast Chakra / Masterful Blitz gauge — three ordered BeastChakra slots (with their packed
// per-type counts in BeastChakraStacks), the Nadi bitfield, and the blitz-window timer. A scalar
// ResourceGauge can't hold this. The scalar Chakra gauge and every Form / Perfect Balance / Formless
// Fist status stay in the JobActions data table (do not write them here).
//
// Beast Chakra opens ONLY inside the Perfect Balance window — verified from the PB tooltip ("Grants
// Opo-opo/Coeurl/Raptor Chakra depending on the form changed to"); the form GCDs themselves grant
// none. The PB status (110, 3 stacks) is the game's window counter, but JobActionHandler runs before
// us and already decremented it on the current GCD, so it reads 0 on the third build GCD — we track
// the three build-GCDs with our own counter instead of reading the status.
internal sealed unsafe class MonkStateHandler : IUserActionHandler
{
    private int blitzBuildGcdsLeft;

    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action) return;
        if (PlayerJob.Current != JobId.Monk) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;
        var g = &jgm->Monk;

        switch (actionId)
        {
            case 69:   // Perfect Balance opens the free-form build window (3 GCDs / 20s)
                blitzBuildGcdsLeft = 3;
                g->BlitzTimeRemaining = 20000;   // VERIFY in-game: PB window length in ms
                break;

            // Opo-opo form GCDs (Bootshine / Leaping Opo, Dragon Kick, Arm / Shadow of the Destroyer):
            case 53 or 36945 or 74 or 62 or 25767: OpenChakra(g, BeastChakraType.OpoOpo); break;
            // Raptor form GCDs (True Strike / Rising Raptor, Twin Snakes, Four-point Fury):
            case 54 or 36946 or 61 or 16473: OpenChakra(g, BeastChakraType.Raptor); break;
            // Coeurl form GCDs (Snap Punch / Pouncing Coeurl, Demolish, Rockbreaker):
            case 56 or 36947 or 66 or 70: OpenChakra(g, BeastChakraType.Coeurl); break;

            // Masterful Blitz base + its four resolved finishers: open a Nadi and clear the chakra.
            case 25764 or 36948 or 25765 or 25768 or 25769: ReleaseBlitz(g); break;
        }
    }

    public void OnScenarioStart() => blitzBuildGcdsLeft = 0;

    // The window also closes on its own timer; tick it so the gauge countdown stays honest and stops
    // generation if the player is slow. Built chakra is kept (Masterful Blitz has no separate gate).
    // VERIFY in-game: whether unspent Beast Chakra survives the timer expiring.
    public void OnTick(float deltaSeconds)
    {
        if (PlayerJob.Current != JobId.Monk) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;
        var g = &jgm->Monk;
        if (g->BlitzTimeRemaining == 0) return;

        var ms = g->BlitzTimeRemaining - deltaSeconds * 1000f;
        if (ms <= 0f) { g->BlitzTimeRemaining = 0; blitzBuildGcdsLeft = 0; }
        else g->BlitzTimeRemaining = (ushort)ms;
    }

    private void OpenChakra(MonkGauge* g, BeastChakraType type)
    {
        if (blitzBuildGcdsLeft <= 0) return;   // chakra only opens during Perfect Balance
        blitzBuildGcdsLeft--;

        if (g->BeastChakra1 == BeastChakraType.None) g->BeastChakra1 = type;
        else if (g->BeastChakra2 == BeastChakraType.None) g->BeastChakra2 = type;
        else if (g->BeastChakra3 == BeastChakraType.None) g->BeastChakra3 = type;
        else return;
        Repack(g);
    }

    // Nadi opened is set by the three chakra types (Masterful Blitz tooltip): all-same -> Elixir Burst
    // (Lunar); all-distinct -> Rising Phoenix (Solar); two types -> Celestial Revolution (Lunar, or
    // Solar if Lunar is already open). With both Nadi already open it is Phantom Rush, which spends them.
    private void ReleaseBlitz(MonkGauge* g)
    {
        var nadi = g->Nadi;
        if (nadi.HasFlag(NadiFlags.Lunar) && nadi.HasFlag(NadiFlags.Solar))
            g->Nadi = default;   // Phantom Rush consumes both Nadi   // VERIFY in-game
        else
        {
            var distinct = DistinctChakra(g);
            if (distinct <= 1) g->Nadi |= NadiFlags.Lunar;
            else if (distinct >= 3) g->Nadi |= NadiFlags.Solar;
            else g->Nadi |= nadi.HasFlag(NadiFlags.Lunar) ? NadiFlags.Solar : NadiFlags.Lunar;
        }

        g->BeastChakra1 = g->BeastChakra2 = g->BeastChakra3 = BeastChakraType.None;
        g->BeastChakraStacks = 0;
        blitzBuildGcdsLeft = 0;
    }

    private static int DistinctChakra(MonkGauge* g)
    {
        var a = g->BeastChakra1; var b = g->BeastChakra2; var c = g->BeastChakra3;
        var n = 0;
        if (a != BeastChakraType.None) n++;
        if (b != BeastChakraType.None && b != a) n++;
        if (c != BeastChakraType.None && c != a && c != b) n++;
        return n;
    }

    // Keep the packed per-type counts (OpoOpo &3, Raptor (>>2)&3, Coeurl (>>4)&3) in sync with the slots.
    private static void Repack(MonkGauge* g)
    {
        var a = g->BeastChakra1; var b = g->BeastChakra2; var c = g->BeastChakra3;
        int opo = Count(a, b, c, BeastChakraType.OpoOpo);
        int rap = Count(a, b, c, BeastChakraType.Raptor);
        int coe = Count(a, b, c, BeastChakraType.Coeurl);
        g->BeastChakraStacks = (byte)((opo & 3) | ((rap & 3) << 2) | ((coe & 3) << 4));
    }

    private static int Count(BeastChakraType a, BeastChakraType b, BeastChakraType c, BeastChakraType t)
        => (a == t ? 1 : 0) + (b == t ? 1 : 0) + (c == t ? 1 : 0);
}
