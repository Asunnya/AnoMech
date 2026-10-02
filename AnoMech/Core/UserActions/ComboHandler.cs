using AnoMech.Core.Native.Implementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using AnoMech.Core.Native.Implementations.Interop;

namespace AnoMech.Core.UserActions;

// Linear combos (the 1-2-3 where using one action lights up the next) are driven by
// the server's ActionEffect carrying a Type-0x1B effect; the sim firewall eats it, so
// we replay that effect and let the client advance its own combo. Combo routes
// (Gnashing Fang etc.) use a separate system and are out of scope. Dedup of
// queued/re-entrant executions is UserActions' job, so OnAction only sees actual,
// once-per-execution calls.
internal sealed unsafe class ComboHandler : IUserActionHandler
{
    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action) return;
        var role = PlayerCombo.RoleOf(actionId);
        if (role == ComboRole.None) return;

        var player = (BattleChara*)(Plugin.ObjectTable.LocalPlayer?.Address ?? 0);
        if (player == null) return;
        var am = ActionManager.Instance();
        if (am == null) return;
        var tgt = (GameObject*)(Plugin.TargetManager.Target?.Address ?? 0);
        var targetId = tgt != null ? tgt->GetGameObjectId() : default;
        var comboFlag = (byte)(role == ComboRole.Continuation ? 1 : 0);
        ActionEffects.FireCombo((Character*)player, actionId, am->LastUsedActionSequence, comboFlag, targetId);
    }
}
