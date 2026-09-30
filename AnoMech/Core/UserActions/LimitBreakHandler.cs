using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;

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

    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action || !IsLimitBreak(actionId)) return;
        Plugin.PlayerInputHooks.SpendLimitBreak();
        DiagnosticLog.Info($"[LimitBreak] {ActionLookup.Name(actionId)} ({actionId}) resolved -- the gauge is spent.");
    }
}
