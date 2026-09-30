using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace AnoMech.Core.UserActions.Jobs;

// Sage's Eukrasia flag on the job gauge. The JobActions table already grants and clears the
// Eukrasia status (2606), but the client swaps Dosis / Diagnosis / Prognosis / Dyskrasia for their
// Eukrasian versions off this gauge byte, so without it the augmented spells never surface.
internal sealed unsafe class SageStateHandler : IUserActionHandler
{
    private const uint Sge = 40;

    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action) return;
        if (Plugin.PlayerState.ClassJob.RowId != Sge) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;

        switch (actionId)
        {
            case 24290:   // Eukrasia
                jgm->Sage.Eukrasia = 1;
                Plugin.Log.Debug("[SageStateHandler] Eukrasia armed");
                break;
            // Every Eukrasian spell spends it:
            case 24291 or 24292 or 24293 or 24308 or 24314 or 37032 or 37034: jgm->Sage.Eukrasia = 0; break;
        }
    }
}
