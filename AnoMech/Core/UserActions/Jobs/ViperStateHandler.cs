using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace AnoMech.Core.UserActions.Jobs;

// Viper's two oGCD-combo state machines — the non-scalar part of the gauge a ResourceGauge can't
// hold. SerpentCombo drives the Serpent's Tail button (Death Rattle / Last Lash / the four Legacy
// finishers); DreadCombo drives the Twinfang/Twinblood swaps. RattlingCoil, AnguineTribute,
// SerpentOffering, the venom statuses, and the melee GCD combo stay in the JobActions data table.
internal sealed unsafe class ViperStateHandler : IUserActionHandler
{
    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action) return;
        if (PlayerJob.Current != JobId.Viper) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;

        switch (actionId)
        {
            // Single-target melee finishers arm Death Rattle:
            case 34610 or 34611 or 34612 or 34613: SetSerpent(jgm, SerpentCombo.DeathRattle); break;
            // AoE finishers arm Last Lash:
            case 34618 or 34619: SetSerpent(jgm, SerpentCombo.LastLash); break;
            // Reawaken Generation weaponskills arm the matching Legacy:
            case 34627: SetSerpent(jgm, SerpentCombo.FirstLegacy); break;
            case 34628: SetSerpent(jgm, SerpentCombo.SecondLegacy); break;
            case 34629: SetSerpent(jgm, SerpentCombo.ThirdLegacy); break;
            case 34630: SetSerpent(jgm, SerpentCombo.FourthLegacy); break;
            // Serpent's Tail follow-ups consume the armed value:
            case 34634 or 34635 or 34640 or 34641 or 34642 or 34643: jgm->Viper.SerpentComboState = 0; break;

            // Vicewinder/Vicepit open the dread combo; the Coil/Den branches advance it:
            case 34620: jgm->Viper.DreadCombo = DreadCombo.Dreadwinder; break;   // Vicewinder
            case 34623: jgm->Viper.DreadCombo = DreadCombo.PitOfDread; break;    // Vicepit
            case 34621: jgm->Viper.DreadCombo = DreadCombo.HuntersCoil; break;   // Hunter's Coil
            case 34622: jgm->Viper.DreadCombo = DreadCombo.SwiftskinsCoil; break; // Swiftskin's Coil
            case 34624: jgm->Viper.DreadCombo = DreadCombo.HuntersDen; break;    // Hunter's Den
            case 34625: jgm->Viper.DreadCombo = DreadCombo.SwiftskinsDen; break; // Swiftskin's Den
            // Twinfang/Twinblood follow-ups (incl. Uncoiled) consume the dread combo:
            case 34636 or 34637 or 34638 or 34639 or 34644 or 34645: jgm->Viper.DreadCombo = 0; break;
        }
    }

    // CS exposes SerpentCombo => (SerpentCombo)(SerpentComboState >> 2), so the stored byte is the
    // enum shifted left by 2.
    // VERIFY in-game: exact SerpentComboState bit layout (low 2 bits assumed unused).
    private static void SetSerpent(JobGaugeManager* jgm, SerpentCombo combo)
        => jgm->Viper.SerpentComboState = (byte)((int)combo << 2);
}
