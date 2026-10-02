using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace AnoMech.Core.UserActions;

// Instant-cast enablers for the player's own spells. Cast time is resolved client-side, so for
// Swiftcast/Acceleration granting the status is enough for the client to make the next cast instant.
// Dualcast's cast-time reduction is NOT reproduced (the client ignores a granted Dualcast status, and
// driving the cast directly glitched the UI) — only its buff is modelled.
//
// The server-driven status removal is firewalled, so we consume the enablers. Consumption differs:
//  - Swiftcast / Triplecast — consumed by the next cast-time spell (priority below Dualcast; skipped
//    when Acceleration/Firestarter/Thunderhead already made the cast instant).
//  - Dualcast — consumed by executing ANY action except an ability (oGCD, ActionCategory 4): instant
//    spells, weaponskills, items, Sprint, etc. all waste it. The one exception is a cast-time spell
//    that Acceleration reduces to instant — then Acceleration is consumed and Dualcast is preserved.
//    (Odd, but this is how the game behaves.)
//  - Acceleration / Firestarter / Thunderhead — granted+consumed by the JobActions table.
//
// Dualcast is granted on cast COMPLETION of a genuine RDM hard cast (tracked from OnTick — not on cast
// start, and not if the cast is interrupted). Registered before JobActionHandler so the spell-specific
// statuses are still present when we check them.
internal sealed unsafe class CastTimeHandler : IUserActionHandler
{
    private const ushort SwiftcastStatus = 167, DualcastStatus = 1393, TriplecastStatus = 1211;
    private const ushort Acceleration = 1238, Firestarter = 165, Thunderhead = 3870;
    private const uint SwiftcastAction = 7561, TriplecastAction = 7421, Foul = 7422;

    // A genuine RDM hard cast whose completion should grant Dualcast (0 = none tracked).
    private uint pendingCastAction;
    private float pendingCastTotal;
    private float pendingCastMax;

    public void OnAction(ActionType actionType, uint actionId)
    {
        var player = Plugin.GameInstance?.Player;
        if (player == null) return;

        // Grant Swiftcast / Triplecast (both abilities → they don't consume Dualcast, so return).
        if (actionType == ActionType.Action && actionId == SwiftcastAction) { player.RemoveStatus(SwiftcastStatus); player.AddStatusParam(SwiftcastStatus, 0, 10f); return; }
        if (actionType == ActionType.Action && actionId == TriplecastAction) { player.RemoveStatus(TriplecastStatus); player.AddStatusParam(TriplecastStatus, 3, 15f); return; }

        // Classify the executed action. Anything that isn't a category-4 ability (spell, weaponskill,
        // item, Sprint, …) consumes Dualcast.
        bool isAbility = false, isCastTimeSpell = false;
        if (actionType == ActionType.Action)
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (sheet.TryGetRow(actionId, out var row))
            {
                var cat = row.ActionCategory.RowId;
                isAbility = cat == 4;
                isCastTimeSpell = cat == 2 && row.Cast100ms > 0 && actionId != Foul;
            }
        }
        bool accelReduced = isCastTimeSpell && player.HasStatus(Acceleration) && actionId is 25855 or 25856 or 16526;

        // Dualcast: any non-ability action consumes it, except an Acceleration-reduced cast.
        bool dualcastConsumed = false;
        if (player.HasStatus(DualcastStatus) && !isAbility && !accelReduced)
        {
            player.RemoveStatus(DualcastStatus);
            dualcastConsumed = true;
        }

        // Swiftcast / Triplecast: the cast-time spell they make instant consumes one — but Dualcast wins
        // the priority, and a spell-specific status (Acceleration/Firestarter/Thunderhead) already took it.
        if (isCastTimeSpell && !dualcastConsumed && !SpellSpecific(player, actionId))
        {
            if (player.HasStatus(TriplecastStatus)) player.AddStatus(TriplecastStatus, 0f, -1);
            else if (player.HasStatus(SwiftcastStatus)) player.RemoveStatus(SwiftcastStatus);
        }

        // Grant Dualcast when a genuine RDM hard cast completes (OnTick). Skip if Dualcast was just
        // consumed on this cast, or if the cast was made instant (no cast bar).
        if (isCastTimeSpell && !dualcastConsumed && PlayerJob.Current == JobId.RedMage)
        {
            var bc = (BattleChara*)(Plugin.ObjectTable.LocalPlayer?.Address ?? 0);
            if (bc != null && bc->CastInfo.IsCasting && bc->CastInfo.TotalCastTime > 0.1f)
            {
                pendingCastAction = actionId;
                pendingCastTotal = bc->CastInfo.TotalCastTime;
                pendingCastMax = bc->CastInfo.CurrentCastTime;
            }
        }
    }

    public void OnTick(float deltaSeconds)
    {
        if (pendingCastAction == 0) return;
        var bc = (BattleChara*)(Plugin.ObjectTable.LocalPlayer?.Address ?? 0);
        if (bc == null) { pendingCastAction = 0; return; }

        // Still casting the tracked spell → follow its progress.
        if (bc->CastInfo.IsCasting && bc->CastInfo.ActionId == pendingCastAction)
        {
            if (bc->CastInfo.CurrentCastTime > pendingCastMax) pendingCastMax = bc->CastInfo.CurrentCastTime;
            return;
        }

        // The tracked cast ended: grant Dualcast only if it reached completion (an interrupt stops well
        // short — the cast can't be cancelled inside the slidecast window).
        if (pendingCastMax >= pendingCastTotal - 0.4f)
        {
            var player = Plugin.GameInstance?.Player;
            player?.RemoveStatus(DualcastStatus);
            player?.AddStatusParam(DualcastStatus, 0, 15f);
        }
        pendingCastAction = 0;
        pendingCastMax = 0;
    }

    // A spell-specific instant status the player holds that applies to this spell (Acceleration →
    // Verthunder III/Veraero III/Impact; Firestarter → Fire III; Thunderhead → the Thunder line).
    private static bool SpellSpecific(SimPlayer p, uint actionId)
    {
        if (p.HasStatus(Acceleration) && actionId is 25855 or 25856 or 16526) return true;
        if (p.HasStatus(Firestarter) && actionId == 152) return true;
        if (p.HasStatus(Thunderhead) && actionId is 144 or 7447 or 153 or 7420 or 36986 or 36987) return true;
        return false;
    }
}
