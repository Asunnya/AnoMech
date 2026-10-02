using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Core;

// Resolves a Status-sheet row id to its display name -- mirrors ActionLookup.
internal static class StatusLookup
{
    public static string Name(ushort statusId) => Natives.Data.StatusName(statusId) ?? statusId.ToString();
}
