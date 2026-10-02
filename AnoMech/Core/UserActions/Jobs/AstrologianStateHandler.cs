using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace AnoMech.Core.UserActions.Jobs;

// Astrologian's Astrosign deck — the packed AstrologianGauge.Cards short (three 4-bit play slots at
// bits 0-3/4-7/8-11 + the Minor-Arcana slot at bits 12-15, each nibble an AstrologianCard value) plus
// the CurrentDraw enum. A scalar ResourceGauge can't hold this, so the deck lives here; Divination,
// Divining, Lightspeed, and Oracle's Divining-consume stay in the JobActions data table.
//
// Astral/Umbral Draw deal a fixed 3-card + arcana set and flip which draw button is shown next; the
// Play I/II/III and Minor Arcana buttons swap (GetAdjustedActionId) to the held card's own action id,
// so pressing them fires the resolved card id — we key on both the base and swapped ids and clear the
// matching slot.
internal sealed unsafe class AstrologianStateHandler : IUserActionHandler
{
    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action) return;
        if (PlayerJob.Current != JobId.Astrologian) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;
        ref var g = ref jgm->Astrologian;

        switch (actionId)
        {
            // Draws fill all four slots from the fixed DT sets and flip to the other draw as "next".
            // VERIFY in-game: CurrentDraw is taken to mean the draw shown NEXT (Astral Draw -> Umbral).
            case 37017: // Astral Draw
                g.Cards = Pack(AstrologianCard.Balance, AstrologianCard.Arrow, AstrologianCard.Spire, AstrologianCard.Lord);
                g.CurrentDraw = AstrologianDraw.Umbral;
                break;
            case 37018: // Umbral Draw
                g.Cards = Pack(AstrologianCard.Spear, AstrologianCard.Bole, AstrologianCard.Ewer, AstrologianCard.Lady);
                g.CurrentDraw = AstrologianDraw.Astral;
                break;

            // Play I — slot 0. Base id + swapped The Balance / The Spear.
            case 37019 or 37023 or 37026: g.Cards = ClearSlot(g.Cards, 0); break;
            // Play II — slot 1. Base id + swapped The Arrow / The Bole.
            case 37020 or 37024 or 37027: g.Cards = ClearSlot(g.Cards, 1); break;
            // Play III — slot 2. Base id + swapped The Spire / The Ewer.
            case 37021 or 37025 or 37028: g.Cards = ClearSlot(g.Cards, 2); break;
            // Minor Arcana — arcana slot. Base id + swapped Lord / Lady of Crowns.
            case 37022 or 7444 or 7445: g.Cards = ClearSlot(g.Cards, 3); break;
        }
    }

    private static short Pack(AstrologianCard slot0, AstrologianCard slot1, AstrologianCard slot2, AstrologianCard arcana)
        => unchecked((short)((int)slot0 | ((int)slot1 << 4) | ((int)slot2 << 8) | ((int)arcana << 12)));

    private static short ClearSlot(short cards, int slot) => (short)(cards & ~(0xF << (4 * slot)));
}
