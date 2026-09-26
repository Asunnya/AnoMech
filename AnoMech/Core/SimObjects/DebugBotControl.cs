namespace AnoMech.Core.SimObjects;

// Debug-only: when true, the local SimPlayer's own MoveTo/Intercept calls
// actually move the real local character (bypassing the "player cannot be
// moved like this" no-op PlayerMovement normally enforces) exactly like a
// bot's Movement would. Read by PlayerMovement itself, which is private-
// protected and not reachable from outside Core/SimObjects -- this is the
// seam MultiplayerManager's debug-bot-controlled peer mode flips through.
// A plain static flag is enough since there is ever only one local SimPlayer.
internal static class DebugBotControl
{
    public static bool Enabled { get; set; }
}
