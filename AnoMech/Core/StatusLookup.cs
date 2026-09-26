using Lumina.Excel;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace AnoMech.Core;

// Resolves a Status-sheet row id to its display name -- mirrors ActionLookup.
internal static class StatusLookup
{
    private static readonly ExcelSheet<LuminaStatus> Sheet =
        Plugin.DataManager.GetExcelSheet<LuminaStatus>();

    public static string Name(ushort statusId)
    {
        if (Sheet.TryGetRow(statusId, out var row))
        {
            var name = row.Name.ExtractText();
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return statusId.ToString();
    }
}
