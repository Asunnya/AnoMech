using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaClassJob = Lumina.Excel.Sheets.ClassJob;

namespace AnoMech.Core.UserActions;

// Spends the scenario's faked limit break gauge once a limit break resolves; a cast
// interrupted before the slidecast window never reaches here, so it costs nothing. What the
// limit break does is an ordinary JobActions row.
internal sealed class LimitBreakHandler : IUserActionHandler
{
    private const uint LimitBreakCategory = 9;

    public static bool IsLimitBreak(uint actionId)
        => Plugin.DataManager.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var action)
           && action.ActionCategory.RowId == LimitBreakCategory;

    // level 1-3; 0 when the job has none at that level.
    public static uint ActionId(uint classJob, int level)
    {
        if (!Plugin.DataManager.GetExcelSheet<LuminaClassJob>().TryGetRow(classJob, out var job)) return 0;
        return level switch
        {
            1 => job.LimitBreak1.RowId,
            2 => job.LimitBreak2.RowId,
            3 => job.LimitBreak3.RowId,
            _ => 0,
        };
    }

    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action || !IsLimitBreak(actionId)) return;
        Plugin.GameInstance?.World.Party.LimitBreak.Spend();
        DiagnosticLog.Info($"[LimitBreak] {ActionLookup.Name(actionId)} ({actionId}) resolved -- the gauge is spent.");
    }
}
