using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace AnoMech.Core.UserActions.Jobs;

// Pictomancer's Motif -> Canvas -> Muse pipeline (the CanvasFlags / CreatureFlags bitfields a scalar
// ResourceGauge can't hold). PalleteGauge, Paint, and the Aetherhues / Hammer Time / Starry statuses
// are scalar/status effects the JobActions data table already handles — untouched here.
//
// CanvasFlags = which motif is painted and ready to spend. CreatureFlags = the creature-cycle
// progression (depiction bits Pom -> Wings -> Claw) that drives the Creature Motif button swap and
// readies the Moogle/Madeen portraits. Ids received are already GetAdjustedActionId-resolved, so a
// Creature/Living Muse press arrives as the concrete Pom/Wing/Claw/Maw variant.
internal sealed unsafe class PictomancerStateHandler : IUserActionHandler
{
    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action) return;
        if (PlayerJob.Current != JobId.Pictomancer) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;
        ref var g = ref jgm->Pictomancer;

        switch (actionId)
        {
            // Motif draws paint the matching canvas (Creature/Weapon/Landscape Motif buttons swap to these):
            case 34664: g.CanvasFlags |= CanvasFlags.Pom;       break;   // Pom Motif
            case 34665: g.CanvasFlags |= CanvasFlags.Wing;      break;   // Wing Motif
            case 34666: g.CanvasFlags |= CanvasFlags.Claw;      break;   // Claw Motif
            case 34667: g.CanvasFlags |= CanvasFlags.Maw;       break;   // Maw Motif
            case 34668: g.CanvasFlags |= CanvasFlags.Weapon;    break;   // Hammer Motif (Weapon Motif swap)
            case 34669: g.CanvasFlags |= CanvasFlags.Landscape; break;   // Starry Sky Motif (Landscape Motif swap)

            // Creature muses (Living Muse swaps to these): spend the painted creature, advance the cycle.
            // VERIFY in-game: CreatureFlags accumulates depiction bits Pom(1)->Wings(2)->Claw(4) to pick the
            // next Creature Motif; Winged Muse readies Moogle, Fanged Muse readies Madeen then clears them.
            // Encoding inferred from the action descriptions, not from the CS/sheets.
            case 34670:                                                  // Pom Muse
                g.CanvasFlags &= ~CanvasFlags.Pom;
                g.CreatureFlags |= CreatureFlags.Pom;
                break;
            case 34671:                                                  // Winged Muse
                g.CanvasFlags &= ~CanvasFlags.Wing;
                g.CreatureFlags |= CreatureFlags.Wings;
                if (g.CreatureFlags.HasFlag(CreatureFlags.Pom))
                    g.CreatureFlags |= CreatureFlags.MooglePortait;
                break;
            case 34672:                                                  // Clawed Muse
                g.CanvasFlags &= ~CanvasFlags.Claw;
                g.CreatureFlags |= CreatureFlags.Claw;
                break;
            case 34673:                                                  // Fanged Muse
                g.CanvasFlags &= ~CanvasFlags.Maw;
                if (g.CreatureFlags.HasFlag(CreatureFlags.Pom | CreatureFlags.Wings | CreatureFlags.Claw))
                    g.CreatureFlags |= CreatureFlags.MadeenPortrait;
                g.CreatureFlags &= ~(CreatureFlags.Pom | CreatureFlags.Wings | CreatureFlags.Claw);
                break;

            case 34674: g.CanvasFlags &= ~CanvasFlags.Weapon;    break;   // Striking Muse (Steel Muse swap)
            case 34675: g.CanvasFlags &= ~CanvasFlags.Landscape; break;   // Starry Muse (Scenic Muse swap)

            case 34676: g.CreatureFlags &= ~CreatureFlags.MooglePortait;  break;   // Mog of the Ages
            case 34677: g.CreatureFlags &= ~CreatureFlags.MadeenPortrait; break;   // Retribution of the Madeen
        }
    }
}
