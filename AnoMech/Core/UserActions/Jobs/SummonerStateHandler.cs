using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace AnoMech.Core.UserActions.Jobs;

// Summoner's non-scalar gauge: the demi-summon trance (SummonTimer + the Phoenix/Solar-Bahamut
// "primed" bits), the elemental attunement (packed Attunement byte + AttunementTimer), and the
// two Aetherflow-stack bits. The Favor/Trance/Arcanum statuses are granted by the JobActions data
// table already — this only writes the SummonerGauge struct.
//
// Attunement is a packed byte: AttunementType = Attunement & 3, AttunementCount = Attunement >> 2,
// so a write is (count << 2) | type. The AetherFlags summon-state bits (Phoenix / Solar) form a
// 2-bit field (bits 2-3); on trance expiry they clear back to the default Bahamut state.
internal sealed unsafe class SummonerStateHandler : IUserActionHandler
{
    // Attunement type codes (Attunement & 3). Ordered Fire/Earth/Wind = Ifrit/Titan/Garuda,
    // mirroring the summon and cost-type (72/73/74) ordering.
    private const byte Fire = 1;   // VERIFY in-game: Ifrit == type 1
    private const byte Earth = 2;  // VERIFY in-game: Titan == type 2
    private const byte Wind = 3;   // VERIFY in-game: Garuda == type 3

    // Bits 2-3: which demi-summon is active (0 = Bahamut/Dreadwyrm). Cleared when SummonTimer expires.
    private const AetherFlags SummonState = AetherFlags.PhoenixPrimed | AetherFlags.SolarBahamutFirstPrimed;

    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action) return;
        if (PlayerJob.Current != JobId.Summoner) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;
        var g = &jgm->Summoner;

        switch (actionId)
        {
            // Demi-summon trances → 15s SummonTimer + the matching summon-state bits.
            case 7427:  // Summon Bahamut
            case 3581:  // Dreadwyrm Trance (pre-70)
                Trance(g, AetherFlags.None); break;
            case 25831: // Summon Phoenix
                Trance(g, AetherFlags.PhoenixPrimed); break;
            case 36992: // Summon Solar Bahamut
                Trance(g, AetherFlags.SolarBahamutSecondPrimed); break;  // VERIFY in-game: solar sets both bits (value 12)

            // Elemental summons → set attunement (type + count) and refresh its 30s timer.
            // Ifrit grants 2, Titan/Garuda grant 4. Base + level-90 "II" ids both resolve here.
            case 25805: case 25838: Attune(g, Fire, 2); break;   // Summon Ifrit / Ifrit II
            case 25806: case 25839: Attune(g, Earth, 4); break;  // Summon Titan / Titan II
            case 25807: case 25840: Attune(g, Wind, 4); break;   // Summon Garuda / Garuda II

            // Attunement spenders (Gemshine/Precious Brilliance and their aspected forms) → -1 count.
            case 25883: case 25884:                     // Gemshine / Precious Brilliance
            case 25808: case 25809: case 25810:         // Ruby / Topaz / Emerald Ruin (sub-72)
            case 25811: case 25812: case 25813:         // Ruby / Topaz / Emerald Ruin II (sub-72)
            case 25817: case 25818: case 25819:         // Ruby / Topaz / Emerald Ruin III (sub-72)
            case 25814: case 25815: case 25816:         // Ruby / Topaz / Emerald Outburst (sub-72)
            case 25823: case 25824: case 25825:         // Ruby / Topaz / Emerald Rite
            case 25827: case 25828: case 25829:         // Ruby / Topaz / Emerald Disaster
            case 25832: case 25833: case 25834:         // Ruby / Topaz / Emerald Catastrophe
                SpendAttunement(g); break;

            // Aetherflow: Energy Drain / Siphon grant 2 stacks (both bits); Fester/Painflare/Necrotize spend 1.
            // VERIFY in-game: the AetherFlags Aetherflow1/2 bits are SMN's 2 Aetherflow stacks.
            case 16508: case 16510:                     // Energy Drain / Energy Siphon
                g->AetherFlags |= AetherFlags.Aetherflow; break;
            case 181: case 3578: case 36990:            // Fester / Painflare / Necrotize
                SpendAetherflow(g); break;
        }
    }

    public void OnTick(float deltaSeconds)
    {
        if (PlayerJob.Current != JobId.Summoner) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;
        var g = &jgm->Summoner;

        var step = (int)(deltaSeconds * 1000f);
        if (g->SummonTimer > 0)
        {
            var t = g->SummonTimer - step;
            if (t <= 0) { g->SummonTimer = 0; g->AetherFlags &= ~SummonState; }
            else g->SummonTimer = (ushort)t;
        }
        if (g->AttunementTimer > 0)
        {
            var t = g->AttunementTimer - step;
            if (t <= 0) { g->AttunementTimer = 0; g->Attunement = 0; }
            else g->AttunementTimer = (ushort)t;
        }
    }

    private static void Trance(SummonerGauge* g, AetherFlags state)
    {
        g->SummonTimer = 15000;
        g->AetherFlags = (g->AetherFlags & ~SummonState) | state;
    }

    private static void Attune(SummonerGauge* g, byte type, byte count)
    {
        g->Attunement = (byte)((count << 2) | type);
        g->AttunementTimer = 30000;
    }

    private static void SpendAttunement(SummonerGauge* g)
    {
        int count = g->AttunementCount;
        if (count <= 0) return;
        count--;
        if (count == 0) { g->Attunement = 0; g->AttunementTimer = 0; }
        else g->Attunement = (byte)((count << 2) | g->AttunementType);
    }

    private static void SpendAetherflow(SummonerGauge* g)
    {
        if ((g->AetherFlags & AetherFlags.Aetherflow2) != 0) g->AetherFlags &= ~AetherFlags.Aetherflow2;
        else g->AetherFlags &= ~AetherFlags.Aetherflow1;
    }
}
